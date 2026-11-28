using System;
using System.Collections.Generic;
using System.Text;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// CONNECT_1121 7번 ③ 타이쿤판 대륙급 공급 사슬 (사용자 결정 2026-11-28, 추천안 — 값 = <see cref="SupplyChainConfig"/>).
    /// - 가동 중인(연결 · 파손 아님 · 합성 중 아님) 고급 모듈의 공급량을 모아 정거장 소비에 더함 (<see cref="ResourceSimulation.SetExternalDrain"/>, 금속 · 식량 · 물).
    /// - 자원별 공급 비율 = 재고가 남아 있으면 1, 바닥이면 생산 ÷ 소비 (지난 틱). 모듈 효율 = 그 모듈 공급 품목 중 가장 낮은 비율 (바닥 <see cref="SupplyChainConfig.MinEfficiency"/>).
    /// - 효율은 생산(<see cref="StationSimulation.ModuleProductionScale"/>)과 가동률(<see cref="StationSimulation.ModuleStrengthScale"/> — 서비스 · 방어 · 화물 · 수리 슬롯 · 연구 속도)에 곱함.
    ///   수용 인구는 그대로 (끊기면 효율만 떨어짐 — 주민이 쫓겨나지 않음).
    /// 상태는 매 틱 다시 셈 (세이브 없음).
    /// </summary>
    public sealed class SupplyChainSystem : StationSystem
    {
        public const string SystemKey = "supplychain";
        private static readonly ResourceType[] Managed = { ResourceType.Metal, ResourceType.Food, ResourceType.Water };

        private readonly SupplyChainConfig _config;
        private readonly Dictionary<ModuleInstance, float> _factor = new Dictionary<ModuleInstance, float>();
        private readonly float[] _demand = new float[ResourceSimulation.ResourceCount];
        private readonly float[] _ratio = new float[ResourceSimulation.ResourceCount];

        public SupplyChainSystem(SupplyChainConfig config)
        {
            _config = config ? config : throw new ArgumentNullException(nameof(config));
            for (int i = 0; i < _ratio.Length; i++)
                _ratio[i] = 1f;
        }

        /// <summary>시뮬레이션에 붙임 — 생산 · 가동률 · 정보 창 고리를 감쌈 (광맥 뒤에).</summary>
        public static SupplyChainSystem AttachTo(StationSimulation sim, SupplyChainConfig config)
        {
            if (sim == null || config == null)
                return null;
            var system = sim.AddSystem(new SupplyChainSystem(config), TickStepNames.Durability);
            var production = sim.ModuleProductionScale;
            sim.ModuleProductionScale = m => (production != null ? production(m) : 1f) * system.FactorOf(m);
            var strength = sim.ModuleStrengthScale;
            sim.ModuleStrengthScale = m => (strength != null ? strength(m) : 1f) * system.FactorOf(m);
            var info = sim.ModuleInfoText;
            sim.ModuleInfoText = m =>
            {
                string a = info?.Invoke(m), b = system.Info(m);
                return string.IsNullOrEmpty(a) ? b : string.IsNullOrEmpty(b) ? a : a + "\n" + b;
            };
            return system;
        }

        public override string Key => SystemKey;
        public SupplyChainConfig Config => _config;

        /// <summary>지금 공급을 받는(가동 중인) 고급 모듈 수 (측정 · 화면).</summary>
        public int SuppliedCount { get; private set; }
        /// <summary>그 가운데 공급이 모자란 모듈 수.</summary>
        public int ShortCount { get; private set; }

        /// <summary>자원별 고급 모듈 공급 합 (초당).</summary>
        public float DemandOf(ResourceType type) => _demand[(int)type];

        /// <summary>자원별 공급 비율 (0~1).</summary>
        public float RatioOf(ResourceType type) => _ratio[(int)type];

        /// <summary>이 모듈의 공급 효율 (고급 모듈이 아니면 1).</summary>
        public float FactorOf(ModuleInstance module) => module != null && _factor.TryGetValue(module, out var f) ? f : 1f;

        public bool IsSupplied(ModuleInstance module) => module != null && _config.SupplyOf(module.Data) != null;

        private bool Running(ModuleInstance m)
            => m.Data != null && Sim.Connectivity.IsActive(m) && !Sim.Damage.IsDamaged(m) && !Sim.IsHalted(m);

        public override void Tick(float dt)
        {
            var r = Sim.Resources;
            // 지난 자원 틱 기준 공급 비율
            foreach (var t in Managed)
            {
                int i = (int)t;
                float consumption = r.GetConsumption(t);
                _ratio[i] = r.GetStock(t) > _config.StockFloor || consumption <= 1e-4f ? 1f : Mathf.Clamp01(r.GetProduction(t) / consumption);
            }
            Array.Clear(_demand, 0, _demand.Length);
            _factor.Clear();
            int supplied = 0, shortCount = 0;
            foreach (var m in Sim.Grid.Modules)
            {
                var supply = _config.SupplyOf(m.Data);
                if (supply == null)
                    continue;
                float factor = 1f;
                if (Running(m))
                {
                    supplied++;
                    foreach (var a in supply)
                    {
                        if (Array.IndexOf(Managed, a.Type) < 0 || a.Amount <= 0f)
                            continue;
                        _demand[(int)a.Type] += a.Amount;
                        factor = Math.Min(factor, _ratio[(int)a.Type]);
                    }
                }
                factor = Math.Max(_config.MinEfficiency, factor);
                if (factor < 0.999f)
                    shortCount++;
                _factor[m] = factor;
            }
            SuppliedCount = supplied;
            ShortCount = shortCount;
            foreach (var t in Managed)
                r.SetExternalDrain(t, _demand[(int)t]);
        }

        /// <summary>정보 창 한 줄 (두 줄 · 한 줄 24자 안쪽).</summary>
        public string Info(ModuleInstance module)
        {
            var supply = module != null ? _config.SupplyOf(module.Data) : null;
            if (supply == null)
                return null;
            var sb = new StringBuilder("공급 사슬 ");
            bool first = true;
            ResourceType worst = ResourceType.Metal;
            float worstRatio = 2f;
            foreach (var a in supply)
            {
                if (Array.IndexOf(Managed, a.Type) < 0 || a.Amount <= 0f)
                    continue;
                if (!first)
                    sb.Append(" · ");
                sb.Append(a.Type.DisplayName()).Append(' ').Append(a.Amount.ToString("0.#")).Append("/초");
                first = false;
                if (_ratio[(int)a.Type] < worstRatio)
                {
                    worstRatio = _ratio[(int)a.Type];
                    worst = a.Type;
                }
            }
            float f = FactorOf(module);
            if (!Running(module))
                sb.Append("\n멈춘 동안은 공급을 받지 않음");
            else if (f >= 0.999f)
                sb.Append("\n공급 100% · 효과 그대로");
            else
                sb.Append($"\n{worst.DisplayName()} 바닥 · 효과 {f * 100f:0}%");
            return sb.ToString();
        }
    }
}
