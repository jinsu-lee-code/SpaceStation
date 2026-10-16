using System;
using System.Collections.Generic;
using System.Text;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 13-7 타이쿤판 등급 중간 단계 (BALANCE 47번). 공통 고리 <see cref="StationProgression.ExtraUnlock"/>로 모듈을 먼저 연다.
    /// - 그 등급에 도달한 뒤 인구 · 모듈 조건을 넘으면 한 번: 모듈이 열림(등급이 내려가도 유지) + 연구 포인트 + 알림
    /// - 조건을 채우기 전에 다음 등급으로 넘어가도 그때 같이 연다 (보상은 그대로)
    /// </summary>
    public sealed class MidGradeSystem : StationSystem
    {
        public const string SystemKey = "mid-grade";

        private readonly MidGradeConfig _config;
        private readonly HashSet<int> _reached = new HashSet<int>();
        private readonly HashSet<ModuleData> _open = new HashSet<ModuleData>();

        /// <summary>단계에 도달함 (단계 번호).</summary>
        public event Action<int> Reached;

        public MidGradeSystem(MidGradeConfig config)
        {
            _config = config ? config : throw new ArgumentNullException(nameof(config));
        }

        /// <summary>시뮬레이션에 붙이고 먼저 연 모듈 고리를 연결. 세이브 복원 전에 부른다.</summary>
        public static MidGradeSystem AttachTo(StationSimulation sim, MidGradeConfig config)
        {
            var system = sim.AddSystem(new MidGradeSystem(config), TickStepNames.Progression);
            sim.Progression.ExtraUnlock = system.IsOpen;
            return system;
        }

        public override string Key => SystemKey;
        public MidGradeConfig Config => _config;

        public bool IsReached(int step) => _reached.Contains(step);

        public bool IsOpen(ModuleData data) => data != null && _open.Contains(data);

        public override void Tick(float dt)
        {
            var p = Sim.Progression;
            for (int i = 0; i < _config.Steps.Count; i++)
            {
                if (_reached.Contains(i))
                    continue;
                var step = _config.Steps[i];
                int highest = p.HighestGradeIndex;
                bool met = highest > step.Grade
                           || (highest >= step.Grade && Sim.Resources.Population >= step.MinPopulation
                               && StationProgression.CountGradeModules(Sim.Grid) >= step.MinModules);
                if (met)
                    Reach(i, report: true);
            }
        }

        private void Reach(int index, bool report)
        {
            var step = _config.Steps[index];
            _reached.Add(index);
            var names = new StringBuilder();
            foreach (var m in step.Modules)
            {
                if (m == null)
                    continue;
                _open.Add(m);
                names.Append(names.Length > 0 ? " · " : "").Append(m.DisplayName);
            }
            if (!report)
                return;
            if (step.ResearchPoints > 0)
            {
                var supply = Sim.Supply;
                supply.SetCounts(supply.ResearchPoints + step.ResearchPoints, supply.FreeRepairs);
            }
            string reward = step.ResearchPoints > 0 ? $" · 연구 포인트 +{step.ResearchPoints}" : "";
            Sim.Report($"{step.DisplayName}: {names} 건설 가능{reward}", true);
            Reached?.Invoke(index);
        }

        [Serializable]
        private sealed class State
        {
            public List<int> Reached = new List<int>();
        }

        public override string Capture(Func<ModuleInstance, int> indexOf)
        {
            var s = new State();
            s.Reached.AddRange(_reached);
            return JsonUtility.ToJson(s);
        }

        public override void Restore(string state, Func<int, ModuleInstance> moduleAt)
        {
            if (string.IsNullOrEmpty(state))
                return;
            var s = JsonUtility.FromJson<State>(state);
            if (s == null)
                return;
            _reached.Clear();
            _open.Clear();
            foreach (int i in s.Reached)
                if (i >= 0 && i < _config.Steps.Count)
                    Reach(i, report: false);
        }
    }
}
