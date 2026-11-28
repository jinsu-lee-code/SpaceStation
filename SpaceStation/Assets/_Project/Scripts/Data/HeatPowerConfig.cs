using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>모듈이 좋아하는 온도 (CONNECT_1121 A10).</summary>
    public enum HeatPreference
    {
        /// <summary>온도 무관 — 보통 한도.</summary>
        None,
        /// <summary>따뜻함: 온기 구간에서 전력 ↓ · 생산 ↑.</summary>
        Warm,
        /// <summary>서늘함: 이득 없음 · 한도 낮음 (더 빨리 과열).</summary>
        Cool,
    }

    /// <summary>
    /// CONNECT_1121 3번 ⑤ 열 온기 (A10) · 과부하 (A11) — 엔드리스 값 (사용자 결정 2026-11-28, 추천안). 캠페인(5장 = 열의 장)은 통합 밸런싱 때.
    /// 이 설정이 없으면(main · 서사판) 열은 예전 그대로 (모든 모듈 한도 같음 · 온기 없음 · 과부하 없음).
    /// 메뉴 SpaceStation/Tycoon/Setup Heat Comfort가 만든다 (BALANCE 16번 뒤 "열 온기 · 과부하").
    /// </summary>
    [CreateAssetMenu(fileName = "HeatPowerConfig", menuName = "SpaceStation/Tycoon/Heat Power Config")]
    public sealed class HeatPowerConfig : ScriptableObject
    {
        [Header("A10 온기 — 모듈마다 알맞은 온도")]
        [Tooltip("따뜻함을 좋아하는 모듈 (온기 구간에서 전력 ↓ · 생산 ↑)")]
        [SerializeField] private List<ModuleData> _warm = new List<ModuleData>();
        [Tooltip("서늘함을 좋아하는 모듈 (한도 낮음)")]
        [SerializeField] private List<ModuleData> _cool = new List<ModuleData>();
        [Tooltip("온기 구간 (열, 이상 · 이하). 0.5 = 연료전지 바로 옆 · 핵융합로 두 칸 안")]
        [SerializeField, Min(0f)] private float _warmMin = 0.5f;
        [SerializeField, Min(0f)] private float _warmMax = 4f;
        [Tooltip("온기 구간의 따뜻함 모듈 전력 소비 배율 (난방 전력 ↓)")]
        [SerializeField, Range(0f, 1f)] private float _warmPowerMultiplier = 0.8f;
        [Tooltip("온기 구간의 따뜻함 모듈 생산 배율 (옛 인접 '해빙 보조' 값을 이어받음)")]
        [SerializeField, Min(1f)] private float _warmProductionMultiplier = 1.15f;
        [Tooltip("서늘함 모듈의 한도 = 보통 한도 × 이 값")]
        [SerializeField, Range(0.1f, 1f)] private float _coolLimitRatio = 0.5f;

        [Header("A10 안쪽 얼굴 (방의 온도)")]
        [Tooltip("따뜻한 방(온기 구간의 따뜻함 모듈) 주민 부탁의 F 시간 배율 (가볍게)")]
        [SerializeField, Range(0.1f, 1f)] private float _warmRequestHold = 0.6f;
        [Tooltip("따뜻한 방 주민 부탁 보상 배율 (기분 ↑)")]
        [SerializeField, Min(1f)] private float _warmRequestReward = 1.5f;
        [Tooltip("과열 방에서 화재가 뽑힐 무게 배율 (불길도 둘)")]
        [SerializeField, Min(1f)] private float _hotFireWeight = 2f;
        [Tooltip("서늘한 방(가동 중인 방열판이 맞닿은 방)에서 차단기가 뽑힐 무게 배율")]
        [SerializeField, Range(0f, 1f)] private float _coolBreakerWeight = 0.5f;

        [Header("A11 과부하 — 배전 범위 (안쪽만)")]
        [Tooltip("발전 모듈 · 배터리에서 이 칸 수(체비셰프, 칸끼리 가장 가까운 거리) 안이면 배전 범위 안")]
        [SerializeField, Min(1)] private int _gridRange = 3;
        [Tooltip("대전력 = 전력 소비가 이 값 이상인 기본 모듈 (발열원 · 진화 모듈 제외)")]
        [SerializeField, Min(0f)] private float _heavyPower = 5f;
        [Tooltip("배전 범위 밖 대전력 방에서 차단기가 뽑힐 무게 배율")]
        [SerializeField, Min(1f)] private float _overloadBreakerWeight = 2f;
        [Tooltip("배전 범위 밖 대전력 방 차단기의 내려간 스위치 추가 수")]
        [SerializeField, Min(0)] private int _overloadExtraSwitches = 1;

        public float WarmMin => _warmMin;
        public float WarmMax => _warmMax;
        public float WarmPowerMultiplier => _warmPowerMultiplier;
        public float WarmProductionMultiplier => _warmProductionMultiplier;
        public float CoolLimitRatio => _coolLimitRatio;
        public float WarmRequestHold => _warmRequestHold;
        public float WarmRequestReward => _warmRequestReward;
        public float HotFireWeight => _hotFireWeight;
        public float CoolBreakerWeight => _coolBreakerWeight;
        public int GridRange => _gridRange;
        public float HeavyPower => _heavyPower;
        public float OverloadBreakerWeight => _overloadBreakerWeight;
        public int OverloadExtraSwitches => _overloadExtraSwitches;
        public IReadOnlyList<ModuleData> WarmModules => _warm;
        public IReadOnlyList<ModuleData> CoolModules => _cool;

        public HeatPreference PreferenceOf(ModuleData data)
        {
            if (data == null)
                return HeatPreference.None;
            if (_warm.Contains(data))
                return HeatPreference.Warm;
            if (_cool.Contains(data))
                return HeatPreference.Cool;
            return HeatPreference.None;
        }

        /// <summary>그 열이 온기 구간 안인지.</summary>
        public bool InWarmBand(float heat) => heat >= _warmMin - 1e-4f && heat <= _warmMax + 1e-4f;

#if UNITY_EDITOR
        public void EditorSet(IEnumerable<ModuleData> warm, IEnumerable<ModuleData> cool)
        {
            _warm = new List<ModuleData>();
            foreach (var m in warm)
                if (m != null)
                    _warm.Add(m);
            _cool = new List<ModuleData>();
            foreach (var m in cool)
                if (m != null)
                    _cool.Add(m);
        }
#endif

        /// <summary>시험용 (값은 기본 · 모듈 목록만).</summary>
        public static HeatPowerConfig CreateForTest(IEnumerable<ModuleData> warm, IEnumerable<ModuleData> cool)
        {
            var c = CreateInstance<HeatPowerConfig>();
            c._warm = new List<ModuleData>(warm);
            c._cool = new List<ModuleData>(cool);
            return c;
        }
    }
}
