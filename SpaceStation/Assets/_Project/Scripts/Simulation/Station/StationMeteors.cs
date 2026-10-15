using System;
using System.Collections.Generic;
using System.Text;
using SpaceStation.Core;
using SpaceStation.Data;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 12-0b (StationSimulation에서 분리): 운석 — 대상 고르기(등급별 개수 · 노출 가중 · 장갑 격벽 끌어오기) → 포탑 격추 · 실드 빗겨냄 · 튕김 판정 →
    /// 파손 · 내구도 피해, 그리고 조기 경보 중 예정 대상(방어 연구). 난수는 시뮬레이션 공용 난수를 같은 순서로 쓴다.
    /// </summary>
    internal sealed class StationMeteors
    {
        private readonly StationSimulation _sim;
        private readonly Func<float> _random01;
        private readonly List<ModuleInstance> _candidates = new List<ModuleInstance>();
        private readonly List<ModuleInstance> _targetsCopy = new List<ModuleInstance>();
        private readonly HashSet<ModuleInstance> _immuneHits = new HashSet<ModuleInstance>();
        private readonly List<ModuleInstance> _ricochetCandidates = new List<ModuleInstance>();
        private readonly List<ModuleInstance> _plannedTargets = new List<ModuleInstance>();
        private readonly List<ModuleInstance> _planBuffer = new List<ModuleInstance>();
        private int _plannedHits;
        private int _forcedHits;

        public event Action<ModuleInstance> ShieldDeflected;
        public event Action<MeteorFlight> MeteorResolved;
        public event Action MeteorPlanChanged;

        public StationMeteors(StationSimulation sim, Func<float> random01)
        {
            _sim = sim;
            _random01 = random01;
        }

        public IReadOnlyList<ModuleInstance> PlannedTargets => _plannedTargets;
        public int PlannedHits => _plannedHits;

        public void ForceNextHits(int hits) => _forcedHits = Math.Max(0, hits);

        // ---------------- 조기 경보 (Phase 6 방어 연구) ----------------

        public void HandleUpcomingChanged(GameEventData upcoming)
        {
            bool had = _plannedHits > 0;
            _plannedTargets.Clear();
            _plannedHits = 0;
            if (upcoming is MeteorEventData && _sim.Effects.MeteorTargetPreview)
            {
                _plannedHits = RollHits();
                _sim.Damage.PickMeteorTargets(_sim.Grid, _sim.Core, _plannedHits, _random01, _plannedTargets);
                _sim.Defense.RedirectToDecoys(_sim.Grid, _plannedTargets, _random01); // 8-5 장갑 격벽 (예정 표시에도 반영)
            }
            if (had || _plannedHits > 0)
                MeteorPlanChanged?.Invoke();
        }

        /// <summary>예정 대상이 철거 · 재건축되면 다른 모듈로 다시 뽑음.</summary>
        public void OnModuleRemoved(ModuleInstance module)
        {
            if (_plannedHits > 0 && _plannedTargets.Contains(module))
            {
                RefillPlannedTargets();
                MeteorPlanChanged?.Invoke();
            }
        }

        private int RollHits()
        {
            var grade = _sim.EventScale; // 13-2 규모를 정하는 쪽이 있으면 그쪽
            int span = grade.MeteorHitsMax - grade.MeteorHitsMin + 1;
            return grade.MeteorHitsMin + Math.Min(span - 1, (int)(_random01() * span));
        }

        /// <summary>예정 대상이 사라지거나(철거·파괴) 맞을 수 없게 되면(파손·내부로 묻힘) 남은 후보에서 다시 뽑아 개수를 채운다.</summary>
        private void RefillPlannedTargets()
        {
            var grid = _sim.Grid;
            for (int i = _plannedTargets.Count - 1; i >= 0; i--)
            {
                var m = _plannedTargets[i];
                if (!grid.TryGetModule(m.Origin, out var placed) || placed != m || _sim.Damage.IsDamaged(m) || !DamageSystem.IsExterior(grid, m))
                    _plannedTargets.RemoveAt(i);
            }
            if (_plannedTargets.Count >= _plannedHits)
                return;
            _sim.Damage.FindMeteorCandidates(grid, _sim.Core, _planBuffer);
            _planBuffer.RemoveAll(_plannedTargets.Contains);
            while (_plannedTargets.Count < _plannedHits && _planBuffer.Count > 0)
            {
                var pick = _sim.Damage.PickWeighted(grid, _planBuffer, _random01);
                if (pick == null)
                    break;
                _plannedTargets.Add(pick);
                _planBuffer.Remove(pick);
            }
        }

        public void RestorePlan(int hits, IEnumerable<ModuleInstance> targets)
        {
            _plannedTargets.Clear();
            _plannedHits = Math.Max(0, hits);
            if (_plannedHits == 0)
                return;
            foreach (var t in targets)
                if (t != null && !_plannedTargets.Contains(t))
                    _plannedTargets.Add(t);
            RefillPlannedTargets();
        }

        // ---------------- 운석 한 번 ----------------

        /// <summary>
        /// 4-1: 등급별 개수만큼 외곽 모듈을 노출 면 수로 가중해 서로 다르게 선택 (분산 타격).
        /// 조기 경보로 대상을 미리 정해 두었으면 그 대상 (사라진 대상은 다시 뽑아 채움).
        /// </summary>
        public void Apply()
        {
            var grid = _sim.Grid;
            var damage = _sim.Damage;
            if (_plannedHits > 0)
            {
                RefillPlannedTargets();
                _candidates.Clear();
                _candidates.AddRange(_plannedTargets);
                _plannedTargets.Clear();
                _plannedHits = 0;
                MeteorPlanChanged?.Invoke();
            }
            else
            {
                int hits = _forcedHits > 0 ? _forcedHits : RollHits(); // 11-18 튜토리얼 운석은 1개
                _forcedHits = 0;
                damage.PickMeteorTargets(grid, _sim.Core, hits, _random01, _candidates);
                _sim.Defense.RedirectToDecoys(grid, _candidates, _random01); // 8-5 장갑 격벽 끌어오기
            }
            if (_candidates.Count == 0)
            {
                _sim.Report("운석이 정거장을 빗나갔습니다", true);
                return;
            }
            Strike(_random01, UnityEngine.Vector3Int.zero, "");
        }

        /// <summary>
        /// 12-3c 유성우 한 파: 정해진 대상(오는 방향에서 보이는 앞면 모듈)에 일반 운석과 같은 판정(격추 · 빗겨냄 · 튕김 · 피해).
        /// 난수는 유성우 전용 (일반 이벤트 난수 순서를 바꾸지 않게).
        /// </summary>
        public void Shower(IReadOnlyList<ModuleInstance> targets, Func<float> random01, UnityEngine.Vector3Int incoming, string label)
        {
            _candidates.Clear();
            foreach (var t in targets)
                _candidates.Add(t);
            if (_candidates.Count == 0)
            {
                _sim.Report($"{label}: 맞을 모듈 없음", true);
                return;
            }
            Strike(random01, incoming, label + ": ");
        }

        /// <summary>_candidates에 운석을 날림 (4-8 판정 → 피해 → 알림). prefix = 알림 앞머리 (일반 운석은 빈 문자열).</summary>
        private void Strike(Func<float> random01, UnityEngine.Vector3Int incoming, string prefix)
        {
            var damage = _sim.Damage;
            var session = _sim.Session;
            var balance = _sim.Balance;

            // 4-8: 포탑 격추(완전 제거) → 실드 빗겨냄(일부는 범위 밖 모듈로 튕김). 피해 적용 전에 모두 판정
            _targetsCopy.Clear();
            int intercepted = 0, deflected = 0, ricochets = 0;
            foreach (var target in _candidates)
            {
                var flight = new MeteorFlight { Target = target, Incoming = incoming };
                var hit = Resolve(target, true, ref intercepted, ref deflected, ref ricochets, flight, random01);
                flight.Hit = hit;
                // Phase 6 방어 연구: 명중해도 일정 확률로 파손 면역 (확률이 0이면 난수를 쓰지 않아 기존 결과 유지)
                if (hit != null && _sim.Effects.HitImmunityChance > 0f && random01() < _sim.Effects.HitImmunityChance)
                {
                    flight.Immune = true;
                    _immuneHits.Add(hit);
                }
                MeteorResolved?.Invoke(flight);
                if (hit != null)
                    _targetsCopy.Add(hit); // 내구도 0으로 파괴되면 목록이 바뀔 수 있어 복사
            }
            session.RecordIntercepted(intercepted);
            session.RecordShieldBlocked(deflected);
            session.RecordRicochet(ricochets);
            string interceptText = (intercepted > 0 ? $"포탑 격추 {intercepted} · " : "")
                                   + (deflected > 0 ? $"실드 빗겨냄 {deflected}" + (ricochets > 0 ? $"(튕겨서 {ricochets}개 명중)" : "") + " · " : "");
            if (_targetsCopy.Count == 0)
            {
                _sim.Report($"{prefix}운석 {_candidates.Count}개 접근 · {interceptText}피해 없음", true);
                return;
            }

            var sb = new StringBuilder();
            int immune = 0;
            foreach (var target in _targetsCopy)
            {
                bool isImmune = _immuneHits.Contains(target);
                if (!isImmune)
                    damage.Damage(target);
                else
                    immune++;
                _sim.Durability.ApplyImpact(target, balance.MeteorDurabilityDamage); // 4-3: 내구도도 깎음
                if (isImmune)
                    continue;
                if (sb.Length > 0)
                    sb.Append(", ");
                sb.Append(target.Data != null ? target.Data.DisplayName : target.ToString());
            }
            _immuneHits.Clear();
            if (immune > 0)
                interceptText += $"방어 연구로 파손 면함 {immune} · ";
            if (sb.Length == 0)
            {
                _sim.Report($"{prefix}운석 {_candidates.Count}개 충돌 · {interceptText}파손 없음", true);
                return;
            }
            string head = prefix + (_candidates.Count > 1 ? $"운석 {_candidates.Count}개 충돌! " : "") + interceptText + "파손: ";
            string spread = balance.SpreadAfterSeconds > 0f ? $", {balance.SpreadAfterSeconds:0}초 방치 시 이웃으로 확산" : "";
            _sim.Report($"{head}{sb}  ·  {balance.DestroyAfterSeconds:0}초 안에 수리 (선택 후 R){spread}", false);
        }

        /// <summary>
        /// 운석 1발의 결과 (4-8). 맞을 모듈을 돌려주고, 격추·빗겨냄으로 피해가 없으면 null.
        /// 실드가 빗겨내면 튕김 확률로 그 실드 범위 밖 외곽 모듈(노출 가중, 이번 운석 무리에서 이미 맞을 곳 제외)을 다시 판정한다.
        /// 튕긴 운석도 포탑 격추는 받지만, 다른 실드에 또 막히면 우주로 (튕김 1회).
        /// </summary>
        private ModuleInstance Resolve(ModuleInstance target, bool canRicochet, ref int intercepted, ref int deflected, ref int ricochets,
            MeteorFlight flight, Func<float> random01)
        {
            var grid = _sim.Grid;
            var defense = _sim.Defense;
            float turret = defense.GetInterceptChance(grid, target);
            if (turret > 0f && random01() < turret)
            {
                intercepted++;
                flight.InterceptedAt = target;
                return null;
            }
            float shield = defense.GetShieldBlockChance(grid, target, out var shieldModule);
            if (shield <= 0f || random01() >= shield)
                return target;

            deflected++;
            ShieldDeflected?.Invoke(shieldModule);
            if (flight.DeflectedBy == null)
                flight.DeflectedBy = shieldModule;
            if (!canRicochet || random01() >= _sim.Effects.RicochetChance)
                return null; // 우주로

            _sim.Damage.FindMeteorCandidates(grid, _sim.Core, _ricochetCandidates);
            for (int i = _ricochetCandidates.Count - 1; i >= 0; i--)
            {
                var c = _ricochetCandidates[i];
                if (defense.IsInShieldRange(shieldModule, c) || _targetsCopy.Contains(c) || _candidates.Contains(c))
                    _ricochetCandidates.RemoveAt(i);
            }
            var next = _sim.Damage.PickWeighted(grid, _ricochetCandidates, random01);
            if (next == null)
                return null; // 튕길 곳 없음 → 우주로
            flight.RicochetTarget = next;
            var hit = Resolve(next, false, ref intercepted, ref deflected, ref ricochets, flight, random01);
            if (hit != null)
                ricochets++;
            return hit;
        }
    }
}
