using System.Collections.Generic;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Simulation;
using UnityEngine;

namespace SpaceStation.Audio
{
    /// <summary>
    /// 사운드 개편 3번 · D-4 · D-12 (2026-11-26, 작업자 P4): 바깥 화면의 진화 모듈 연출 (오로라 · 번개 · 불꽃놀이 · 드론 쇼 · 후룸 · 가리비 배 · 순찰차 · 빛기둥 · 구름 비)과
    /// 오가는 배 (화물선 · 채굴선 · 보급선 · 지나가는 배) 엔진 소리. GameAudio가 붙임 (장면 편집 없음).
    /// - 멀리서 들리는 소리: 카메라 가까이 · 화면 가운데일 때만, 먹먹하게 (로우패스) · 작게 (<see cref="ExteriorFxSound"/>).
    /// - 한 물건 = 소리 하나: 1인칭 체험 · 소리 풍경에 같은 물건 소리가 있으면 그 클립 (SoundLibrary Fx 칸이 같은 파일을 가리킴), 크기만 바깥 값.
    /// - 루프는 같은 종류끼리 가장 가까운 하나 · 종류 둘까지 (D-2), 1인칭 내부 · 게임 오버 중엔 모두 끔 (내부는 방 소리가 따로).
    /// 바깥 연출 부품은 내부 템플릿에도 쓰이므로 정거장 모듈 화면 (StationController 모듈 뷰)에 붙은 것만 봄.
    /// </summary>
    public sealed class ExteriorFxAudio : MonoBehaviour
    {
        private enum Kind { Aurora, Beam, Drones, Flume, Boat, Patrol, Rain, Count }

        private sealed class Site
        {
            public ModuleView View;
            public AuroraFx Aurora;
            public LightBeamFx Beam;
            public DroneShowFx Drones;
            public FlumeRideFx Flume;
            public BoatShuttleFx[] Boats;
            public PatrolCarFx[] Cars;
            public bool[] CarFlying;
            public RainFx Rain;
            public LightningFx Lightning;
            public FireworkFx Fireworks;
            public System.Action<Vector3, bool> OnStruck;
            public System.Action<int> OnLaunched;
            public System.Action<Vector3> OnSplashed;
            public float Level;   // 이번 프레임 자리 크기 (모듈 가운데 — 거리 × 화면 자리)
            public bool Loud;     // 가장 큰 자리 MaxSites 안 — 이 자리만 소리를 냄
        }

        private sealed class Ship
        {
            public Transform T;
            public float Size;
            public Vector3 Last;
            public float Speed;
            public AudioService.Loop Loop;
            public AudioLowPassFilter Filter;
        }

        private StationController _station;
        private StationSimulation _sim;
        private Camera _camera;
        private AudioService _audio;
        private SoundLibrary _lib;
        private AmbientSpace _space;
        private bool _inside;

        private readonly Dictionary<ModuleView, Site> _sites = new Dictionary<ModuleView, Site>();
        private readonly HashSet<ModuleView> _checked = new HashSet<ModuleView>();
        private readonly List<ModuleView> _gone = new List<ModuleView>();
        private float _nextScan;

        private readonly AudioService.Loop[] _loops = new AudioService.Loop[(int)Kind.Count];
        private readonly AudioLowPassFilter[] _filters = new AudioLowPassFilter[(int)Kind.Count];
        private readonly float[] _want = new float[(int)Kind.Count];
        private readonly float[] _pan = new float[(int)Kind.Count];
        private readonly float[] _cut = new float[(int)Kind.Count];
        private readonly int[] _order = new int[(int)Kind.Count];

        private const int ShotVoices = 6;
        private readonly AudioSource[] _shots = new AudioSource[ShotVoices];
        private readonly AudioLowPassFilter[] _shotFilters = new AudioLowPassFilter[ShotVoices];
        private readonly float[] _shotBusy = new float[ShotVoices];

        private readonly List<Ship> _ships = new List<Ship>();
        private const int MaxShips = 3;
        private const float FireworkRise = 0.8f;   // FireworkFx 박격포대 불꽃이 올라가는 시간 → 터짐
        private readonly List<Site> _rank = new List<Site>();

