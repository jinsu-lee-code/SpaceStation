using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 8-5 화물 터미널 (BALANCE 23번). 터미널마다 따로 화물선 타이머가 돌고(가동률만큼 느려짐, 파손·비활성이면 멈춤),
    /// 다 차면 가장 부족한 저장 자원(저장 한도 대비 재고 비율이 가장 낮은 것)을 그 한도 × 비율만큼 가져온다.
    /// 타이머는 세이브하지 않는다 (불러오면 처음부터).
    /// </summary>
    public sealed class CargoSystem
    {
        private readonly Dictionary<ModuleInstance, float> _progress = new Dictionary<ModuleInstance, float>();
        private readonly List<ModuleInstance> _terminals = new List<ModuleInstance>();

        /// <summary>(터미널, 자원, 실제로 들어온 양 — 한도 초과분 제외).</summary>
        public event Action<ModuleInstance, ResourceType, float> Delivered;

        /// <summary>다음 화물선까지 진행도 0~1.</summary>
        public float GetProgress(ModuleInstance terminal) => _progress.TryGetValue(terminal, out var p) ? p : 0f;

        public void Forget(ModuleInstance module) => _progress.Remove(module);

        public void Tick(StationGrid grid, ResourceSimulation resources, Func<ModuleInstance, float> strength, float dt)
        {
            _terminals.Clear();
            foreach (var m in grid.Modules)
            {
                if (m.Data != null && m.Data.IsCargoTerminal)
                    _terminals.Add(m);
            }
            foreach (var m in _terminals)
            {
                float s = strength != null ? strength(m) : 1f;
                if (s <= 0f)
                    continue;
                float p = GetProgress(m) + dt * s / m.Data.CargoInterval;
                if (p >= 1f - 1e-4f)
                {
                    p = Math.Max(0f, p - 1f);
                    var type = PickNeediest(resources);
                    float added = resources.AddStock(type, resources.GetCapacity(type) * m.Data.CargoFraction);
                    Delivered?.Invoke(m, type, added);
                }
                _progress[m] = p;
            }
        }

        /// <summary>저장 한도 대비 재고 비율이 가장 낮은 저장 자원 (같으면 enum 순서 앞쪽).</summary>
        public static ResourceType PickNeediest(ResourceSimulation resources)
        {
            var best = ResourceType.Metal;
            float bestRatio = float.MaxValue;
            foreach (ResourceType t in Enum.GetValues(typeof(ResourceType)))
            {
                if (!ResourceSimulation.IsStock(t))
                    continue;
                float cap = resources.GetCapacity(t);
                if (cap <= 0f)
                    continue;
                float ratio = resources.GetStock(t) / cap;
                if (ratio < bestRatio)
                {
                    bestRatio = ratio;
                    best = t;
                }
            }
            return best;
        }
    }
}
