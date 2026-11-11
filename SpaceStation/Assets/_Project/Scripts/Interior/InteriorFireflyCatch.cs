using System.Collections.Generic;
using SpaceStation.Audio;
using SpaceStation.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 바이오스피어 미니게임 "반딧불이 잡기" (사용자 2026-11-11 — 1분 동안 많이 잡기): 뒤 오른쪽 숲 길가 유리병 선반을 보고 F = 병을 들고 시작.
    /// 안이 밤처럼 어두워지고 (방 불빛 · 해 · 환경광 · 하늘을 낮춤) 반딧불이 30마리가 숲 모서리를 떠다니며 깜박임 — 빛날 때만,
    /// 2.2 m 안에서 가운데로 보고 F = 잡음 (가까이 가면 슬금슬금 달아남, 잡힌 자리엔 잠시 뒤 새로 나타남). 60초가 지나면 잡은 마릿수 · 최고 기록 (글자),
    /// 병에서 풀려난 반딧불이가 하늘 호수로 날아오른 뒤 낮으로 돌아옴. 시간 = 실제 시간. 값 = <see cref="Biosphere"/>.
    /// </summary>
    [DefaultExecutionOrder(-320)] // InteriorMode(-300)보다 먼저: 잡는 F를 여기서 먼저 씀
    public sealed class InteriorFireflyCatch : MonoBehaviour
    {
        private enum Phase { Idle, Dusk, Playing, Release, Dawn }

        private const int Count = 30, MaxRelease = 40;
        private const float GameSeconds = 60f, Reach = 2.2f, Aim = 0.3f, FleeDistance = 1.4f, FleeSpeed = 0.7f;
        private const float DuskSeconds = 1.6f, DawnSeconds = 2.4f, ReleaseSeconds = 6f, CatchSeconds = 0.35f;
        private const float Night = 0.14f;
        private static readonly Color Glow = new Color(0.86f, 1f, 0.42f);
        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int CullModeId = Shader.PropertyToID("_CullMode");

        [Tooltip("유리병 선반 (보고 F = 시작)")]
        [SerializeField] private Collider _rack;
        [SerializeField] private Material _glowMaterial;
        [SerializeField] private TMP_FontAsset _font;
        [Tooltip("하늘 면 (밤에 어둡게)")]
        [SerializeField] private Renderer _sky;
        [SerializeField] private Vector3 _zoneCenter;
        [SerializeField] private float _zoneRadius = 2.4f;
        [SerializeField] private float _floor = -1.3f;
        [Tooltip("풀려난 반딧불이가 날아가는 곳 (하늘 호수 위)")]
        [SerializeField] private Vector3 _releaseTarget;
        [SerializeField] private Vector3 _boardPosition;
        [SerializeField] private float _boardYaw;
        [Tooltip("장식 빛 점 (숲 반딧불 · 병 빛 — 밤 동안 꺼서 잡을 수 있는 반딧불이만 빛나게)")]
        [SerializeField] private Renderer _ambientDots;

        private struct Fly
        {
            public Vector3 Home, Pos, Drift;
            public float Phase, Speed, BlinkOn, BlinkOff, BlinkT, Light;
            public bool Alive;
            public float Respawn, CatchT;
            public Vector3 CatchFrom;
        }

        private static int _best = -1;
        private static int _active;

        /// <summary>어느 방에서든 반딧불이 잡기가 진행 중 (해 지기 ~ 해 뜨기) — 다른 체험 화면 안내가 양보함.</summary>
        public static bool Active => _active > 0;

        private Phase _phase = Phase.Idle;
        private float _phaseT;
        private float _timeLeft;
        private int _caught;
        private bool _record;
        private readonly Fly[] _flies = new Fly[Count];
        private Vector3[] _release;
        private Vector3[] _releaseFrom;
        private float[] _releaseSeed;
        private System.Random _random = new System.Random(7);
        private ParticleSystem _ps;
        private ParticleSystem.Particle[] _particles;
        private Transform _eye;
        private float _night = 1f;                 // 1 = 낮 (그대로), Night = 한밤
        private readonly List<Light> _lights = new List<Light>();
        private float[] _lightBase, _lightSet;
        private Light _sun;
        private float _sunBase, _sunSet;
        private Color _ambientBase, _ambientSet;
        private MaterialPropertyBlock _skyBlock;
        private TextMeshPro[] _boards;
        private Canvas _canvas;
        private TextMeshProUGUI _hint;
        private string _hintShown;
        private string _notice;
        private float _noticeTimer;

        public bool Playing => _phase == Phase.Dusk || _phase == Phase.Playing;
        public int Caught => _caught;
        public static int Best => _best;
        public float NightLevel => _night;

        public void Configure(Collider rack, Material glow, TMP_FontAsset font, Renderer sky, Vector3 zoneCenter, float zoneRadius, float floor,
            Vector3 releaseTarget, Vector3 boardPosition, float boardYaw, Renderer ambientDots)
        {
            _ambientDots = ambientDots;
            _rack = rack;
            _glowMaterial = glow;
            _font = font;
            _sky = sky;
            _zoneCenter = zoneCenter;
            _zoneRadius = zoneRadius;
            _floor = floor;
            _releaseTarget = releaseTarget;
            _boardPosition = boardPosition;
            _boardYaw = boardYaw;
        }

        private void Awake()
        {
            if (_rack != null)
            {
                var use = _rack.gameObject.AddComponent<InteriorInteractable>();
                use.Tap = true;
                use.Prompt = () => "유리병 들고 반딧불이 잡기  <size=80%>(1분)</size>";
                use.Blocked = () => _phase != Phase.Idle ? "반딧불이를 잡는 중이에요" : null;
                use.Completed = Begin;
            }
            _skyBlock = new MaterialPropertyBlock();
            BuildParticles();
            BuildBoards();
            UpdateBoards();
        }

        private void OnDisable()
        {
            // 방을 나가면 바로 끝 · 낮으로
            SetPhase(Phase.Idle);
            ApplyNight(1f);
            if (_ps != null)
                _ps.Clear();
            ShowHint(null);
        }

        private void OnDestroy()
        {
            if (_canvas != null)
                Destroy(_canvas.gameObject);
        }

        // ---------------- 진행 ----------------

        private void Begin()
        {
            if (_phase != Phase.Idle)
                return;
            _random = new System.Random(System.Environment.TickCount);
            CollectLights();
            for (int i = 0; i < Count; i++)
                Spawn(ref _flies[i], true);
            _caught = 0;
            _timeLeft = GameSeconds;
            SetPhase(Phase.Dusk);
            Notice("해가 져요… 빛나는 반딧불이를 가운데로 보고 F", DuskSeconds + 3f);
            AudioService.TryPlay(l => l.UiOpen);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (_eye == null && Camera.main != null)
                _eye = Camera.main.transform;
            if (_noticeTimer > 0f)
                _noticeTimer -= dt;
            _phaseT += dt;
            switch (_phase)
            {
                case Phase.Idle:
                    break;
                case Phase.Dusk:
                    ApplyNight(Mathf.Lerp(1f, Night, Smooth(_phaseT / DuskSeconds)));
                    MoveFlies(dt);
                    if (_phaseT >= DuskSeconds)
                        SetPhase(Phase.Playing);
                    break;
                case Phase.Playing:
                    _timeLeft -= dt;
                    MoveFlies(dt);
                    if (KeyBindings.WasPressed(GameAction.Interact))
                        TryCatch();
                    if (_timeLeft <= 0f)
                        EndGame();
                    break;
                case Phase.Release:
                    MoveFlies(dt);
                    if (_phaseT >= ReleaseSeconds)
                        SetPhase(Phase.Dawn);
                    break;
                case Phase.Dawn:
                    ApplyNight(Mathf.Lerp(Night, 1f, Smooth(_phaseT / DawnSeconds)));
                    if (_phaseT >= DawnSeconds)
                    {
                        SetPhase(Phase.Idle);
                        ApplyNight(1f);
                    }
                    break;
            }
            DrawParticles();
            UpdateHint();
        }

        private void SetPhase(Phase phase)
        {
            bool was = _phase != Phase.Idle, now = phase != Phase.Idle;
            if (was != now)
                _active = Mathf.Max(0, _active + (now ? 1 : -1));
            _phase = phase;
            _phaseT = 0f;
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private void EndGame()
        {
            _record = _best < 0 || _caught > _best;
            if (_record)
                _best = _caught;
            Notice($"반딧불이 {_caught}마리!" + (_record ? "  ·  최고 기록!" : $"  ·  최고 {_best}마리") + "  ·  하늘 호수로 풀어 줘요", ReleaseSeconds + 1f);
            int n = Mathf.Min(_caught, MaxRelease);
            _release = new Vector3[n];
            _releaseFrom = new Vector3[n];
            _releaseSeed = new float[n];
            var jar = JarPoint();
            for (int i = 0; i < n; i++)
            {
                _releaseFrom[i] = jar + new Vector3(Rand(-0.08f, 0.08f), Rand(-0.05f, 0.05f), Rand(-0.08f, 0.08f));
                _release[i] = _releaseFrom[i];
                _releaseSeed[i] = Rand(0f, 100f);
            }
            for (int i = 0; i < Count; i++)
                _flies[i].Alive = false;   // 남은 반딧불이는 숲으로 꺼져 감 (아래 Light가 줄어듦)
            SetPhase(Phase.Release);
            UpdateBoards();
            AudioService.TryPlay(l => _record ? l.UiOpen : l.UiClose);
        }

        // ---------------- 반딧불이 ----------------

        private float Rand(float a, float b) => a + (float)_random.NextDouble() * (b - a);

        private void Spawn(ref Fly f, bool first)
        {
            float ang = Rand(0f, Mathf.PI * 2f);
            float rad = Mathf.Sqrt(Rand(0f, 1f)) * _zoneRadius;
            f.Home = ClampZone(_zoneCenter + new Vector3(Mathf.Cos(ang) * rad, Rand(0.45f, 2.1f), Mathf.Sin(ang) * rad));
            f.Pos = f.Home;
            f.Drift = Vector3.zero;
            f.Phase = Rand(0f, 100f);
            f.Speed = Rand(0.25f, 0.55f);
            f.BlinkOn = Rand(1.6f, 3.2f);
            f.BlinkOff = Rand(0.5f, 1.1f);
            f.BlinkT = first ? Rand(0f, f.BlinkOn + f.BlinkOff) : 0f;
            f.Light = first ? 0f : 0f;
            f.Alive = true;
            f.Respawn = 0f;
            f.CatchT = -1f;
        }

        /// <summary>숲 모서리 안 (가운데 반지름 · 방 하늘 면 0.4 m 안 · 높이 바닥 0.35 ~ 2.4 m).</summary>
        private Vector3 ClampZone(Vector3 p)
        {
            var d = new Vector3(p.x - _zoneCenter.x, 0f, p.z - _zoneCenter.z);
            if (d.magnitude > _zoneRadius)
                d = d.normalized * _zoneRadius;
            float x = Mathf.Clamp(_zoneCenter.x + d.x, -3.55f, 19.55f);
            float z = Mathf.Clamp(_zoneCenter.z + d.z, -3.55f, 19.55f);
            return new Vector3(x, Mathf.Clamp(p.y, _floor + 0.35f, _floor + 2.4f), z);
        }

        private Vector3 PlayerLocal() => _eye != null ? transform.InverseTransformPoint(_eye.position) : new Vector3(0f, -999f, 0f);

        private void MoveFlies(float dt)
        {
            var player = PlayerLocal();
            float t = Time.unscaledTime;
            for (int i = 0; i < Count; i++)
            {
                ref var f = ref _flies[i];
                if (f.CatchT >= 0f)
                {
                    f.CatchT += dt / CatchSeconds;   // 병으로 빨려 들어감
                    if (f.CatchT >= 1f)
                    {
                        f.CatchT = -1f;
                        f.Respawn = Rand(2.5f, 4f);
                    }
                    continue;
                }
                if (!f.Alive)
                {
                    f.Light = Mathf.MoveTowards(f.Light, 0f, dt / 1.2f);
                    if (_phase == Phase.Playing && f.Respawn > 0f)
                    {
                        f.Respawn -= dt;
                        if (f.Respawn <= 0f)
                            Spawn(ref f, false);
                    }
                    continue;
                }
                // 깜박임: 켜짐 · 꺼짐을 번갈아 (밝기가 부드럽게 오르내림)
                f.BlinkT += dt;
                float cycle = f.BlinkOn + f.BlinkOff;
                if (f.BlinkT > cycle)
                    f.BlinkT -= cycle;
                float want = f.BlinkT < f.BlinkOn ? 1f : 0f;
                f.Light = Mathf.MoveTowards(f.Light, want, dt / 0.35f);
                // 떠돌기: 집 둘레를 천천히 (사인 합) + 집도 조금씩 옮겨 감
                float p = f.Phase;
                var wander = new Vector3(Mathf.Sin(t * 0.37f * f.Speed * 2f + p) + 0.5f * Mathf.Sin(t * 0.91f + p * 1.7f),
                                         0.6f * Mathf.Sin(t * 0.53f + p * 0.6f),
                                         Mathf.Cos(t * 0.29f * f.Speed * 2f + p * 1.3f) + 0.5f * Mathf.Cos(t * 0.77f + p * 0.4f)) * 0.55f;
                f.Home = ClampZone(f.Home + new Vector3(Mathf.Sin(t * 0.11f + p), 0f, Mathf.Cos(t * 0.13f + p)) * (0.08f * dt));
                // 가까이 오면 달아남 (가로로)
                var away = new Vector3(f.Pos.x - player.x, 0f, f.Pos.z - player.z);
                if (_phase == Phase.Playing && away.magnitude < FleeDistance && away.sqrMagnitude > 1e-4f)
                    f.Drift += away.normalized * (FleeSpeed * dt);
                f.Drift *= Mathf.Exp(-dt * 0.35f);
                var target = ClampZone(f.Home + wander + f.Drift);
                f.Pos = Vector3.Lerp(f.Pos, target, 1f - Mathf.Exp(-dt * 2.5f));
            }
            if (_phase == Phase.Release && _release != null)
            {
                for (int i = 0; i < _release.Length; i++)
                {
                    float k = Smooth(Mathf.Clamp01((_phaseT - i * 0.04f) / (ReleaseSeconds * 0.8f)));
                    float s = _releaseSeed[i];
                    var swirl = new Vector3(Mathf.Cos(s + _phaseT * 1.6f), 0f, Mathf.Sin(s + _phaseT * 1.6f)) * (0.6f * Mathf.Sin(k * Mathf.PI) + 0.4f * k);
                    _release[i] = Vector3.Lerp(_releaseFrom[i], _releaseTarget, k) + swirl + Vector3.up * (Mathf.Sin(k * Mathf.PI) * 0.8f);
                }
            }
        }

        /// <summary>잡는 병 자리 (눈 앞 아래 오른쪽).</summary>
        private Vector3 JarPoint()
        {
            if (_eye == null)
                return _zoneCenter + Vector3.up;
            return transform.InverseTransformPoint(_eye.position + _eye.forward * 0.45f - _eye.up * 0.28f + _eye.right * 0.2f);
        }

        /// <summary>시선 가운데에서 가장 가까운, 손 닿는 (2.2 m) 빛나는 반딧불이 하나.</summary>
        private void TryCatch()
        {
            if (_eye == null)
                return;
            int best = -1;
            float bestMiss = float.MaxValue;
            for (int i = 0; i < Count; i++)
            {
                ref var f = ref _flies[i];
                if (!f.Alive || f.CatchT >= 0f || f.Light < 0.5f)
                    continue;
                var to = transform.TransformPoint(f.Pos) - _eye.position;
                float along = Vector3.Dot(to, _eye.forward);
                if (along <= 0.05f || to.magnitude > Reach)
                    continue;
                float miss = (to - _eye.forward * along).magnitude;
                if (miss <= Aim + 0.06f * along && miss < bestMiss)
                {
                    bestMiss = miss;
                    best = i;
                }
            }
            if (best < 0)
            {
                AudioService.TryPlay(l => l.UiHover);
                return;
            }
            ref var c = ref _flies[best];
            c.Alive = false;
            c.CatchT = 0f;
            c.CatchFrom = c.Pos;
            _caught++;
            AudioService.TryPlay(l => l.UiOpen);
        }

        // ---------------- 그리기 (입자 하나에 직접 자리 · 밝기) ----------------

        private void BuildParticles()
        {
            if (_glowMaterial == null)
                return;
            var go = new GameObject("Fireflies");
            go.transform.SetParent(transform, false);
            _ps = go.AddComponent<ParticleSystem>();
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = Count + MaxRelease;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startLifetime = 1e6f;
            main.startSpeed = 0f;
            var em = _ps.emission;
            em.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = _glowMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            _particles = new ParticleSystem.Particle[Count + MaxRelease];
            _ps.Play();   // 뿜지 않음 (뿜기 끔) — 자리 · 밝기는 매 프레임 SetParticles
        }

        private void DrawParticles()
        {
            if (_ps == null)
                return;
            if (_phase == Phase.Idle)
            {
                if (_ps.particleCount > 0)
                    _ps.Clear();
                return;
            }
            int n = 0;
            Vector3 jar = JarPoint();
            float t = Time.unscaledTime;
            for (int i = 0; i < Count; i++)
            {
                var f = _flies[i];
                Vector3 at;
                float light, size = 0.075f;
                if (f.CatchT >= 0f)
                {
                    float k = Smooth(f.CatchT);
                    at = Vector3.Lerp(f.CatchFrom, jar, k);
                    light = 1f;
                    size *= 1f - 0.6f * k;
                }
                else
                {
                    at = f.Pos;
                    light = f.Light;
                }
                if (light < 0.02f)
                    continue;
                AddParticle(ref n, at, size * (0.7f + 0.3f * light), light * (0.85f + 0.15f * Mathf.Sin(t * 9f + f.Phase)));
            }
            if (_phase == Phase.Release && _release != null)
            {
                float fade = 1f - Smooth((_phaseT - ReleaseSeconds * 0.8f) / (ReleaseSeconds * 0.2f));
                for (int i = 0; i < _release.Length; i++)
                    AddParticle(ref n, _release[i], 0.14f, fade * (0.85f + 0.15f * Mathf.Sin(t * 7f + _releaseSeed[i])));   // 멀리 올라가도 보이게 (0.08은 하늘 호수 쪽에서 점처럼 작았음)
            }
            _ps.SetParticles(_particles, n);
        }

        private void AddParticle(ref int n, Vector3 at, float size, float light)
        {
            if (n >= _particles.Length)
                return;
            var p = new ParticleSystem.Particle
            {
                position = at,
                startSize = size,
                remainingLifetime = 1e5f,
                startLifetime = 1e6f,
                startColor = new Color(Glow.r * light, Glow.g * light, Glow.b * light, Mathf.Clamp01(light)),   // 가산 재질: 밝기 = 색 (알파를 안 볼 수도 있어 색도 줄임)
            };
            _particles[n++] = p;
        }

        // ---------------- 밤 (방 불빛 · 해 · 환경광 · 하늘) ----------------

        private void CollectLights()
        {
            if (_lightBase != null)
                return;
            var root = transform.Find("Lights");
            if (root != null)
                _lights.AddRange(root.GetComponentsInChildren<Light>(true));
            _lightBase = new float[_lights.Count];
            _lightSet = new float[_lights.Count];
            for (int i = 0; i < _lights.Count; i++)
                _lightBase[i] = _lightSet[i] = _lights[i].intensity;
        }

        private void ApplyNight(float k)
        {
            if (_lightBase != null)
            {
                for (int i = 0; i < _lights.Count; i++)
                {
                    var l = _lights[i];
                    if (l == null)
                        continue;
                    if (!Mathf.Approximately(l.intensity, _lightSet[i]))   // 다른 것 (정전 · 경보)이 바꿨으면 그 값을 새 기준으로
                        _lightBase[i] = l.intensity / Mathf.Max(_night, 0.01f);
                    _lightSet[i] = _lightBase[i] * k;
                    l.intensity = _lightSet[i];
                }
            }
            if (_sun == null)
                _sun = RenderSettings.sun;
            if (_sun != null)
            {
                if (Mathf.Approximately(_night, 1f) || !Mathf.Approximately(_sun.intensity, _sunSet))
                    _sunBase = _sun.intensity / Mathf.Max(_night, 0.01f);
                _sunSet = _sunBase * k;
                _sun.intensity = _sunSet;
            }
            if (Mathf.Approximately(_night, 1f) || RenderSettings.ambientLight != _ambientSet)
                _ambientBase = RenderSettings.ambientLight * (1f / Mathf.Max(_night, 0.01f));
            _ambientSet = _ambientBase * Mathf.Lerp(0.3f, 1f, k);
            RenderSettings.ambientLight = _ambientSet;
            if (_sky != null)
            {
                // 하늘 = 창 셰이더, 칸마다 블록 (InteriorExteriorView가 바깥 화면 텍스처를 넣음) → 그 블록을 읽어 투과색만 바꿈
                var mats = _sky.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || !mats[i].HasProperty(TintId))
                        continue;
                    _sky.GetPropertyBlock(_skyBlock, i);
                    _skyBlock.SetColor(TintId, mats[i].GetColor(TintId) * Mathf.Lerp(0.12f, 1f, k));
                    _sky.SetPropertyBlock(_skyBlock, i);
                }
            }
            if (_ambientDots != null)
            {
                float dots = Mathf.InverseLerp(Night, 1f, k);
                var dotMats = _ambientDots.sharedMaterials;
                for (int i = 0; i < dotMats.Length; i++)
                {
                    if (dotMats[i] == null || !dotMats[i].name.Contains("InteriorLight"))
                        continue;
                    _ambientDots.GetPropertyBlock(_skyBlock, i);
                    if (dotMats[i].HasProperty(EmissionId))
                        _skyBlock.SetColor(EmissionId, dotMats[i].GetColor(EmissionId) * dots);
                    if (dotMats[i].HasProperty(BaseColorId))
                        _skyBlock.SetColor(BaseColorId, dotMats[i].GetColor(BaseColorId) * Mathf.Lerp(0.15f, 1f, dots));
                    _ambientDots.SetPropertyBlock(_skyBlock, i);
                }
            }
            _night = k;
        }

        // ---------------- 글자 ----------------

        private void BuildBoards()
        {
            if (_font == null)
                return;
            _boards = new TextMeshPro[2];
            for (int k = 0; k < 2; k++)
            {
                var go = new GameObject("FireflyBoard" + k);
                go.transform.SetParent(transform, false);
                go.transform.localPosition = _boardPosition;
                go.transform.localRotation = Quaternion.Euler(0f, _boardYaw + 180f * k, 0f);
                var text = go.AddComponent<TextMeshPro>();
                text.font = _font;
                text.enableAutoSizing = true;
                text.fontSizeMin = 0.5f;
                text.fontSizeMax = 1.1f;
                text.fontSize = 1.1f;
                text.color = new Color(1f, 0.96f, 0.84f);
                text.alignment = TextAlignmentOptions.Center;
                text.rectTransform.sizeDelta = new Vector2(1.3f, 0.55f);
                text.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (text.fontMaterial.HasProperty(CullModeId))   // 앞뒤 두 장: 뒷면 안 그림 (뒤집힌 글자가 겹쳐 보였음)
                    text.fontMaterial.SetFloat(CullModeId, (float)UnityEngine.Rendering.CullMode.Back);
                _boards[k] = text;
            }
        }

        private void UpdateBoards()
        {
            if (_boards == null)
                return;
            string best = _best < 0 ? "최고 기록 없음" : $"최고 {_best}마리";
            foreach (var b in _boards)
                b.SetText($"반딧불이 잡기 (1분)\n<size=65%>선반의 유리병 F · {best}</size>");
        }

        private void Notice(string text, float seconds)
        {
            _notice = text;
            _noticeTimer = seconds;
        }

        private void UpdateHint()
        {
            string text = null;
            if (_phase == Phase.Dusk || _phase == Phase.Playing)
            {
                text = $"반딧불이 {_caught}마리  ·  남은 시간 {Mathf.CeilToInt(Mathf.Max(0f, _timeLeft))}초\n"
                     + $"<size=80%>빛날 때 가운데로 보고 {KeyBindings.Label(GameAction.Interact)} (2 m 안) · 가까이 가면 슬금슬금 달아나요</size>";
            }
            if (_noticeTimer > 0f && !string.IsNullOrEmpty(_notice))
                text = text == null ? _notice : _notice + "\n<size=80%>" + text + "</size>";
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
            var go = new GameObject("FireflyHint", typeof(RectTransform));
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
            Place(back.rectTransform, new Vector2(1060f, 104f));
            back.color = new Color(0.03f, 0.05f, 0.1f, 0.62f);
            back.raycastTarget = false;
            _hint = new GameObject("Hint", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            _hint.transform.SetParent(go.transform, false);
            Place(_hint.rectTransform, new Vector2(1030f, 96f));
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
            rt.anchoredPosition = new Vector2(0f, 92f);
            rt.sizeDelta = size;
        }
    }
}