        public void Init(StationController station, Camera camera, Interior.InteriorMode interior)
        {
            _station = station;
            _sim = station != null ? station.Simulation : null;
            _camera = camera;
            _audio = AudioService.Instance;
            _lib = _audio != null ? _audio.Library : null;
            if (_lib == null || _sim == null)
            {
                enabled = false;
                return;
            }
            var cues = new[] { _lib.FxAuroraLoop, _lib.FxLightBeamLoop, _lib.FxDroneShowLoop, _lib.FxFlumeLoop, _lib.FxBoatLoop, _lib.FxPatrolHoverLoop, _lib.FxRainLoop };
            for (int k = 0; k < cues.Length; k++)
            {
                _loops[k] = _audio.CreateLoop(cues[k]);
                _loops[k].FadeSpeed = 0.8f;
                _filters[k] = _loops[k].Source.gameObject.AddComponent<AudioLowPassFilter>();
            }
            for (int i = 0; i < ShotVoices; i++)
            {
                var go = new GameObject("ExteriorFxShot" + i);
                go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                s.dopplerLevel = 0f;
                _shots[i] = s;
                _shotFilters[i] = go.AddComponent<AudioLowPassFilter>();
            }
            if (interior != null)
            {
                _inside = interior.IsInside;
                interior.InsideChanged += HandleInside;
            }
            _space = FindFirstObjectByType<AmbientSpace>();
            if (_space != null)
                _space.ShipLaunched += HandleShip;
        }

        private void OnDestroy()
        {
            if (_space != null)
                _space.ShipLaunched -= HandleShip;
            foreach (var site in _sites.Values)
                Unbind(site);
            _sites.Clear();
        }

        private void HandleInside(bool inside) => _inside = inside;

        private bool Muted => _inside || _sim == null || _sim.Session.IsGameOver || _camera == null;

        // ---------------- 모듈 연출 찾기 ----------------

        private void Scan()
        {
            _gone.Clear();
            foreach (var view in _sites.Keys)
                if (view == null || view.Module == null || !_station.TryGetView(view.Module, out var now) || now != view)
                    _gone.Add(view);
            foreach (var view in _gone)
            {
                Unbind(_sites[view]);
                _sites.Remove(view);
                _checked.Remove(view);
            }
            _checked.RemoveWhere(v => v == null);
            foreach (var module in _station.Grid.Modules)
            {
                if (module?.Data == null || !_sim.Synthesis.IsRecipeResult(module.Data) || !_station.TryGetView(module, out var view) || view == null || !_checked.Add(view))
                    continue;
                var site = new Site
                {
                    View = view,
                    Aurora = view.GetComponentInChildren<AuroraFx>(true),
                    Beam = view.GetComponentInChildren<LightBeamFx>(true),
                    Drones = view.GetComponentInChildren<DroneShowFx>(true),
                    Flume = view.GetComponentInChildren<FlumeRideFx>(true),
                    Boats = view.GetComponentsInChildren<BoatShuttleFx>(true),
                    Cars = view.GetComponentsInChildren<PatrolCarFx>(true),
                    Rain = view.GetComponentInChildren<RainFx>(true),
                    Lightning = view.GetComponentInChildren<LightningFx>(true),
                    Fireworks = view.GetComponentInChildren<FireworkFx>(true),
                };
                bool any = site.Aurora != null || site.Beam != null || site.Drones != null || site.Flume != null || site.Boats.Length > 0
                    || site.Cars.Length > 0 || site.Rain != null || site.Lightning != null || site.Fireworks != null;
                if (!any)
                    continue;
                site.CarFlying = new bool[site.Cars.Length];
                Bind(site);
                _sites[view] = site;
            }
        }

        private void Bind(Site site)
        {
            if (site.Lightning != null)
            {
                site.OnStruck = (at, treeToTree) => { if (!treeToTree && site.Loud) Shot(_lib.FxLightning, at); };
                site.Lightning.Struck += site.OnStruck;
            }
            if (site.Fireworks != null)
            {
                var fx = site.Fireworks;
                site.OnLaunched = _ =>
                {
                    if (!site.Loud)
                        return;
                    Vector3 at = fx.transform.position;
                    Shot(_lib.FxFireworkLaunch, at);
                    Shot(_lib.FxFireworkBurst, at + Vector3.up, delay: FireworkRise);
                };
                fx.Launched += site.OnLaunched;
            }
            if (site.Flume != null)
            {
                site.OnSplashed = at => { if (site.Loud) Shot(_lib.FxFlumeSplash, at); };
                site.Flume.Splashed += site.OnSplashed;
            }
        }

