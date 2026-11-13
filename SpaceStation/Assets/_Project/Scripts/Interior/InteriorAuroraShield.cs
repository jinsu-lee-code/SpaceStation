using System.Collections.Generic;
using SpaceStation.Audio;
using SpaceStation.Building;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 궤도 방어망 체험 "오로라 보호막 펼치기" (사용자 2026-11-13 "레버 → 보호막 쇼 관람", 순서 = <see cref="AuroraShieldShow"/>, 값 = <see cref="OrbitalDefense"/>):
    /// 방파제 끝 레버를 보고 F = 당김 → 레버 자리에서 오로라 보호막 (바다 위 납작한 반구)이 퍼져 덮고, 버티는 동안 별똥별이 하늘 면에서 떨어져 막에 부딪힘.
    /// 막 = 바깥 3단계 실드와 같은 무늬 (<see cref="AuroraBarrierFx.Paint"/> 오로라 구면 파문 + 닿은 자리 <see cref="CrystalBarrierFx"/> 수정 결정 막), 막 안에 서 있으면 안쪽 면이 앞면.
    /// 선베드 (<see cref="InteriorBenchSeats"/>, 눕기)에 누우면 막과 하늘을 올려다봄. 끝나면 막은 별똥별 몇 개를 막았는지 글로 알려 줌 (점수 없음). 실제 시간.
    /// </summary>
    public sealed class InteriorAuroraShield : MonoBehaviour
    {
        private const int HalfRings = 18;            // 수면 → 꼭대기 위도 줄
        private const int Segments = 72;             // 경도 칸
        private const float MeteorSpeed = 8f, MeteorReach = 4.5f;   // 별똥별 모양 = ShootingStar
        private const float BandLift = 0.42f;        // 오로라 띠 셋을 수면 위 반구에 (바깥 실드 구는 가운데 위아래로 걸침)
        private const float HitLife = 1.6f;          // 닿은 자리 번쩍 · 고리가 퍼지는 시간
        private const float CrystalRadius = 4f;      // 수정판을 맺는 구 (닿은 자리에서 막에 접함)
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly Color Front = new Color(0.75f, 0.97f, 1f);

        [Tooltip("방파제 끝 레버 (원점 = 굴대)")]
        [SerializeField] private Transform _lever;
        [Tooltip("레버 손잡이 충돌체 (보고 F = 당김)")]
        [SerializeField] private Collider _grip;
        [Tooltip("막 재질 (M_AuroraBarrier — 꼭짓점 색 가산)")]
        [SerializeField] private Material _domeMaterial;
        [Tooltip("별똥별 꼬리 (M_FireworkLine)")]
        [SerializeField] private Material _streakMaterial;
        [Tooltip("별똥별 머리에서 흩날리는 반짝이 (M_FireworkSpark)")]
        [SerializeField] private Material _sparkMaterial;
        [Tooltip("닿은 자리 수정 결정 막 (실제 시간)")]
        [SerializeField] private CrystalBarrierFx _crystal;
        [Tooltip("선베드 눕기 — 쉬는 중에 누우면 레버 안내")]
        [SerializeField] private InteriorBenchSeats _loungers;
        [SerializeField] private TMP_FontAsset _font;

        private sealed class Meteor
        {
            public ShootingStar Star;
            public Vector3 Unit;
        }

        private readonly AuroraShieldShow _show = new AuroraShieldShow();
        private readonly List<Meteor> _meteors = new List<Meteor>();
        private readonly List<Vector4> _hits = new List<Vector4>();   // xyz = 단위 구 방향, w = 지난 시간
        private Quaternion _leverRest;
        private Transform _dome;
        private MeshRenderer _domeRenderer;
        private Mesh _domeMesh;
        private Vector3[] _unit;
        private Color[] _colors;
        private MaterialPropertyBlock _block;
        private ParticleSystem _sparks;
        private Vector3 _leverDir;
        private float _time;
        private string _notice;
        private float _noticeTimer;
        private Canvas _canvas;
        private TextMeshProUGUI _hint;
        private string _hintShown;

        public AuroraShieldShow Show => _show;
        public Transform Dome => _dome;

        public void Configure(Transform lever, Collider grip, Material dome, Material streak, Material spark, CrystalBarrierFx crystal, InteriorBenchSeats loungers, TMP_FontAsset font)
        {
            _sparkMaterial = spark;
            _lever = lever;
            _grip = grip;
            _domeMaterial = dome;
            _streakMaterial = streak;
            _crystal = crystal;
            _loungers = loungers;
            _font = font;
        }

        private void Awake()
        {
            if (_lever != null)
                _leverRest = _lever.localRotation;
            BuildDome();
            var d = OrbitalDefense.AuroraLever - OrbitalDefense.ShieldCenter;
            _leverDir = new Vector3(d.x / OrbitalDefense.ShieldRadii.x, Mathf.Max(0f, d.y) / OrbitalDefense.ShieldRadii.y, d.z / OrbitalDefense.ShieldRadii.z).normalized;
            if (_grip != null)
            {
                var use = _grip.gameObject.AddComponent<InteriorInteractable>();
                use.Tap = true;
                use.Prompt = () => "오로라 보호막 펼치기";
                use.Blocked = () => _show.Running ? "보호막이 펼쳐져 있어요" : null;
                use.Completed = Pull;
            }
            if (_loungers != null)
                _loungers.SatDown += () =>
                {
                    if (!_show.Running)
                        Notice("방파제 끝 레버를 당기면 오로라 보호막이 펼쳐져요", 4f);
                };
        }

        private void OnDisable()
        {
            // 방을 나가면 쇼를 접음 (다음에 들어오면 레버부터)
            while (_show.Running)
                _show.Step(1f);
            foreach (var m in _meteors)
                m.Star.Destroy();
            _meteors.Clear();
            _hits.Clear();
            _noticeTimer = 0f;
            ApplyLever();
            if (_domeRenderer != null)
                _domeRenderer.enabled = false;
            ShowHint(null);
        }

        private void OnDestroy()
        {
            if (_canvas != null)
                Destroy(_canvas.gameObject);
            if (_domeMesh != null)
                Destroy(_domeMesh);
        }

        private void Pull()
        {
            if (!_show.Start())
                return;
            _time = 0f;
            _hits.Clear();
            Notice("오로라 보호막이 펼쳐져요  ·  선베드에 누우면 하늘이 잘 보여요", 5f);
            AudioService.TryPlay(l => l.UiOpen);
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (_noticeTimer > 0f)
                _noticeTimer -= dt;
            if (_show.Running)
            {
                var before = _show.Current;
                _time += dt;
                if (_show.Step(dt))
                    Launch();
                if (before == AuroraShieldShow.Phase.Hold && _show.Current == AuroraShieldShow.Phase.Fold)
                    Notice(_show.Blocked > 0 ? $"오로라 보호막이 별똥별 {_show.Blocked}개를 막았어요" : "오로라 보호막이 접혀요", 5f);
            }
            ApplyLever();
            StepMeteors(dt);
            PaintDome(dt);
            ShowHint(_noticeTimer > 0f ? _notice : null);
        }

        private void ApplyLever()
        {
            if (_lever != null)
                _lever.localRotation = Quaternion.AngleAxis(OrbitalDefense.LeverPull * _show.Lever, Vector3.right) * _leverRest;
        }

        // ---------------- 막 ----------------

        private void BuildDome()
        {
            if (_domeMaterial == null)
                return;
            _unit = new Vector3[(HalfRings + 1) * (Segments + 1)];
            for (int j = 0; j <= HalfRings; j++)
            {
                float lat = 0.5f * Mathf.PI * j / HalfRings;
                for (int i = 0; i <= Segments; i++)
                {
                    float lon = 2f * Mathf.PI * i / Segments;
                    _unit[j * (Segments + 1) + i] = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                }
            }
            var tris = new int[HalfRings * Segments * 6];
            int n = 0;
            for (int j = 0; j < HalfRings; j++)
            {
                for (int i = 0; i < Segments; i++)
                {
                    int a = j * (Segments + 1) + i, b = a + 1, c = a + Segments + 1, d = c + 1;
                    tris[n++] = a; tris[n++] = c; tris[n++] = b;
                    tris[n++] = b; tris[n++] = c; tris[n++] = d;
                }
            }
            _colors = new Color[_unit.Length];
            _domeMesh = new Mesh { name = "AuroraShieldDome" };
            _domeMesh.MarkDynamic();
            _domeMesh.vertices = _unit;
            _domeMesh.triangles = tris;
            _domeMesh.colors = _colors;
            _domeMesh.RecalculateBounds();
            var go = new GameObject("AuroraShieldDome");
            _dome = go.transform;
            _dome.SetParent(transform, false);
            _dome.localPosition = OrbitalDefense.ShieldCenter;
            _dome.localScale = OrbitalDefense.ShieldRadii;   // 단위 반구를 늘림 — 무늬 (위도 · 경도)는 단위 구에서
            go.AddComponent<MeshFilter>().sharedMesh = _domeMesh;
            _domeRenderer = go.AddComponent<MeshRenderer>();
            _domeRenderer.sharedMaterial = _domeMaterial;
            _domeRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _domeRenderer.receiveShadows = false;
            _domeRenderer.enabled = false;
            go.AddComponent<InteriorSkyMirrorAlways>();   // 곶 윗면 · 등대에서 칸 턱 너머로 보이는 북쪽이 직선으로 잘렸음
            _block = new MaterialPropertyBlock();
        }

        private void PaintDome(float dt)
        {
            for (int i = _hits.Count - 1; i >= 0; i--)
            {
                var h = _hits[i];
                h.w += dt;
                if (h.w > HitLife)
                    _hits.RemoveAt(i);
                else
                    _hits[i] = h;
            }
            if (_domeRenderer == null)
                return;
            float alpha = _show.Alpha;
            _domeRenderer.enabled = alpha > 0.001f;
            if (!_domeRenderer.enabled)
                return;
            var cam = Camera.main;
            var eye = cam != null ? _dome.InverseTransformPoint(cam.transform.position) : _leverDir * 3f;
            bool inside = eye.y >= 0f && eye.sqrMagnitude < 1f;
            float unfold = _show.Current == AuroraShieldShow.Phase.Unfold ? _show.PhaseTime : 99f;
            AuroraBarrierFx.Paint(_colors, _unit, Vector3.zero, 1f, _leverDir, eye, _time, _show.Spread, Mathf.Exp(-unfold * 4f), inside, BandLift);
            AddHits(_colors, _unit, _hits);
            _domeMesh.colors = _colors;
            var tint = _domeMaterial.GetColor(BaseColorId);
            _block.SetColor(BaseColorId, new Color(tint.r, tint.g, tint.b, tint.a * alpha));
            _domeRenderer.SetPropertyBlock(_block);
        }

        /// <summary>별똥별이 닿은 자리마다: 흰 번쩍 + 막을 따라 퍼지는 밝은 고리 (닿은 지 HitLife초 동안 옅어짐). 꼭짓점 색에 더함.</summary>
        public static void AddHits(Color[] colors, Vector3[] unit, List<Vector4> hits)
        {
            foreach (var h in hits)
            {
                var dir = new Vector3(h.x, h.y, h.z);
                float age = h.w, life = 1f - age / HitLife;
                float ring = 0.15f + age * 0.9f;                  // 고리 반지름 (단위 구 각, 라디안)
                float flash = Mathf.Exp(-age * 6f);
                for (int q = 0; q < unit.Length; q++)
                {
                    float dot = Vector3.Dot(unit[q], dir);
                    float ang = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f));
                    float r = (ang - ring) / 0.1f;
                    float wave = Mathf.Exp(-r * r) * life * 0.7f;
                    float spot = flash * Mathf.Exp(-(1f - dot) * 40f);
                    if (wave + spot < 0.004f)
                        continue;
                    var c = colors[q];
                    var rgb = Color.Lerp(Color.Lerp(c, Front, Mathf.Clamp01(wave * 1.4f)), Color.white, Mathf.Clamp01(spot));
                    colors[q] = new Color(rgb.r, rgb.g, rgb.b, Mathf.Clamp01(c.a + wave + spot));
                }
            }
        }

        // ---------------- 별똥별 ----------------

        private void Launch()
        {
            if (_dome == null)
                return;
            for (int tries = 0; tries < 8; tries++)
            {
                // 막 위 닿는 자리 = 위쪽 · 남쪽 (선베드 · 해변에서 보이는 쪽)
                float lat = Random.Range(25f, 68f) * Mathf.Deg2Rad;
                float lon = Random.Range(0f, 2f * Mathf.PI);
                var unit = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                if (unit.z > 0.35f)
                    continue;
                var to = OrbitalDefense.ShieldPoint(unit);
                var incoming = new Vector3(Random.Range(-0.5f, 0.5f), 1f, Random.Range(0.2f, 0.8f));
                var from = OrbitalDefense.MeteorStart(to, incoming, MeteorReach);
                if ((from - to).magnitude < 1.2f)
                    continue;
                var star = new ShootingStar(transform, _streakMaterial, transform.TransformPoint(from), transform.TransformPoint(to), MeteorSpeed);
                _meteors.Add(new Meteor { Star = star, Unit = unit });
                AudioService.TryPlay(l => l.MeteorIncoming);
                return;
            }
        }

        private void StepMeteors(float dt)
        {
            for (int i = _meteors.Count - 1; i >= 0; i--)
            {
                var m = _meteors[i];
                m.Star.Step(dt);
                if (!m.Star.Arrived)
                {
                    if (_sparks == null && _sparkMaterial != null)
                        _sparks = ShootingStar.CreateSparks(transform, _sparkMaterial);
                    m.Star.EmitTrail(_sparks);
                    continue;
                }
                Impact(m);
                m.Star.Destroy();
                _meteors.RemoveAt(i);
            }
        }

        private void Impact(Meteor m)
        {
            _hits.Add(new Vector4(m.Unit.x, m.Unit.y, m.Unit.z, 0f));
            _show.CountBlocked();
            if (_crystal != null)
            {
                var to = m.Star.To;
                var normal = transform.TransformDirection(OrbitalDefense.ShieldNormal(transform.InverseTransformPoint(to))).normalized;
                _crystal.Play(to - normal * CrystalRadius, CrystalRadius, to);
            }
            AudioService.TryPlay(l => l.ShieldDeflect);
        }

        // ---------------- 안내 ----------------

        private void Notice(string text, float seconds)
        {
            _notice = text;
            _noticeTimer = seconds;
        }

        private void ShowHint(string text)
        {
            if (text == _hintShown)
                return;
            _hintShown = text;
            if (text == null)
            {
                if (_canvas != null)
                    _canvas.gameObject.SetActive(false);
                return;
            }
            EnsureCanvas();
            _canvas.gameObject.SetActive(true);
            _hint.SetText(text);
        }

        private void EnsureCanvas()
        {
            if (_canvas != null)
                return;
            var go = new GameObject("AuroraShieldHint", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 300;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            var back = new GameObject("Back", typeof(RectTransform)).AddComponent<Image>();
            back.transform.SetParent(go.transform, false);
            Place(back.rectTransform, new Vector2(900f, 56f));
            back.color = new Color(0.03f, 0.05f, 0.1f, 0.62f);
            back.raycastTarget = false;
            _hint = new GameObject("Hint", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            _hint.transform.SetParent(go.transform, false);
            Place(_hint.rectTransform, new Vector2(880f, 48f));
            if (_font != null)
                _hint.font = _font;
            _hint.fontSize = 26f;
            _hint.enableAutoSizing = true;
            _hint.fontSizeMin = 15f;
            _hint.fontSizeMax = 26f;
            _hint.color = new Color(0.92f, 0.96f, 1f);
            _hint.alignment = TextAlignmentOptions.Center;
            _hint.raycastTarget = false;
        }

        private static void Place(RectTransform rt, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, 150f);   // 선베드 안내 (64) 위
            rt.sizeDelta = size;
        }
    }
}
