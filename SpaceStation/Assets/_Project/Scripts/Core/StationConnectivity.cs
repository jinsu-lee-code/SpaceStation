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

        /// <summary>
        /// CONNECT M7: 지금 격자에서 코어에 닿는 모듈을 모은다 (exclude는 없다고 봄). 활성 상태 · 알림은 바꾸지 않는다
        /// (묶음 옮기기 판정 — 옮긴 뒤 끊기는 모듈 세기).
        /// </summary>
        public void CollectReachable(HashSet<ModuleInstance> into, ModuleInstance exclude = null)
        {
            into.Clear();
            if (Root == null || Root == exclude || !_grid.TryGetModule(Root.Origin, out var root) || root != Root)
                return;
            var queue = new Queue<ModuleInstance>();
            var neighbors = new List<ModuleInstance>();
            into.Add(Root);
            queue.Enqueue(Root);
            while (queue.Count > 0)
            {
                _rule.GetConnectedModules(_grid, queue.Dequeue(), neighbors);
                foreach (var next in neighbors)
                {
                    if (next != exclude && into.Add(next))
                        queue.Enqueue(next);
                }
            }
        }

        /// <summary>
        /// CONNECT M7 가지 선택 (사용자 결정 2026-11-27): module을 빼면 코어에서 끊기는 쪽 가운데 module에 이어진 모듈 (module 포함, module이 맨 앞).
        /// 고리처럼 다른 길이 있으면 module 하나뿐. 이미 끊긴 섬의 모듈이면 그 섬 전체.
        /// </summary>
        public void CollectBranch(ModuleInstance module, List<ModuleInstance> into)
        {
            into.Clear();
            if (module == null)
                return;
            var reach = new HashSet<ModuleInstance>();
            CollectReachable(reach, module);
            var seen = new HashSet<ModuleInstance> { module };
            var queue = new Queue<ModuleInstance>();
            var neighbors = new List<ModuleInstance>();
            into.Add(module);
            queue.Enqueue(module);
            while (queue.Count > 0)
            {
                _rule.GetConnectedModules(_grid, queue.Dequeue(), neighbors);
                foreach (var next in neighbors)
                {
                    if (next == Root || reach.Contains(next) || !seen.Add(next))
                        continue;
                    into.Add(next);
                    queue.Enqueue(next);
                }
            }
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
