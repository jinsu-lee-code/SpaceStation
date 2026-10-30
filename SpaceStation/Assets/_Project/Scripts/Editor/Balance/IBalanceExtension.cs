using SpaceStation.Simulation;

namespace SpaceStation.Editor.Balance
{
    /// <summary>
    /// 측정 한 판에 얹는 확장 (13-8 공통 고리, 사용자 결정 2026-10-25: 서사판 캠페인 봇 = 공통 봇 위에 장 · 이민선 · 탐사 · 정책 처리).
    /// <see cref="BalanceRunSettings.Extension"/>이 판마다 새로 만든다. 같은 시드면 같은 결과가 나오게 난수는 seed로만.
    /// 순서: <see cref="Configure"/> → 시뮬레이션 생성 → <see cref="Attach"/> → (시작 상태 복원) → 매 틱 봇 결정 뒤 <see cref="Decide"/> → 끝에 <see cref="Finish"/>.
    /// 게임 오버면 <see cref="RetryState"/>가 상태를 주는 동안 그 상태로 새 시뮬레이션을 만들어 이어 감 (Configure · Attach를 다시 부름).
    /// </summary>
    public interface IBalanceExtension
    {
        /// <summary>시뮬레이션을 만들기 전 설정 (모드 데이터 등).</summary>
        void Configure(StationSimulationSettings settings, int seed);

        /// <summary>만든 직후 · 상태 복원 전 (시스템을 붙임 — 복원 때 상태가 돌아오도록). 다시 시작이면 새 시뮬레이션으로 또 부름.</summary>
        void Attach(StationSimulation sim, int seed);

        /// <summary>매 틱 공통 봇 결정 뒤 (elapsed = 판 시작부터 흐른 시간, 다시 시작으로 되돌아간 시간 포함).</summary>
        void Decide(BalanceBot bot, float elapsed);

        /// <summary>true면 그 판을 끝냄 (예: 캠페인 끝).</summary>
        bool Finished { get; }

        /// <summary>게임 오버 때 이어 갈 상태 (null = 그 판 끝).</summary>
        StationState RetryState();

        /// <summary>판이 끝날 때 결과를 채움 (<see cref="BalanceRunResult.Extra"/>).</summary>
        void Finish(BalanceRunResult result);
    }
}
