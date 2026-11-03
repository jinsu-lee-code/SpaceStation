using System.Collections.Generic;
using SpaceStation.Audio;
using SpaceStation.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 2단계 시민 회관 "스텔라돔" 상영 (사용자 2026-11-03 시안 1 — 2단계 직접 체험 "스텔라돔"): 쉼 → 어두워짐 → 별 · 은하수 → 별자리 여덟 → 별똥별 · 오로라 → 새벽, 쉼으로 돌아감 (실제 시간, 앉지 않아도 돔 안에서 함께 봄).
    /// 돔 안 조명 · 빛 조각은 상영 동안 어두워짐 (조명은 <see cref="InteriorAtmosphere"/>가 바꾼 세기를 존중해 곱함). 별 하늘 = 껍질 <see cref="_starDome"/> (SpaceStation/StellaDome), 별자리 = 실행 중 만드는 메시 (SpaceStation/StellaLines).
    /// 객석: 보고 F = 앉음 (앉은 채 둘러보기), 다시 F = 앉기 전 자리로 일어남. 쉬는 중에 앉으면 곧 시작. 바라본 별자리는 이름 · 선이 밝아지고 아래 글에 설명이 나옴 (색이 아니라 글 · 밝기 — 색약).
    /// 상영 동안 등받이가 차례로 뒤로 눕고, 앉은 플레이어도 함께 누워 하늘을 봄 (사용자 2026-11-03 "어느 자리에 앉아도 자연스럽게 관람"), 시선은 더하기로 옮겨 그 사이 마우스로 둘러본 만큼은 남음.
    /// 앉아 있던 주민도 같이 눕고 (<see cref="ResidentMotion.SeatRecline"/>), 누운 동안은 일어나 돌아다니지 않음 (<see cref="ResidentWander.Hold"/>, 사용자 2026-11-03). 상영 중 걸어와 앉은 주민은 앉은 뒤부터 눕고 묶임.
    /// 위치는 모두 템플릿 루트(이 물체) 기준. 템플릿 빌더가 configure에서 붙임. 라운지 구체가 천천히 오르내리는 것도 여기서.
    /// </summary>
    [DefaultExecutionOrder(-320)] // InteriorMode(-300)보다 먼저: 앉은 채 누른 F를 여기서 먼저 씀
    public sealed class InteriorStellaDome : InteriorRoomDisplay
    {
        private enum Phase { Idle, Dim, Stars, Constellations, Finale, Dawn }

        private const int ChartCount = 8;
        private const float LookAngle = 13f;      // 이 각 안의 별자리를 바라본 것으로
        private const float LineWidth = 0.02f;    // 잇는 선 반 폭 (m)
        private const float StarSize = 0.17f;     // 네 갈래 별 반 크기 (m)
        private const float SkyInset = -0.13f;    // 돔 곡면에서 안쪽으로 (별 하늘 껍질 -0.11보다 앞)
        private const float LabelInset = -0.3f;
        private const float LabelPull = 0.6f;     // 이름표를 눈 쪽으로 당기는 비율 (곡면에 파묻혀 글자가 잘리지 않게, 그만큼 줄여 보이는 크기 · 자리는 같음)

        private readonly struct Chart
        {
            public readonly string Name, Line;
            public readonly float Azimuth, Elevation, Scale;
            public readonly Vector2[] Stars;
            public readonly Vector2Int[] Links;

            public Chart(string name, string line, float azimuth, float elevation, float scale, Vector2[] stars, Vector2Int[] links)
            {
                Name = name; Line = line; Azimuth = azimuth; Elevation = elevation; Scale = scale; Stars = stars; Links = links;
            }
        }

        private static Vector2 V(float x, float y) => new Vector2(x, y);
        private static Vector2Int L(int a, int b) => new Vector2Int(a, b);

        // 상영 순서 (civ_builder CONS와 같은 자리 — 유리에 그린 별자리와 겹침)
        private static readonly Chart[] Charts =
        {
            new Chart("북두칠성", "북쪽 하늘의 길잡이, 국자 모양 일곱 별", 30f, 52f, 1.05f,
                new[] { V(-14, 2), V(-8, 3), V(-3, 2), V(2, 0), V(3, -5), V(10, -6), V(11, -1) }, new[] { L(0, 1), L(1, 2), L(2, 3), L(3, 4), L(4, 5), L(5, 6), L(6, 3) }),
            new Chart("카시오페이아", "W 모양으로 북극성을 찾아 주는 별", 150f, 50f, 1.05f,
                new[] { V(-9, -3), V(-4, 3), V(0, -1), V(4, 3), V(9, -3) }, new[] { L(0, 1), L(1, 2), L(2, 3), L(3, 4) }),
            new Chart("거문고자리", "여름밤 가장 밝은 별 베가", 90f, 62f, 1.1f,
                new[] { V(0, 5), V(-2.5f, 0), V(2.5f, 0), V(-3.5f, -6), V(1.5f, -6) }, new[] { L(0, 1), L(0, 2), L(1, 2), L(1, 3), L(2, 4), L(3, 4) }),
            new Chart("백조자리", "은하수를 따라 나는 커다란 십자", 325f, 40f, 1.15f,
                new[] { V(0, 11), V(0, 4), V(0, -2), V(0, -10), V(-7, 5), V(7, 2) }, new[] { L(0, 1), L(1, 2), L(2, 3), L(4, 1), L(1, 5) }),
            new Chart("사자자리", "봄밤의 왕, 낫 모양 머리와 긴 등", 95f, 30f, 1.1f,
                new[] { V(-11, -1), V(-9, 4), V(-6, 7), V(-3, 6), V(-3, 2), V(-6, -2), V(5, 0), V(10, -2) }, new[] { L(0, 1), L(1, 2), L(2, 3), L(3, 4), L(4, 5), L(5, 0), L(4, 6), L(6, 7), L(7, 5) }),
            new Chart("전갈자리", "남쪽 하늘에 휘어진 붉은 심장의 꼬리", 200f, 24f, 1.05f,
                new[] { V(-12, 7), V(-8, 6), V(-4, 4), V(0, 2), V(3, -2), V(5, -6), V(9, -8), V(13, -6), V(14, -2) }, new[] { L(0, 1), L(1, 2), L(2, 3), L(3, 4), L(4, 5), L(5, 6), L(6, 7), L(7, 8) }),
            new Chart("페가수스자리", "가을밤을 가로지르는 커다란 사각형", 270f, 62f, 0.95f,
                new[] { V(-8, 8), V(8, 8), V(8, -8), V(-8, -8), V(-14, -12) }, new[] { L(0, 1), L(1, 2), L(2, 3), L(3, 0), L(3, 4) }),
            new Chart("오리온자리", "겨울밤의 사냥꾼, 허리띠의 세 별", 250f, 36f, 1.15f,
                new[] { V(-6, 9), V(6, 10), V(-2.2f, 0.5f), V(0, 0), V(2.2f, -0.5f), V(-5, -9), V(6, -9) }, new[] { L(0, 1), L(0, 2), L(1, 4), L(2, 3), L(3, 4), L(2, 5), L(4, 6), L(5, 6) }),
        };

        private static readonly int ShowId = Shader.PropertyToID("_Show");
        private static readonly int StarRevealId = Shader.PropertyToID("_StarReveal");
        private static readonly int MilkyId = Shader.PropertyToID("_Milky");
        private static readonly int AuroraId = Shader.PropertyToID("_Aurora");
        private static readonly int MeteorId = Shader.PropertyToID("_Meteor");
        private static readonly int DawnId = Shader.PropertyToID("_Dawn");
        private static readonly int RevealId = Shader.PropertyToID("_Reveal");
        private static readonly int GlowId = Shader.PropertyToID("_Glow");
        private static readonly int FadeId = Shader.PropertyToID("_Fade");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [Tooltip("별 하늘 껍질")]
        [SerializeField] private Renderer _starDome;
        [Tooltip("별자리 재질 (SpaceStation/StellaLines)")]
        [SerializeField] private Material _lineMaterial;
        [Tooltip("상영 때 어두워지는 빛 조각 (하루 띠 · 돔 안 빛줄 · 태양)")]
        [SerializeField] private Renderer[] _glow;
        [Tooltip("객석 쿠션 (충돌체 있음)")]
        [SerializeField] private Transform _seats;
        [Tooltip("자리: 발 위치 x · y · z, w = 바라보는 방향 (템플릿 기준)")]
        [SerializeField] private Vector4[] _seatSpots;
        [SerializeField] private Transform _orbs;
        [Tooltip("돔 축 (x, z) · 돔 안 반지름")]
        [SerializeField] private Vector3 _axis = new Vector3(4f, 4f, 6.35f);
        [Tooltip("돔 곡선: 시작 높이 · 꼭대기 높이 · 어깨 지수 (civ_builder dome_pt)")]
        [SerializeField] private Vector3 _curve = new Vector3(1.2f, 3.78f, 2.3f);
        [SerializeField] private float _idleSeconds = 20f;
        [SerializeField] private float _dimSeconds = 6f;
        [SerializeField] private float _starsSeconds = 12f;
        [SerializeField] private float _chartSeconds = 4.5f;
        [SerializeField] private float _finaleSeconds = 14f;
        [SerializeField] private float _dawnSeconds = 7f;
        [Tooltip("쉬는 중에 앉으면 이 안에 시작 (초)")]
        [SerializeField] private float _seatedWait = 5f;
        [Tooltip("상영 중 조명 · 빛 조각 세기")]
        [SerializeField] private float _houseLow = 0.1f;
        [Tooltip("앉았을 때 눈높이 (좌석 바닥에서 m)")]
        [SerializeField] private float _seatedEye = 1.1f;
        [Tooltip("앉았을 때 처음 시선 (음수 = 위)")]
        [SerializeField] private float _seatedPitch = -32f;
        [Tooltip("상영 때 누운 시선 (음수 = 위)")]
        [SerializeField] private float _lyingPitch = -68f;
        [Tooltip("눕는 등받이 (자리 순서대로)")]
        [SerializeField] private Transform[] _backs;
        [Tooltip("등받이 경첩 (템플릿 기준, 자리 순서대로)")]
        [SerializeField] private Vector3[] _hinges;
        [Tooltip("상영 때 등받이가 눕는 각 (도)")]
        [SerializeField] private float _reclineAngle = 28f;
        [Tooltip("등받이 · 플레이어가 눕고 일어나는 데 걸리는 시간 (초)")]
        [SerializeField] private float _reclineSeconds = 3.2f;
        [Tooltip("등받이가 차례로 움직이는 시차 (초)")]
        [SerializeField] private float _reclineWave = 0.9f;
        [Tooltip("새벽의 이 비율에서 등받이가 일어나기 시작")]
        [SerializeField] private float _riseAtDawn = 0.4f;

        private Phase _phase = Phase.Idle;
        private float _timer;
        private float _house = 1f;
        private bool _seated;
        private int _standFrame = -1;
        private Vector3 _standPoint;
        private Collider _seatCollider;
        private FirstPersonController _player;
        private Material _sky;
        private Material _lines;
        private Renderer _lineRenderer;
        private readonly float[] _reveal = new float[ChartCount];
        private readonly float[] _look = new float[ChartCount];
        private Vector3[] _chartCenters;
        private TextMeshPro[] _labels;
        private float[] _labelAlpha;
        private Vector3[] _labelAnchor;
        private int _looking = -1;
        private readonly List<Light> _lights = new List<Light>();
        private float[] _lightBase;
        private float[] _lightSet;
        private Material[] _glowMaterials;
        private Color[] _glowBase;
        private Color[] _glowEmission;
        private float _appliedHouse = -1f;
        private int _aimFrame = -1;
        private int _aimSeat = -1;
        private Canvas _canvas;
        private TextMeshProUGUI _hint;
        private string _hintShown;
        private Vector3 _orbsBase;
        private readonly Collider[] _overlap = new Collider[16];
        private Vector3[] _backAxis;
        private Vector3[] _backPos;
        private Quaternion[] _backRot;
        private float[] _backDelay;
        private float[] _backAmount;   // 0 = 세움 ~ 1 = 누움
        private ResidentFigure[] _residents;   // 자리에 앉은 주민 (상영 동안 찾음)
        private float[] _bodyAmount;           // 주민: 0 = 앉음 ~ 1 = 누움
        private int _residentCheck;
        private bool _reclined;
        private float _reclineClock;
        private int _seat = -1;
        private float _lie;            // 앉은 플레이어: 0 = 앉음 ~ 1 = 누움

        public bool Seated => _seated;
        public bool ShowRunning => _phase != Phase.Idle;
        public string PhaseName => _phase.ToString();

        public void Configure(Renderer starDome, Material lineMaterial, Renderer[] glow, Transform seats, Vector4[] seatSpots, Transform[] backs, Vector3[] hinges, Transform orbs, Vector3 axis, Vector3 curve)
        {
            _backs = backs;
            _hinges = hinges;
            _starDome = starDome;
            _lineMaterial = lineMaterial;
            _glow = glow;
            _seats = seats;
            _seatSpots = seatSpots;
            _orbs = orbs;
            _axis = axis;
            _curve = curve;
        }

        private void Awake()
        {
            if (_starDome == null)
                return;
            _sky = _starDome.material;
            _timer = _idleSeconds;
            _orbsBase = _orbs != null ? _orbs.localPosition : Vector3.zero;
            BuildChartMesh();
            BuildLabels();
            CollectHouse();
            SetupBacks();
            if (_seats != null)
            {
                _seatCollider = _seats.GetComponent<Collider>();
                var use = _seats.gameObject.AddComponent<InteriorInteractable>();
                use.Tap = true;
                use.Prompt = () => "앉아서 별 보기";
                use.Blocked = () => AimSeat() >= 0 && ResidentOn(AimSeat()) != null ? "주민이 앉아 있는 자리" : null;
                use.Completed = Sit;
            }
            ApplySky(0f, 0f, 0f, 0f, 0f, 0f, 0f);
            ApplyHouse(1f);
        }

        private void OnDisable()
        {
            // 내부에서 나가거나 방이 꺼지면 처음으로
            ReleasePlayer();
            ShowHint(null);
            if (_sky == null)
                return;
            ResetShow();
            ResetBacks();
        }

        private void OnDestroy()
        {
            if (_canvas != null)
                Destroy(_canvas.gameObject);
            if (_sky != null)
                Destroy(_sky);
            if (_lines != null)
                Destroy(_lines);
            if (_glowMaterials != null)
            {
                foreach (var m in _glowMaterials)
                    Destroy(m);
            }
        }

        protected override void Refresh()
        {
            if (!Active && _phase != Phase.Idle)
                ResetShow();    // 전력이 끊기면 상영 중단
        }

        private void ResetShow()
        {
            _phase = Phase.Idle;
            _timer = _idleSeconds;
            for (int i = 0; i < ChartCount; i++)
            {
                _reveal[i] = 0f;
                _look[i] = 0f;
            }
            _looking = -1;
            ApplySky(0f, 0f, 0f, 0f, 0f, 0f, 0f);
            UpdateLabels(0f, Time.unscaledDeltaTime);
            ApplyHouse(1f);
        }

        // ---------------- 시간표 ----------------

        private void Update()
        {
            if (_sky == null)
                return;
            float dt = Time.unscaledDeltaTime;
            if (_player == null)
                _player = FindFirstObjectByType<FirstPersonController>();
            if (_seated && KeyBindings.WasPressed(GameAction.Interact))
                StandUp();
            if (_orbs != null)
                _orbs.localPosition = _orbsBase + Vector3.up * (0.06f * Mathf.Sin(Time.unscaledTime * 0.7f));
            bool running = !Polled || Active;
            float show = 0f, stars = 0f, milky = 0f, aurora = 0f, meteor = 0f, dawn = 0f, fade = 1f, house = 1f;
            switch (_phase)
            {
                case Phase.Idle:
                    if (running)
                    {
                        _timer -= dt;
                        if (_timer <= 0f)
                            Next(Phase.Dim);
                    }
                    break;
                case Phase.Dim:
                {
                    float p = Advance(dt, _dimSeconds, Phase.Stars);
                    show = Mathf.SmoothStep(0f, 1f, p);
                    house = Mathf.Lerp(1f, _houseLow, p);
                    break;
                }
                case Phase.Stars:
                {
                    float p = Advance(dt, _starsSeconds, Phase.Constellations);
                    show = 1f;
                    stars = p;
                    milky = Mathf.SmoothStep(0f, 1f, p);
                    house = _houseLow;
                    break;
                }
                case Phase.Constellations:
                {
                    Advance(dt, _chartSeconds * ChartCount, Phase.Finale);
                    show = stars = milky = 1f;
                    house = _houseLow;
                    for (int i = 0; i < ChartCount; i++)
                        _reveal[i] = Mathf.Clamp01((_timer - i * _chartSeconds) / (_chartSeconds * 0.62f));
                    break;
                }
                case Phase.Finale:
                {
                    float p = Advance(dt, _finaleSeconds, Phase.Dawn);
                    show = stars = milky = 1f;
                    house = _houseLow;
                    meteor = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(p * 6f)) * Mathf.SmoothStep(1f, 0f, Mathf.Clamp01((p - 0.85f) / 0.15f));
                    aurora = Mathf.Sin(Mathf.Clamp01(p * 1.04f) * Mathf.PI);
                    aurora *= aurora > 0f ? Mathf.Sqrt(aurora) : 0f;
                    fade = 1f - 0.72f * Mathf.SmoothStep(0f, 1f, p);
                    for (int i = 0; i < ChartCount; i++)
                        _reveal[i] = 1f;
                    break;
                }
                case Phase.Dawn:
                {
                    float p = Advance(dt, _dawnSeconds, Phase.Idle);
                    dawn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(p * 1.6f));
                    stars = milky = 1f - Mathf.SmoothStep(0f, 1f, p);
                    fade = 0.28f * (1f - Mathf.Clamp01(p * 2f));
                    show = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((p - 0.5f) / 0.5f));
                    house = Mathf.Lerp(_houseLow, 1f, Mathf.Clamp01((p - 0.45f) / 0.55f));
                    if (_phase == Phase.Idle)
                    {
                        show = stars = milky = dawn = fade = 0f;
                        house = 1f;
                        System.Array.Clear(_reveal, 0, ChartCount);
                    }
                    break;
                }
            }
            UpdateRecline(dt);
            UpdateLooking(dt);
            ApplySky(show, stars, milky, aurora, meteor, dawn, fade);
            UpdateLabels(fade, dt);
            ApplyHouse(house);
            UpdateHint();
        }

        private float Advance(float dt, float seconds, Phase next)
        {
            _timer += dt;
            float p = Mathf.Clamp01(_timer / Mathf.Max(seconds, 0.01f));
            if (_timer >= seconds)
                Next(next);
            return p;
        }

        private void Next(Phase phase)
        {
            _phase = phase;
            _timer = phase == Phase.Idle ? _idleSeconds : 0f;
            if (phase == Phase.Dim && InDome())
                AudioService.TryPlay(l => l.UiOpen);
        }

        private void ApplySky(float show, float stars, float milky, float aurora, float meteor, float dawn, float fade)
        {
            bool visible = show > 0.001f;
            if (_starDome.enabled != visible)
                _starDome.enabled = visible;
            bool lines = visible && fade > 0.001f;
            if (_lineRenderer != null && _lineRenderer.enabled != lines)
                _lineRenderer.enabled = lines;
            if (!visible)
                return;
            _sky.SetFloat(ShowId, show);
            _sky.SetFloat(StarRevealId, stars);
            _sky.SetFloat(MilkyId, milky);
            _sky.SetFloat(AuroraId, aurora);
            _sky.SetFloat(MeteorId, meteor);
            _sky.SetFloat(DawnId, dawn);
            if (_lines != null)
            {
                _lines.SetFloat(FadeId, fade);
                _lines.SetFloatArray(RevealId, _reveal);
                _lines.SetFloatArray(GlowId, _look);
            }
        }

        // ---------------- 집 불빛 ----------------

        private void CollectHouse()
        {
            var root = transform.Find("Lights");
            if (root != null)
            {
                foreach (var l in root.GetComponentsInChildren<Light>(true))
                {
                    var p = l.transform.localPosition;
                    if (new Vector2(p.x - _axis.x, p.z - _axis.y).magnitude < _axis.z + 0.1f)
                        _lights.Add(l);
                }
            }
            _lightBase = new float[_lights.Count];
            _lightSet = new float[_lights.Count];
            for (int i = 0; i < _lights.Count; i++)
                _lightBase[i] = _lightSet[i] = _lights[i].intensity;
            var mats = new List<Material>();
            if (_glow != null)
            {
                foreach (var r in _glow)
                {
                    if (r != null)
                        mats.AddRange(r.materials);
                }
            }
            _glowMaterials = mats.ToArray();
            _glowBase = new Color[_glowMaterials.Length];
            _glowEmission = new Color[_glowMaterials.Length];
            for (int i = 0; i < _glowMaterials.Length; i++)
            {
                var m = _glowMaterials[i];
                _glowBase[i] = m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : Color.white;
                _glowEmission[i] = m.HasProperty(EmissionColorId) ? m.GetColor(EmissionColorId) : Color.black;
            }
        }

        private void ApplyHouse(float k)
        {
            for (int i = 0; i < _lights.Count; i++)
            {
                var l = _lights[i];
                if (l == null)
                    continue;
                // InteriorAtmosphere가 세기를 바꿨으면 (정전 · 경보) 그 값을 새 기준으로
                if (!Mathf.Approximately(l.intensity, _lightSet[i]))
                    _lightBase[i] = l.intensity / Mathf.Max(_house, 0.01f);
                _lightSet[i] = _lightBase[i] * k;
                l.intensity = _lightSet[i];
            }
            _house = k;
            if (Mathf.Approximately(k, _appliedHouse))
                return;
            _appliedHouse = k;
            for (int i = 0; i < _glowMaterials.Length; i++)
            {
                var m = _glowMaterials[i];
                if (m.HasProperty(EmissionColorId))
                    m.SetColor(EmissionColorId, _glowEmission[i] * k);
                if (m.HasProperty(BaseColorId))
                {
                    var c = _glowBase[i] * Mathf.Lerp(0.15f, 1f, k);
                    c.a = _glowBase[i].a;
                    m.SetColor(BaseColorId, c);
                }
            }
        }

        // ---------------- 별자리 ----------------

        /// <summary>돔 곡면의 한 점 (civ_builder dome_pt와 같음, 템플릿 기준). th = 곡선 각 (0 = 돔 시작 · 90 = 꼭대기).</summary>
        private Vector3 DomePoint(float azimuth, float th, float inset)
        {
            float t = th * Mathf.Deg2Rad;
            float c = Mathf.Max(0f, Mathf.Cos(t)), s = Mathf.Max(0f, Mathf.Sin(t));
            float e = 2f / _curve.z;
            float r = _axis.z * Mathf.Pow(c, e) + inset * c;
            float y = _curve.x + (_curve.y - _curve.x) * Mathf.Pow(s, e) + inset * s;
            float a = azimuth * Mathf.Deg2Rad;
            return new Vector3(_axis.x + r * Mathf.Cos(a), y, _axis.y + r * Mathf.Sin(a));
        }

        private static Vector2 ChartPoint(in Chart chart, Vector2 star)
        {
            float el = chart.Elevation + star.y * chart.Scale;
            float az = chart.Azimuth + star.x * chart.Scale / Mathf.Max(Mathf.Cos(chart.Elevation * Mathf.Deg2Rad), 0.3f);
            return new Vector2(az, el);
        }

        private void BuildChartMesh()
        {
            if (_lineMaterial == null)
                return;
            var vertices = new List<Vector3>();
            var uv0 = new List<Vector2>();
            var uv1 = new List<Vector4>();
            var indices = new List<int>();
            var center = new Vector3(_axis.x, _curve.x, _axis.y);
            _chartCenters = new Vector3[ChartCount];
            for (int c = 0; c < ChartCount; c++)
            {
                var chart = Charts[c];
                float[] first = new float[chart.Stars.Length];
                for (int s = 0; s < first.Length; s++)
                    first[s] = 2f;
                float span = 1f / chart.Links.Length;
                for (int k = 0; k < chart.Links.Length; k++)
                {
                    var link = chart.Links[k];
                    first[link.x] = Mathf.Min(first[link.x], k * span);
                    first[link.y] = Mathf.Min(first[link.y], k * span);
                    var a = ChartPoint(chart, chart.Stars[link.x]);
                    var b = ChartPoint(chart, chart.Stars[link.y]);
                    const int steps = 6;
                    int start = vertices.Count;
                    for (int n = 0; n <= steps; n++)
                    {
                        float f = n / (float)steps;
                        var ae = Vector2.Lerp(a, b, f);
                        var p = DomePoint(ae.x, ae.y, SkyInset);
                        var q = DomePoint(Mathf.Lerp(a.x, b.x, Mathf.Min(f + 0.02f, 1f)), Mathf.Lerp(a.y, b.y, Mathf.Min(f + 0.02f, 1f)), SkyInset) - DomePoint(Mathf.Lerp(a.x, b.x, Mathf.Max(f - 0.02f, 0f)), Mathf.Lerp(a.y, b.y, Mathf.Max(f - 0.02f, 0f)), SkyInset);
                        var side = Vector3.Cross(q, center - p).normalized * LineWidth;
                        vertices.Add(p - side);
                        vertices.Add(p + side);
                        uv0.Add(new Vector2(f, -1f));
                        uv0.Add(new Vector2(f, 1f));
                        var data = new Vector4(c, k * span, span, 0f);
                        uv1.Add(data);
                        uv1.Add(data);
                        if (n > 0)
                        {
                            int v = start + (n - 1) * 2;
                            indices.AddRange(new[] { v, v + 2, v + 1, v + 1, v + 2, v + 3 });
                        }
                    }
                }
                var sum = Vector3.zero;
                for (int s = 0; s < chart.Stars.Length; s++)
                {
                    var ae = ChartPoint(chart, chart.Stars[s]);
                    var p = DomePoint(ae.x, ae.y, SkyInset - 0.005f);
                    sum += p;
                    var n = (center - p).normalized;
                    var u = Vector3.Cross(Vector3.up, n);
                    u = u.sqrMagnitude > 0.0001f ? u.normalized : Vector3.right;
                    var w = Vector3.Cross(n, u);
                    float size = StarSize * (s % 3 == 0 ? 1.2f : 0.85f);
                    int start = vertices.Count;
                    var data = new Vector4(c, Mathf.Min(first[s], 1f), span, 1f);
                    for (int i = 0; i < 4; i++)
                    {
                        float x = i == 0 || i == 3 ? -1f : 1f, y = i < 2 ? -1f : 1f;
                        vertices.Add(p + (u * x + w * y) * size);
                        uv0.Add(new Vector2(x, y));
                        uv1.Add(data);
                    }
                    indices.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
                }
                _chartCenters[c] = sum / chart.Stars.Length;
            }
            var mesh = new Mesh { name = "StellaCharts", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            var go = new GameObject("StellaCharts");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            _lineRenderer = go.AddComponent<MeshRenderer>();
            _lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _lineRenderer.receiveShadows = false;
            _lines = new Material(_lineMaterial);
            _lineRenderer.sharedMaterial = _lines;
            _lineRenderer.enabled = false;
        }

        private void BuildLabels()
        {
            if (Font == null || _chartCenters == null)
                return;
            _labels = new TextMeshPro[ChartCount];
            _labelAlpha = new float[ChartCount];
            _labelAnchor = new Vector3[ChartCount];
            var center = new Vector3(_axis.x, _curve.x, _axis.y);
            for (int c = 0; c < ChartCount; c++)
            {
                var go = new GameObject("StellaLabel" + c);
                go.transform.SetParent(transform, false);
                var p = _chartCenters[c];
                _labelAnchor[c] = p + (center - p).normalized * -LabelInset;
                go.transform.localPosition = _labelAnchor[c];
                var text = go.AddComponent<TextMeshPro>();
                text.font = Font;
                text.fontSize = 3.2f;
                text.color = new Color(0.92f, 0.95f, 1f, 0f);
                text.alignment = TextAlignmentOptions.Center;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.rectTransform.sizeDelta = new Vector2(3.6f, 0.7f);
                text.SetText(Charts[c].Name);
                go.SetActive(false);
                _labels[c] = text;
            }
        }

        private void UpdateLooking(float dt)
        {
            _looking = -1;
            if (_player != null && _chartCenters != null && _phase == Phase.Constellations || _phase == Phase.Finale)
            {
                var eye = _player.Eye;
                float best = LookAngle;
                for (int c = 0; c < ChartCount; c++)
                {
                    if (_reveal[c] < 0.3f)
                        continue;
                    float angle = Vector3.Angle(eye.forward, transform.TransformPoint(_chartCenters[c]) - eye.position);
                    if (angle < best)
                    {
                        best = angle;
                        _looking = c;
                    }
                }
            }
            for (int c = 0; c < ChartCount; c++)
                _look[c] = Mathf.MoveTowards(_look[c], c == _looking ? 1f : 0f, dt * 2.5f);
        }

        private void UpdateLabels(float fade, float dt)
        {
            if (_labels == null)
                return;
            for (int c = 0; c < ChartCount; c++)
            {
                float want = _reveal[c] > 0.45f && (_phase == Phase.Constellations || _phase == Phase.Finale) ? Mathf.Max(0.55f, _look[c]) * Mathf.Clamp01(fade * 1.4f) : 0f;
                float a = Mathf.MoveTowards(_labelAlpha[c], want, dt * 1.6f);
                var label = _labels[c];
                if (!Mathf.Approximately(a, _labelAlpha[c]))
                {
                    _labelAlpha[c] = a;
                    label.alpha = a;
                }
                bool on = a > 0.003f;
                if (label.gameObject.activeSelf != on)
                    label.gameObject.SetActive(on);
                if (on && _player != null)
                {
                    var eye = _player.Eye;
                    label.transform.position = Vector3.Lerp(eye.position, transform.TransformPoint(_labelAnchor[c]), LabelPull);
                    label.transform.localScale = Vector3.one * LabelPull;
                    label.transform.rotation = Quaternion.LookRotation(label.transform.position - eye.position, eye.up);   // 누운 시선에서도 화면에 똑바로
                }
            }
        }

        // ---------------- 눕는 등받이 ----------------

        private void SetupBacks()
        {
            if (_backs == null || _hinges == null || _backs.Length != _hinges.Length)
            {
                _backs = null;
                return;
            }
            int n = _backs.Length;
            _backAxis = new Vector3[n];
            _backPos = new Vector3[n];
            _backRot = new Quaternion[n];
            _backDelay = new float[n];
            _backAmount = new float[n];
            _residents = new ResidentFigure[n];
            _bodyAmount = new float[n];
            for (int i = 0; i < n; i++)
            {
                var outward = new Vector3(_hinges[i].x - _axis.x, 0f, _hinges[i].z - _axis.y).normalized;
                _backAxis[i] = Vector3.Cross(Vector3.up, outward);   // 양의 각 = 위 끝이 돔 축에서 멀어짐
                _backPos[i] = _backs[i].localPosition;
                _backRot[i] = _backs[i].localRotation;
                float az = Mathf.Atan2(outward.z, outward.x) * Mathf.Rad2Deg;
                _backDelay[i] = _reclineWave * Mathf.Repeat(az + 45f, 90f) / 90f;   // 부채꼴마다 한쪽 끝에서 차례로
            }
        }

        private bool WantRecline()
        {
            switch (_phase)
            {
                case Phase.Idle:
                    return false;
                case Phase.Dawn:
                    return _timer < _dawnSeconds * _riseAtDawn;
                default:
                    return true;
            }
        }

        private void UpdateRecline(float dt)
        {
            if (_backs == null)
                return;
            bool want = WantRecline();
            int n = _backs.Length;
            if (want != _reclined)
            {
                _reclined = want;
                _reclineClock = 0f;
                if (want)
                {
                    for (int i = 0; i < n; i++)
                        FindResident(i);
                }
            }
            _reclineClock += dt;
            if (want)
            {
                // 상영 중에 걸어와 앉은 주민 (한 프레임에 한 자리씩)
                _residentCheck = (_residentCheck + 1) % n;
                FindResident(_residentCheck);
            }
            float step = dt / Mathf.Max(_reclineSeconds, 0.01f);
            for (int i = 0; i < n; i++)
            {
                if (i != _seat && _reclineClock < _backDelay[i])
                    continue;
                float amount = Mathf.MoveTowards(_backAmount[i], want ? 1f : 0f, step);
                if (amount != _backAmount[i])
                {
                    _backAmount[i] = amount;
                    ApplyBack(i);
                }
                UpdateResident(i, want, step);
            }
            if (_seated && _seat >= 0)
            {
                float lie = Mathf.MoveTowards(_lie, want ? 1f : 0f, step);
                if (lie != _lie)
                    SetLie(lie);
            }
        }

        private void FindResident(int i)
        {
            var figure = i == _seat ? null : ResidentOn(i);
            if (figure != _residents[i])
            {
                ReleaseResident(i);
                _residents[i] = figure;
            }
        }

        /// <summary>자리의 주민을 등받이와 같은 빠르기로 눕히고, 누운 동안 일어나지 못하게 묶음.</summary>
        private void UpdateResident(int i, bool want, float step)
        {
            var figure = _residents[i];
            if (figure == null || figure.Motion == null)
                return;
            bool seated = figure.Motion.StandBlend <= 0.01f;
            float amount = Mathf.MoveTowards(_bodyAmount[i], want && seated ? 1f : 0f, step);
            _bodyAmount[i] = amount;
            figure.Motion.SeatRecline = Mathf.SmoothStep(0f, 1f, amount);
            var wander = figure.GetComponent<ResidentWander>();
            if (wander != null)
                wander.Hold = seated && (want || amount > 0f);
            if (!want && amount <= 0f)
                _residents[i] = null;
        }

        private void ReleaseResident(int i)
        {
            var figure = _residents[i];
            _bodyAmount[i] = 0f;
            _residents[i] = null;
            if (figure == null)
                return;
            if (figure.Motion != null)
                figure.Motion.SeatRecline = 0f;
            var wander = figure.GetComponent<ResidentWander>();
            if (wander != null)
                wander.Hold = false;
        }

        private void ApplyBack(int i)
        {
            var q = Quaternion.AngleAxis(_reclineAngle * Mathf.SmoothStep(0f, 1f, _backAmount[i]), _backAxis[i]);
            _backs[i].localRotation = q * _backRot[i];
            _backs[i].localPosition = _hinges[i] + q * (_backPos[i] - _hinges[i]);
        }

        private void ResetBacks()
        {
            if (_backs == null)
                return;
            _reclined = false;
            for (int i = 0; i < _backs.Length; i++)
            {
                ReleaseResident(i);
                if (_backAmount[i] > 0f)
                {
                    _backAmount[i] = 0f;
                    ApplyBack(i);
                }
            }
        }

        /// <summary>앉은 플레이어를 등받이와 같은 경첩으로 눕힘: 눈이 뒤로 내려가고 시선이 위로 (그 사이 마우스로 움직인 만큼은 남음).</summary>
        private void SetLie(float lie)
        {
            float before = Mathf.SmoothStep(0f, 1f, _lie), after = Mathf.SmoothStep(0f, 1f, lie);
            _lie = lie;
            var s = _seatSpots[_seat];
            var eye = new Vector3(s.x, s.y + _seatedEye, s.z);
            var hinge = _hinges[_seat];
            var moved = hinge + Quaternion.AngleAxis(_reclineAngle * after, _backAxis[_seat]) * (eye - hinge);
            float pitch = _player.Pitch + (_lyingPitch - _seatedPitch) * (after - before);
            _player.RideAt(transform, transform.TransformPoint(moved - Vector3.up * _seatedEye), _seatedEye, null, pitch);
        }

        // ---------------- 객석 ----------------

        private int AimSeat()
        {
            if (_aimFrame == Time.frameCount)
                return _aimSeat;
            _aimFrame = Time.frameCount;
            _aimSeat = -1;
            if (_player == null || _seatCollider == null || _seatSpots == null)
                return -1;
            var eye = _player.Eye;
            if (!_seatCollider.Raycast(new Ray(eye.position, eye.forward), out var hit, 4f))
                return -1;
            var local = transform.InverseTransformPoint(hit.point);
            float best = float.MaxValue;
            for (int i = 0; i < _seatSpots.Length; i++)
            {
                var s = _seatSpots[i];
                float d = new Vector2(local.x - s.x, local.z - s.z).sqrMagnitude + 2f * Mathf.Abs(local.y - s.y - 0.5f);
                if (d < best)
                {
                    best = d;
                    _aimSeat = i;
                }
            }
            return _aimSeat;
        }

        private ResidentFigure ResidentOn(int seat)
        {
            // 앉는 판 위 몸통 자리 (경첩에서 돔 축 쪽으로 0.2m)
            var hinge = _hinges[seat];
            var toAxis = new Vector3(_axis.x - hinge.x, 0f, _axis.y - hinge.z).normalized;
            var world = transform.TransformPoint(hinge + toAxis * 0.2f + Vector3.up * 0.2f);
            int n = Physics.OverlapSphereNonAlloc(world, 0.18f, _overlap, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var figure = _overlap[i].GetComponentInParent<ResidentFigure>();
                if (figure != null)
                    return figure;
            }
            return null;
        }

        private void Sit()
        {
            int seat = AimSeat();
            if (_seated || _player == null || seat < 0 || Time.frameCount == _standFrame || ResidentOn(seat) != null)
                return; // 일어나기와 같은 프레임의 F (InteriorMode가 뒤에서 다시 부름)
            var s = _seatSpots[seat];
            _standPoint = _player.transform.position;
            float yaw = s.w + transform.eulerAngles.y;
            var feet = transform.TransformPoint(new Vector3(s.x, s.y, s.z));
            _player.RideAt(transform, feet, _seatedEye, yaw, _seatedPitch);
            _seated = true;
            _seat = seat;
            _lie = 0f;
            if (_seatCollider != null)
                _seatCollider.enabled = false;
            if (_phase == Phase.Idle && _timer > _seatedWait)
                _timer = _seatedWait;
            AudioService.TryPlay(l => l.UiOpen);
        }

        private void StandUp()
        {
            _standFrame = Time.frameCount;
            _seated = false;
            _seat = -1;
            _lie = 0f;
            if (_seatCollider != null)
                _seatCollider.enabled = true;
            _player.RideAt(transform, _standPoint, FirstPersonController.EyeHeight, null, 0f);
            _player.Dismount();
            AudioService.TryPlay(l => l.UiClose);
        }

        private void ReleasePlayer()
        {
            if (!_seated)
                return;
            _seated = false;
            _seat = -1;
            _lie = 0f;
            if (_seatCollider != null)
                _seatCollider.enabled = true;
            if (_player != null)
            {
                _player.RideAt(transform, _standPoint, FirstPersonController.EyeHeight, null, 0f);
                _player.Dismount();
            }
        }

        // ---------------- 안내 ----------------

        private bool InDome()
        {
            if (_seated)
                return true;
            if (_player == null)
                return false;
            var p = transform.InverseTransformPoint(_player.transform.position);
            return new Vector2(p.x - _axis.x, p.z - _axis.y).magnitude < _axis.z + 0.05f && p.y < _curve.x;
        }

        private void UpdateHint()
        {
            string text = null;
            if (InDome())
            {
                switch (_phase)
                {
                    case Phase.Idle:
                        text = Polled && !Active ? "전력이 끊겨 상영을 쉬는 중" : $"다음 상영 약 {Mathf.CeilToInt(Mathf.Max(0f, _timer))}초";
                        break;
                    case Phase.Dim:
                        text = "스텔라돔 상영을 시작합니다";
                        break;
                    case Phase.Stars:
                        text = "별이 하나씩 떠오릅니다";
                        break;
                    case Phase.Constellations:
                    {
                        // 지금 그려지는 별자리를 먼저, 다 그려진 뒤에는 바라보는 별자리
                        float at = _timer / Mathf.Max(_chartSeconds, 0.01f);
                        int c = Mathf.Clamp(Mathf.FloorToInt(at), 0, ChartCount - 1);
                        if (_looking >= 0 && at - c > 0.7f)
                            c = _looking;
                        text = $"{Charts[c].Name}  ·  {Charts[c].Line}";
                        break;
                    }
                    case Phase.Finale:
                        text = "별똥별이 지나고 오로라가 일렁입니다";
                        break;
                    case Phase.Dawn:
                        text = "새벽이 밝아 옵니다";
                        break;
                }
                if (_seated)
                    text = $"{text}   ·   {KeyBindings.Label(GameAction.Interact)}  일어나기";
            }
            ShowHint(text);
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
            var go = new GameObject("StellaHint", typeof(RectTransform));
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
            Place(back.rectTransform, new Vector2(900f, 52f));
            back.color = new Color(0.03f, 0.05f, 0.1f, 0.62f);
            back.raycastTarget = false;
            _hint = new GameObject("Hint", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            _hint.transform.SetParent(go.transform, false);
            Place(_hint.rectTransform, new Vector2(880f, 44f));
            if (Font != null)
                _hint.font = Font;
            _hint.fontSize = 26f;
            _hint.color = new Color(0.92f, 0.96f, 1f);
            _hint.alignment = TextAlignmentOptions.Center;
            _hint.raycastTarget = false;
            _hint.textWrappingMode = TextWrappingModes.NoWrap;
        }

        private static void Place(RectTransform rt, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, 64f);
            rt.sizeDelta = size;
        }
    }
}
