using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEngine;

namespace SpaceStation.Editor.Balance
{
    /// <summary>
    /// 제자리 봇 (CONNECT_1121 0번 기준선, 사용자 결정 2026-11-26): 정해진 등급에 오르면 확장을 멈추고 유지만 한다 — "넓히지 않아도 사는가".
    /// - 멈춘 때 차지한 <b>칸 수를 넘지 않는다</b> (모듈 수가 아니라 칸 — 합성으로 모듈 수가 줄어도 넓어지지 않게).
    /// - 그 안에서는 평소처럼 수리 · 정비 · 재건축 · 연구 · 보급 상자 · 유성우 막기 · 합성을 한다. 부서져 빈 칸은 다시 채운다.
    /// - 새로 지을 자리가 모자라면 <b>남는 모듈을 철거하고 바꿔 짓는다</b>: 남는 장갑 격벽 → 남는 창고 → 남는 생존 자원 생산 → 빈 거주 순.
    ///   철거해도 다른 모듈이 코어와 끊기지 않는 것만, 지으려는 것과 같은 종류는 철거하지 않음 (같은 것끼리 바꾸기 방지).
    /// - 합성 결과가 재료보다 칸이 많아 상한을 넘으면 남는 모듈을 철거해 되돌리고, 되돌릴 때까지 새로 짓지 않는다.
    /// - 보완 (사용자 결정 2026-11-26 "안정된 뒤 고정 + 위기엔 바꾸기", 첫 측정에서 오른 순간 고정한 판이 밤 전력 · 산소로 몇 분 만에 무너짐):
    ///   오른 뒤 <see cref="HoldStable"/>이 1분 이어지면 고정 (최대 10분, <see cref="BalanceRun"/>) · 전력 · 산소 · 물 · 식량이 실제로 바닥나는 중이면(<see cref="InCrisis"/>) 남지 않은
    ///   거주 (인구가 줄어듦) → 서비스 → 연구소도 바꾼다 · 생존 자원 생산은 남은 거주 칸까지 인구가 차는 몫을 빼고도 남을 때만 "남는다".
    /// </summary>
    public sealed partial class BalanceBot
    {
        /// <summary>철거 판단의 여유: 짓는 기준(<see cref="NetMargin"/>)보다 이만큼 더 남을 때만 "남는다" (짓고 부수기를 되풀이하지 않게).</summary>
        private const float HoldSurplusMargin = 1f;

        /// <summary>칸 수 상한 (-1 = 제자리 봇 아님).</summary>
        public int HoldCellCap { get; private set; } = -1;
        public bool Holding => HoldCellCap >= 0;
        /// <summary>바꿔 짓느라 철거한 모듈 수 · 칸이 모자라 짓지 못한 횟수 (결정 단위).</summary>
        public int HoldDemolished { get; private set; }
        public int HoldBlocked { get; private set; }
        /// <summary>그중 위기라 남지 않은 모듈(거주 · 서비스 · 연구소)을 바꾼 수.</summary>
        public int HoldCrisisDemolished { get; private set; }
        /// <summary>철거 기록 (언제 · 무엇을 · 무엇을 지으려고 · 그때 자원) — 최대 300줄, 리포트 hold_events.txt.</summary>
        public List<string> HoldEvents { get; } = new List<string>();
        /// <summary>철거한 모듈 종류별 개수 (표시 이름 → 개수).</summary>
        public Dictionary<string, int> HoldDemolishedKinds { get; } = new Dictionary<string, int>();

        private readonly List<ModuleInstance> _holdCandidates = new List<ModuleInstance>();
        private readonly HashSet<ModuleInstance> _holdReached = new HashSet<ModuleInstance>();
        private readonly Queue<ModuleInstance> _holdQueue = new Queue<ModuleInstance>();
        private readonly List<ModuleInstance> _holdNeighbors = new List<ModuleInstance>();

        /// <summary>지금 차지한 칸 수를 상한으로 확장을 멈춘다.</summary>
        public void StartHold()
        {
            HoldCellCap = OccupiedCells();
        }

