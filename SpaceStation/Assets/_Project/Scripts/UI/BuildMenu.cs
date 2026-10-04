using System.Collections.Generic;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEngine;

namespace SpaceStation.UI
{
    /// <summary>
    /// 하단 건설 메뉴. 위쪽 탭 줄(4-5, Tab/Shift+Tab)에서 고른 분류의 모듈만 버튼으로 보인다.
    /// 버튼 번호 = 탭 안의 숫자키. 버튼 상태: 등급 잠김 / 최대 설치 수 / 비용 부족이면 비활성.
    /// </summary>
    public sealed class BuildMenu : MonoBehaviour
    {
        [SerializeField] private BuildController _build;
        [SerializeField] private ResourceController _resources;
        [SerializeField] private StationController _station;
        [SerializeField] private ProgressionController _progression;
        [SerializeField] private BuildButtonView _buttonPrefab;
        [SerializeField] private RectTransform _container;
        [SerializeField] private TooltipView _tooltip;
        [Header("Tabs (4-5)")]
        [SerializeField] private BuildTabView _tabPrefab;
        [SerializeField] private RectTransform _tabContainer;
        [SerializeField] private RectTransform _tabHint;

        private readonly List<BuildButtonView> _buttons = new List<BuildButtonView>();
        private readonly List<BuildTabView> _tabs = new List<BuildTabView>();
        private readonly List<ModuleData> _scratch = new List<ModuleData>();
        private readonly List<string> _ruleLines = new List<string>();
        private bool _stateDirty = true;

        private void Start()
        {
            // 탭 순서대로 버튼 생성, 번호는 탭 안의 순서
            foreach (var category in _build.Categories)
            {
                BuildCategories.Filter(_build.BuildableModules, category, _scratch);
                for (int i = 0; i < _scratch.Count; i++)
                {
                    var button = Instantiate(_buttonPrefab, _container);
                    button.Initialize(this, _scratch[i], i + 1);
                    _buttons.Add(button);
                }
                if (_tabPrefab != null && _tabContainer != null)
                {
                    var tab = Instantiate(_tabPrefab, _tabContainer);
                    tab.Initialize(this, category, _scratch.Count);
                    _tabs.Add(tab);
                }
            }
            if (_tabHint != null)
            {
                _tabHint.SetAsLastSibling(); // "Tab 전환" 안내를 탭 오른쪽 끝으로
                RefreshTabHint();
                KeyBindings.Changed += RefreshTabHint; // 7-5: 바꾼 키 표시
            }

            _build.CategoryChanged += HandleCategoryChanged;
            HandleCategoryChanged(_build.Category);
            _build.SelectionChanged += HandleSelectionChanged;
            _resources.Simulation.Changed += MarkDirty;
            if (_progression != null)
                _progression.Changed += MarkDirty;
            HandleSelectionChanged(_build.Selected);
        }

        private void RefreshTabHint()
        {
            var text = _tabHint != null ? _tabHint.GetComponent<TMPro.TMP_Text>() : null;
            if (text != null)
            {
                string key = KeyBindings.Label(GameAction.NextCategory);
                text.SetText($"<color=#9AA3B2>{key} / Shift+{key}</color>");
            }
        }

        private void OnDestroy()
        {
            KeyBindings.Changed -= RefreshTabHint;
            if (_build != null)
            {
                _build.SelectionChanged -= HandleSelectionChanged;
                _build.CategoryChanged -= HandleCategoryChanged;
            }
            if (_resources != null && _resources.Simulation != null)
                _resources.Simulation.Changed -= MarkDirty;
            if (_progression != null)
                _progression.Changed -= MarkDirty;
        }

        private void LateUpdate()
        {
            if (!_stateDirty)
                return;
            _stateDirty = false;
            var station = _station.Simulation;
            foreach (var button in _buttons)
            {
                var buildable = _station.CheckBuildable(button.Data);
                bool affordable = station.CanAfford(button.Data); // 건설·경제 연구 할인 반영
                button.SetState(buildable == PlacementResult.Valid && affordable, BlockedStatus(button.Data, buildable), station.GetBuildCost(button.Data));
            }
        }

