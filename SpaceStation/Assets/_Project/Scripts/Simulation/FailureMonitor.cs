using System;
using SpaceStation.Data;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 추가 실패 조건 (4-10, BALANCE 21번). 순수 C#. 조건이 풀리면 타이머는 초기화된다.
    /// - 산소 고갈 지속, 만족도 0 지속, 코어 주변 붕괴(코어 이웃이 최소 수 이상이고 모두 파손·수리 안 함) 지속.
    /// 설정값이 0이면 해당 조건은 꺼진다.
    /// </summary>
    public sealed class FailureMonitor
    {
        private readonly BalanceConfig _config;

        public FailureMonitor(BalanceConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>세이브 복원.</summary>
        internal void Restore(float oxygen, float satisfaction, float core)
        {
            OxygenTimer = Math.Max(0f, oxygen);
            SatisfactionTimer = Math.Max(0f, satisfaction);
            CoreTimer = Math.Max(0f, core);
        }

        public float OxygenTimer { get; private set; }
        public float SatisfactionTimer { get; private set; }
        public float CoreTimer { get; private set; }

        /// <summary>남은 시간(초). 조건이 진행 중이 아니면 −1 (UI 경고용).</summary>
        public float OxygenRemaining => Remaining(OxygenTimer, _config.OxygenFailSeconds);
        public float SatisfactionRemaining => Remaining(SatisfactionTimer, _config.SatisfactionFailSeconds);
        public float CoreRemaining => Remaining(CoreTimer, _config.CoreCollapseSeconds);

        /// <param name="coreNeighbors">코어에 면이 맞닿은 모듈 수</param>
        /// <param name="coreNeighborsDown">그중 파손되었고 수리 중이 아닌 수</param>
        /// <returns>이번 틱에 확정된 실패 사유 (없으면 None)</returns>
        public GameOverReason Tick(float dt, bool oxygenDepleted, float satisfaction, int coreNeighbors, int coreNeighborsDown)
        {
            OxygenTimer = Advance(_config.OxygenFailSeconds > 0f && oxygenDepleted, OxygenTimer, dt);
            SatisfactionTimer = Advance(_config.SatisfactionFailSeconds > 0f && satisfaction <= 0.01f, SatisfactionTimer, dt);
            bool collapse = _config.CoreCollapseSeconds > 0f && coreNeighbors >= _config.CoreCollapseMinNeighbors
                            && coreNeighborsDown >= coreNeighbors;
            CoreTimer = Advance(collapse, CoreTimer, dt);

            if (Due(OxygenTimer, _config.OxygenFailSeconds)) return GameOverReason.Oxygen;
            if (Due(SatisfactionTimer, _config.SatisfactionFailSeconds)) return GameOverReason.Satisfaction;
            if (Due(CoreTimer, _config.CoreCollapseSeconds)) return GameOverReason.CoreCollapse;
            return GameOverReason.None;
        }

        private static float Advance(bool condition, float timer, float dt) => condition ? timer + dt : 0f;

        private static bool Due(float timer, float limit) => limit > 0f && timer >= limit - 1e-4f;

        private static float Remaining(float timer, float limit) => limit > 0f && timer > 0f ? Math.Max(0f, limit - timer) : -1f;

        public static string Describe(GameOverReason reason)
        {
            switch (reason)
            {
                case GameOverReason.Population: return "정거장에 남은 주민이 없습니다";
                case GameOverReason.Oxygen: return "산소가 바닥난 채 오래 버티지 못했습니다";
                case GameOverReason.Satisfaction: return "주민들이 폭동을 일으켜 정거장을 버렸습니다";
                case GameOverReason.CoreCollapse: return "코어 주변 모듈이 모두 무너져 정거장이 붕괴했습니다";
                default: return "";
            }
        }
    }
}
