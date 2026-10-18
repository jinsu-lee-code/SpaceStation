using SpaceStation.Data;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 판별 정책 고리 (MERGE_NOTES M-22, 서사판 13-6a 3장 정책). <see cref="PolicySystem.Gate"/>에 꽂으면
    /// 슬롯 수 · 고를 수 있는 정책 · 적용 대기 · 교체 대기 · 시행 중 효과의 실제 값(예: 주민 반응)을 판이 정한다.
    /// 꽂지 않으면(기본, 타이쿤판 · 봇) 정책은 지금과 똑같다.
    /// </summary>
    public interface IPolicyGate
    {
        /// <summary>쓸 수 있는 슬롯 수. byDefault = 등급 · 규칙으로 정한 기존 값.</summary>
        int SlotCount(int byDefault);

        /// <summary>고를 수 있는 정책인지 (false면 창에서 숨기고 예약을 막음 — 이미 시행 중인 것은 그대로).</summary>
        bool Offers(PolicyData policy);

        /// <summary>새 정책이 효과를 내기까지 (초). byDefault = 설정 값.</summary>
        float ApplyDelay(float byDefault);

        /// <summary>한 슬롯을 바꾼 뒤 다시 바꿀 수 있기까지 (초). byDefault = 설정 값.</summary>
        float SwapCooldown(float byDefault);

        /// <summary>시행 중 효과 하나의 실제 값 (그대로면 modifier.Value).</summary>
        float Value(PolicyData policy, PolicyModifier modifier);

        /// <summary>정책 줄에 덧붙일 한 줄 (예: "통제형 · 곰 반발 · 만족도 상한 −10 → −13", 없으면 null).</summary>
        string Note(PolicyData policy);

        /// <summary>창 위 슬롯 안내 (null이면 기본 "국가급 2 · 대륙급 3 · 지구급 4").</summary>
        string Summary { get; }

        /// <summary>아직 열리지 않은 슬롯 카드 문구 (null이면 기본 "국가급부터" 등).</summary>
        string LockedSlot(int index);
    }
}
