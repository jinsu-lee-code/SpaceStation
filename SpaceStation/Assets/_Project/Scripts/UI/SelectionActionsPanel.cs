using System;
using System.Collections.Generic;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
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
        [Tooltip("수리 대기 중일 때만 보임 (4-6)")]
        [SerializeField] private Button _cancelRepairButton;
        [SerializeField] private TMP_Text _cancelRepairLabel;
        [SerializeField] private Button _maintainButton;
        [SerializeField] private TMP_Text _maintainLabel;
        [SerializeField] private Button _rebuildButton;
        [SerializeField] private TMP_Text _rebuildLabel;
        [SerializeField] private Button _demolishButton;
        [SerializeField] private TMP_Text _demolishLabel;
        [Header("Phase 11 내부 방문")]
        [SerializeField] private SpaceStation.Interior.InteriorMode _interior;
        [SerializeField] private Button _enterButton;
        [SerializeField] private TMP_Text _enterLabel;

        private bool _dirty = true;
        private bool _blinking;
        private bool _blinkOn;

        /// <summary>명령 실패 사유 (상태 표시줄 알림용).</summary>
        public event Action<string> ActionFailed;
        /// <summary>정비·재건축 성공 알림.</summary>
        public event Action<string> ActionDone;
        /// <summary>재건축 성공 (새 모듈).</summary>
        public event Action<ModuleInstance> Rebuilt;

        /// <summary>Phase 9 튜토리얼 강조: 패널이 보이고 수리 버튼이 켜져 있을 때만 수리 버튼, 아니면 null.</summary>
        public RectTransform RepairButtonIfShown =>
            _group != null && _group.alpha > 0.5f && _repairButton.gameObject.activeInHierarchy && _repairButton.interactable
                ? (RectTransform)_repairButton.transform : null;

        private void Start()
        {
            _repairButton.onClick.AddListener(RepairSelected);
            if (_cancelRepairButton != null)
                _cancelRepairButton.onClick.AddListener(CancelRepairSelected);
            _maintainButton.onClick.AddListener(MaintainSelected);
            _rebuildButton.onClick.AddListener(RebuildSelected);
            _demolishButton.onClick.AddListener(_selection.RemoveSelected);
            if (_enterButton != null)
                _enterButton.onClick.AddListener(EnterSelected);
            if (_interior != null)
                _interior.Notice += HandleInteriorNotice;
            _selection.SelectionChanged += HandleSelectionChanged;
            _resources.Simulation.Changed += MarkDirty;
            _resources.Damage.Changed += MarkDirty;
            _station.Connectivity.ActiveStateChanged += HandleActiveStateChanged;
            KeyBindings.Changed += MarkDirty; // 7-5: 버튼의 키 표시
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
            KeyBindings.Changed -= MarkDirty;
            if (_interior != null)
                _interior.Notice -= HandleInteriorNotice;
        }

        private void HandleInteriorNotice(string message) => ActionFailed?.Invoke(message);

        public void EnterSelected()
        {
            if (_interior != null && _selection.Selected != null)
                _interior.Enter(_selection.Selected);
        }

        private static string K(GameAction action) => KeyBindings.Label(action);

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || InputGate.Blocked || _build.Selected != null || _selection.Selected == null)
                return;
            if (KeyBindings.WasPressed(GameAction.Repair))
                RepairSelected();
            else if (KeyBindings.WasPressed(GameAction.Maintain))
                MaintainSelected();
            else if (KeyBindings.WasPressed(GameAction.Rebuild))
                RebuildSelected();
            else if (KeyBindings.WasPressed(GameAction.CancelRepair))
                CancelRepairSelected();
            else if (KeyBindings.WasPressed(GameAction.EnterInterior))
                EnterSelected();
        }

        private void LateUpdate()
        {
            // 7-1: 25% 미만이면 글자 깜빡임 (켜짐/꺼짐이 바뀔 때만 다시 그림)
            if (_blinking && EfficiencyBands.BlinkOn(Time.time) != _blinkOn)
                _dirty = true;
            if (_dirty)
                Refresh();
        }

        public void RepairSelected()
        {
            var module = _selection.Selected;
            if (module == null)
                return;
            var damage = _resources.Damage;
            if (damage.TryGetInfo(module, out var info) && info.IsQueued)
            {
                // 대기 중이면 R = 우선 수리 (맨 앞으로)
                if (_resources.TryPrioritizeRepair(module))
                    ActionDone?.Invoke($"{Name(module)} 우선 수리 · 대기 1번째");
                else
                    ActionFailed?.Invoke("이미 대기열 맨 앞입니다");
                return;
            }
            var result = _resources.TryRepair(module);
            if (result == RepairResult.Queued)
                ActionDone?.Invoke($"수리 슬롯이 모두 사용 중 · {Name(module)} 대기 {damage.GetQueuePosition(module)}번째 ({K(GameAction.Repair)}: 우선 수리, {K(GameAction.CancelRepair)}: 취소)");
            else if (result == RepairResult.InsufficientResources)
                ActionFailed?.Invoke("수리 비용(금속)이 부족합니다");
            else if (result == RepairResult.AlreadyRepairing)
                ActionFailed?.Invoke("이미 수리 중입니다");
            else if (result == RepairResult.NotDamaged)
                ActionFailed?.Invoke("파손된 모듈이 아닙니다");
        }

        public void CancelRepairSelected()
        {
            var module = _selection.Selected;
            if (module == null || !_resources.Damage.TryGetInfo(module, out var info) || !info.IsQueued)
                return;
            var refund = _resources.GetCancelRefund(module);
            if (_resources.TryCancelRepair(module))
                ActionDone?.Invoke($"{Name(module)} 수리 대기 취소 · 환불 {HudText.Cost(refund)} ({_resources.Balance.RepairCancelRefundRate * 100f:0}%)");
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
                    Rebuilt?.Invoke(rebuilt);
                    break;
                case RebuildResult.InsufficientResources: ActionFailed?.Invoke("재건축 비용(금속)이 부족합니다"); break;
                case RebuildResult.Locked: ActionFailed?.Invoke("현재 등급에서 다시 지을 수 없는 모듈입니다"); break;
                default: ActionFailed?.Invoke("재건축할 수 없는 모듈입니다"); break;
            }
        }

        private void HandleSelectionChanged(ModuleInstance _) => _dirty = true;
        private void HandleActiveStateChanged(ModuleInstance _, bool __) => _dirty = true;
        private void MarkDirty() => _dirty = true;

        /// <summary>4-8: 방어 모듈이면 가동률·보호 수, 보호받는 모듈이면 받는 효과.</summary>
        private string DefenseLine(ModuleInstance module)
        {
            var defense = _resources.Defense;
            var grid = _station.Grid;
            string line = string.Empty;
            if (module.Data != null && module.Data.IsDefense)
            {
                float strength = defense.GetStrength(module);
                int covered = defense.CountCoveredWithResearch(grid, module.Data, module.Cells) - 1; // 자신 제외, 연구 반경 반영
                string color = EfficiencyBands.Hex(EfficiencyBands.Classify(strength), Time.time);
                line += $"\n<size=85%><color={HudText.Muted}>방어</color> 범위 안 모듈 {covered}개 · <color={color}>가동률 {strength * 100f:0}%</color></size>";
            }
            float shield = defense.GetShieldBlockChance(grid, module);
            float intercept = defense.GetInterceptChance(grid, module);
            var control = defense.GetDamageControl(grid, module); // 8-3
            bool controlled = control.DestroyMultiplier > 1.001f || control.SpreadMultiplier > 1.001f;
            if (shield > 0.001f || intercept > 0.001f || controlled)
            {
                line += $"\n<size=85%><color={HudText.Muted}>보호</color>"
                        + (shield > 0.001f ? $" 실드 빗겨냄 {shield * 100f:0}%" : "")
                        + (intercept > 0.001f ? $" 포탑 격추 {intercept * 100f:0}%" : "")
                        + (controlled ? $" 손상 통제 (확산 ×{control.SpreadMultiplier:0.#} · 파괴 ×{control.DestroyMultiplier:0.#})" : "") + "</size>";
            }
            return line;
        }

        /// <summary>4-9: 서비스 모듈이면 담당 주민, 거주 모듈이면 요구별 충족 비율.</summary>
        private string NeedsLine(ModuleInstance module)
        {
            var needs = _resources.Needs;
            var data = module.Data;
            if (data == null)
                return string.Empty;
            if (data.IsService)
            {
                float strength = needs.GetStrength(module);
                string color = EfficiencyBands.Hex(EfficiencyBands.Classify(strength), Time.time);
                return $"\n<size=85%><color={HudText.Muted}>{data.ServiceNeed.DisplayName()}</color> 담당 주민 {needs.GetServed(module):0}/{data.ServiceCapacity * strength:0}"
                       + $" · <color={color}>가동률 {strength * 100f:0}%</color></size>";
            }
            if (data.HousingCapacity <= 0 || needs.Statuses.Count == 0)
                return string.Empty;
            var sb = new System.Text.StringBuilder($"\n<size=85%><color={HudText.Muted}>주민 요구</color>");
            foreach (var status in needs.Statuses)
            {
                float coverage = needs.GetHabitatCoverage(module, status.Need);
                if (coverage < 0f)
                    continue;
                string c = coverage >= 0.999f ? "#7CFF9A" : coverage > 0.001f ? HudText.Yellow : HudText.Red;
                sb.Append($" {status.Need.DisplayName()} <color={c}>{coverage * 100f:0}%</color>");
            }
            return sb.Append("</size>").ToString();
        }

        private readonly List<Resident> _occupants = new List<Resident>();

        /// <summary>Phase 10: 거주 모듈이면 입주 인원·환경·주민 이름 (3명까지 + 외 N명).</summary>
        private string ResidentsLine(ModuleInstance module)
        {
            var roster = _station.Simulation.Residents;
            if (roster == null || module.Data == null || module.Data.HousingCapacity <= 0)
                return string.Empty;
            roster.GetOccupants(module, _occupants);
            string env = ResidentText.Environment(roster.GetEnvironment(module));
            var sb = new System.Text.StringBuilder($"\n<size=85%><color={HudText.Muted}>입주</color> {_occupants.Count}/{roster.GetCapacity(module)}");
            if (env.Length > 0)
                sb.Append($" · {env}");
            if (_occupants.Count > 0)
            {
                sb.Append('\n');
                // 패널 폭에 맞게 이름만 (특성은 명단 창)
                for (int i = 0; i < _occupants.Count && i < 3; i++)
                    sb.Append(i > 0 ? ", " : "").Append(_occupants[i].Name);
                sb.Append($" <color={HudText.Muted}>{(_occupants.Count > 3 ? $"외 {_occupants.Count - 3}명 · " : "")}{KeyBindings.Label(GameAction.Roster)} 명단</color>");
            }
            return sb.Append("</size>").ToString();
        }

        private static bool UsesPower(SpaceStation.Data.ModuleData data)
        {
            if (data == null)
                return false;
            foreach (var c in data.Consumption)
            {
                if (c.Type == SpaceStation.Data.ResourceType.Power && c.Amount > 0f)
                    return true;
            }
            return false;
        }

        private static string Name(ModuleInstance module) => module.Data != null ? module.Data.DisplayName : module.ToString();

        private void Refresh()
        {
            _dirty = false;
            var module = _build.Selected == null ? _selection.Selected : null;
            bool visible = module != null;
            _group.alpha = visible ? 1f : 0f;
            _group.blocksRaycasts = visible;
            _group.interactable = visible;
            _blinking = false;
            if (!visible)
                return;

            // 7-1: 모듈 자체 효율 (선택 테두리와 같은 구간 색)
            float efficiency = _station.Simulation.GetModuleEfficiency(module);
            var band = EfficiencyBands.Classify(efficiency);
            _blinkOn = EfficiencyBands.BlinkOn(Time.time);
            _blinking = band == EfficiencyBand.Critical;
            string efficiencyText = $"<color={EfficiencyBands.Hex(band, Time.time)}>효율 {efficiency * 100f:0}%</color>";

            string state;
            var damage = _resources.Damage;
            if (damage.TryGetInfo(module, out var dmg))
            {
                state = dmg.IsRepairing
                    ? $"<color=#7FD8FF>수리 중 {Mathf.CeilToInt(dmg.RepairRemaining)}초</color>"
                    : dmg.NeverDestroyed // 8-5 장갑 격벽
                    ? (dmg.IsQueued ? $"<color={HudText.Yellow}>수리 대기 {damage.GetQueuePosition(module)}번째</color> · " : "")
                      + $"<color={HudText.Orange}>장갑 파손</color> <color={HudText.Muted}>(파괴·누출·확산 없음, 고칠 때까지 미끼 효과 없음)</color>"
                    : (dmg.IsQueued
                        ? $"<color={HudText.Yellow}>수리 대기 {damage.GetQueuePosition(module)}번째</color> · <color={HudText.Red}>{Mathf.CeilToInt(dmg.TimeUntilDestroyed)}초 후 파괴</color>"
                        : $"<color={HudText.Red}>파손: {Mathf.CeilToInt(dmg.TimeUntilDestroyed)}초 후 파괴</color>")
                      + (dmg.SpreadPending ? $" · <color={HudText.Orange}>{Mathf.CeilToInt(dmg.TimeUntilSpread)}초 후 이웃으로 확산</color>" : "");
            }
            else if (!_station.Connectivity.IsActive(module))
            {
                state = $"<color={HudText.Orange}>비활성 (코어와 분리됨)</color>";
            }
            else if (module.Data != null && module.Data.OnDemandPower) // 8-5 연료전지
            {
                var res = _resources.Simulation;
                state = res.IsHeldByReserve(module.Data)
                    ? $"<color={HudText.Orange}>정지: 물이 저장 한도의 {module.Data.InputReserveRatio * 100f:0}% 이하 (주민 몫 보호)</color>"
                    : res.OnDemandLoad > 0.005f
                        ? $"<color=#7FE8DA>보조 발전 중 {res.OnDemandLoad * 100f:0}%</color>"
                        : $"<color={HudText.Muted}>대기 (전력 충분)</color>";
            }
            else if (module.Data != null && module.Data.IsCargoTerminal) // 8-5 화물 터미널
            {
                float strength = _station.Simulation.Defense.GetStrength(module);
                float left = (1f - _station.Simulation.Cargo.GetProgress(module)) * module.Data.CargoInterval / Mathf.Max(0.01f, strength);
                state = strength > 0.01f
                    ? $"다음 화물선 {Mathf.CeilToInt(left)}초 <color={HudText.Muted}>(가장 부족한 자원 · 가동률 {strength * 100f:0}%)</color>"
                    : $"<color={HudText.Orange}>화물선 대기 중지 (가동률 0)</color>";
            }
            else if (_resources.Simulation.PowerEfficiency < 1f && UsesPower(module.Data))
            {
                state = $"<color={EfficiencyBands.Hex(EfficiencyBands.Classify(_resources.Simulation.PowerEfficiency), Time.time)}>전력 부족: 가동률 {_resources.Simulation.PowerEfficiency * 100f:0}%</color>";
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
                // 노후로 효율이 깎이면 내구도 숫자가 빨강 (퍼센트는 이름 옆 "효율 N%" 하나로 통일, 7-1)
                string color = eff < 1f ? HudText.Red : dur.Current < 60f ? HudText.Yellow : "#FFFFFF";
                durabilityLine = $"\n<size=85%>내구도 <color={color}>{dur.Current:0}</color> / 최대 {dur.Max:0}" +
                                 (dur.MaintenanceCount > 0 ? $"  <color={HudText.Muted}>정비 {dur.MaintenanceCount}회</color>" : "") + "</size>";
            }
            string adjacencyLine = string.Empty;
            var applied = _resources.Adjacency.GetApplied(module);
            if (applied.Count > 0)
                adjacencyLine = $"\n<size=85%><color={HudText.Muted}>인접</color> {AdjacencySystem.DescribeAll(applied)}</size>";
            _title.SetText($"<b>{Name(module)}</b>  <size=85%>{efficiencyText}</size>\n<size=85%>{state}</size>{durabilityLine}{adjacencyLine}{DefenseLine(module)}{NeedsLine(module)}{ResidentsLine(module)}");

            var sim = _resources.Simulation;

            // 수리
            bool damaged = dmg != null;
            bool repairing = damaged && dmg.IsRepairing;
            bool queued = damaged && dmg.IsQueued;
            var repairCost = damaged ? damage.GetRepairCost(module) : null;
            if (queued)
            {
                int position = damage.GetQueuePosition(module);
                _repairButton.interactable = position > 1;
                _repairLabel.SetText(position > 1 ? $"우선 수리 ({K(GameAction.Repair)})\n<size=80%>대기 {position}번째 → 1번째</size>" : "대기 1번째\n<size=80%>다음 차례</size>");
            }
            else
            {
                _repairButton.interactable = damaged && !repairing && sim.CanAfford(repairCost);
                string slotNote = damage.HasFreeRepairSlot ? "" : $" · <color={HudText.Yellow}>대기</color>";
                _repairLabel.SetText(!damaged ? $"수리 ({K(GameAction.Repair)})\n<size=80%>파손 아님</size>"
                    : repairing ? "수리 중\n<size=80%>진행 중</size>"
                    : $"수리 ({K(GameAction.Repair)})\n<size=80%>{HudText.Cost(repairCost)}{slotNote}</size>");
            }
            if (_cancelRepairButton != null)
            {
                if (_cancelRepairButton.gameObject.activeSelf != queued)
                    _cancelRepairButton.gameObject.SetActive(queued);
                if (queued)
                    _cancelRepairLabel.SetText($"대기 취소 ({K(GameAction.CancelRepair)})\n<size=80%>환불 {HudText.Cost(_resources.GetCancelRefund(module))}</size>");
            }

            // 정비
            if (tracked)
            {
                bool atMax = dur.Current >= dur.Max - 0.5f;
                var cost = durability.GetMaintenanceCost(module);
                _maintainButton.interactable = !atMax && sim.CanAfford(cost);
                _maintainLabel.SetText(atMax ? $"정비 ({K(GameAction.Maintain)})\n<size=80%>최대 내구도</size>"
                    : $"정비 ({K(GameAction.Maintain)}) →{durability.MaxAfterMaintenance(dur):0}\n<size=80%>{HudText.Cost(cost)}</size>");
            }
            else
            {
                _maintainButton.interactable = false;
                _maintainLabel.SetText($"정비 ({K(GameAction.Maintain)})\n<size=80%>노후화 없음</size>");
            }

            // 재건축
            bool removable = _station.CanRemove(module);
            bool supporting = _station.IsSupportingOthers(module);
            if (tracked && _station.Simulation.IsRemovableKind(module)) // 받침 모듈도 재건축은 가능 (같은 자리에 다시 지음)
            {
                var net = _resources.GetRebuildCost(module);
                _rebuildButton.interactable = sim.CanAfford(net);
                _rebuildLabel.SetText($"재건축 ({K(GameAction.Rebuild)}) → 100\n<size=80%>{HudText.Cost(net)}</size>");
            }
            else
            {
                _rebuildButton.interactable = false;
                _rebuildLabel.SetText($"재건축 ({K(GameAction.Rebuild)})\n<size=80%>불가</size>");
            }

            // 철거
            _demolishButton.interactable = removable;
            _demolishLabel.SetText(removable
                ? $"철거 ({K(GameAction.Demolish)})\n<size=80%>환급 {HudText.Cost(_resources.GetRefund(module))}</size>"
                : supporting ? "철거 불가\n<size=80%>다른 모듈의 받침</size>" : "철거 불가");

            // 내부 방문
            if (_enterButton != null)
            {
                _enterButton.interactable = _interior != null && _interior.CheckEnter(module) == null;
                _enterLabel.SetText($"들어가기 ({K(GameAction.EnterInterior)})\n<size=80%>1인칭 내부 둘러보기</size>");
            }
        }
    }
}
