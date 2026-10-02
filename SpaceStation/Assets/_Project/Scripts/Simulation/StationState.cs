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

        public bool ReachedFinalGrade;
        public SessionState Session = new SessionState();
        public float OxygenFailTimer;
        public float SatisfactionFailTimer;
        public float CoreFailTimer;

        public List<NamedValue> ResearchLevels = new List<NamedValue>();
        /// <summary>진행 중인 연구 (순서 = 연구소가 모자랄 때 멈추는 우선순위).</summary>
        public List<ProjectState> Projects = new List<ProjectState>();
        public AutomationState Automation = new AutomationState();
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
