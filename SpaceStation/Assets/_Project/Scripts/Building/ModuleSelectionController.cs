using System;
using SpaceStation.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpaceStation.Building
{
    /// <summary>
    /// 배치 모드가 아닐 때 모듈 선택/철거.
    /// 좌클릭: 선택 (빈 곳 클릭 시 해제) / Delete·X: 철거 / ESC: 선택 해제
    /// CONNECT M7 묶음 선택 (사용자 결정 2026-11-27): Shift+좌클릭 = 묶음에 더하기 · 빼기, 가지 선택 키(기본 G) = 선택한 모듈 너머 전부.
    /// <see cref="Selected"/>는 대표 모듈(처음 고른 것 — 정보 창 · 다른 화면은 그대로 이 하나를 봄), <see cref="Group"/>은 대표 포함 전부.
    /// 묶음이 둘 이상이면 철거 키는 막음 (옮기기만).
    /// </summary>
    public sealed class ModuleSelectionController : MonoBehaviour
    {
        [SerializeField] private StationController _station;
        [SerializeField] private BuildController _build;
        [Tooltip("12-3 합성 위치 지정 중에는 선택하지 않음")]
        [SerializeField] private SynthesisController _synthesis;
        [SerializeField] private Camera _camera;

        [Header("Raycast")]
        [SerializeField] private LayerMask _moduleMask = ~0;
        [SerializeField] private float _maxRayDistance = 500f;

        private ModuleInstance _selected;
        private readonly System.Collections.Generic.List<ModuleInstance> _group = new System.Collections.Generic.List<ModuleInstance>();
        private readonly System.Collections.Generic.List<ModuleInstance> _branch = new System.Collections.Generic.List<ModuleInstance>();

        /// <summary>선택 모듈이 바뀔 때 (null = 해제).</summary>
        public event Action<ModuleInstance> SelectionChanged;
        /// <summary>CONNECT M7: 묶음(대표 포함)이 바뀔 때.</summary>
        public event Action GroupChanged;
        /// <summary>CONNECT M7: 묶음 조작 안내 · 거절 (상태 표시줄 알림용 — 글, 거절인지).</summary>
        public event Action<string, bool> GroupNotice;

        /// <summary>CONNECT M7: 묶음 (대표 포함, 대표가 맨 앞). 하나만 골랐으면 그 하나, 선택이 없으면 비어 있음.</summary>
        public System.Collections.Generic.IReadOnlyList<ModuleInstance> Group => _group;
        /// <summary>CONNECT M7: 모듈을 둘 이상 고름.</summary>
        public bool IsGroup => _group.Count > 1;
        /// <summary>철거를 시도했으나 불가 (코어 등).</summary>
        public event Action<ModuleInstance> RemoveRejected;
        /// <summary>철거 성공.</summary>
        public event Action<ModuleInstance> Removed;

        private ModuleInstance _removedSelection;

        public ModuleInstance Selected => _selected;
        /// <summary>마지막 철거의 실제 환급액 (내구도 반영, 철거 직전에 계산).</summary>
        public System.Collections.Generic.IReadOnlyList<SpaceStation.Data.ResourceAmount> LastRemovedRefund { get; private set; }

        private void Awake()
        {
            if (_camera == null)
                _camera = Camera.main;
        }

        private void Start()
        {
            // StationController.Awake에서 그리드가 만들어진 뒤 구독
            _station.Grid.ModuleRemoved += HandleModuleRemoved;
            _station.Simulation.Automation.ModuleRebuilt += HandleAutoRebuilt;
            if (_build != null)
            {
                _build.Moved += HandleMoved;
                _build.MoveCancelled += HandleMoved;
                _build.GroupMoveEnded += SetGroup;
            }
        }

        /// <summary>12-3b: 옮긴(또는 이동을 취소한) 모듈을 계속 선택 (이동 모드에 들어가며 선택이 풀렸음).</summary>
        private void HandleMoved(ModuleInstance module) => Select(module);

        private void OnDestroy()
        {
            if (_station != null && _station.Grid != null)
                _station.Grid.ModuleRemoved -= HandleModuleRemoved;
            if (_station != null && _station.Simulation != null)
                _station.Simulation.Automation.ModuleRebuilt -= HandleAutoRebuilt;
            if (_build != null)
            {
                _build.Moved -= HandleMoved;
                _build.MoveCancelled -= HandleMoved;
                _build.GroupMoveEnded -= SetGroup;
            }
        }

        /// <summary>선택한 모듈이 자동 재건축되면 새 모듈을 계속 선택한다.</summary>
        private void HandleAutoRebuilt(ModuleInstance previous, ModuleInstance rebuilt)
        {
            if (previous != null && previous == _removedSelection)
                Select(rebuilt);
            _removedSelection = null;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard == null || mouse == null || InputGate.Blocked)
                return;

            if ((_build != null && _build.Selected != null) || (_synthesis != null && _synthesis.Active))
            {
                Select(null); // 배치 · 합성 위치 지정 중에는 선택 해제
                return;
            }

            if (mouse.leftButton.wasPressedThisFrame && !UiPointer.IsOverUi() && !InputGate.ClickConsumedThisFrame)
            {
                var picked = PickModule(mouse.position.ReadValue());
                if (keyboard.shiftKey.isPressed)
                    ToggleInGroup(picked); // CONNECT M7: 빈 곳 Shift+클릭은 묶음 그대로
                else
                    Select(picked);
            }

            if (_selected == null)
                return;

            if (keyboard.escapeKey.wasPressedThisFrame && !InputGate.EscapeConsumedThisFrame)
            {
                Select(null);
                return;
            }

            if (KeyBindings.WasPressed(GameAction.SelectBranch))
                SelectBranch();

            if ((KeyBindings.WasPressed(GameAction.Demolish) || KeyBindings.WasPressed(GameAction.DemolishAlt)) && TutorialGate.Check(TutorialAction.Demolish))
            {
                if (IsGroup)
                    GroupNotice?.Invoke("묶음을 고른 동안에는 철거할 수 없습니다 (모듈 하나만 고르기)", true);
                else
                    RemoveSelected();
            }
        }

        /// <summary>CONNECT M7 Shift+클릭: 묶음에 없으면 더하고 있으면 뺌 (대표를 빼면 다음 모듈이 대표). 선택이 없으면 그냥 선택.</summary>
        public void ToggleInGroup(ModuleInstance module)
        {
            if (module == null)
                return;
            if (_selected == null)
            {
                Select(module);
                return;
            }
            int index = _group.IndexOf(module);
            if (index < 0)
            {
                _group.Add(module);
                SetHighlight(module, true);
                GroupChanged?.Invoke();
                return;
            }
            _group.RemoveAt(index);
            SetHighlight(module, false);
            if (module == _selected)
            {
                _selected = _group.Count > 0 ? _group[0] : null;
                if (_selected != null)
                    SetHighlight(_selected, true);
                SelectionChanged?.Invoke(_selected);
            }
            GroupChanged?.Invoke();
        }

        /// <summary>CONNECT M7 가지 선택: 대표 모듈을 빼면 코어에서 끊기는 쪽 전부를 묶음에 더함 (이미 고른 모듈은 그대로).</summary>
        public void SelectBranch()
        {
            if (_selected == null)
                return;
            _station.Simulation.CollectBranch(_selected, _branch);
            int added = 0;
            foreach (var m in _branch)
            {
                if (_group.Contains(m))
                    continue;
                _group.Add(m);
                SetHighlight(m, true);
                added++;
            }
            if (added > 0)
                GroupChanged?.Invoke();
            if (_branch.Count <= 1)
                GroupNotice?.Invoke("이 모듈 너머로 끊기는 모듈이 없습니다 (다른 길로도 코어에 이어짐)", true);
            else
                GroupNotice?.Invoke($"가지 선택 · 모듈 {_branch.Count}개 (묶음 {_group.Count}개)", false);
        }

        /// <summary>CONNECT M7: 묶음째 옮기기가 끝나거나 취소되면 같은 묶음을 다시 고름 (pivot = 대표).</summary>
        public void SetGroup(System.Collections.Generic.IReadOnlyList<ModuleInstance> modules, ModuleInstance pivot)
        {
            Select(pivot);
            if (pivot == null || modules == null)
                return;
            bool changed = false;
            foreach (var m in modules)
            {
                if (m == null || _group.Contains(m) || !_station.Grid.TryGetModule(m.Origin, out var at) || at != m)
                    continue;
                _group.Add(m);
                SetHighlight(m, true);
                changed = true;
            }
            if (changed)
                GroupChanged?.Invoke();
        }

        private void SetHighlight(ModuleInstance module, bool on)
        {
            if (module != null && _station.TryGetView(module, out var view))
                view.SetHighlighted(on);
        }

        /// <summary>7-1: 선택 모듈의 효율 구간을 테두리 색에 반영 (값 조회만, 구간이 바뀔 때만 뷰 갱신).</summary>
        private void LateUpdate()
        {
            if (_selected != null && _station.TryGetView(_selected, out var view))
                view.SetEfficiencyBand(EfficiencyBands.Classify(_station.Simulation.GetModuleEfficiency(_selected)));
        }

        public void Select(ModuleInstance module)
        {
            if (module == _selected && _group.Count <= 1)
                return;

            bool hadGroup = _group.Count > 1;
            foreach (var m in _group)
            {
                if (m != module)
                    SetHighlight(m, false);
            }
            _group.Clear();
            bool changed = module != _selected;
            _selected = module;
            if (_selected != null)
            {
                _group.Add(_selected);
                if (_station.TryGetView(_selected, out var view))
                {
                    view.SetEfficiencyBand(EfficiencyBands.Classify(_station.Simulation.GetModuleEfficiency(_selected)));
                    view.SetHighlighted(true);
                }
            }
            if (changed)
                SelectionChanged?.Invoke(_selected);
            if (changed || hadGroup)
                GroupChanged?.Invoke();
        }

        public void RemoveSelected()
        {
            if (_selected == null)
                return;
            var target = _selected;
            LastRemovedRefund = _station.GetRefund(target);
            if (_station.TryRemove(target))
                Removed?.Invoke(target);
            else
                RemoveRejected?.Invoke(target);
        }

        private ModuleInstance PickModule(Vector2 screenPosition)
        {
            // 12-0 (U-5): 모델 모양(Pick 콜라이더)으로 판정, 맞은 콜라이더의 모듈 뷰에서 모듈을 찾는다
            var ray = _camera.ScreenPointToRay(screenPosition);
            if (!Physics.Raycast(ray, out var hit, _maxRayDistance, ModulePick.SelectionMask(_moduleMask), QueryTriggerInteraction.Ignore))
                return null;
            var view = hit.collider.GetComponentInParent<ModuleView>();
            if (view != null && view.Module != null)
                return view.Module;
            _station.Grid.TryGetModule(GridConfig.GetHitCell(hit.point, hit.normal), out var module);
            return module;
        }

        private void HandleModuleRemoved(ModuleInstance module)
        {
            if (!_group.Remove(module))
                return;
            if (module != _selected)
            {
                GroupChanged?.Invoke(); // 묶음의 다른 모듈이 파괴 · 철거됨
                return;
            }
            _removedSelection = module; // 자동 재건축이면 곧 새 모듈로 다시 선택
            _selected = _group.Count > 0 ? _group[0] : null; // 뷰는 곧 파괴되므로 강조 해제 불필요
            if (_selected != null)
                SetHighlight(_selected, true);
            SelectionChanged?.Invoke(_selected);
            GroupChanged?.Invoke();
        }
    }
}
