using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>13-1 타이쿤판 해금 선택 후보 하나 = 위기 + 대응 도구 묶음 (TYCOON.md K2-2).</summary>
    [Serializable]
    public sealed class UnlockOption
    {
        [Tooltip("세이브 · 식별용 (바꾸면 이전 세이브의 선택 기록을 못 읽음)")]
        [SerializeField] private string _key = "";
        [SerializeField] private string _displayName = "";
        [Tooltip("선택 카드 설명 (여러 줄 가능)")]
        [SerializeField, TextArea(2, 5)] private string _description = "";
        [Tooltip("고르면 열리는 규칙 (여러 개 가능)")]
        [SerializeField] private StationRule _rules;

        public string Key => _key;
        public string DisplayName => _displayName;
        public string Description => _description;
        public StationRule Rules => _rules;

        public UnlockOption() { }

        public UnlockOption(string key, string displayName, string description, StationRule rules)
        {
            _key = key;
            _displayName = displayName;
            _description = description;
            _rules = rules;
        }
    }

    /// <summary>13-1: 등급 하나에서 자동으로 열리는 규칙 + 새 후보.</summary>
    [Serializable]
    public sealed class UnlockGradeEntry
    {
        [Tooltip("등급 인덱스 (0 = 등급 없음, 1 = 소형 … 7 = 지구)")]
        [SerializeField, Min(0)] private int _grade;
        [Tooltip("이 등급에 처음 도달하면 고르지 않아도 열리는 규칙 (예: 소형 = 정책)")]
        [SerializeField] private StationRule _autoRules;
        [Tooltip("이 등급의 새 후보 (직전 등급에서 고르지 않은 쪽이 함께 후보가 됨)")]
        [SerializeField] private List<UnlockOption> _options = new List<UnlockOption>();

        public int Grade => _grade;
        public StationRule AutoRules => _autoRules;
        public IReadOnlyList<UnlockOption> Options => _options;

        public UnlockGradeEntry() { }

        public UnlockGradeEntry(int grade, StationRule autoRules, params UnlockOption[] options)
        {
            _grade = grade;
            _autoRules = autoRules;
            _options = new List<UnlockOption>(options);
        }
    }

    /// <summary>
    /// 13-1 타이쿤판 해금 선택 (TYCOON.md K2, BALANCE 41번). 메뉴 SpaceStation/Tycoon/Setup Unlock Choices가 만든다.
    /// 등급에 처음 도달하면 후보(그 등급 새 후보 + 직전에 남은 쪽) 중 하나를 고른다.
    /// 남은 새 후보는 다음 등급 후보로 넘어가고, 넘어온 후보를 또 안 고르면 다음 등급에 자동으로 열린다 (사용자 결정 2026-10-15).
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/Unlock Choice Config", fileName = "UnlockChoiceConfig")]
    public sealed class UnlockChoiceConfig : ScriptableObject
    {
        [SerializeField] private List<UnlockGradeEntry> _grades = new List<UnlockGradeEntry>();

        public IReadOnlyList<UnlockGradeEntry> Grades => _grades;

        public UnlockGradeEntry ForGrade(int grade)
        {
            foreach (var g in _grades)
                if (g != null && g.Grade == grade)
                    return g;
            return null;
        }

        public UnlockOption Find(string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;
            foreach (var g in _grades)
                if (g != null)
                    foreach (var o in g.Options)
                        if (o != null && o.Key == key)
                            return o;
            return null;
        }

        /// <summary>에디터 설정 메뉴 · 시험용.</summary>
        public void SetGrades(List<UnlockGradeEntry> grades) => _grades = grades;
    }
}
