using System.Collections.Generic;
using SpaceStation.Audio;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 사운드 개편 4-3 · 4-4 주민 소리 (2026-11-26, SOUND_LIST_NEW voice_* · crowd_* · act_* · D-2 · D-4 · D-5 ④ · D-11) — 1인칭 내부에 있는 동안, 지금 있는 방의 주민만.
    /// - 목소리 (그 주민 머리 자리 3D, 1.5 m까지 그대로 → 14 m에서 0): 플레이어가 다가와 반응할 때 (<see cref="ResidentMotion.Reacted"/> — 폴짝 · 손 흔들기 = 기쁨,
    ///   팔짱 = 불만, 부탁이 걸린 주민 = 부탁) · 부탁 말풍선 · 부탁을 들어줌 (<see cref="InteriorRequests"/>가 <see cref="Say"/>) · 그 방에 사고가 생김 · 운석이 이 방이나 옆방을 맞힘 (놀람) ·
    ///   오락실 · 2단계 이상 여가 방에서 가끔 웃음. 반복 막기 = <see cref="ResidentVoiceGate"/> (주민마다 20초 · 방 동시 2).
    /// - 웅성 (2D 루프 셋): 그 방에 깨어 있는 주민 수 → <see cref="ResidentSound.CrowdLevel"/> → 이웃 단계끼리 교차.
    /// - 활동 (그 주민 자리 3D, 1 m까지 그대로 → 7 m에서 0, 아주 작게): 걷는 발 (가까운 둘) · 앉고 일어섬 · 그릇 · 키보드 · 공구 · 잠 숨 · 놀이 도구.
    /// GameAudio가 붙이고 묶음 (시뮬레이션 · 주민 동작은 그대로 — 동작 쪽은 읽기만).
    /// </summary>
    public sealed class InteriorResidentSound : MonoBehaviour
    {
        private const float VoiceNear = 1.5f, VoiceFar = 14f;
        private const float ActNear = 1f;
        private const float ReactDelay = 0.15f;      // 고개를 돌리는 동안 — 손을 흔들기 시작할 때 울음

        private static InteriorResidentSound _instance;

        private sealed class Track
        {
            public float Next;
            public float CheerNext;
            public int Step = int.MinValue;
            public float Stand = -1f;
        }

        private StationController _station;
        private MeteorFx _meteors;
        private InteriorMode _mode;
        private InteriorResidents _residents;
        private InteriorRequests _requests;
        private InteriorIncidents _incidents;
        private float _findAt;
        private Camera _camera;
        private readonly ResidentVoiceGate _voices = new ResidentVoiceGate();
        private readonly Dictionary<ResidentFigure, Track> _tracks = new Dictionary<ResidentFigure, Track>();
        private readonly List<ResidentFigure> _here = new List<ResidentFigure>();
        private readonly List<ResidentFigure> _walkers = new List<ResidentFigure>();
        private readonly List<float> _actEnds = new List<float>();
        private readonly List<ModuleInstance> _neighbors = new List<ModuleInstance>();
        private AudioService.Loop[] _crowd;
        private readonly float[] _mix = new float[3];
        private float _crowdLevel, _crowdTarget, _tick;
        private float _lastActStart = -99f, _lastPeriodic = -99f, _cheerReadyAt;
        private ModuleInstance _room;
        private string _roomName;
        private InteriorAmbience _roomAmbience;
        private bool _roomCheer, _night, _incidentWas;
        private ModuleInstance _incidentRoom;

        private bool Inside => _mode != null && _mode.IsInside && _station != null && _station.Simulation != null;
        private static SoundLibrary Lib => AudioService.Instance != null ? AudioService.Instance.Library : null;

        public void Bind(StationController station, MeteorFx meteors, InteriorMode mode)
        {
            _station = station;
            _meteors = meteors;
            _mode = mode;
            _instance = this;
            ResidentMotion.Reacted += HandleReacted;
            if (_meteors != null)
                _meteors.Impacted += HandleImpacted;
        }

        private void OnDestroy()
        {
            ResidentMotion.Reacted -= HandleReacted;
            if (_meteors != null)
                _meteors.Impacted -= HandleImpacted;
            if (_instance == this)
                _instance = null;
        }

        // ---------------- 목소리 ----------------

        /// <summary>
        /// 주민 한 명이 한마디 (지금 있는 방의 주민일 때만 — D-4). reply = 부탁 · 완료처럼 플레이어가 한 일의 대답 (주민마다 20초 간격을 건너뜀, 방 동시 수는 지킴).
        /// delay = 실시간 초 (같은 순간의 알림 · 완료음 뒤에 오게).
        /// </summary>
        public static void Say(ResidentFigure figure, ResidentVoiceMood mood, float delay = 0f, bool reply = false)
        {
            if (_instance != null)
                _instance.TrySay(figure, mood, delay, reply);
        }

        private bool TrySay(ResidentFigure figure, ResidentVoiceMood mood, float delay, bool reply)
        {
            var lib = Lib;
            if (figure == null || lib == null || !Inside || !InteriorRoomDisplay.PlayerIn(figure.Room))
                return false;
            var cue = lib.VoiceFor(ResidentSound.SpeciesIndex(figure.SpeciesId), mood);
            if (cue == null || !cue.HasClip)
                return false;
            float length = cue.Clips[0] != null ? cue.Clips[0].length : 0.5f;
            if (!_voices.TryAdmit(figure.ResidentId, Time.unscaledTime + delay, length, reply))
                return false;
            var at = figure.TagPoint - Vector3.up * 0.3f;   // 머리 (이름표 바로 아래)
            return AudioService.Instance.PlayAt(cue, at, 1f, VoiceNear, VoiceFar, 1f, delay);
        }

        private void HandleReacted(ResidentMotion motion, ResidentReaction reaction)
        {
            if (motion == null || !Inside)
                return;
            var figure = motion.GetComponent<ResidentFigure>();
            if (figure == null)
                return;
            var mood = ResidentSound.MoodForReaction(reaction, _requests != null && _requests.Pending(figure.ResidentId));
            if (mood.HasValue)
                TrySay(figure, mood.Value, ReactDelay, false);
        }

        /// <summary>놀람: 그 방 주민 중 플레이어에게 가까운 둘 (12 m 안)이 조금씩 어긋나게.</summary>
        private void SurpriseNear(ModuleInstance room, float delay)
        {
            if (room == null || _residents == null)
                return;
            var eye = Listener();
            ResidentFigure a = null, b = null;
            float da = 144f, db = 144f;
            foreach (var f in _residents.Figures)
            {
                if (f == null || f.Room != room)
                    continue;
                float d = (f.transform.position - eye).sqrMagnitude;
                if (d < da) { b = a; db = da; a = f; da = d; }
                else if (d < db) { b = f; db = d; }
            }
            if (a != null)
                TrySay(a, ResidentVoiceMood.Surprise, delay, false);
            if (b != null)
                TrySay(b, ResidentVoiceMood.Surprise, delay + 0.5f, false);
        }

        private void HandleImpacted(Vector3 point, ModuleInstance module)
        {
            if (!Inside || module == null || _room == null)
                return;
            bool near = module == _room;
            if (!near)
            {
                _station.Grid.GetNeighborModules(_room, _neighbors);
                near = _neighbors.Contains(module);
            }
            if (near)
                SurpriseNear(_room, 0.3f);   // 선체 너머 "쿵" (InteriorOutsideSound) 뒤
        }

        // ---------------- 매 프레임 ----------------

        private void Update()
        {
            var lib = Lib;
            if (!Inside || lib == null)
            {
                SetCrowd(0f, true);
                if (_tracks.Count > 0)
                    _tracks.Clear();
                _room = null;
                return;
            }
            float now = Time.unscaledTime;
            if ((_residents == null || _requests == null || _incidents == null) && now >= _findAt)
            {
                _findAt = now + 1f;
                if (_residents == null) _residents = FindFirstObjectByType<InteriorResidents>();
                if (_requests == null) _requests = FindFirstObjectByType<InteriorRequests>();
                if (_incidents == null) _incidents = FindFirstObjectByType<InteriorIncidents>();
            }
            if (_residents == null || _requests == null)
            {
                SetCrowd(0f, false);
                return;
            }
            if (_requests.CurrentRoom != _room)
                ChangeRoom(_requests.CurrentRoom);
            if (now >= _tick)
            {
                _tick = now + 0.25f;
                Tick();
            }
            _crowdLevel = Mathf.MoveTowards(_crowdLevel, _crowdTarget, 0.5f * Time.unscaledDeltaTime);
            SetCrowd(_crowdLevel, false);
            UpdateActivity(now, lib);
        }

        private void ChangeRoom(ModuleInstance room)
        {
            _room = room;
            _voices.ClearRoom();
            _actEnds.Clear();
            _tracks.Clear();
            _cheerReadyAt = Time.unscaledTime + 4f;   // 들어서자마자 웃음이 몰리지 않게 (다가가 반응은 그대로)
            _roomName = room != null && room.Data != null ? room.Data.name : null;
            var builder = _mode.Builder;
            _roomAmbience = builder != null && room != null ? builder.AmbienceFor(room) : InteriorAmbience.Hall;
            var template = builder != null && room != null ? builder.TemplateFor(room) : null;
            _roomCheer = ResidentSound.IsPlayRoom(_roomName) || (template != null && template.Leisure && builder.StageFor(room) >= 2);
        }

        /// <summary>0.25초마다: 이 방 주민 목록 · 깨어 있는 수 → 웅성 단계 · 사고가 이 방에 막 생겼는지.</summary>
        private void Tick()
        {
            var sim = _station.Simulation;
            _night = !sim.DayNight.IsDay(sim.ElapsedSeconds);
            _here.Clear();
            int awake = 0;
            foreach (var f in _residents.Figures)
            {
                if (f == null || f.Room != _room)
                    continue;
                _here.Add(f);
                if (!(_night && f.AtHome))
                    awake++;
            }
            _crowdTarget = ResidentSound.CrowdLevel(awake);

            if (_incidents != null)
            {
                bool active = _incidents.TryGetActive(out _, out var room, out _);
                if (active && (!_incidentWas || room != _incidentRoom) && room == _room)
                    SurpriseNear(_room, 0.6f);   // 위험 경보 · 사고 자리 소리 뒤
                _incidentWas = active;
                _incidentRoom = active ? room : null;
            }
        }

        private void SetCrowd(float level, bool off)
        {
            var lib = Lib;
            if (_crowd == null)
            {
                if (off || lib == null || AudioService.Instance == null)
                    return;
                _crowd = new[] { AudioService.Instance.CreateLoop(lib.CrowdLoop1), AudioService.Instance.CreateLoop(lib.CrowdLoop2), AudioService.Instance.CreateLoop(lib.CrowdLoop3) };
                foreach (var l in _crowd)
                    l.FadeSpeed = 0.6f;
            }
            if (off)
                _mix[0] = _mix[1] = _mix[2] = 0f;
            else
                ResidentSound.CrowdMix(level, _mix);
            for (int i = 0; i < 3; i++)
                _crowd[i].Target = _mix[i];
        }

        private Vector3 Listener()
        {
            if (_camera == null)
                _camera = Camera.main;
            return _camera != null ? _camera.transform.position : Vector3.zero;
        }

        /// <summary>걷는 발 (가까운 둘) · 앉고 일어섬 · 이어지는 활동 (그릇 · 키보드 · 공구 · 숨 · 놀이) · 가끔 웃음.</summary>
        private void UpdateActivity(float now, SoundLibrary lib)
        {
            var eye = Listener();
            float range2 = ResidentSound.ActivityRange * ResidentSound.ActivityRange;
            _actEnds.RemoveAll(e => e <= now);
            _walkers.Clear();
            foreach (var f in _here)
            {
                if (f == null || f.Motion == null)
                    continue;
                var m = f.Motion;
                if (!_tracks.TryGetValue(f, out var t))
                {
                    _tracks[f] = t = new Track { Next = now + Random.Range(1f, 10f), CheerNext = now + Random.Range(10f, 50f), Stand = m.StandBlend };
                    t.Step = Mathf.FloorToInt(m.WalkPhase / Mathf.PI);
                }
                var feet = f.transform.position;
                bool inRange = (feet - eye).sqrMagnitude < range2;

                // 앉고 일어섬 (방석 "푹") — 일어서기 시작 · 다 앉음
                float sb = m.StandBlend;
                if (inRange && t.Stand >= 0f && ((t.Stand < 0.05f && sb >= 0.05f) || (t.Stand > 0.5f && sb <= 0.02f)))
                    PlayAct(lib.ActSit, feet + Vector3.up * 0.4f, now, 0.6f, false);
                t.Stand = sb;

                // 걷는 발: 걸음마다 (한 걸음 = 위상 π)
                int step = Mathf.FloorToInt(m.WalkPhase / Mathf.PI);
                if (step != t.Step)
                {
                    t.Step = step;
                    if (m.WalkBlend > 0.5f && (feet - eye).sqrMagnitude < ResidentSound.WalkRange * ResidentSound.WalkRange)
                        _walkers.Add(f);
                }

                bool working = m.Pose == ResidentPose.Work;
                bool seated = m.Pose == ResidentPose.Sit && sb < 0.5f;
                var act = ResidentSound.ActivityOf(_roomName, _roomAmbience, working, seated, f.AtHome, _night);
                if (act != ResidentActivity.None && now >= t.Next)
                {
                    var (lo, hi) = ResidentSound.GapOf(act);
                    var cue = ActCue(lib, act, f.SpeciesId);
                    float len = cue != null && cue.HasClip && cue.Clips[0] != null ? cue.Clips[0].length : 0.5f;
                    t.Next = now + (act == ResidentActivity.Sleep ? len : 0f) + Random.Range(lo, hi);
                    if (inRange)
                        PlayAct(cue, feet + Vector3.up * (act == ResidentActivity.Sleep ? 0.45f : 0.6f), now, len, true);
                }
                if (_roomCheer && !working && !(_night && f.AtHome) && now >= t.CheerNext && now >= _cheerReadyAt)
                {
                    t.CheerNext = now + Random.Range(ResidentSound.CheerGapMin, ResidentSound.CheerGapMax);
                    if (TrySay(f, ResidentVoiceMood.Happy, 0f, false))   // 오락실 · 테마파크의 웃음 — 방 전체 8 ~ 15초에 하나
                        _cheerReadyAt = now + Random.Range(ResidentSound.CheerRoomGapMin, ResidentSound.CheerRoomGapMax);
                }
            }
            // 이 프레임에 발을 디딘 주민 중 가까운 둘만
            if (_walkers.Count > 0)
            {
                _walkers.Sort((x, y) => (x.transform.position - eye).sqrMagnitude.CompareTo((y.transform.position - eye).sqrMagnitude));
                for (int i = 0; i < _walkers.Count && i < ResidentSound.MaxWalkers; i++)
                    AudioService.Instance.PlayAt(lib.ActWalkSmall, _walkers[i].transform.position + Vector3.up * 0.05f, 1f, ActNear, ResidentSound.WalkRange);
            }
        }

        private static SoundCue ActCue(SoundLibrary lib, ResidentActivity act, string species)
        {
            switch (act)
            {
                case ResidentActivity.Eat: return lib.ActEat;
                case ResidentActivity.Work: return lib.ActWork;
                case ResidentActivity.WorkTool: return lib.ActWorkTool;
                case ResidentActivity.Play: return lib.ActPlay;
                case ResidentActivity.Sleep:
                    int s = ResidentSound.SleepSize(species);
                    return s == 0 ? lib.ActSleepBig : s == 2 ? lib.ActSleepSmall : lib.ActSleepMid;
                default: return null;
            }
        }

        /// <summary>활동 소리 하나 — 방 동시 둘 · 시작끼리 0.3초 넘게, 이어지는 활동 (그릇 · 키보드 · 공구 · 숨 · 놀이)은 방 전체 2초 넘게 (몰리지 않게, D-2).</summary>
        private void PlayAct(SoundCue cue, Vector3 at, float now, float length, bool periodic)
        {
            if (cue == null || !cue.HasClip || _actEnds.Count >= ResidentSound.MaxActivity || now - _lastActStart < 0.3f)
                return;
            if (periodic && now - _lastPeriodic < ResidentSound.ActivityRoomGap)
                return;
            if (AudioService.Instance.PlayAt(cue, at, 1f, ActNear, ResidentSound.ActivityRange))
            {
                _lastActStart = now;
                if (periodic)
                    _lastPeriodic = now;
                _actEnds.Add(now + Mathf.Max(0.1f, length));
            }
        }
    }
}
