using System.Collections.Generic;
using SpaceStation.Audio;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 11-9 내부 방문 소리: 지금 있는 방의 환경음(성격별 6묶음 루프, 방을 옮기면 교차 페이드) + 발소리. 11-10: 파손 방 경보 루프.
    /// 2026-11-23: 내부 음악 — 방 모듈 단계별 4곡을 함께 돌리고 지금 단계 층만 올림 (<see cref="SetMusicStage"/>).
    /// 루프는 처음 들어갈 때 한 번 만들어 두고(AudioService 루프는 지울 수 없음), 나오면 모두 0으로 내린다.
    /// <see cref="InteriorMode"/>가 방이 바뀔 때 <see cref="SetRoom"/>, 나올 때 <see cref="Stop"/>를 부른다.
    /// </summary>
    public sealed class InteriorAudio
    {
        private const float FadeSpeed = 0.7f;   // 초당 (1.4초에 0 → 1)
        private const float WalkVolume = 0.75f;

        private readonly Dictionary<InteriorAmbience, AudioService.Loop> _loops = new Dictionary<InteriorAmbience, AudioService.Loop>();
        private AudioService.Loop _alarm; // 11-10 파손 방 경보
        private AudioService.MusicLayers _music; // 2026-11-23 내부 음악 (모듈 단계별 4곡, 함께 돌며 음량만 바뀜)
        private readonly InteriorSoundscape _scape = new InteriorSoundscape(); // 2026-11-23 모듈 소리 풍경 (D-7b, 지금 방만)
        private SoundCue[] _musicCues;
        private FirstPersonController _player;
        private readonly List<FootstepSurface.WaterLevel> _water = new List<FootstepSurface.WaterLevel>(); // ⑤ 지금 방의 물 면 (높이 폭 1.2 m 안의 물 메시 — 얕은 물 발소리)
        private StepFloor _floor = StepFloor.Metal;
        // ⑥ 2026-11-25 (SOUND_LIST_NEW 4-4 · D-5): 1단계 판 방 환경음 · 가끔 소리 꾸러미 · 상태 층
        private readonly Dictionary<InteriorAmbience, AudioService.Loop> _loops1 = new Dictionary<InteriorAmbience, AudioService.Loop>(); // 1단계 이상 방 (소리 풍경이 없는 방)
        private readonly InteriorSoundscape _spots = new InteriorSoundscape(); // 가끔 소리 꾸러미 (기본 · 1단계 방 — 방 아무 칸 3D)
        private AudioService.Loop _bed;            // 지금 방 바탕 (상태 층이 크기를 바꿈)
        private AudioService.Loop _spark, _boil;   // 파손 = 전선 지직 · 과열 = 끓는 거품 (2D)
        private IReadOnlyList<Vector3> _anchors;   // 지금 방 칸마다 바닥 가운데 (삐걱 자리)
        private float _bedMul = 1f, _stateNext, _creakAt, _powerAt, _powerLast = -99f, _fanLast = -99f;
        private bool _wasPowerLow, _wasOxygenLow;

        public void Attach(FirstPersonController player)
        {
            Detach();
            _player = player;
            if (_player != null)
                _player.Footstep += HandleFootstep;
        }

        public void Detach()
        {
            if (_player != null)
                _player.Footstep -= HandleFootstep;
            _player = null;
        }

        /// <summary>이 묶음만 올리고 나머지는 내림. 2026-11-23: 소리 풍경이 있는 방은 묶음 대신 그 방 소리 풍경 (room = 템플릿 인스턴스).
        /// 2026-11-25 ⑥: 1단계 이상 방은 1단계 판 바탕 (파일이 없으면 기본 판) + 성격 · 단계별 가끔 소리 꾸러미 (anchors = 그 방 칸 바닥 자리).</summary>
        public void SetRoom(InteriorAmbience ambience, RoomSoundscape scape = null, Transform room = null, Transform prefab = null,
            int stage = 0, IReadOnlyList<Vector3> anchors = null)
        {
            FootstepSurface.CollectWater(room, _water);
            if (!EnsureLoops())
                return;
            _scape.SetRoom(scape, room, prefab);
            bool own = _scape.Active;
            _bed = null;
            if (!own && !(stage >= 1 && _loops1.TryGetValue(ambience, out _bed)))
                _loops.TryGetValue(ambience, out _bed);
            foreach (var pair in _loops)
                pair.Value.Target = pair.Value == _bed ? _bedMul : 0f;
            foreach (var pair in _loops1)
                pair.Value.Target = pair.Value == _bed ? _bedMul : 0f;
            // 지금 있는 방 소리만 (D-4). 예외 자리 (예정, CONNECT_1121 A8): 옆방이 소음원 태그면 그 방 성격 바탕을 여기서 낮은 Target + 로우패스로 (지금은 안 함)
            _anchors = anchors;
            var lib = AudioService.Instance.Library;
            _spots.SetRoom(own ? null : lib.SpotsFor(ambience, stage), null, null, anchors);
            _wasPowerLow = _wasOxygenLow = false;
            _creakAt = 0f;
            _stateNext = 0f;
        }

        /// <summary>소리 풍경 페이드 · 가끔 소리 (나온 뒤에도 내려갈 때까지 불러야 함). 2026-11-25 ⑥ 가끔 소리 꾸러미도.</summary>
        public void Tick(float dt, bool day)
        {
            _scape.Tick(dt, day);
            _spots.Tick(dt, day);
        }

        /// <summary>
        /// ⑥ 상태 층 (2026-11-25, SOUND_LIST_NEW 4-4 · D-5 ③): 지금 있는 방 상태를 0.25초마다 읽어 겹침 — 파손 = 전선 지직 루프 + 선체 삐걱 (6 ~ 14초, 방 아무 칸 3D) ·
        /// 전력 부족 / 비활성 = 바탕이 효율만큼 작아지고 웅이 꺼지는 소리 (들어설 때 · 30 ~ 50초마다) · 산소 바닥 = 환기 팬 멈춤 (들어설 때) + 바탕 0.6배 · 과열 = 끓는 거품 루프.
        /// 경보 (파손 방 경보 루프 · 알림)는 경보 위계 (D-3) 쪽.
        /// </summary>
        public void SetRoomState(StationController station, ModuleInstance module)
        {
            float now = Time.unscaledTime;
            if (_loops.Count == 0 || station == null || station.Simulation == null || module == null || now < _stateNext)
                return;
            _stateNext = now + 0.25f;
            var sim = station.Simulation;
            bool damaged = sim.Damage.TryGetInfo(module, out _);
            float power = station.Connectivity.IsActive(module) ? sim.Resources.PowerEfficiency : 0f;
            bool powerLow = power < 0.95f;
            bool oxygenLow = sim.Resources.IsDepleted(ResourceType.Oxygen);
            bool hot = sim.Heat != null && sim.Heat.IsOverheated(module);
            _bedMul = Mathf.Lerp(0.3f, 1f, Mathf.InverseLerp(0.25f, 1f, power)) * (oxygenLow ? 0.6f : 1f);
            if (_bed != null)
                _bed.Target = _bedMul;
            if (_spark != null)
                _spark.Target = damaged ? 1f : 0f;
            if (_boil != null)
                _boil.Target = hot ? 1f : 0f;
            if (powerLow && (!_wasPowerLow || now >= _powerAt) && now - _powerLast > 20f)
            {
                AudioService.TryPlay(l => l.StatePowerDown);
                _powerLast = now;
                _powerAt = now + Random.Range(30f, 50f);
            }
            if (oxygenLow && !_wasOxygenLow && now - _fanLast > 20f)
            {
                AudioService.TryPlay(l => l.StateFanStop);
                _fanLast = now;
            }
            _wasPowerLow = powerLow;
            _wasOxygenLow = oxygenLow;
            if (!damaged || _anchors == null || _anchors.Count == 0)
            {
                _creakAt = 0f;
                return;
            }
            if (_creakAt <= 0f)
                _creakAt = now + Random.Range(3f, 8f);
            else if (now >= _creakAt)
            {
                var at = _anchors[Random.Range(0, _anchors.Count)] + new Vector3(Random.Range(-2.5f, 2.5f), Random.Range(1.6f, 3f), Random.Range(-2.5f, 2.5f));
                AudioService.TryPlayAt(l => l.StateCreak, at);
                _creakAt = now + Random.Range(6f, 14f);
            }
        }

        /// <summary>지금 방 모듈 단계의 음악 층을 올림 (3 = 3단계 이상). 그 단계 곡이 없으면 아래 단계 곡으로.</summary>
        public void SetMusicStage(int stage)
        {
            if (!EnsureLoops() || _music == null)
                return;
            int layer = -1;
            for (int i = Mathf.Clamp(stage, 0, _musicCues.Length - 1); i >= 0 && layer < 0; i--)
                if (_musicCues[i] != null && _musicCues[i].HasClip)
                    layer = i;
            _music.Active = layer;
        }

        /// <summary>11-10 지금 있는 방이 파손이면 경보음을 올린다.</summary>
        public void SetAlarm(bool on)
        {
            if (!EnsureLoops() || _alarm == null)
                return;
            _alarm.Target = on ? 1f : 0f;
        }

        public void Stop()
        {
            foreach (var loop in _loops.Values)
                loop.Target = 0f;
            if (_alarm != null)
                _alarm.Target = 0f;
            if (_music != null)
                _music.Active = -1;
            _scape.Stop();
            foreach (var loop in _loops1.Values)
                loop.Target = 0f;
            if (_spark != null)
                _spark.Target = 0f;
            if (_boil != null)
                _boil.Target = 0f;
            _spots.Stop();
            _bed = null;
            Detach();
        }

        private bool EnsureLoops()
        {
            if (_loops.Count > 0)
                return true;
            var service = AudioService.Instance;
            if (service == null || service.Library == null)
                return false;
            var lib = service.Library;
            Add(service, InteriorAmbience.Life, lib.RoomLife);
            Add(service, InteriorAmbience.Water, lib.RoomWater);
            Add(service, InteriorAmbience.Machine, lib.RoomMachine);
            Add(service, InteriorAmbience.Heat, lib.RoomHeat);
            Add(service, InteriorAmbience.Electric, lib.RoomElectric);
            Add(service, InteriorAmbience.Hall, lib.RoomHall);
            if (lib.InteriorAlarm != null && lib.InteriorAlarm.HasClip)
            {
                _alarm = service.CreateLoop(lib.InteriorAlarm);
                _alarm.FadeSpeed = 3f; // 방에 들어서면 빠르게
                _alarm.Target = 0f;
            }
            // ⑥ 1단계 판 바탕 (파일이 있는 성격만) · 상태 층 루프 (2026-11-25)
            foreach (InteriorAmbience kind in System.Enum.GetValues(typeof(InteriorAmbience)))
            {
                var cue = lib.Room1For(kind);
                if (cue == null || !cue.HasClip)
                    continue;
                var loop = service.CreateLoop(cue);
                loop.FadeSpeed = FadeSpeed;
                loop.Target = 0f;
                _loops1[kind] = loop;
            }
            _spark = StateLoop(service, lib.StateSparkLoop);
            _boil = StateLoop(service, lib.StateBoilLoop);
            _musicCues = new[] { lib.MusicInterior0, lib.MusicInterior1, lib.MusicInterior2, lib.MusicInterior3 };
            _music = service.CreateMusicLayers(_musicCues);
            return true;
        }

        private static AudioService.Loop StateLoop(AudioService service, SoundCue cue)
        {
            if (cue == null || !cue.HasClip)
                return null;
            var loop = service.CreateLoop(cue);
            loop.FadeSpeed = 0.5f; // 2초에 차오름
            loop.Target = 0f;
            return loop;
        }

        private void Add(AudioService service, InteriorAmbience kind, SoundCue cue)
        {
            var loop = service.CreateLoop(cue);
            loop.FadeSpeed = FadeSpeed;
            loop.Target = 0f;
            _loops[kind] = loop;
        }

        /// <summary>⑤ 바닥별 발소리 (2026-11-25): 발밑 재질로 바닥 7 중 하나 (<see cref="FootstepSurface"/>) × 걷기 / 달리기. 그 칸에 소리가 없으면 금속 판.</summary>
        private void HandleFootstep(bool run)
        {
            if (_player != null)
                _floor = FootstepSurface.At(_player.transform.position, _water, _floor);
            var floor = _floor;
            AudioService.TryPlay(l => StepCue(l, floor, run), run ? 1f : WalkVolume);
        }

        private static SoundCue StepCue(SoundLibrary l, StepFloor floor, bool run)
        {
            SoundCue cue;
            switch (floor)
            {
                case StepFloor.Grate: cue = run ? l.StepGrateRun : l.StepGrate; break;
                case StepFloor.Glass: cue = run ? l.StepGlassRun : l.StepGlass; break;
                case StepFloor.Grass: cue = run ? l.StepGrassRun : l.StepGrass; break;
                case StepFloor.Wood: cue = run ? l.StepWoodRun : l.StepWood; break;
                case StepFloor.Stone: cue = run ? l.StepStoneRun : l.StepStone; break;
                case StepFloor.Water: cue = run ? l.StepWaterRun : l.StepWater; break;
                default: cue = run ? l.FootstepRun : l.Footstep; break;
            }
            if (cue == null || !cue.HasClip)
                cue = run && l.FootstepRun != null && l.FootstepRun.HasClip ? l.FootstepRun : l.Footstep;
            return cue;
        }
    }
}
