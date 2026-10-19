using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>
    /// 13-3 서사판 종족 하나 (REDESIGN T5 · V2 · S2, 사용자 결정 2026-10-18 D-8: 기본값 = 사회 1 + 기술 1 + 견딤 1).
    /// 주민의 종류 = <see cref="Id"/> (동물 모델 Id와 같게 — 내부에서 그 동물로 보임). 개인 특성(Phase 10)은 그 위에서 따로.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/Species", fileName = "SP_New")]
    public sealed class SpeciesData : ScriptableObject
    {
        [Tooltip("주민 종류 Id = 동물 모델 Id (rabbit · bear …)")]
        [SerializeField] private string _id;
        [SerializeField] private string _displayName;
        [TextArea(2, 3)]
        [SerializeField] private string _description;

        [Header("사회 (V2: 중시 요구 · 정책 반응)")]
        [Tooltip("이 종족이 중요하게 여기는 요구 — 채워지지 않으면 감점이 (종족 비율 × 가중치)만큼 커짐")]
        [SerializeField] private ResidentNeed _favoredNeed;
        [SerializeField, Min(0f)] private float _favoredWeight = 0.5f;
        [Tooltip("통제형 정책 반응 (+ 순응 · − 반발). 13-6 정책에서 씀")]
        [SerializeField, Range(-1f, 1f)] private float _controlReaction;

        [Header("기술 (특성 통로에 더함)")]
        [Tooltip("효과 통로: 원예가(식량 생산) · 기술자(수리 시간) · 과학자(연구 속도) · 정비공(노후 속도)")]
        [SerializeField] private ResidentTrait _techTrait = ResidentTrait.Gardener;
        [SerializeField, Min(0f)] private float _techPerResident = 0.02f;
        [SerializeField, Min(0f)] private float _techMax = 0.1f;

        [Header("견딤 (S2 — 열 · 방사선은 5장, 1~3장은 값만)")]
        [SerializeField, Range(-1f, 1f)] private float _heatTolerance;
        [SerializeField, Range(-1f, 1f)] private float _radiationTolerance;

        [Header("이민선 (13-6b)")]
        [Tooltip("이민선 몸통 띠 색 (흰 띠 재질을 물들임)")]
        [SerializeField] private Color _shipColor = Color.white;
        [Tooltip("이 종족 배를 처음 맞을 때 코어 독백 (도킹 연출 뒤, 2~3줄)")]
        [SerializeField, TextArea(1, 3)] private List<string> _arrivalLines = new List<string>();

        [Header("13-7c 주민 한마디 (종족 말투, 사건 키별 — 같은 키가 여럿이면 그중 하나)")]
        [SerializeField] private List<SpeciesRemark> _remarks = new List<SpeciesRemark>();

        public string Id => _id;
        public IReadOnlyList<SpeciesRemark> Remarks => _remarks;
        public Color ShipColor => _shipColor;
        public IReadOnlyList<string> ArrivalLines => _arrivalLines;
        public string DisplayName => _displayName;
        public string Description => _description;
        public ResidentNeed FavoredNeed => _favoredNeed;
        public float FavoredWeight => _favoredWeight;
        public float ControlReaction => _controlReaction;
        public ResidentTrait TechTrait => _techTrait;
        public float TechPerResident => _techPerResident;
        public float TechMax => _techMax;
        public float HeatTolerance => _heatTolerance;
        public float RadiationTolerance => _radiationTolerance;

        /// <summary>이 종족 n명이 주는 기술 효과 (상한 적용).</summary>
        public float TechFor(int count) => Mathf.Min(count * _techPerResident, _techMax);

        public static string TechName(ResidentTrait trait)
        {
            switch (trait)
            {
                case ResidentTrait.Gardener: return "식량 생산";
                case ResidentTrait.Technician: return "수리 속도";
                case ResidentTrait.Scientist: return "연구 속도";
                case ResidentTrait.Mechanic: return "노후 방지";
                default: return trait.ToString();
            }
        }

        public static string ToleranceText(float v) => v >= 0.5f ? "강함" : v <= -0.5f ? "약함" : "보통";

        /// <summary>"의료 중시 · 식량 생산 +2%/명 (최대 +10%) · 열 보통 · 방사선 보통".</summary>
        public string Summary()
        {
            var parts = new List<string>();
            if (_favoredNeed != ResidentNeed.None)
                parts.Add($"{_favoredNeed.DisplayName()} 중시");
            if (_techPerResident > 0f)
                parts.Add($"{TechName(_techTrait)} +{_techPerResident * 100f:0.#}%/명 (최대 +{_techMax * 100f:0.#}%)");
            parts.Add($"열 {ToleranceText(_heatTolerance)} · 방사선 {ToleranceText(_radiationTolerance)}");
            return string.Join(" · ", parts);
        }

#if UNITY_EDITOR
        public void EditorSet(string id, string displayName, string description, ResidentNeed favored, float favoredWeight, float control,
            ResidentTrait tech, float techPer, float techMax, float heat, float radiation)
        {
            _id = id;
            _displayName = displayName;
            _description = description;
            _favoredNeed = favored;
            _favoredWeight = favoredWeight;
            _controlReaction = control;
            _techTrait = tech;
            _techPerResident = techPer;
            _techMax = techMax;
            _heatTolerance = heat;
            _radiationTolerance = radiation;
        }

        public void EditorSetShip(Color color, params string[] arrivalLines)
        {
            _shipColor = color;
            _arrivalLines = new List<string>(arrivalLines);
        }

        public void EditorSetRemarks(params (string key, string text)[] remarks)
        {
            _remarks = new List<SpeciesRemark>();
            foreach (var (key, text) in remarks)
                _remarks.Add(new SpeciesRemark { Key = key, Text = text });
        }
#endif
    }

    /// <summary>13-7c 주민 한마디 하나: 사건 키(<c>chapter.start.2</c> · <c>climax.warning.3</c> · <c>climax.Clean</c> · <c>ship.accept</c> …) → 그 종족 말투의 한 줄.</summary>
    [System.Serializable]
    public sealed class SpeciesRemark
    {
        public string Key;
        [TextArea(1, 2)] public string Text;
    }
}
