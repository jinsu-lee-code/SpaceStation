using System;
using SpaceStation.Core;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 여객 도킹 포트 도착 안내판(12-5 <c>InteriorArrivalBoard</c>)이 보는 이민선 일정 — <see cref="StationSimulation.Arrivals"/>.
    /// 기본 = 이 포트 시계, 이민선을 따로 정하는 쪽(서사판 이민선 시계)이 있으면 그쪽이 바꿈 (사용자 결정 2026-10-25).
    /// </summary>
    public interface IArrivalSchedule
    {
        /// <summary>다음 이민선까지 (초, 일정이 없으면 -1).</summary>
        float TimeLeft { get; }
        /// <summary>지금까지 내린 주민 수.</summary>
        int Arrived { get; }
        /// <summary>이민선이 다녀간 횟수 (내린 주민이 없어도 셈) — 안내판이 늘어난 것을 보고 도착을 알림.</summary>
        int Visits { get; }
    }

    /// <summary>
    /// 12-5 여객 도킹 포트 — 주기적 이민선 (사용자 결정 2026-10-14, BALANCE 39번).
    /// 가동 중인 포트가 있으면 간격(기본 180초 ÷ 포트 수)마다 이민선이 와서, 빈 수용 인원이 있고 만족도가 기준(60) 이상이면
    /// 주민을 한꺼번에(최대 4명, 빈 자리까지) 내려 준다. 조건이 안 맞으면 그냥 떠남 (알림). 포트가 없으면 시계가 멈춤.
    /// </summary>
    public sealed class ImmigrationSystem : StationSystem, IArrivalSchedule
    {
        public const string SystemKey = "immigration";

        private float _timer = -1f;

        public override string Key => SystemKey;
        /// <summary>다음 이민선까지 (포트가 없으면 -1).</summary>
        public float TimeLeft => _timer;
        public int Arrived { get; private set; }
        /// <summary>이번 실행에서 이민선이 온 횟수 (내린 주민이 없어도 셈, 저장 안 함) — 12-5 도착 안내판이 도착을 알아챔.</summary>
        public int Visits { get; private set; }
        /// <summary>13-3 공통 고리: 이민선을 따로 정하는 쪽(서사판 이민선 시계)이 있으면 이 포트 시계를 끔.</summary>
        public bool Suppressed { get; set; }

        public override void Tick(float dt)
        {
            if (dt <= 0f || Sim.Session.IsGameOver)
                return;
            int ports = Sim.ImmigrationPorts;
            if (ports <= 0 || Suppressed)
            {
                _timer = -1f;
                return;
            }
            float interval = Sim.Balance.ImmigrationInterval / ports;
            if (_timer < 0f || _timer > interval)
                _timer = interval;
            _timer -= dt;
            if (_timer > 0f)
                return;
            _timer = interval;
            Visits++;
            var r = Sim.Resources;
            int room = r.HousingCapacity - r.Population;
            if (room <= 0)
            {
                Sim.Report("이민선이 왔지만 빈 거주 공간이 없어 돌아갔습니다", false);
                return;
            }
            if (Sim.Population.Satisfaction < Sim.Balance.ImmigrationMinSatisfaction)
            {
                Sim.Report($"이민선이 왔지만 만족도가 낮아(기준 {Sim.Balance.ImmigrationMinSatisfaction:0}) 아무도 내리지 않았습니다", false);
                return;
            }
            int n = Math.Min(room, Sim.Balance.ImmigrationSize);
            Sim.Population.Add(n, PopulationChangeReason.Immigration);
            Arrived += n;
            Sim.Report($"이민선 도착: 주민 {n}명이 들어왔습니다", true);
        }

        [Serializable]
        private sealed class State
        {
            public float Timer;
            public int Arrived;
        }

        public override string Capture(Func<ModuleInstance, int> indexOf)
            => _timer < 0f && Arrived == 0 ? null : JsonUtility.ToJson(new State { Timer = _timer, Arrived = Arrived });

        public override void Restore(string state, Func<int, ModuleInstance> moduleAt)
        {
            if (string.IsNullOrEmpty(state))
                return;
            var s = JsonUtility.FromJson<State>(state);
            if (s == null)
                return;
            _timer = s.Timer;
            Arrived = s.Arrived;
        }
    }
}
