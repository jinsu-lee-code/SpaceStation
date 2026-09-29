using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;

namespace SpaceStation.UI
{
    /// <summary>
    /// 하단 상태 표시줄.
    /// 안내 줄: 현재 모드(대기/배치/선택) 조작 안내와 배치 불가 사유 — 상태가 바뀔 때만 갱신.
    /// 알림 줄: 철거·분리·고갈 등 일시 메시지.
    /// </summary>
    public sealed class StatusBar : MonoBehaviour
    {
        [SerializeField] private BuildController _build;
        [SerializeField] private ModuleSelectionController _selection;
        [SerializeField] private StationController _station;
        [SerializeField] private ResourceController _resources;
        [SerializeField] private EventEffectController _eventEffects;
        [SerializeField] private SelectionActionsPanel _selectionActions;
        [SerializeField] private ProgressionController _progression;
        [SerializeField] private TMP_Text _hintText;
        [SerializeField] private TMP_Text _messageText;
        [SerializeField] private float _messageSeconds = 3f;
        [SerializeField] private string _idleHint = "숫자키 1~7 또는 아래 메뉴로 모듈 선택  ·  모듈 클릭: 선택  ·  휠 드래그: 카메라 회전";

        private float _messageUntil;
        private int _disconnectedThisFrame;

        // 안내 줄 캐시 키
        private ModuleData _shownBuild;
        private ModuleInstance _shownSelection;
        private bool _shownSelectionActive;
        private bool _shownHasTarget;
        private PlacementResult _shownResult;
        private int _shownDamageState;
        private bool _hintInitialized;

        private void Start()
        {
            _selection.Removed += HandleRemoved;
            _selection.RemoveRejected += HandleRemoveRejected;
            _station.Connectivity.ActiveStateChanged += HandleActiveStateChanged;
            _resources.Simulation.DepletionChanged += HandleDepletionChanged;
            _resources.Population.PopulationChanged += HandlePopulationChanged;
            _resources.Damage.Destroyed += HandleDestroyed;
            _resources.Damage.Repaired += HandleRepaired;
            if (_eventEffects != null)
                _eventEffects.Reported += HandleEffectReported;
            if (_selectionActions != null)
                _selectionActions.RepairFailed += HandleRepairFailed;
            if (_progression != null)
                _progression.Progression.GradeChanged += HandleGradeChanged;
            _messageText.SetText(string.Empty);
        }

        private void OnDestroy()
        {
            if (_selection != null)
            {
                _selection.Removed -= HandleRemoved;
                _selection.RemoveRejected -= HandleRemoveRejected;
            }
            if (_station != null && _station.Connectivity != null)
                _station.Connectivity.ActiveStateChanged -= HandleActiveStateChanged;
            if (_resources != null && _resources.Simulation != null)
                _resources.Simulation.DepletionChanged -= HandleDepletionChanged;
            if (_resources != null && _resources.Population != null)
                _resources.Population.PopulationChanged -= HandlePopulationChanged;
            if (_resources != null && _resources.Damage != null)
            {
                _resources.Damage.Destroyed -= HandleDestroyed;
                _resources.Damage.Repaired -= HandleRepaired;
            }
            if (_eventEffects != null)
                _eventEffects.Reported -= HandleEffectReported;
            if (_selectionActions != null)
                _selectionActions.RepairFailed -= HandleRepairFailed;
            if (_progression != null && _progression.Progression != null)
                _progression.Progression.GradeChanged -= HandleGradeChanged;
        }

        private void Update()
        {
            UpdateHint();
            if (_messageUntil > 0f && Time.unscaledTime >= _messageUntil)
            {
                _messageUntil = 0f;
                _messageText.SetText(string.Empty);
            }
        }

        private void LateUpdate()
        {
            _disconnectedThisFrame = 0;
        }

        private void UpdateHint()
        {
            var build = _build.Selected;
            var selected = build == null ? _selection.Selected : null;
            bool selectedActive = selected != null && _station.Connectivity.IsActive(selected);
            bool hasTarget = build != null && _build.HasTarget;
            var result = hasTarget ? _build.TargetResult : PlacementResult.Valid;
            // 0 정상, 1 파손, 2 수리 중
            int damageState = selected != null && _resources.Damage.TryGetInfo(selected, out var info) ? (info.IsRepairing ? 2 : 1) : 0;

            if (_hintInitialized && build == _shownBuild && selected == _shownSelection && selectedActive == _shownSelectionActive
                && hasTarget == _shownHasTarget && result == _shownResult && damageState == _shownDamageState)
                return;

            _hintInitialized = true;
            _shownBuild = build;
            _shownSelection = selected;
            _shownSelectionActive = selectedActive;
            _shownHasTarget = hasTarget;
            _shownResult = result;
            _shownDamageState = damageState;

            if (build != null)
            {
                string text = $"<b>{build.DisplayName}</b> 배치  ·  좌클릭: 배치  ·  R: 회전  ·  우클릭/ESC: 취소";
                if (hasTarget && result != PlacementResult.Valid)
                    text += $"\n<color={HudText.Red}>배치 불가: {HudText.PlacementReason(result)}</color>";
                _hintText.SetText(text);
            }
            else if (selected != null)
            {
                string name = selected.Data != null ? selected.Data.DisplayName : selected.ToString();
                string state = selectedActive ? string.Empty : $"  <color={HudText.Orange}>(비활성: 코어와 분리됨)</color>";
                string action = _station.CanRemove(selected)
                    ? $"Delete/X: 철거 (환급 {HudText.Cost(selected.Data != null ? selected.Data.BuildCost : null, _resources.Balance.DemolishRefundRate)})"
                    : "철거 불가";
                string repair = damageState == 1 ? $"  ·  <color={HudText.Red}>R: 수리</color>" : string.Empty;
                _hintText.SetText($"선택: <b>{name}</b>{state}{repair}  ·  {action}  ·  ESC: 선택 해제");
            }
            else
            {
                _hintText.SetText(_idleHint);
            }
        }