        public int OccupiedCells()
        {
            int cells = 0;
            foreach (var m in _sim.Grid.Modules)
                cells += m.Cells.Count;
            return cells;
        }

        /// <summary>
        /// 결정마다 맨 앞: 상한을 넘었으면(합성 결과가 더 큰 경우) 남는 모듈을 철거해 되돌린다.
        /// 반환: 새 건설을 해도 되는지 (상한 안).
        /// </summary>
        private bool HoldWithinCap()
        {
            if (!Holding)
                return true;
            HoldTrimHousing(); // 남은 거주 칸까지 인구가 차면 생존 자원이 모자랄 것 같으면 빈 거주를 줄여 인구를 묶음
            int over = OccupiedCells() - HoldCellCap;
            if (over <= 0)
                return true;
            MakeRoom(over, null);
            return OccupiedCells() <= HoldCellCap;
        }

        /// <summary>
        /// 제자리 봇이면 <paramref name="data"/>를 지을 칸을 상한 안에서 마련한다 (필요하면 철거). 제자리 봇이 아니면 늘 true.
        /// </summary>
        private bool HoldMakeRoomFor(ModuleData data)
        {
            if (!Holding || data == null)
                return true;
            int need = OccupiedCells() + data.CellOffsets.Count - HoldCellCap;
            if (need <= 0)
                return true;
            if (MakeRoom(need, data))
                return true;
            HoldBlocked++;
            return false;
        }

        /// <summary>남는 모듈을 철거해 <paramref name="cells"/>칸 이상을 비운다. <paramref name="wanted"/>와 같은 종류는 철거하지 않음.</summary>
        private bool MakeRoom(int cells, ModuleData wanted)
        {
            // 먼저 비울 수 있는지 본다 — 모자라면 아무것도 부수지 않음
            bool crisis = InCrisis(wanted);
            CollectCandidates(wanted, crisis, cells);
            if (CandidateCells() < cells)
            {
                if (wanted != null)
                    LogBlocked(wanted, cells, crisis);
                return false;
            }
            int freed = 0;
            while (freed < cells)
            {
                int surplusCount = CollectCandidates(wanted, crisis, cells - freed); // 하나 철거할 때마다 다시 (남는 정도 · 연결이 바뀜)
                if (_holdCandidates.Count == 0)
                    return false;
                var target = _holdCandidates[0];
                int size = target.Cells.Count;
                string name = target.Data.DisplayName;
                if (!_sim.TryRemove(target))
                    return false;
                freed += size;
                HoldDemolished++;
                if (surplusCount == 0)
                    HoldCrisisDemolished++;
                if (HoldEvents.Count < 300)
                {
                    var r = _sim.Resources;
                    HoldEvents.Add($"{_sim.ElapsedSeconds / 60f:0.0}분 {(surplusCount == 0 ? "위기" : "남음")} 철거 {name} → {(wanted != null ? wanted.DisplayName : "상한 되돌리기")}"
                                   + $" (인구 {r.Population}/{r.HousingCapacity} · 산소 {r.GetStock(ResourceType.Oxygen):0} {r.GetNetRate(ResourceType.Oxygen):+0.0;-0.0} 최저 {WorstNet(ResourceType.Oxygen):+0.0;-0.0}"
                                   + $" · 물 {r.GetStock(ResourceType.Water):0} · 식량 {r.GetStock(ResourceType.Food):0} · 전력 효율 {r.PowerEfficiency * 100f:0}%)");
                }
                HoldDemolishedKinds.TryGetValue(name, out int n);
                HoldDemolishedKinds[name] = n + 1;
            }
            return true;
        }

