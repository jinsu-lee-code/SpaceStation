using System;
using System.Collections.Generic;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>11-11 주민 연출 수치 (InteriorMode 인스펙터에서 조정).</summary>
    [Serializable]
    public sealed class InteriorResidentTuning
    {
        [Tooltip("낮에 일터가 없는 주민 중 여가 방(휴게실 · 회전 링 · 코어)에 가 있는 비율")]
        [Range(0f, 1f)] public float LeisureShare = 0.5f;
        [Tooltip("이 거리 안에서 바라보면 이름표 (m)")]
        public float TagDistance = 4f;
        [Tooltip("이 거리 안에 오면 플레이어 쪽으로 고개를 돌림 (m)")]
        public float WatchRange = 3f;
        [Tooltip("앉기 자세: 앉는 면에서 인물 원점(발 사이 바닥)까지 내려가는 높이 (골반 아래)")]
        public float SitDrop = 0.41f;
        public float TagFontSize = 1.6f;

        [Header("동물 주민 (11-11d)")]
        [Tooltip("동물 모델 배율 (모델 키 약 0.9m, 귀 포함 최대 1.15m — 사용자 결정 '1m 안팎')")]
        public float AnimalScale = 1f;

        [Header("생명감 (11-16)")]
        [Tooltip("서 있는 쉬는 주민이 같은 방 안의 서기 자리 사이를 가끔 걸어 다님 (방 밖으로는 안 나감)")]
        public bool Wander = true;
    }

    /// <summary>
    /// 11-11 내부의 주민. 들어갈 때 · 내부를 다시 만들 때(<see cref="Rebuild"/>) 명단을 <see cref="ResidentPlacementRules"/>로 배치해
    /// 템플릿 자리(<see cref="InteriorTemplate.ResidentSpots"/>)에 정지 인물을 세운다 (낮 = 일터 · 여가, 밤 = 집).
    /// 바라보면(<see cref="InteriorResidentTuning.TagDistance"/> 안) 머리 위에 이름 · 특성 · 지금 하는 일.
    /// 인물은 방 오브젝트 아래에 붙어 내부를 다시 만들 때 함께 지워진다.
    /// </summary>
    public sealed class InteriorResidents : MonoBehaviour
    {
        private StationController _station;
        private InteriorBuilder _builder;
        private InteriorResidentTuning _tuning;
        private AnimalModelSet _animals;
        private TMP_FontAsset _font;
        private Transform _eye;

        private readonly List<ResidentFigure> _figures = new List<ResidentFigure>();
        private readonly List<GameObject> _stools = new List<GameObject>();
        private readonly List<PlacementRoom> _rooms = new List<PlacementRoom>();
        private readonly List<ModuleInstance> _roomModules = new List<ModuleInstance>();
        private readonly List<PlacementPerson> _people = new List<PlacementPerson>();
        private readonly List<Placement> _placements = new List<Placement>();
        private TextMeshPro _tag;
        private ResidentFigure _looked;

        public int Count => _figures.Count;
        /// <summary>11-17 ③ 지금 세워 둔 인물들 (주민 요청이 고름).</summary>
        public IReadOnlyList<ResidentFigure> Figures => _figures;
        /// <summary>지금 바라봐서 이름표가 뜬 인물 (없으면 null) — 말풍선을 이름표 위로 올림.</summary>
        public ResidentFigure Looked => _tag != null && _tag.gameObject.activeSelf ? _looked : null;

        public ResidentFigure Find(int residentId)
        {
            foreach (var f in _figures)
            {
                if (f != null && f.ResidentId == residentId)
                    return f;
            }
            return null;
        }

        /// <summary>11-17 ③ 요청을 들어준 뒤: 기분(보너스 포함)을 다시 계산해 평소 동작에 반영.</summary>
        public void RefreshMood(int residentId)
        {
            var figure = Find(residentId);
            var sim = _station != null ? _station.Simulation : null;
            if (figure == null || sim == null || sim.Residents == null)
                return;
            foreach (var r in sim.Residents.Residents)
            {
                if (r.Id != residentId)
                    continue;
                figure.Configure(figure.Label, MoodOf(r), _eye, _tuning.WatchRange);
                return;
            }
        }

        private float MoodOf(Resident resident)
        {
            var sim = _station.Simulation;
            return ResidentMood.Of(sim.Residents.PersonalMood(resident), sim.Population.Satisfaction, resident.Home == null) + sim.CheerOf(resident);
        }

        public static InteriorResidents Create(Transform parent, StationController station, InteriorBuilder builder, InteriorResidentTuning tuning,
            AnimalModelSet animals, TMP_FontAsset font, Transform eye)
        {
            var go = new GameObject("InteriorResidents");
            go.transform.SetParent(parent, false);
            var r = go.AddComponent<InteriorResidents>();
            r._station = station;
            r._builder = builder;
            r._tuning = tuning ?? new InteriorResidentTuning();
            r._animals = animals != null && animals.IsValid ? animals : null;
            r._font = font;
            r._eye = eye;
            builder.RoomBuilt += r.SpawnRoom;
            return r;
        }

        private void OnDestroy()
        {
            if (_builder != null)
                _builder.RoomBuilt -= SpawnRoom;
        }

        /// <summary>방마다 세울 주민 (계획 순서대로) — 12-3c-opt: 배치는 들어갈 때 한 번에 정하고, 인물은 방을 지을 때 세운다.</summary>
        private readonly Dictionary<ModuleInstance, List<(Resident Resident, Placement Placement)>> _planned = new Dictionary<ModuleInstance, List<(Resident, Placement)>>();
        private readonly HashSet<ModuleInstance> _spawnedRooms = new HashSet<ModuleInstance>();
        private bool _day;

        public void Rebuild(InteriorLayout layout)
        {
            foreach (var f in _figures)
            {
                if (f != null)
                    Destroy(f.gameObject);
            }
            _figures.Clear();
            foreach (var s in _stools)
            {
                if (s != null)
                    Destroy(s);
            }
            _stools.Clear();
            _planned.Clear();
            _spawnedRooms.Clear();
            HideTag();
            if (layout == null || _station == null || _station.Simulation == null || _animals == null)
                return;
            var sim = _station.Simulation;
            var roster = sim.Residents;
            if (roster == null)
                return;

            // 방 (템플릿 자리가 있는 방만)
            _rooms.Clear();
            _roomModules.Clear();
            foreach (var room in layout.Rooms)
            {
                var template = _builder.TemplateFor(room.Module);
                if (template == null || template.ResidentSpots.Count == 0)
                    continue;
                int work = 0, rest = 0;
                foreach (var s in template.ResidentSpots)
                {
                    if (s.Work) work++;
                    else rest++;
                }
                _rooms.Add(new PlacementRoom
                {
                    WorkSpots = work,
                    RestSpots = rest,
                    WorkTraits = template.WorkTraits,
                    Leisure = template.Leisure,
                    OpenWork = template.OpenWork,
                    Usable = _station.Connectivity.IsActive(room.Module) && !sim.Damage.TryGetInfo(room.Module, out _),
                });
                _roomModules.Add(room.Module);
            }

            // 주민
            _people.Clear();
            foreach (var r in roster.Residents)
                _people.Add(new PlacementPerson { Id = r.Id, HomeRoom = r.Home != null ? _roomModules.IndexOf(r.Home) : -1, Traits = r.Traits });

            _day = sim.DayNight.IsDay(sim.ElapsedSeconds);
            ResidentPlacementRules.Plan(_people, _rooms, _day, _tuning.LeisureShare, _placements);
            foreach (var p in _placements)
            {
                var module = _roomModules[p.Room];
                if (!_planned.TryGetValue(module, out var list))
                    _planned[module] = list = new List<(Resident, Placement)>();
                list.Add((roster.Residents[p.Person], p));
            }
            foreach (var module in _builder.BuiltRooms)
                SpawnRoom(module);
        }

        /// <summary>방 하나의 계획된 주민을 세운다 (일하는 사람 → 일하는 자리, 나머지 → 앉기 먼저).</summary>
        private void SpawnRoom(ModuleInstance module)
        {
            if (!_planned.TryGetValue(module, out var list) || !_spawnedRooms.Add(module))
                return;
            if (!_builder.TryGetTemplateRoom(module, out var template, out var instance))
                return;
            var roster = _station.Simulation.Residents;
            int nextWork = 0;
            var taken = new HashSet<ResidentSpot>();
            (WanderRoom Room, List<ResidentSpot> Spots) wr = default;
            foreach (var (resident, p) in list)
            {
                // 11-16 ③ 쉬는 주민 절반(주민 번호)은 서는 자리를 먼저 — 앉기 먼저만 채우면 모두 앉아 걸어 다니는 주민이 없었음
                bool preferStand = _tuning.Wander && (resident.Id & 1) == 1;
                var spot = NextSpot(template, p.Work, preferStand, taken, ref nextWork);
                if (spot == null)
                    continue;
                var figure = Spawn(resident, spot, instance, _builder.RoomParent(module), Status(resident, module, p, _day, template.VisitStatus, template.WorkStatus), roster.Config,
                    out float mood);
                figure.Room = module;
                // 11-16 ③ 서 있는 쉬는 주민은 같은 방의 서기 자리 사이를 가끔 걸어 다님, ④ 앉은 주민은 일어나 걷다가 돌아와 앉음
                if (_tuning.Wander && !spot.Work)
                {
                    if (wr.Room == null)
                    {
                        wr = (new WanderRoom(), new List<ResidentSpot>());
                        foreach (var s in template.ResidentSpots)
                        {
                            if (s.Work || s.Pose != ResidentPose.Stand)
                                continue;
                            wr.Room.Add(instance.TransformPoint(s.Position), (instance.rotation * Quaternion.Euler(0f, s.Yaw, 0f)).eulerAngles.y);
                            wr.Spots.Add(s);
                        }
                    }
                    if (spot.Pose == ResidentPose.Sit)
                        figure.gameObject.AddComponent<ResidentWander>().InitSeated(wr.Room, mood, _eye, _tuning.AnimalScale, resident.Id);
                    else
                    {
                        int home = wr.Spots.IndexOf(spot);
                        if (home >= 0)
                            figure.gameObject.AddComponent<ResidentWander>().Init(wr.Room, home, mood, _eye, _tuning.AnimalScale, resident.Id);
                    }
                }
            }
        }

        /// <summary>
        /// 일하는 자리는 차례로. 쉬는 자리는 템플릿 순서(앉기 먼저)대로 빈 곳 — <paramref name="preferStand"/>면 빈 서기 자리를 먼저.
        /// </summary>
        private static ResidentSpot NextSpot(InteriorTemplate template, bool work, bool preferStand, HashSet<ResidentSpot> takenRest, ref int nextWork)
        {
            if (work)
            {
                int seen = 0;
                foreach (var s in template.ResidentSpots)
                {
                    if (!s.Work)
                        continue;
                    if (seen++ == nextWork)
                    {
                        nextWork++;
                        return s;
                    }
                }
                return null;
            }
            ResidentSpot first = null;
            foreach (var s in template.ResidentSpots)
            {
                if (s.Work || takenRest.Contains(s))
                    continue;
                if (!preferStand || s.Pose == ResidentPose.Stand)
                {
                    takenRest.Add(s);
                    return s;
                }
                if (first == null)
                    first = s;
            }
            if (first != null)
                takenRest.Add(first);
            return first;
        }

        /// <summary>11-11d 동물 주민: 종류 · 털색 = 주민 번호 (<see cref="AnimalLooks"/>). 13-3: 주민 종류(<see cref="Resident.Kind"/>)가 모델 Id면 그 동물.</summary>
        private ResidentFigure Spawn(Resident resident, ResidentSpot spot, Transform instance, Transform parent, string status, ResidentConfig config,
            out float mood)
        {
            int id = resident.Id;
            var species = FindSpecies(resident.Kind) ?? _animals.Species[AnimalLooks.Species(id, _animals.Species.Count)];
            var fur = species.FurColors.Length > 0 ? species.FurColors[AnimalLooks.Fur(id, species.FurColors.Length)] : Color.white;
            var position = instance.TransformPoint(spot.Position);
            if (spot.Pose == ResidentPose.Sit)
                position.y -= _tuning.SitDrop;
            var rotation = instance.rotation * Quaternion.Euler(0f, spot.Yaw, 0f);
            if (spot.Pose == ResidentPose.Work)
                position.y += PlaceStool(position, rotation, parent != null ? parent : transform);
            var figure = ResidentFigure.Create(_animals, species, fur, parent != null ? parent : transform, position, rotation, spot.Pose,
                _tuning.AnimalScale, _tuning.SitDrop, id);
            if (spot.Pose == ResidentPose.Sit)
                ResidentSeatFit.Fit(figure.transform, _tuning.SitDrop); // 다리 · 꼬리가 의자를 뚫지 않게 앞뒤 맞춤 (사용자 2026-10-29) — 앉은 자리 기억(ResidentWander)보다 먼저
            figure.name = "Resident_" + id;
            figure.ResidentId = id;
            // 11-16 기분 = 본인 보정(특성) + 정거장 만족도 + 집 없음 → 평소 동작 · 반응 (+ 11-17 ③ 요청을 들어준 보너스)
            mood = MoodOf(resident);
            figure.Configure(Label(resident, species.DisplayName, status, config), mood, _eye, _tuning.WatchRange);
            _figures.Add(figure);
            return figure;
        }

        /// <summary>
        /// 동물용 발판 (사용자 결정 2026-10-20): 작업 자리 앞 작업대 높이를 재서 손이 닿게 맞는 발판을 발밑에 두고, 올라선 높이를 돌려줌 (없으면 0).
        /// 작업대 = 앞 0.2 ~ 0.8m를 위에서 아래로 쏴 처음 맞은, 발보다 0.3m 이상 높은 면 (주민 · 다른 발판 제외).
        /// </summary>
        private float PlaceStool(Vector3 feet, Quaternion rotation, Transform parent)
        {
            if (_animals == null || _animals.Stools.Count == 0)
                return 0f;
            if (_syncedFrame != Time.frameCount) // 방을 막 지었으면 충돌체 위치를 광선에 반영 (자동 동기화 꺼짐)
            {
                Physics.SyncTransforms();
                _syncedFrame = Time.frameCount;
            }
            var forward = rotation * Vector3.forward;
            float surface = -1f;
            for (float d = 0.2f; d <= 0.8f && surface < 0f; d += 0.05f)
            {
                var from = feet + forward * d + Vector3.up * 1.6f;
                int n = Physics.RaycastNonAlloc(from, Vector3.down, _stoolHits, 1.6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                float best = float.MinValue;
                for (int i = 0; i < n; i++)
                {
                    var c = _stoolHits[i].collider;
                    if (c.GetComponentInParent<ResidentFigure>() != null || c.name == StoolName)
                        continue;
                    best = Mathf.Max(best, _stoolHits[i].point.y - feet.y); // 가장 위 = 처음 맞은 면
                }
                if (best > 0.3f)
                    surface = best;
            }
            if (surface < 0f)
                return 0f;
            var stool = _animals.StoolFor(surface, _tuning.AnimalScale);
            if (stool == null)
                return 0f;
            var go = Instantiate(stool.Model, parent, false);
            go.name = StoolName;
            go.transform.SetPositionAndRotation(feet, rotation);
            var mf = go.GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                var box = go.AddComponent<BoxCollider>(); // 플레이어가 지나가지 못하게
                box.center = mf.sharedMesh.bounds.center;
                box.size = mf.sharedMesh.bounds.size;
            }
            _stools.Add(go);
            return stool.Height;
        }

        private const string StoolName = "AnimalStool";
        private readonly RaycastHit[] _stoolHits = new RaycastHit[8];
        private int _syncedFrame = -1;

        private static string Status(Resident r, ModuleInstance room, Placement p, bool day, string visitStatus, string workStatus)
        {
            string place = room.Data != null ? room.Data.DisplayName : "";
            if (p.Work)
                return $"{(string.IsNullOrEmpty(workStatus) ? "일하는 중" : workStatus)} · {place}";   // 12-5 방사선 차폐실 = 물자 관리 중
            if (room == r.Home)
                return day ? "집에서 쉬는 중" : "집 · 쉬는 시간";
            return $"{(string.IsNullOrEmpty(visitStatus) ? "쉬는 중" : visitStatus)} · {place}";   // 12-5 행정 관제소 = 민원 보는 중
        }

        private AnimalSpecies FindSpecies(string kind)
        {
            if (string.IsNullOrEmpty(kind))
                return null;
            foreach (var s in _animals.Species)
                if (s != null && s.Id == kind)
                    return s;
            return null;
        }

        /// <summary>"<b>두부 기관사</b>" / 종류 · 특성 / 지금 하는 일 (11-11d 이름 + 직함, 승진 단계 포함 — 들어갈 때 정해짐).</summary>
        private static string Label(Resident r, string species, string status, ResidentConfig config)
        {
            var sb = new System.Text.StringBuilder();
            string title = config != null ? config.TitleOf(r.Traits, r.Rank) : null;
            sb.Append("<b>").Append(r.Name);
            if (!string.IsNullOrEmpty(title))
                sb.Append(' ').Append(title);
            sb.Append("</b>\n<size=70%>");
            if (!string.IsNullOrEmpty(species))
                sb.Append("<color=#E8D9B5>").Append(species).Append("</color> · ");
            for (int i = 0; i < r.Traits.Count; i++)
            {
                var def = config != null ? config.Get(r.Traits[i]) : null;
                if (i > 0)
                    sb.Append(" · ");
                sb.Append(def != null && !def.Positive ? "<color=#FF8A7A>" : "<color=#8FE39A>")
                  .Append(def != null ? def.DisplayName : r.Traits[i].ToString()).Append("</color>");
            }
            sb.Append("</size>\n<size=60%><color=#AFC4D8>").Append(status).Append("</color></size>");
            return sb.ToString();
        }

        // ---------------- 이름표 ----------------

        private void LateUpdate()
        {
            if (_eye == null)
                return;
            ResidentFigure target = null;
            if (Physics.Raycast(_eye.position, _eye.forward, out var hit, _tuning.TagDistance, ~0, QueryTriggerInteraction.Ignore))
                target = hit.collider.GetComponentInParent<ResidentFigure>();
            if (target == null)
            {
                HideTag();
                return;
            }
            EnsureTag();
            if (target != _looked)
            {
                _looked = target;
                _tag.SetText(target.Label);
            }
            if (!_tag.gameObject.activeSelf)
                _tag.gameObject.SetActive(true);
            _tag.transform.position = target.TagPoint;
            _tag.transform.rotation = Quaternion.LookRotation(_tag.transform.position - _eye.position, Vector3.up);
        }

        private void EnsureTag()
        {
            if (_tag != null)
                return;
            var go = new GameObject("ResidentTag");
            go.transform.SetParent(transform, false);
            _tag = go.AddComponent<TextMeshPro>();
            if (_font != null)
                _tag.font = _font;
            _tag.fontSize = _tuning.TagFontSize;
            _tag.alignment = TextAlignmentOptions.Bottom;
            _tag.rectTransform.sizeDelta = new Vector2(3f, 1f);
            _tag.rectTransform.pivot = new Vector2(0.5f, 0f);
            _tag.textWrappingMode = TextWrappingModes.NoWrap;
            _tag.color = Color.white;
            _tag.outlineWidth = 0.18f;
            _tag.outlineColor = new Color32(0, 0, 0, 220);
            go.SetActive(false);
        }

        private void HideTag()
        {
            _looked = null;
            if (_tag != null && _tag.gameObject.activeSelf)
                _tag.gameObject.SetActive(false);
        }
    }
}
