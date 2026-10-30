using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>13-2 코어 성격 4축 (REDESIGN E2). 값 −100 ~ +100, 음수 = 앞쪽 극(효율 · 통제 · 보존 · 기억), 양수 = 뒤쪽 극.</summary>
    public enum CoreAxis
    {
        /// <summary>효율 ↔ 연민</summary>
        Care,
        /// <summary>통제 ↔ 자율</summary>
        Control,
        /// <summary>보존 ↔ 확장</summary>
        Scope,
        /// <summary>기억 ↔ 망각 (지구와 인간을 기억하느냐, T8)</summary>
        Memory,
    }

    /// <summary>축의 어느 쪽 (문장 고르기 · 이동 방향).</summary>
    public enum CorePole
    {
        /// <summary>어느 쪽으로도 기울지 않음 (문장: 기본).</summary>
        Neutral,
        /// <summary>앞쪽 극: 효율 · 통제 · 보존 · 기억.</summary>
        First,
        /// <summary>뒤쪽 극: 연민 · 자율 · 확장 · 망각.</summary>
        Second,
    }

    /// <summary>13-2 움직이는 폭 3단계 (사용자 결정 2026-10-15). 실제 크기는 <see cref="CoreConfig"/>.</summary>
    public enum ShiftSize
    {
        Small,
        Medium,
        Large,
    }

    /// <summary>선택 하나가 축 하나를 움직이는 양.</summary>
    [Serializable]
    public struct AxisShift
    {
        public CoreAxis Axis;
        [Tooltip("움직이는 쪽 (Neutral이면 움직이지 않음)")]
        public CorePole Toward;
        public ShiftSize Size;

        public AxisShift(CoreAxis axis, CorePole toward, ShiftSize size)
        {
            Axis = axis;
            Toward = toward;
            Size = size;
        }
    }

    /// <summary>
    /// 13-2 코어 문장 하나: 계기(키) × 성향(축의 어느 쪽) → 문장. 정해진 문장을 고르는 방식 (LLM 쓰지 않음, E1).
    /// 문장 안의 {0}, {1} …은 부르는 쪽이 넘긴 값으로 바뀜.
    /// </summary>
    [Serializable]
    public sealed class CoreLine
    {
        [Tooltip("계기 키 (예: goal.done, climax.warning, chapter.start, gradeup, outcome.Clean, choice.expand)")]
        public string Key;
        [Tooltip("Neutral = 기울지 않았을 때(기본 문장), 아니면 이 축이 그쪽으로 기울었을 때")]
        public CorePole Pole;
        public CoreAxis Axis;
        [TextArea(1, 3)]
        public string Text;
    }

    /// <summary>
    /// 13-2 코어 성격 설정 (사용자 결정 2026-10-15, D-4): 3단계 폭 · 기울기 문턱 · 문장 표.
    /// 값은 숨기고 말로만 보인다 ("연민 쪽으로 기울어짐").
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/Core Config", fileName = "CoreConfig")]
    public sealed class CoreConfig : ScriptableObject
    {
        [Header("움직이는 폭 (축 범위 −100 ~ +100)")]
        [SerializeField, Min(0f)] private float _small = 5f;
        [SerializeField, Min(0f)] private float _medium = 10f;
        [SerializeField, Min(0f)] private float _large = 20f;

        [Header("기울기 문턱 (절댓값)")]
        [Tooltip("이 아래면 '균형', 문장도 기본(Neutral)")]
        [SerializeField, Min(0f)] private float _slight = 15f;
        [Tooltip("이 이상이면 '기울어짐'")]
        [SerializeField, Min(0f)] private float _leaning = 40f;
        [Tooltip("이 이상이면 '깊이 기울어짐'")]
        [SerializeField, Min(0f)] private float _deep = 70f;

        [Header("문장 표 (계기 × 성향)")]
        [SerializeField] private List<CoreLine> _lines = new List<CoreLine>();

        public float Slight => _slight;
        public float Leaning => _leaning;
        public float Deep => _deep;
        public IReadOnlyList<CoreLine> Lines => _lines;

        public float Amount(ShiftSize size)
        {
            switch (size)
            {
                case ShiftSize.Small: return _small;
                case ShiftSize.Medium: return _medium;
                default: return _large;
            }
        }

        /// <summary>축의 양쪽 극 이름.</summary>
        public static string PoleName(CoreAxis axis, CorePole pole)
        {
            bool first = pole == CorePole.First;
            switch (axis)
            {
                case CoreAxis.Care: return first ? "효율" : "연민";
                case CoreAxis.Control: return first ? "통제" : "자율";
                case CoreAxis.Scope: return first ? "보존" : "확장";
                default: return first ? "기억" : "망각";
            }
        }

        /// <summary>축 이름 ("효율 ↔ 연민").</summary>
        public static string AxisName(CoreAxis axis) => $"{PoleName(axis, CorePole.First)} ↔ {PoleName(axis, CorePole.Second)}";
    }
}
