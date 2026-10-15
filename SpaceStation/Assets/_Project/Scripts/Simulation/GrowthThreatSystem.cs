using System;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 13-2 타이쿤판 성장 비례 위협 (TYCOON.md K2-1, BALANCE 42번). 공통 고리(<see cref="IEventScale"/> · 파업 기준 보정)에 붙어
    /// 등급 표 대신 정거장 상태가 위기 크기를 정한다. 등급은 위기 종류만 연다(13-1 해금 선택).
    /// - 노출 면 총합(우주로 드러난 면) → 운석 · 유성우 개수, 이벤트 간격 — 넓게 퍼지면 커지고, 촘촘히 짓거나 덮으면 줄어듦
    /// - 모듈 수 → 이벤트 강도 (산소 누출 비율 · 태양 폭풍 시간 · 보급선 양)
    /// - 밀집도(인구 ÷ 거주 수용) → 파업 기준 만족도 +
    /// 칸 단위 열은 이미 발열 모듈 배치에 비례하므로 그대로.
    /// </summary>
    public sealed class GrowthThreatSystem : StationSystem, IEventScale
    {
        public const string SystemKey = "growth-threat";

        private readonly GrowthThreatConfig _config;
        private bool _dirty = true;
        private int _lastCount = -1;
        private float _hitsMin = 1f, _hitsMax = 1f, _interval = 1f, _intensity = 1f, _shower = -1f;

        public GrowthThreatSystem(GrowthThreatConfig config)
        {
            _config = config ? config : throw new ArgumentNullException(nameof(config));
        }

        /// <summary>시뮬레이션에 붙이고 이벤트 규모 · 파업 기준을 정하는 쪽으로 연결. 세이브 복원 전에 부른다.</summary>
        public static GrowthThreatSystem AttachTo(StationSimulation sim, GrowthThreatConfig config)
        {
            var system = sim.AddSystem(new GrowthThreatSystem(config), TickStepNames.Progression);
            sim.SetEventScale(system);
            if (sim.Crisis != null)
                sim.Crisis.ThresholdModifier = () => system.CrowdingThreshold;
            if (sim.MeteorShower != null)
                sim.MeteorShower.HitsMultiplier = () => system.ShowerMultiplier >= 0f ? system.ShowerMultiplier : sim.Balance.ShowerHitsMultiplier; // 작은 정거장 유성우 완화
            return system;
        }

        public override string Key => SystemKey;
        public GrowthThreatConfig Config => _config;
        /// <summary>우주로 노출된 면 총합.</summary>
        public int Exposure { get; private set; }
        /// <summary>등급 조건으로 세는 모듈 수.</summary>
        public int ModuleCount { get; private set; }
        /// <summary>밀집도 = 인구 ÷ 거주 수용 (수용 0이면 0).</summary>
        public float Crowding => Sim.Resources.HousingCapacity > 0 ? (float)Sim.Resources.Population / Sim.Resources.HousingCapacity : 0f;
        /// <summary>밀집도 때문에 파업 기준 만족도에 더해지는 값.</summary>
        public float CrowdingThreshold => _config.CrowdingThreshold(Crowding);

        public int MeteorHitsMin
        {
            get
            {
                Refresh();
                var (now, next) = Band();
                return Math.Max(1, Mathf.RoundToInt(Clamp(_hitsMin, now?.MeteorHitsMin, next?.MeteorHitsMin)));
            }
        }

        public int MeteorHitsMax
        {
            get
            {
                Refresh();
                var (now, next) = Band();
                return Math.Max(MeteorHitsMin, Mathf.RoundToInt(Clamp(_hitsMax, now?.MeteorHitsMax, next?.MeteorHitsMax)));
            }
        }

        public float EventIntervalMultiplier
        {
            get
            {
                Refresh();
                var (now, next) = Band();
                // 간격은 작을수록 위험 → 지금 등급 값이 상한, 다음 등급 값이 하한
                if (now == null)
                    return Mathf.Max(0.05f, _interval);
                float v = Mathf.Min(_interval, now.EventIntervalMultiplier);
                if (next != null && next.EventIntervalMultiplier < now.EventIntervalMultiplier)
                    v = Mathf.Max(v, next.EventIntervalMultiplier);
                return Mathf.Max(0.05f, v);
            }
        }

        /// <summary>
        /// 13-2 보강 (사용자 결정 2026-10-15: 등급 띠 안에서만 비례): 위협은 지금 등급 값 ~ 다음 등급 값 사이.
        /// 다음 등급 값이 지금보다 크지 않으면(도시급부터 등급 표가 같음) 상한 없이 계속 커짐. 등급 띠를 끄면 (null, null).
        /// </summary>
        private (StationGrade Now, StationGrade Next) Band()
        {
            if (!_config.ClampToGradeBand)
                return (null, null);
            return (Sim.Progression.Current, Sim.Progression.Next);
        }

        /// <summary>value를 [now, next] 사이로 (next가 now보다 크지 않으면 하한만). now가 null이면 그대로.</summary>
        private static float Clamp(float value, float? now, float? next)
        {
            if (now == null)
                return value;
            float v = Mathf.Max(value, now.Value);
            if (next != null && next.Value > now.Value)
                v = Mathf.Min(v, next.Value);
            return v;
        }

        /// <summary>유성우 배율 (노출 면 기준, 기준점이 없으면 -1).</summary>
        public float ShowerMultiplier
        {
            get
            {
                Refresh();
                return _shower;
            }
        }

        public float EventIntensityMultiplier
        {
            get
            {
                Refresh();
                var (now, next) = Band();
                return Mathf.Max(0.05f, Clamp(_intensity, now?.EventIntensityMultiplier, next?.EventIntensityMultiplier));
            }
        }

        public override void Tick(float dt) => Refresh();

        public override void OnModulePlaced(ModuleInstance module) => _dirty = true;
        public override void OnModuleRemoved(ModuleInstance module) => _dirty = true;
        public override void OnModuleMoved(ModuleInstance module) => _dirty = true;

        /// <summary>모듈이 바뀌었으면 노출 면 · 모듈 수를 다시 센다 (틱마다 · 규모를 읽을 때).</summary>
        public void Refresh()
        {
            int count = Sim.Grid.Modules.Count;
            if (!_dirty && count == _lastCount)
                return; // 알림 없이 바뀐 경우(세이브 복원 등)도 개수로 잡음
            _dirty = false;
            _lastCount = count;
            int exposure = 0;
            foreach (var m in Sim.Grid.Modules)
                exposure += DamageSystem.CountExposedFaces(Sim.Grid, m);
            Exposure = exposure;
            ModuleCount = StationProgression.CountGradeModules(Sim.Grid);
            (_hitsMin, _hitsMax, _interval) = _config.AtExposure(exposure);
            _shower = _config.ShowerAt(exposure);
            _intensity = _config.IntensityAt(ModuleCount);
        }
    }
}
