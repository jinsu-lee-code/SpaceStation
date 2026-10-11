using System;
using System.Collections.Generic;
using System.Text;
using SpaceStation.Core;
using SpaceStation.Data;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 12-0b (StationSimulation에서 분리): 무작위 이벤트의 효과 (3-3) — 운석(<see cref="StationMeteors"/>) · 산소 누출 · 태양 폭풍 · 보급선(+ 내부 보급 상자),
    /// 그리고 방치된 파손의 확산(4-7).
    /// </summary>
    internal sealed class StationEventEffects
    {
        private readonly StationSimulation _sim;
        private readonly StationMeteors _meteors;
        private readonly Func<float> _random01;
        private readonly Random _crateRandom;
        private readonly List<ModuleInstance> _spreadCandidates = new List<ModuleInstance>();

        /// <summary>파손이 번짐 (원본, 대상). 4-7</summary>
        public event Action<ModuleInstance, ModuleInstance> DamageSpread;

        public StationEventEffects(StationSimulation sim, StationMeteors meteors, Func<float> random01, Random crateRandom)
        {
            _sim = sim;
            _meteors = meteors;
            _random01 = random01;
            _crateRandom = crateRandom;
        }

        public void HandleEventStarted(GameEventData data)
        {
            _sim.Session.RecordEvent();
            switch (data)
            {
                case MeteorEventData _:
                    _meteors.Apply();
                    break;
                case OxygenLeakEventData leak:
                    var resources = _sim.Resources;
                    float ratio = Math.Min(leak.MaxLossRatio, leak.StockLossRatio * _sim.EventIntensity); // 8-6 상한 (노멀 70%)
                    float lost = resources.RemoveStock(ResourceType.Oxygen, resources.GetStock(ResourceType.Oxygen) * ratio);
                    _sim.Report($"산소 -{lost:0.#} ({ratio * 100f:0}%)", false);
                    break;
                case SolarStormEventData storm:
                    _sim.Resources.PowerSupplyMultiplier = storm.PowerSupplyMultiplier;
                    _sim.Report($"전력 생산 -{(1f - storm.PowerSupplyMultiplier) * 100f:0}% ({ActiveDuration(storm):0}초)", false);
                    break;
                case SupplyShipEventData supply:
                    ApplySupply(supply);
                    break;
            }
        }

        public void HandleEventEnded(ActiveEvent active)
        {
            if (active.Data is SolarStormEventData)
                _sim.Resources.PowerSupplyMultiplier = 1f;
        }

        private float ActiveDuration(GameEventData data)
        {
            foreach (var a in _sim.Events.ActiveEvents)
            {
                if (a.Data == data)
                    return a.Duration;
            }
            return data.Duration;
        }

        private void ApplySupply(SupplyShipEventData supply)
        {
            var sb = new StringBuilder();
            bool clamped = false;
            float intensity = _sim.EventIntensity;
            foreach (var reward in supply.Rewards)
            {
                float amount = reward.Amount * intensity;
                float added = _sim.Resources.AddStock(reward.Type, amount);
                if (added < amount - 1e-3f)
                    clamped = true;
                if (sb.Length > 0)
                    sb.Append(", ");
                sb.Append(reward.Type.DisplayName()).Append(" +").Append(added.ToString("0.#"));
            }
            if (clamped)
                sb.Append(" (저장 한도 초과분 제외)");
            // 11-17 ②: 내부 화물 터미널 · 창고에 보급 상자 (특별 상자 1개 확정)
            int crates = _sim.Supply.Spawn(_sim.Grid, _sim.Connectivity.IsActive, _sim.Core, _sim.Balance, intensity, () => (float)_crateRandom.NextDouble());
            if (crates > 0)
                sb.Append($" · 내부에 보급 상자 {crates}개");
            _sim.Report(sb.ToString(), true);
        }

        /// <summary>
        /// 4-7 연쇄 파손: 면이 맞닿은 정상 모듈(코어 제외) 중 1곳을 무작위로 파손시키고 내구도를 깎는다.
        /// 번진 파손도 60초 방치되면 다시 번진다. 대상이 없으면 조용히 끝.
        /// </summary>
        public void HandleSpreadDue(ModuleInstance source)
        {
            var grid = _sim.Grid;
            if (!grid.TryGetModule(source.Origin, out var placed) || placed != source)
                return; // 같은 틱에 파괴됨
            grid.GetNeighborModules(source, _spreadCandidates);
            for (int i = _spreadCandidates.Count - 1; i >= 0; i--)
            {
                var n = _spreadCandidates[i];
                if (n == _sim.Core || _sim.Damage.IsDamaged(n))
                    _spreadCandidates.RemoveAt(i);
            }
            if (_spreadCandidates.Count == 0)
                return;
            int index = Math.Min(_spreadCandidates.Count - 1, (int)(_random01() * _spreadCandidates.Count));
            var target = _spreadCandidates[index];
            _sim.Damage.Damage(target);
            _sim.Durability.ApplyImpact(target, _sim.Balance.SpreadDurabilityDamage);
            _sim.Session.RecordSpread();
            DamageSpread?.Invoke(source, target);
            string from = source.Data != null ? source.Data.DisplayName : source.ToString();
            string to = target.Data != null ? target.Data.DisplayName : target.ToString();
            _sim.Report($"파손 확산! {from} → {to}  ·  방치된 파손은 번집니다 (수리 또는 대기열 등록으로 멈춤)", false);
        }
    }
}
