using System.Collections.Generic;
using SpaceStation.Data;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 판별 연구 고리 (MERGE_NOTES M-19, 서사판 13-5b 기록 복원). <see cref="ResearchSystem.Gate"/>에 꽂으면
    /// 연구를 열지 · 단계 시작 조건(예: 기록 조각) · 시작할 때 쓰는 것 · 완료한 단계의 실제 효과(선택 대기 · 강화)를 판이 정한다.
    /// 꽂지 않으면(기본, 타이쿤판 · 봇) 연구는 지금과 똑같다.
    /// </summary>
    public interface IResearchGate
    {
        /// <summary>연구를 쓸 수 있는지 (false면 [관리]에서 숨기고 창에는 <see cref="ClosedReason"/>).</summary>
        bool IsOpen { get; }

        /// <summary>닫혀 있을 때 보일 한 줄.</summary>
        string ClosedReason { get; }

        /// <summary>메뉴 · 창에 쓸 이름 (null이면 "연구").</summary>
        string Title { get; }

        /// <summary>연구 창 맨 위 줄에 덧붙일 상태 (예: "기록 조각 1", 없으면 null).</summary>
        string Status { get; }

        /// <summary>이 단계를 시작할 수 없는 까닭 (시작할 수 있으면 null) → <see cref="ResearchStartResult.Gated"/>.</summary>
        string BlockReason(ResearchCategoryData category, int level);

        /// <summary>단계를 시작함 (자원은 이미 냄 — 판이 따로 쓰는 것을 여기서 씀).</summary>
        void OnStarted(ResearchCategoryData category, int level);

        /// <summary>완료한 단계의 실제 효과. defaults = 레벨 표 값. 빈 목록이면 효과 없음(예: 선택 대기).</summary>
        IReadOnlyList<ResearchModifier> Modifiers(ResearchCategoryData category, int level, IReadOnlyList<ResearchModifier> defaults);

        /// <summary>상세 화면에 덧붙일 한 줄 (완료한 단계 · 다음 단계 모두 물음, 없으면 null).</summary>
        string Note(ResearchCategoryData category, int level);
    }
}
