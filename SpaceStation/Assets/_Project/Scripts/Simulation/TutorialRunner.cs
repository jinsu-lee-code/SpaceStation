using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// Phase 9 튜토리얼 진행 (순수 C#). <see cref="StationSimulation.StartTutorial"/>로 만들고 매 틱 시뮬레이션이 진행시킨다.
    /// - 진행 중에는 무작위 이벤트 타이머를 멈춘다 (<see cref="EventScheduler.Held"/>). 끝나면 유예 시간 뒤부터 다시.
    /// - 첫 밤 대비 단계가 남아 있으면 해질녘 직전에 시간을 멈춘다 (<see cref="HoldsTime"/>, 시뮬레이션 틱이 건너뜀).
    /// - 다음에 지을 모듈(또는 수리) 비용이 일정 시간 모자라면 부족분을 보급한다.
    /// - 수리 단계: 시작하고 잠시 뒤 정해진 운석 1발(모듈 하나만, 11-18) → 맞은 모듈을 모두 고치면 완료.
    /// - 11-18: 진행 중에는 노후화 · 파손 확산 · 파괴 타이머가 멈춘다 (<see cref="StationSimulation"/>가 <see cref="Active"/>를 봄).
    ///   정비 단계는 모듈 하나를 일부러 낡게 해 두고 정비하면 완료. 단계에 해당하는 조작만 허용 (<see cref="Allows"/>).
    /// 카메라 조작·배속은 화면 쪽(TutorialView)이 <see cref="ReportCamera"/>, <see cref="ReportSpeed"/>로 알린다.
    /// - 11-18 내부 단계(들어가기 · 패드 · 부탁 · 사고)는 내부 쪽(InteriorMode)이 <see cref="ReportInside"/>, <see cref="ReportInterior"/>로 알리고 바로 판정.
    /// </summary>
    public sealed class TutorialRunner
    {
        [Flags]
        public enum CameraUse { None = 0, Rotate = 1, Zoom = 2, Move = 4, All = 7 }

        private readonly StationSimulation _sim;
        private readonly TutorialData _data;
        private readonly List<ResourceAmount> _supplied = new List<ResourceAmount>();

        public event Action<int> StepChanged;
        /// <summary>단계 하나 완료 (완료한 단계 번호).</summary>
        public event Action<int> StepCompleted;
        /// <summary>부족분 보급 (자원별 양).</summary>
        public event Action<IReadOnlyList<ResourceAmount>> Supplied;
        /// <summary>튜토리얼 종료 (건너뛰기면 true).</summary>
        public event Action<bool> Finished;
        /// <summary>시간 정지 상태가 바뀜.</summary>
        public event Action<bool> HoldChanged;

        public TutorialData Data => _data;
        public bool Active { get; private set; } = true;
        public bool Skipped { get; private set; }
        public int StepIndex { get; private set; }
        public TutorialStep Current => Active && StepIndex < _data.Steps.Count ? _data.Steps[StepIndex] : null;
        public int StepCount => _data.Steps.Count;

        public CameraUse CameraDone { get; private set; }
        public bool SpeedUsed { get; private set; }
        public int NightsPassed { get; private set; }
        public bool MeteorFired { get; private set; }
        /// <summary>현재 단계에서 흐른 게임 시간 (운석 대기).</summary>
        public float StepElapsed { get; private set; }
        /// <summary>다음 비용이 모자란 채로 흐른 시간 (보급 대기).</summary>
        public float ShortageElapsed { get; private set; }
        public bool HoldsTime { get; private set; }

        /// <summary>11-18: 플레이어가 1인칭 내부에 있음 (내부 쪽이 <see cref="ReportInside"/>로 알림).</summary>
        public bool InsideInterior { get; private set; }

        private bool _wasNight;
        private ModuleInstance _maintainTarget;
        private bool _interiorDone;

        /// <summary>11-18 정비 단계에서 정비할 모듈 (없으면 null).</summary>
        public ModuleInstance MaintainTarget => _maintainTarget;

        internal TutorialRunner(StationSimulation sim, TutorialData data)
        {
            _sim = sim ?? throw new ArgumentNullException(nameof(sim));
            _data = data ?? throw new ArgumentNullException(nameof(data));
            _sim.Events.Held = true;
            _wasNight = !_sim.DayNight.IsDay(_sim.ElapsedSeconds);
            if (_data.Steps.Count == 0)
                Finish(false);
        }

        // ---------------- 화면 쪽 보고 ----------------

        public void ReportCamera(CameraUse use)
        {
            if (!Active)
                return;
            CameraDone |= use;
        }

        public void ReportSpeed(float speed)
        {
            if (Active && speed >= _data.RequiredSpeed - 1e-3f)
                SpeedUsed = true;
        }

        /// <summary>
        /// 11-18 내부 단계: 내부에서는 기본 설정상 시간이 멈춰 틱이 돌지 않으므로, 보고를 받으면 바로 판정한다.
        /// 들어가기 단계는 들어간 순간 완료.
        /// </summary>
        public void ReportInside(bool inside)
        {
            InsideInterior = inside;
            if (inside)
                ReportInterior(TutorialGoal.EnterInterior);
        }

        /// <summary>11-18: 지금 단계가 이 내부 목표면 완료 (패드 들기 · 부탁 · 사고 처리).</summary>
        public void ReportInterior(TutorialGoal goal)
        {
            var step = Current;
            if (step == null || step.Goal != goal)
                return;
            _interiorDone = true;
            Advance();
        }

        /// <summary>11-18: 내부에 있어야 진행되는 단계 (패드 · 부탁 · 사고).</summary>
        public static bool NeedsInside(TutorialGoal goal) =>
            goal == TutorialGoal.InteriorPad || goal == TutorialGoal.InteriorRequest || goal == TutorialGoal.InteriorIncident;

        public void Skip()
        {
            if (Active)
                Finish(true);
        }

        // ---------------- 진행 ----------------

        /// <summary>이번 틱에 시간을 멈출지 (시뮬레이션 틱 시작 때 판정).</summary>
        internal bool UpdateHold()
        {
            bool hold = Active && NeedsNightHold();
            if (hold != HoldsTime)
            {
                HoldsTime = hold;
                HoldChanged?.Invoke(hold);
            }
            return hold;
        }

        private bool NeedsNightHold()
        {
            var day = _sim.DayNight;
            if (!day.Enabled || _sim.ElapsedSeconds >= day.Period || !day.IsDay(_sim.ElapsedSeconds))
                return false; // 첫 낮에만
            if (_sim.ElapsedSeconds < day.DayLength - day.Transition - _data.NightHoldLead)
                return false;
            for (int i = StepIndex; i < _data.Steps.Count; i++)
                if (_data.Steps[i].HoldBeforeFirstNight)
                    return true;
            return false;
        }

        /// <param name="dt">게임 시간 (시간 정지 중에도 보급 대기용으로 흐른다).</param>
        internal void Tick(float dt)
        {
            if (!Active)
                return;
            bool night = !_sim.DayNight.IsDay(_sim.ElapsedSeconds);
            if (_wasNight && !night)
                NightsPassed++;
            _wasNight = night;

            var step = Current;
            if (step == null)
                return;
            StepElapsed += dt;
            if (step.Goal == TutorialGoal.Repair && !MeteorFired && StepElapsed >= _data.MeteorDelay)
                FireMeteor();
            if (step.Goal == TutorialGoal.Maintain && !IsMaintainTargetValid())
                PrepareMaintain();

            if (IsComplete(step))
            {
                Advance();
                return;
            }
            UpdateSupply(step, dt);
        }

        private void FireMeteor()
        {
            MeteorFired = true;
            foreach (var e in _sim.EventPool)
            {
                if (!(e is MeteorEventData))
                    continue;
                _sim.ForceNextMeteorHits(1); // 11-18: 튜토리얼 운석은 모듈 하나만 (한 번에 5개가 파손돼 무너졌음)
                if (_sim.Events.Trigger(e))
                    return;
                _sim.ForceNextMeteorHits(0);
            }
        }

        // ---------------- 정비 (11-18) ----------------

        private bool IsMaintainTargetValid() =>
            _maintainTarget != null && _sim.Grid.TryGetModule(_maintainTarget.Origin, out var placed) && placed == _maintainTarget
            && _sim.Durability.TryGetInfo(_maintainTarget, out _);

        /// <summary>정비할 모듈을 고르고 낡게 만듦: 채굴 도킹(첫 건설) → 없으면 코어가 아닌 첫 모듈.</summary>
        private void PrepareMaintain()
        {
            _maintainTarget = null;
            foreach (var m in _sim.Grid.Modules)
            {
                if (m == _sim.Core || !_sim.Durability.TryGetInfo(m, out _) || _sim.Damage.IsDamaged(m))
                    continue;
                if (_maintainTarget == null || (m.Data != null && m.Data.Category == ModuleCategory.Industry && _maintainTarget.Data.Category != ModuleCategory.Industry))
                    _maintainTarget = m;
            }
            if (_maintainTarget == null || !_sim.Durability.TryGetInfo(_maintainTarget, out var info))
                return;
            float drop = info.Current - _data.MaintainWornDurability;
            if (drop > 0f)
                _sim.Durability.ApplyImpact(_maintainTarget, drop);
        }

        /// <summary>정비 완료: 효율 저하 기준(50) 위로 회복.</summary>
        private bool IsMaintained() =>
            IsMaintainTargetValid() && _sim.Durability.TryGetInfo(_maintainTarget, out var info)
            && info.Current >= _sim.Balance.DurabilityEfficiencyThreshold + 10f;

        // ---------------- 허용 조작 (11-18) ----------------

        /// <summary>
        /// 튜토리얼 중에는 지금 단계에 해당하는 조작만 (사용자 결정 2026-10-11): 건설 단계 = 그 단계의 남은 모듈만, 수리 · 정비 단계 = 그 조작만,
        /// 배속 = 밤 넘기기 단계부터. 철거 · 재건축 · 연구 · 주민 · 내부 들어가기는 끝나기 전엔 막음. 끝났거나 건너뛰면 모두 허용.
        /// </summary>
        public bool Allows(TutorialAction action, ModuleData data = null)
        {
            var step = Current;
            if (!Active || step == null)
                return true;
            switch (action)
            {
                case TutorialAction.Build:
                    return step.Goal == TutorialGoal.Build && data != null && CountActive(data) < Required(step, data);
                case TutorialAction.Repair:
                    return step.Goal == TutorialGoal.Repair;
                case TutorialAction.Maintain:
                    return step.Goal == TutorialGoal.Maintain;
                case TutorialAction.EnterInterior:
                    return step.Goal == TutorialGoal.EnterInterior || NeedsInside(step.Goal); // 나왔으면 다시 들어가기
                case TutorialAction.Speed:
                    for (int i = 0; i <= StepIndex && i < _data.Steps.Count; i++)
                    {
                        if (_data.Steps[i].Goal == TutorialGoal.SurviveNight)
                            return true;
                    }
                    return false;
                default:
                    return false;
            }
        }

        /// <summary>건설 단계에서 이 모듈을 몇 개 지어야 하는지 (목록에 넣은 횟수).</summary>
        public static int Required(TutorialStep step, ModuleData data)
        {
            int n = 0;
            foreach (var m in step.Modules)
            {
                if (m == data)
                    n++;
            }
            return n;
        }

        public bool IsComplete(TutorialStep step)
        {
            switch (step.Goal)
            {
                case TutorialGoal.Camera:
                    return (CameraDone & CameraUse.All) == CameraUse.All;
                case TutorialGoal.Build:
                    foreach (var m in step.Modules)
                        if (m != null && CountActive(m) < Required(step, m))
                            return false;
                    return true;
                case TutorialGoal.SurviveNight:
                    return SpeedUsed && NightsPassed > 0;
                case TutorialGoal.Repair:
                    return MeteorFired && _sim.Damage.DamagedModules.Count == 0;
                case TutorialGoal.Maintain:
                    return IsMaintained();
                case TutorialGoal.EnterInterior:
                case TutorialGoal.InteriorPad:
                case TutorialGoal.InteriorRequest:
                case TutorialGoal.InteriorIncident:
                    return _interiorDone;
                default:
                    return true;
            }
        }

        /// <summary>코어에 연결된 해당 모듈 수.</summary>
        public int CountActive(ModuleData data)
        {
            int n = 0;
            foreach (var m in _sim.Grid.Modules)
                if (m.Data == data && _sim.Connectivity.IsActive(m))
                    n++;
            return n;
        }

        /// <summary>건설 단계에서 아직 짓지 않은 첫 모듈 (없으면 null).</summary>
        public ModuleData NextModule(TutorialStep step)
        {
            if (step == null || step.Goal != TutorialGoal.Build)
                return null;
            foreach (var m in step.Modules)
                if (m != null && CountActive(m) < Required(step, m))
                    return m;
            return null;
        }

        private void Advance()
        {
            int done = StepIndex;
            StepIndex++;
            StepElapsed = 0f;
            ShortageElapsed = 0f;
            _interiorDone = false;
            StepCompleted?.Invoke(done);
            if (StepIndex >= _data.Steps.Count)
            {
                Finish(false);
                return;
            }
            StepChanged?.Invoke(StepIndex);
            Tick(0f); // 이미 끝난 단계면 바로 넘김 (예: 밤을 이미 넘김)
        }

        private void Finish(bool skipped)
        {
            Active = false;
            Skipped = skipped;
            UpdateHold();
            _sim.Events.Held = false;
            _sim.Events.Postpone(_data.PostTutorialGrace);
            Finished?.Invoke(skipped);
        }

        // ---------------- 보급 ----------------

        /// <summary>지금 단계를 진행하는 데 필요한 다음 비용 (없으면 null).</summary>
        public List<ResourceAmount> NextCost(TutorialStep step)
        {
            if (step == null)
                return null;
            if (step.Goal == TutorialGoal.Build)
            {
                var next = NextModule(step);
                return next != null ? _sim.GetBuildCost(next) : null;
            }
            if (step.Goal == TutorialGoal.Repair)
            {
                foreach (var info in _sim.Damage.DamagedModules)
                    if (!info.IsRepairing && !info.IsQueued)
                        return _sim.Damage.GetRepairCost(info.Module);
            }
            if (step.Goal == TutorialGoal.Maintain && IsMaintainTargetValid() && !IsMaintained())
                return _sim.Durability.GetMaintenanceCost(_maintainTarget);
            return null;
        }

        private void UpdateSupply(TutorialStep step, float dt)
        {
            var cost = NextCost(step);
            if (cost == null || _sim.Resources.CanAfford(cost))
            {
                ShortageElapsed = 0f;
                return;
            }
            ShortageElapsed += dt;
            if (ShortageElapsed < _data.SupplyDelay)
                return;
            ShortageElapsed = 0f;
            _supplied.Clear();
            foreach (var a in cost)
            {
                float lack = a.Amount - _sim.Resources.GetStock(a.Type);
                if (lack <= 0f)
                    continue;
                float added = _sim.Resources.AddStock(a.Type, lack + _data.SupplyMargin);
                if (added > 0f)
                    _supplied.Add(new ResourceAmount(a.Type, added));
            }
            if (_supplied.Count > 0)
                Supplied?.Invoke(_supplied);
        }

        // ---------------- 세이브 ----------------

        internal TutorialState Capture() => new TutorialState
        {
            Active = Active,
            Step = StepIndex,
            StepElapsed = StepElapsed,
            CameraDone = (int)CameraDone,
            SpeedUsed = SpeedUsed,
            NightsPassed = NightsPassed,
            MeteorFired = MeteorFired,
        };

        internal void Restore(TutorialState s)
        {
            if (s == null || !s.Active)
            {
                if (Active)
                    Finish(true);
                return;
            }
            StepIndex = Math.Max(0, Math.Min(_data.Steps.Count, s.Step));
            StepElapsed = s.StepElapsed;
            CameraDone = (CameraUse)s.CameraDone;
            SpeedUsed = s.SpeedUsed;
            NightsPassed = s.NightsPassed;
            MeteorFired = s.MeteorFired;
            _wasNight = !_sim.DayNight.IsDay(_sim.ElapsedSeconds);
            if (StepIndex >= _data.Steps.Count)
                Finish(false);
        }
    }

    [Serializable]
    public sealed class TutorialState
    {
        public bool Active;
        public int Step;
        public float StepElapsed;
        public int CameraDone;
        public bool SpeedUsed;
        public int NightsPassed;
        public bool MeteorFired;
    }
}