        /// <summary>남는 모듈 → (위기면) 남지 않은 모듈 순으로 후보를 모은다 (모은 칸이 <paramref name="cells"/>에 닿으면 멈춤). 반환: 그중 남는 모듈 수 (앞쪽).</summary>
        private int CollectCandidates(ModuleData wanted, bool crisis, int cells)
        {
            CollectSurplus(wanted, cells);
            int surplus = _holdCandidates.Count;
            if (crisis && CandidateCells() < cells)
                CollectCrisis(wanted, cells);
            return surplus;
        }

        private int CandidateCells()
        {
            int n = 0;
            foreach (var m in _holdCandidates)
                n += m.Cells.Count;
            return n;
        }

        /// <summary>
        /// 위기 (사용자 결정 2026-11-26 — 남지 않은 모듈도 바꿀 수 있음): 산소 · 물 · 식량 중 하나가 줄고 있으면서 한도의 <see cref="CrisisStockRatio"/> 아래이고,
        /// 파손된 생산을 고쳐도 메워지지 않으며, 지으려는 것이 그 부족을 실제로 메울 때만. 전력 부족은 생존 자원이 줄어드는 것으로 나타날 때 위기.
        /// (평소의 전력 여유 · 밤 효율 저하만으로는 위기가 아님 — 소형은 밤마다 효율이 떨어져도 산소 여유로 120분 버텼는데,
        ///  효율 기준으로 위기를 걸자 거주를 부숴 인구가 10 → 4로 줄었음)
        /// </summary>
        private bool InCrisis(ModuleData wanted)
        {
            if (wanted == null)
                return false;
            var r = _sim.Resources;
            // 지으려는 것이 그 부족을 실제로 메우는지 — 전력 효율이 절반 아래면 상시 · 저장 전력(배터리 · 핵융합로 · 연료전지)만,
            // 그 위면 산소 · 물 · 식량 생산만. 태양광은 밤 · 폭풍에 바로 메우지 못하므로 위기 대상 아님
            // (4차 확인: 전력 효율 25%인 밤에 거주를 부숴 산소 생성기를 더 지음 — 그것도 25%로 돎 / 도시급은 55%에서 산소 생성기가 필요했는데 막힘)
            bool powerShort = r.PowerEfficiency < CrisisPowerEfficiency;
            bool fixes = powerShort
                ? wanted == _battery || wanted == _fusion || wanted == _fuelCell
                : wanted == _oxygen || wanted == _water || wanted == _food;
            if (!fixes)
                return false;
            foreach (var type in LifeTypes)
                if (r.GetNetRate(type) < 0f && r.GetStock(type) < r.GetCapacity(type) * CrisisStockRatio
                    && r.GetNetRate(type) + DamagedOutput(type) < 0f) // 파손된 생산을 고치면 메워지는 부족은 위기가 아님 (수리를 기다림)
                    return true;
            return false;
        }

        private const float CrisisPowerEfficiency = 0.5f;

        /// <summary>
        /// 파손돼 멈춘 모듈의 그 자원 생산 합 (데이터 값). 4차 확인: 산소 생성기 파손으로 산소가 줄자 위기로 보고 거주를 부숴
        /// 새 생성기를 지었음 (수용 10 → 4) — 사람이라면 고치기를 기다림.
        /// </summary>
        private float DamagedOutput(ResourceType type)
        {
            float sum = 0f;
            foreach (var info in _sim.Damage.DamagedModules)
            {
                var data = info.Module != null ? info.Module.Data : null;
                if (data == null)
                    continue;
                foreach (var p in data.Production)
                    if (p.Type == type)
                        sum += p.Amount;
            }
            return sum;
        }

        private const float CrisisStockRatio = 0.25f;

        private static readonly ResourceType[] LifeTypes = { ResourceType.Oxygen, ResourceType.Water, ResourceType.Food };

