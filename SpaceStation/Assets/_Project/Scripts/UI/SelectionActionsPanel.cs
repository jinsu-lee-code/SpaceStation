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
    /// 모듈 선택 시 좌측 하단 패널: 이름·상태·내구도 + [수리 R] [정비 M] [재건축 B] [철거 Del].
    /// 키 입력도 여기서 처리한다 (배치 모드에서는 R이 회전이므로 선택 모드에서만). 철거 키는 ModuleSelectionController.
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
        [SerializeField] private Button _maintainButton;
        [SerializeField] private TMP_Text _maintainLabel;
        [SerializeField] private Button _rebuildButton;
        [SerializeField] private TMP_Text _rebuildLabel;
        [SerializeField] private Button _demolishButton;
        [SerializeField] private TMP_Text _demolishLabel;

        private bool _dirty = true;

        /// <summary>명령 실패 사유 (상태 표시줄 알림용).</summary>
        public event Action<string> ActionFailed;
        /// <summary>정비·재건축 성공 알림.</summary>
        public event Action<string> ActionDone;

        private void Start()
        {
            _repairButton.onClick.AddListener(RepairSelected);
            _maintainButton.onClick.AddListener(MaintainSelected);
            _rebuildButton.onClick.AddListener(RebuildSelected);
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
            if (keyboard == null || _build.Selected != null || _selection.Selected == null)
                return;
            if (keyboard.rKey.wasPressedThisFrame)
                RepairSelected();
            else if (keyboard.mKey.wasPressedThisFrame)
                MaintainSelected();
            else if (keyboard.bKey.wasPressedThisFrame)
                RebuildSelected();
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
            if (result == RepairResult.InsufficientResources)
                ActionFailed?.Invoke("수리 비용(금속)이 부족합니다");
            else if (result == RepairResult.AlreadyRepairing)
                ActionFailed?.Invoke("이미 수리 중입니다");
            else if (result == RepairResult.NotDamaged)
                ActionFailed?.Invoke("파손된 모듈이 아닙니다");
        }

        public void MaintainSelected()
        {
            var module = _selection.Selected;
            if (module == null)
                return;
            switch (_resources.TryMaintain(module))
            {
                case MaintainResult.Done:
                    _resources.Durability.TryGetInfo(module, out var info);
                    ActionDone?.Invoke($"{Name(module)} 정비 완료 · 내구도 {info.Current:0} (최대 {info.Max:0})");
                    break;
                case MaintainResult.InsufficientResources: ActionFailed?.Invoke("정비 비용(금속)이 부족합니다"); break;
                case MaintainResult.AlreadyAtMax: ActionFailed?.Invoke("이미 최대 내구도입니다"); break;
                default: ActionFailed?.Invoke("정비할 수 없는 모듈입니다"); break;
            }
            _dirty = true;
        }

        public void RebuildSelected()
        {
            var module = _selection.Selected;
            if (module == null)
                return;
            string name = Name(module);
            switch (_resources.TryRebuild(module, out var rebuilt))
            {
                case RebuildResult.Done:
                    _selection.Select(rebuilt);
                    ActionDone?.Invoke($"{name} 재건축 완료 · 내구도 100");
                    break;
                case RebuildResult.InsufficientResources: ActionFailed?.Invoke("재건축 비용(금속)이 부족합니다"); break;
                case RebuildResult.Locked: ActionFailed?.Invoke("현재 등급에서 다시 지을 수 없는 모듈입니다"); break;
                default: ActionFailed?.Invoke("재건축할 수 없는 모듈입니다"); break;
            }
        }

        private void HandleSelectionChanged(ModuleInstance _) => _dirty = true;
        private void HandleActiveStateChanged(ModuleInstance _, bool __) => _dirty = true;
        private void MarkDirty() => _dirty = true;

        private static string Name(ModuleInstance module) => module.Data != null ? module.Data.DisplayName : module.ToString();

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

            string state;
            var damage = _resources.Damage;
            if (damage.TryGetInfo(module, out var dmg))
            {
                state = dmg.IsRepairing
                    ? $"<color=#7FD8FF>수리 중 {Mathf.CeilToInt(dmg.RepairRemaining)}초</color>"
                    : $"<color={HudText.Red}>파손: {Mathf.CeilToInt(dmg.TimeUntilDestroyed)}초 후 파괴</color>";
            }
            else if (!_station.Connectivity.IsActive(module))
            {
                state = $"<color={HudText.Orange}>비활성 (코어와 분리됨)</color>";
            }
            else
            {
                state = "정상";
            }

            string durabilityLine = string.Empty;
            var durability = _resources.Durability;
            bool tracked = durability.TryGetInfo(module, out var dur);
            if (tracked)
            {
                float eff = durability.EfficiencyFor(dur.Current);
                string color = eff < 1f ? HudText.Red : dur.Current < 60f ? HudText.Yellow : "#FFFFFF";
                durabilityLine = $"\n<size=85%>내구도 <color={color}>{dur.Current:0}</color> / 최대 {dur.Max:0}" +
                                 (eff < 1f ? $"  <color={HudText.Red}>효율 {eff * 100f:0}%</color>" : "") +
                                 (dur.MaintenanceCount > 0 ? $"  <color={HudText.Muted}>정비 {dur.MaintenanceCount}회</color>" : "") + "</size>";
            }
            _title.SetText($"<b>{Name(module)}</b>\n<size=85%>{state}</size>{durabilityLine}");

            var sim = _resources.Simulation;

            // 수리
            bool damaged = dmg != null;
            bool repairing = damaged && dmg.IsRepairing;
            var repairCost = damaged ? damage.GetRepairCost(module) : null;
            _repairButton.interactable = damaged && !repairing && sim.CanAfford(repairCost);
            _repairLabel.SetText(!damaged ? "수리 (R)\n<size=80%>파손 아님</size>"
                : repairing ? "수리 중\n<size=80%>진행 중</size>"
                : $"수리 (R)\n<size=80%>{HudText.Cost(repairCost)}</size>");

            // 정비
            if (tracked)
            {
                bool atMax = dur.Current >= dur.Max - 0.5f;
                var cost = durability.GetMaintenanceCost(module);
                _maintainButton.interactable = !atMax && sim.CanAfford(cost);
                _maintainLabel.SetText(atMax ? "정비 (M)\n<size=80%>최대 내구도</size>"
                    : $"정비 (M) → {durability.MaxAfterMaintenance(dur):0}\n<size=80%>{HudText.Cost(cost)}</size>");
            }
            else
            {
                _maintainButton.interactable = false;
                _maintainLabel.SetText("정비 (M)\n<size=80%>노후화 없음</size>");
            }

            // 재건축
            bool removable = _station.CanRemove(module);
            if (tracked && removable)
            {
                var net = _resources.GetRebuildCost(module);
                _rebuildButton.interactable = sim.CanAfford(net);
                _rebuildLabel.SetText($"재건축 (B) → 100\n<size=80%>{HudText.Cost(net)}</size>");
            }
            else
            {
                _rebuildButton.interactable = false;
                _rebuildLabel.SetText("재건축 (B)\n<size=80%>불가</size>");
            }

            // 철거
            _demolishButton.interactable = removable;
            _demolishLabel.SetText(removable
                ? $"철거 (Del)\n<size=80%>환급 {HudText.Cost(_resources.GetRefund(module))}</size>"
                : "철거 불가");
        }
    }
}
