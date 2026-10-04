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
        [Tooltip("5-8: 시작 후 이 시간(실시간 초) 동안만 조작 안내를 보여주고, 이후에는 H로 켜고 끈다")]
        [SerializeField] private float _idleHintSeconds = 60f;

        private float _messageUntil;
        private bool _helpPinned;      // H로 켠 상태
        private bool _helpDismissed;   // 시간이 지났거나 첫 건설을 함
        private UiTween _messageTween;
        private int _disconnectedThisFrame;

        // 안내 줄 캐시 키
        private ModuleData _shownBuild;
        private ModuleInstance _shownSelection;
        private bool _shownSelectionActive;
        private bool _shownHasTarget;
        private PlacementResult _shownResult;
        private int _shownDamageState;
        private Vector3Int _shownTargetCell;
        private int _shownRotation;
        private bool _hintInitialized;
        private Settings.HintMode _shownHints;
        private readonly System.Collections.Generic.List<AppliedAdjacency> _previewSelf = new System.Collections.Generic.List<AppliedAdjacency>();
        private readonly System.Collections.Generic.List<string> _previewNeighbors = new System.Collections.Generic.List<string>();

        private void Start()
        {
            _selection.Removed += HandleRemoved;
            _selection.RemoveRejected += HandleRemoveRejected;
            _station.Connectivity.ActiveStateChanged += HandleActiveStateChanged;
            _resources.Simulation.DepletionChanged += HandleDepletionChanged;
            _resources.Population.PopulationChanged += HandlePopulationChanged;
            if (_station.Simulation.Residents != null)
            {
                _station.Simulation.Residents.Arrived += HandleResidentArrived;
                _station.Simulation.Residents.Left += HandleResidentLeft;
            }
            _resources.Damage.Destroyed += HandleDestroyed;
            _resources.Damage.Repaired += HandleRepaired;
            _resources.Durability.WornOut += HandleWornOut;
            if (_eventEffects != null)
                _eventEffects.Reported += HandleEffectReported;
            if (_selectionActions != null)
            {
                _selectionActions.ActionFailed += HandleActionFailed;
                _selectionActions.ActionDone += HandleActionDone;
            }
            if (_progression != null)
                _progression.Progression.GradeChanged += HandleGradeChanged;
            _station.Simulation.Research.Completed += HandleResearchCompleted;
            _station.Simulation.Automation.Performed += HandleAutomationPerformed;
            Save.SaveManager.Notice += HandleSaveNotice;
            KeyBindings.Changed += HandleKeysChanged;
            _station.Grid.ModulePlaced += HandleModulePlaced;
            _station.Grid.ModuleRemoved += HandleModuleRemovedForLimit;
            if (_progression != null)
                _shownLimit = _progression.Progression.CurrentLimit(_station.Grid);
            _messageText.SetText(string.Empty);
            var group = _messageText.GetComponent<CanvasGroup>();
            if (group == null)
                group = _messageText.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            // 레이아웃 그룹 안이라 위치는 그대로 두고 알파만 (offset 0)
            _messageTween = new UiTween(null, group, Vector2.zero, 0.15f, 0.45f);
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
            if (_station != null && _station.Simulation != null && _station.Simulation.Residents != null)
            {
                _station.Simulation.Residents.Arrived -= HandleResidentArrived;
                _station.Simulation.Residents.Left -= HandleResidentLeft;
            }
            if (_resources != null && _resources.Damage != null)
            {
                _resources.Damage.Destroyed -= HandleDestroyed;
                _resources.Damage.Repaired -= HandleRepaired;
                _resources.Durability.WornOut -= HandleWornOut;
            }
            if (_eventEffects != null)
                _eventEffects.Reported -= HandleEffectReported;
            if (_selectionActions != null)
            {
                _selectionActions.ActionFailed -= HandleActionFailed;
                _selectionActions.ActionDone -= HandleActionDone;
            }
            if (_progression != null && _progression.Progression != null)
                _progression.Progression.GradeChanged -= HandleGradeChanged;
            if (_station != null && _station.Simulation != null)
            {
                _station.Simulation.Research.Completed -= HandleResearchCompleted;
                _station.Simulation.Automation.Performed -= HandleAutomationPerformed;
            }
            Save.SaveManager.Notice -= HandleSaveNotice;
            KeyBindings.Changed -= HandleKeysChanged;
            if (_station != null && _station.Grid != null)
            {
                _station.Grid.ModulePlaced -= HandleModulePlaced;
                _station.Grid.ModuleRemoved -= HandleModuleRemovedForLimit;
            }
        }

        /// <summary>철거·등급 변화로 바뀐 한도는 알림 없이 기준값만 갱신 (다시 늘 때만 알림).</summary>
        private void HandleModuleRemovedForLimit(ModuleInstance _) => SyncShownLimit();

        private void SyncShownLimit()
        {
            if (_progression != null)
                _shownLimit = _progression.Progression.CurrentLimit(_station.Grid);
        }

        private int _shownLimit = -1;

        /// <summary>8-0: 최고 등급에서 모듈 수로 채굴 도킹 최대 수가 늘면 알림.</summary>
        private void HandleModulePlaced(ModuleInstance _)
        {
            if (_progression == null)
                return;
            var p = _progression.Progression;
            int limit = p.CurrentLimit(_station.Grid);
            if (_shownLimit >= 0 && limit > _shownLimit && p.IsAtOrAboveVictory && p.LimitedModule != null)
                ShowMessage($"<color={HudTheme.GreenHex}>{p.LimitedModule.DisplayName} 최대 {limit}개로 증가 (모듈 {SpaceStation.Simulation.StationProgression.CountGradeModules(_station.Grid)}개)</color>");
            _shownLimit = limit;
        }

        private void HandleKeysChanged() => _hintInitialized = false; // 7-5: 안내 문구의 키 이름 다시 그림

        private static string K(GameAction action) => KeyBindings.Label(action);

        /// <summary>대기 중 조작 안내 (현재 키 설정 반영).</summary>
        private static string IdleHint =>
            $"{K(GameAction.NextCategory)}: 건설 탭 전환  ·  숫자키 또는 아래 메뉴로 모듈 선택  ·  모듈 클릭: 선택  ·  휠 드래그: 카메라 회전";

        private void HandleSaveNotice(string message, bool warning)
        {
            ShowMessage($"<color={(warning ? HudText.Orange : "#7FD8FF")}>{message}</color>");
        }

        private void HandleResearchCompleted(ResearchCategoryData category, int level)
        {
            var def = category.GetLevel(level);
            string name = category.MaxLevel == 1 ? category.DisplayName : $"{category.DisplayName} Lv.{level}";
            ShowMessage($"<color=#7CFF9A><b>연구 완료: {name}</b>  ·  {def?.Description}</color>");
        }

        private void HandleAutomationPerformed(int maintained, int rebuilt, bool batch)
        {
            var sb = new System.Text.StringBuilder(batch ? "일괄 정비: " : "자동 정비: ");
            if (maintained > 0)
                sb.Append("정비 ").Append(maintained).Append("개");
            if (rebuilt > 0)
                sb.Append(maintained > 0 ? " · " : "").Append("재건축 ").Append(rebuilt).Append("개");
            ShowMessage($"<color=#7FD8FF>{sb}</color>");
        }

        private void Update()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && !InputGate.Blocked && KeyBindings.WasPressed(GameAction.ToggleHelp))
            {
                _helpPinned = !_helpPinned;
                _hintInitialized = false; // 다시 그림
            }
            if (!_helpDismissed && (Time.unscaledTime > _idleHintSeconds || _build.Selected != null))
            {
                _helpDismissed = true;
                _hintInitialized = false;
            }
            if (Settings.GameSettings.Hints != _shownHints)
            {
                _shownHints = Settings.GameSettings.Hints;
                _hintInitialized = false;
            }
            UpdateHint();
            _messageTween?.Update();
            if (_messageUntil > 0f && Time.unscaledTime >= _messageUntil)
            {
                _messageUntil = 0f;
                _messageTween?.Hide();
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
            // 3 수리 대기 (4-6)
            int damageState = selected != null && _resources.Damage.TryGetInfo(selected, out var info) ? (info.IsRepairing ? 2 : info.IsQueued ? 3 : 1) : 0;

            var targetCell = hasTarget ? _build.TargetCell : Vector3Int.zero;
            int rotation = build != null ? _build.Rotation : 0;

            if (_hintInitialized && build == _shownBuild && selected == _shownSelection && selectedActive == _shownSelectionActive
                && hasTarget == _shownHasTarget && result == _shownResult && damageState == _shownDamageState
                && targetCell == _shownTargetCell && rotation == _shownRotation)
                return;
            _shownTargetCell = targetCell;
            _shownRotation = rotation;

            _hintInitialized = true;
            _shownBuild = build;
            _shownSelection = selected;
            _shownSelectionActive = selectedActive;
            _shownHasTarget = hasTarget;
            _shownResult = result;
            _shownDamageState = damageState;

            if (build != null)
            {
                string text = $"<b>{build.DisplayName}</b> 배치  ·  좌클릭: 배치  ·  {K(GameAction.Rotate)}: 회전  ·  우클릭/ESC: 취소";
                if (hasTarget && result != PlacementResult.Valid)
                    text += $"\n<color={HudText.Red}>배치 불가: {HudText.PlacementReason(result)}</color>";
                else if (hasTarget)
                {
                    text += AdjacencyPreviewLine(build, targetCell, rotation);
                    if (build.IsDefense) // 4-8: 범위 안에 들어올 모듈 수
                    {
                        var cells = StationGrid.ResolveCells(build.CellOffsets, targetCell, rotation);
                        int covered = _resources.Defense.CountCoveredWithResearch(_station.Grid, build, cells);
                        int radius = _resources.Defense.RangeOf(build); // 연구 반경 반영
                        text += $"\n<size=90%><color=#7FD8FF>방어 범위 (반경 {radius}칸): 모듈 {covered}개 보호</color></size>";
                    }
                    if (build.IsService) // 4-9: 범위 안 거주 모듈·주민
                    {
                        var cells = StationGrid.ResolveCells(build.CellOffsets, targetCell, rotation);
                        int habitats = _resources.Needs.CountHabitatsInRange(_station.Grid, build, cells, _resources.Simulation.Population, out float residents);
                        string color = habitats > 0 ? "#7CFF9A" : HudText.Orange;
                        text += $"\n<size=90%><color={color}>{build.ServiceNeed.DisplayName()} 범위 (반경 {_station.Simulation.Effects.ServiceRadius(build)}칸): 거주 모듈 {habitats}개 · 주민 약 {residents:0}명 (담당 최대 {build.ServiceCapacity}명)</color></size>";
                    }
                }
                _hintText.SetText(text);
            }
            else if (selected != null)
            {
                string name = selected.Data != null ? selected.Data.DisplayName : selected.ToString();
                string state = selectedActive ? string.Empty : $"  <color={HudText.Orange}>(비활성: 코어와 분리됨)</color>";
                string action = _station.CanRemove(selected) ? $"{K(GameAction.Maintain)}: 정비  ·  {K(GameAction.Rebuild)}: 재건축  ·  {K(GameAction.Demolish)}/{K(GameAction.DemolishAlt)}: 철거" : "철거 불가";
                string repair = damageState == 1 ? $"  ·  <color={HudText.Red}>{K(GameAction.Repair)}: 수리</color>"
                    : damageState == 3 ? $"  ·  <color={HudText.Yellow}>{K(GameAction.Repair)}: 우선 수리  ·  {K(GameAction.CancelRepair)}: 대기 취소</color>" : string.Empty;
                string lab = selected.Data != null && selected.Data.ResearchSlots > 0 ? $"  ·  <color=#B79CFF>{K(GameAction.Research)}: 연구 창</color>" : string.Empty;
                _hintText.SetText($"선택: <b>{name}</b>{state}{repair}{lab}  ·  {action}  ·  ESC: 선택 해제");
            }
            else
            {
                // 5-8: 조작 안내는 처음 잠시만, 이후에는 작게 "H: 도움말" (5-10 설정: 항상 / 처음 1분 / 끔)
                var hints = Settings.GameSettings.Hints;
                bool full = _helpPinned || hints == Settings.HintMode.Always || (!_helpDismissed && hints == Settings.HintMode.FirstMinute);
                _hintText.SetText(full
                    ? $"{IdleHint}  <size=85%><color={HudText.Muted}>·  {K(GameAction.ToggleHelp)}: 안내 {(_helpPinned ? "숨기기" : "고정")}</color></size>"
                    : $"<size=85%><color={HudText.Muted}>{HudTheme.Icon("info")} {K(GameAction.ToggleHelp)}: 조작 안내</color></size>");
            }
        }

        /// <summary>4-4: 이 자리에 놓으면 생길 인접 효과 (자신 / 이웃). 없으면 빈 문자열.</summary>
        private string AdjacencyPreviewLine(ModuleData data, Vector3Int cell, int rotation)
        {
            _station.Simulation.PreviewAdjacency(data, cell, rotation, _previewSelf, _previewNeighbors);
            if (_previewSelf.Count == 0 && _previewNeighbors.Count == 0)
                return string.Empty;
            var sb = new System.Text.StringBuilder("\n<size=90%>인접: ");
            bool first = true;
            foreach (var a in _previewSelf)
            {
                if (!first) sb.Append("  ·  ");
                first = false;
                sb.Append(AdjacencySystem.IsBeneficial(a.Rule, a.Total) ? "<color=#7CFF9A>" : $"<color={HudText.Orange}>") // 소비 −는 좋은 효과
                  .Append(AdjacencySystem.Describe(a)).Append("</color>");
            }
            foreach (var line in _previewNeighbors)
            {
                if (!first) sb.Append("  ·  ");
                first = false;
                sb.Append($"<color={HudText.Muted}>이웃</color> ").Append(line);
            }
            sb.Append("</size>");
            return sb.ToString();
        }

        private void ShowMessage(string message)
        {
            // 5-8: 색으로 중요도를 판단해 아이콘을 붙이고, 짧게 나타났다 사라짐
            string icon = message.Contains(HudText.Red) || message.Contains(HudText.Orange) ? "warning"
                : message.Contains(HudTheme.GreenHex) ? "event" : "info";
            _messageText.SetText($"{HudTheme.Icon(icon)} {message}");
            _messageUntil = Time.unscaledTime + _messageSeconds;
            _messageTween?.Play(restart: !_messageTween.Visible);
        }

        private void HandleGradeChanged(int previous, int current)
        {
            SyncShownLimit(); // 등급으로 바뀐 한도는 아래 등급 알림에 포함
            var p = _progression.Progression;
            var grade = p.GetGrade(current);
            if (current > previous)
            {
                var sb = new System.Text.StringBuilder();
                sb.Append($"<color=#7CFF9A><b>등급 상승: {grade.DisplayName}!</b>");
                if (p.IsFinalGrade && current > p.VictoryGradeIndex && !p.HasReachedTopGrade)
                    sb.Append("  <color=#FFD36A>★ 최고 등급 달성 (추가 목표)</color>"); // 8-6 초대형
                // 이전 등급 초과 ~ 현재 등급까지의 해금 목록
                for (int i = previous + 1; i <= current; i++)
                {
                    foreach (var m in p.GetGrade(i).Unlocks)
                        sb.Append($"  ·  {m.DisplayName} 해금");
                }
                if (p.LimitedModule != null)
                    sb.Append($"  ·  {p.LimitedModule.DisplayName} 최대 {p.CurrentLimit(_station.Grid)}개");
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

        private void HandleWornOut(ModuleInstance module)
        {
            string name = module.Data != null ? module.Data.DisplayName : module.ToString();
            string message = $"<color={HudText.Red}>{name} 노후로 파괴됨 (내구도 0)</color>";
            if (_disconnectedThisFrame > 0)
                message += $"\n<color={HudText.Orange}>모듈 {_disconnectedThisFrame}개가 코어와 분리되어 비활성화됨</color>";
            ShowMessage(message);
        }

        private void HandleActionFailed(string reason)
        {
            ShowMessage($"<color={HudText.Red}>{reason}</color>");
        }

        private void HandleActionDone(string message)
        {
            ShowMessage($"<color=#7FD8FF>{message}</color>");
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
            string refund = HudText.Cost(_selection.LastRemovedRefund); // 내구도 반영, 철거 직전에 계산됨
            string message = $"{name} 철거  ·  환급 {refund}";
            if (_disconnectedThisFrame > 0)
                message += $"\n<color={HudText.Orange}>모듈 {_disconnectedThisFrame}개가 코어와 분리되어 비활성화됨</color>";
            ShowMessage(message);
        }

        private void HandleRemoveRejected(ModuleInstance module)
        {
            bool isCore = module == _station.Core;
            string reason = isCore ? "코어는 철거할 수 없습니다"
                : _station.IsSupportingOthers(module) ? "코어 2층 옆·코어 위 모듈을 받치고 있어 철거할 수 없습니다 (그 모듈을 먼저 철거)"
                : "철거할 수 없는 모듈입니다";
            ShowMessage($"<color={HudText.Red}>{reason}</color>");
        }

        // Phase 10: 명단이 있으면 이름으로 알림
        private void HandleResidentArrived(Resident r, PopulationChangeReason? reason)
        {
            if (reason == null)
                return; // 불러오기·명단 보정
            var roster = _station.Simulation.Residents;
            ShowMessage($"<color={HudTheme.AccentHex}>새 주민 {r.Name}</color> · {ResidentText.Traits(roster.Config, r)} · {ResidentText.HomeName(roster, r.Home)}");
        }

        private void HandleResidentLeft(Resident r, PopulationChangeReason? reason)
        {
            string cause = ResidentText.Reason(reason);
            ShowMessage($"<color={HudText.Red}>{r.Name} 떠남{(cause != null ? $" ({cause})" : "")}</color>");
        }

        private void HandlePopulationChanged(int delta, PopulationChangeReason reason)
        {
            if (_station.Simulation.Residents != null)
                return; // 이름 알림 (HandleResidentLeft)
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
