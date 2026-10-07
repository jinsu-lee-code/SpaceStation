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

        [Header("작업복 색 (일터 특성)")]
        public Color SuitDefault = new Color(0.25f, 0.42f, 0.68f);
        public Color SuitTechnical = new Color(0.86f, 0.46f, 0.16f);   // 기술자 · 정비공
        public Color SuitScience = new Color(0.86f, 0.89f, 0.92f);     // 과학자
        public Color SuitGarden = new Color(0.33f, 0.58f, 0.3f);       // 원예가
        public Color Accent = new Color(0.95f, 0.76f, 0.22f);
        public Color[] SkinTones =
        {
            new Color(0.96f, 0.8f, 0.68f), new Color(0.87f, 0.68f, 0.52f), new Color(0.72f, 0.52f, 0.38f),
            new Color(0.55f, 0.37f, 0.25f), new Color(0.38f, 0.25f, 0.17f),
        };
        public Color[] HairColors =
        {
            new Color(0.08f, 0.06f, 0.05f), new Color(0.25f, 0.15f, 0.08f), new Color(0.55f, 0.38f, 0.2f),
            new Color(0.8f, 0.65f, 0.4f), new Color(0.6f, 0.6f, 0.62f), new Color(0.45f, 0.12f, 0.08f),
        };
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
        private ResidentFigure[] _prefabs; // 자세 순서 (Stand, Sit, Work)
        private TMP_FontAsset _font;
        private Transform _eye;

        private readonly List<ResidentFigure> _figures = new List<ResidentFigure>();
        private readonly List<PlacementRoom> _rooms = new List<PlacementRoom>();
        private readonly List<ModuleInstance> _roomModules = new List<ModuleInstance>();
        private readonly List<PlacementPerson> _people = new List<PlacementPerson>();
        private readonly List<Placement> _placements = new List<Placement>();
        private TextMeshPro _tag;
        private ResidentFigure _looked;

        public int Count => _figures.Count;

        public static InteriorResidents Create(Transform parent, StationController station, InteriorBuilder builder, InteriorResidentTuning tuning,
            ResidentFigure[] prefabs, TMP_FontAsset font, Transform eye)
        {
            var go = new GameObject("InteriorResidents");
            go.transform.SetParent(parent, false);
            var r = go.AddComponent<InteriorResidents>();
            r._station = station;
            r._builder = builder;
            r._tuning = tuning ?? new InteriorResidentTuning();
            r._prefabs = prefabs;
            r._font = font;
            r._eye = eye;
            return r;
        }

        public void Rebuild(InteriorLayout layout)
        {
            foreach (var f in _figures)
            {
                if (f != null)
                    Destroy(f.gameObject);
            }
            _figures.Clear();
            HideTag();
            if (layout == null || _station == null || _station.Simulation == null || _prefabs == null || _prefabs.Length < 3)
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
                if (!_builder.TryGetTemplateRoom(room.Module, out var template, out _) || template.ResidentSpots.Count == 0)
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
                    Usable = _station.Connectivity.IsActive(room.Module) && !sim.Damage.TryGetInfo(room.Module, out _),
                });
                _roomModules.Add(room.Module);
            }

            // 주민
            _people.Clear();
            foreach (var r in roster.Residents)
                _people.Add(new PlacementPerson { Id = r.Id, HomeRoom = r.Home != null ? _roomModules.IndexOf(r.Home) : -1, Traits = r.Traits });

            bool day = sim.DayNight.IsDay(sim.ElapsedSeconds);
            ResidentPlacementRules.Plan(_people, _rooms, day, _tuning.LeisureShare, _placements);

            // 방마다 자리를 차례로 (일하는 사람 → 일하는 자리, 나머지 → 앉기 먼저)
            var nextWork = new int[_rooms.Count];
            var nextRest = new int[_rooms.Count];
            foreach (var p in _placements)
            {
                var module = _roomModules[p.Room];
                _builder.TryGetTemplateRoom(module, out var template, out var instance);
                var spot = NextSpot(template, p.Work, ref nextWork[p.Room], ref nextRest[p.Room]);
                if (spot == null)
                    continue;
                var resident = roster.Residents[p.Person];
                Spawn(resident, spot, instance, _builder.RoomParent(module), Status(resident, module, p, day), roster.Config);
            }
        }

        private static ResidentSpot NextSpot(InteriorTemplate template, bool work, ref int nextWork, ref int nextRest)
        {
            int seen = 0;
            foreach (var s in template.ResidentSpots)
            {
                if (s.Work != work)
                    continue;
                if (seen++ == (work ? nextWork : nextRest))
                {
                    if (work) nextWork++;
                    else nextRest++;
                    return s;
                }
            }
            return null;
        }

        private void Spawn(Resident resident, ResidentSpot spot, Transform instance, Transform parent, string status, ResidentConfig config)
        {
            var prefab = _prefabs[(int)spot.Pose];
            if (prefab == null)
                return;
            var position = instance.TransformPoint(spot.Position);
            if (spot.Pose == ResidentPose.Sit)
                position.y -= _tuning.SitDrop;
            var rotation = instance.rotation * Quaternion.Euler(0f, spot.Yaw, 0f);
            var figure = Instantiate(prefab, position, rotation, parent != null ? parent : transform);
            figure.name = "Resident_" + resident.Id;
            int id = resident.Id;
            var skin = Pick(_tuning.SkinTones, id * 7 + 3);
            var hair = Pick(_tuning.HairColors, id * 13 + 5);
            figure.Configure(Label(resident, status, config), SuitColor(resident), _tuning.Accent, skin, hair, id * 1.37f, _eye, _tuning.WatchRange);
            _figures.Add(figure);
        }

        private static Color Pick(Color[] colors, int seed)
        {
            if (colors == null || colors.Length == 0)
                return Color.white;
            return colors[(int)((uint)seed % (uint)colors.Length)];
        }

        private Color SuitColor(Resident r)
        {
            foreach (var t in r.Traits)
            {
                switch (t)
                {
                    case ResidentTrait.Technician:
                    case ResidentTrait.Mechanic: return _tuning.SuitTechnical;
                    case ResidentTrait.Scientist: return _tuning.SuitScience;
                    case ResidentTrait.Gardener: return _tuning.SuitGarden;
                }
            }
            return _tuning.SuitDefault;
        }

        private static string Status(Resident r, ModuleInstance room, Placement p, bool day)
        {
            string place = room.Data != null ? room.Data.DisplayName : "";
            if (p.Work)
                return $"일하는 중 · {place}";
            if (room == r.Home)
                return day ? "집에서 쉬는 중" : "집 · 쉬는 시간";
            return $"쉬는 중 · {place}";
        }

        private static string Label(Resident r, string status, ResidentConfig config)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("<b>").Append(r.Name).Append("</b>\n<size=70%>");
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