        /// <summary>
        /// 위기 때 더 바꿀 수 있는 모듈 (사용자 결정 2026-11-26 "안정된 뒤 고정 + 위기엔 바꾸기"): 거주 (인구가 줄어듦 — 수용이 작은 것부터)
        /// → 서비스 → 연구소. 생존 자원 · 전력 · 금속 · 방어 · 수리 · 창고는 건드리지 않음. 모은 칸이 <paramref name="cells"/>에 닿으면 멈춤.
        /// </summary>
        private void CollectCrisis(ModuleData wanted, int cells)
        {
            var grid = _sim.Grid;
            _holdSorted.Clear();
            foreach (var m in grid.Modules)
                if (m.Data != wanted && m.Data != null && m.Data.HousingCapacity > 0 && !_holdCandidates.Contains(m))
                    _holdSorted.Add(m);
            _holdSorted.Sort((a, b) => a.Data.HousingCapacity.CompareTo(b.Data.HousingCapacity));
            foreach (var m in _holdSorted)
            {
                if (CandidateCells() >= cells)
                    return;
                if (Removable(m))
                    _holdCandidates.Add(m);
            }
            foreach (var m in grid.Modules)
            {
                if (CandidateCells() >= cells)
                    return;
                if (m.Data == wanted || m.Data == null || _holdCandidates.Contains(m))
                    continue;
                bool service = m.Data.IsService && m.Data.HousingCapacity <= 0;
                bool lab = m.Data == _lab;
                if ((service || lab) && Removable(m))
                    _holdCandidates.Add(m);
            }
        }

        /// <summary>
        /// 멈춰도 되는 상태 (사용자 결정 2026-11-26 "안정된 뒤 고정"): 생존 자원 순수지가 짓는 기준 이상 · 밤을 배터리로 버팀 · 낮 전력 여유.
        /// <see cref="BalanceRun"/>이 등급에 오른 뒤 이 상태가 1분 이어지면(최대 10분) 칸 수를 고정한다.
        /// </summary>
        public bool HoldStable()
        {
            var r = _sim.Resources;
            return LifeSupportStable(r) && !NightUncovered(r) && PowerSurplus() >= PowerHeadroom;
        }

