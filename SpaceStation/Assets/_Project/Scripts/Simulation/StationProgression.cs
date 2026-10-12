using System;
using SpaceStation.Core;
using SpaceStation.Data;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 정거장 등급 (GDD 12-3, BALANCE 11번). 인구·전체 모듈 수로 실시간 판정하며, 조건을 벗어나면 내려간다.
    /// 해금: 현재 등급 이하 등급들의 해금 목록에 있는 모듈만 건설 가능.
    /// 개수 제한: 제한 모듈(채굴 도킹)은 현재 등급의 최대 수까지만 추가 건설 가능 (초과분은 유지).
    /// </summary>
    public sealed class StationProgression
    {
        private readonly StationGradeConfig _config;

        /// <summary>(이전 등급 인덱스, 새 등급 인덱스).</summary>
        public event Action<int, int> GradeChanged;
        /// <summary>승리 등급(대형)에 처음 도달했을 때 한 번 (완성 소리). 결과 화면은 <see cref="MilestoneReached"/>.</summary>
        public event Action VictoryReached;
        /// <summary>마지막(최고) 등급에 처음 도달했을 때 한 번 (12-1부터 지구급).</summary>
        public event Action TopGradeReached;
        /// <summary>12-1: ★ · 결과 화면 등급에 처음 도달 (등급 인덱스, 종류). 한 번에 여러 등급을 넘으면 낮은 등급부터 차례로.</summary>
        public event Action<int, GradeMilestone> MilestoneReached;

        public int GradeIndex { get; private set; }
        public StationGrade Current => _config.Grades[GradeIndex];
        public StationGrade Next => GradeIndex + 1 < _config.Grades.Count ? _config.Grades[GradeIndex + 1] : null;
        /// <summary>마지막(최고) 등급인지.</summary>
        public bool IsFinalGrade => GradeIndex == _config.Grades.Count - 1;
        public int VictoryGradeIndex => _config.VictoryGradeIndex;
        /// <summary>승리 등급 이상인지 (도킹 추가 한도 적용).</summary>
        public bool IsAtOrAboveVictory => GradeIndex >= VictoryGradeIndex;
        public bool HasReachedVictory { get; private set; }
        /// <summary>마지막 등급에 한 번이라도 도달.</summary>
        public bool HasReachedTopGrade { get; private set; }
        /// <summary>12-1: 지금까지 도달한 가장 높은 등급 (등급이 내려가도 유지, ★ · 결과 화면은 이 값을 넘을 때만).</summary>
        public int HighestGradeIndex { get; private set; }
        public int GradeCount => _config.Grades.Count;
        public ModuleData LimitedModule => _config.LimitedModule;

        public StationProgression(StationGradeConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            if (config.Grades.Count == 0)
                throw new ArgumentException("등급이 하나 이상 필요", nameof(config));
        }

        public StationGrade GetGrade(int index) => _config.Grades[index];

        /// <summary>세이브 복원: 승리·최고 등급 도달 기록 (결과 화면이 다시 뜨지 않게, 이벤트 없음).</summary>
        internal void RestoreReached(bool victory, bool topGrade, int highest)
        {
            HasReachedVictory = victory;
            HasReachedTopGrade = topGrade;
            HighestGradeIndex = Math.Max(GradeIndex, Math.Min(highest, _config.Grades.Count - 1));
        }

        /// <summary>12-1: 이 결과 화면 등급이 몇 번째 구간인지 (1부터). 결과 등급이 아니면 0.</summary>
        public int ResultNumber(int gradeIndex)
        {
            if (gradeIndex < 0 || gradeIndex >= _config.Grades.Count || _config.Grades[gradeIndex].Milestone != GradeMilestone.Result)
                return 0;
            int n = 0;
            for (int i = 0; i <= gradeIndex; i++)
                if (_config.Grades[i].Milestone == GradeMilestone.Result)
                    n++;
            return n;
        }

        /// <summary>12-1: 마지막 결과 화면 등급(최종 결과)인지.</summary>
        public bool IsFinalResult(int gradeIndex)
        {
            if (ResultNumber(gradeIndex) == 0)
                return false;
            for (int i = gradeIndex + 1; i < _config.Grades.Count; i++)
                if (_config.Grades[i].Milestone == GradeMilestone.Result)
                    return false;
            return true;
        }

        /// <summary>세이브·전시용: 이 모듈 정의를 이름으로 찾는다 (등급 해금 목록 기준). 없으면 null.</summary>
        public ModuleData FindModule(string assetName)
        {
            if (string.IsNullOrEmpty(assetName))
                return null;
            foreach (var g in _config.Grades)
                foreach (var m in g.Unlocks)
                    if (m != null && m.name == assetName)
                        return m;
            return null;
        }

        /// <summary>조건을 만족하는 가장 높은 등급 (첫 등급은 조건과 무관하게 항상 만족).</summary>
        public int ComputeGrade(int population, int moduleCount)
        {
            int best = 0;
            for (int i = 1; i < _config.Grades.Count; i++)
            {
                var g = _config.Grades[i];
                if (population >= g.MinPopulation && moduleCount >= g.MinModules)
                    best = i;
            }
            return best;
        }

        public void Evaluate(int population, int moduleCount)
        {
            int next = ComputeGrade(population, moduleCount);
            if (next == GradeIndex)
                return;
            int previous = GradeIndex;
            GradeIndex = next;
            GradeChanged?.Invoke(previous, next);

            if (IsAtOrAboveVictory && !HasReachedVictory)
            {
                HasReachedVictory = true;
                VictoryReached?.Invoke();
            }
            if (IsFinalGrade && GradeIndex > VictoryGradeIndex && !HasReachedTopGrade)
            {
                HasReachedTopGrade = true;
                TopGradeReached?.Invoke();
            }
            if (next > HighestGradeIndex)
            {
                int from = HighestGradeIndex + 1;
                HighestGradeIndex = next; // 알림 처리 중 다시 판정돼도 같은 등급을 두 번 알리지 않게 먼저 기록
                for (int i = from; i <= next; i++)
                {
                    var milestone = _config.Grades[i].Milestone;
                    if (milestone != GradeMilestone.None)
                        MilestoneReached?.Invoke(i, milestone);
                }
            }
        }

        /// <summary>이 모듈이 해금되는 등급 인덱스. 어떤 등급에도 없으면 -1 (건설 불가).</summary>
        public int GetUnlockGrade(ModuleData data)
        {
            for (int i = 0; i < _config.Grades.Count; i++)
            {
                foreach (var m in _config.Grades[i].Unlocks)
                {
                    if (m == data)
                        return i;
                }
            }
            return -1;
        }

        public bool IsUnlocked(ModuleData data)
        {
            int grade = GetUnlockGrade(data);
            return grade >= 0 && grade <= GradeIndex;
        }

        public int CountLimited(StationGrid grid)
        {
            if (_config.LimitedModule == null)
                return 0;
            int count = 0;
            foreach (var m in grid.Modules)
            {
                if (m.Data == _config.LimitedModule)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// 제한 모듈(채굴 도킹) 최대 수. 승리 등급(대형) 이상에서는 현재 등급 최소 모듈 수를 넘는 모듈 N개마다 +1 (8-0, 후반 금속 수급).
        /// </summary>
        public int LimitFor(int moduleCount)
        {
            int limit = Current.MaxLimitedModules;
            int every = _config.ExtraLimitEveryModules;
            if (IsAtOrAboveVictory && every > 0)
                limit += Math.Max(0, moduleCount - Current.MinModules) / every;
            return limit;
        }

        public int CurrentLimit(StationGrid grid) => LimitFor(CountGradeModules(grid));

        /// <summary>
        /// 8-6: 등급 조건·도킹 추가 한도에 쓰는 모듈 수 (<see cref="ModuleData.CountsTowardGrade"/>가 false인 장갑 격벽 등 제외).
        /// 12-3: 진화 모듈은 재료 개수만큼 (<see cref="ModuleData.GradeModuleCount"/>, A4) — 합성해도 등급이 떨어지지 않음.
        /// </summary>
        public static int CountGradeModules(StationGrid grid)
        {
            int count = 0;
            foreach (var m in grid.Modules)
            {
                if (m.Data == null)
                    count++;
                else if (m.Data.CountsTowardGrade)
                    count += m.Data.GradeModuleCount;
            }
            return count;
        }

        /// <summary>12-3: 모듈 수를 진화 모듈은 재료 개수만큼 센 값 (장갑 격벽 포함 — 결과 화면 · 기록용, 합성 전후가 같음).</summary>
        public static int CountModules(StationGrid grid)
        {
            int count = 0;
            foreach (var m in grid.Modules)
                count += m.Data != null ? m.Data.GradeModuleCount : 1;
            return count;
        }

        /// <summary>승리 등급 이상에서 다음 +1까지 남은 모듈 수 (확장 없음·승리 등급 미만이면 -1).</summary>
        public int ModulesUntilNextExtra(int moduleCount)
        {
            int every = _config.ExtraLimitEveryModules;
            if (!IsAtOrAboveVictory || every <= 0)
                return -1;
            int over = Math.Max(0, moduleCount - Current.MinModules);
            return every - over % every;
        }

        /// <summary>위치와 무관한 건설 가능 여부: Valid / ModuleLocked / LimitReached.</summary>
        public PlacementResult CheckBuildable(ModuleData data, StationGrid grid)
        {
            if (data == null)
                return PlacementResult.InvalidDefinition;
            if (!IsUnlocked(data))
                return PlacementResult.ModuleLocked;
            if (data == _config.LimitedModule && CountLimited(grid) >= CurrentLimit(grid))
                return PlacementResult.LimitReached;
            return PlacementResult.Valid;
        }
    }
}
