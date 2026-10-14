using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>13-1: 장 준비 목표의 종류 (판정은 <see cref="SpaceStation.Simulation.ChapterSystem"/>).</summary>
    public enum ChapterGoalKind
    {
        /// <summary>정거장 등급 도달 (실시간 판정, 한 번 도달하면 완료).</summary>
        ReachGrade,
        /// <summary>코어에 연결된 해당 모듈 N개.</summary>
        BuildModule,
        /// <summary>인구 N명.</summary>
        Population,
        /// <summary>자원 재고 N 이상.</summary>
        Stock,
        /// <summary>다른 시스템이 올리는 표시 (예: 첫 탐사, 이민선 받기 — <see cref="SpaceStation.Simulation.ChapterSystem.SetFlag"/>).</summary>
        Flag,
        /// <summary>이 장에서 N초 버팀.</summary>
        SurviveSeconds,
    }

    /// <summary>13-1: 장 준비 목표 하나. 한 번 채우면 완료로 남는다 (인구가 줄어도 다시 열리지 않음).</summary>
    [Serializable]
    public sealed class ChapterGoal
    {
        [SerializeField] private ChapterGoalKind _kind;
        [Tooltip("목표 문구 (비면 종류에 맞춰 자동)")]
        [SerializeField] private string _text;
        [Tooltip("선택 목표 (필수가 아님, 채우면 보상)")]
        [SerializeField] private bool _optional;
        [Tooltip("ReachGrade: 등급 인덱스")]
        [SerializeField, Min(0)] private int _grade;
        [Tooltip("BuildModule: 모듈")]
        [SerializeField] private ModuleData _module;
        [Tooltip("BuildModule · Population: 개수 / 인원")]
        [SerializeField, Min(0)] private int _count = 1;
        [Tooltip("Stock: 자원")]
        [SerializeField] private ResourceType _resource;
        [Tooltip("Stock: 양 · SurviveSeconds: 초")]
        [SerializeField, Min(0f)] private float _amount;
        [Tooltip("Flag: 표시 이름")]
        [SerializeField] private string _flag;

        [Header("선택 목표 보상")]
        [SerializeField] private List<ResourceAmount> _rewardResources = new List<ResourceAmount>();
        [SerializeField, Min(0)] private int _rewardResearchPoints;
        [SerializeField, Min(0)] private int _rewardFreeRepairs;

        public ChapterGoalKind Kind => _kind;
        public string Text => _text;
        public bool Optional => _optional;
        public int Grade => _grade;
        public ModuleData Module => _module;
        public int Count => _count;
        public ResourceType Resource => _resource;
        public float Amount => _amount;
        public string Flag => _flag;
        public IReadOnlyList<ResourceAmount> RewardResources => _rewardResources;
        public int RewardResearchPoints => _rewardResearchPoints;
        public int RewardFreeRepairs => _rewardFreeRepairs;
        public bool HasReward => _rewardResources.Count > 0 || _rewardResearchPoints > 0 || _rewardFreeRepairs > 0;
    }

    /// <summary>13-1: 장마다 켜는 규칙 (장 틀이 있으면 등급 대신 이것으로 시스템을 연다).</summary>
    [Flags]
    public enum StationRule
    {
        None = 0,
        /// <summary>유성우 방향 예보 (기존 도시급 규칙, 2장 예정).</summary>
        MeteorShower = 1,
        /// <summary>상시 정책 (기존 국가급 규칙, 3장 예정).</summary>
        Policies = 2,
        /// <summary>합성 (4장 예정).</summary>
        Synthesis = 4,
        /// <summary>열 관리 (5장 예정).</summary>
        Heat = 8,
        /// <summary>사회 위기 선택지 (6장 예정).</summary>
        SocialCrisis = 16,
        /// <summary>방문 세력 (7장 예정).</summary>
        Contact = 32,
    }

    /// <summary>13-1: 절정 사건의 종류. 결과 판정은 절정마다 전용 기준 (사용자 결정 2026-10-15).</summary>
    public enum ChapterClimaxKind
    {
        /// <summary>절정 없음 (예고 뒤 바로 '깔끔히 버팀').</summary>
        None,
        /// <summary>그레이박스: 운석 연타 — 파 수 × 파당 피해 개수. 판정 = 파손 · 파괴 개수.</summary>
        MeteorBarrage,
    }

    /// <summary>13-1: 절정 설정 (종류마다 쓰는 값이 다름).</summary>
    [Serializable]
    public sealed class ChapterClimaxSettings
    {
        [SerializeField] private ChapterClimaxKind _kind;
        [SerializeField] private string _title = "절정";
        [TextArea(2, 4)]
        [SerializeField] private string _description;
        [Tooltip("필수 목표를 채운 뒤 절정까지 남은 시간 (초, [바로 맞이하기]로 당길 수 있음)")]
        [SerializeField, Min(0f)] private float _warningSeconds = 180f;

        [Header("운석 연타 (MeteorBarrage)")]
        [SerializeField, Min(1)] private int _waves = 3;
        [SerializeField, Min(1f)] private float _waveInterval = 20f;
        [SerializeField, Min(1)] private int _hitsPerWave = 2;
        [Tooltip("마지막 파 뒤 결과를 내기까지 (파괴 타이머가 도는 시간)")]
        [SerializeField, Min(0f)] private float _settleSeconds = 30f;
        [Tooltip("깔끔히 버팀: 파손 개수 이하 + 파괴 0")]
        [SerializeField, Min(0)] private int _cleanMaxDamaged = 1;
        [Tooltip("피해를 입고 버팀: 파괴 개수 이하 (넘으면 일부를 잃음)")]
        [SerializeField, Min(0)] private int _hurtMaxDestroyed = 0;

        public ChapterClimaxKind Kind => _kind;
        public string Title => _title;
        public string Description => _description;
        public float WarningSeconds => _warningSeconds;
        public int Waves => _waves;
        public float WaveInterval => _waveInterval;
        public int HitsPerWave => _hitsPerWave;
        public float SettleSeconds => _settleSeconds;
        public int CleanMaxDamaged => _cleanMaxDamaged;
        public int HurtMaxDestroyed => _hurtMaxDestroyed;
    }

    /// <summary>13-1: 장 전환에서 고르는 방주의 방향 하나 (효과 · 코어 성격 축은 13-2).</summary>
    [Serializable]
    public sealed class ChapterChoice
    {
        [SerializeField] private string _id;
        [SerializeField] private string _title;
        [TextArea(2, 4)]
        [SerializeField] private string _description;

        public string Id => _id;
        public string Title => _title;
        public string Description => _description;
    }

    /// <summary>
    /// 13-1 장 하나 (REDESIGN P3-2: 준비 → 절정 → 전환). 장 틀이 있으면 해금 · 새 요구 · 이벤트 규모 · 규칙은 등급 대신 장이 정한다
    /// (사용자 결정 2026-10-15, D-2). 해금 · 요구는 지금 장까지 누적.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/Chapter", fileName = "CH_New")]
    public sealed class ChapterData : ScriptableObject
    {
        [SerializeField] private string _displayName = "1장";
        [Tooltip("장 부제 (이야기 이름, 이야기 설계 때)")]
        [SerializeField] private string _subtitle;

        [Header("해금 · 요구 (이 장에서 새로)")]
        [SerializeField] private List<ModuleData> _unlocks = new List<ModuleData>();
        [SerializeField] private List<ResidentNeed> _newNeeds = new List<ResidentNeed>();
        [Tooltip("이 장에서 켜는 규칙 (지난 장 규칙도 누적)")]
        [SerializeField] private StationRule _rules;

        [Header("이벤트 규모 (BALANCE 13번과 같은 뜻)")]
        [SerializeField, Min(1)] private int _meteorHitsMin = 1;
        [SerializeField, Min(1)] private int _meteorHitsMax = 1;
        [SerializeField, Min(0.05f)] private float _eventIntervalMultiplier = 1f;
        [SerializeField, Min(0.05f)] private float _eventIntensityMultiplier = 1f;

        [Header("등급 목표를 채우는 순간 (사용자 결정 2026-10-15)")]
        [Tooltip("이 장의 등급 도달 목표를 채우는 순간 생기는 요구 (등급이 오르는 때와 같은 시점, 한 번 생기면 유지)")]
        [SerializeField] private List<ResidentNeed> _gradeUpNeeds = new List<ResidentNeed>();
        [Tooltip("등급 목표를 채우는 순간 이벤트 규모를 아래 값으로 올림 (끄면 장 시작 값 그대로)")]
        [SerializeField] private bool _gradeUpScale;
        [SerializeField, Min(1)] private int _gradeUpMeteorHitsMin = 1;
        [SerializeField, Min(1)] private int _gradeUpMeteorHitsMax = 1;
        [SerializeField, Min(0.05f)] private float _gradeUpEventIntervalMultiplier = 1f;
        [SerializeField, Min(0.05f)] private float _gradeUpEventIntensityMultiplier = 1f;

        [Header("준비 목표 · 절정 · 전환")]
        [SerializeField] private List<ChapterGoal> _goals = new List<ChapterGoal>();
        [SerializeField] private ChapterClimaxSettings _climax = new ChapterClimaxSettings();
        [Tooltip("장 전환에서 고르는 방주의 방향 (비면 고르지 않고 넘어감)")]
        [SerializeField] private List<ChapterChoice> _choices = new List<ChapterChoice>();

        public string DisplayName => _displayName;
        public string Subtitle => _subtitle;
        public IReadOnlyList<ModuleData> Unlocks => _unlocks;
        public IReadOnlyList<ResidentNeed> NewNeeds => _newNeeds;
        public StationRule Rules => _rules;
        public int MeteorHitsMin => Mathf.Max(1, _meteorHitsMin);
        public int MeteorHitsMax => Mathf.Max(MeteorHitsMin, _meteorHitsMax);
        public float EventIntervalMultiplier => _eventIntervalMultiplier > 0f ? _eventIntervalMultiplier : 1f;
        public float EventIntensityMultiplier => _eventIntensityMultiplier > 0f ? _eventIntensityMultiplier : 1f;
        public IReadOnlyList<ResidentNeed> GradeUpNeeds => _gradeUpNeeds;
        public bool HasGradeUpScale => _gradeUpScale;
        public int GradeUpMeteorHitsMin => Mathf.Max(1, _gradeUpMeteorHitsMin);
        public int GradeUpMeteorHitsMax => Mathf.Max(GradeUpMeteorHitsMin, _gradeUpMeteorHitsMax);
        public float GradeUpEventIntervalMultiplier => _gradeUpEventIntervalMultiplier > 0f ? _gradeUpEventIntervalMultiplier : 1f;
        public float GradeUpEventIntensityMultiplier => _gradeUpEventIntensityMultiplier > 0f ? _gradeUpEventIntensityMultiplier : 1f;
        public IReadOnlyList<ChapterGoal> Goals => _goals;
        public ChapterClimaxSettings Climax => _climax;
        public IReadOnlyList<ChapterChoice> Choices => _choices;
    }
}