        /// <summary>
        /// 철거해도 되는 남는 모듈을 우선순위 순으로 모은다 (<see cref="_holdCandidates"/>, 모은 칸이 <paramref name="cells"/>에 닿으면 멈춤):
        /// 남는 장갑 격벽 → 빈 거주 → 남는 창고 → 남는 생존 자원 생산. 여럿을 뺄 때의 합을 셈 (5차 측정: 종류마다 하나만 모아
        /// 2칸 이상인 모듈은 빈 거주가 수백 칸 남아도 늘 "모자람" → 도시급이 시간의 95% 동안 못 지음).
        /// </summary>
        private void CollectSurplus(ModuleData wanted, int cells)
        {
            _holdCandidates.Clear();
            var r = _sim.Resources;
            var grid = _sim.Grid;

            // ① 남는 장갑 격벽: 평소 허용(모듈 20개당 1개)을 넘는 것. 유성우 예보 · 낙하 중에는 정면 쪽 격벽은 남긴다
            if (_armor != null && wanted != _armor)
            {
                int armorCount = CountOf(_armor);
                int allowance = grid.ModuleCount / ModulesPerArmor;
                if (armorCount > allowance)
                {
                    var shower = _sim.MeteorShower;
                    bool protectFacing = shower != null && shower.Active;
                    int added = 0;
                    foreach (var m in grid.Modules)
                    {
                        if (added >= armorCount - allowance || CandidateCells() >= cells)
                            break;
                        if (m.Data != _armor || (protectFacing && Contains(shower.FacingModules, m)))
                            continue;
                        if (Removable(m))
                        {
                            _holdCandidates.Add(m);
                            added++;
                        }
                    }
                }
            }

            // ② 빈 거주: 빼도 지금 인구 + 미리 둘 칸이 다 들어갈 때 (여럿이면 뺀 수용을 더해 가며). 빈 거주를 빼면 칸도 나오고 인구가 더 늘지도 않음
            if (_housing != null)
            {
                int removedHousing = 0;
                foreach (var m in grid.Modules)
                {
                    if (CandidateCells() >= cells)
                        break;
                    if (m.Data == wanted || m.Data == null || m.Data.HousingCapacity <= 0 || m.Data.IsService)
                        continue;
                    int h = _sim.Effects.Housing(m.Data);
                    if (r.HousingCapacity - removedHousing - h < r.Population + _persona.HousingAhead || !Removable(m))
                        continue;
                    _holdCandidates.Add(m);
                    removedHousing += h;
                }
            }

            // ③ 남는 창고: 빼도 네 자원 재고가 다 들어가고, 해금된 가장 비싼 모듈 + 여유를 살 한도가 남을 때 (여럿이면 뺀 한도를 더해 가며)
            if (_storage != null && wanted != _storage)
            {
                float removedBonus = 0f;
                foreach (var m in grid.Modules)
                {
                    if (CandidateCells() >= cells)
                        break;
                    if (m.Data != _storage)
                        continue;
                    float bonus = removedBonus + _sim.Effects.StorageBonus(m.Data);
                    bool fits = r.GetCapacity(ResourceType.Metal) - bonus >= MaxUnlockedCost() + _persona.FusionMetalReserve;
                    foreach (var type in StockTypes)
                        fits &= r.GetCapacity(type) - bonus >= r.GetStock(type);
                    if (!fits || !Removable(m))
                        continue;
                    _holdCandidates.Add(m);
                    removedBonus = bonus;
                }
            }

            // ④ 남는 생존 자원 생산 (산소 · 물 · 식량): 빼도 순수지가 짓는 기준 + 여유 이상 — 남은 거주 칸까지 인구가 차는 몫을 미리 빼고 (2026-11-26 첫 측정:
            //    인구가 늘어나는 중에 산소 생성기를 "남는다"며 부쉈다가 산소가 모자라 무너짐). 전력 · 금속을 만드는 모듈은 빼지 않음
            //    순수지는 지금 값이 아니라 지난 한 바퀴(낮 + 밤) 동안 가장 낮았던 값으로 (3차 측정: 낮에 "남는다"며 산소 생성기를 부쉈다가
            //    밤에 산소가 바닥나 위기 → 거주를 부숴 인구가 10 → 4). 한 바퀴를 다 보기 전에는 남는 것으로 치지 않음. 여럿이면 뺀 생산을 더해 가며
            if (_lifeCycles >= 1)
            {
                int growth = Mathf.Max(0, r.HousingCapacity - r.Population);
                _holdRemovedLife[0] = _holdRemovedLife[1] = _holdRemovedLife[2] = 0f;
                foreach (var m in grid.Modules)
                {
                    if (CandidateCells() >= cells)
                        break;
                    if (m.Data == wanted || m == _sim.Core || !IsLifeOnlyProducer(m.Data))
                        continue;
                    bool surplus = true;
                    foreach (var p in m.Data.Production)
                    {
                        int i = System.Array.IndexOf(LifeTypes, p.Type);
                        float consumption = r.GetProduction(p.Type) - r.GetNetRate(p.Type);
                        float perResident = consumption / Mathf.Max(1, r.Population); // 모듈 소비까지 주민 몫으로 쳐서 넉넉하게
                        surplus &= WorstNet(p.Type) - _holdRemovedLife[i] - p.Amount - perResident * growth >= NetMargin + HoldSurplusMargin;
                    }
                    if (!surplus || !Removable(m))
                        continue;
                    _holdCandidates.Add(m);
                    foreach (var p in m.Data.Production)
                        _holdRemovedLife[System.Array.IndexOf(LifeTypes, p.Type)] += p.Amount;
                }
            }
        }

        private readonly float[] _holdRemovedLife = new float[3];
        private readonly List<ModuleInstance> _holdSorted = new List<ModuleInstance>();