        private void ShowMessage(string message)
        {
            _messageText.SetText(message);
            _messageUntil = Time.unscaledTime + _messageSeconds;
        }

        private void HandleGradeChanged(int previous, int current)
        {
            var p = _progression.Progression;
            var grade = p.GetGrade(current);
            if (current > previous)
            {
                var sb = new System.Text.StringBuilder();
                sb.Append($"<color=#7CFF9A><b>등급 상승: {grade.DisplayName}!</b>");
                // 이전 등급 초과 ~ 현재 등급까지의 해금 목록
                for (int i = previous + 1; i <= current; i++)
                {
                    foreach (var m in p.GetGrade(i).Unlocks)
                        sb.Append($"  ·  {m.DisplayName} 해금");
                }
                if (p.LimitedModule != null)
                    sb.Append($"  ·  {p.LimitedModule.DisplayName} 최대 {grade.MaxLimitedModules}개");
                sb.Append("</color>");
                ShowMessage(sb.ToString());
            }
            else
            {
                ShowMessage($"<color={HudText.Orange}>등급 하락: {grade.DisplayName} (조건 미달)</color>");
            }
        }

        private void HandleEffectReported(string message, bool positive)
        {
            ShowMessage($"<color={(positive ? "#7CFF9A" : HudText.Orange)}>{message}</color>");
        }

        private void HandleDestroyed(ModuleInstance module)
        {
            string name = module.Data != null ? module.Data.DisplayName : module.ToString();
            string message = $"<color={HudText.Red}>{name} 파괴됨 (수리하지 않고 방치)</color>";
            if (_disconnectedThisFrame > 0)
                message += $"\n<color={HudText.Orange}>모듈 {_disconnectedThisFrame}개가 코어와 분리되어 비활성화됨</color>";
            ShowMessage(message);
        }

        private void HandleRepaired(ModuleInstance module)
        {
            string name = module.Data != null ? module.Data.DisplayName : module.ToString();
            ShowMessage($"<color=#7FD8FF>{name} 수리 완료</color>");
        }

        private void HandleRepairFailed(ModuleInstance module, RepairResult result)
        {
            string reason = result == RepairResult.InsufficientResources ? "수리 비용(금속)이 부족합니다"
                : result == RepairResult.AlreadyRepairing ? "이미 수리 중입니다"
                : "파손된 모듈이 아닙니다";
            ShowMessage($"<color={HudText.Red}>{reason}</color>");
        }

        private void HandleActiveStateChanged(ModuleInstance module, bool active)
        {
            // 철거 직후 같은 프레임에 비활성이 된 모듈 수 (Removed보다 먼저 발생)
            if (!active)
                _disconnectedThisFrame++;
        }

        private void HandleRemoved(ModuleInstance module)
        {
            string name = module.Data != null ? module.Data.DisplayName : module.ToString();
            string refund = HudText.Cost(module.Data != null ? module.Data.BuildCost : null, _resources.Balance.DemolishRefundRate);
            string message = $"{name} 철거  ·  환급 {refund}";
            if (_disconnectedThisFrame > 0)
                message += $"\n<color={HudText.Orange}>모듈 {_disconnectedThisFrame}개가 코어와 분리되어 비활성화됨</color>";
            ShowMessage(message);
        }

        private void HandleRemoveRejected(ModuleInstance module)
        {
            bool isCore = module == _station.Core;
            ShowMessage($"<color={HudText.Red}>{(isCore ? "코어는 철거할 수 없습니다" : "철거할 수 없는 모듈입니다")}</color>");
        }

        private void HandlePopulationChanged(int delta, PopulationChangeReason reason)
        {
            if (delta >= 0)
                return; // 증가는 패널 진행도로 충분 (알림 과다 방지)
            string cause;
            switch (reason)
            {
                case PopulationChangeReason.OxygenDepleted: cause = "산소 고갈"; break;
                case PopulationChangeReason.LowSatisfaction: cause = "만족도 낮음"; break;
                case PopulationChangeReason.Overcrowded: cause = "수용 인구 초과"; break;
                default: cause = reason.ToString(); break;
            }
            ShowMessage($"<color={HudText.Red}>주민 {-delta}명 감소 ({cause})</color>");
        }

        private void HandleDepletionChanged(ResourceType type, bool depleted)
        {
            if (depleted && type != ResourceType.Metal)
                ShowMessage($"<color={HudText.Red}>{HudText.ResourceName(type)} 고갈!</color>");
        }
    }
}
