using System;
using System.Collections.Generic;
using SpaceStation.Core;
using TMPro;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 11-13 패드 위 3D 홀로그램 모형: 지금 보는 층의 모듈을 실제 모양(<see cref="PadHoloSet"/>)으로 줄여 가산 발광 재질로 띄운다.
    /// - 방향은 실제 정거장과 같음(월드 축) — 몸을 돌리면 모형이 제자리에 있고 보는 쪽이 바뀜. 보는 사람 쪽으로 기울여(<see cref="Settings.Tilt"/>)
    ///   위에서 내려다보는 지도처럼 보임 (눈높이에서 수평이면 납작한 판으로 보였음)
    /// - 색 = 패드가 정함(분류 · 파손 · 비활성 · 갈 수 없음), 고른 모듈 · 마우스를 올린 모듈은 밝게 + 이름표
    /// - 층 바꾸기 = 지금 모형이 위/아래로 빠지며 흐려지고 새 층이 반대쪽에서 들어옴 (위아래 층은 평소에 그리지 않음 — 겹치면 지저분)
    /// - 투사 빛기둥 · 바닥 고리 · 지나가는 스캔 면 · 깜박임
    /// </summary>
    public sealed class PadHologram : MonoBehaviour
    {
        public sealed class Settings
        {
            public PadHoloSet Set;
            public Material Fill;
            public Material Line;
            public TMP_FontAsset Font;
            /// <summary>모형 가로 · 세로 중 긴 쪽 최대 크기 (m, 확대 상태).</summary>
            public float Footprint = 0.15f;
            /// <summary>칸 하나의 최대 크기 (m) — 방이 몇 개 없을 때 너무 커지지 않게.</summary>
            public float MaxCell = 0.035f;
            public float FillAlpha = 0.12f;
            public float LineAlpha = 0.85f;
            public float Glow = 1.4f;
            /// <summary>투사기에서 모형 바닥까지 높이 (m, 확대 상태) — 모형이 패드 화면을 가리지 않게.</summary>
            public float Lift = 0.085f;
            /// <summary>보는 사람 쪽으로 기울이는 각 (도) — 위에서 내려다보는 지도처럼.</summary>
            public float Tilt = 55f;
        }

        private sealed class Piece
        {
            public ModuleInstance Module;
            public Transform Transform;
            public MeshRenderer Fill;
            public MeshRenderer Lines;
            public Bounds LocalBounds; // Fill 메시 기준
            public Color Color;
        }

        private sealed class Layer
        {
            public Transform Root;
            public readonly List<Piece> Pieces = new List<Piece>();
            public readonly Dictionary<Vector2Int, ModuleInstance> Cells = new Dictionary<Vector2Int, ModuleInstance>();
            public float Offset;   // 미끄러짐 (칸 단위, 위 +)
            public float Alpha = 1f;
            public float Target;   // 목표 Offset
            public float TargetAlpha = 1f;
            public Vector2 Center;   // 칸 범위 가운데 (x · z)
            public float Scale = 1f; // 칸 → m
        }

        private Settings _s;
        private Transform _anchor;
        private Transform _root;      // 수평 · 월드 축 (위치 = 투사기 위)
        private Transform _model;     // 크기 적용
        private Layer _layer;
        private readonly List<Layer> _leaving = new List<Layer>();
        private Transform _marker;
        private MeshRenderer _markerRenderer;
        private MeshRenderer _beam;
        private MeshRenderer _ring;
        private Transform _sweep;
        private MeshRenderer _sweepRenderer;
        private TextMeshPro _label;
        private MeshRenderer _plate, _stripe;
        private Material _labelMaterial;
        private Mesh _quad;
        private const int LabelQueue = 3100; // 홀로그램 재질(3050) 뒤
        private MaterialPropertyBlock _mpb;
        private float _scale = 1f;    // 지금 층의 칸 → m
        private float _present = 1f;  // 나타남 정도 (확대할 때 0 → 1로 펼쳐짐)
        private float _seed;
        private Mesh _octa, _cone, _disc, _plane, _bowlMesh;
        private MeshRenderer _bowl;

        public ModuleInstance Hover { get; set; }
        public ModuleInstance Selected { get; set; }
        public int Floor { get; private set; } = int.MinValue;
        /// <summary>모듈 색 (패드가 정함). 없으면 청록.</summary>
        public Func<ModuleInstance, Color> ColorOf;

        public static PadHologram Create(Transform anchor, Settings settings)
        {
            var go = new GameObject("PadHologram");
            var holo = go.AddComponent<PadHologram>();
            holo._s = settings;
            holo._anchor = anchor;
            holo.Build();
            return holo;
        }

        private void Build()
        {
            _mpb = new MaterialPropertyBlock();
            _seed = UnityEngine.Random.value * 10f;
            _root = transform;
            _model = new GameObject("Model").transform;
            _model.SetParent(_root, false);
            _octa = Octahedron();
            _cone = Cone(24);
            _disc = Ring(48);
            _plane = SweepPlane();

            // 뒤 배경판: 밝은 방 벽 앞에서도 모형이 보이게 은은하게 어둡게 (선 재질 = 반투명 섞기, 모형보다 먼저 그림)
            // 받침 반구: 모형 아래 검은 그릇 — 밝은 방 벽 앞에서도 모형이 보이게 (선 재질 = 반투명 섞기, 모형보다 먼저 그림)
            _bowl = Primitive("Bowl", _model, _bowlMesh = Bowl(40, 10), _s.Line);
            _bowl.sortingOrder = -1;
            _beam = Primitive("Beam", _root, _cone, _s.Fill);
            _ring = Primitive("Ring", _root, _disc, _s.Line);
            _sweepRenderer = Primitive("Sweep", _model, _plane, _s.Fill);
            _sweep = _sweepRenderer.transform;
            _markerRenderer = Primitive("Marker", _model, _octa, _s.Fill);
            _marker = _markerRenderer.transform;

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(_root, false);
            _label = labelGo.AddComponent<TextMeshPro>();
            if (_s.Font != null)
                _label.font = _s.Font;
            _label.fontSize = 0.8f; // TextMeshPro 3D: 글자 크기 1 ≈ 0.1 단위 높이 → 크기 0.1로 줄여 약 0.8cm
            _label.alignment = TextAlignmentOptions.Bottom;
            _label.textWrappingMode = TextWrappingModes.NoWrap;
            _label.rectTransform.sizeDelta = new Vector2(4f, 0.4f);
            _label.rectTransform.pivot = new Vector2(0.5f, 0f);
            labelGo.transform.localScale = Vector3.one * 0.1f;
            // 이름표 = 어두운 바탕판 + 흰 굵은 글자 + 왼쪽 모듈 색 띠. 모형(가산, 큐 3050)보다 나중에 그려야 발광에 묻히지 않음
            _label.color = new Color(0.97f, 0.97f, 0.97f, 1f); // 1 미만 — Bloom으로 번지지 않게
            _label.fontStyle = FontStyles.Bold;
            _label.outlineWidth = 0.2f;
            _label.outlineColor = new Color32(0, 8, 16, 255);
            _label.fontMaterial.renderQueue = LabelQueue + 2;
            _label.text = "";
            _labelMaterial = new Material(_s.Line) { renderQueue = LabelQueue };
            _quad = Quad();
            _plate = Primitive("Plate", labelGo.transform, _quad, _labelMaterial);
            _stripe = Primitive("Stripe", labelGo.transform, _quad, _labelMaterial);
            _stripe.sortingOrder = 1;
        }

        private MeshRenderer Primitive(string name, Transform parent, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        private void OnDestroy()
        {
            Destroy(_octa);
            Destroy(_cone);
            Destroy(_disc);
            Destroy(_plane);
            Destroy(_bowlMesh);
            Destroy(_quad);
            Destroy(_labelMaterial);
        }

        // ---------------- 층 만들기 ----------------

        /// <summary>층 모형을 (다시) 만듦. direction = 층 바꿈 방향(+1 위층으로 · −1 아래층으로 · 0 미끄러짐 없음).</summary>
        public void ShowFloor(int floor, IReadOnlyList<PadMapCell> cells, int direction)
        {
            if (_layer != null)
            {
                if (direction == 0)
                    Destroy(_layer.Root.gameObject);
                else
                {
                    // 위층으로 가면 지금 모형은 아래로 빠짐
                    _layer.Target = -direction * 1.6f;
                    _layer.TargetAlpha = 0f;
                    _leaving.Add(_layer);
                }
            }
            Floor = floor;
            _layer = new Layer { Root = new GameObject("Floor" + floor).transform };
            _layer.Root.SetParent(_model, false);
            _marker.SetParent(_layer.Root, false); // 내 위치 핀은 지금 층 칸 좌표
            if (direction != 0)
            {
                _layer.Offset = direction * 1.6f;
                _layer.Alpha = 0f;
            }

            // 칸 범위 → 가운데 · 크기
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            var seen = new HashSet<ModuleInstance>();
            foreach (var c in cells)
            {
                min = Vector2.Min(min, c.Cell);
                max = Vector2.Max(max, c.Cell);
                _layer.Cells[c.Cell] = c.Module;
                if (seen.Add(c.Module))
                    AddPiece(c.Module, floor);
            }
            if (cells.Count == 0)
            {
                min = max = Vector2.zero;
            }
            // 층마다 자기 크기 (빠져나가는 층이 새 층 크기로 커지지 않게)
            float span = Mathf.Max(max.x - min.x, max.y - min.y) + 1f;
            _layer.Center = (min + max) * 0.5f;
            _layer.Scale = Mathf.Min(_s.Footprint / span, _s.MaxCell);
            _scale = _layer.Scale;
            _layer.Root.localScale = Vector3.one * _layer.Scale;
            Place(_layer);
        }

        private static void Place(Layer layer)
        {
            float s = layer.Scale;
            layer.Root.localPosition = new Vector3(-layer.Center.x * s, layer.Offset * s, -layer.Center.y * s);
        }

        private void AddPiece(ModuleInstance module, int floor)
        {
            var entry = _s.Set != null ? _s.Set.Find(module.Data) : null;
            if (entry == null || entry.Fill == null)
                return;
            var t = new GameObject(module.Data.name).transform;
            t.SetParent(_layer.Root, false);
            // 모듈 원점(칸 가운데) 기준. 층 바닥 = 모형 바닥 (칸 반 높이만큼 올림).
            // 아래층에서 시작한 여러 층 모듈(코어 등)도 지금 층 바닥에 앉힘 — 아래로 삐져나오면 패드 화면을 가림
            t.localPosition = new Vector3(module.Origin.x, Mathf.Max(0, module.Origin.y - floor) + 0.5f, module.Origin.z);
            t.localRotation = GridDirections.ToQuaternion(module.Rotation);
            var piece = new Piece
            {
                Module = module,
                Transform = t,
                Fill = Primitive("Fill", t, entry.Fill, _s.Fill),
                Lines = entry.Lines != null ? Primitive("Lines", t, entry.Lines, _s.Line) : null,
                LocalBounds = entry.Fill.bounds,
            };
            _layer.Pieces.Add(piece);
        }

        // ---------------- 매 프레임 ----------------

        /// <summary>나타남 정도 (0 = 숨김 · 1 = 다 펼침) — 확대(패드 조작)할 때만 펼쳐지고, 들고 걸을 때는 숨김.</summary>
        public void SetPresentation(float appear01)
        {
            _present = appear01;
        }

        /// <summary>내 위치 표시 (칸 단위 x · z, 지금 층일 때만).</summary>
        public void SetPlayer(Vector2 cell, bool visible)
        {
            _marker.gameObject.SetActive(visible);
            if (visible)
                _marker.localPosition = new Vector3(cell.x, 1.15f + 0.06f * Mathf.Sin(Time.unscaledTime * 4f), cell.y); // 모듈 위에 뜬 핀 (층 칸 좌표)
        }

        private void LateUpdate()
        {
            if (_anchor == null)
                return;
            float t = Time.unscaledTime + _seed;
            float dt = Time.unscaledDeltaTime;
            _root.position = _anchor.position;
            // 방향은 월드 축(실제 정거장과 같음) 그대로, 보는 사람 쪽으로만 기울임 — 눈높이에서 수평이면 납작한 판으로 보임
            var cam = Camera.main;
            var flat = cam != null ? cam.transform.forward : Vector3.forward;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-4f)
                flat = Vector3.forward;
            var right = Vector3.Cross(Vector3.up, flat.normalized);
            _root.rotation = Quaternion.AngleAxis(-_s.Tilt, right);
            float lift = _s.Lift * _present;
            _model.localPosition = new Vector3(0f, lift, 0f);
            _model.localScale = Vector3.one * _present; // m 단위 (층마다 칸 크기는 층 Root에)

            // 깜박임 · 지지직
            float flicker = 0.93f + 0.07f * Mathf.Sin(t * 11.3f) * Mathf.Sin(t * 3.7f);
            if (Mathf.PerlinNoise(t * 2.3f, 0.71f) > 0.8f)
                flicker *= 0.7f;
            // 지지직: 가끔 모형이 옆으로 살짝 밀림
            if (Mathf.PerlinNoise(t * 5.1f, 2.3f) > 0.86f)
                _model.localPosition += new Vector3(0.0015f * _present, 0f, 0f);

            Animate(_layer, dt, flicker);
            for (int i = _leaving.Count - 1; i >= 0; i--)
            {
                var l = _leaving[i];
                Animate(l, dt, flicker);
                if (l.Alpha <= 0.01f && Mathf.Abs(l.Offset - l.Target) < 0.01f)
                {
                    Destroy(l.Root.gameObject);
                    _leaving.RemoveAt(i);
                }
            }

            // 빛기둥: 투사기에서 모형 바닥 고리까지 (모형 크기에 맞춤)
            float half = 0.5f * _s.Footprint * _present;
            _beam.transform.localPosition = Vector3.zero;
            _beam.transform.localScale = new Vector3(half, lift, half);
            Tint(_beam, HoloColor(0.12f * flicker));
            _ring.transform.localPosition = new Vector3(0f, lift, 0f);
            _ring.transform.localScale = new Vector3(half * 1.05f, 1f, half * 1.05f);
            _ring.transform.localRotation = Quaternion.Euler(0f, t * 12f, 0f);
            Tint(_ring, HoloColor(0.9f * flicker, 1.6f));

            // 스캔 면: 바닥 → 위로 천천히 지나감 (칸 높이 1.2 범위)
            float phase = Mathf.Repeat(t / 2.6f, 1f);
            _sweep.localPosition = new Vector3(0f, phase * 1.25f * _scale, 0f);
            _sweep.localScale = new Vector3(_s.Footprint * 0.55f, 1f, _s.Footprint * 0.55f);
            Tint(_sweepRenderer, HoloColor(0.22f * (1f - phase) * flicker));

            if (_marker.gameObject.activeSelf)
            {
                _marker.localRotation = Quaternion.Euler(0f, t * 90f, 0f);
                _marker.localScale = Vector3.one * 0.32f;
                Tint(_markerRenderer, new Color(1.5f, 1.5f, 1.5f, 0.95f));
            }

            // 받침 반구: 모형 바닥 아래 납작한 검은 그릇 (모형과 함께 커짐 — _model 자식)
            float bowlRadius = _s.Footprint * 0.62f;
            _bowl.transform.localPosition = new Vector3(0f, -0.002f, 0f);
            _bowl.transform.localScale = new Vector3(bowlRadius, bowlRadius * 0.45f, bowlRadius);
            Tint(_bowl, new Color(0.004f, 0.012f, 0.022f, 0.92f));
            UpdateLabel();
        }

        private void Animate(Layer layer, float dt, float flicker)
        {
            if (layer == null)
                return;
            layer.Offset = Mathf.MoveTowards(layer.Offset, layer.Target, dt * 5f);
            layer.Alpha = Mathf.MoveTowards(layer.Alpha, layer.TargetAlpha, dt * 3.2f);
            Place(layer);
            float a = layer.Alpha * flicker;
            foreach (var piece in layer.Pieces)
            {
                var c = ColorOf != null ? ColorOf(piece.Module) : new Color(0.31f, 0.85f, 1f);
                bool sel = piece.Module == Selected, hov = piece.Module == Hover;
                float boost = sel ? 1.9f : hov ? 1.5f : 1f;
                var lineColor = sel ? Color.Lerp(c, Color.white, 0.55f) : c;
                Tint(piece.Fill, Hdr(c, _s.Glow * 0.6f * boost, _s.FillAlpha * a * (sel ? 1.8f : hov ? 1.5f : 1f)));
                if (piece.Lines != null)
                    Tint(piece.Lines, Hdr(lineColor, _s.Glow * boost, _s.LineAlpha * a));
            }
        }

        private void UpdateLabel()
        {
            var target = Hover ?? Selected;
            Piece piece = null;
            if (target != null && _layer != null)
            {
                foreach (var p in _layer.Pieces)
                {
                    if (p.Module == target)
                    {
                        piece = p;
                        break;
                    }
                }
            }
            if (piece == null || _present < 0.75f)
            {
                if (_label.gameObject.activeSelf)
                    _label.gameObject.SetActive(false);
                return;
            }
            if (!_label.gameObject.activeSelf)
                _label.gameObject.SetActive(true);
            string name = target.Data != null ? target.Data.DisplayName : "";
            if (_label.text != name)
            {
                _label.text = name;
                // 바탕판 = 실제 글자 범위 + 여백 (이름표 로컬 단위)
                _label.ForceMeshUpdate();
                var tb = _label.textBounds;
                const float padX = 0.12f, padY = 0.06f, stripe = 0.05f;
                float w = tb.size.x + padX * 2f, h = tb.size.y + padY * 2f;
                _plate.transform.localPosition = new Vector3(tb.center.x, tb.min.y - padY, 0.02f);
                _plate.transform.localScale = new Vector3(w, h, 1f);
                _stripe.transform.localPosition = new Vector3(tb.center.x - w * 0.5f + stripe * 0.5f, tb.min.y - padY, 0.01f);
                _stripe.transform.localScale = new Vector3(stripe, h, 1f);
            }
            Tint(_plate, new Color(0.005f, 0.02f, 0.035f, 0.9f));
            var accent = ColorOf != null ? ColorOf(target) : new Color(0.31f, 0.85f, 1f);
            Tint(_stripe, new Color(accent.r, accent.g, accent.b, 1f));
            // 모듈 위쪽 가운데 위에 띄우고 카메라를 바라봄
            var b = piece.LocalBounds;
            var top = piece.Transform.TransformPoint(new Vector3(b.center.x, b.max.y, b.center.z));
            _label.transform.position = top + _root.up * 0.012f;
            var cam = Camera.main;
            if (cam != null)
                _label.transform.rotation = Quaternion.LookRotation(_label.transform.position - cam.transform.position, cam.transform.up);
        }

        // ---------------- 고르기 ----------------

        /// <summary>
        /// 광선이 가리키는 모듈: 먼저 층 바닥 칸(칸 가운데 높이 수평면)으로 — 위에서 본 지도처럼. 칸 밖이면 모듈별 상자로. 없으면 null.
        /// </summary>
        public ModuleInstance Pick(Ray ray)
        {
            if (_layer == null || _present < 0.75f)
                return null;
            var root = _layer.Root;
            var local = new Ray(root.InverseTransformPoint(ray.origin), root.InverseTransformDirection(ray.direction));
            if (PadMap.RayToCell(local, 0.5f, out var cell) && _layer.Cells.TryGetValue(cell, out var onFloor))
                return onFloor;
            ModuleInstance best = null;
            float bestDist = float.MaxValue;
            foreach (var p in _layer.Pieces)
            {
                var t = p.Fill.transform;
                var localRay = new Ray(t.InverseTransformPoint(ray.origin), t.InverseTransformDirection(ray.direction));
                var b = p.LocalBounds;
                b.Expand(0.04f);
                if (!b.IntersectRay(localRay, out float d))
                    continue;
                var hit = t.TransformPoint(localRay.GetPoint(d));
                float world = (hit - ray.origin).magnitude;
                if (world < bestDist)
                {
                    bestDist = world;
                    best = p.Module;
                }
            }
            return best;
        }

        // ---------------- 색 ----------------

        private void Tint(Renderer r, Color c)
        {
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColor, c);
            r.SetPropertyBlock(_mpb);
        }

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        private static Color Hdr(Color c, float intensity, float alpha) => new Color(c.r * intensity, c.g * intensity, c.b * intensity, Mathf.Clamp01(alpha));

        private Color HoloColor(float alpha, float intensity = 1f) => Hdr(new Color(0.31f, 0.85f, 1f), _s.Glow * intensity, alpha);

        // ---------------- 메시 ----------------

        private static Mesh Octahedron()
        {
            var m = new Mesh { name = "HoloMarker" };
            var v = new[]
            {
                new Vector3(0f, 1f, 0f), new Vector3(0f, -0.6f, 0f),
                new Vector3(0.5f, 0.2f, 0f), new Vector3(0f, 0.2f, 0.5f), new Vector3(-0.5f, 0.2f, 0f), new Vector3(0f, 0.2f, -0.5f),
            };
            m.vertices = v;
            m.triangles = new[] { 0, 3, 2, 0, 4, 3, 0, 5, 4, 0, 2, 5, 1, 2, 3, 1, 3, 4, 1, 4, 5, 1, 5, 2 };
            m.colors = new[] { Color.white, Color.white, Color.white, Color.white, Color.white, Color.white };
            m.RecalculateBounds();
            return m;
        }

        /// <summary>빛기둥: 꼭짓점(투사기, y=0)에서 위(y=1) 반지름 1 원으로 퍼지는 원뿔 옆면. 알파 = 위로 갈수록 흐려짐.</summary>
        private static Mesh Cone(int segments)
        {
            var m = new Mesh { name = "HoloBeam" };
            var v = new List<Vector3>();
            var c = new List<Color>();
            var tri = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                v.Add(Vector3.zero);
                c.Add(new Color(1f, 1f, 1f, 1f));
                v.Add(new Vector3(Mathf.Cos(a), 1f, Mathf.Sin(a)));
                c.Add(new Color(1f, 1f, 1f, 0.15f));
            }
            for (int i = 0; i < segments; i++)
            {
                int b = i * 2;
                tri.AddRange(new[] { b, b + 1, b + 3, b, b + 3, b + 2 });
            }
            m.SetVertices(v);
            m.SetColors(c);
            m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>바닥 고리: 원 + 눈금 12개 (선).</summary>
        private static Mesh Ring(int segments)
        {
            var m = new Mesh { name = "HoloRing" };
            var v = new List<Vector3>();
            var idx = new List<int>();
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                idx.Add(v.Count);
                v.Add(new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)));
                idx.Add(v.Count);
                v.Add(new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)));
            }
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI * 2f / 12f;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                idx.Add(v.Count);
                v.Add(d * 0.9f);
                idx.Add(v.Count);
                v.Add(d * (i % 3 == 0 ? 1.12f : 1f));
            }
            m.SetVertices(v);
            var colors = new Color[v.Count];
            for (int i = 0; i < colors.Length; i++)
                colors[i] = Color.white;
            m.colors = colors;
            m.SetIndices(idx, MeshTopology.Lines, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>이름표 판: xy 평면 사각형 (x −0.5~0.5 · y 0~1, 아래 가운데 기준).</summary>
        private static Mesh Quad()
        {
            var m = new Mesh { name = "HoloLabelQuad" };
            m.vertices = new[] { new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(0.5f, 1f, 0f), new Vector3(-0.5f, 1f, 0f) };
            m.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateBounds();
            return m;
        }

        /// <summary>
        /// 받침 반구: 위가 열린 아래쪽 반구 (반지름 1, 테두리 y=0 · 바닥 y=−1). 알파 = 바닥 1 → 테두리 0.8.
        /// 모형을 위에서 비스듬히 보므로 그릇 안쪽이 모형 뒤 바탕이 됨 (밝은 방 벽 앞에서도 보이게).
        /// </summary>
        private static Mesh Bowl(int segments, int rings)
        {
            var m = new Mesh { name = "HoloBowl" };
            var v = new List<Vector3>();
            var c = new List<Color>();
            var tri = new List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float lat = r / (float)rings * Mathf.PI * 0.5f; // 0 = 테두리 · π/2 = 바닥
                float y = -Mathf.Sin(lat), rad = Mathf.Cos(lat);
                for (int i = 0; i <= segments; i++)
                {
                    float a = i * Mathf.PI * 2f / segments;
                    v.Add(new Vector3(Mathf.Cos(a) * rad, y, Mathf.Sin(a) * rad));
                    c.Add(new Color(1f, 1f, 1f, Mathf.Lerp(0.8f, 1f, r / (float)rings)));
                }
            }
            int row = segments + 1;
            for (int r = 0; r < rings; r++)
            {
                for (int i = 0; i < segments; i++)
                {
                    int a = r * row + i, b = a + 1, d = a + row, e = d + 1;
                    tri.AddRange(new[] { a, b, e, a, e, d });
                }
            }
            m.SetVertices(v);
            m.SetColors(c);
            m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>스캔 면: 가운데가 밝고 가장자리로 흐려지는 수평 판 (반지름 1).</summary>
        private static Mesh SweepPlane()
        {
            var m = new Mesh { name = "HoloSweep" };
            const int n = 4;
            var v = new List<Vector3>();
            var c = new List<Color>();
            var tri = new List<int>();
            for (int z = 0; z <= n; z++)
            {
                for (int x = 0; x <= n; x++)
                {
                    float fx = x / (float)n * 2f - 1f, fz = z / (float)n * 2f - 1f;
                    v.Add(new Vector3(fx, 0f, fz));
                    float a = Mathf.Clamp01(1f - Mathf.Max(Mathf.Abs(fx), Mathf.Abs(fz)));
                    c.Add(new Color(1f, 1f, 1f, a));
                }
            }
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    int i = z * (n + 1) + x;
                    tri.AddRange(new[] { i, i + n + 1, i + 1, i + 1, i + n + 1, i + n + 2 });
                }
            }
            m.SetVertices(v);
            m.SetColors(c);
            m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
