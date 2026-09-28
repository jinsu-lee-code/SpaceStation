using System;
using System.Collections.Generic;

namespace SpaceStation.Core
{
    /// <summary>
    /// 루트(코어) 기준 BFS로 모듈 활성 여부를 계산한다.
    /// 배치/철거 뒤 <see cref="Recalculate"/>를 호출하면, 상태가 바뀐 모듈(새 모듈 포함)만 이벤트로 알린다.
    /// </summary>
    public sealed class StationConnectivity
    {
        private readonly StationGrid _grid;
        private readonly IConnectionRule _rule;
        private readonly HashSet<ModuleInstance> _active = new HashSet<ModuleInstance>();
        private readonly Dictionary<ModuleInstance, bool> _reported = new Dictionary<ModuleInstance, bool>();
        private readonly Queue<ModuleInstance> _queue = new Queue<ModuleInstance>();
        private readonly List<ModuleInstance> _neighbors = new List<ModuleInstance>();
        private readonly List<ModuleInstance> _stale = new List<ModuleInstance>();

        /// <summary>(모듈, 활성 여부). 이전에 알린 상태와 다를 때만 발생.</summary>
        public event Action<ModuleInstance, bool> ActiveStateChanged;

        public ModuleInstance Root { get; set; }
        public int ActiveCount => _active.Count;

        public StationConnectivity(StationGrid grid, IConnectionRule rule)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _rule = rule ?? throw new ArgumentNullException(nameof(rule));
        }

        public bool IsActive(ModuleInstance module)
        {
            return module != null && _active.Contains(module);
        }

        public void Recalculate()
        {
            _active.Clear();
            if (Root != null && _grid.TryGetModule(Root.Origin, out var root) && root == Root)
            {
                _active.Add(Root);
                _queue.Enqueue(Root);
                while (_queue.Count > 0)
                {
                    var current = _queue.Dequeue();
                    _rule.GetConnectedModules(_grid, current, _neighbors);
                    foreach (var next in _neighbors)
                    {
                        if (_active.Add(next))
                            _queue.Enqueue(next);
                    }
                }
            }

            // 그리드에서 사라진 모듈은 알림 없이 정리
            _stale.Clear();
            foreach (var module in _reported.Keys)
            {
                if (!_grid.TryGetModule(module.Origin, out var current) || current != module)
                    _stale.Add(module);
            }
            foreach (var module in _stale)
                _reported.Remove(module);

            foreach (var module in _grid.Modules)
            {
                bool isActive = _active.Contains(module);
                if (_reported.TryGetValue(module, out bool previous) && previous == isActive)
                    continue;
                _reported[module] = isActive;
                ActiveStateChanged?.Invoke(module, isActive);
            }
        }
    }
}
