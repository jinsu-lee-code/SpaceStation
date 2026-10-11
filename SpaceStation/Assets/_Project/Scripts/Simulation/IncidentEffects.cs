using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;

namespace SpaceStation.Simulation
{
    /// <summary>11-17 ④ 내부 사고 종류 (사용자 결정 2026-10-11).</summary>
    public enum IncidentKind
    {
        /// <summary>배관 누출 — 밸브 잠그기 (놓치면 물 · 산소가 샘).</summary>
        Leak,
        /// <summary>차단기 내려감 — 경고등 켜진 스위치 올리기 (놓치면 그 방 생산이 잠시 줄어듦).</summary>
        Breaker,
        /// <summary>작은 화재 — 소화기로 끄기 (놓치면 그 방 내구도 · 만족도 조금).</summary>
        Fire,
        /// <summary>환기 경보 — 필터 교체 (놓치면 산소가 조금 샘 · 만족도 조금).</summary>
        Filter,
    }

    /// <summary>
    /// 11-17 ④ 놓친 사고의 손실 (BALANCE 31번, 순수 C#): 상한이 있는 짧은 손실만 — 정해진 시간(게임 초)이 지나면 정비 인력이 처리한 것으로 끝남.
    /// 자원 누출 = 시간 동안 나눠서 빠짐, 차단기 = 그 방 생산 배율. 저장하지 않음 (2분짜리 짧은 효과).
    /// </summary>
    public sealed class IncidentEffects
    {
        private sealed class Drain
        {
            public ResourceType Type;
            public float PerSecond;
            public float Left;
        }

        private readonly List<Drain> _drains = new List<Drain>();
        private readonly Dictionary<ModuleInstance, (float Multiplier, float Left)> _trips = new Dictionary<ModuleInstance, (float, float)>();
        private readonly List<ModuleInstance> _ended = new List<ModuleInstance>();

        /// <summary>차단기가 내려간 방의 생산 배율 (없으면 1).</summary>
        public float ProductionMultiplier(ModuleInstance module) =>
            module != null && _trips.TryGetValue(module, out var t) ? t.Multiplier : 1f;

        public bool IsTripped(ModuleInstance module) => module != null && _trips.ContainsKey(module);

        /// <summary>지금 이어지는 손실 수 (누출 + 차단기).</summary>
        public int ActiveCount => _drains.Count + _trips.Count;

        internal void AddDrain(ResourceType type, float total, float seconds)
        {
            if (total <= 0f || seconds <= 0f)
                return;
            _drains.Add(new Drain { Type = type, PerSecond = total / seconds, Left = seconds });
        }

        internal void Trip(ModuleInstance module, float multiplier, float seconds)
        {
            if (module == null || seconds <= 0f)
                return;
            _trips[module] = (multiplier, seconds);
        }

        internal void Tick(float dt, ResourceSimulation resources)
        {
            if (dt <= 0f)
                return;
            for (int i = _drains.Count - 1; i >= 0; i--)
            {
                var d = _drains[i];
                float step = Math.Min(dt, d.Left);
                resources.RemoveStock(d.Type, d.PerSecond * step);
                d.Left -= step;
                if (d.Left <= 1e-4f)
                    _drains.RemoveAt(i);
            }
            _ended.Clear();
            var keys = new List<ModuleInstance>(_trips.Keys);
            foreach (var m in keys)
            {
                var t = _trips[m];
                t.Left -= dt;
                if (t.Left <= 0f)
                    _ended.Add(m);
                else
                    _trips[m] = t;
            }
            foreach (var m in _ended)
                _trips.Remove(m);
        }
    }
}
