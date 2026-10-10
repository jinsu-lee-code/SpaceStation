using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;

namespace SpaceStation.Simulation
{
    /// <summary>11-17 ② 특별 상자의 보상 종류.</summary>
    public enum CrateBonus
    {
        /// <summary>자원 상자 (금속 · 식량 · 물 · 산소 중 하나).</summary>
        None,
        /// <summary>연구 포인트 — 연구 시작 비용 할인 (Phase 12에서 레시피 해금에도 씀).</summary>
        ResearchPoints,
        /// <summary>다음 수리 1회 무료.</summary>
        FreeRepair,
        /// <summary>만족도 즉시 증가.</summary>
        Satisfaction,
    }

    /// <summary>정거장 안에 놓인 보급 상자 하나 (줍기 전까지 남음, 저장됨).</summary>
    public sealed class SupplyCrate
    {
        public int Id { get; internal set; }
        /// <summary>놓인 방 (화물 터미널 · 창고 · 코어).</summary>
        public ModuleInstance Module { get; internal set; }
        public ResourceType Resource { get; internal set; }
        public float Amount { get; internal set; }
        public CrateBonus Bonus { get; internal set; }
        /// <summary>같은 방 안 순번 (내부에서 자리 고르기 씨앗).</summary>
        public int Slot { get; internal set; }
        public bool IsSpecial => Bonus != CrateBonus.None;
    }

    /// <summary>
    /// 11-17 ② 보급품 줍기 (BALANCE 29번): 보급선이 오면 바깥 보상과 별개로 내부 화물 터미널 · 창고(없으면 코어)에
    /// 자원 상자 N개 + 특별 상자 1개(연구 포인트 · 무료 수리 · 만족도 중 하나)가 놓인다. 플레이어가 안에 들어가 주워야 받는다 ("하면 이득").
    /// 연구 포인트 · 무료 수리권도 여기서 센다. 보상 적용은 <see cref="StationSimulation.TryPickupCrate"/>.
    /// </summary>
    public sealed class SupplyCrateSystem
    {
        private static readonly ResourceType[] CrateResources = { ResourceType.Metal, ResourceType.Food, ResourceType.Water, ResourceType.Oxygen };
        private static readonly CrateBonus[] Bonuses = { CrateBonus.ResearchPoints, CrateBonus.FreeRepair, CrateBonus.Satisfaction };

        private readonly List<SupplyCrate> _crates = new List<SupplyCrate>();
        private readonly List<ModuleInstance> _targets = new List<ModuleInstance>();
        private int _nextId = 1;

        public IReadOnlyList<SupplyCrate> Crates => _crates;
        /// <summary>연구 포인트 (연구 시작 비용 할인, Phase 12 레시피 해금).</summary>
        public int ResearchPoints { get; internal set; }
        /// <summary>남은 무료 수리 횟수.</summary>
        public int FreeRepairs { get; internal set; }
        /// <summary>상자가 생기거나 줍거나 옮겨짐 · 포인트 · 수리권 변화.</summary>
        public event Action Changed;

        /// <summary>
        /// 보급선 도착: 상자를 놓을 방 = 연결된 화물 터미널 → 창고 → 코어 순으로 있는 것들에 돌아가며.
        /// 최대 수를 넘으면 특별 상자를 먼저 남기고 자원 상자를 덜 놓는다. 반환: 놓은 상자 수.
        /// </summary>
        public int Spawn(StationGrid grid, Func<ModuleInstance, bool> isActive, ModuleInstance core, BalanceConfig balance, float intensity, Func<float> random01)
        {
            if (grid == null || balance == null || random01 == null)
                throw new ArgumentNullException();
            int room = balance.SupplyCrateMax - _crates.Count;
            if (room <= 0)
                return 0;
            CollectTargets(grid, isActive, core);
            if (_targets.Count == 0)
                return 0;
            int placed = 0;
            // 특별 상자 1개 확정 (사용자 결정)
            Add(new SupplyCrate { Bonus = Bonuses[Pick(random01, Bonuses.Length)] }, placed++);
            // 한 번에 오는 자원 상자끼리는 종류가 겹치지 않게 (섞은 순서대로)
            var order = (ResourceType[])CrateResources.Clone();
            for (int i = order.Length - 1; i > 0; i--)
            {
                int j = Pick(random01, i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
            for (int i = 0; i < balance.SupplyResourceCrates && placed < room; i++)
            {
                Add(new SupplyCrate
                {
                    Resource = order[i % order.Length],
                    Amount = balance.CrateResourceAmount * Math.Max(0f, intensity),
                }, placed++);
            }
            Changed?.Invoke();
            return placed;
        }

        private static int Pick(Func<float> random01, int count) => Math.Min(count - 1, (int)(random01() * count));

        private void Add(SupplyCrate crate, int index)
        {
            crate.Id = _nextId++;
            crate.Module = _targets[index % _targets.Count];
            crate.Slot = CountIn(crate.Module);
            _crates.Add(crate);
        }

        private int CountIn(ModuleInstance module)
        {
            int n = 0;
            foreach (var c in _crates)
            {
                if (c.Module == module)
                    n++;
            }
            return n;
        }

        private void CollectTargets(StationGrid grid, Func<ModuleInstance, bool> isActive, ModuleInstance core)
        {
            _targets.Clear();
            foreach (var m in grid.Modules)
            {
                if (m.Data != null && m.Data.IsCargoTerminal && (isActive == null || isActive(m)))
                    _targets.Add(m);
            }
            if (_targets.Count == 0)
            {
                foreach (var m in grid.Modules)
                {
                    if (m.Data != null && m.Data.StorageBonus > 0f && (isActive == null || isActive(m)))
                        _targets.Add(m);
                }
            }
            if (_targets.Count == 0 && core != null)
                _targets.Add(core);
        }

        public SupplyCrate Find(int id)
        {
            foreach (var c in _crates)
            {
                if (c.Id == id)
                    return c;
            }
            return null;
        }

        internal bool Remove(SupplyCrate crate)
        {
            if (!_crates.Remove(crate))
                return false;
            Changed?.Invoke();
            return true;
        }

        /// <summary>방이 철거 · 파괴됨: 그 방 상자는 코어로 옮김 (잃지 않음).</summary>
        internal void Relocate(ModuleInstance removed, ModuleInstance core)
        {
            bool moved = false;
            foreach (var c in _crates)
            {
                if (c.Module != removed)
                    continue;
                c.Module = core;
                c.Slot = 100 + c.Id; // 코어 안 기존 상자와 자리 씨앗이 겹치지 않게
                moved = true;
            }
            if (moved)
                Changed?.Invoke();
        }

        /// <summary>연구 포인트 · 수리권을 쓰거나 얻음.</summary>
        internal void SetCounts(int researchPoints, int freeRepairs)
        {
            ResearchPoints = Math.Max(0, researchPoints);
            FreeRepairs = Math.Max(0, freeRepairs);
            Changed?.Invoke();
        }

        /// <summary>세이브 복원.</summary>
        internal void Restore(SupplyCrate crate)
        {
            crate.Id = _nextId++;
            _crates.Add(crate);
        }
    }
}
