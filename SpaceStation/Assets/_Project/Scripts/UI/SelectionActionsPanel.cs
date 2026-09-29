using System;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// 모듈 선택 시 좌측 하단 패널: 이름·상태 + [수리 (R)] [철거 (Del)] 버튼.
    /// R 키 수리도 여기서 처리한다 (배치 모드에서는 R이 회전이므로 선택 모드에서만).
    /// </summary>
    public sealed class SelectionActionsPanel : MonoBehaviour
    {
        [SerializeField] private ModuleSelectionController _selection;
        [SerializeField] private BuildController _build;
        [SerializeField] private ResourceController _resources;
        [SerializeField] private StationController _station;
        [SerializeField] private CanvasGroup _group;
        [SerializeField] private TMP_Text _title;
        [SerializeField] private Button _repairButton;
        [SerializeField] private TMP_Text _repairLabel;
        [SerializeField] private Button _demolishButton;
        [SerializeField] private TMP_Text _demolishLabel;

        private bool _dirty = true;

        /// <summary>수리 요청이 실패했을 때 (자원 부족 등).</summary>
        public event Action<ModuleInstance, RepairResult> RepairFailed;

        private void Start()
        {
            _repairButton.onClick.AddListener(RepairSelected);
            _demolishButton.onClick.AddListener(_selection.RemoveSelected);
            _selection.SelectionChanged += HandleSelectionChanged;
            _resources.Simulation.Changed += MarkDirty;
            _resources.Damage.Changed += MarkDirty;
            _station.Connectivity.ActiveStateChanged += HandleActiveStateChanged;
            Refresh();
        }

        private void OnDestroy()
        {
            if (_selection != null)
                _selection.SelectionChanged -= HandleSelectionChanged;
            if (_resources != null && _resources.Simulation != null)
            {
                _resources.Simulation.Changed -= MarkDirty;
                _resources.Damage.Changed -= MarkDirty;
            }
            if (_station != null && _station.Connectivity != null)
                _station.Connectivity.ActiveStateChanged -= HandleActiveStateChanged;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame && _build.Selected == null && _selection.Selected != null)
                RepairSelected();
        }

        private void LateUpdate()
        {
            if (_dirty)
                Refresh();
        }

        public void RepairSelected()
        {
            var module = _selection.Selected;
            if (module == null)
                return;
            var result = _resources.TryRepair(module);
            if (result != RepairResult.Started)
                RepairFailed?.Invoke(module, result);
        }

        private void HandleSelectionChanged(ModuleInstance _) => _dirty = true;
        private void HandleActiveStateChanged(ModuleInstance _, bool __) => _dirty = true;
        private void MarkDirty() => _dirty = true;

        private void Refresh()
        {
            _dirty = false;
            var module = _build.Selected == null ? _selection.Selected : null;
            bool visible = module != null;
            _group.alpha = visible ? 1f : 0f;
            _group.blocksRaycasts = visible;
            _group.interactable = visible;
            if (!visible)
                return;

            string name = module.Data != null ? module.Data.DisplayName : module.ToString();
            string state;
            var damage = _resources.Damage;
            if (damage.TryGetInfo(module, out var info))
            {
                state = info.IsRepairing
                    ? $"<color=#7FD8FF>수리 중 {Mathf.CeilToInt(info.RepairRemaining)}초</color>"
                    : $"<color={HudText.Red}>파손: {Mathf.CeilToInt(info.TimeUntilDestroyed)}초 후 파괴</color>";
            }
            else if (!_station.Connectivity.IsActive(module))
            {
                state = $"<color={HudText.Orange}>비활성 (코어와 분리됨)</color>";
            }
            else
            {
                state = "정상";
            }
            _title.SetText($"<b>{name}</b>\n<size=85%>{state}</size>");

            // 수리
            bool damaged = info != null;
            bool repairing = damaged && info.IsRepairing;
            var repairCost = damaged ? damage.GetRepairCost(module) : null;
            bool affordable = damaged && _resources.Simulation.CanAfford(repairCost);
            _repairButton.interactable = damaged && !repairing && affordable;
            if (!damaged)
                _repairLabel.SetText("수리 (R)\n<size=80%>파손 아님</size>");
            else if (repairing)
                _repairLabel.SetText("수리 중\n<size=80%>진행 중</size>");
            else
                _repairLabel.SetText($"수리 (R)\n<size=80%>{HudText.Cost(repairCost)}</size>");

            // 철거
            bool removable = _station.CanRemove(module);
            _demolishButton.interactable = removable;
            _demolishLabel.SetText(removable
                ? $"철거 (Del)\n<size=80%>환급 {HudText.Cost(module.Data != null ? module.Data.BuildCost : null, _resources.Balance.DemolishRefundRate)}</size>"
                : "철거 불가");
        }
    }
}
