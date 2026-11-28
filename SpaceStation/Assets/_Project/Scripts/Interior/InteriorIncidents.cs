using System;
using System.Collections.Generic;
using SpaceStation.Audio;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 11-17 ④ 내부 사고 대응 (BALANCE 31번, 사용자 결정 2026-10-11). 플레이어가 안에 있을 때만, 지금 방 · 옆방에서 하나씩.
    /// - 첫 사고 90초 뒤, 끝난 뒤 3~5분마다(실시간). 처리 시간 90초 — 지나거나 밖으로 나가면 놓침
    ///   → 상한 있는 짧은 손실 후 정비 인력이 처리(<see cref="StationSimulation.MissIncident"/>), 처리하면 만족도 조금(<see cref="StationSimulation.ResolveIncident"/>)
    /// - 배관 누출: 이음에서 김 → 밸브 바퀴를 F 길게(돌아감) / 차단기: 방 조명이 깜박임 → 함 문 열기 → 경고등 켜진 스위치만 올리기(틀리면 스파크 후 처음부터)
    ///   / 화재: 배전함 위 불꽃 · 연기 → 벽의 소화기를 들고 불에 F 길게(분사) → 그을음이 남음 / 환기: 경고등 깜박 · 먼지 → 필터 카트리지 교체(뽑았다 끼움)
    /// - 표시: 소품 위 경고 말풍선(남은 시간) · 알림 · 경고음 · 패드 홀로그램 빨간 표시(<see cref="CountIn"/>). 사고 중에는 새 주민 요청이 쉼.
    /// 모델: BlenderWork/InteriorProps/incident_builder.py. 내부를 다시 만들면 진행 중 사고는 손실 없이 사라짐.
    /// </summary>
    public sealed partial class InteriorIncidents : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("배관 누출 (자식 LeakPipe · ValveWheel)")]
            public GameObject Leak;
            [Tooltip("차단기 함 (자식 BreakerBox · BreakerDoor · Switch0~5 · Led0~5)")]
            public GameObject Breaker;
            [Tooltip("불이 나는 배전함 (자식 FireBox · Soot)")]
            public GameObject FireBox;
            [Tooltip("소화기 (자식 ExtBracket · ExtBottle)")]
            public GameObject Extinguisher;
            [Tooltip("공기 정화 필터함 (자식 FilterUnit · FilterLed · Cartridge)")]
            public GameObject Filter;
            [Tooltip("CONNECT A6 2단계 장식 조명 꺼짐: 바닥에 세우는 조명 분전함 (자식 IncDecoBox · DecoLed)")]
            public GameObject DecoBox;
            [Tooltip("CONNECT A6 2단계 놀이기구 멈춤: 바닥에 세우는 운행 조작반 (자식 IncRideConsole · Switch0~5 · Led0~5, 레버 = 스위치 누르는 시간)")]
            public GameObject RideConsole;
            [Tooltip("CONNECT A6 2단계 놀이 소품 흩어짐: 바구니 · 소품 한 벌 (자식 ToyBasket · ToyBall · ToyStar · ToyRing, 줍기 · 넣기 = 소화기 들기 시간)")]
            public GameObject Toys;
            [Tooltip("CONNECT A6 3단계 물길 막힘: 물가 물꼬 판 (자식 IncSluice · Wheel0~2 · Gate0~2 · Notch00~24, 바퀴 = 스위치 누르는 시간)")]
            public GameObject Sluice;
            [Tooltip("CONNECT A6 3단계 등불 순서: 정원 등불 (자식 IncLantern · LampGlass)")]
            public GameObject Lantern;
            [Tooltip("CONNECT A6 4단계 별빛 등불: 별빛 등불 (자식 IncStarLantern · StarGlass)")]
            public GameObject StarLantern;
            [Tooltip("CONNECT A6 3단계 덩굴 웃자람: 덩굴 덤불 (자식 IncVines, 쳐내기 = 불 끄기 시간)")]
            public GameObject Vines;
            [Tooltip("CONNECT A6 4단계 신호 흐트러짐: 다이얼 조작반 (자식 IncSignalConsole · Screen · Dial0~1 · Tick00~14)")]
            public GameObject SignalConsole;
            [Tooltip("CONNECT A6 1단계 홀로 패널 오류: 벽 홀로 판 (자식 IncHoloPanel · Cell0~5 · Warn0~5, 칸 = 스위치 누르는 시간)")]
            public GameObject HoloPanel;
            [Tooltip("CONNECT A6 1단계 냉각 코어 과열: 벽 냉각 장치 (자식 IncCoolantCore · Cartridge · GaugeFill · CoolLed, 갈기 = 필터 시간)")]
            public GameObject CoolantCore;
            [Tooltip("CONNECT A6 1단계 정비 로봇 멈춤: 바닥 로봇 팔 (관절 계층 IncRobotArm > ArmTurret > ArmUpper > ArmFore > ArmHead)")]
            public GameObject RobotArm;
            [Tooltip("CONNECT A6 1단계 소품의 발광 유리 (M_IncGlow 칸을 이것으로 바꿔 끼워 물들임 — 반투명 · 발광 URP Lit)")]
            public Material Glow;
            [Tooltip("정비 로봇 다시 켜는 시간(초)")]
            public float BotHold = 2f;
            [Tooltip("1단계 벽 소품 가운데 높이 (바닥에서, m): 홀로 패널 · 냉각 코어")]
            public float HoloHeight = 1.45f;
            public float CoolantHeight = 1.35f;
            [Tooltip("경고 말풍선 재질 (주민 요청과 같은 것)")]
            public Material Bubble;
            [Tooltip("F 누르는 시간(초): 밸브 · 불 끄기 · 필터 · 소화기 들기 · 함 문 · 스위치")]
            public float ValveHold = 2f;
            public float FireHold = 2f;
            public float FilterHold = 1.8f;
            public float PickupHold = 0.4f;
            public float DoorHold = 0.3f;
            public float SwitchHold = 0.15f;
            [Tooltip("분전함 하나 고치는 시간(초)")]
            public float DecoHold = 1.5f;
            [Tooltip("소품 가운데 높이 (바닥에서, m): 누출 · 차단기 · 화재 · 필터 · 소화기")]
            public float LeakHeight = 1.2f;
            public float BreakerHeight = 1.4f;
            public float FireHeight = 1.3f;
            public float FilterHeight = 1.6f;
            public float ExtinguisherHeight = 1.0f;
            [Tooltip("경고 말풍선이 보이는 거리 (m)")]
            public float BubbleRange = 25f;
        }

        private sealed partial class Incident
        {
            public IncidentKind Kind;
            public ModuleInstance Room;
            public ResourceType Resource;
            public float Age;
            public bool Done;
            public float DoneAge;
            public GameObject Prop;
            public SpeechBubble Bubble;
            public int ShownSeconds = -1;
            /// <summary>11-18 튜토리얼 사고: 제한 시간 없음 · 나가도 손해 없음.</summary>
            public bool Tutorial;
            public readonly List<GameObject> Extra = new List<GameObject>();
            // 사운드 개편 4-3 (2026-11-25): 계속 나는 소리 (누출 쉬익 · 불 타닥 — 지금 방일 때만) · F를 누르는 동안 손 동작과 맞춘 소리 (밸브 · 분사 · 필터)
            public ObjectLoop Loop;
            public HeldSound Held;
            public bool WasHolding;
            // 누출
            public Transform Wheel;
            public Quaternion WheelRest;
            public ParticleSystem Plume;
            // 차단기
            public Transform Door;
            public Quaternion DoorRest;
            public float DoorOpen, DoorTarget;
            public readonly Transform[] Levers = new Transform[InteriorIncidentRules.Switches];
            public readonly Quaternion[] LeverRest = new Quaternion[InteriorIncidentRules.Switches];
            public readonly Renderer[] Leds = new Renderer[InteriorIncidentRules.Switches];
            public bool[] Pattern, Lit;
            public readonly List<(Light Light, float Intensity)> RoomLights = new List<(Light, float)>();
            // 화재
            public ParticleSystem Flames, Smoke;
            public Light Glow;
            public GameObject Soot, Bottle;
            public bool Carrying;
            public float NextSpray;
            // 필터
            public Transform Cartridge;
            public Vector3 CartridgeRest;
            public Renderer FilterLed;
            public bool Holding;
            public float HoldProgress;
        }

        private static readonly Color Warn = new Color(1f, 0.42f, 0.18f);
        private static readonly Color LedRed = new Color(1f, 0.12f, 0.08f);
        private static readonly Color LedGreen = new Color(0.2f, 1f, 0.35f);
        private static readonly Color FilterDirty = new Color(0.45f, 0.36f, 0.22f);
        private static readonly Color FilterClean = new Color(0.94f, 0.95f, 0.92f);
        private const float LeverAngle = 28f;
        private const float DoorAngle = 105f;

        private StationController _station;
        private InteriorBuilder _builder;
        private Settings _s;
        private TMP_FontAsset _font;
        private Transform _eye;
        private Material _smoke, _spark;
        private InteriorLayout _layout;
        private float _timer;
        private IncidentKind? _last;
        private Incident _active;
        private bool _quitting;
        private readonly System.Random _rng = new System.Random();
        private readonly List<ModuleInstance> _neighbors = new List<ModuleInstance>();
        private readonly List<(Vector3 Position, Vector3 Normal)> _walls = new List<(Vector3, Vector3)>();
        private MaterialPropertyBlock _block;

        /// <summary>플레이어가 있는 방 (사고는 이 방 · 옆방에서만 생김).</summary>
        public ModuleInstance CurrentRoom { get; set; }
        /// <summary>알림 (글, 실패 · 경고 여부).</summary>
        public event Action<string, bool> Message;
        /// <summary>처리 중인 사고가 있음 (새 주민 요청을 쉼).</summary>
        public bool Active => _active != null && !_active.Done;
        /// <summary>11-18 튜토리얼 중: 저절로 생기는 사고를 쉼 (타이머도 멈춤 — 끝나면 처음 간격부터).</summary>
        public bool TutorialHold { get; set; }
        /// <summary>11-18: 사고 하나를 해결함 (튜토리얼 진행용).</summary>
        public event Action Resolved;
        /// <summary>사고가 끝남 (제목, 해결했는지 — 놓치면 false). 플레이 기록(CONNECT_1121 V3 ③)이 센다.</summary>
        public static event Action<string, bool> IncidentFinished;

        private StationSimulation Sim => _station.Simulation;

        public static InteriorIncidents Create(Transform parent, StationController station, InteriorBuilder builder, Settings settings,
            TMP_FontAsset font, Transform eye, Material smoke, Material spark)
        {
            var go = new GameObject("InteriorIncidents");
            go.transform.SetParent(parent, false);
            var r = go.AddComponent<InteriorIncidents>();
            r._station = station;
            r._builder = builder;
            r._s = settings ?? new Settings();
            r._font = font;
            r._eye = eye;
            r._smoke = smoke;
            r._spark = spark;
            r._block = new MaterialPropertyBlock();
            r._timer = station.Simulation.Balance.IncidentFirstDelay;
            return r;
        }

        /// <summary>내부를 (다시) 만든 직후 — 방 소품은 이미 지워졌으므로 진행 중 사고는 손실 없이 정리.</summary>
        public void Rebuild(InteriorLayout layout)
        {
            if (_active != null)
                End(_active, rebuilt: true);
            _active = null;
            _layout = layout;
        }

        /// <summary>
        /// 밖으로 나갈 때 (InteriorMode가 지우기 전에): 처리하지 않은 사고는 놓친 것으로.
        /// </summary>
        /// <returns>놓친 사고 알림 (없으면 null) — 밖 상태 표시줄로 보냄.</returns>
        public string LeaveInterior()
        {
            string text = null;
            if (_active != null && !_active.Done)
            {
                if (!_active.Tutorial) // 튜토리얼 사고는 손해 없이 정리 (다시 들어가면 새로 생김)
                    text = Miss(_active, left: true);
                End(_active, rebuilt: false);
            }
            _active = null;
            return text;
        }

        private void OnApplicationQuit() => _quitting = true;

        private void OnDestroy()
        {
            if (_active != null && !_quitting)
                RestoreLights(_active);
        }

        /// <summary>11-17 ④ 패드 홀로그램 표시용: 이 방의 진행 중 사고 수 (0 · 1).</summary>
        public int CountIn(ModuleInstance module) => _active != null && !_active.Done && _active.Room == module ? 1 : 0;

        /// <summary>확인용: 지금 바로 사고 하나 (종류 지정 가능).</summary>
        public bool ForceIncident(IncidentKind? kind = null)
        {
            if (_active != null)
                return false;
            return TrySpawn(kind);
        }

        /// <summary>11-18 튜토리얼 사고: 지금 방에 화재(안 되면 아무 종류), 제한 시간 · 손해 없음. 성공하면 true.</summary>
        public bool ForceTutorialIncident()
        {
            if (_active != null)
                return false;
            return TrySpawn(IncidentKind.Fire, tutorial: true) || TrySpawn(null, tutorial: true);
        }

        /// <summary>11-18 화면 표시용: 처리 중인 사고 (남은 초 — 튜토리얼 사고는 -1). 없으면 false.</summary>
        public bool TryGetActive(out string title, out ModuleInstance room, out int secondsLeft)
        {
            if (_active == null || _active.Done)
            {
                title = null;
                room = null;
                secondsLeft = 0;
                return false;
            }
            title = Title(_active);
            room = _active.Room;
            secondsLeft = _active.Tutorial ? -1 : Mathf.Max(0, Mathf.CeilToInt(_active.Window - _active.Age));
            return true;
        }

        private void Update()
        {
            if (_layout == null || _eye == null)
                return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.25f);
            var b = Sim.Balance;
            if (_active == null)
            {
                if (TutorialHold)
                    return;
                _timer -= dt;
                if (_timer <= 0f)
                    _timer = TrySpawn(null) ? 0f : 20f; // 못 만들었으면 잠시 뒤 다시 (간격은 사고가 끝난 뒤 정함)
                return;
            }
            var a = _active;
            a.Age += dt;
            if (a.Done)
            {
                if (a.Age - a.DoneAge > 2.5f)
                {
                    End(a, rebuilt: false);
                    _active = null;
                    _timer = ResidentRequestRules.NextDelay(b.IncidentIntervalMin, b.IncidentIntervalMax, _rng.NextDouble());
                }
                else
                {
                    PlaceBubble(a);
                    AnimateDoor(a, dt);
                    AnimateStageDone(a, dt); // CONNECT A6 (분전함 내려감 등)
                }
                return;
            }
            if (a.Tutorial)
            {
                PlaceBubble(a);
                Animate(a, dt);
                return;
            }
            if (a.Age > a.Window)
            {
                // CONNECT A5 결과 단계 (사용자 결정 2026-11-28): 나쁜 이웃 옆에서만 그 이웃 방으로 한 번 번짐 (손상 통제실이 옆이면 안 번짐)
                var spreadTo = SpreadTarget(a, out bool blocked, out _);
                Miss(a, left: false, spreadTo, blocked);
                var kind = a.Kind;
                string from = RoomName(a.Room);
                End(a, rebuilt: false);
                _active = null;
                _timer = ResidentRequestRules.NextDelay(b.IncidentIntervalMin, b.IncidentIntervalMax, _rng.NextDouble());
                if (spreadTo != null)
                    SpawnSpread(kind, spreadTo, from);
                return;
            }
            int left = Mathf.CeilToInt(a.Window - a.Age);
            if (left != a.ShownSeconds && a.Bubble != null)
            {
                a.ShownSeconds = left;
                a.Bubble.Set(BubbleText(a, left), Warn);
            }
            PlaceBubble(a);
            Animate(a, dt);
        }

        // ---------------- 만들기 ----------------

        private bool TrySpawn(IncidentKind? forced, bool tutorial = false)
        {
            if (_layout == null || CurrentRoom == null || !_layout.Contains(CurrentRoom))
                return false;
            // 지금 방 먼저(60%), 아니면 옆방 중 하나
            var rooms = new List<ModuleInstance>();
            _station.Grid.GetNeighborModules(CurrentRoom, _neighbors);
            foreach (var n in _neighbors)
            {
                if (_layout.Contains(n) && n != CurrentRoom && _builder.RoomParent(n) != null)
                    rooms.Add(n);
            }
            for (int i = rooms.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (rooms[i], rooms[j]) = (rooms[j], rooms[i]);
            }
            if (forced != null || _rng.NextDouble() < 0.6)
                rooms.Insert(0, CurrentRoom);
            else
                rooms.Add(CurrentRoom);
            foreach (var room in rooms)
            {
                // CONNECT A6 단계별 상황: 방의 진화 단계에 맞는 상황만 (트인 방에 벽 상황이 붙지 않게)
                var kinds = KindsFor(room);
                HitsFor(room, _hitScratch); // CONNECT A5 이웃 관계 (안쪽 얼굴)
                bool canTrip = InteriorIncidentRules.CanTrip(room.Data);
                IncidentKind kind;
                if (forced != null)
                {
                    if (!Contains(kinds, forced.Value))
                        continue;
                    kind = forced.Value;
                }
                else
                {
                    var picked = InteriorIncidentRules.Pick(Biased(kinds, _hitScratch, room), canTrip, _last, _rng.NextDouble()); // 환기 경보가 잦은 방 = 환기 2배 · 과열 방 = 화재 2배 · 서늘한 방 = 차단기 절반 · 배전 범위 밖 = 차단기 2배
                    if (picked == null)
                        continue;
                    kind = picked.Value;
                }
                // 뽑힌 상황을 이 방에 못 놓으면 (자리 · 멈출 기구 없음) 같은 방의 다른 상황부터 — CONNECT A6 (2026-11-27)
                _tryKinds.Clear();
                _tryKinds.Add(kind);
                if (forced == null)
                {
                    int first = _tryKinds.Count;
                    foreach (var k in kinds)
                    {
                        if (k != kind)
                            _tryKinds.Add(k);
                    }
                    for (int i = _tryKinds.Count - 1; i > first; i--)
                    {
                        int j = first + _rng.Next(i - first + 1);
                        (_tryKinds[i], _tryKinds[j]) = (_tryKinds[j], _tryKinds[i]);
                    }
                }
                Incident a = null;
                foreach (var k in _tryKinds)
                {
                    if (k == IncidentKind.Breaker && !canTrip)
                        continue;
                    var tryA = new Incident { Kind = k, Room = room, Resource = InteriorIncidentRules.LeakResource(room.Data), Tutorial = tutorial };
                    Prepare(tryA, _hitScratch);
                    if (Place(tryA))
                    {
                        a = tryA;
                        break;
                    }
                    End(tryA, rebuilt: false);
                }
                if (a == null)
                    continue;
                Activate(a, null);
                return true;
            }
            return false;
        }

        /// <summary>놓은 사고를 진행 중으로: 원인 한 줄 · 말풍선 · 알림 · 경보 (note = 번져 왔을 때 등 앞에 붙일 말).</summary>
        private void Activate(Incident a, string note)
        {
            _active = a;
            _last = a.Kind;
            WriteCause(a); // CONNECT A5 원인 한 줄 (이웃이 중립이면 없음)
            a.Bubble = SpeechBubble.Create(transform, _font, _s.Bubble);
            a.ShownSeconds = Mathf.CeilToInt(a.Window);
            a.Bubble.Set(BubbleText(a, a.ShownSeconds), Warn);
            string where = a.Room == CurrentRoom ? "이 방" : RoomName(a.Room);
            string extra = (note != null ? " · " + note : "") + (a.Cause != null ? " · " + a.Cause : "")
                           + (!a.Tutorial && InteriorEmergencyKit.AvailableNear(Sim.Grid, a.Room, _neighbors) ? " · 옆 통로에 비상 공구함" : ""); // CONNECT A8 완충
            Message?.Invoke($"사고 · {where}: {Title(a)} · {InteriorIncidentRules.Hint(a.Kind)}{extra}", true);
            AlertSound.TryPlay(AlertLevel.Danger); // 경보 위계 (2026-11-25, D-3): 내부 사고 생김 = 위험 (예전 조기 경보 빌림)
        }

        private readonly List<IncidentKind> _ready = new List<IncidentKind>();
        private readonly List<IncidentKind> _tryKinds = new List<IncidentKind>();

        /// <summary>
        /// CONNECT A6: 이 방에서 지금 낼 수 있는 상황 = 그 단계 표 가운데 만든 것 (<see cref="Implemented"/>).
        /// 1단계(닫힌 방)는 그 단계 상황을 만들기 전까지 기본 넷, 2단계 이상(트인 방)은 만든 것만 — 없으면 상황 없음.
        /// </summary>
        private IReadOnlyList<IncidentKind> KindsFor(ModuleInstance room)
        {
            int stage = _builder.StageFor(room);
            _ready.Clear();
            foreach (var k in IncidentCatalog.KindsForStage(stage))
            {
                if (Implemented(k))
                    _ready.Add(k);
            }
            if (_ready.Count == 0 && stage <= 1)
                return IncidentCatalog.KindsForStage(0);
            return _ready;
        }

        /// <summary>놓는 코드가 있는 상황 (단계 상황은 U2 ~ U4에서 하나씩 더함).</summary>
        private static bool Implemented(IncidentKind kind) => IncidentCatalog.StageOf(kind) == 0 || StageImplemented(kind);

        private static bool Contains(IReadOnlyList<IncidentKind> kinds, IncidentKind kind)
        {
            for (int i = 0; i < kinds.Count; i++)
            {
                if (kinds[i] == kind)
                    return true;
            }
            return false;
        }

        private bool Place(Incident a)
        {
            switch (a.Kind)
            {
                case IncidentKind.Leak: return PlaceLeak(a);
                case IncidentKind.Breaker: return PlaceBreaker(a);
                case IncidentKind.Fire: return PlaceFire(a);
                case IncidentKind.Filter: return PlaceFilter(a);
                default: return PlaceStage(a); // CONNECT A6 단계 상황 (InteriorIncidents.Stage.cs)
            }
        }

        /// <summary>벽 자리 하나에 소품을 붙임 (다른 소품과 0.9m 이상).</summary>
        private GameObject Mount(GameObject prefab, ModuleInstance room, float height, string name, Vector3? avoid = null)
        {
            var parent = _builder.RoomParent(room);
            if (prefab == null || parent == null)
                return null;
            InteriorSpots.Walls(_builder, room, _rng, height, _walls);
            foreach (var (pos, n) in _walls)
            {
                if (avoid.HasValue && (pos - avoid.Value).sqrMagnitude < 1.4f * 1.4f)
                    continue;
                var go = Instantiate(prefab, parent);
                go.name = name;
                go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(n, Vector3.up));
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // 벽에 붙은 얇은 소품
                return go;
            }
            return null;
        }

        // ---------------- 배관 누출 ----------------

        private bool PlaceLeak(Incident a)
        {
            a.Prop = Mount(_s.Leak, a.Room, _s.LeakHeight, "IncidentLeak");
            if (a.Prop == null)
                return false;
            var t = a.Prop.transform;
            Tint(a.Prop, "M_IncBand", a.Resource == ResourceType.Water ? InteriorSupply.ResourceColor(ResourceType.Water) : InteriorSupply.ResourceColor(ResourceType.Oxygen));
            a.Wheel = t.Find("ValveWheel");
            if (a.Wheel != null)
                a.WheelRest = a.Wheel.localRotation;
            // 새는 이음 (가운데 왼쪽 — Unity는 가로가 뒤집혀 +x) 앞에서 김이 뿜어 나옴
            var leak = t.TransformPoint(new Vector3(0.055f, 0f, 0.13f));
            a.Plume = InteriorFx.Plume(t, leak, t.forward + Vector3.up * 0.3f, _smoke, new Color(0.82f, 0.86f, 0.9f, 0.8f), 30f, 0.1f, 1.6f); // 가늘고 빠른 분사 (크고 느리면 네모 입자가 판처럼 보였음)
            // 소리: 새는 이음에서 김 "쉬이익" (작은 누출) · 밸브 바퀴를 돌리는 "드르륵 → 툭" (누르는 2초에 맞춤)
            a.Loop = AudioService.AttachLoop(l => l.PipeBurstLoop, t);
            if (a.Loop != null)
            {
                a.Loop.transform.localPosition = new Vector3(0.055f, 0f, 0.13f);
                a.Loop.Level = 0f;
            }
            a.Held = AudioService.AttachHeld(l => l.ValveTurn, a.Wheel != null ? a.Wheel : t);
            var col = a.Prop.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.02f, 0.13f);
            col.size = new Vector3(0.7f, 0.3f, 0.26f);
            var use = a.Prop.AddComponent<InteriorInteractable>();
            use.HoldSeconds = _s.ValveHold;
            use.Work = true;
            use.Prompt = () => $"밸브 잠그기  <color=#FF8A5C>{Title(a)}</color>";
            use.Holding = p =>
            {
                a.Holding = true;
                a.HoldProgress = p;
                a.HoldWheel = a.Wheel;
            };
            use.Completed = () => Resolve(a);
            if (Is(a, InnerFace.LeakNeighborValve))
                PlaceNeighborValve(a); // CONNECT A5 좋은 이웃: 옆 수원 방 문가에 차단 밸브
            return true;
        }

        // ---------------- 차단기 ----------------

        private bool PlaceBreaker(Incident a)
        {
            a.Prop = Mount(_s.Breaker, a.Room, _s.BreakerHeight, "IncidentBreaker");
            if (a.Prop == null)
                return false;
            var t = a.Prop.transform;
            a.Door = t.Find("BreakerDoor");
            if (a.Door != null)
            {
                a.DoorRest = a.Door.localRotation;
                var dc = a.Door.gameObject.AddComponent<BoxCollider>();
                FitCollider(dc, a.Door.gameObject);
                var du = a.Door.gameObject.AddComponent<InteriorInteractable>();
                du.HoldSeconds = _s.DoorHold;
                du.Prompt = () => $"차단기 함 열기  <color=#FF8A5C>{Title(a)}</color>";
                du.Completed = () =>
                {
                    a.DoorTarget = 1f;
                    Destroy(du);
                    Destroy(dc);
                    AudioService.TryPlayAt(l => l.BreakerDoor, a.Door.position);
                };
            }
            int extra = a.Overload && Sim.Heat != null && Sim.Heat.Comfort != null ? Sim.Heat.Comfort.OverloadExtraSwitches : 0; // CONNECT A11 배전 범위 밖 = 하나 더
            a.Pattern = Is(a, InnerFace.BreakerOneSwitch) ? InteriorIncidentRules.TripPattern(_rng, 1) // CONNECT A5 배터리 옆 = 하나만
                : extra > 0 ? InteriorIncidentRules.TripPattern(_rng, 2 + _rng.Next(2) + extra)
                : InteriorIncidentRules.TripPattern(_rng);
            a.Lit = (bool[])a.Pattern.Clone();
            for (int i = 0; i < InteriorIncidentRules.Switches; i++)
            {
                var lever = t.Find("Switch" + i);
                var led = t.Find("Led" + i);
                a.Levers[i] = lever;
                a.Leds[i] = led != null ? led.GetComponent<Renderer>() : null;
                if (lever == null)
                    continue;
                a.LeverRest[i] = lever.localRotation;
                var sc = lever.gameObject.AddComponent<BoxCollider>();
                sc.center = new Vector3(0f, 0f, 0.035f);
                sc.size = new Vector3(0.05f, 0.08f, 0.08f);
                int index = i;
                var su = lever.gameObject.AddComponent<InteriorInteractable>();
                su.HoldSeconds = _s.SwitchHold;
                su.Prompt = () => a.Lit[index] ? "스위치 올리기  <color=#FF8A5C>경고등 켜짐</color>" : "스위치  <color=#AFC4D8>정상 (건드리지 마세요)</color>";
                su.Completed = () => PressSwitch(a, index);
            }
            ShowSwitches(a);
            // 방 조명이 깜박이며 어두워짐 (끝나면 되돌림)
            var roomParent = _builder.RoomParent(a.Room);
            if (roomParent != null)
            {
                foreach (var l in roomParent.GetComponentsInChildren<Light>())
                {
                    if (!l.transform.IsChildOf(a.Prop.transform))
                        a.RoomLights.Add((l, l.intensity));
                }
            }
            // 차단기 내려감 = 차단기 함 자리 스파크 (3D, 2026-11-25) — 알림은 사고 생김 위험 경보 하나 (예전 2D 스파크는 다른 방에서도 경보와 겹쳐 들렸음)
            AudioService.TryPlayAt(l => l.StormSpark, a.Prop.transform.position);
            return true;
        }

        private void ShowSwitches(Incident a)
        {
            for (int i = 0; i < InteriorIncidentRules.Switches; i++)
            {
                if (a.Levers[i] != null)
                    a.Levers[i].localRotation = Quaternion.AngleAxis(a.Lit[i] ? LeverAngle : -LeverAngle, Vector3.right) * a.LeverRest[i]; // 내려감 = 끝이 아래
                if (a.Leds[i] != null)
                    TintRenderer(a.Leds[i], "M_IncLed", a.Lit[i] ? LedRed : LedGreen);
            }
        }

        private void PressSwitch(Incident a, int index)
        {
            if (a.Done)
                return;
            var result = InteriorIncidentRules.Press(a.Lit, index);
            if (result == SwitchPress.Wrong)
            {
                var lever = a.Levers[index];
                if (lever != null)
                    InteriorFx.Spark(transform, lever.position, a.Prop.transform.forward, _spark, 10, 18, 1.0f);
                AudioService.TryPlay(l => l.UiError);
                AudioService.TryPlayAt(l => l.StormSpark, lever != null ? lever.position : a.Prop.transform.position);
                if (Is(a, InnerFace.BreakerForgiving)) // CONNECT A5 연구소 옆 = 틀려도 처음으로 안 돌아감 (사용자 결정 2026-11-28)
                {
                    Message?.Invoke($"정상 스위치예요 · 옆 {RoomName(NeighborWith(a, InnerFace.BreakerForgiving))} 덕에 진행은 그대로 (경고등 켜진 것만)", true);
                    ShowSwitches(a);
                    return;
                }
                Message?.Invoke("틀렸어요! 스파크 · 처음 상태로 돌아갔어요 (경고등 켜진 것만)", true);
                Array.Copy(a.Pattern, a.Lit, a.Lit.Length);
                ShowSwitches(a);
                return;
            }
            // 사운드 개편 4-3: 스위치마다 큰 "딸깍" (BuildRotate 대신), 마지막 = 같은 딸깍 + 전기 방 웅이 돌아옴
            var at = a.Levers[index] != null ? a.Levers[index].position : a.Prop.transform.position;
            if (result == SwitchPress.AllDone)
                AudioService.TryPlayAt(l => l.BreakerFlip, at);
            else
                AudioService.TryPlayAt(l => l.BreakerSwitch, at);
            ShowSwitches(a);
            if (result == SwitchPress.AllDone)
            {
                Resolve(a);
                a.DoorTarget = 0f;
            }
        }

        // ---------------- 화재 ----------------

        private bool PlaceFire(Incident a)
        {
            a.Prop = Mount(_s.FireBox, a.Room, _s.FireHeight, "IncidentFire");
            if (a.Prop == null)
                return false;
            var t = a.Prop.transform;
            a.Soot = t.Find("Soot") != null ? t.Find("Soot").gameObject : null;
            if (a.Soot != null)
                a.Soot.SetActive(false);
            a.Flames = InteriorFx.Flames(t, t.TransformPoint(new Vector3(0f, 0.125f, 0.07f)), _spark, 0.26f);
            a.Smoke = InteriorFx.Plume(t, t.TransformPoint(new Vector3(0f, 0.2f, 0.08f)), Vector3.up, _smoke, new Color(0.18f, 0.17f, 0.16f, 1f), 14f, 0.26f, 0.5f);
            var glowGo = new GameObject("FireGlow");
            glowGo.transform.SetParent(t, false);
            glowGo.transform.localPosition = new Vector3(0f, 0.35f, 0.5f);
            a.Glow = glowGo.AddComponent<Light>();
            a.Glow.type = LightType.Point;
            a.Glow.shadows = LightShadows.None;
            a.Glow.color = new Color(1f, 0.55f, 0.25f);
            a.Glow.range = 2.6f;
            a.Glow.intensity = 0.35f; // 11-17 ① 피드백: 은은하게
            // 소리: 불꽃 자리에서 작은 불 "타닥" (지금 방일 때만) · 소화기 분사 "쉬익"은 들고 있는 소화기 = 눈 앞 (누르는 2초에 맞춤)
            a.Loop = AudioService.AttachLoop(l => l.FireLoop, t);
            if (a.Loop != null)
            {
                a.Loop.transform.localPosition = new Vector3(0f, 0.125f, 0.07f);
                a.Loop.Level = 0f;
            }
            a.Held = AudioService.AttachHeld(l => l.Extinguish, _eye);
            var col = a.Prop.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.05f, 0.08f);
            col.size = new Vector3(0.4f, 0.5f, 0.2f);
            var use = a.Prop.AddComponent<InteriorInteractable>();
            use.HoldSeconds = _s.FireHold;
            use.Work = true;
            use.Prompt = () => $"소화기로 불 끄기  <color=#FF8A5C>{(a.Fire2 != null ? "불길 두 곳" : Title(a))}</color>";
            use.Blocked = () => a.Carrying ? null : "벽에 걸린 소화기를 먼저 드세요";
            use.Holding = p =>
            {
                a.Holding = true;
                a.HoldProgress = p;
                a.SprayAt = t;
            };
            use.Completed = () => PutOut(a, main: true);

            // 소화기: 같은 방 다른 벽 (불에서 1.4m 이상) — 자리가 없으면 들고 있는 것으로
            if (Is(a, InnerFace.FireTwoFlames))
            {
                PlaceSecondFire(a); // CONNECT A5 나쁜 이웃 (인화 위험): 불길 두 곳 — 소화기보다 먼저 (좁은 방에서 자리를 뺏기지 않게)
                if (a.Fire2 == null) // 자리가 없으면 한 곳 · 보통 시간 (알림도 안 함)
                {
                    a.Inner &= ~InnerFace.FireTwoFlames;
                    a.Window = Sim.Balance.IncidentWindow;
                }
            }
            var ext = a.Inner == InnerFace.None
                ? Mount(_s.Extinguisher, a.Room, _s.ExtinguisherHeight, "IncidentExtinguisher", a.Prop.transform.position)
                : MountExtinguisherTight(a); // CONNECT A5: 이웃 변형이 있으면 불길 둘을 다 피하고, 좁은 방에서도 (예전 방은 그대로)
            if (ext == null)
                a.Carrying = true;
            else
            {
                a.Extra.Add(ext);
                AddBottle(a, ext);
            }
            if (!a.Carrying && Is(a, InnerFace.FireDoorExtinguisher))
                PlaceDoorExtinguisher(a); // CONNECT A5 좋은 이웃: 정비 베이 쪽 문 옆에 하나 더 (둘째 불길 다음 — 좁은 방은 불이 먼저)
            return true;
        }

        // ---------------- 환기 경보 ----------------

        private bool PlaceFilter(Incident a)
        {
            a.Prop = Mount(_s.Filter, a.Room, _s.FilterHeight, "IncidentFilter");
            if (a.Prop == null)
                return false;
            var t = a.Prop.transform;
            a.Cartridge = t.Find("Cartridge");
            if (a.Cartridge != null)
                a.CartridgeRest = a.Cartridge.localPosition;
            var led = t.Find("FilterLed");
            a.FilterLed = led != null ? led.GetComponent<Renderer>() : null;
            Tint(a.Prop, "M_IncFilter", DirtyColor(a));
            a.Plume = InteriorFx.Plume(t, t.TransformPoint(new Vector3(0f, 0.12f, 0.16f)), t.forward, _smoke,
                Is(a, InnerFace.FilterRadiation) ? new Color(0.78f, 0.74f, 0.32f, 0.6f) : new Color(0.5f, 0.46f, 0.38f, 0.6f), 8f, 0.12f, 0.35f); // 방사선 경보 = 노란 먼지
            a.Held = AudioService.AttachHeld(l => l.FilterSwap, a.Cartridge != null ? a.Cartridge : t); // 걸쇠 · 미끄러짐 · 딸깍 (누르는 1.8초에 맞춤)
            var col = a.Prop.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0f, 0.08f);
            col.size = new Vector3(0.62f, 0.52f, 0.16f);
            var use = a.Prop.AddComponent<InteriorInteractable>();
            use.HoldSeconds = _s.FilterHold * (a.VentOnly ? EasyFilterHold : 1f); // CONNECT A5 녹지 옆 = 환기구만 (짧게)
            use.Work = true;
            use.Prompt = () => a.VentOnly ? $"환기구 청소 (필터 그대로)  <color=#FF8A5C>{Title(a)}</color>"
                : a.FiltersLeft > 1 ? $"필터 교체 (두 장 중 첫 장)  <color=#FF8A5C>{Title(a)}</color>"
                : $"필터 교체  <color=#FF8A5C>{Title(a)}</color>";
            use.Holding = p =>
            {
                a.Holding = true;
                a.HoldProgress = p;
            };
            use.Completed = () =>
            {
                if (a.FiltersLeft > 1) // CONNECT A5 소음원 옆 = 필터 두 장
                {
                    a.FiltersLeft--;
                    Tint(a.Prop, "M_IncFilter", DirtyColor(a));
                    AudioService.TryPlayAt(l => l.BreakerSwitch, t.position);
                    Message?.Invoke("필터 한 장을 갈았어요 · 한 장 더 (F를 다시 길게)", false);
                    return;
                }
                Resolve(a);
            };
            return true;
        }

        private static Color DirtyColor(Incident a) => (a.Inner & InnerFace.FilterRadiation) != 0 ? new Color(0.86f, 0.74f, 0.18f) : FilterDirty;

        // ---------------- 진행 · 연출 ----------------

        private void Animate(Incident a, float dt)
        {
            float now = Time.unscaledTime;
            bool holding = a.Holding;
            a.Holding = false;
            // 소리 (사운드 개편 4-3): 손 동작 소리는 누르기 시작할 때 처음부터 · 다 누르기 전에 떼면 꺼짐, 계속 나는 소리는 지금 방 (또는 손이 닿는 곳)일 때만 · 누르는 만큼 줄어듦
            if (a.Held != null)
            {
                if (holding && !a.WasHolding)
                    a.Held.Begin();
                else if (!holding && a.WasHolding)
                    a.Held.Release();
            }
            a.WasHolding = holding;
            if (a.Loop != null && !a.MainOut) // 불길 두 곳: 먼저 끈 불은 조용히 (CONNECT A5)
                a.Loop.Level = holding || InteriorRoomDisplay.PlayerIn(a.Room) ? (holding ? 1f - 0.7f * a.HoldProgress : 1f) : 0f;
            switch (a.Kind)
            {
                case IncidentKind.Leak:
                    if (holding && a.HoldWheel != null) // 이 방 밸브 또는 옆방 차단 밸브 (CONNECT A5)
                        a.HoldWheel.localRotation = Quaternion.AngleAxis(-a.HoldProgress * 540f, Vector3.forward) * (a.HoldWheel == a.Wheel2 ? a.Wheel2Rest : a.WheelRest);
                    SetRate(a.Plume, 30f * (holding ? 1f - a.HoldProgress : 1f));
                    break;
                case IncidentKind.Breaker:
                    // 방 조명: 어둡게(35%) + 가끔 짧게 깜박
                    float flicker = Mathf.PerlinNoise(now * 7f, 0.3f) > 0.72f ? 0.9f : 0.35f;
                    foreach (var (l, i) in a.RoomLights)
                    {
                        if (l != null)
                            l.intensity = i * flicker;
                    }
                    break;
                case IncidentKind.Fire:
                {
                    float glow = 0.3f + 0.08f * Mathf.Sin(now * 9f) + 0.05f * Mathf.Sin(now * 23f);
                    if (a.Glow != null)
                        a.Glow.intensity = glow;
                    var f2 = a.Fire2; // CONNECT A5 불길 두 곳
                    if (f2 != null && !f2.Out)
                    {
                        if (f2.Glow != null)
                            f2.Glow.intensity = glow;
                        if (f2.Loop != null)
                            f2.Loop.Level = InteriorRoomDisplay.PlayerIn(a.Room) ? 1f : 0f;
                    }
                    bool onSecond = f2 != null && a.SprayAt != null && f2.Prop != null && a.SprayAt == f2.Prop.transform;
                    if (holding && a.SprayAt != null)
                    {
                        SetRate(onSecond ? f2.Flames : a.Flames, 60f * (1f - a.HoldProgress));
                        if (now >= a.NextSpray)
                        {
                            a.NextSpray = now + 0.12f;
                            var from = _eye.position + _eye.right * 0.18f - _eye.up * 0.3f + _eye.forward * 0.3f;
                            InteriorFx.Spray(transform, from, a.SprayAt.TransformPoint(new Vector3(0f, 0.12f, 0.1f)), _smoke);
                        }
                    }
                    else
                    {
                        if (!a.MainOut)
                            SetRate(a.Flames, 60f);
                        if (f2 != null && !f2.Out)
                            SetRate(f2.Flames, 60f);
                    }
                    if (!holding)
                        a.SprayAt = null;
                    break;
                }
                case IncidentKind.Filter:
                    if (a.FilterLed != null)
                        TintRenderer(a.FilterLed, "M_IncLed", Mathf.Repeat(now, 0.8f) < 0.4f ? LedRed : new Color(0.25f, 0.05f, 0.04f));
                    if (a.Cartridge != null && !a.VentOnly) // 환기구만 = 필터를 뽑지 않음
                    {
                        float p = holding ? a.HoldProgress : 0f;
                        a.Cartridge.localPosition = a.CartridgeRest + Vector3.forward * (0.17f * Mathf.Sin(p * Mathf.PI));
                        if (holding && p > 0.5f)
                            Tint(a.Prop, "M_IncFilter", FilterClean);
                        else if (!holding)
                            Tint(a.Prop, "M_IncFilter", DirtyColor(a));
                    }
                    break;
                default:
                    AnimateStage(a, now, holding); // CONNECT A6 단계 상황
                    break;
            }
            AnimateDoor(a, dt);
        }

        /// <summary>차단기 문 열고 닫기 (0.4초).</summary>
        private static void AnimateDoor(Incident a, float dt)
        {
            if (a.Door == null)
                return;
            a.DoorOpen = Mathf.MoveTowards(a.DoorOpen, a.DoorTarget, dt / 0.4f);
            a.Door.localRotation = Quaternion.AngleAxis(Mathf.SmoothStep(0f, 1f, a.DoorOpen) * DoorAngle, Vector3.up) * a.DoorRest;
        }

        private void Resolve(Incident a)
        {
            if (a.Done)
                return;
            a.Done = true;
            a.DoneAge = a.Age;
            if (a.Loop != null)
                a.Loop.Level = 0f; // 김 · 불이 멎음 (손 동작 소리는 끝 "툭" · "딸깍"까지 그대로)
            string reward = Sim.ResolveIncident();
            switch (a.Kind)
            {
                case IncidentKind.Leak:
                    if (a.Wheel != null)
                        a.Wheel.localRotation = Quaternion.AngleAxis(-540f, Vector3.forward) * a.WheelRest;
                    if (a.Wheel2 != null)
                        a.Wheel2.localRotation = Quaternion.AngleAxis(-540f, Vector3.forward) * a.Wheel2Rest;
                    StopFx(a.Plume);
                    break;
                case IncidentKind.Breaker:
                    RestoreLights(a);
                    break;
                case IncidentKind.Fire:
                    StopFx(a.Flames);
                    StopFx(a.Smoke);
                    if (a.Glow != null)
                        Destroy(a.Glow.gameObject);
                    if (a.Soot != null)
                        a.Soot.SetActive(true);
                    Douse(a.Fire2);
                    if (a.TakenBottle != null)
                        a.TakenBottle.SetActive(true); // 다 쓴 소화기는 제자리에 (든 것만)
                    break;
                case IncidentKind.Filter:
                    StopFx(a.Plume);
                    if (a.Cartridge != null)
                        a.Cartridge.localPosition = a.CartridgeRest;
                    Tint(a.Prop, "M_IncFilter", FilterClean);
                    if (a.FilterLed != null)
                        TintRenderer(a.FilterLed, "M_IncLed", LedGreen);
                    break;
                default:
                    ResolveStage(a); // CONNECT A6 단계 상황 (기구 다시 움직임 등)
                    break;
            }
            foreach (var use in a.Prop.GetComponentsInChildren<InteriorInteractable>())
                Destroy(use);
            foreach (var x in a.Extra) // 옆방 차단 밸브 · 문 옆 소화기 · 둘째 불길 (CONNECT A5)
            {
                if (x == null)
                    continue;
                foreach (var use in x.GetComponentsInChildren<InteriorInteractable>(true))
                    Destroy(use);
            }
            if (a.Bubble != null)
                a.Bubble.Set($"<b>해결!</b>\n<size=80%>{Title(a)}</size>", new Color(0.35f, 0.8f, 0.45f));
            string line = ResolveLine(a); // CONNECT A5 결과 한 줄 (도와준 이웃 · "옆에 ○○가 있었다면")
            Message?.Invoke($"{Title(a)} 해결 · {reward}{(line != null ? " · " + line : "")}", false);
            // 해결 = 좋은 소식 (위계 밖). F 길게 누르기로 끝냈으면 (밸브 · 소화기 · 필터) 그 완료 화음 하나만 — 해결음과 겹치지 않게 (2026-11-25)
            if (!AlertSound.PlayedThisFrame(l => l.HoldDone))
                AudioService.TryPlay(l => l.RepairDone);
            Resolved?.Invoke();
            IncidentFinished?.Invoke(InteriorIncidentRules.Title(a.Kind), true);
        }

        /// <summary>놓침: 이웃이 바꾼 손실 (CONNECT A5 결과 단계) · 번짐 한 줄.</summary>
        private string Miss(Incident a, bool left, ModuleInstance spreadTo = null, bool spreadBlocked = false)
        {
            string help = null;
            var mods = a.Tutorial ? IncidentMiss.None : MissMods(a, out help);
            string loss = Sim.MissIncident(a.Kind, a.Room, a.Resource, mods);
            if (a.Tutorial)
                help = null;
            string why = left ? "밖으로 나가는 동안" : "시간 안에 못 막아";
            string text = $"{Title(a)} · {why} 정비 인력이 처리했어요 · {loss}{(help != null ? " · " + help : "")}";
            if (spreadTo != null)
                text += $" · 옆방 {RoomName(spreadTo)}{Josa(RoomName(spreadTo), "으로", "로")} 번졌어요";
            else if (spreadBlocked)
                text += $" · 옆 {RoomName(NeighborWith(a, InnerFace.NoSpread))} 덕에 번지지 않았어요";
            if (!left)
                Message?.Invoke(text, true);
            AlertSound.TryPlay(AlertLevel.Caution); // 경보 위계 (2026-11-25): 사고 놓침 = 주의 (손실은 상한 있게 끝남 · 예전 나쁜 사건 소리 0.6)
            IncidentFinished?.Invoke(InteriorIncidentRules.Title(a.Kind), false);
            return text;
        }

        /// <summary>사고 정리: 말풍선 · 연출 정리, 조명 되돌림. 소품은 그 자리에 남김 (고친 모습) — 놓쳤거나 다시 만들면 지움.</summary>
        private void End(Incident a, bool rebuilt)
        {
            RestoreLights(a);
            RestoreStage(a); // CONNECT A6: 꺼 둔 장식 등

            if (a.Bubble != null)
                Destroy(a.Bubble.gameObject);
            a.Bubble = null;
            if (a.Held != null && (a.Prop == null || !a.Held.transform.IsChildOf(a.Prop.transform)))
                Destroy(a.Held.gameObject); // 눈에 붙인 소화기 분사 (소품 밖) — 소품 아래 소리는 소품과 함께 지워짐
            a.Held = null;
            if (rebuilt)
                return; // 방 오브젝트 아래 소품은 이미 지워짐
            if (!a.Done)
            {
                if (a.Prop != null)
                    Destroy(a.Prop);
                foreach (var x in a.Extra)
                {
                    if (x != null)
                        Destroy(x);
                }
                return;
            }
            if (a.Prop != null)
            {
                foreach (var use in a.Prop.GetComponentsInChildren<InteriorInteractable>())
                    Destroy(use);
            }
        }

        private void RestoreLights(Incident a)
        {
            foreach (var (l, i) in a.RoomLights)
            {
                if (l != null)
                    l.intensity = i;
            }
            a.RoomLights.Clear();
        }

        private void PlaceBubble(Incident a)
        {
            if (a.Bubble == null || a.Prop == null)
                return;
            var t = a.BubbleAt != null ? a.BubbleAt : a.Prop.transform; // 바닥에 세운 소품은 위쪽 빈 자리 (CONNECT A6)
            var tip = t.position + Vector3.up * 0.5f + t.forward * 0.2f;
            bool show = (tip - _eye.position).sqrMagnitude < _s.BubbleRange * _s.BubbleRange;
            if (a.Bubble.gameObject.activeSelf != show)
                a.Bubble.gameObject.SetActive(show);
            if (show)
                a.Bubble.Place(tip, _eye, 0.4f); // 소품 앞에서 조작할 때 화면을 가리지 않게
        }

        private string BubbleText(Incident a, int secondsLeft) =>
            $"<color=#C8401A><b>{Title(a)}!</b></color>\n{InteriorIncidentRules.Hint(a.Kind)}" +
            (a.CauseShort != null ? $"\n<size=75%><color=#3A5A78>{a.CauseShort}</color></size>" : "") + // CONNECT A5 원인 (말풍선 둘째 줄)
            (a.Tutorial ? "" : $"\n<size=75%><color=#7A6A60>남은 시간 {secondsLeft}초</color></size>");

        // ---------------- 도우미 ----------------

        private static void SetRate(ParticleSystem ps, float rate)
        {
            if (ps == null)
                return;
            var e = ps.emission;
            e.rateOverTime = Mathf.Max(0f, rate);
        }

        private static void StopFx(ParticleSystem ps)
        {
            if (ps == null)
                return;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Destroy(ps.gameObject, 5f);
        }

        /// <summary>재질 이름이 slot으로 시작하는 칸만 물들임 (모델 하나 · 재질 복사 없음).</summary>
        private void Tint(GameObject go, string slot, Color color)
        {
            if (go == null)
                return;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                TintRenderer(r, slot, color);
        }

        private void TintRenderer(Renderer r, string slot, Color color)
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null || !mats[i].name.StartsWith(slot, StringComparison.Ordinal))
                    continue;
                _block.Clear();
                _block.SetColor("_BaseColor", color);
                r.SetPropertyBlock(_block, i);
            }
        }

        private static void FitCollider(BoxCollider col, GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return;
            var b = renderers[0].bounds;
            foreach (var x in renderers)
                b.Encapsulate(x.bounds);
            var t = go.transform;
            col.center = t.InverseTransformPoint(b.center);
            // 벽에 붙은 소품: 월드 상자를 로컬 축으로 대강 옮김 (세로축으로만 돌아 있음)
            var size = t.InverseTransformVector(b.size);
            col.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Max(0.06f, Mathf.Abs(size.z)));
        }

        private static string RoomName(ModuleInstance m) => m != null && m.Data != null ? m.Data.DisplayName : "옆방";
    }
}