        /// <summary>Phase 9 튜토리얼 강조: 모듈 버튼 (없으면 null).</summary>
        public RectTransform FindButton(ModuleData data)
        {
            foreach (var button in _buttons)
                if (button.Data == data)
                    return (RectTransform)button.transform;
            return null;
        }

        /// <summary>Phase 9 튜토리얼 강조: 분류 탭 (없으면 null).</summary>
        public RectTransform FindTab(ModuleCategory category)
        {
            foreach (var tab in _tabs)
                if (tab.Category == category)
                    return (RectTransform)tab.transform;
            return null;
        }

        public void HandleButtonClicked(ModuleData data)
        {
            _build.ToggleSelect(data);
        }

        public void HandleTabClicked(ModuleCategory category)
        {
            _build.SetCategory(category);
        }

        private void HandleCategoryChanged(ModuleCategory category)
        {
            foreach (var tab in _tabs)
                tab.SetSelected(tab.Category == category);
            foreach (var button in _buttons)
                button.gameObject.SetActive(button.Data.Category == category);
            HideTooltip(); // 숨겨진 버튼의 툴팁이 남지 않도록
        }

        public void ShowTooltip(ModuleData data, RectTransform anchor)
        {
            if (_tooltip == null)
                return;
            string text = HudText.ModuleTooltip(data, _station.Simulation.Effects);
            _station.Simulation.Adjacency.DescribeRulesFor(data, _ruleLines);
            if (_ruleLines.Count > 0)
                text += $"\n<color={HudText.Muted}>인접 효과</color>\n" + string.Join("\n", _ruleLines);
            var buildable = _station.CheckBuildable(data);
            if (buildable != PlacementResult.Valid)
                text += $"\n<color={HudText.Red}>{BlockedDetail(data, buildable)}</color>";
            _tooltip.Show(text, anchor);
        }

        public void HideTooltip()
        {
            if (_tooltip != null)
                _tooltip.Hide();
        }

        private string BlockedStatus(ModuleData data, PlacementResult buildable)
        {
            if (_progression == null)
                return null;
            var p = _progression.Progression;
            switch (buildable)
            {
                case PlacementResult.ModuleLocked:
                    int grade = p.GetUnlockGrade(data);
                    return grade >= 0 ? $"<color={HudText.Muted}>잠김 · {p.GetGrade(grade).DisplayName}</color>" : $"<color={HudText.Muted}>잠김</color>";
                case PlacementResult.LimitReached:
                    return $"<color={HudText.Yellow}>최대 {p.CurrentLimit(_station.Grid)}개</color>";
                default:
                    return null;
            }
        }

        private string BlockedDetail(ModuleData data, PlacementResult buildable)
        {
            var p = _progression.Progression;
            if (buildable == PlacementResult.ModuleLocked)
            {
                int grade = p.GetUnlockGrade(data);
                return grade >= 0 ? $"{p.GetGrade(grade).DisplayName} 등급에서 해금" : "해금 조건 없음";
            }
            if (buildable == PlacementResult.LimitReached)
            {
                int limit = p.CurrentLimit(_station.Grid);
                int until = p.ModulesUntilNextExtra(SpaceStation.Simulation.StationProgression.CountGradeModules(_station.Grid));
                return until > 0
                    ? $"{p.Current.DisplayName} 등급 최대 {limit}개 (모듈 {until}개 더 지으면 +1)"
                    : $"{p.Current.DisplayName} 등급 최대 {limit}개 (등급을 올리면 증가)";
            }
            return HudText.PlacementReason(buildable);
        }

        private void MarkDirty()
        {
            _stateDirty = true;
        }

        private void HandleSelectionChanged(ModuleData selected)
        {
            foreach (var button in _buttons)
                button.SetSelected(button.Data == selected);
        }
    }
}
