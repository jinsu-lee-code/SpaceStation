using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEngine;

namespace SpaceStation.Audio
{
    /// <summary>
    /// 사운드 개편 D-4 (2026-11-26, SOUND_LIST_NEW 4-4 "바깥에서 모듈 가까이 줌"): 바깥 화면에서 카메라가 모듈에 바싹 다가가면 그 모듈 방의 바탕을 작고 먹먹하게 —
    /// 새 파일 없이 내부 바탕 (3단계 소리 풍경 바탕 · 1단계 판 · 성격별 방 환경음)을 다시 씀 (농장 = 물소리, 기계 = 웅웅). 벽 너머처럼 900 Hz 로우패스 · 2D.
    /// 모듈 = 카메라가 바라보는 점 (궤도 카메라 초점) 둘레의 모듈, 크기 = 카메라 ~ 그 모듈 가장 가까운 칸 거리 (<see cref="Interior.InteriorObjectSound.ZoomLevel"/>),
    /// 모듈이 바뀌면 0.7초에 걸쳐 교차. 1인칭 내부 · 게임 오버 = 끔.
    /// GameAudio가 만들고 매 프레임 <see cref="Tick"/>.
    /// </summary>
    public sealed class ExteriorZoomBed
    {
        public const float Gain = 0.55f;      // 내부에서의 그 바탕 크기 대비 (먹먹하게 + 작게)
        public const float Cutoff = 900f;     // 벽 너머 로우패스 (Hz)
        private const int Search = 4;         // 카메라 둘레 칸 (바깥 칸 1 m)
        private const float FadeSpeed = 1.4f;

        private readonly Camera _camera;
        private readonly StationSimulation _sim;
        private readonly Interior.InteriorMode _interior;
        private readonly SoundLibrary _lib;
        private readonly AudioSource[] _src = new AudioSource[2];
        private readonly SoundCue[] _cue = new SoundCue[2];
        private readonly float[] _level = new float[2];
        private int _cur;
        private float _target, _nextScan;
        private ModuleInstance _module;
        private OrbitCameraController _orbit;

        public ExteriorZoomBed(Transform host, Camera camera, StationSimulation sim, Interior.InteriorMode interior, SoundLibrary lib)
        {
            _camera = camera;
            _sim = sim;
            _interior = interior;
            _lib = lib;
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject("ZoomBed" + i);
                go.transform.SetParent(host, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.loop = true;
                s.spatialBlend = 0f;
                s.volume = 0f;
                go.AddComponent<AudioLowPassFilter>().cutoffFrequency = Cutoff;
                _src[i] = s;
            }
        }

        public void Tick(bool inside, bool gameOver)
        {
            float now = Time.unscaledTime;
            if (now >= _nextScan)
            {
                _nextScan = now + 0.2f;
                Scan(inside || gameOver);
            }
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < 2; i++)
            {
                float want = i == _cur ? _target : 0f;
                _level[i] = Mathf.MoveTowards(_level[i], want, FadeSpeed * dt);
                var s = _src[i];
                if (_level[i] <= 0f || _cue[i] == null)
                {
                    if (s.isPlaying)
                        s.Stop();
                    continue;
                }
                if (!s.isPlaying && s.clip != null)
                {
                    s.time = Random.Range(0f, s.clip.length * 0.9f);
                    s.Play();
                }
                s.volume = _level[i] * _cue[i].Volume * Gain * AudioVolumes.For(AudioBus.Ambient);
            }
        }

        /// <summary>카메라에서 가장 가까운 모듈 칸 (둘레 4칸 안)과 거리 → 크기 · 바탕.</summary>
        private void Scan(bool off)
        {
            _target = 0f;
            if (off || _camera == null || _sim == null)
                return;
            var eye = _camera.transform.position;
            // 고르는 모듈 = 카메라가 바라보는 점 (궤도 카메라 초점) 둘레의 모듈 — 카메라에서 가장 가까운 칸으로 고르면 초점 모듈 대신 옆 통로 · 허브가 잡혔음 (시험 2026-11-26)
            if (_orbit == null)
                _orbit = Object.FindFirstObjectByType<OrbitCameraController>();
            var look = _orbit != null && _orbit.Rig != null ? _orbit.Rig.Focus : eye;
            var near = Nearest(look, 2, out _) ?? Nearest(eye, Search, out _);
            if (near == null)
                return;
            float best = float.MaxValue;
            foreach (var c in near.Cells)
                best = Mathf.Min(best, Vector3.Distance(eye, GridConfig.CellToWorld(c)));
            _target = Interior.InteriorObjectSound.ZoomLevel(best);
            if (_target <= 0f || near == _module && _cue[_cur] != null)
                return;
            _module = near;
            var cue = BedFor(near);
            if (cue == null || !cue.HasClip || cue == _cue[_cur])
                return;
            // 다른 바탕 = 다른 소스로 교차 (지금 소리는 내려가고 새 것이 올라옴)
            _cur = 1 - _cur;
            _cue[_cur] = cue;
            _src[_cur].Stop();
            _src[_cur].clip = cue.Pick();
            _level[_cur] = 0f;
        }

        /// <summary>점 p 둘레 (r칸 안) 가장 가까운 모듈 칸의 모듈 — 없으면 null.</summary>
        private ModuleInstance Nearest(Vector3 p, int r, out float distance)
        {
            var c0 = GridConfig.WorldToCell(p);
            distance = float.MaxValue;
            ModuleInstance near = null;
            for (int x = -r; x <= r; x++)
            for (int y = -r; y <= r; y++)
            for (int z = -r; z <= r; z++)
            {
                var c = new Vector3Int(c0.x + x, c0.y + y, c0.z + z);
                if (!_sim.Grid.TryGetModule(c, out var m))
                    continue;
                float d = Vector3.Distance(p, GridConfig.CellToWorld(c));
                if (d < distance)
                {
                    distance = d;
                    near = m;
                }
            }
            return near;
        }

        /// <summary>그 모듈 방 바탕 — 3단계 소리 풍경 바탕 · 1단계 이상 = 1단계 판 · 나머지 = 성격별 방 환경음 (InteriorAudio.SetRoom과 같은 고르기).</summary>
        private SoundCue BedFor(ModuleInstance m)
        {
            var scape = _lib.SoundscapeFor(m.Data);
            if (scape != null && scape.Bed != null && scape.Bed.HasClip)
                return scape.Bed;
            var builder = _interior != null ? _interior.Builder : null;
            var kind = builder != null ? builder.AmbienceFor(m) : InteriorAmbience.Hall;
            if (builder != null && builder.StageFor(m) >= 1)
            {
                var one = _lib.Room1For(kind);
                if (one != null && one.HasClip)
                    return one;
            }
            switch (kind)
            {
                case InteriorAmbience.Life: return _lib.RoomLife;
                case InteriorAmbience.Water: return _lib.RoomWater;
                case InteriorAmbience.Machine: return _lib.RoomMachine;
                case InteriorAmbience.Heat: return _lib.RoomHeat;
                case InteriorAmbience.Electric: return _lib.RoomElectric;
                default: return _lib.RoomHall;
            }
        }
    }
}
