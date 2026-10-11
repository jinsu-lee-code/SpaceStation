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
    public sealed class InteriorIncidents : MonoBehaviour
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
            [Tooltip("경고 말풍선 재질 (주민 요청과 같은 것)")]
            public Material Bubble;
            [Tooltip("F 누르는 시간(초): 밸브 · 불 끄기 · 필터 · 소화기 들기 · 함 문 · 스위치")]
            public float ValveHold = 2f;
            public float FireHold = 2f;
            public float FilterHold = 1.8f;
            public float PickupHold = 0.4f;
            public float DoorHold = 0.3f;
            public float SwitchHold = 0.15f;
            [Tooltip("소품 가운데 높이 (바닥에서, m): 누출 · 차단기 · 화재 · 필터 · 소화기")]
            public float LeakHeight = 1.2f;
            public float BreakerHeight = 1.4f;
            public float FireHeight = 1.3f;
            public float FilterHeight = 1.6f;
            public float ExtinguisherHeight = 1.0f;
            [Tooltip("경고 말풍선이 보이는 거리 (m)")]
            public float BubbleRange = 25f;
        }

        private sealed class Incident
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
            title = InteriorIncidentRules.Title(_active.Kind);
            room = _active.Room;
            secondsLeft = _active.Tutorial ? -1 : Mathf.Max(0, Mathf.CeilToInt(Sim.Balance.IncidentWindow - _active.Age));
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
                }
                return;
            }
            if (a.Tutorial)
            {
                PlaceBubble(a);
                Animate(a, dt);
                return;
            }
            if (a.Age > b.IncidentWindow)
            {
                Miss(a, left: false);
                End(a, rebuilt: false);
                _active = null;
                _timer = ResidentRequestRules.NextDelay(b.IncidentIntervalMin, b.IncidentIntervalMax, _rng.NextDouble());
                return;
            }
            int left = Mathf.CeilToInt(b.IncidentWindow - a.Age);
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
                var kind = forced ?? InteriorIncidentRules.Pick(InteriorIncidentRules.CanTrip(room.Data), _last, _rng.NextDouble());
                if (forced == IncidentKind.Breaker && !InteriorIncidentRules.CanTrip(room.Data))
                    continue;
                var a = new Incident { Kind = kind, Room = room, Resource = InteriorIncidentRules.LeakResource(room.Data), Tutorial = tutorial };
                if (!Place(a))
                {
                    End(a, rebuilt: false);
                    continue;
                }
                _active = a;
                _last = kind;
                a.Bubble = SpeechBubble.Create(transform, _font, _s.Bubble);
                a.ShownSeconds = Mathf.CeilToInt(Sim.Balance.IncidentWindow);
                a.Bubble.Set(BubbleText(a, a.ShownSeconds), Warn);
                string where = room == CurrentRoom ? "이 방" : RoomName(room);
                Message?.Invoke($"사고 · {where}: {InteriorIncidentRules.Title(kind)} · {InteriorIncidentRules.Hint(kind)}", true);
                AudioService.TryPlay(l => l.EarlyWarning);
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
                default: return PlaceFilter(a);
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
            var col = a.Prop.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.02f, 0.13f);
            col.size = new Vector3(0.7f, 0.3f, 0.26f);
            var use = a.Prop.AddComponent<InteriorInteractable>();
            use.HoldSeconds = _s.ValveHold;
            use.Prompt = () => $"밸브 잠그기  <color=#FF8A5C>{InteriorIncidentRules.Title(a.Kind)}</color>";
            use.Holding = p =>
            {
                a.Holding = true;
                a.HoldProgress = p;
            };
            use.Completed = () => Resolve(a);
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
                du.Prompt = () => $"차단기 함 열기  <color=#FF8A5C>{InteriorIncidentRules.Title(a.Kind)}</color>";
                du.Completed = () =>
                {
                    a.DoorTarget = 1f;
                    Destroy(du);
                    Destroy(dc);
                    AudioService.TryPlay(l => l.UiOpen);
                };
            }
            a.Pattern = InteriorIncidentRules.TripPattern(_rng);
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
            AudioService.TryPlay(l => l.StormSpark);
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
                AudioService.TryPlay(l => l.StormSpark);
                Message?.Invoke("틀렸어요! 스파크 · 처음 상태로 돌아갔어요 (경고등 켜진 것만)", true);
                Array.Copy(a.Pattern, a.Lit, a.Lit.Length);
                ShowSwitches(a);
                return;
            }
            AudioService.TryPlay(l => l.BuildRotate, 0.6f);
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
            var col = a.Prop.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.05f, 0.08f);
            col.size = new Vector3(0.4f, 0.5f, 0.2f);
            var use = a.Prop.AddComponent<InteriorInteractable>();
            use.HoldSeconds = _s.FireHold;
            use.Prompt = () => $"소화기로 불 끄기  <color=#FF8A5C>{InteriorIncidentRules.Title(a.Kind)}</color>";
            use.Blocked = () => a.Carrying ? null : "벽에 걸린 소화기를 먼저 드세요";
            use.Holding = p =>
            {
                a.Holding = true;
                a.HoldProgress = p;
            };
            use.Completed = () => Resolve(a);

            // 소화기: 같은 방 다른 벽 (불에서 1.4m 이상) — 자리가 없으면 들고 있는 것으로
            var ext = Mount(_s.Extinguisher, a.Room, _s.ExtinguisherHeight, "IncidentExtinguisher", a.Prop.transform.position);
            if (ext == null)
            {
                a.Carrying = true;
                return true;
            }
            a.Extra.Add(ext);
            var bottle = ext.transform.Find("ExtBottle");
            a.Bottle = bottle != null ? bottle.gameObject : ext;
            var bc = a.Bottle.AddComponent<BoxCollider>();
            FitCollider(bc, a.Bottle);
            var bu = a.Bottle.AddComponent<InteriorInteractable>();
            bu.HoldSeconds = _s.PickupHold;
            bu.Prompt = () => "소화기 들기";
            bu.Completed = () =>
            {
                a.Carrying = true;
                a.Bottle.SetActive(false);
                Message?.Invoke("소화기를 들었어요 · 불에 대고 F를 길게 누르세요", false);
                AudioService.TryPlay(l => l.BuildSelect);
            };
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
            Tint(a.Prop, "M_IncFilter", FilterDirty);
            a.Plume = InteriorFx.Plume(t, t.TransformPoint(new Vector3(0f, 0.12f, 0.16f)), t.forward, _smoke, new Color(0.5f, 0.46f, 0.38f, 0.6f), 8f, 0.12f, 0.35f);
            var col = a.Prop.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0f, 0.08f);
            col.size = new Vector3(0.62f, 0.52f, 0.16f);
            var use = a.Prop.AddComponent<InteriorInteractable>();
            use.HoldSeconds = _s.FilterHold;
            use.Prompt = () => $"필터 교체  <color=#FF8A5C>{InteriorIncidentRules.Title(a.Kind)}</color>";
            use.Holding = p =>
            {
                a.Holding = true;
                a.HoldProgress = p;
            };
            use.Completed = () => Resolve(a);
            return true;
        }

        // ---------------- 진행 · 연출 ----------------

        private void Animate(Incident a, float dt)
        {
            float now = Time.unscaledTime;
            bool holding = a.Holding;
            a.Holding = false;
            switch (a.Kind)
            {
                case IncidentKind.Leak:
                    if (a.Wheel != null && holding)
                        a.Wheel.localRotation = Quaternion.AngleAxis(-a.HoldProgress * 540f, Vector3.forward) * a.WheelRest;
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
                    if (a.Glow != null)
                        a.Glow.intensity = 0.3f + 0.08f * Mathf.Sin(now * 9f) + 0.05f * Mathf.Sin(now * 23f);
                    if (holding)
                    {
                        SetRate(a.Flames, 60f * (1f - a.HoldProgress));
                        if (now >= a.NextSpray)
                        {
                            a.NextSpray = now + 0.12f;
                            var from = _eye.position + _eye.right * 0.18f - _eye.up * 0.3f + _eye.forward * 0.3f;
                            InteriorFx.Spray(transform, from, a.Prop.transform.TransformPoint(new Vector3(0f, 0.12f, 0.1f)), _smoke);
                        }
                    }
                    else
                        SetRate(a.Flames, 60f);
                    break;
                default:
                    if (a.FilterLed != null)
                        TintRenderer(a.FilterLed, "M_IncLed", Mathf.Repeat(now, 0.8f) < 0.4f ? LedRed : new Color(0.25f, 0.05f, 0.04f));
                    if (a.Cartridge != null)
                    {
                        float p = holding ? a.HoldProgress : 0f;
                        a.Cartridge.localPosition = a.CartridgeRest + Vector3.forward * (0.17f * Mathf.Sin(p * Mathf.PI));
                        if (holding && p > 0.5f)
                            Tint(a.Prop, "M_IncFilter", FilterClean);
                        else if (!holding)
                            Tint(a.Prop, "M_IncFilter", FilterDirty);
                    }
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
            string reward = Sim.ResolveIncident();
            switch (a.Kind)
            {
                case IncidentKind.Leak:
                    if (a.Wheel != null)
                        a.Wheel.localRotation = Quaternion.AngleAxis(-540f, Vector3.forward) * a.WheelRest;
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
                    if (a.Bottle != null)
                        a.Bottle.SetActive(true); // 다 쓴 소화기는 제자리에
                    break;
                default:
                    StopFx(a.Plume);
                    if (a.Cartridge != null)
                        a.Cartridge.localPosition = a.CartridgeRest;
                    Tint(a.Prop, "M_IncFilter", FilterClean);
                    if (a.FilterLed != null)
                        TintRenderer(a.FilterLed, "M_IncLed", LedGreen);
                    break;
            }
            foreach (var use in a.Prop.GetComponentsInChildren<InteriorInteractable>())
                Destroy(use);
            if (a.Bubble != null)
                a.Bubble.Set($"<b>해결!</b>\n<size=80%>{InteriorIncidentRules.Title(a.Kind)}</size>", new Color(0.35f, 0.8f, 0.45f));
            Message?.Invoke($"{InteriorIncidentRules.Title(a.Kind)} 해결 · {reward}", false);
            AudioService.TryPlay(l => l.RepairDone);
            Resolved?.Invoke();
        }

        private string Miss(Incident a, bool left)
        {
            string loss = Sim.MissIncident(a.Kind, a.Room, a.Resource);
            string why = left ? "밖으로 나가는 동안" : "시간 안에 못 막아";
            string text = $"{InteriorIncidentRules.Title(a.Kind)} · {why} 정비 인력이 처리했어요 · {loss}";
            if (!left)
                Message?.Invoke(text, true);
            AudioService.TryPlay(l => l.EventNegative, 0.6f);
            return text;
        }

        /// <summary>사고 정리: 말풍선 · 연출 정리, 조명 되돌림. 소품은 그 자리에 남김 (고친 모습) — 놓쳤거나 다시 만들면 지움.</summary>
        private void End(Incident a, bool rebuilt)
        {
            RestoreLights(a);
            if (a.Bubble != null)
                Destroy(a.Bubble.gameObject);
            a.Bubble = null;
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
            var t = a.Prop.transform;
            var tip = t.position + Vector3.up * 0.5f + t.forward * 0.2f;
            bool show = (tip - _eye.position).sqrMagnitude < _s.BubbleRange * _s.BubbleRange;
            if (a.Bubble.gameObject.activeSelf != show)
                a.Bubble.gameObject.SetActive(show);
            if (show)
                a.Bubble.Place(tip, _eye, 0.4f); // 소품 앞에서 조작할 때 화면을 가리지 않게
        }

        private static string BubbleText(Incident a, int secondsLeft) =>
            $"<color=#C8401A><b>{InteriorIncidentRules.Title(a.Kind)}!</b></color>\n{InteriorIncidentRules.Hint(a.Kind)}" +
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
