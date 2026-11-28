using System.Collections.Generic;
using SpaceStation.Audio;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// CONNECT_1121 3번 ④ V2 (사용자 결정 2026-11-28): 기본 사고 넷의 좋은 · 나쁜 이웃 변형 — 인접 관계의 안쪽 얼굴(<see cref="InnerFace"/>).
    /// - 원인: 그 방에서 무엇이 얼마나 자주 (환기 경보 잦음 = 그 방에서 뽑힐 확률 2배, 전체 빈도는 그대로)
    /// - 과정: 도구가 어디 있고 몇 단계인지 (옆 수원 방 문가 밸브 · 정비 베이 쪽 문 옆 소화기 · 불길 두 곳 · 스위치 하나 · 틀려도 그대로 · 환기구만 · 필터 두 장)
    /// - 결과: 놓쳤을 때 (물 회수 · 만족도 지킴 · 코어가 처리 · 절반 · 나쁜 이웃 방으로 한 번 번짐 — 손상 통제실이 옆이면 안 번짐)
    /// - 읽히게: 원인 · 결과 한 줄을 알림 줄과 말풍선 둘째 줄에 (이웃이 중립이면 없음, 해결 뒤에는 "옆에 ○○가 있었다면 …")
    /// 튜토리얼 사고 · 진화 방(태그 없음)은 변형 없음.
    /// </summary>
    public sealed partial class InteriorIncidents
    {
        /// <summary>불길 두 곳일 때 처리 시간 배율 (A8 "처리 시간 짧음").</summary>
        private const float TwoFlamesWindow = 0.7f;
        /// <summary>환기구만일 때 F 누르는 시간 배율.</summary>
        private const float EasyFilterHold = 0.5f;

        private sealed partial class Incident
        {
            /// <summary>이 사고에 걸린 안쪽 얼굴 (그 사고 종류에 해당하는 것만).</summary>
            public InnerFace Inner;
            public readonly List<InnerHit> Hits = new List<InnerHit>();
            /// <summary>원인 한 줄 (알림 줄) · 말풍선 둘째 줄 — 비면 없음.</summary>
            public string Cause, CauseShort;
            /// <summary>나쁜 이웃 방에서 번져 온 사고 (다시 번지지 않음).</summary>
            public bool Spread;
            /// <summary>처리 시간 (초).</summary>
            public float Window;
            // 누출: 옆 수원 방 문가 밸브
            public GameObject Valve2;
            public Transform Wheel2;
            public Quaternion Wheel2Rest;
            public ModuleInstance Valve2Room;
            public Transform HoldWheel;
            // 화재: 문 옆 소화기 · 둘째 불길
            public readonly List<GameObject> Bottles = new List<GameObject>();
            public GameObject TakenBottle;
            public FireSpot Fire2;
            public bool MainOut;
            public Transform SprayAt;
            // 환기: 환기구만 · 필터 두 장
            public bool VentOnly;
            public int FiltersLeft = 1;
        }

        private sealed class FireSpot
        {
            public GameObject Prop, Soot;
            public ParticleSystem Flames, Smoke;
            public Light Glow;
            public ObjectLoop Loop;
            public bool Out;
        }

        private readonly List<InnerHit> _hitScratch = new List<InnerHit>();
        private readonly List<IncidentKind> _biased = new List<IncidentKind>();

        /// <summary>그 방의 안쪽 얼굴 관계 (이웃 방마다).</summary>
        private void HitsFor(ModuleInstance room, List<InnerHit> hits)
        {
            if (Sim.Adjacency == null)
            {
                hits.Clear();
                return;
            }
            Sim.Adjacency.InnerFor(Sim.Grid, room, hits);
        }

        private static bool Has(List<InnerHit> hits, InnerFace face)
        {
            foreach (var h in hits)
            {
                if ((h.Face & face) != 0)
                    return true;
            }
            return false;
        }

        /// <summary>환기 경보가 잦은 방이면 종류 목록에 환기 경보를 한 번 더 (뽑힐 확률 2배).</summary>
        private IReadOnlyList<IncidentKind> Biased(IReadOnlyList<IncidentKind> kinds, List<InnerHit> hits)
        {
            if (!Has(hits, InnerFace.FilterFrequent) || !Contains(kinds, IncidentKind.Filter))
                return kinds;
            _biased.Clear();
            for (int i = 0; i < kinds.Count; i++)
                _biased.Add(kinds[i]);
            _biased.Add(IncidentKind.Filter);
            return _biased;
        }

        /// <summary>그 사고 종류에 해당하는 안쪽 얼굴만.</summary>
        private static InnerFace Relevant(IncidentKind kind)
        {
            const InnerFace miss = InnerFace.MissNoMood | InnerFace.MissNoLoss | InnerFace.MissHalf | InnerFace.NoSpread;
            switch (kind)
            {
                case IncidentKind.Leak: return miss | InnerFace.LeakNeighborValve;
                case IncidentKind.Fire: return miss | InnerFace.FireDoorExtinguisher | InnerFace.FireTwoFlames;
                case IncidentKind.Breaker: return miss | InnerFace.BreakerOneSwitch | InnerFace.BreakerForgiving | InnerFace.BreakerBacked;
                case IncidentKind.Filter: return miss | InnerFace.FilterEasy | InnerFace.FilterTwo | InnerFace.FilterRadiation;
                default: return miss; // 단계 상황: 놓침 손실 · 번짐만 (진화 방 태그는 ⑥에서)
            }
        }

        /// <summary>사고를 놓기 전에: 이웃 관계 → 안쪽 얼굴 · 처리 시간.</summary>
        private void Prepare(Incident a, List<InnerHit> hits)
        {
            a.Hits.Clear();
            a.Inner = InnerFace.None;
            if (!a.Tutorial)
            {
                var relevant = Relevant(a.Kind);
                foreach (var h in hits)
                {
                    a.Hits.Add(h);
                    a.Inner |= h.Face & relevant;
                }
                if ((a.Inner & InnerFace.FilterEasy) != 0 && (a.Inner & InnerFace.FilterTwo) != 0)
                    a.Inner &= ~InnerFace.FilterEasy; // 쉬움 · 어려움이 겹치면 어려운 쪽 (A5 폭 3단계 — 보통으로 상쇄하지 않고 나쁜 이웃을 읽히게)
            }
            a.Window = Sim.Balance.IncidentWindow * (Is(a, InnerFace.FireTwoFlames) ? TwoFlamesWindow : 1f);
            a.FiltersLeft = Is(a, InnerFace.FilterTwo) ? 2 : 1;
            a.VentOnly = Is(a, InnerFace.FilterEasy);
        }

        private static bool Is(Incident a, InnerFace face) => (a.Inner & face) != 0;

        /// <summary>그 안쪽 얼굴을 준 이웃 방 (지은 방 먼저, 없으면 아무 쪽).</summary>
        private ModuleInstance NeighborWith(Incident a, InnerFace face, bool built = false)
        {
            ModuleInstance any = null;
            foreach (var h in a.Hits)
            {
                if ((h.Face & face) == 0 || h.Neighbor == null)
                    continue;
                if (_layout != null && _layout.Contains(h.Neighbor) && _builder.RoomParent(h.Neighbor) != null)
                    return h.Neighbor;
                any = any ?? h.Neighbor;
            }
            return built ? null : any;
        }

        private string Title(Incident a) => Is(a, InnerFace.FilterRadiation) && a.Kind == IncidentKind.Filter ? "방사선 경보" : InteriorIncidentRules.Title(a.Kind);

        /// <summary>사고가 뜰 때 원인 한 줄 · 말풍선 둘째 줄 (이웃이 중립이면 없음).</summary>
        private void WriteCause(Incident a)
        {
            a.Cause = a.CauseShort = null;
            void Set(string line, string shortLine)
            {
                if (a.Cause == null)
                {
                    a.Cause = line;
                    a.CauseShort = shortLine;
                }
                else
                {
                    a.Cause += " · " + line;
                    a.CauseShort += " · " + shortLine;
                }
            }
            string N(InnerFace f) => RoomName(NeighborWith(a, f));
            if (Is(a, InnerFace.FireTwoFlames))
                Set($"옆방 {N(InnerFace.FireTwoFlames)}의 열로 불길이 두 곳 · 시간이 짧아요", "옆방 열 · 불길 두 곳");
            if (Is(a, InnerFace.FilterRadiation))
                Set($"옆 {N(InnerFace.FilterRadiation)}의 방사선 경보", "옆방 방사선");
            if (Is(a, InnerFace.FilterTwo))
                Set($"옆 {N(InnerFace.FilterTwo)}의 먼지로 필터 두 장", "필터 두 장");
            if (Is(a, InnerFace.LeakNeighborValve) && a.Valve2 != null)
                Set($"옆방 {RoomName(a.Valve2Room)}에서도 잠글 수 있어요 (물 회수)", "옆방 밸브로도 잠금");
            if (Is(a, InnerFace.FireDoorExtinguisher) && a.Bottles.Count > 1)
                Set($"옆 {N(InnerFace.FireDoorExtinguisher)} 덕에 문 옆에 소화기가 하나 더", "문 옆 소화기");
            if (Is(a, InnerFace.BreakerOneSwitch))
                Set($"옆 {N(InnerFace.BreakerOneSwitch)}{Josa(N(InnerFace.BreakerOneSwitch), "이", "가")} 받쳐 스위치 하나만 내려갔어요", "스위치 하나만");
            if (Is(a, InnerFace.BreakerForgiving))
                Set($"옆 {N(InnerFace.BreakerForgiving)}{Josa(N(InnerFace.BreakerForgiving), "이", "가")} 정상 스위치를 알려 줘요 (틀려도 그대로)", "틀려도 그대로");
            if (Is(a, InnerFace.FilterEasy))
                Set($"옆 {N(InnerFace.FilterEasy)} 덕에 필터 교체 없이 환기구만", "환기구만");
        }

        /// <summary>해결했을 때 한 줄: 도와준 이웃 · 아니면 "옆에 ○○가 있었다면 …" (없으면 null).</summary>
        private string ResolveLine(Incident a)
        {
            if (Is(a, InnerFace.LeakNeighborValve) && a.Valve2 != null)
                return $"{RoomName(a.Valve2Room)}{Josa(RoomName(a.Valve2Room), "이", "가")} 옆에 있어 옆방에서도 잠글 수 있었어요";
            if (Is(a, InnerFace.FireDoorExtinguisher) && a.Bottles.Count > 1)
                return $"옆 {RoomName(NeighborWith(a, InnerFace.FireDoorExtinguisher))} 덕에 소화기가 문 옆에 있었어요";
            if (Is(a, InnerFace.BreakerOneSwitch) || Is(a, InnerFace.BreakerForgiving) || Is(a, InnerFace.FilterEasy))
                return null; // 뜰 때 이미 말함
            return Hint(a);
        }

        /// <summary>이 방에서 이 사고를 쉽게 해 줄 이웃 (아직 없는 것) — "옆에 ○○가 있었다면 …".</summary>
        private string Hint(Incident a)
        {
            if (a.Tutorial || Sim.Adjacency == null || a.Room?.Data == null)
                return null;
            InnerFace want;
            string then;
            switch (a.Kind)
            {
                case IncidentKind.Leak: want = InnerFace.LeakNeighborValve; then = "옆방에서도 잠그고 물을 회수했을 거예요"; break;
                case IncidentKind.Fire: want = InnerFace.FireDoorExtinguisher; then = "문 옆에 소화기가 하나 더 있었을 거예요"; break;
                case IncidentKind.Breaker: want = InnerFace.BreakerOneSwitch; then = "스위치가 하나만 내려갔을 거예요"; break;
                case IncidentKind.Filter: want = InnerFace.FilterEasy; then = "필터 교체 없이 환기구만 손봤을 거예요"; break;
                default: return null;
            }
            if (Is(a, want))
                return null;
            var rule = Sim.Adjacency.FindInnerRule(a.Room.Data, want);
            if (rule == null)
                return null;
            string who = AdjacencySystem.NeighborLabel(rule);
            return $"옆에 {who}{Josa(who, "이", "가")} 있었다면 {then}";
        }

        /// <summary>놓쳤을 때 이웃이 바꾸는 손실.</summary>
        private IncidentMiss MissMods(Incident a, out string why)
        {
            why = null;
            var m = IncidentMiss.None;
            if (Sim.Incidents.MissScale <= 0f)
                return m; // 놓쳐도 손실이 없는 판 (타이쿤 13-6) — 이웃이 바꿀 손실이 없음
            if (Is(a, InnerFace.MissNoLoss))
            {
                m |= IncidentMiss.NoLoss;
                why = $"옆 {RoomName(NeighborWith(a, InnerFace.MissNoLoss))}{Josa(RoomName(NeighborWith(a, InnerFace.MissNoLoss)), "이", "가")} 대신 처리했어요";
            }
            else if (a.Kind == IncidentKind.Breaker && Is(a, InnerFace.BreakerBacked))
            {
                m |= IncidentMiss.NoLoss;
                why = $"옆 {RoomName(NeighborWith(a, InnerFace.BreakerBacked))}{Josa(RoomName(NeighborWith(a, InnerFace.BreakerBacked)), "이", "가")} 받쳐 줬어요";
            }
            if (Is(a, InnerFace.MissHalf))
            {
                m |= IncidentMiss.Half;
                why = why ?? $"옆 {RoomName(NeighborWith(a, InnerFace.MissHalf))} 덕에 손실 절반";
            }
            if (Is(a, InnerFace.MissNoMood))
            {
                m |= IncidentMiss.NoMood;
                why = why ?? $"옆 {RoomName(NeighborWith(a, InnerFace.MissNoMood))} 덕에 만족도를 지켰어요";
            }
            if (a.Kind == IncidentKind.Leak && Is(a, InnerFace.LeakNeighborValve) && a.Resource == ResourceType.Water)
            {
                m |= IncidentMiss.Recovered;
                why = why ?? $"옆 {RoomName(NeighborWith(a, InnerFace.LeakNeighborValve))}{Josa(RoomName(NeighborWith(a, InnerFace.LeakNeighborValve)), "이", "가")} 샌 물을 회수했어요";
            }
            return m;
        }

        /// <summary>
        /// 놓친 사고가 번질 나쁜 이웃 방 (사용자 결정 2026-11-28: 나쁜 이웃 옆에서만 한 번 · 안에 있을 때만 · 같은 사고 · 손상 통제실이 옆이면 안 번짐).
        /// 번지지 않으면 null (blocked = 통제실이 막음).
        /// </summary>
        private ModuleInstance SpreadTarget(Incident a, out bool blocked, out AdjacencyRule rule)
        {
            blocked = false;
            rule = null;
            if (a.Tutorial || a.Spread || _layout == null)
                return null;
            ModuleInstance target = null;
            foreach (var h in a.Hits)
            {
                if (!h.Bad || h.Neighbor == null || h.Neighbor == a.Room || !_layout.Contains(h.Neighbor) || _builder.RoomParent(h.Neighbor) == null)
                    continue;
                if (!Contains(KindsFor(h.Neighbor), a.Kind))
                    continue;
                if (a.Kind == IncidentKind.Breaker && !InteriorIncidentRules.CanTrip(h.Neighbor.Data))
                    continue;
                target = h.Neighbor;
                rule = h.Rule;
                break;
            }
            if (target != null && Has(a.Hits, InnerFace.NoSpread))
            {
                blocked = true;
                return null;
            }
            return target;
        }

        /// <summary>번진 사고를 그 방에 바로 (자리가 없으면 false).</summary>
        private bool SpawnSpread(IncidentKind kind, ModuleInstance room, string from)
        {
            if (_active != null)
                return false;
            HitsFor(room, _hitScratch);
            var a = new Incident { Kind = kind, Room = room, Resource = InteriorIncidentRules.LeakResource(room.Data), Spread = true };
            Prepare(a, _hitScratch);
            if (!Place(a))
            {
                End(a, rebuilt: false);
                return false;
            }
            Activate(a, $"{from}에서 번졌어요");
            return true;
        }

        private readonly List<Vector3> _avoid = new List<Vector3>();

        /// <summary>
        /// 벽 자리 고르기 (CONNECT A5 — 소품이 여럿일 때): avoid 모두에서 minAvoid 이상 떨어진 자리 가운데
        /// near(문)가 있으면 문에 가장 가까운 곳 (문을 막지 않게 0.9m 이상), 없으면 avoid에서 가장 먼 곳.
        /// 플레이 확인 2026-11-28: 산소 생성기 방은 벽 자리 6곳이 한 벽 1.9m 안 — 1.4m를 띄우면 둘째 불길 · 소화기가 못 들어갔음.
        /// </summary>
        private GameObject MountPick(GameObject prefab, ModuleInstance room, float height, string name, Vector3? near, List<Vector3> avoid, float minAvoid, bool farthest = true)
        {
            var parent = _builder.RoomParent(room);
            if (prefab == null || parent == null)
                return null;
            InteriorSpots.Walls(_builder, room, _rng, height, _walls);
            int best = -1;
            float bestScore = float.MinValue;
            for (int i = 0; i < _walls.Count; i++)
            {
                var p = _walls[i].Position;
                float nearest = float.MaxValue;
                foreach (var q in avoid)
                    nearest = Mathf.Min(nearest, new Vector2(p.x - q.x, p.z - q.z).magnitude);
                if (nearest < minAvoid)
                    continue;
                float score;
                if (near.HasValue)
                {
                    float d = new Vector2(p.x - near.Value.x, p.z - near.Value.z).magnitude;
                    if (d < 0.9f)
                        continue;
                    score = -d;
                }
                else
                    score = farthest ? nearest : 0f; // farthest가 아니면 처음 맞는 자리 (벽 자리 순서는 섞여 있음 — 예전 Mount와 같음)
                if (score <= bestScore)
                    continue;
                bestScore = score;
                best = i;
                if (!farthest && !near.HasValue)
                    break;
            }
            if (best < 0)
                return null;
            var (pos, n) = _walls[best];
            var go = Instantiate(prefab, parent);
            go.name = name;
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(n, Vector3.up));
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// <summary>from 방에서 to 방으로 가는 문 (월드) — 위아래로만 이어졌거나 길이 없으면 null.</summary>
        private Vector3? DoorToward(ModuleInstance from, ModuleInstance to)
        {
            if (from == null || to == null)
                return null;
            var parent = _builder.RoomParent(from);
            var center = parent != null ? parent.position : Vector3.zero;
            if (_builder.TryGetWayPoint(from, to, center, out var point, out _, out bool lift) && !lift)
                return point;
            return null;
        }

        /// <summary>누출 · 좋은 이웃: 옆 수원 방의 이 방 쪽 문가에 차단 밸브 (어느 쪽이든 잠그면 해결).</summary>
        private void PlaceNeighborValve(Incident a)
        {
            var room = NeighborWith(a, InnerFace.LeakNeighborValve, built: true);
            if (room == null)
                return;
            _avoid.Clear();
            var door = DoorToward(room, a.Room);
            var valve = door.HasValue ? MountPick(_s.Leak, room, _s.LeakHeight, "IncidentLeakShutoff", door, _avoid, 0f)
                                      : Mount(_s.Leak, room, _s.LeakHeight, "IncidentLeakShutoff");
            if (valve == null)
                return;
            a.Extra.Add(valve);
            a.Valve2 = valve;
            a.Valve2Room = room;
            var t = valve.transform;
            Tint(valve, "M_IncBand", InteriorSupply.ResourceColor(ResourceType.Water));
            a.Wheel2 = t.Find("ValveWheel");
            if (a.Wheel2 != null)
                a.Wheel2Rest = a.Wheel2.localRotation;
            var col = valve.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.02f, 0.13f);
            col.size = new Vector3(0.7f, 0.3f, 0.26f);
            var use = valve.AddComponent<InteriorInteractable>();
            use.HoldSeconds = _s.ValveHold;
            use.Prompt = () => $"차단 밸브 잠그기  <color=#FF8A5C>옆방 {RoomName(a.Room)} 누출</color>";
            use.Holding = p =>
            {
                a.Holding = true;
                a.HoldProgress = p;
                a.HoldWheel = a.Wheel2;
            };
            use.Completed = () => Resolve(a);
        }

        /// <summary>화재 · 좋은 이웃: 정비 베이 쪽 문 옆에 소화기 하나 더.</summary>
        private void PlaceDoorExtinguisher(Incident a)
        {
            var maint = NeighborWith(a, InnerFace.FireDoorExtinguisher);
            FireAvoid(a);
            var door = DoorToward(a.Room, maint);
            var ext = door.HasValue ? MountPick(_s.Extinguisher, a.Room, _s.ExtinguisherHeight, "IncidentExtinguisherDoor", door, _avoid, 0.8f) : null;
            if (ext == null)
                return;
            a.Extra.Add(ext);
            AddBottle(a, ext);
        }

        /// <summary>소화기 하나를 들 수 있게 (들면 다른 소화기는 그대로 — 다 쓰면 든 것만 제자리에).</summary>
        private void AddBottle(Incident a, GameObject ext)
        {
            var bottleT = ext.transform.Find("ExtBottle");
            var bottle = bottleT != null ? bottleT.gameObject : ext;
            a.Bottles.Add(bottle);
            if (a.Bottle == null)
                a.Bottle = bottle;
            var bc = bottle.AddComponent<BoxCollider>();
            FitCollider(bc, bottle);
            var bu = bottle.AddComponent<InteriorInteractable>();
            bu.HoldSeconds = _s.PickupHold;
            bu.Prompt = () => a.Carrying ? "소화기를 이미 들고 있어요" : "소화기 들기";
            bu.Blocked = () => a.Carrying ? "소화기를 이미 들고 있어요" : null;
            bu.Completed = () =>
            {
                if (a.Carrying)
                    return;
                a.Carrying = true;
                a.TakenBottle = bottle;
                AudioService.TryPlayAt(l => l.ExtinguisherPickup, bottle.transform.position);
                bottle.SetActive(false);
                Message?.Invoke(a.Fire2 != null && !a.Fire2.Out && !a.MainOut ? "소화기를 들었어요 · 불길 두 곳에 F를 길게 누르세요" : "소화기를 들었어요 · 불에 대고 F를 길게 누르세요", false);
            };
        }

        /// <summary>불 · 소화기 자리 (서로 떨어뜨림).</summary>
        private void FireAvoid(Incident a)
        {
            _avoid.Clear();
            if (a.Prop != null)
                _avoid.Add(a.Prop.transform.position);
            if (a.Fire2 != null && a.Fire2.Prop != null)
                _avoid.Add(a.Fire2.Prop.transform.position);
            foreach (var b in a.Bottles)
            {
                if (b != null)
                    _avoid.Add(b.transform.position);
            }
        }

        /// <summary>
        /// 이웃 변형이 있는 화재의 소화기: 불길(둘째 포함)에서 1.4m 떨어진 첫 자리 (예전 규칙), 없으면 가장 먼 자리 (0.5m 이상 — 좁은 방).
        /// 둘째 불길을 먼저 놓고 소화기를 남은 자리에 (나쁜 이웃이 보이게, 플레이 확인 2026-11-28).
        /// </summary>
        private GameObject MountExtinguisherTight(Incident a)
        {
            FireAvoid(a);
            return MountPick(_s.Extinguisher, a.Room, _s.ExtinguisherHeight, "IncidentExtinguisher", null, _avoid, 1.4f, farthest: false)
                   ?? MountPick(_s.Extinguisher, a.Room, _s.ExtinguisherHeight, "IncidentExtinguisher", null, _avoid, 0.5f);
        }

        /// <summary>화재 · 나쁜 이웃(인화 위험): 같은 방 다른 벽에 불길 하나 더 (불 · 소화기에서 가장 먼 자리).</summary>
        private void PlaceSecondFire(Incident a)
        {
            FireAvoid(a);
            var box = MountPick(_s.FireBox, a.Room, _s.FireHeight, "IncidentFire2", null, _avoid, 0.8f);
            if (box == null)
                return;
            a.Extra.Add(box);
            var t = box.transform;
            var spot = new FireSpot { Prop = box };
            var soot = t.Find("Soot");
            spot.Soot = soot != null ? soot.gameObject : null;
            if (spot.Soot != null)
                spot.Soot.SetActive(false);
            spot.Flames = InteriorFx.Flames(t, t.TransformPoint(new Vector3(0f, 0.125f, 0.07f)), _spark, 0.26f);
            spot.Smoke = InteriorFx.Plume(t, t.TransformPoint(new Vector3(0f, 0.2f, 0.08f)), Vector3.up, _smoke, new Color(0.18f, 0.17f, 0.16f, 1f), 14f, 0.26f, 0.5f);
            var glowGo = new GameObject("FireGlow");
            glowGo.transform.SetParent(t, false);
            glowGo.transform.localPosition = new Vector3(0f, 0.35f, 0.5f);
            spot.Glow = glowGo.AddComponent<Light>();
            spot.Glow.type = LightType.Point;
            spot.Glow.shadows = LightShadows.None;
            spot.Glow.color = new Color(1f, 0.55f, 0.25f);
            spot.Glow.range = 2.6f;
            spot.Glow.intensity = 0.35f;
            spot.Loop = AudioService.AttachLoop(l => l.FireLoop, t);
            if (spot.Loop != null)
            {
                spot.Loop.transform.localPosition = new Vector3(0f, 0.125f, 0.07f);
                spot.Loop.Level = 0f;
            }
            var col = box.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.05f, 0.08f);
            col.size = new Vector3(0.4f, 0.5f, 0.2f);
            var use = box.AddComponent<InteriorInteractable>();
            use.HoldSeconds = _s.FireHold;
            use.Prompt = () => $"소화기로 불 끄기  <color=#FF8A5C>불길 두 곳</color>";
            use.Blocked = () => a.Carrying ? null : "벽에 걸린 소화기를 먼저 드세요";
            use.Holding = p =>
            {
                a.Holding = true;
                a.HoldProgress = p;
                a.SprayAt = t;
            };
            use.Completed = () => PutOut(a, main: false);
            a.Fire2 = spot;
        }

        /// <summary>불길 하나를 끔 — 남은 불길이 있으면 그것만, 다 끄면 해결.</summary>
        private void PutOut(Incident a, bool main)
        {
            if (a.Done)
                return;
            if (a.Fire2 == null || (main ? a.Fire2.Out : a.MainOut))
            {
                Resolve(a);
                return;
            }
            if (main)
            {
                a.MainOut = true;
                StopFx(a.Flames);
                StopFx(a.Smoke);
                a.Flames = a.Smoke = null;
                if (a.Glow != null)
                    Destroy(a.Glow.gameObject);
                a.Glow = null;
                if (a.Soot != null)
                    a.Soot.SetActive(true);
                if (a.Loop != null)
                    a.Loop.Level = 0f;
                foreach (var use in a.Prop.GetComponents<InteriorInteractable>())
                    Destroy(use);
            }
            else
                Douse(a.Fire2);
            AudioService.TryPlay(l => l.RepairDone);
            Message?.Invoke("불길 하나를 껐어요 · 한 곳 남았어요", false);
        }

        private static void Douse(FireSpot f)
        {
            if (f == null || f.Out)
                return;
            f.Out = true;
            StopFx(f.Flames);
            StopFx(f.Smoke);
            if (f.Glow != null)
                Destroy(f.Glow.gameObject);
            if (f.Soot != null)
                f.Soot.SetActive(true);
            if (f.Prop != null)
            {
                foreach (var use in f.Prop.GetComponentsInChildren<InteriorInteractable>())
                    Destroy(use);
            }
            if (f.Loop != null)
                f.Loop.Level = 0f;
        }

        /// <summary>한글 이름 뒤 조사 (받침 있으면 withBatchim) — "로/으로"는 ㄹ 받침도 "로".</summary>
        private static string Josa(string word, string withBatchim, string without)
        {
            if (string.IsNullOrEmpty(word))
                return without;
            char c = word[word.Length - 1];
            if (c < 0xAC00 || c > 0xD7A3)
                return without;
            int jong = (c - 0xAC00) % 28;
            if (withBatchim == "으로")
                return jong == 0 || jong == 8 ? "로" : "으로";
            return jong == 0 ? without : withBatchim;
        }
    }
}
