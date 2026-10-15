using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>
    /// 13-3 타이쿤판 사회 사건 '역병' (사용자 결정 2026-10-15, BALANCE 43번). 메뉴 SpaceStation/Tycoon/Setup Needs가 만든다.
    /// 무작위로 들어오고(주기 굴림 + 이민선 도착 때 굴림), 검역 · 의료가 부족할수록 확률 ↑ → 경고(선택지) → 역병(인구 · 생산 감소).
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/Plague Config", fileName = "PlagueConfig")]
    public sealed class PlagueConfig : ScriptableObject
    {
        [Header("발생")]
        [Tooltip("이 등급부터 (규칙을 정하는 쪽이 있으면 '사회 사건' 규칙이 열려야 함)")]
        [SerializeField, Min(0)] private int _minGrade = 4;
        [Tooltip("굴림 간격 (초)")]
        [SerializeField, Min(1f)] private float _rollInterval = 60f;
        [Tooltip("굴림마다 기본 확률 (검역 · 의료가 모두 충분해도)")]
        [SerializeField, Range(0f, 1f)] private float _baseChance = 0.02f;
        [Tooltip("부족분(1 − 검역 · 의료 중 낮은 충족률)에 곱해 더하는 확률")]
        [SerializeField, Range(0f, 1f)] private float _shortageChance = 0.25f;
        [Tooltip("이민선이 주민을 내릴 때 한 번 더 굴림 (외부에서 들어옴) — 기본 확률")]
        [SerializeField, Range(0f, 1f)] private float _immigrationChance = 0.05f;
        [Tooltip("사건이 끝난 뒤 다시 일어나기까지 최소 (초)")]
        [SerializeField, Min(0f)] private float _cooldown = 300f;

        [Header("경고 · 역병")]
        [Tooltip("경고 → 역병까지 (초)")]
        [SerializeField, Min(1f)] private float _warningSeconds = 90f;
        [Tooltip("역병 지속 (초)")]
        [SerializeField, Min(1f)] private float _outbreakSeconds = 90f;
        [Tooltip("이 간격(초)마다 주민이 줄어듦")]
        [SerializeField, Min(1f)] private float _lossInterval = 15f;
        [Tooltip("한 번에 줄어드는 비율 (인구 기준, 최소 1명)")]
        [SerializeField, Range(0f, 1f)] private float _lossRatio = 0.02f;
        [Tooltip("검역 충족률 100%면 피해를 이만큼 줄임 (0.5 = 절반)")]
        [SerializeField, Range(0f, 1f)] private float _quarantineProtection = 0.5f;
        [Tooltip("역병 중 생산 배율 (일손 부족)")]
        [SerializeField, Range(0f, 1f)] private float _outbreakProduction = 0.8f;

        [Header("선택지")]
        [Tooltip("방역 물자: 이 자원을 내고 해소")]
        [SerializeField] private List<ResourceAmount> _suppliesCost = new List<ResourceAmount> { new ResourceAmount(ResourceType.Food, 60f), new ResourceAmount(ResourceType.Water, 60f) };
        [Tooltip("격리 봉쇄: 즉시 해소, 만족도 변화 + 떠나는 주민")]
        [SerializeField] private float _lockdownSatisfaction = -10f;
        [SerializeField, Min(0)] private int _lockdownLeave = 2;
        [Tooltip("위생 캠페인(일시 정책): 이 시간 동안 생산 배율, 역병은 막음")]
        [SerializeField, Min(1f)] private float _campaignSeconds = 180f;
        [SerializeField, Range(0f, 1f)] private float _campaignProduction = 0.85f;

        public int MinGrade => _minGrade;
        public float RollInterval => _rollInterval;
        public float BaseChance => _baseChance;
        public float ShortageChance => _shortageChance;
        public float ImmigrationChance => _immigrationChance;
        public float Cooldown => _cooldown;
        public float WarningSeconds => _warningSeconds;
        public float OutbreakSeconds => _outbreakSeconds;
        public float LossInterval => _lossInterval;
        public float LossRatio => _lossRatio;
        public float QuarantineProtection => _quarantineProtection;
        public float OutbreakProduction => _outbreakProduction;
        public IReadOnlyList<ResourceAmount> SuppliesCost => _suppliesCost;
        public float LockdownSatisfaction => _lockdownSatisfaction;
        public int LockdownLeave => _lockdownLeave;
        public float CampaignSeconds => _campaignSeconds;
        public float CampaignProduction => _campaignProduction;
    }
}
