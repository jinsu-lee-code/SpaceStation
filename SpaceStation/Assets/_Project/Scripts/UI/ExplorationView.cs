using System.Collections.Generic;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.UI
{
    /// <summary>
    /// 13-5a 탐사 지점 표시 (REDESIGN V5: 지도 화면 대신 정거장 시점의 방향 마커).
    /// - 밝히기 전 (사용자 결정 2026-10-18): 홀로그램 신호만 — 퍼져 나가는 파동 고리 + 빛기둥 + 라벨(힌트 · 거리). 무엇인지는 안 보임.
    /// - 밝히면 실제 모습이 커지며 나타남 (사용자 결정 2026-10-18: 난파선만 Blender 모델, 나머지는 코드 조합):
    ///   소행성 = 큰 저폴리 바위 + 면을 따라 그린 주황 광맥(OreVeinMesh) + 작은 위성 바위 / 얼음 혼석 = 푸른 바위 + 빛나는 결정 /
    ///   난파선 = 두 동강 난 옛 수송선(SM_Wreck) + 떨어져 나온 선체 조각 + 깜박이는 붉은 신호등 / 파편 지대 = 천천히 도는 바위 · 금속 조각.
    ///   '2칸 이내 도달 시 탐사 시작' 범위는 모서리 선 (밝힌 뒤에만). 탐사 중이면 초록.
    /// 1인칭 내부에서는 숨김. 내용은 코드로 만든다.
    /// </summary>
    public sealed class ExplorationView : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private const float EdgeWidth = 0.06f;
        private const float RevealSeconds = 0.9f;
        /// <summary>난파선 모델 길이 (월드, 두 동강 + 틈 포함 — 범위 상자 5칸 안)</summary>
        private const float WreckLength = 4f;

        [SerializeField] private StationController _station;
        [Tooltip("반투명 재질 (M_DefenseRange) — 신호 고리 · 빛기둥 · 범위 선")]
        [SerializeField] private Material _material;
        [SerializeField] private TMP_FontAsset _font;
        [Tooltip("라벨을 올릴 화면 (HUD)")]
        [SerializeField] private RectTransform _labelRoot;
        [SerializeField] private float _beamHeight = 7f;
        [Header("지점 모습")]
        [SerializeField] private Material _rockMaterial;
        [SerializeField] private Material _iceMaterial;
        [SerializeField] private Material _hullMaterial;
        [Tooltip("발광 재질 (_EMISSION 켬) — 금속 맥 · 결정 · 신호등")]
        [SerializeField] private Material _glowMaterial;
        [Tooltip("난파선 모델 (SM_Wreck.fbx, BlenderWork/Explore/wreck_builder.py) — 재질 칸 이름이 Beacon인 칸을 깜박임")]
        [SerializeField] private GameObject _wreckModel;

        private ExplorationSystem _explore;
        private Interior.InteriorMode _interior;
        private Camera _camera;
        private MaterialPropertyBlock _block;
        private RectTransform _labels;
        private Mesh _ring;
        private readonly List<Marker> _markers = new List<Marker>();
        private float _nextText;
        private bool _initial;

        private sealed class Spin
        {
            public Transform T;
            public Vector3 Axis;
            public float Speed;
            public Vector3 Orbit; // 0이면 제자리
            public float OrbitSpeed;
        }

        private sealed class Marker
        {
            public ExploreSite Site;
            public GameObject Root;
            public readonly List<MeshRenderer> Edges = new List<MeshRenderer>(12);
            public readonly List<MeshRenderer> Rings = new List<MeshRenderer>(2);
            public MeshRenderer Beam;
            public GameObject Body;
            public SiteKind BodyKind;
            public readonly List<Spin> Spins = new List<Spin>();
            public Renderer Beacon;
            /// <summary>신호등 재질 칸 (모델의 한 칸만 깜박임, -1 = 렌더러 전체)</summary>
            public int BeaconIndex = -1;
            public float RevealedAt = -1f;
            public TMP_Text Label;
            /// <summary>모습을 만들며 새로 만든 메시 (모습을 지울 때 함께 지움)</summary>
            public readonly List<Mesh> Meshes = new List<Mesh>();
        }

        private static Mesh Own(Marker m, Mesh mesh)
        {
            m.Meshes.Add(mesh);
            return mesh;
        }

        private void Start()
        {
            var sim = _station != null ? _station.Simulation : null;
            _explore = sim?.GetSystem<ExplorationSystem>();
            if (_explore == null || _material == null)
            {
                enabled = false;
                return;
            }
            _block = new MaterialPropertyBlock();
            _camera = Camera.main;
            _ring = RingMesh(48, 0.88f, 1f);
            // 라벨 층은 HUD 맨 아래 (창 · 카드가 라벨을 덮게)
            _labels = new GameObject("ExploreLabels", typeof(RectTransform)).GetComponent<RectTransform>();
            _labels.SetParent(_labelRoot != null ? _labelRoot : transform, false);
            _labels.anchorMin = Vector2.zero;
            _labels.anchorMax = Vector2.one;
            _labels.offsetMin = _labels.offsetMax = Vector2.zero;
            _labels.SetAsFirstSibling();
            _interior = FindFirstObjectByType<Interior.InteriorMode>(FindObjectsInactive.Include);
            _explore.Changed += Rebuild;
            _initial = true;
            Rebuild();
            _initial = false;
        }

        private void OnDestroy()
        {
            if (_explore != null)
                _explore.Changed -= Rebuild;
            if (_ring != null)
                Destroy(_ring);
            foreach (var m in _markers)
                foreach (var mesh in m.Meshes)
                    Destroy(mesh);
        }

        // ---------------- 만들기 ----------------

        private void Rebuild()
        {
            var sites = _explore.Sites;
            while (_markers.Count < sites.Count)
                _markers.Add(Create());
            for (int i = 0; i < _markers.Count; i++)
            {
                var m = _markers[i];
                bool show = i < sites.Count;
                m.Root.SetActive(show);
                m.Label.gameObject.SetActive(show);
                if (!show)
                {
                    m.Site = null;
                    continue;
                }
                if (m.Site != sites[i])
                {
                    m.Site = sites[i];
                    ClearBody(m);
                }
                m.Root.transform.position = GridConfig.CellToWorld(m.Site.Cell);
                float half = (_explore.Config.ReachRadius + 0.5f) * GridConfig.CellSize;
                for (int e = 0; e < 12; e++)
                {
                    int axis = e / 4; // 0 = x 방향 선, 1 = y, 2 = z
                    float u = (e & 1) == 0 ? -half : half, v = (e & 2) == 0 ? -half : half;
                    m.Edges[e].transform.localPosition = axis == 0 ? new Vector3(0f, u, v) : axis == 1 ? new Vector3(u, 0f, v) : new Vector3(u, v, 0f);
                    m.Edges[e].transform.localScale = axis == 0 ? new Vector3(half * 2f, EdgeWidth, EdgeWidth)
                        : axis == 1 ? new Vector3(EdgeWidth, half * 2f, EdgeWidth) : new Vector3(EdgeWidth, EdgeWidth, half * 2f);
                }
                m.Beam.transform.localPosition = Vector3.up * (_beamHeight * 0.5f);
                m.Beam.transform.localScale = new Vector3(0.12f, _beamHeight, 0.12f);
                if (m.Site.Revealed && m.Body == null)
                    BuildBody(m, animate: !_initial); // 불러온 판은 연출 없이
            }
            _nextText = 0f;
        }

        private MeshRenderer Part(Transform parent, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        private Marker Create()
        {
            var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var root = new GameObject("ExploreSite");
            root.transform.SetParent(transform, false);
            var marker = new Marker { Root = root, Beam = Part(root.transform, "Beam", cube, _material) };
            for (int e = 0; e < 12; e++)
                marker.Edges.Add(Part(root.transform, "Edge", cube, _material));
            for (int i = 0; i < 2; i++)
                marker.Rings.Add(Part(root.transform, "Ring", _ring, _material));
            var label = new GameObject("ExploreLabel", typeof(RectTransform)).GetComponent<RectTransform>();
            label.SetParent(_labels, false);
            label.anchorMin = label.anchorMax = new Vector2(0f, 0f);
            label.pivot = new Vector2(0.5f, 0f);
            label.sizeDelta = new Vector2(320f, 60f);
            var text = label.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = _font;
            text.fontSize = 17f;
            text.alignment = TextAlignmentOptions.Bottom;
            text.raycastTarget = false;
            text.enableWordWrapping = true;
            marker.Label = text;
            return marker;
        }

        private void ClearBody(Marker m)
        {
            if (m.Body != null)
                Destroy(m.Body);
            m.Body = null;
            foreach (var mesh in m.Meshes)
                Destroy(mesh);
            m.Meshes.Clear();
            m.Spins.Clear();
            m.Beacon = null;
            m.BeaconIndex = -1;
            m.RevealedAt = -1f;
        }

        /// <summary>밝힌 지점의 실제 모습 (지점 칸 · 종류로 정해지는 모양 — 다시 불러와도 같음).</summary>
        private void BuildBody(Marker m, bool animate)
        {
            ClearBody(m);
            var s = m.Site;
            int seed = s.Cell.x * 73856093 ^ s.Cell.y * 19349663 ^ s.Cell.z * 83492791;
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            Vector3 OnSphere() => Random3(rng).normalized;
            Quaternion Rot() => Quaternion.Euler(R() * 360f, R() * 360f, R() * 360f);
            var body = new GameObject("Body_" + s.Kind).transform;
            body.SetParent(m.Root.transform, false);
            m.Body = body.gameObject;
            m.BodyKind = s.Kind;
            m.RevealedAt = animate ? Time.unscaledTime : -1f;
            var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");

            switch (s.Kind)
            {
                case SiteKind.Asteroid:
                case SiteKind.IceRock:
                {
                    bool ice = s.Kind == SiteKind.IceRock;
                    var rockMesh = Own(m, RockMesh.Create(seed, ice ? 0.22f : 0.34f, 2));
                    var rock = Part(body, "Rock", rockMesh, ice ? _iceMaterial : _rockMaterial);
                    float size = ice ? 2.2f : 2.6f;
                    rock.transform.localScale = new Vector3(size, size * (0.75f + R() * 0.2f), size * (0.85f + R() * 0.2f));
                    rock.transform.localRotation = Rot();
                    m.Spins.Add(new Spin { T = rock.transform, Axis = OnSphere(), Speed = 6f });
                    if (ice)
                    {
                        var rv = rockMesh.vertices;
                        var rt = rockMesh.triangles;
                        for (int i = 0; i < 14; i++) // 면에서 비스듬히 솟은 결정 (면 중심 · 면 법선 기준)
                        {
                            int f = rng.Next(rt.Length / 3) * 3;
                            Vector3 a = rv[rt[f]], b = rv[rt[f + 1]], c = rv[rt[f + 2]];
                            var n = Vector3.Cross(b - a, c - a).normalized;
                            var center = (a + b + c) / 3f;
                            float len = 0.14f + R() * 0.16f;
                            var up = Quaternion.AngleAxis(R() * 30f - 15f, Vector3.Cross(n, Vector3.up).normalized) * n;
                            var p = Part(rock.transform, "Crystal", cube, _glowMaterial);
                            p.transform.localPosition = center + up * len * 0.4f;
                            p.transform.localRotation = Quaternion.LookRotation(up) * Quaternion.Euler(0f, 0f, R() * 90f);
                            p.transform.localScale = new Vector3(0.045f, 0.045f, len);
                            Glow(p, new Color(0.55f, 0.9f, 1f), 1.6f);
                        }
                    }
                    else // 금속 광맥: 굵은 주맥(곁가지 · 광석 조각) + 옅은 잔맥, 바위 면을 따라 그린 띠
                    {
                        var main = Own(m, OreVeinMesh.Create(rockMesh, rng, new OreVeinMesh.Style
                            { Veins = 4, MinLength = 6, MaxLength = 10, Width = 0.016f, Branch = 0.35f, Patches = 6, Lift = 0.0018f }));
                        var fine = Own(m, OreVeinMesh.Create(rockMesh, rng, new OreVeinMesh.Style
                            { Veins = 9, MinLength = 2, MaxLength = 5, Width = 0.0065f, Branch = 0f, Patches = 0, Lift = 0.0009f }));
                        Glow(Part(rock.transform, "Veins", main, _glowMaterial), new Color(1f, 0.55f, 0.15f), 2.6f);
                        Glow(Part(rock.transform, "FineVeins", fine, _glowMaterial), new Color(0.85f, 0.4f, 0.1f), 0.9f);
                    }
                    for (int i = 0; i < 3; i++) // 작은 위성 바위
                    {
                        var moon = Part(body, "Moon", Own(m, RockMesh.Create(seed + i + 1, 0.35f, 1)), ice ? _iceMaterial : _rockMaterial);
                        float sc = 0.3f + R() * 0.35f;
                        moon.transform.localScale = Vector3.one * sc;
                        moon.transform.localPosition = OnSphere() * (1.9f + R() * 0.6f);
                        m.Spins.Add(new Spin { T = moon.transform, Axis = OnSphere(), Speed = 30f, Orbit = Vector3.up, OrbitSpeed = 8f + R() * 6f });
                    }
                    break;
                }
                case SiteKind.Wreck:
                {
                    var hull = new GameObject("Hull").transform;
                    hull.SetParent(body, false);
                    hull.localRotation = Quaternion.Euler(10f + R() * 12f, R() * 360f, 14f + R() * 16f); // 기운 채 떠 있음
                    m.Spins.Add(new Spin { T = hull, Axis = new Vector3(0.2f, 1f, 0.1f).normalized, Speed = 2.5f });
                    Material plateHull = _hullMaterial, plateBurn = _hullMaterial;
                    if (_wreckModel != null)
                    {
                        var ship = Instantiate(_wreckModel, hull, false);
                        ship.name = "Wreck";
                        var r = ship.GetComponentInChildren<MeshRenderer>();
                        var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                        var b = mesh.bounds;
                        float scale = WreckLength / Mathf.Max(0.01f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
                        ship.transform.localScale = Vector3.one * scale;
                        ship.transform.localPosition = -(ship.transform.localRotation * b.center) * scale;
                        r.shadowCastingMode = ShadowCastingMode.Off;
                        var mats = r.sharedMaterials;
                        for (int i = 0; i < mats.Length; i++)
                        {
                            string n = mats[i] != null ? mats[i].name : "";
                            if (n.EndsWith("Beacon")) // 규칙적인 인공 신호 (그 재질 칸만 깜박임)
                            {
                                m.Beacon = r;
                                m.BeaconIndex = i;
                            }
                            else if (n.EndsWith("WreckHull")) plateHull = mats[i];
                            else if (n.EndsWith("BurnM")) plateBurn = mats[i];
                        }
                    }
                    else
                    {
                        var beacon = Part(hull, "Beacon", cube, _glowMaterial);
                        beacon.transform.localScale = Vector3.one * 0.14f;
                        m.Beacon = beacon;
                    }
                    for (int i = 0; i < 5; i++) // 떨어져 나온 선체 조각
                    {
                        var plate = Part(body, "Plate", cube, i % 2 == 0 ? plateHull : plateBurn);
                        plate.transform.localScale = new Vector3(0.3f + R() * 0.35f, 0.04f, 0.2f + R() * 0.25f);
                        plate.transform.localPosition = OnSphere() * (2.3f + R() * 0.6f);
                        plate.transform.localRotation = Rot();
                        m.Spins.Add(new Spin { T = plate.transform, Axis = OnSphere(), Speed = 12f + R() * 18f, Orbit = Vector3.up, OrbitSpeed = 3f + R() * 3f });
                    }
                    break;
                }
                default: // 파편 지대
                {
                    for (int i = 0; i < 22; i++)
                    {
                        bool metal = i % 3 == 0;
                        var chunk = metal ? Part(body, "Shard", cube, _hullMaterial) : Part(body, "Chunk", Own(m, RockMesh.Create(seed + i, 0.4f, 1)), _rockMaterial);
                        chunk.transform.localScale = metal ? new Vector3(0.35f + R() * 0.3f, 0.06f, 0.12f + R() * 0.15f) : Vector3.one * (0.18f + R() * 0.35f);
                        var pos = Random3(rng);
                        pos.y *= 0.55f; // 납작한 띠
                        chunk.transform.localPosition = pos * 2.3f;
                        chunk.transform.localRotation = Rot();
                        m.Spins.Add(new Spin { T = chunk.transform, Axis = OnSphere(), Speed = 20f + R() * 50f, Orbit = Vector3.up, OrbitSpeed = 4f + R() * 8f });
                    }
                    break;
                }
            }
            if (animate)
                body.localScale = Vector3.one * 0.01f;
        }

        private static Vector3 Random3(System.Random rng)
        {
            for (int i = 0; i < 8; i++)
            {
                var v = new Vector3((float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f);
                if (v.sqrMagnitude > 0.05f && v.sqrMagnitude <= 1f)
                    return v;
            }
            return Vector3.up;
        }

        private void Glow(Renderer r, Color c, float intensity, int materialIndex = -1)
        {
            _block.Clear();
            _block.SetColor(BaseColorId, c);
            _block.SetColor(EmissionId, c * intensity);
            if (materialIndex >= 0)
                r.SetPropertyBlock(_block, materialIndex);
            else
                r.SetPropertyBlock(_block);
        }

        /// <summary>
        /// 바닥에 눕힌 납작한 고리 (위 · 아래 양면). 면마다 꼭짓점을 따로 둔다 — 함께 쓰면 법선이 0이 되어
        /// 조명 값이 깨지고 블룸이 화면을 하얗게 덮었음.
        /// </summary>
        private static Mesh RingMesh(int segments, float inner, float outer)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();
            for (int side = 0; side < 2; side++)
            {
                int start = verts.Count;
                var n = side == 0 ? Vector3.up : Vector3.down;
                for (int i = 0; i <= segments; i++)
                {
                    float a = i * Mathf.PI * 2f / segments;
                    var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    verts.Add(d * inner);
                    verts.Add(d * outer);
                    normals.Add(n);
                    normals.Add(n);
                }
                for (int i = 0; i < segments; i++)
                {
                    int k = start + i * 2;
                    if (side == 0)
                        tris.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 }); // 위 (위에서 보면 시계 방향)
                    else
                        tris.AddRange(new[] { k, k + 1, k + 2, k + 1, k + 3, k + 2 }); // 아래
                }
            }
            var mesh = new Mesh { name = "ExploreRing" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---------------- 매 프레임 ----------------

        private static Color KindColor(SiteKind kind)
        {
            switch (kind)
            {
                case SiteKind.Asteroid: return new Color(1f, 0.62f, 0.25f);
                case SiteKind.IceRock: return new Color(0.55f, 0.85f, 1f);
                case SiteKind.Wreck: return new Color(0.75f, 0.55f, 1f);
                default: return new Color(1f, 0.3f, 0.25f);
            }
        }

        private void LateUpdate()
        {
            if (_explore == null)
                return;
            bool inside = _interior != null && _interior.IsInside;
            if (_camera == null)
                _camera = Camera.main;
            bool refreshText = Time.unscaledTime >= _nextText;
            if (refreshText)
                _nextText = Time.unscaledTime + 0.25f;
            float t = Time.unscaledTime;
            float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(t * 2.4f));
            foreach (var m in _markers)
            {
                if (m.Site == null)
                    continue;
                if (m.Root.activeSelf == inside)
                    m.Root.SetActive(!inside);
                var s = m.Site;
                if (s.Revealed && m.Body == null)
                    BuildBody(m, animate: true);

                Color c;
                float a;
                if (s.Reached) { c = new Color(0.45f, 1f, 0.6f); a = 0.45f; }
                else if (s.Revealed) { c = KindColor(s.Kind); a = 0.8f; }
                else if (_explore.ProbeTarget == s) { c = new Color(1f, 0.85f, 0.3f); a = pulse; }
                else { c = new Color(0.5f, 0.9f, 1f); a = pulse * 0.8f; }
                foreach (var edge in m.Edges)
                {
                    if (edge.gameObject.activeSelf != s.Revealed)
                        edge.gameObject.SetActive(s.Revealed); // 범위 선은 밝힌 뒤에만
                    if (s.Revealed)
                        Tint(edge, c, a * 0.6f);
                }
                Tint(m.Beam, c, (s.Revealed ? 0.35f : 0.55f) * a);
                // 신호 파동: 고리 두 개가 번갈아 퍼지며 옅어짐 (밝히면 꺼짐)
                for (int i = 0; i < m.Rings.Count; i++)
                {
                    var ring = m.Rings[i];
                    bool on = !s.Revealed;
                    if (ring.gameObject.activeSelf != on)
                        ring.gameObject.SetActive(on);
                    if (!on)
                        continue;
                    float k = Mathf.Repeat(t * 0.55f + i * 0.5f, 1f);
                    ring.transform.localScale = Vector3.one * Mathf.Lerp(0.4f, 2.8f, k);
                    ring.transform.localRotation = Quaternion.Euler(i == 0 ? 0f : 90f, t * 20f, 0f);
                    Tint(ring, c, (1f - k) * 0.75f);
                }
                if (m.Body != null)
                    Animate(m, t);
                PlaceLabel(m, inside, refreshText);
            }
        }

        private void Animate(Marker m, float t)
        {
            if (m.RevealedAt >= 0f) // 밝힌 순간 커지며 나타남
            {
                float k = Mathf.Clamp01((t - m.RevealedAt) / RevealSeconds);
                float e = 1f - (1f - k) * (1f - k) * (1f - k);
                m.Body.transform.localScale = Vector3.one * Mathf.Max(0.01f, e * (1f + 0.12f * Mathf.Sin(k * Mathf.PI)));
                if (k >= 1f)
                {
                    m.Body.transform.localScale = Vector3.one;
                    m.RevealedAt = -1f;
                }
            }
            float dt = Time.deltaTime;
            foreach (var sp in m.Spins)
            {
                sp.T.Rotate(sp.Axis, sp.Speed * dt, Space.Self);
                if (sp.OrbitSpeed != 0f)
                    sp.T.localPosition = Quaternion.AngleAxis(sp.OrbitSpeed * dt, sp.Orbit) * sp.T.localPosition;
            }
            if (m.Beacon != null)
            {
                bool lit = Mathf.Repeat(t, 1.4f) < 0.25f; // 규칙적으로 깜박임
                Glow(m.Beacon, new Color(1f, 0.15f, 0.12f), lit ? 4f : 0.15f, m.BeaconIndex);
            }
        }

        private void Tint(Renderer r, Color c, float alpha)
        {
            _block.Clear();
            _block.SetColor(BaseColorId, new Color(c.r, c.g, c.b, alpha));
            r.SetPropertyBlock(_block);
        }

        private void PlaceLabel(Marker m, bool inside, bool refreshText)
        {
            var world = m.Root.transform.position + Vector3.up * (_beamHeight + 0.3f);
            var screen = _camera != null ? _camera.WorldToScreenPoint(world) : Vector3.back;
            bool show = !inside && screen.z > 0f;
            if (m.Label.gameObject.activeSelf != show)
                m.Label.gameObject.SetActive(show);
            if (!show)
                return;
            var parent = (RectTransform)m.Label.rectTransform.parent;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, null, out var local);
            m.Label.rectTransform.anchoredPosition = local + parent.rect.size * parent.pivot; // 왼쪽 아래 기준
            if (!refreshText)
                return;
            var s = m.Site;
            string distance = $"{_explore.Distance(s)}칸";
            m.Label.SetText(s.Reached ? $"<color=#7CFF9A>{ExplorationConfig.Name(s.Kind)} · 탐사 중</color>"
                : s.Revealed ? $"<b>{ExplorationConfig.Name(s.Kind)}</b>\n<size=85%>지금 {distance} · {_explore.Config.ReachRadius}칸 이내 도달 시 탐사 시작</size>"
                : _explore.ProbeTarget == s ? $"<color=#FFD36A>탐사선 비행 중</color>\n<size=85%>{distance}</size>"
                : $"<color=#9FE8FF>신호</color> <size=85%>{ExplorationConfig.Hint(s.Kind)}</size>\n<size=85%>{distance}</size>");
        }
    }
}
