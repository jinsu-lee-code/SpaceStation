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
        private int _revealed = -1;
        private float _nextRevealCheck;

        private void Start()
        {
            // 모든 모듈 · 분류의 버튼 · 탭을 한 번 만들고, 보이는 것 · 순서 · 번호는 Relayout이 정함 (13-6a)
            var all = new List<ModuleCategory>();
            BuildCategories.GetAvailable(_build.BuildableModules, all);
            foreach (var category in all)
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
            _build.ModulesChanged += Relayout;
            _build.RefreshModules();
            _revealed = CountRevealed();
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
                _build.ModulesChanged -= Relayout;
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
            if (Time.unscaledTime >= _nextRevealCheck)
            {
                _nextRevealCheck = Time.unscaledTime + 0.5f;
                _stateDirty = true; // 일시정지 중에 규칙이 열려도(해금 선택 등) 새 모듈이 보이게
            }
            if (!_stateDirty)
                return;
            _stateDirty = false;
            int revealed = CountRevealed();
            if (revealed != _revealed)
            {
                _revealed = revealed;
                _build.RefreshModules(); // 13-6a 새로 열린 모듈 → 탭 · 순서 · 번호 다시
            }
            var station = _station.Simulation;
            foreach (var button in _buttons)
            {
                if (!button.gameObject.activeSelf)
                    continue;
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

        private void HandleCategoryChanged(ModuleCategory category) => Relayout();

        /// <summary>
        /// 13-6a: 탭 = 열린 모듈이 있는 분류만 (수 = 열린 모듈 수), 버튼 = 지금 탭의 열린 모듈만 · 등급 → 가격 순 · 숫자키도 그 순서.
        /// </summary>
        private void Relayout()
        {
            var category = _build.Category;
            foreach (var tab in _tabs)
            {
                BuildCategories.Filter(_build.BuildableModules, tab.Category, _scratch, _build.Revealed);
                tab.gameObject.SetActive(_scratch.Count > 0);
                tab.SetCount(_scratch.Count);
                tab.SetSelected(tab.Category == category);
            }
            if (_tabHint != null)
                _tabHint.SetAsLastSibling();
            var list = _build.CategoryModules;
            foreach (var button in _buttons)
                button.gameObject.SetActive(false);
            for (int i = 0; i < list.Count; i++) // 목록 순서대로 앞에서부터 놓아야 서로 밀리지 않음
            {
                foreach (var button in _buttons)
                {
                    if (button.Data != list[i])
                        continue;
                    button.gameObject.SetActive(true);
                    button.transform.SetSiblingIndex(i);
                    button.SetHotkey(i + 1);
                    break;
                }
            }
            _stateDirty = true;
            HideTooltip(); // 숨겨진 버튼의 툴팁이 남지 않도록
        }

        private int CountRevealed()
        {
            int n = 0;
            foreach (var m in _build.BuildableModules)
                if (m != null && _build.Revealed(m))
                    n++;
            return n;
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
                    if (data.ComingSoon)
                        return $"<color={HudText.Muted}>준비 중</color>"; // 12-5
                    int grade = p.GetUnlockGrade(data);
                    if (grade >= 0 && grade <= p.GradeIndex && p.IsRuleLocked(data))
                        return $"<color={HudText.Muted}>잠김 · {StationRules.DisplayName(data.RequiredRule)}</color>"; // 13-1
                    return grade >= 0 ? $"<color={HudText.Muted}>잠김 · {p.GetGrade(grade).DisplayName}</color>" : $"<color={HudText.Muted}>잠김</color>";
                case PlacementResult.LimitReached:
                    return $"<color={HudText.Yellow}>최대 {p.CurrentLimit(_station.Grid)}개</color>";
                case PlacementResult.TutorialLocked:
                    return $"<color={HudText.Muted}>튜토리얼 후</color>";
                default:
                    return null;
            }
        }

        private string BlockedDetail(ModuleData data, PlacementResult buildable)
        {
            var p = _progression.Progression;
            if (buildable == PlacementResult.ModuleLocked)
            {
                if (data.ComingSoon)
                    return string.IsNullOrEmpty(data.ComingSoonNote) ? "준비 중 (기능을 만드는 중)" : $"준비 중 · {data.ComingSoonNote}";
                int grade = p.GetUnlockGrade(data);
                if (grade >= 0 && grade <= p.GradeIndex && p.IsRuleLocked(data))
                    return $"'{StationRules.DisplayName(data.RequiredRule)}' 시스템이 열리면 건설 가능"; // 13-1
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
