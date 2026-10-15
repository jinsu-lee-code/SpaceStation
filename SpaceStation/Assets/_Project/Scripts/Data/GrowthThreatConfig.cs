using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>13-2: 노출 면 총합 기준점 하나 (운석 개수 · 이벤트 간격).</summary>
    [Serializable]
    public struct ExposurePoint
    {
        [Tooltip("우주로 노출된 면 총합")]
        public float Exposure;
        [Tooltip("운석 1회 파손 모듈 수 (최소 · 최대)")]
        public float HitsMin;
        public float HitsMax;
        [Tooltip("이벤트 간격 배율 (작을수록 자주)")]
        public float IntervalMultiplier;
        [Tooltip("유성우 배율 (운석 1회 개수에 곱해 여러 파로 나눔, BalanceConfig 기본 3) — 작은 정거장은 낮게")]
        public float ShowerMultiplier;

        public ExposurePoint(float exposure, float hitsMin, float hitsMax, float interval, float shower)
        {
            Exposure = exposure;
            HitsMin = hitsMin;
            HitsMax = hitsMax;
            IntervalMultiplier = interval;
            ShowerMultiplier = shower;
        }
    }

    /// <summary>13-2: 모듈 수 기준점 하나 (이벤트 강도).</summary>
    [Serializable]
    public struct ModulePoint
    {
        [Tooltip("등급 조건으로 세는 모듈 수")]
        public float Modules;
        [Tooltip("이벤트 강도 배율 (산소 누출 비율 · 태양 폭풍 시간 · 보급선 양)")]
        public float Intensity;

        public ModulePoint(float modules, float intensity)
        {
            Modules = modules;
            Intensity = intensity;
        }
    }

    /// <summary>
    /// 13-2 타이쿤판 성장 비례 위협 (TYCOON.md K2-1, BALANCE 42번). 메뉴 SpaceStation/Tycoon/Setup Growth Threat가 만든다.
    /// 등급 표(BALANCE 13 · 24 · 27번) 대신 정거장 상태가 위기 크기를 정한다 — 기준점 사이는 직선으로 잇고, 마지막 기준점 뒤는 그대로.
    /// - 노출 면 총합 → 운석 개수 · 이벤트 간격 (유성우 개수도 운석 개수 기준)
    /// - 모듈 수 → 이벤트 강도
    /// - 밀집도(인구 ÷ 거주 수용) → 파업 기준 만족도 +
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/Growth Threat Config", fileName = "GrowthThreatConfig")]
    public sealed class GrowthThreatConfig : ScriptableObject
    {
        [SerializeField] private List<ExposurePoint> _exposure = new List<ExposurePoint>();
        [SerializeField] private List<ModulePoint> _modules = new List<ModulePoint>();
        [Tooltip("13-2 보강: 위협을 지금 등급 값 ~ 다음 등급 값 사이로 제한 (다음 등급 값이 더 크지 않으면 하한만 — 도시급부터는 계속 커짐)")]
        [SerializeField] private bool _clampToGradeBand = true;
        [Header("밀집도 → 파업 기준")]
        [Tooltip("밀집도(인구 ÷ 거주 수용)가 이 값을 넘으면 파업 기준이 오르기 시작")]
        [SerializeField, Range(0f, 1f)] private float _crowdingStart = 0.8f;
        [Tooltip("밀집도 100%일 때 파업 기준 만족도에 더하는 값")]
        [SerializeField, Min(0f)] private float _crowdingMaxThreshold = 10f;

        public IReadOnlyList<ExposurePoint> Exposure => _exposure;
        public IReadOnlyList<ModulePoint> Modules => _modules;
        public float CrowdingStart => _crowdingStart;
        public bool ClampToGradeBand { get => _clampToGradeBand; set => _clampToGradeBand = value; }
        public float CrowdingMaxThreshold => _crowdingMaxThreshold;

        /// <summary>노출 면 총합에서 (최소 개수, 최대 개수, 간격 배율). 기준점이 없으면 (1, 1, 1).</summary>
        public (float HitsMin, float HitsMax, float Interval) AtExposure(float exposure)
        {
            if (_exposure.Count == 0)
                return (1f, 1f, 1f);
            if (exposure <= _exposure[0].Exposure)
                return (_exposure[0].HitsMin, _exposure[0].HitsMax, _exposure[0].IntervalMultiplier);
            for (int i = 1; i < _exposure.Count; i++)
            {
                var b = _exposure[i];
                if (exposure > b.Exposure)
                    continue;
                var a = _exposure[i - 1];
                float t = b.Exposure > a.Exposure ? (exposure - a.Exposure) / (b.Exposure - a.Exposure) : 1f;
                return (Mathf.Lerp(a.HitsMin, b.HitsMin, t), Mathf.Lerp(a.HitsMax, b.HitsMax, t), Mathf.Lerp(a.IntervalMultiplier, b.IntervalMultiplier, t));
            }
            var last = _exposure[_exposure.Count - 1];
            return (last.HitsMin, last.HitsMax, last.IntervalMultiplier);
        }

        /// <summary>노출 면 총합에서 유성우 배율. 기준점이 없으면 -1 (= BalanceConfig 값 그대로).</summary>
        public float ShowerAt(float exposure)
        {
            if (_exposure.Count == 0)
                return -1f;
            if (exposure <= _exposure[0].Exposure)
                return _exposure[0].ShowerMultiplier;
            for (int i = 1; i < _exposure.Count; i++)
            {
                var b = _exposure[i];
                if (exposure > b.Exposure)
                    continue;
                var a = _exposure[i - 1];
                float t = b.Exposure > a.Exposure ? (exposure - a.Exposure) / (b.Exposure - a.Exposure) : 1f;
                return Mathf.Lerp(a.ShowerMultiplier, b.ShowerMultiplier, t);
            }
            return _exposure[_exposure.Count - 1].ShowerMultiplier;
        }

        /// <summary>모듈 수에서 이벤트 강도. 기준점이 없으면 1.</summary>
        public float IntensityAt(float modules)
        {
            if (_modules.Count == 0)
                return 1f;
            if (modules <= _modules[0].Modules)
                return _modules[0].Intensity;
            for (int i = 1; i < _modules.Count; i++)
            {
                var b = _modules[i];
                if (modules > b.Modules)
                    continue;
                var a = _modules[i - 1];
                float t = b.Modules > a.Modules ? (modules - a.Modules) / (b.Modules - a.Modules) : 1f;
                return Mathf.Lerp(a.Intensity, b.Intensity, t);
            }
            return _modules[_modules.Count - 1].Intensity;
        }

        /// <summary>밀집도에서 파업 기준 + 값.</summary>
        public float CrowdingThreshold(float crowding)
        {
            if (crowding <= _crowdingStart || _crowdingStart >= 1f)
                return 0f;
            return _crowdingMaxThreshold * Mathf.Clamp01((crowding - _crowdingStart) / (1f - _crowdingStart));
        }

        /// <summary>에디터 설정 메뉴 · 시험용.</summary>
        public void Set(List<ExposurePoint> exposure, List<ModulePoint> modules, float crowdingStart, float crowdingMax)
        {
            _exposure = exposure;
            _modules = modules;
            _crowdingStart = crowdingStart;
            _crowdingMaxThreshold = crowdingMax;
        }
    }
}
