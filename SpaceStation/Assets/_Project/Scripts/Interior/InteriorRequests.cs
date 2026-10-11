using System;
using System.Collections.Generic;
using SpaceStation.Audio;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 11-17 ③ 주민 요청 (BALANCE 30번, 사용자 결정 2026-10-11). 플레이어가 안에 있을 때만, 지금 방 · 옆방의 주민이 부탁한다.
    /// - 2~3분마다(실시간) 하나, 동시에 2개까지, 5분 지나면 조용히 사라짐(손해 없음). 내부를 다시 만들면 걸린 요청은 모두 조용히 사라짐.
    /// - 종류: 시든 화분에 물 주기(<see cref="InteriorPlants"/>) · 다른 방에 두고 온 물건 가져다주기(공구함 · 구급함 · 간식 바구니)
    ///   · 덜컹거리는 환풍구 손보기 · 말동무. 할 수 있는 것만 고름(<see cref="ResidentRequestRules"/>).
    /// - 표시: 주민 머리 위 말풍선(<see cref="SpeechBubble"/>) + 패드 홀로그램 표시(<see cref="CountIn"/>).
    /// - 보상: 그 주민 기분 보너스 + 정거장 만족도 조금, 가끔 연구 포인트 (<see cref="StationSimulation.CompleteResidentRequest"/>).
    /// 조작은 모두 F 길게(<see cref="InteriorInteractable"/>). 물건 · 환풍구 모델: BlenderWork/InteriorProps/request_builder.py.
    /// </summary>
    public sealed class InteriorRequests : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("공구함 (request_builder.py)")]
            public GameObject Toolbox;
            [Tooltip("구급함")]
            public GameObject Medkit;
            [Tooltip("간식 바구니")]
            public GameObject Snack;
            [Tooltip("덜컹거리는 환풍구 (자식 VentFrame · VentCover · VentScrews)")]
            public GameObject Vent;
            [Tooltip("말풍선 재질 (URP Unlit, 양면)")]
            public Material Bubble;
            [Tooltip("F 누르는 시간(초): 물 주기 · 환풍구 · 줍기 · 건네기/말 걸기")]
            public float WaterHold = 1.2f;
            public float FixHold = 1.5f;
            public float PickupHold = 0.4f;
            public float HandHold = 0.5f;
            [Tooltip("환풍구 가운데 높이 (바닥에서, m) — 자리가 없으면 1.35m에서 다시 찾음")]
            public float VentHeight = 2.1f;
            [Tooltip("말풍선이 보이는 거리 (m)")]
            public float BubbleRange = 20f;
            [Tooltip("들어준 뒤 고맙다는 말풍선 시간(초)")]
            public float ReplySeconds = 4.5f;
        }

        private sealed class Request
        {
            public RequestKind Kind;
            public int ResidentId;
            public string Name;
            public ModuleInstance Room;
            public float Age;
            public bool Done;
            public float DoneAge;
            public string Reply;
            public SpeechBubble Bubble;
            public InteriorInteractable ResidentUse;
            // 물 주기
            public InteriorPlants.Plant Plant;
            public BoxCollider PlantCollider;
            public InteriorInteractable PlantUse;
            // 가져다주기
            public FetchItem Item;
            public ModuleInstance ItemRoom;
            public GameObject ItemObject;
            public bool Carrying;
            // 환풍구
            public GameObject Vent;
            public Transform Cover;
            public Quaternion CoverRest;
            public float NextRattle, RattleUntil;
            public bool Holding;
        }

        private static readonly Color WaterColor = new Color(0.3f, 0.62f, 0.95f);
        private static readonly Color FetchColor = new Color(0.95f, 0.62f, 0.2f);
        private static readonly Color FixColor = new Color(0.45f, 0.75f, 0.35f);
        private static readonly Color TalkColor = new Color(0.92f, 0.45f, 0.65f);
        private const float LooseTilt = 9f;

        private StationController _station;
        private InteriorBuilder _builder;
        private InteriorResidents _residents;
        private InteriorPlants _plants;
        private Settings _s;
        private TMP_FontAsset _font;
        private Transform _eye;
        private InteriorLayout _layout;
        private float _timer;
        private readonly System.Random _rng = new System.Random();
        private readonly List<Request> _requests = new List<Request>();
        private readonly List<ModuleInstance> _near = new List<ModuleInstance>();
        private readonly List<ModuleInstance> _neighbors = new List<ModuleInstance>();
        private readonly List<ResidentFigure> _candidates = new List<ResidentFigure>();
        private readonly List<(Vector3 Position, Vector3 Normal)> _walls = new List<(Vector3, Vector3)>();
        private readonly List<Vector3> _usedFloor = new List<Vector3>();
        private readonly Dictionary<ModuleInstance, GameObject> _vents = new Dictionary<ModuleInstance, GameObject>();

        /// <summary>플레이어가 있는 방 (요청은 이 방 · 옆방에서만 생김).</summary>
        public ModuleInstance CurrentRoom { get; set; }
        /// <summary>알림 (글, 실패 여부).</summary>
        public event Action<string, bool> Message;

        private StationSimulation Sim => _station.Simulation;

        public static InteriorRequests Create(Transform parent, StationController station, InteriorBuilder builder, InteriorResidents residents,
            InteriorPlants plants, Settings settings, TMP_FontAsset font, Transform eye)
        {
            var go = new GameObject("InteriorRequests");
            go.transform.SetParent(parent, false);
            var r = go.AddComponent<InteriorRequests>();
            r._station = station;
            r._builder = builder;
            r._residents = residents;
            r._plants = plants;
            r._s = settings ?? new Settings();
            r._font = font;
            r._eye = eye;
            r._timer = station.Simulation.Balance.RequestFirstDelay;
            if (r._s.Bubble == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader != null)
                    r._s.Bubble = new Material(shader) { name = "SpeechBubble (runtime)" };
            }
            return r;
        }

        /// <summary>내부를 (다시) 만든 직후 — 인물 · 식물 · 방 소품이 새로 만들어졌으므로 걸린 요청은 조용히 정리.</summary>
        public void Rebuild(InteriorLayout layout)
        {
            foreach (var r in _requests)
                Cancel(r, rebuilt: true);
            _requests.Clear();
            _vents.Clear();
            _usedFloor.Clear();
            _layout = layout;
        }

        /// <summary>11-17 ③ 패드 홀로그램 표시용: 이 방에 걸린 요청 수 (부탁한 주민 + 가져올 물건이 있는 방).</summary>
        public int CountIn(ModuleInstance module)
        {
            int n = 0;
            foreach (var r in _requests)
            {
                if (r.Done)
                    continue;
                if (r.Room == module)
                    n++;
                else if (r.Kind == RequestKind.Fetch && !r.Carrying && r.ItemRoom == module)
                    n++;
            }
            return n;
        }

        /// <summary>확인용: 지금 바로 요청 하나 (종류 지정 가능). 성공하면 true.</summary>
        public bool ForceRequest(RequestKind? kind = null) => TrySpawn(kind);

        public int ActiveCount
        {
            get
            {
                int n = 0;
                foreach (var r in _requests)
                {
                    if (!r.Done)
                        n++;
                }
                return n;
            }
        }

        private void Update()
        {
            if (_layout == null || _eye == null)
                return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.25f);
            var b = Sim.Balance;
            _timer -= dt;
            if (_timer <= 0f)
            {
                bool ok = ResidentRequestRules.CanAdd(ActiveCount, b.RequestMaxActive) && TrySpawn(null);
                // 못 걸었으면(주변에 주민이 없음 등) 잠시 뒤 다시
                _timer = ok ? ResidentRequestRules.NextDelay(b.RequestIntervalMin, b.RequestIntervalMax, _rng.NextDouble()) : 15f;
            }
            for (int i = _requests.Count - 1; i >= 0; i--)
            {
                var r = _requests[i];
                if (!Tick(r, dt, b.RequestExpireSeconds))
                {
                    Cancel(r, rebuilt: false);
                    _requests.RemoveAt(i);
                }
            }
        }

        /// <summary>요청 하나 진행. false = 없앰 (만료 · 주민이 사라짐 · 고맙다는 말풍선이 끝남).</summary>
        private bool Tick(Request r, float dt, float expire)
        {
            r.Age += dt;
            var figure = _residents != null ? _residents.Find(r.ResidentId) : null;
            if (figure == null)
                return false;
            if (r.Done)
            {
                if (r.Age - r.DoneAge > _s.ReplySeconds)
                    return false;
            }
            else if (r.Age > expire)
                return false;
            UpdateVent(r, dt);
            if (r.Bubble != null)
            {
                float lift = _residents.Looked == figure ? 0.62f : 0.12f; // 이름표가 뜨면 그 위로
                var tip = figure.TagPoint + Vector3.up * lift;
                bool show = (tip - _eye.position).sqrMagnitude < _s.BubbleRange * _s.BubbleRange;
                if (r.Bubble.gameObject.activeSelf != show)
                    r.Bubble.gameObject.SetActive(show);
                if (show)
                    r.Bubble.Place(tip, _eye);
            }
            return true;
        }

        // ---------------- 요청 만들기 ----------------

        private bool TrySpawn(RequestKind? forced)
        {
            if (_layout == null || CurrentRoom == null || _residents == null || !_layout.Contains(CurrentRoom))
                return false;
            _near.Clear();
            _near.Add(CurrentRoom);
            _station.Grid.GetNeighborModules(CurrentRoom, _neighbors);
            foreach (var n in _neighbors)
            {
                if (_layout.Contains(n) && !_near.Contains(n))
                    _near.Add(n);
            }
            _candidates.Clear();
            foreach (var f in _residents.Figures)
            {
                if (f != null && f.Room != null && _near.Contains(f.Room) && !HasRequest(f.ResidentId))
                    _candidates.Add(f);
            }
            if (_candidates.Count == 0)
                return false;
            var figure = _candidates[_rng.Next(_candidates.Count)];
            var resident = ResidentOf(figure.ResidentId);
            if (resident == null)
                return false;
            var room = figure.Room;

            var plant = FindPlant(room);
            bool fetch = _layout.Rooms.Count > 1;
            bool fix = !_vents.ContainsKey(room) && FindVentSpot(room, out _, out _);
            var active = new List<RequestKind>();
            foreach (var q in _requests)
            {
                if (!q.Done)
                    active.Add(q.Kind);
            }
            var kind = forced ?? ResidentRequestRules.Pick(plant != null, fetch, fix, active, _rng.NextDouble());
            if (kind == null)
                return false;
            var r = new Request { Kind = kind.Value, ResidentId = resident.Id, Name = resident.Name, Room = room };
            bool placed;
            switch (r.Kind)
            {
                case RequestKind.Water: placed = plant != null && PlaceWater(r, plant); break;
                case RequestKind.Fetch: placed = fetch && PlaceFetch(r); break;
                case RequestKind.Fix: placed = PlaceVent(r); break;
                default: placed = true; break;
            }
            if (!placed)
            {
                ClearPlant(r, wilt: false);
                if (forced != null)
                    return false;
                r.Kind = RequestKind.Talk; // 자리를 못 찾으면 말동무로
            }
            AttachResident(r, figure);
            r.Bubble = SpeechBubble.Create(transform, _font, _s.Bubble);
            r.Bubble.Set(BubbleText(r), Accent(r.Kind));
            _requests.Add(r);
            string where = room == CurrentRoom ? "" : $" <color=#AFC4D8>({RoomName(room)})</color>";
            Message?.Invoke($"{r.Name}의 부탁{where} · {ResidentRequestRules.Title(r.Kind)}", false);
            AudioService.TryPlay(l => l.UiOpen, 0.8f);
            return true;
        }

        private bool HasRequest(int residentId)
        {
            foreach (var r in _requests)
            {
                if (r.ResidentId == residentId)
                    return true;
            }
            return false;
        }

        private Resident ResidentOf(int id)
        {
            var roster = Sim.Residents;
            if (roster == null)
                return null;
            foreach (var r in roster.Residents)
            {
                if (r.Id == id)
                    return r;
            }
            return null;
        }

        /// <summary>시들게 할 화분: 부탁한 주민의 방에 있는 아직 멀쩡한 식물 (다른 요청이 쓰지 않는 것).</summary>
        private InteriorPlants.Plant FindPlant(ModuleInstance room)
        {
            if (_plants == null)
                return null;
            var list = new List<InteriorPlants.Plant>();
            foreach (var p in _plants.Plants)
            {
                if (p.Module == room && !p.IsWilted && p.Root != null)
                    list.Add(p);
            }
            return list.Count > 0 ? list[_rng.Next(list.Count)] : null;
        }

        private bool FindVentSpot(ModuleInstance room, out Vector3 position, out Vector3 normal)
        {
            position = normal = default;
            int seed = room.Origin.x * 73856093 ^ room.Origin.y * 19349663 ^ room.Origin.z * 83492791 ^ 0x5EED;
            foreach (float h in new[] { _s.VentHeight, 1.35f })
            {
                InteriorSpots.Walls(_builder, room, new System.Random(seed), h, _walls);
                if (_walls.Count > 0)
                {
                    (position, normal) = _walls[0];
                    return true;
                }
            }
            return false;
        }

        // ---------------- 물 주기 ----------------

        private bool PlaceWater(Request r, InteriorPlants.Plant plant)
        {
            plant.SetWilted(true);
            r.Plant = plant;
            var root = plant.Root;
            var source = plant.Wilted != null ? plant.Wilted : plant.Healthy;
            var renderers = source.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return false;
            var bounds = renderers[0].bounds;
            foreach (var x in renderers)
                bounds.Encapsulate(x.bounds);
            r.PlantCollider = root.gameObject.AddComponent<BoxCollider>();
            float scale = Mathf.Max(0.01f, root.lossyScale.x);
            r.PlantCollider.center = root.InverseTransformPoint(bounds.center);
            r.PlantCollider.size = bounds.size / scale;
            r.PlantUse = root.gameObject.AddComponent<InteriorInteractable>();
            r.PlantUse.HoldSeconds = _s.WaterHold;
            r.PlantUse.Prompt = () => $"시든 화분에 물 주기  <color=#AFC4D8>{r.Name}의 부탁</color>";
            r.PlantUse.Completed = () =>
            {
                ClearPlant(r, wilt: false);
                Complete(r);
            };
            return true;
        }

        private void ClearPlant(Request r, bool wilt)
        {
            if (r.Plant != null)
                r.Plant.SetWilted(wilt);
            if (r.PlantUse != null)
                Destroy(r.PlantUse);
            if (r.PlantCollider != null)
                Destroy(r.PlantCollider);
            r.PlantUse = null;
            r.PlantCollider = null;
        }

        // ---------------- 가져다주기 ----------------

        private bool PlaceFetch(Request r)
        {
            var used = new List<FetchItem>();
            foreach (var q in _requests)
            {
                if (q.Kind == RequestKind.Fetch && !q.Done)
                    used.Add(q.Item);
            }
            r.Item = ResidentRequestRules.PickItem(used, _rng.NextDouble());
            var prefab = r.Item == FetchItem.Toolbox ? _s.Toolbox : r.Item == FetchItem.Medkit ? _s.Medkit : _s.Snack;
            if (prefab == null)
                return false;
            // 다른 방 중 아무 데나 (가까운 방에 몰리지 않게 섞어서 차례로 시도)
            var rooms = new List<ModuleInstance>();
            foreach (var room in _layout.Rooms)
            {
                if (room.Module != r.Room)
                    rooms.Add(room.Module);
            }
            for (int i = rooms.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (rooms[i], rooms[j]) = (rooms[j], rooms[i]);
            }
            foreach (var room in rooms)
            {
                var parent = _builder.RoomParent(room);
                if (parent == null)
                    continue;
                if (!InteriorSpots.Floor(_builder, room, _rng, new Vector3(0.28f, 0.18f, 0.28f), _usedFloor, 0.9f, out var spot, 0.8f, 2.4f))
                    continue;
                _usedFloor.Add(spot);
                var go = Instantiate(prefab, parent);
                go.name = "Request" + r.Item;
                go.transform.SetPositionAndRotation(spot, Quaternion.Euler(0f, (float)_rng.NextDouble() * 360f, 0f));
                AddBoxCollider(go);
                var use = go.AddComponent<InteriorInteractable>();
                use.HoldSeconds = _s.PickupHold;
                use.Prompt = () => $"{ResidentRequestRules.ItemName(r.Item)} 줍기  <color=#AFC4D8>{r.Name}의 부탁</color>";
                use.Completed = () => Pickup(r);
                r.ItemObject = go;
                r.ItemRoom = room;
                return true;
            }
            return false;
        }

        private void Pickup(Request r)
        {
            if (r.ItemObject != null)
                Destroy(r.ItemObject);
            r.ItemObject = null;
            r.Carrying = true;
            if (r.ResidentUse != null)
                r.ResidentUse.Completed = () => Complete(r);
            string item = ResidentRequestRules.ItemName(r.Item);
            Message?.Invoke($"{item}{ResidentRequestRules.Particle(item, "을", "를")} 들었어요 · {r.Name}<color=#AFC4D8>({RoomName(r.Room)})</color>에게 가져다주세요", false);
            AudioService.TryPlay(l => l.BuildSelect);
            if (r.Bubble != null)
                r.Bubble.Set(BubbleText(r), Accent(r.Kind));
        }

        // ---------------- 환풍구 ----------------

        private bool PlaceVent(Request r)
        {
            if (_s.Vent == null || _vents.ContainsKey(r.Room) || !FindVentSpot(r.Room, out var position, out var normal))
                return false;
            var parent = _builder.RoomParent(r.Room);
            if (parent == null)
                return false;
            var go = Instantiate(_s.Vent, parent);
            go.name = "RequestVent";
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(normal, Vector3.up));
            foreach (var x in go.GetComponentsInChildren<Renderer>())
                x.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // 벽에 붙은 얇은 소품
            r.Cover = go.transform.Find("VentCover");
            var screws = go.transform.Find("VentScrews");
            if (screws != null)
                screws.gameObject.SetActive(false);
            if (r.Cover != null)
            {
                r.CoverRest = r.Cover.localRotation;
                r.Cover.localRotation = Quaternion.AngleAxis(LooseTilt, Vector3.forward) * r.CoverRest; // 부모(벽 법선 = +z) 기준으로 돌림
            }
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0f, 0.03f);
            col.size = new Vector3(0.52f, 0.4f, 0.08f);
            var use = go.AddComponent<InteriorInteractable>();
            use.HoldSeconds = _s.FixHold;
            use.Prompt = () => $"덜컹거리는 환풍구 조이기  <color=#AFC4D8>{r.Name}의 부탁</color>";
            use.Holding = _ => r.Holding = true;
            use.Completed = () =>
            {
                FixVent(r, quiet: false);
                Complete(r);
            };
            r.Vent = go;
            r.NextRattle = r.Age + 0.5f;
            _vents[r.Room] = go;
            return true;
        }

        /// <summary>덜컹거림: 왼쪽 위 나사에 매달려 9° 기운 덮개가 가끔 짧게 떨림 (가까우면 덜컥 소리). F를 누르는 동안은 조여지며 바로 섬.</summary>
        private void UpdateVent(Request r, float dt)
        {
            if (r.Cover == null || r.Done)
                return;
            float tilt = LooseTilt;
            if (r.Holding)
            {
                tilt = Mathf.Lerp(LooseTilt, 0f, 0.5f) + Mathf.Sin(Time.unscaledTime * 45f) * 1.2f;
                r.Holding = false;
            }
            else
            {
                if (r.Age >= r.NextRattle)
                {
                    r.RattleUntil = r.Age + 0.45f;
                    r.NextRattle = r.Age + 1.5f + (float)_rng.NextDouble() * 2.5f;
                    if (r.Vent != null && (r.Vent.transform.position - _eye.position).sqrMagnitude < 7f * 7f)
                        AudioService.TryPlay(l => l.BuildRotate, 0.35f);
                }
                if (r.Age < r.RattleUntil)
                    tilt += Mathf.Sin(r.Age * 60f) * 2.5f * (r.RattleUntil - r.Age) / 0.45f;
            }
            r.Cover.localRotation = Quaternion.AngleAxis(tilt, Vector3.forward) * r.CoverRest;
        }

        private void FixVent(Request r, bool quiet)
        {
            if (r.Vent == null)
                return;
            if (r.Cover != null)
                r.Cover.localRotation = r.CoverRest;
            var screws = r.Vent.transform.Find("VentScrews");
            if (screws != null)
                screws.gameObject.SetActive(true);
            var use = r.Vent.GetComponent<InteriorInteractable>();
            if (use != null)
                Destroy(use);
            r.Cover = null;
            if (!quiet)
                AudioService.TryPlay(l => l.RepairDone);
        }

        // ---------------- 주민 ----------------

        private void AttachResident(Request r, ResidentFigure figure)
        {
            var use = figure.gameObject.GetComponent<InteriorInteractable>();
            if (use == null)
                use = figure.gameObject.AddComponent<InteriorInteractable>();
            use.HoldSeconds = _s.HandHold;
            use.Blocked = null;
            use.Holding = null;
            use.Prompt = () => ResidentPrompt(r);
            use.Completed = r.Kind == RequestKind.Talk ? () => Complete(r) : (Action)null;
            r.ResidentUse = use;
        }

        private string ResidentPrompt(Request r)
        {
            string muted = "<color=#AFC4D8>";
            switch (r.Kind)
            {
                case RequestKind.Talk:
                    return $"{r.Name}{ResidentRequestRules.Particle(r.Name, "과", "와")} 이야기하기";
                case RequestKind.Fetch:
                    string item = ResidentRequestRules.ItemName(r.Item);
                    return r.Carrying ? $"{item} 건네기  {muted}{r.Name}</color>"
                        : $"{r.Name}의 부탁  {muted}{item}{ResidentRequestRules.Particle(item, "은", "는")} {RoomName(r.ItemRoom)}에 있어요</color>";
                case RequestKind.Water:
                    return $"{r.Name}의 부탁  {muted}시든 화분에 물을 주세요</color>";
                default:
                    return $"{r.Name}의 부탁  {muted}덜컹거리는 환풍구를 조여 주세요</color>";
            }
        }

        // ---------------- 끝내기 ----------------

        private void Complete(Request r)
        {
            if (r.Done)
                return;
            var resident = ResidentOf(r.ResidentId);
            string reward = null;
            if (resident != null)
                Sim.CompleteResidentRequest(resident, out reward);
            r.Done = true;
            r.DoneAge = r.Age;
            r.Reply = r.Kind == RequestKind.Talk ? ResidentRequestRules.TalkLine(resident != null ? resident.Traits : null, _rng.NextDouble())
                : ResidentRequestRules.Thanks(r.Kind);
            if (r.ResidentUse != null)
                Destroy(r.ResidentUse);
            r.ResidentUse = null;
            if (r.Bubble != null)
                r.Bubble.Set(r.Reply, Accent(r.Kind));
            _residents.RefreshMood(r.ResidentId);
            var figure = _residents.Find(r.ResidentId);
            if (figure != null && figure.Motion != null)
                figure.Motion.Celebrate();
            string head = r.Kind == RequestKind.Talk ? $"{r.Name}{ResidentRequestRules.Particle(r.Name, "과", "와")} 이야기했어요" : $"{r.Name}의 부탁을 들어줬어요";
            Message?.Invoke(reward != null ? $"{head} · {reward}" : head, false);
            AudioService.TryPlay(l => l.EventPositive, 0.7f);
        }

        /// <summary>요청 정리 (만료 · 주민이 사라짐 · 다시 만들기). 손해 없이 조용히 — 시든 화분은 되살리고 환풍구는 조여 둠.</summary>
        private void Cancel(Request r, bool rebuilt)
        {
            if (r.Bubble != null)
                Destroy(r.Bubble.gameObject);
            r.Bubble = null;
            if (r.ResidentUse != null)
                Destroy(r.ResidentUse);
            r.ResidentUse = null;
            if (rebuilt)
                return; // 방 오브젝트 아래 소품 · 식물 · 인물은 이미 지워짐
            ClearPlant(r, wilt: false);
            if (r.ItemObject != null)
                Destroy(r.ItemObject);
            r.ItemObject = null;
            FixVent(r, quiet: true);
        }

        // ---------------- 표시 ----------------

        private static string BubbleText(Request r)
        {
            string ask = r.Kind == RequestKind.Fetch && r.Carrying
                ? $"{ResidentRequestRules.ItemName(r.Item)}! 여기요, 여기!"
                : ResidentRequestRules.Ask(r.Kind, r.Item, RoomName(r.ItemRoom));
            return $"<size=78%><color=#{ColorUtility.ToHtmlStringRGB(Accent(r.Kind) * 0.8f)}><b>{ResidentRequestRules.Title(r.Kind)}</b></color></size>\n{ask}";
        }

        public static Color Accent(RequestKind kind)
        {
            switch (kind)
            {
                case RequestKind.Water: return WaterColor;
                case RequestKind.Fetch: return FetchColor;
                case RequestKind.Fix: return FixColor;
                default: return TalkColor;
            }
        }

        private static string RoomName(ModuleInstance m) => m != null && m.Data != null ? m.Data.DisplayName : "다른 방";

        private static void AddBoxCollider(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            var col = go.AddComponent<BoxCollider>();
            if (renderers.Length == 0)
                return;
            var b = renderers[0].bounds;
            foreach (var x in renderers)
                b.Encapsulate(x.bounds);
            // 세로축으로만 돌려 놓으므로 월드 상자를 로컬로 옮겨도 넉넉한 정도
            col.center = go.transform.InverseTransformPoint(b.center);
            col.size = Vector3.Max(b.size, new Vector3(0.25f, 0.15f, 0.25f));
        }
    }
}