        /// <summary>
        /// 거주 여유 줄이기 (5차 측정 뒤): 남은 빈 거주 칸까지 인구가 차면 산소 · 물 · 식량이 모자랄 것 같으면 빈 거주를 하나 철거한다 —
        /// 넓히기를 멈춘 사람이 인구를 묶는 방법 (게임에 인구 증가를 막는 정책은 없음). 도시급은 오를 때 거주가 인구의 7배쯤이라
        /// 칸을 고정해도 인구가 197 → 356으로 늘어 생존 자원이 따라가지 못하고 무너졌음. 30초에 한 번까지.
        /// </summary>
        private void HoldTrimHousing()
        {
            if (!Holding || _lifeCycles < 1 || _sim.ElapsedSeconds < _nextHousingTrim)
                return;
            var r = _sim.Resources;
            int growth = r.HousingCapacity - r.Population;
            if (growth <= _persona.HousingAhead)
                return;
            bool shortAhead = false;
            foreach (var type in LifeTypes)
            {
                float consumption = r.GetProduction(type) - r.GetNetRate(type);
                float perResident = consumption / Mathf.Max(1, r.Population);
                shortAhead |= WorstNet(type) - perResident * growth < NetMargin;
            }
            if (!shortAhead)
                return;
            _nextHousingTrim = _sim.ElapsedSeconds + 30f;
            ModuleInstance best = null;
            int bestH = 0;
            foreach (var m in _sim.Grid.Modules)
            {
                if (m.Data == null || m.Data.HousingCapacity <= 0 || m.Data.IsService)
                    continue;
                int h = _sim.Effects.Housing(m.Data);
                if (h <= bestH || r.HousingCapacity - h < r.Population + _persona.HousingAhead || !Removable(m))
                    continue;
                best = m;
                bestH = h;
            }
            if (best == null)
                return;
            string name = best.Data.DisplayName;
            if (!_sim.TryRemove(best))
                return;
            HoldHousingTrimmed++;
            HoldDemolished++;
            HoldDemolishedKinds.TryGetValue(name, out int n);
            HoldDemolishedKinds[name] = n + 1;
            if (HoldEvents.Count < 300)
                HoldEvents.Add($"{_sim.ElapsedSeconds / 60f:0.0}분 인구 묶기 철거 {name} (수용 {r.HousingCapacity + bestH} → {r.HousingCapacity} · 인구 {r.Population})");
        }

        private float _nextHousingTrim;
        /// <summary>인구를 묶으려고 빈 거주를 철거한 수 (<see cref="HoldTrimHousing"/>, 철거 수에도 포함).</summary>
        public int HoldHousingTrimmed { get; private set; }
        private float _nextBlockedLog;

        /// <summary>칸이 모자라 못 지을 때 이유를 10분에 한 번 기록 (철거 기록과 함께).</summary>
        private void LogBlocked(ModuleData wanted, int need, bool crisis)
        {
            if (_sim.ElapsedSeconds < _nextBlockedLog || HoldEvents.Count >= 300)
                return;
            _nextBlockedLog = _sim.ElapsedSeconds + 600f;
            var r = _sim.Resources;
            int housing = 0, housingRemovable = 0;
            foreach (var m in _sim.Grid.Modules)
            {
                if (m.Data == null || m.Data.HousingCapacity <= 0)
                    continue;
                housing++;
                if (Removable(m))
                    housingRemovable++;
            }
            HoldEvents.Add($"{_sim.ElapsedSeconds / 60f:0.0}분 막힘: {wanted.DisplayName} {need}칸 필요 · 후보 {CandidateCells()}칸 · 위기 {(crisis ? "예" : "아니오")}"
                           + $" · 거주 {housing}개 중 뺄 수 있는 것 {housingRemovable} · 인구 {r.Population}/{r.HousingCapacity} · 산소 {r.GetStock(ResourceType.Oxygen):0} · 전력 효율 {r.PowerEfficiency * 100f:0}%");
        }

        private static readonly ResourceType[] StockTypes = { ResourceType.Oxygen, ResourceType.Water, ResourceType.Food, ResourceType.Metal };

