using System.Collections.Generic;
using SpaceStation.Audio;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 사운드 개편 D-10 (2026-11-26, SOUND_LIST_NEW 4-4 hull_* · light_flicker · radio_static · dock_clank): 1인칭 내부에 있는 동안 바깥 일이 선체 너머로 먹먹하게 들림.
    /// 그 일이 난 모듈의 내부 자리 (그 모듈 칸 중 플레이어에게 가장 가까운 칸 가운데)에서 3D로 — 맞은 모듈이 가까울수록 크게 (직선 감쇠 8 m까지 그대로 → 60 m에서 0 — 바로 옆 방까지 그대로, 대여섯 방 너머는 안 들림).
    /// - 운석 충돌 = 선체 "쿵 · 달그락" (모듈을 맞힌 것만 — 실드 · 공중 폭발은 선체에 안 닿음) + 가까운 큰 충돌 · 유성우 몰아침 = 낮은 울림 한 번 (D-2).
    ///   바깥 운석 소리 (GameAudio — 1인칭 내부에서 절반 · 가운데)는 "일이 났다", 이 소리는 "어디서 · 얼마나 가까이"를 맡음.
    /// - 태양 폭풍 = 방 불빛이 번쩍이는 순간 조명 "지직" (<see cref="InteriorAtmosphere.StormFlashed"/>, 몇 초에 한 번까지) + 가끔 방 스피커 무전 잡음 (2D).
    /// - 화물선 · 보급선 · 이민선 도킹 = 선체로 전해지는 "철컹" — 그 모듈 방에 있을 때는 바깥 물건 쪽 (GameAudio · InteriorSupply) 소리가 바로 벽 너머라 여기선 안 냄.
    /// - 근처 합성 = 벽 너머 공사 "쿵" (가볍게, 7 ~ 15초마다) · 합성 완료 = 낮은 "철컹".
    /// GameAudio가 붙이고 묶음 (판정은 바꾸지 않음).
    /// </summary>
    public sealed class InteriorOutsideSound : MonoBehaviour
    {
        private const float Near = 8f, Far = 60f;           // 내부 칸 하나 = 8 m — 바로 옆 방까지 그대로, 7칸 너머 0
        private const float CargoDockDelay = 1.5f;          // 화물선이 멈추는 "쿵" (GameAudio와 같은 때)
        private const float SupplyDockDelay = 5.0f;         // 보급선이 정거장 옆에 붙는 때 (AmbientSpace.SupplyShip)
        private const float BuildRange = 40f;               // 합성 공사가 들리는 거리

        private StationController _station;
        private MeteorFx _meteors;
        private SolarStormFx _storm;
        private InteriorMode _mode;
        private InteriorAtmosphere _atmosphere;
        private readonly List<float> _impacts = new List<float>();
        private float _rumbleAt = -99f, _flickerNext, _radioNext, _buildNext;
        private int _visits = -1, _lastCrate = -1;

        private StationSimulation Sim => _station != null ? _station.Simulation : null;
        private bool Inside => _mode != null && _mode.IsInside && Sim != null;

        public void Bind(StationController station, MeteorFx meteors, SolarStormFx storm, InteriorMode mode)
        {
            _station = station;
            _meteors = meteors;
            _storm = storm;
            _mode = mode;
            if (_meteors != null)
                _meteors.Impacted += HandleImpacted;
            var sim = Sim;
            if (sim != null)
            {
                sim.Cargo.Delivered += HandleCargo;
                sim.Supply.Changed += HandleSupply;
                sim.Synthesis.Completed += HandleSynthCompleted;
                foreach (var crate in sim.Supply.Crates)
                    _lastCrate = Mathf.Max(_lastCrate, crate.Id);
            }
        }

        private void OnDestroy()
        {
            if (_meteors != null)
                _meteors.Impacted -= HandleImpacted;
            var sim = Sim;
            if (sim != null)
            {
                sim.Cargo.Delivered -= HandleCargo;
                sim.Supply.Changed -= HandleSupply;
                sim.Synthesis.Completed -= HandleSynthCompleted;
            }
            if (_atmosphere != null)
                _atmosphere.StormFlashed -= HandleStormFlash;
        }

        // ---------------- 자리 ----------------

        /// <summary>그 모듈 칸 중 플레이어 (리스너)에게 가장 가까운 칸 가운데 — 내부 공간 좌표.</summary>
        private bool TryRoomPoint(IEnumerable<Vector3Int> cells, out Vector3 point, out float distance)
        {
            point = default;
            distance = float.MaxValue;
            var builder = _mode != null ? _mode.Builder : null;
            var cam = Camera.main;
            if (builder == null || cam == null || cells == null)
                return false;
            var ear = cam.transform.position;
            foreach (var c in cells)
            {
                var p = builder.CellCenter(c);
                float d = Vector3.Distance(p, ear);
                if (d < distance)
                {
                    distance = d;
                    point = p;
                }
            }
            return distance < float.MaxValue;
        }

        private void PlayFar(System.Func<SoundLibrary, SoundCue> pick, IEnumerable<Vector3Int> cells, float volume, float pitch = 1f, float delay = 0f, float far = Far)
        {
            var svc = AudioService.Instance;
            if (svc == null || svc.Library == null || !TryRoomPoint(cells, out var at, out _))
                return;
            svc.PlayAt(pick(svc.Library), at, volume, Near, far, pitch, delay);
        }

        // ---------------- 운석 ----------------

        private void HandleImpacted(Vector3 _, ModuleInstance hit)
        {
            var svc = AudioService.Instance;
            if (!Inside || hit == null || svc == null || !TryRoomPoint(hit.Cells, out var at, out float distance))
                return;
            var lib = svc.Library;
            svc.PlayAt(lib.HullThud, at, 1f, Near, Far, Random.Range(0.95f, 1.05f), 0f);
            float now = Time.unscaledTime;
            _impacts.Add(now);
            _impacts.RemoveAll(t => now - t > 2.5f);
            bool close = distance < 20f && now - _rumbleAt >= 4f;   // 이 방 · 바로 옆 방
            if (close || InteriorObjectSound.Barrage(_impacts.Count, now - _rumbleAt))
            {
                _rumbleAt = now;
                svc.PlayAt(lib.HullRumble, at, 1f, Near, 45f, 1f, 0.05f);
            }
        }

        // ---------------- 도킹 · 공사 ----------------

        /// <summary>화물 터미널 화물선 도킹 — 그 방에 있으면 GameAudio의 도킹 소리 (바로 벽 너머).</summary>
        private void HandleCargo(ModuleInstance terminal, ResourceType _, float __)
        {
            if (Inside && terminal != null && !InteriorRoomDisplay.PlayerIn(terminal))
                PlayFar(l => l.DockClank, terminal.Cells, 1f, 1f, CargoDockDelay);
        }

        /// <summary>보급선 (새 상자가 생김 = 배가 옴) — 상자가 놓이는 방에 있으면 InteriorSupply의 상자 소리.</summary>
        private void HandleSupply()
        {
            var sim = Sim;
            if (sim == null)
                return;
            int newest = _lastCrate;
            ModuleInstance room = null;
            foreach (var crate in sim.Supply.Crates)
            {
                if (crate.Id > _lastCrate && room == null)
                    room = crate.Module;
                newest = Mathf.Max(newest, crate.Id);
            }
            _lastCrate = newest;
            if (Inside && room != null && !InteriorRoomDisplay.PlayerIn(room))
                PlayFar(l => l.DockClank, room.Cells, 0.85f, 1.04f, SupplyDockDelay);
        }

        /// <summary>합성 완료 (안에 있을 때) — 결과 모듈 자리에서 낮은 "철컹".</summary>
        private void HandleSynthCompleted(SynthesisJob job, ModuleInstance result)
        {
            if (Inside && result != null && !InteriorRoomDisplay.PlayerIn(result))
                PlayFar(l => l.DockClank, result.Cells, 0.8f, 0.9f);
        }

        private void Update()
        {
            if (!Inside)
            {
                _visits = Sim != null && Sim.Immigration != null ? Sim.Immigration.Visits : -1;
                return;
            }
            var sim = Sim;
            float now = Time.unscaledTime;
            UpdateImmigration(sim);
            UpdateStorm(now);
            UpdateBuild(sim, now);
        }

        /// <summary>이민선 도착 (방문 수가 늘어남): 가동 중인 여객 포트 중 하나 — 그 포트 방에 있으면 GameAudio의 도착 소리.</summary>
        private void UpdateImmigration(StationSimulation sim)
        {
            var im = sim.Immigration;
            if (im == null)
                return;
            int visits = im.Visits;
            if (_visits < 0 || visits < _visits)
            {
                _visits = visits;
                return;
            }
            if (visits == _visits)
                return;
            _visits = visits;
            foreach (var m in sim.Grid.Modules)
            {
                if (m.Data == null || !m.Data.ImmigrationPort || !_station.Connectivity.IsActive(m))
                    continue;
                if (!InteriorRoomDisplay.PlayerIn(m))
                    PlayFar(l => l.DockClank, m.Cells, 0.9f, 0.97f, 1.0f);
                return;
            }
        }

        /// <summary>태양 폭풍 동안 가끔 방 스피커 무전 잡음 (2D, 14 ~ 30초마다) — 조명 "지직"은 불빛이 번쩍이는 순간 (<see cref="HandleStormFlash"/>).</summary>
        private void UpdateStorm(float now)
        {
            if (_atmosphere == null)
            {
                _atmosphere = FindFirstObjectByType<InteriorAtmosphere>();
                if (_atmosphere != null)
                    _atmosphere.StormFlashed += HandleStormFlash;
            }
            if (!StormActive())
            {
                _radioNext = now + Random.Range(4f, 10f);
                return;
            }
            if (now < _radioNext)
                return;
            _radioNext = now + Random.Range(14f, 30f);
            AudioService.TryPlay(l => l.RadioStatic);
        }

        private void HandleStormFlash()
        {
            float now = Time.unscaledTime;
            if (!Inside || now < _flickerNext)
                return;
            _flickerNext = now + Random.Range(2.5f, 6f);   // 번쩍임은 초에 0.5번 — 몇 번에 한 번만 (D-2)
            AudioService.TryPlay(l => l.LightFlicker, Random.Range(0.75f, 1f));
        }

        private bool StormActive()
        {
            if (_storm != null)
                return _storm.StormActive;
            foreach (var a in Sim.Events.ActiveEvents)
            {
                if (a.Data is SolarStormEventData)
                    return true;
            }
            return false;
        }

        /// <summary>근처 합성 공사 (안에서 가장 가까운 진행 중 합성 하나, 40 m 안): 7 ~ 15초마다 벽 너머 가벼운 "쿵" — 멈춘 합성 · 지금 방 재료는 빼고.</summary>
        private void UpdateBuild(StationSimulation sim, float now)
        {
            if (now < _buildNext)
                return;
            _buildNext = now + Random.Range(7f, 15f);
            float best = BuildRange;
            Vector3Int[] near = null;
            foreach (var job in sim.Synthesis.Jobs)
            {
                if (job?.Recipe?.Result == null || job.Paused)
                    continue;
                var cells = StationGrid.ResolveCells(job.Recipe.Result.CellOffsets, job.Origin, job.Rotation);
                if (TryRoomPoint(cells, out _, out float d) && d < best && d > 4f)
                {
                    best = d;
                    near = cells;
                }
            }
            if (near != null)
                PlayFar(l => l.HullThud, near, 0.4f, 1.12f, 0f, BuildRange);
        }
    }
}
