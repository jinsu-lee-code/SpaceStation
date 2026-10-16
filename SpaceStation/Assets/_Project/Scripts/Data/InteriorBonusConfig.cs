using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>
    /// 13-6 타이쿤판 1인칭 가볍게 (TYCOON.md Q3 '선택형 보너스', 사용자 결정 2026-10-16, BALANCE 46번). 메뉴 SpaceStation/Tycoon/Setup Interior Bonus가 만든다.
    /// 들어가면 이득만, 안 들어가도 막히지 않게:
    /// - 내부 사고: 그대로 생기고 처리하면 보상, 놓치거나 나가도 손실 없음 (손실 배율)
    /// - 보급 상자: 줍지 않으면 다음 보급선 때 바깥에서 자동 회수 (자원 상자 양 × 비율, 특별 상자는 그대로) — 직접 주우면 바로 · 전부
    /// - 연구 포인트: 등급에 처음 오를 때마다 지급 (내부 밖에서도 레시피 해금 · 연구 할인을 쓸 수 있게)
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/Interior Bonus Config", fileName = "InteriorBonusConfig")]
    public sealed class InteriorBonusConfig : ScriptableObject
    {
        [Tooltip("놓친 내부 사고의 손실 배율 (0 = 손실 없음, 1 = BALANCE 31번 그대로)")]
        [SerializeField, Min(0f)] private float _incidentMissScale;
        [Tooltip("줍지 않은 보급 상자를 다음 보급선 때 회수하는 자원 비율 (음수 = 자동 회수 끔)")]
        [SerializeField] private float _crateAutoCollectRate = 0.5f;
        [Tooltip("등급에 처음 오를 때 받는 연구 포인트 (등급 번호 순: 0 = 등급 없음, 1 = 소형 …, 목록 밖 = 마지막 값)")]
        [SerializeField] private List<int> _gradeUpResearchPoints = new List<int> { 0, 1, 2, 2, 3, 3, 4, 4 };

        public float IncidentMissScale => _incidentMissScale;
        public float CrateAutoCollectRate => _crateAutoCollectRate;

        /// <summary>그 등급에 처음 오를 때 받는 연구 포인트.</summary>
        public int GradeUpPoints(int grade)
        {
            if (grade <= 0 || _gradeUpResearchPoints.Count == 0)
                return 0;
            return Mathf.Max(0, _gradeUpResearchPoints[Mathf.Min(grade, _gradeUpResearchPoints.Count - 1)]);
        }

        /// <summary>에디터 설정 메뉴 · 시험용.</summary>
        public void Set(float incidentMissScale, float crateAutoCollectRate, List<int> gradeUpPoints)
        {
            _incidentMissScale = incidentMissScale;
            _crateAutoCollectRate = crateAutoCollectRate;
            _gradeUpResearchPoints = gradeUpPoints;
        }
    }
}
