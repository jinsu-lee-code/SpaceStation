using System;
using System.Collections.Generic;
using SpaceStation.Data;

namespace SpaceStation.Simulation
{
    public enum ResearchStartResult
    {
        Ok,
        MaxLevel,
        AlreadyResearching,
        NoFreeLab,
        GradeTooLow,
        PopulationTooLow,
        InsufficientResources,
        Unknown,
    }

    /// <summary>진행 중인 연구 하나.</summary>
    public sealed class ResearchProject
    {
        public ResearchCategoryData Category { get; }
        public int TargetLevel { get; }
        public ResearchLevel Level { get; }
        /// <summary>0~1.</summary>
        public float Progress { get; internal set; }
        /// <summary>이번 틱에 연구소가 모자라 멈췄는지.</summary>
        public bool Paused { get; internal set; }
        /// <summary>이번 틱의 진행 속도 배율 (전력 효율, 멈춤이면 0).</summary>
        public float Rate { get; internal set; }

        internal ResearchProject(ResearchCategoryData category, int targetLevel)
        {
            Category = category;
            TargetLevel = targetLevel;
            Level = category.GetLevel(targetLevel);
        }

        /// <summary>남은 시간(초) — 현재 속도 기준. 멈춤이면 무한대.</summary>
        public float RemainingSeconds => Rate > 1e-4f ? (1f - Progress) * Level.Duration / Rate : float.PositiveInfinity;
    }

    /// <summary>
    /// Phase 6 연구 (RESEARCH.md). 순수 C#.
    /// - 카테고리 6개 × 레벨 1~4 + 단일 레벨 자동화, 레벨은 되돌릴 수 없음. 카테고리당 동시 1개.
    /// - 동시 연구 수 = 가동 중인 연구소 슬롯 합. 슬롯이 줄면 나중에 시작한 연구부터 멈춤(진행률 유지).
    /// - 시작 비용 1회(환급 없음), 진행 중 전력 수요 추가, 진행 속도 = 전력 효율 × 1/소요 시간.
    /// - 시작 조건: 정거장 등급 + 인구 (ResearchLevelCapConfig), 카테고리 최소 등급(MinGrade)이 더 높으면 그것.
    /// </summary>
    public sealed class ResearchSystem
    {
        private readonly List<ResearchCategoryData> _categories = new List<ResearchCategoryData>();
        private readonly Dictionary<ResearchCategoryData, int> _levels = new Dictionary<ResearchCategoryData, int>();
        private readonly List<ResearchProject> _projects = new List<ResearchProject>();
        private readonly List<ResearchProject> _finished = new List<ResearchProject>();
        private readonly ResearchLevelCapConfig _caps;

        public ResearchEffects Effects { get; }
        /// <summary>레벨 상한 조건 (null이면 조건 없음).</summary>
        public ResearchLevelCapConfig Caps => _caps;
        public IReadOnlyList<ResearchCategoryData> Categories => _categories;
        public IReadOnlyList<ResearchProject> Projects => _projects;
        /// <summary>가동 중인 연구소 슬롯 합 (StationSimulation이 매 틱 갱신).</summary>
        public int LabSlots { get; internal set; }

        public event Action<ResearchProject> Started;
        public event Action<ResearchCategoryData, int> Completed;
        public event Action<ResearchCategoryData> Cancelled;

        public ResearchSystem(IReadOnlyList<ResearchCategoryData> categories, ResearchLevelCapConfig caps, ResearchEffects effects)
        {
            Effects = effects ?? throw new ArgumentNullException(nameof(effects));
            _caps = caps;
            if (categories != null)
            {
                foreach (var c in categories)
                {
                    if (c != null && !_categories.Contains(c))
                    {
                        _categories.Add(c);
                        _levels[c] = 0;
                    }
                }
            }
        }

        public int GetLevel(ResearchCategoryData category) => category != null && _levels.TryGetValue(category, out var l) ? l : 0;

        public ResearchCategoryData Find(ResearchCategory category)
        {
            foreach (var c in _categories)
                if (c.Category == category)
                    return c;
            return null;
        }