        private static void Unbind(Site site)
        {
            if (site.Lightning != null && site.OnStruck != null)
                site.Lightning.Struck -= site.OnStruck;
            if (site.Fireworks != null && site.OnLaunched != null)
                site.Fireworks.Launched -= site.OnLaunched;
            if (site.Flume != null && site.OnSplashed != null)
                site.Flume.Splashed -= site.OnSplashed;
        }

        // ---------------- 매 프레임 ----------------

        private void Update()
        {
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 1f;
                Scan();
            }
            for (int k = 0; k < _want.Length; k++)
                _want[k] = 0f;
            RankSites();
            if (!Muted)
            {
                bool night = _sim.DayNight.Enabled && !_sim.DayNight.IsDay(_sim.ElapsedSeconds);
                foreach (var site in _sites.Values)
                {
                    if (!site.Loud)
                        continue;
                    if (site.Aurora != null)
                        Want(Kind.Aurora, site.Aurora.transform.position, night ? 1f : 0.7f);   // 밤에 오로라가 세짐 (D-7)
                    if (site.Beam != null)
                        Want(Kind.Beam, site.Beam.transform.position, 1f);
                    if (site.Drones != null)
                        Want(Kind.Drones, site.Drones.transform.position, site.Drones.Grand ? 1f : 0.75f);
                    if (site.Flume != null)
                        Want(Kind.Flume, site.Flume.transform.position, 1f);
                    foreach (var boat in site.Boats)
                        if (boat != null)
                            Want(Kind.Boat, boat.transform.position, 1f);
                    for (int i = 0; i < site.Cars.Length; i++)
                    {
                        var car = site.Cars[i];
                        if (car == null)
                            continue;
                        bool flying = car.Flying;
                        if (flying != site.CarFlying[i])
                        {
                            site.CarFlying[i] = flying;
                            Shot(_lib.FxSiren, car.transform.position);   // 출동 · 돌아옴 "삐용" (1인칭 순찰차와 같은 규칙)
                        }
                        Want(Kind.Patrol, car.transform.position, flying ? 1f : 0.6f);
                    }
                    if (site.Rain != null)
                        Want(Kind.Rain, site.Rain.transform.position, 1f);
                }
            }
            ApplyLoops();
            UpdateShips();
        }

        /// <summary>
        /// 자리 고르기 (D-2): 진화 모듈이 모여 있으면 줌한 자리 둘레의 연출이 한꺼번에 울림 (첫 시험 — 요새 포대 불꽃 · 번개 · 후룸 물보라 · 순찰차가 어느 자리에서나 겹침)
        /// → 가동 중인 자리를 모듈 가운데 크기로 줄 세워 가장 큰 MaxSites곳만 루프 · 한 번 소리를 냄.
        /// </summary>
        private void RankSites()
        {
            _rank.Clear();
            foreach (var site in _sites.Values)
            {
                site.Loud = false;
                site.Level = 0f;
                if (Muted || site.View == null || !site.View.Operational)
                    continue;
                Vector3 at = site.View.transform.position;
                site.Level = ExteriorFxSound.Level(Vector3.Distance(_camera.transform.position, at), _camera.WorldToViewportPoint(at));
                if (site.Level > ExteriorFxSound.MinSiteLevel)
                    _rank.Add(site);
            }
            _rank.Sort((a, b) => b.Level.CompareTo(a.Level));
            for (int i = 0; i < _rank.Count && i < ExteriorFxSound.MaxSites; i++)
                _rank[i].Loud = true;
        }

        private void Want(Kind kind, Vector3 at, float scale)
        {
            var v = _camera.WorldToViewportPoint(at);
            float d = Vector3.Distance(_camera.transform.position, at);
            float level = ExteriorFxSound.Level(d, v) * scale;
            int k = (int)kind;
            if (level <= _want[k])
                return;
            _want[k] = level;
            _pan[k] = ExteriorFxSound.Pan(v);
            _cut[k] = ExteriorFxSound.Cutoff(d);
        }

