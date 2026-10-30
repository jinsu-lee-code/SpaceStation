using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>
    /// 13-3 서사판 이민선 시계 + 종족 (REDESIGN P3-1, 사용자 결정 2026-10-18 D-8 · D-9).
    /// 장마다 켜는 값(간격 · 규모 · 예보 · 새 종족 · 자연 증가 배율)은 <see cref="ChapterData"/>의 이민선 칸.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/Ark Ship Config", fileName = "ArkShipConfig")]
    public sealed class ArkShipConfig : ScriptableObject
    {
        [Tooltip("1장 시작 주민 종족 (한 종족, 사용자 결정)")]
        [SerializeField] private SpeciesData _startSpecies;
        [Tooltip("모든 종족 (세이브 · 표시용 찾기)")]
        [SerializeField] private List<SpeciesData> _species = new List<SpeciesData>();
        [Tooltip("종족 기술 + 개인 특성 합산 상한 (효과 하나당)")]
        [SerializeField, Range(0f, 1f)] private float _abilityCap = 0.2f;

        [Header("위기 완충 (무너지는 정거장)")]
        [Tooltip("만족도가 이보다 낮거나 산소 · 물 · 식량 중 하나가 바닥이면 위기")]
        [SerializeField, Range(0f, 100f)] private float _crisisSatisfaction = 50f;
        [Tooltip("위기 동안 이민선 시계가 가는 속도 (0.5 = 절반)")]
        [SerializeField, Range(0.05f, 1f)] private float _crisisClockScale = 0.5f;

        [Header("여객 도킹 포트 (있으면 규모 +)")]
        [SerializeField, Min(0)] private int _portSizeBonus = 2;
        [SerializeField, Min(0f)] private float _portForecastBonus = 15f;
        [SerializeField, Min(0)] private int _maxPortsCounted = 2;

        [Header("코어 성격 (받기 / 빈 집만큼 / 거부)")]
        [SerializeField] private List<AxisShift> _acceptShifts = new List<AxisShift>();
        [SerializeField] private List<AxisShift> _partialShifts = new List<AxisShift>();
        [SerializeField] private List<AxisShift> _refuseShifts = new List<AxisShift>();

        [Tooltip("화면 없이 돌 때(봇 · 시험) 도착한 배를 이만큼 기다린 뒤 '빈 집만큼'으로 (초)")]
        [SerializeField, Min(1f)] private float _autoResolveSeconds = 30f;

        public SpeciesData StartSpecies => _startSpecies;
        public IReadOnlyList<SpeciesData> Species => _species;
        public float AbilityCap => _abilityCap;
        public float CrisisSatisfaction => _crisisSatisfaction;
        public float CrisisClockScale => _crisisClockScale;
        public int PortSizeBonus => _portSizeBonus;
        public float PortForecastBonus => _portForecastBonus;
        public int MaxPortsCounted => _maxPortsCounted;
        public IReadOnlyList<AxisShift> AcceptShifts => _acceptShifts;
        public IReadOnlyList<AxisShift> PartialShifts => _partialShifts;
        public IReadOnlyList<AxisShift> RefuseShifts => _refuseShifts;
        public float AutoResolveSeconds => _autoResolveSeconds;

        public SpeciesData Find(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            foreach (var s in _species)
                if (s != null && s.Id == id)
                    return s;
            return _startSpecies != null && _startSpecies.Id == id ? _startSpecies : null;
        }

#if UNITY_EDITOR
        public void EditorSet(SpeciesData start, List<SpeciesData> species, List<AxisShift> accept, List<AxisShift> partial, List<AxisShift> refuse)
        {
            _startSpecies = start;
            _species = species;
            _acceptShifts = accept;
            _partialShifts = partial;
            _refuseShifts = refuse;
        }
#endif
    }
}