        // 산소 · 물 · 식량 순수지의 가장 낮은 값: 지금 바퀴 · 지난 바퀴 (낮밤 한 바퀴 = DayNight.Period)
        private readonly float[] _lifeNetNow = { float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity };
        private readonly float[] _lifeNetLast = { float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity };
        private float _lifeCycleStart = float.NaN;
        private int _lifeCycles;

        /// <summary>결정마다: 생존 자원 순수지의 가장 낮은 값을 낮밤 한 바퀴 단위로 기록 (남는 생산 판단용).</summary>
        private void TrackLifeNet()
        {
            var r = _sim.Resources;
            float now = _sim.ElapsedSeconds;
            if (float.IsNaN(_lifeCycleStart))
                _lifeCycleStart = now;
            float period = _sim.DayNight != null ? _sim.DayNight.Period : 0f;
            if (period > 0f && now - _lifeCycleStart >= period)
            {
                for (int i = 0; i < LifeTypes.Length; i++)
                {
                    _lifeNetLast[i] = _lifeNetNow[i];
                    _lifeNetNow[i] = float.PositiveInfinity;
                }
                _lifeCycleStart = now;
                _lifeCycles++;
            }
            for (int i = 0; i < LifeTypes.Length; i++)
                _lifeNetNow[i] = Mathf.Min(_lifeNetNow[i], r.GetNetRate(LifeTypes[i]));
        }

        private float WorstNet(ResourceType type)
        {
            int i = System.Array.IndexOf(LifeTypes, type);
            return i < 0 ? _sim.Resources.GetNetRate(type) : Mathf.Min(_lifeNetNow[i], _lifeNetLast[i]);
        }

        /// <summary>산소 · 물 · 식량만 만드는 모듈 (전력 · 금속 등 다른 생산이 섞이면 아님).</summary>
        private static bool IsLifeOnlyProducer(ModuleData data)
        {
            if (data == null || data.Production.Count == 0 || data.HousingCapacity > 0)
                return false;
            foreach (var p in data.Production)
                if (p.Type != ResourceType.Oxygen && p.Type != ResourceType.Water && p.Type != ResourceType.Food)
                    return false;
            return true;
        }

        private static bool Contains(IReadOnlyList<ModuleInstance> list, ModuleInstance m)
        {
            foreach (var x in list)
                if (x == m)
                    return true;
            return false;
        }

        /// <summary>철거할 수 있고(종류 · 받침 · 합성 재료 아님), 빼도 지금 코어에 이어진 모듈이 모두 이어져 있음.</summary>
        private bool Removable(ModuleInstance module)
        {
            if (module == null || module == _sim.Core || !_sim.CanRemove(module))
                return false;
            if (_sim.Synthesis != null)
                foreach (var job in _sim.Synthesis.Jobs)
                    foreach (var mat in job.Materials)
                        if (mat == module)
                            return false;
            if (_plan != null) // 진행 중인 합성 계획의 재료는 남김
                foreach (var piece in _plan.Pieces)
                    if (module.Data == piece.Data && module.Origin == piece.Origin)
                        return false;
            return KeepsConnected(module);
        }

        /// <summary>이 모듈을 빼도 코어에서 닿던 모듈이 모두 닿는지 (면 맞닿음 BFS).</summary>
        private bool KeepsConnected(ModuleInstance removed)
        {
            var grid = _sim.Grid;
            var core = _sim.Core;
            if (core == null)
                return true;
            _holdReached.Clear();
            _holdQueue.Clear();
            _holdReached.Add(core);
            _holdQueue.Enqueue(core);
            while (_holdQueue.Count > 0)
            {
                var current = _holdQueue.Dequeue();
                grid.GetNeighborModules(current, _holdNeighbors);
                foreach (var next in _holdNeighbors)
                    if (next != removed && _holdReached.Add(next))
                        _holdQueue.Enqueue(next);
            }
            foreach (var m in grid.Modules)
                if (m != removed && _sim.Connectivity.IsActive(m) && !_holdReached.Contains(m))
                    return false;
            return true;
        }
    }
}
