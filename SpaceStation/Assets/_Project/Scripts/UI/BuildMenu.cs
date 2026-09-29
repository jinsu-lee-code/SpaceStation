using System.Collections.Generic;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEngine;

namespace SpaceStation.UI
{
    /// <summary>
    /// 하단 건설 메뉴. BuildController의 건설 목록 순서대로 버튼을 만든다 (숫자키와 동일한 순서).
    /// 버튼 상태: 등급 잠김 / 최대 설치 수 / 비용 부족이면 비활성.
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

        private readonly List<BuildButtonView> _buttons = new List<BuildButtonView>();
        private readonly List<string> _ruleLines = new List<string>();
        private bool _stateDirty = true;

        private void Start()
        {
            var modules = _build.BuildableModules;
            for (int i = 0; i < modules.Count; i++)
            {
                var button = Instantiate(_buttonPrefab, _container);
                button.Initialize(this, modules[i], i + 1);
                _buttons.Add(button);
            }

            _build.SelectionChanged += HandleSelectionChanged;
            _resources.Simulation.Changed += MarkDirty;
            if (_progression != null)
                _progression.Changed += MarkDirty;
            HandleSelectionChanged(_build.Selected);
        }

        private void OnDestroy()
        {
            if (_build != null)
                _build.SelectionChanged -= HandleSelectionChanged;
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
            var sim = _resources.Simulation;
            foreach (var button in _buttons)
            {
                var buildable = _station.CheckBuildable(button.Data);
                bool affordable = sim.CanAfford(button.Data.BuildCost);
                button.SetState(buildable == PlacementResult.Valid && affordable, BlockedStatus(button.Data, buildable));
            }
        }

        public void HandleButtonClicked(ModuleData data)
        {
            _build.ToggleSelect(data);
        }

        public void ShowTooltip(ModuleData data, RectTransform anchor)
        {
            if (_tooltip == null)
                return;
            string text = HudText.ModuleTooltip(data);
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
                    return $"<color={HudText.Yellow}>최대 {p.Current.MaxLimitedModules}개</color>";
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
                return $"{p.Current.DisplayName} 등급 최대 {p.Current.MaxLimitedModules}개 (등급을 올리면 증가)";
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
