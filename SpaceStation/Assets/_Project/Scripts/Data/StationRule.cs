using System;

namespace SpaceStation.Data
{
    /// <summary>
    /// 13-1 공통 판정 고리: 등급 숫자 대신 다른 쪽(서사판 장 틀 · 타이쿤판 해금 선택)이 열 수 있는 시스템 규칙.
    /// 규칙을 정하는 쪽(<see cref="IStationRules"/>)이 없으면 각 시스템은 기존 등급 조건 그대로.
    /// </summary>
    [Flags]
    public enum StationRule
    {
        None = 0,
        /// <summary>유성우 + 방향 예보 (기존 도시급).</summary>
        MeteorShower = 1,
        /// <summary>상시 정책 (기존 국가급).</summary>
        Policies = 2,
        /// <summary>합성 1단계 레시피 (기존 도시급).</summary>
        Synthesis = 4,
        /// <summary>칸 단위 열 (기존 대륙급).</summary>
        Heat = 8,
        /// <summary>사회 위기 + 선택지 (기존 국가급).</summary>
        SocialCrisis = 16,
        /// <summary>방문 세력 (기존 지구급).</summary>
        Contact = 32,
        /// <summary>합성 2단계 레시피.</summary>
        SynthesisStage2 = 64,
        /// <summary>합성 3단계 이상 레시피.</summary>
        SynthesisStage3 = 128,
        /// <summary>정책 슬롯 확장 (<see cref="PolicyConfig.ExpansionSlots"/>만큼 추가).</summary>
        PolicyExpansion = 256,
    }

    /// <summary>13-1: 규칙을 정하는 쪽. 시뮬레이션에 붙어 있으면 등급 조건 대신 이것이 시스템 개방을 정한다.</summary>
    public interface IStationRules
    {
        bool Allows(StationRule rule);
    }

    public static class StationRules
    {
        /// <summary>합성 레시피 단계 → 규칙 (1 = 합성, 2 = 2단계, 3 이상 = 3단계).</summary>
        public static StationRule ForSynthesisStage(int stage) =>
            stage >= 3 ? StationRule.SynthesisStage3 : stage == 2 ? StationRule.SynthesisStage2 : StationRule.Synthesis;

        /// <summary>화면 표시 이름 (잠김 안내 등). 규칙 하나만.</summary>
        public static string DisplayName(StationRule rule)
        {
            switch (rule)
            {
                case StationRule.MeteorShower: return "유성우 방향 예보";
                case StationRule.Policies: return "정책";
                case StationRule.Synthesis: return "합성 1단계";
                case StationRule.Heat: return "칸 단위 열";
                case StationRule.SocialCrisis: return "사회 사건";
                case StationRule.Contact: return "방문 세력";
                case StationRule.SynthesisStage2: return "합성 2단계";
                case StationRule.SynthesisStage3: return "합성 3단계";
                case StationRule.PolicyExpansion: return "정책 슬롯 확장";
                default: return rule.ToString();
            }
        }
    }
}
