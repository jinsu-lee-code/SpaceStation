using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 세이브용 정거장 상태 (JsonUtility 직렬화). 에셋은 이름(ModuleData·GameEventData·ResearchCategoryData의 name),
    /// 자원은 ResourceType 이름으로 저장해 순서가 바뀌어도 읽을 수 있게 한다. 무한대는 -1로 저장한다.
    /// 캡처·복원은 <see cref="StationStateSerializer"/>.
    /// </summary>
    [Serializable]
    public sealed class StationState
    {
        public float Elapsed;
        /// <summary>코어 제외, 배치 순서대로.</summary>
        public List<ModuleState> Modules = new List<ModuleState>();
        public List<NamedValue> Stock = new List<NamedValue>();
        public float BatteryCharge;
        public float PowerSupplyMultiplier = 1f;
        public int Population;

        public float Satisfaction;
        public float GrowthProgress;
        public float OxygenLossTimer;
        public float LowSatisfactionLossTimer;
        public float OvercrowdedLossTimer;

        public List<DamageState> Damage = new List<DamageState>();

        public float TimeUntilNextEvent;
        public List<EventState> ActiveEvents = new List<EventState>();
        /// <summary>조기 경보 중인 다음 이벤트 (없으면 빈 문자열), 운석 예정 대상 (Modules 순번).</summary>
        public string UpcomingEvent;
        public int PlannedMeteorHits;
        public List<int> PlannedMeteorTargets = new List<int>();

        public bool ReachedFinalGrade; // 승리 등급(대형) 도달 — 이름은 이전 세이브 호환용
        public bool ReachedTopGrade;   // 8-6 최고 등급(초대형) 도달
        public SessionState Session = new SessionState();
        public float OxygenFailTimer;
        public float SatisfactionFailTimer;
        public float CoreFailTimer;

        public List<NamedValue> ResearchLevels = new List<NamedValue>();
        /// <summary>진행 중인 연구 (순서 = 연구소가 모자랄 때 멈추는 우선순위).</summary>
        public List<ProjectState> Projects = new List<ProjectState>();
        public AutomationState Automation = new AutomationState();
        /// <summary>Phase 9 튜토리얼 진행 (Active false = 없음·끝남).</summary>
        public TutorialState Tutorial = new TutorialState();
        /// <summary>Phase 10 주민 명단 (이전 세이브는 비어 있음 → 인원수만큼 새로 생성).</summary>
        public List<ResidentState> Residents = new List<ResidentState>();
        /// <summary>11-17 ② 내부 보급 상자 · 연구 포인트 · 무료 수리권 (이전 세이브는 비어 있음 = 0).</summary>
        public List<CrateState> Crates = new List<CrateState>();
        public int ResearchPoints;
        public int FreeRepairs;
    }

    [Serializable]
    public sealed class CrateState
    {
        /// <summary>StationState.Modules 안의 순번, 코어 -2.</summary>
        public int Module;
        public string Resource;
        public float Amount;
        public string Bonus;
        public int Slot;
    }

    [Serializable]
    public sealed class ModuleState
    {
        public string Data;
        public Vector3Int Origin;
        public int Rotation;
        public bool HasDurability;
        public float Durability;
        public float MaxDurability;
        public int Maintenances;
    }

    [Serializable]
    public sealed class NamedValue
    {
        public string Name;
        public float Value;

        public NamedValue() { }
        public NamedValue(string name, float value)
        {
            Name = name;
            Value = value;
        }
    }

    [Serializable]
    public sealed class DamageState
    {
        /// <summary>StationState.Modules 안의 순번.</summary>
        public int Module;
        public float TimeUntilDestroyed;
        public bool Repairing;
        public float RepairRemaining;
        /// <summary>-1 = 확산 없음.</summary>
        public float TimeUntilSpread;
        public bool HasSpread;
        public bool Queued;
    }

    [Serializable]
    public sealed class EventState
    {
        public string Data;
        public float Duration;
        public float Remaining;
    }

    [Serializable]
    public sealed class SessionState
    {
        public bool HasEverHadPopulation;
        public int MaxPopulation;
        public int EventsExperienced;
        public int ModulesDestroyed;
        public int DamageSpreads;
        public int MeteorsIntercepted;
        public int MeteorsBlocked;
        public int Ricochets;
    }

    [Serializable]
    public sealed class ProjectState
    {
        public string Category;
        public int TargetLevel;
        public float Progress;
    }

    [Serializable]
    public sealed class AutomationState
    {
        public bool AutoMaintain = true;
        public bool AutoRebuild = true;
        public float Threshold = MaintenanceAutomation.DefaultThreshold;
        public float ReserveRatio = MaintenanceAutomation.DefaultReserveRatio;
        public int AutoMaintainCount;
        public int AutoRebuildCount;
    }
}
