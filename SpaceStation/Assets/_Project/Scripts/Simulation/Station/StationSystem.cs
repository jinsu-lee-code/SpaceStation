using System;
using System.Collections.Generic;
using SpaceStation.Core;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 12-0b 정거장 시스템 공통 틀 (사용자 결정 2026-10-12, C-6): Phase 12의 새 시스템(합성 · 사회 위기 · 환경 위기 등)은
    /// 이 클래스를 상속해 <see cref="StationSimulation.AddSystem"/>로 끼운다. 틱 순서 · 모듈 배치/철거 알림 · 세이브가 자동으로 연결된다.
    /// 기존 시스템(자원 · 파손 · 내구도 …)은 그대로 두고 틱 단계 목록(<see cref="StationSimulation.TickSteps"/>)에 이름으로 올라가 있다.
    /// </summary>
    public abstract class StationSystem
    {
        /// <summary>세이브 키 (시스템마다 고유, 바꾸면 이전 세이브의 이 시스템 상태를 못 읽음).</summary>
        public abstract string Key { get; }

        /// <summary>붙은 시뮬레이션 (<see cref="StationSimulation.AddSystem"/> 때 정해짐).</summary>
        public StationSimulation Sim { get; private set; }

        internal void Attach(StationSimulation sim)
        {
            if (Sim != null)
                throw new InvalidOperationException($"{Key}: 이미 다른 시뮬레이션에 붙어 있음");
            Sim = sim;
            OnAttached();
        }

        /// <summary>붙은 직후 (다른 시스템 · 이벤트 구독 준비).</summary>
        protected virtual void OnAttached() { }

        /// <summary>틱 단계에서 매 틱 (dt = 게임 초, 세이브 복원 직후 0초 틱도 옴).</summary>
        public virtual void Tick(float dt) { }

        /// <summary>모듈이 배치됨 (연결 · 수용량 재계산 뒤).</summary>
        public virtual void OnModulePlaced(ModuleInstance module) { }

        /// <summary>모듈이 철거 · 파괴됨 (연결 · 수용량 재계산 뒤).</summary>
        public virtual void OnModuleRemoved(ModuleInstance module) { }

        /// <summary>세이브: 상태를 JSON 등 문자열로 (없으면 null). 모듈은 indexOf로 순번을 받아 저장한다 (코어 -2, 없음 -1).</summary>
        public virtual string Capture(Func<ModuleInstance, int> indexOf) => null;

        /// <summary>세이브 복원: 모듈 · 재고 · 파손 · 이벤트 복원 뒤, 파생값 재계산(0초 틱) 전에 부름. moduleAt(순번) → 모듈 (없으면 null).</summary>
        public virtual void Restore(string state, Func<int, ModuleInstance> moduleAt) { }
    }

    /// <summary>12-0b 틱 단계 하나 (이름 = 끼워 넣을 위치를 가리킬 때 씀).</summary>
    public sealed class TickStep
    {
        public string Name { get; }
        internal Action<float> Run { get; }

        internal TickStep(string name, Action<float> run)
        {
            Name = name;
            Run = run;
        }
    }

    /// <summary>12-0b 틱 단계 이름 (기존 순서 — 바꾸면 결과가 바뀐다).</summary>
    public static class TickStepNames
    {
        public const string Damage = "damage";
        public const string Durability = "durability";
        public const string Resources = "resources";
        public const string Incidents = "incidents";
        public const string Research = "research";
        public const string Cargo = "cargo";
        public const string Automation = "automation";
        public const string Needs = "needs";
        public const string Population = "population";
        public const string Events = "events";
        public const string Clock = "clock";
        public const string Tutorial = "tutorial";
        public const string Progression = "progression";
        public const string Failure = "failure";
    }

    /// <summary>12-0b 세이브: 공통 틀 시스템 하나의 상태.</summary>
    [Serializable]
    public sealed class SystemState
    {
        public string Key;
        public string State;
    }

    internal static class TickStepList
    {
        /// <summary>after 이름 뒤에 끼움 (null이면 맨 뒤, 없는 이름이면 예외).</summary>
        public static void Insert(List<TickStep> steps, TickStep step, string after)
        {
            if (after == null)
            {
                steps.Add(step);
                return;
            }
            int i = steps.FindIndex(s => s.Name == after);
            if (i < 0)
                throw new ArgumentException($"틱 단계 없음: {after}", nameof(after));
            steps.Insert(i + 1, step);
        }
    }
}
