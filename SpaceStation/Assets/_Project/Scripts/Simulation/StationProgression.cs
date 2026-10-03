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
        /// <summary>최고 등급에 처음 도달했을 때 한 번.</summary>
        public event Action FinalGradeReached;

        public int GradeIndex { get; private set; }
        public StationGrade Current => _config.Grades[GradeIndex];
        public StationGrade Next => GradeIndex + 1 < _config.Grades.Count ? _config.Grades[GradeIndex + 1] : null;
        public bool IsFinalGrade => GradeIndex == _config.Grades.Count - 1;
        public bool HasReachedFinalGrade { get; private set; }
        public int GradeCount => _config.Grades.Count;
        public ModuleData LimitedModule => _config.LimitedModule;

        public StationProgression(StationGradeConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            if (config.Grades.Count == 0)
                throw new ArgumentException("등급이 하나 이상 필요", nameof(config));
        }

        public StationGrade GetGrade(int index) => _config.Grades[index];

        /// <summary>세이브 복원: 최고 등급 도달 기록 (결과 화면이 다시 뜨지 않게, 이벤트 없음).</summary>
        internal void RestoreReachedFinal(bool reached)
        {
            HasReachedFinalGrade = reached;
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

            if (IsFinalGrade && !HasReachedFinalGrade)
            {
                HasReachedFinalGrade = true;
                FinalGradeReached?.Invoke();
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
        /// 제한 모듈(채굴 도킹) 최대 수. 최고 등급에서는 그 등급 최소 모듈 수를 넘는 모듈 N개마다 +1 (8-0, 후반 금속 수급).
        /// </summary>
        public int LimitFor(int moduleCount)
        {
            int limit = Current.MaxLimitedModules;
            int every = _config.ExtraLimitEveryModules;
            if (IsFinalGrade && every > 0)
                limit += Math.Max(0, moduleCount - Current.MinModules) / every;
            return limit;
        }

        public int CurrentLimit(StationGrid grid) => LimitFor(CountGradeModules(grid));

        /// <summary>8-6: 등급 조건·도킹 추가 한도에 쓰는 모듈 수 (<see cref="ModuleData.CountsTowardGrade"/>가 false인 장갑 격벽 등 제외).</summary>
        public static int CountGradeModules(StationGrid grid)
        {
            int count = 0;
            foreach (var m in grid.Modules)
            {
                if (m.Data == null || m.Data.CountsTowardGrade)
                    count++;
            }
            return count;
        }

        /// <summary>최고 등급에서 다음 +1까지 남은 모듈 수 (확장 없음·최고 등급 아님이면 -1).</summary>
        public int ModulesUntilNextExtra(int moduleCount)
        {
            int every = _config.ExtraLimitEveryModules;
            if (!IsFinalGrade || every <= 0)
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