        public ResearchProject GetProject(ResearchCategoryData category)
        {
            foreach (var p in _projects)
                if (p.Category == category)
                    return p;
            return null;
        }

        public bool HasFreeSlot => _projects.Count < LabSlots;

        /// <summary>다음 레벨 연구를 시작할 수 있는지 (자원 제외 조건은 앞에서부터).</summary>
        public ResearchStartResult CanStart(ResearchCategoryData category, int grade, int population, Func<IReadOnlyList<ResourceAmount>, bool> canAfford)
        {
            if (category == null || !_levels.ContainsKey(category))
                return ResearchStartResult.Unknown;
            int next = GetLevel(category) + 1;
            if (next > category.MaxLevel)
                return ResearchStartResult.MaxLevel;
            if (GetProject(category) != null)
                return ResearchStartResult.AlreadyResearching;
            if (grade < RequiredGrade(category, next))
                return ResearchStartResult.GradeTooLow;
            if (population < RequiredPopulation(next))
                return ResearchStartResult.PopulationTooLow;
            if (!HasFreeSlot)
                return ResearchStartResult.NoFreeLab;
            if (canAfford != null && !canAfford(category.GetLevel(next).StartCost))
                return ResearchStartResult.InsufficientResources;
            return ResearchStartResult.Ok;
        }

        /// <summary>레벨 시작에 필요한 등급 = 레벨 상한 표와 카테고리 최소 등급 중 큰 쪽.</summary>
        public int RequiredGrade(ResearchCategoryData category, int level)
        {
            int grade = _caps != null ? _caps.Get(level).Grade : 0;
            return category != null ? Math.Max(grade, category.MinGrade) : grade;
        }

        public int RequiredPopulation(int level) => _caps != null ? _caps.Get(level).MinPopulation : 0;

        /// <summary>시작 (비용 차감은 호출자가 먼저 성공시켜야 함 — StationSimulation.TryStartResearch).</summary>
        internal ResearchProject Begin(ResearchCategoryData category)
        {
            var project = new ResearchProject(category, GetLevel(category) + 1);
            _projects.Add(project);
            Started?.Invoke(project);
            return project;
        }

        /// <summary>진행 중 연구 취소 (진행률·시작 비용 모두 잃음).</summary>
        public bool Cancel(ResearchCategoryData category)
        {
            var p = GetProject(category);
            if (p == null)
                return false;
            _projects.Remove(p);
            Cancelled?.Invoke(category);
            return true;
        }

        /// <summary>이번 틱에 진행 중(멈추지 않은) 연구의 전력 수요 합.</summary>
        public float RunningPowerDemand
        {
            get
            {
                float sum = 0f;
                for (int i = 0; i < _projects.Count && i < LabSlots; i++)
                    sum += _projects[i].Level.PowerDemand;
                return sum;
            }
        }

        public void Tick(float dt, float powerEfficiency)
        {
            _finished.Clear();
            for (int i = 0; i < _projects.Count; i++)
            {
                var p = _projects[i];
                p.Paused = i >= LabSlots;
                p.Rate = p.Paused ? 0f : Math.Max(0f, powerEfficiency);
                if (p.Paused)
                    continue;
                p.Progress = Math.Min(1f, p.Progress + dt * p.Rate / Math.Max(1f, p.Level.Duration));
                if (p.Progress >= 1f - 1e-5f)
                    _finished.Add(p);
            }
            foreach (var p in _finished)
            {
                _projects.Remove(p);
                _levels[p.Category] = p.TargetLevel;
                Effects.Recalculate(_categories, GetLevel);
                Completed?.Invoke(p.Category, p.TargetLevel);
            }
        }

        /// <summary>테스트·세이브용: 레벨 직접 설정.</summary>
        public void SetLevel(ResearchCategoryData category, int level)
        {
            if (category == null || !_levels.ContainsKey(category))
                return;
            _levels[category] = Math.Max(0, Math.Min(level, category.MaxLevel));
            Effects.Recalculate(_categories, GetLevel);
        }
    }
}
