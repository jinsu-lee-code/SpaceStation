using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>
    /// CONNECT_1121 7번 ② 타이쿤판 국가급 사회 사건 '비리' (사용자 결정 2026-11-28, 추천안) — 역병(<see cref="PlagueConfig"/>)과 같은 틀.
    /// 행정 충족률이 낮을수록 잦음 → 경고 중 선택지 4개 (감사 · 강경 숙청 · 투명 행정 · 방치) → 방치하면 횡령 (금속 수입 절반 · 저장 금속 일부를 잃음).
    /// 파업 연결: 소비(상업 구역) 부족만큼 파업 기준 만족도 ↑ · 비리를 방치하면 다음 파업 기준 ↑. 값은 가안 (BALANCE 51번 뒤 "비리").
    /// 메뉴 SpaceStation/Tycoon/Setup Corruption이 만든다.
    /// </summary>
    [CreateAssetMenu(fileName = "CorruptionConfig", menuName = "SpaceStation/Tycoon/Corruption Config")]
    public sealed class CorruptionConfig : ScriptableObject
    {
        [Header("발생")]
        [Tooltip("이 등급부터 (5 = 국가급 — 행정 요구가 생기는 등급)")]
        [SerializeField, Min(0)] private int _minGrade = 5;
        [Tooltip("굴림 간격 (초)")]
        [SerializeField, Min(1f)] private float _rollInterval = 60f;
        [Tooltip("행정이 충분해도 굴림마다 일어날 확률")]
        [SerializeField, Range(0f, 1f)] private float _baseChance = 0.02f;
        [Tooltip("행정 부족분(1 − 충족률) × 이 값을 더함")]
        [SerializeField, Range(0f, 1f)] private float _shortageChance = 0.25f;
        [Tooltip("끝난 뒤 다시 안 일어나는 시간 (초)")]
        [SerializeField, Min(0f)] private float _cooldown = 300f;

        [Header("경고 · 횡령")]
        [SerializeField, Min(1f)] private float _warningSeconds = 90f;
        [SerializeField, Min(1f)] private float _embezzleSeconds = 90f;
        [Tooltip("횡령 동안 금속 생산 배율 (0.5 = 절반이 샘)")]
        [SerializeField, Range(0f, 1f)] private float _embezzleMetal = 0.5f;
        [Tooltip("횡령이 시작될 때 잃는 저장 금속 비율")]
        [SerializeField, Range(0f, 1f)] private float _embezzleStoredLoss = 0.1f;

        [Header("선택지")]
        [Tooltip("감사 비용")]
        [SerializeField] private List<ResourceAmount> _auditCost = new List<ResourceAmount> { new ResourceAmount(ResourceType.Metal, 100f) };
        [SerializeField] private float _purgeSatisfaction = -10f;
        [SerializeField, Min(0)] private int _purgeLeave = 2;
        [Tooltip("일시 정책 '투명 행정' 길이 · 연구 속도 배율")]
        [SerializeField, Min(1f)] private float _transparencySeconds = 180f;
        [SerializeField, Range(0f, 1f)] private float _transparencyResearch = 0.8f;

        [Header("파업 연결")]
        [Tooltip("소비(상업 구역) 부족분 × 이 값만큼 파업 기준 만족도 ↑")]
        [SerializeField, Min(0f)] private float _commerceStrikeMax = 20f;
        [Tooltip("비리를 방치(횡령)하면 다음 파업 기준 만족도 ↑ (다음 파업 사건이 끝나면 사라짐)")]
        [SerializeField, Min(0f)] private float _ignoredStrikeBonus = 10f;

        public int MinGrade => _minGrade;
        public float RollInterval => _rollInterval;
        public float BaseChance => _baseChance;
        public float ShortageChance => _shortageChance;
        public float Cooldown => _cooldown;
        public float WarningSeconds => _warningSeconds;
        public float EmbezzleSeconds => _embezzleSeconds;
        public float EmbezzleMetal => _embezzleMetal;
        public float EmbezzleStoredLoss => _embezzleStoredLoss;
        public IReadOnlyList<ResourceAmount> AuditCost => _auditCost;
        public float PurgeSatisfaction => _purgeSatisfaction;
        public int PurgeLeave => _purgeLeave;
        public float TransparencySeconds => _transparencySeconds;
        public float TransparencyResearch => _transparencyResearch;
        public float CommerceStrikeMax => _commerceStrikeMax;
        public float IgnoredStrikeBonus => _ignoredStrikeBonus;
    }
}
