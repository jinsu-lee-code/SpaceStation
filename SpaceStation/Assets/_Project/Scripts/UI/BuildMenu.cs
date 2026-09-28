using System.Collections.Generic;
using SpaceStation.Building;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEngine;

namespace SpaceStation.UI
{
    /// <summary>
    /// 하단 건설 메뉴. BuildController의 건설 목록 순서대로 버튼을 만든다 (숫자키와 동일한 순서).
    /// </summary>
    public sealed class BuildMenu : MonoBehaviour
    {
        [SerializeField] private BuildController _build;
        [SerializeField] private ResourceController _resources;
        [SerializeField] private BuildButtonView _buttonPrefab;
        [SerializeField] private RectTransform _container;
        [SerializeField] private TooltipView _tooltip;

        private readonly List<BuildButtonView> _buttons = new List<BuildButtonView>();
        private bool _affordabilityDirty = true;

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
            _resources.Simulation.Changed += MarkAffordabilityDirty;
            HandleSelectionChanged(_build.Selected);
        }

        private void OnDestroy()
        {
            if (_build != null)
                _build.SelectionChanged -= HandleSelectionChanged;
            if (_resources != null && _resources.Simulation != null)
                _resources.Simulation.Changed -= MarkAffordabilityDirty;
        }

        private void LateUpdate()
        {
            if (!_affordabilityDirty)
                return;
            _affordabilityDirty = false;
            var sim = _resources.Simulation;
            foreach (var button in _buttons)
                button.SetAffordable(sim.CanAfford(button.Data.BuildCost));
        }

        public void HandleButtonClicked(ModuleData data)
        {
            _build.ToggleSelect(data);
        }

        public void ShowTooltip(ModuleData data, RectTransform anchor)
        {
            if (_tooltip != null)
                _tooltip.Show(HudText.ModuleTooltip(data), anchor);
        }

        public void HideTooltip()
        {
            if (_tooltip != null)
                _tooltip.Hide();
        }

        private void MarkAffordabilityDirty()
        {
            _affordabilityDirty = true;
        }

        private void HandleSelectionChanged(ModuleData selected)
        {
            foreach (var button in _buttons)
                button.SetSelected(button.Data == selected);
        }
    }
}
