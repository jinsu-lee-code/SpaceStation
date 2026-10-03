using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 정거장 상태 캡처·복원 (세이브/로드). 복원은 새로 만든 시뮬레이션(코어만 있는 상태)에 한 번만 한다.
    /// 복원 순서: 연구 레벨(저장 한도·수용 인원에 영향) → 모듈·내구도 → 재고·인구·배터리 → 파손 → 이벤트 → 통계·타이머 → 진행 중 연구
    /// → 0초 틱으로 파생값(생산량·전력 효율·요구 충족 등)을 다시 계산.
    /// 데이터가 바뀌어 찾을 수 없는 항목(모듈·이벤트·연구)은 건너뛰고 개수를 돌려준다.
    /// 시작 효과가 있는 진행 중 이벤트(태양 폭풍)는 전력 배율을 그대로 저장·복원한다.
    /// </summary>
    public static class StationStateSerializer
    {
        public static StationState Capture(StationSimulation sim)
        {
            if (sim == null)
                throw new ArgumentNullException(nameof(sim));
            var s = new StationState { Elapsed = sim.ElapsedSeconds };

            var index = new Dictionary<ModuleInstance, int>();
            foreach (var module in sim.Grid.Modules)
            {
                if (module == sim.Core || module.Data == null)
                    continue;
                var m = new ModuleState { Data = module.Data.name, Origin = module.Origin, Rotation = module.Rotation };
                if (sim.Durability.TryGetInfo(module, out var d))
                {
                    m.HasDurability = true;
                    m.Durability = d.Current;
                    m.MaxDurability = d.Max;
                    m.Maintenances = d.MaintenanceCount;
                }
                index[module] = s.Modules.Count;
                s.Modules.Add(m);
            }

            var res = sim.Resources;
            foreach (ResourceType type in Enum.GetValues(typeof(ResourceType)))
            {
                if (ResourceSimulation.IsStock(type))
                    s.Stock.Add(new NamedValue(type.ToString(), res.GetStock(type)));
            }
            s.BatteryCharge = res.BatteryCharge;
            s.PowerSupplyMultiplier = res.PowerSupplyMultiplier;
            s.Population = res.Population;

            var pop = sim.Population;
            s.Satisfaction = pop.Satisfaction;
            s.GrowthProgress = pop.GrowthProgress;
            s.OxygenLossTimer = pop.OxygenLossTimer;
            s.LowSatisfactionLossTimer = pop.LowSatisfactionLossTimer;
            s.OvercrowdedLossTimer = pop.OvercrowdedLossTimer;

            // 파손: 대기 중이 아닌 것 먼저, 대기열은 순서대로 (복원 시 호출 순서 = 대기 순서)
            foreach (var info in sim.Damage.DamagedModules)
            {
                if (!info.IsQueued && index.TryGetValue(info.Module, out int i))
                    s.Damage.Add(ToState(info, i));
            }
            foreach (var info in sim.Damage.Queue)
            {
                if (index.TryGetValue(info.Module, out int i))
                    s.Damage.Add(ToState(info, i));
            }

            s.TimeUntilNextEvent = sim.Events.TimeUntilNext;
            foreach (var a in sim.Events.ActiveEvents)
                s.ActiveEvents.Add(new EventState { Data = a.Data.name, Duration = a.Duration, Remaining = a.Remaining });
            s.UpcomingEvent = sim.Events.Upcoming != null ? sim.Events.Upcoming.name : "";
            s.PlannedMeteorHits = sim.PlannedMeteorHits;
            foreach (var t in sim.PlannedMeteorTargets)
                if (index.TryGetValue(t, out int i))
                    s.PlannedMeteorTargets.Add(i);

            s.ReachedFinalGrade = sim.Progression.HasReachedFinalGrade;
            var session = sim.Session;
            s.Session = new SessionState
            {
                HasEverHadPopulation = session.HasEverHadPopulation,
                MaxPopulation = session.MaxPopulation,
                EventsExperienced = session.EventsExperienced,
                ModulesDestroyed = session.ModulesDestroyed,
                DamageSpreads = session.DamageSpreads,
                MeteorsIntercepted = session.MeteorsIntercepted,
                MeteorsBlocked = session.MeteorsBlocked,
                Ricochets = session.Ricochets,
            };
            s.OxygenFailTimer = sim.Failure.OxygenTimer;
            s.SatisfactionFailTimer = sim.Failure.SatisfactionTimer;
            s.CoreFailTimer = sim.Failure.CoreTimer;

            foreach (var c in sim.Research.Categories)
                s.ResearchLevels.Add(new NamedValue(c.name, sim.Research.GetLevel(c)));
            foreach (var p in sim.Research.Projects)
                s.Projects.Add(new ProjectState { Category = p.Category.name, TargetLevel = p.TargetLevel, Progress = p.Progress });

            var auto = sim.Automation;
            s.Automation = new AutomationState
            {
                AutoMaintain = auto.AutoMaintain,
                AutoRebuild = auto.AutoRebuild,
                Threshold = auto.Threshold,
                ReserveRatio = auto.ReserveRatio,
                AutoMaintainCount = auto.AutoMaintainCount,
                AutoRebuildCount = auto.AutoRebuildCount,
            };
            return s;
        }

        private static DamageState ToState(DamageInfo info, int module) => new DamageState
        {
            Module = module,
            TimeUntilDestroyed = info.NeverDestroyed ? -1f : info.TimeUntilDestroyed, // 8-5 장갑: 음수 = 파괴 없음
            Repairing = info.IsRepairing,
            RepairRemaining = info.RepairRemaining,
            TimeUntilSpread = float.IsPositiveInfinity(info.TimeUntilSpread) ? -1f : info.TimeUntilSpread,
            HasSpread = info.HasSpread,
            Queued = info.IsQueued,
        };

        /// <summary>새 시뮬레이션에 상태를 복원한다. 반환: 찾지 못해 건너뛴 항목 수.</summary>
        public static int Restore(StationSimulation sim, StationState s)
        {
            if (sim == null)
                throw new ArgumentNullException(nameof(sim));
            if (s == null)
                return 0;
            int missing = 0;

            // 1. 연구 레벨 (저장 한도·수용 인원·배터리 용량이 먼저 맞아야 재고가 잘리지 않음)
            foreach (var lv in s.ResearchLevels)
            {
                var category = FindCategory(sim, lv.Name);
                if (category == null)
                {
                    if (lv.Value > 0f)
                        missing++;
                    continue;
                }
                sim.Research.SetLevel(category, (int)Math.Round(lv.Value));
            }
            var auto = sim.Automation;
            auto.AutoMaintain = s.Automation.AutoMaintain;
            auto.AutoRebuild = s.Automation.AutoRebuild;
            auto.Threshold = s.Automation.Threshold;
            auto.ReserveRatio = s.Automation.ReserveRatio;
            auto.AutoMaintainCount = s.Automation.AutoMaintainCount;
            auto.AutoRebuildCount = s.Automation.AutoRebuildCount;

            // 2. 모듈 (규칙·비용 없이 그대로 배치) + 내구도
            var placed = new ModuleInstance[s.Modules.Count];
            for (int i = 0; i < s.Modules.Count; i++)
            {
                var m = s.Modules[i];
                var data = sim.Progression.FindModule(m.Data);
                if (data == null || !sim.Grid.TryPlace(data, m.Origin, m.Rotation, out var module))
                {
                    missing++;
                    continue;
                }
                placed[i] = module;
                if (m.HasDurability)
                    sim.Durability.Restore(module, m.Durability, m.MaxDurability, m.Maintenances);
            }

            // 3. 재고·인구·배터리 (한도는 배치 때 다시 계산됨)
            foreach (var v in s.Stock)
            {
                if (Enum.TryParse(v.Name, out ResourceType type))
                    sim.Resources.SetStock(type, v.Value);
            }
            sim.Resources.SetPopulation(s.Population);
            sim.Resources.RestoreBattery(s.BatteryCharge);
            sim.Resources.PowerSupplyMultiplier = s.PowerSupplyMultiplier;

            // 4. 파손 (대기열 순서 유지)
            foreach (var d in s.Damage)
            {
                var module = d.Module >= 0 && d.Module < placed.Length ? placed[d.Module] : null;
                if (module == null)
                {
                    missing++;
                    continue;
                }
                sim.Damage.Restore(module, d.TimeUntilDestroyed, d.Repairing, d.RepairRemaining, d.TimeUntilSpread, d.HasSpread, d.Queued);
            }

            // 5. 이벤트
            sim.Events.RestoreTimer(s.TimeUntilNextEvent);
            foreach (var e in s.ActiveEvents)
            {
                var data = FindEvent(sim, e.Data);
                if (data == null)
                {
                    missing++;
                    continue;
                }
                sim.Events.RestoreActive(data, e.Duration, e.Remaining);
            }
            if (!string.IsNullOrEmpty(s.UpcomingEvent))
            {
                var upcoming = FindEvent(sim, s.UpcomingEvent);
                if (upcoming == null)
                    missing++;
                else
                {
                    sim.Events.RestoreUpcoming(upcoming);
                    var targets = new List<ModuleInstance>();
                    foreach (int i in s.PlannedMeteorTargets ?? new List<int>())
                        if (i >= 0 && i < placed.Length)
                            targets.Add(placed[i]);
                    sim.RestoreMeteorPlan(s.PlannedMeteorHits, targets); // 사라진 대상은 다시 뽑아 채움
                }
            }

            // 6. 통계·타이머·만족도
            var ss = s.Session ?? new SessionState();
            sim.Session.Restore(ss.HasEverHadPopulation, ss.MaxPopulation, ss.EventsExperienced, ss.ModulesDestroyed, ss.DamageSpreads,
                ss.MeteorsIntercepted, ss.MeteorsBlocked, ss.Ricochets);
            sim.Failure.Restore(s.OxygenFailTimer, s.SatisfactionFailTimer, s.CoreFailTimer);
            sim.Population.Restore(s.Satisfaction, s.GrowthProgress, s.OxygenLossTimer, s.LowSatisfactionLossTimer, s.OvercrowdedLossTimer);
            sim.RestoreElapsed(s.Elapsed);

            // 7. 진행 중 연구
            foreach (var p in s.Projects)
            {
                var category = FindCategory(sim, p.Category);
                if (category == null || !sim.Research.RestoreProject(category, p.TargetLevel, p.Progress))
                    missing++;
            }

            // 8. 파생값 다시 계산 (시간은 흐르지 않음), 최고 등급 기록은 그 뒤에 덮어씀
            sim.Tick(0f);
            sim.Progression.RestoreReachedFinal(s.ReachedFinalGrade || sim.Progression.IsFinalGrade);
            return missing;
        }

        private static ResearchCategoryData FindCategory(StationSimulation sim, string name)
        {
            foreach (var c in sim.Research.Categories)
                if (c.name == name)
                    return c;
            return null;
        }

        private static GameEventData FindEvent(StationSimulation sim, string name)
        {
            foreach (var e in sim.EventPool)
                if (e != null && e.name == name)
                    return e;
            return null;
        }
    }
}