        /// <summary>가장 큰 종류 MaxLoops개만 (D-2), 나머지는 내림.</summary>
        private void ApplyLoops()
        {
            for (int k = 0; k < _order.Length; k++)
                _order[k] = k;
            System.Array.Sort(_order, (a, b) => _want[b].CompareTo(_want[a]));
            for (int r = 0; r < _order.Length; r++)
            {
                int k = _order[r];
                var loop = _loops[k];
                if (loop == null)
                    continue;
                loop.Target = r < ExteriorFxSound.MaxLoops ? _want[k] : 0f;
                if (_want[k] > 0f)
                {
                    loop.Source.panStereo = _pan[k];
                    _filters[k].cutoffFrequency = _cut[k];
                }
            }
        }

        // ---------------- 한 번 소리 (먹먹하게) ----------------

        private void Shot(SoundCue cue, Vector3 at, float gain = 1f, float delay = 0f)
        {
            if (Muted || cue == null || !cue.HasClip)
                return;
            var v = _camera.WorldToViewportPoint(at);
            float d = Vector3.Distance(_camera.transform.position, at);
            float level = ExteriorFxSound.Level(d, v) * gain;
            if (level < 0.03f)
                return;
            float now = Time.unscaledTime;
            float when = now + delay;
            if (Mathf.Abs(when - cue.LastPlayed) < cue.Cooldown)
                return;
            int pick = -1;
            for (int i = 0; i < ShotVoices; i++)
                if (_shotBusy[i] <= now && (pick < 0 || _shotBusy[i] < _shotBusy[pick]))
                    pick = i;
            if (pick < 0)
                return;   // 몰리면 생략 (바깥 연출은 낮은 우선순위)
            var clip = cue.Pick();
            if (clip == null)
                return;
            cue.LastPlayed = when;
            float pitch = Random.Range(cue.Pitch.x, cue.Pitch.y);
            var s = _shots[pick];
            s.Stop();
            s.clip = clip;
            s.pitch = pitch;
            s.panStereo = ExteriorFxSound.Pan(v);
            s.volume = cue.Volume * level * AudioVolumes.For(cue.Bus);
            _shotFilters[pick].cutoffFrequency = ExteriorFxSound.Cutoff(d);
            _shotBusy[pick] = when + clip.length / Mathf.Max(0.05f, pitch) + 0.05f;
            if (delay > 0f)
                s.PlayDelayed(delay);
            else
                s.Play();
        }

        // ---------------- 오가는 배 (D-12) ----------------

        private void HandleShip(Transform ship, AmbientShipKind kind)
        {
            if (ship == null || _lib.ExtShipEngine == null || !_lib.ExtShipEngine.HasClip)
                return;
            Ship slot = null;
            foreach (var s in _ships)
                if (s.T == null)
                {
                    slot = s;
                    break;
                }
            if (slot == null)
            {
                if (_ships.Count >= MaxShips)
                    return;
                var loop = _audio.CreateLoop(_lib.ExtShipEngine);
                loop.FadeSpeed = 1.5f;
                slot = new Ship { Loop = loop, Filter = loop.Source.gameObject.AddComponent<AudioLowPassFilter>() };
                _ships.Add(slot);
            }
            slot.T = ship;
            slot.Last = ship.position;
            slot.Speed = 0f;
            slot.Size = kind == AmbientShipKind.Dock ? 1.15f : kind == AmbientShipKind.Supply ? 0.85f : 1f;
            if (slot.Loop.Source.clip != null)
                slot.Loop.Source.time = Random.Range(0f, slot.Loop.Source.clip.length * 0.9f);
        }

        private void UpdateShips()
        {
            float dt = Time.deltaTime;
            foreach (var s in _ships)
            {
                if (s.T == null)
                {
                    s.Loop.Target = 0f;
                    continue;
                }
                Vector3 p = s.T.position;
                if (dt > 0f)
                    s.Speed = Mathf.Lerp(s.Speed, Vector3.Distance(p, s.Last) / dt, 0.2f);
                s.Last = p;
                if (Muted)
                {
                    s.Loop.Target = 0f;
                    continue;
                }
                var v = _camera.WorldToViewportPoint(p);
                float d = Vector3.Distance(_camera.transform.position, p);
                s.Loop.Target = ExteriorFxSound.Level(d, v) * ExteriorFxSound.EngineLevel(s.Speed);
                s.Loop.Pitch = ExteriorFxSound.EnginePitch(s.Speed, s.Size);
                s.Loop.Source.panStereo = ExteriorFxSound.Pan(v);
                s.Filter.cutoffFrequency = ExteriorFxSound.Cutoff(d);
            }
        }
    }
}
