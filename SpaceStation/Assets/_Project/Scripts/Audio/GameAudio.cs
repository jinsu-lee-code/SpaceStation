using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using SpaceStation.UI;
using UnityEngine;

namespace SpaceStation.Audio
{
    /// <summary>
    /// 5-8 게임 소리 연결. 시뮬레이션·건설·연출 이벤트를 소리로 바꾸고, 반복음(정거장 험·폭풍·경보)과
    /// 상태별 음악(낮/밤/위기)을 관리한다. 판정은 바꾸지 않는다.
    /// 좌우 위치: 화면에서 보이는 위치에 따라 살짝 패닝.
    /// </summary>
    public sealed class GameAudio : MonoBehaviour
    {
        [SerializeField] private StationController _station;
        [SerializeField] private BuildController _build;
        [SerializeField] private ModuleSelectionController _selection;
        [SerializeField] private SelectionActionsPanel _actions;
        [SerializeField] private SimulationClock _clock;
        [SerializeField] private MeteorFx _meteors;
        [SerializeField] private SolarStormFx _storm;
        [SerializeField] private Camera _camera;

        [Header("Timing")]
        [Tooltip("meteor_incoming 클립에서 소리가 가장 큰 지점(초) — 운석 도착 순간에 맞춘다")]
        [SerializeField] private float _incomingPeak = 1.28f;
        [Tooltip("보급선 소리 지연 (도착, 출발) — AmbientSpace 비행 시간에 맞춤")]
        [SerializeField] private Vector2 _supplyDelays = new Vector2(1.7f, 8.2f);
        [Tooltip("음악 상태가 이 시간(초) 이상 유지되어야 곡을 바꾼다")]
        [SerializeField] private float _musicHold = 3f;

        [Header("바깥 정거장 험 3단계 (2026-11-25, SOUND_LIST_NEW 4-4)")]
        [Tooltip("두께 1 · 2 · 3단계에 닿는 등급 번호 (StationGrades 순서: 1 소형 · 3 대형 · 5 국가급). 모듈 수 기준도 그 등급의 모듈 수 조건을 씀")]
        [SerializeField] private int[] _humTierGrades = { 1, 3, 5 };
        [Tooltip("두께가 바뀌는 빠르기 (단계 / 실시간 초). 모듈 하나를 지을 때 툭 바뀌지 않게 천천히 (0.05 = 20초에 한 단계)")]
        [SerializeField] private float _humTierSpeed = 0.05f;
        [Tooltip("들어 보기용: 0 ~ 3이면 두께를 이 값으로 고정 (플레이 중 끌어 단계 1 · 2 · 3을 들어 봄). 평소 -1 = 정거장 크기대로")]
        [SerializeField, Range(-1f, 3f)] private float _humThicknessPreview = -1f;

        // Interior = 1인칭 내부: 바깥 음악을 끄고 내부 음악 층(InteriorAudio)에 넘김 (2026-11-23). 위기 음악은 안에서도 그대로.
        private enum MusicState { None, Day, Night, Crisis, Interior }

        private AudioService _audio;
        private SoundLibrary _lib;
        private StationSimulation _sim;
        private AudioService.Loop _hum;
        private AudioService.Loop _stormLoop;
        private AudioService.Loop _alarm;
        private MusicState _musicState;
        private MusicState _wantedState;
        private float _wantedFor;
        private bool _gameOver;
        private float _restoreMusicAt;
        private Interior.InteriorMode _interior;
        private bool _inside;
        private const float InsideHum = 0.25f;
        // 바깥 정거장 험 3단계: StationHum 위에 정거장 크기 (모듈 수 · 등급)로 이웃 단계끼리 교차하는 층 (ExteriorHum)
        private AudioService.Loop[] _humTiers;
        private int[] _humTierModules;
        private float _humThickness = -1f;
        // 경보 위계 (2026-11-25, SOUND_LIST_NEW D-3): 소리가 없던 경고 (역병 · 파업 · 외부 접촉 · 침입 드론)의 단계 변화 · 자원 바닥 반복 막기
        private AlertWatch _alertWatch;
        // 바깥 알림 (2026-11-26, SOUND_LIST_NEW 2번 · NoticeSound): 연구 완료 · 연결 끊김 · 과열 · 낮밤
        private NoticeWatch _noticeWatch;
        // 방 장치 · 바깥 일 (2026-11-26 작업자 P5, SOUND_LIST_NEW 4-4 · D-10 · D-4): 1인칭 내부에서 바깥 일이 선체 너머로 (InteriorOutsideSound) · 바깥에서 모듈 가까이 줌 = 그 방 바탕 (ExteriorZoomBed)
        private ExteriorZoomBed _zoomBed;
        private readonly System.Collections.Generic.Dictionary<ResourceType, float> _depletedAlertAt = new System.Collections.Generic.Dictionary<ResourceType, float>();
        private const float DepletedRepeat = 20f;
        private const float AlarmUrgentSeconds = 10f;
        // 합성 · 이민선 (2026-11-25, SOUND_LIST_NEW 2번): 진행 중 합성 루프 하나 (가장 가까운 합성) · 이민선 방문 수 변화 · 나온 순간 (선택 소리 겹침 막기)
        private AudioService.Loop _synthLoop;
        private readonly System.Collections.Generic.Dictionary<SynthesisJob, Vector3> _synthCenters = new System.Collections.Generic.Dictionary<SynthesisJob, Vector3>();
        private int _immigrationVisits = -1;
        private float _exitedAt = -9f;
        private const float SynthNear = 10f, SynthFar = 45f;   // 카메라 ~ 합성 거리: 이 안 = 그대로, 밖 = 들리지 않음

        // 같은 프레임에 몰리는 알림을 하나로 (일시정지+배속, 등급 상승+완성)
        private int _pendingClock; // 0 없음, 1 일시정지, 2 재개, 3 배속
        private float _pendingSpeedPitch = 1f;
        private float _lastSpeed = 1f;

        private void Start()
        {
            _audio = AudioService.Instance;
            if (_audio == null || _audio.Library == null || _station == null)
            {
                enabled = false;
                return;
            }
            _lib = _audio.Library;
            _sim = _station.Simulation;
            if (_camera == null)
                _camera = Camera.main;

            _hum = _audio.CreateLoop(_lib.StationHum);
            _hum.FadeSpeed = 0.3f;
            _hum.Target = 1f;
            _humTiers = new[] { _audio.CreateLoop(_lib.ExtHum1), _audio.CreateLoop(_lib.ExtHum2), _audio.CreateLoop(_lib.ExtHum3) };
            foreach (var tier in _humTiers)
                tier.FadeSpeed = 0.3f;
            var grades = _sim.Progression;
            _humTierModules = new int[ExteriorHum.Tiers];
            for (int k = 0; k < ExteriorHum.Tiers; k++)
            {
                int g = k < _humTierGrades.Length ? _humTierGrades[k] : grades.GradeCount - 1;
                _humTierModules[k] = grades.GetGrade(Mathf.Clamp(g, 0, grades.GradeCount - 1)).MinModules;
            }
            _stormLoop = _audio.CreateLoop(_lib.StormLoop);
            _stormLoop.FadeSpeed = 0.5f;
            _alarm = _audio.CreateLoop(_lib.AlarmLoop);
            _alarm.FadeSpeed = 2f;
            _synthLoop = _audio.CreateLoop(_lib.SynthLoop);
            _synthLoop.FadeSpeed = 1.2f;
            _lastSpeed = _clock != null ? _clock.Clock.Speed : 1f;

            // 11-9: 내부 방문 중에는 바깥 정거장 험을 낮춤 (방 환경음이 들리게)
            _interior = FindFirstObjectByType<Interior.InteriorMode>();
            if (_interior != null)
                _interior.InsideChanged += HandleInsideChanged;

            Subscribe(true);
            _alertWatch = new AlertWatch(_sim);
            _alertWatch.Bind(true);
            _noticeWatch = new NoticeWatch(_sim);
            _noticeWatch.Bind(true);
            // 바깥 진화 연출 · 오가는 배 (2026-11-26 작업자 P4, SOUND_LIST_NEW 3번 · D-4 · D-12): 가까이 · 화면 가운데만 · 먹먹하게
            gameObject.AddComponent<ExteriorFxAudio>().Init(_station, _camera, _interior);
            // 바깥 일이 안에서 · 줌 바탕 (2026-11-26 작업자 P5, SOUND_LIST_NEW 4-4 · D-10 · D-4)
            gameObject.AddComponent<Interior.InteriorOutsideSound>().Bind(_station, _meteors, _storm, _interior);
            // 주민 소리 (2026-11-26 작업자 P6, SOUND_LIST_NEW 4-3 · 4-4 · D-2 · D-5 ④ · D-11): 목소리 · 웅성 · 활동 — 1인칭 내부 지금 방만
            gameObject.AddComponent<Interior.InteriorResidentSound>().Bind(_station, _meteors, _interior);
            _zoomBed = new ExteriorZoomBed(transform, _camera, _sim, _interior, _lib);
        }

        private void HandleInsideChanged(bool inside)
        {
            _inside = inside;
            if (!inside)
                _exitedAt = Time.unscaledTime;   // 나오며 마지막 방을 고르는 선택 소리가 나가기 소리와 겹치지 않게 (HandleModuleSelection)
        }

        private void OnDestroy()
        {
            if (_lib != null)
                Subscribe(false);
            _alertWatch?.Bind(false);
            _noticeWatch?.Bind(false);
            if (_interior != null)
                _interior.InsideChanged -= HandleInsideChanged;
        }

        private void Subscribe(bool on)
        {
            if (_sim != null)
            {
                if (on)
                {
                    _sim.Damage.Damaged += HandleDamaged;
                    _sim.Damage.RepairStarted += HandleRepairStarted;
                    _sim.Damage.Repaired += HandleRepaired;
                    _sim.Damage.Destroyed += HandleDestroyed;
                    _sim.Durability.WornOut += HandleDestroyed;
                    _sim.Durability.Maintained += HandleMaintained;
                    _sim.Events.EventStarted += HandleEventStarted;
                    _sim.Events.EventEnded += HandleEventEnded;
                    _sim.Progression.GradeChanged += HandleGradeChanged;
                    _sim.Progression.VictoryReached += HandleVictory;
                    _sim.Session.GameOver += HandleGameOver;
                    _sim.Resources.DepletionChanged += HandleDepletion;
                    _sim.Synthesis.Started += HandleSynthStarted;
                    _sim.Synthesis.Completed += HandleSynthCompleted;
                    _sim.Synthesis.Cancelled += HandleSynthCancelled;
                    _sim.Automation.Performed += HandleAutomationPerformed;   // 2026-11-26 바깥 물건: 정비 D-2 · M-49
                    _sim.Cargo.Delivered += HandleCargoDelivered;
                    _sim.Supply.Changed += HandleSupplyChanged;
                    foreach (var crate in _sim.Supply.Crates)
                        _lastCrateId = Mathf.Max(_lastCrateId, crate.Id);
                }
                else
                {
                    _sim.Damage.Damaged -= HandleDamaged;
                    _sim.Damage.RepairStarted -= HandleRepairStarted;
                    _sim.Damage.Repaired -= HandleRepaired;
                    _sim.Damage.Destroyed -= HandleDestroyed;
                    _sim.Durability.WornOut -= HandleDestroyed;
                    _sim.Durability.Maintained -= HandleMaintained;
                    _sim.Events.EventStarted -= HandleEventStarted;
                    _sim.Events.EventEnded -= HandleEventEnded;
                    _sim.Progression.GradeChanged -= HandleGradeChanged;
                    _sim.Progression.VictoryReached -= HandleVictory;
                    _sim.Session.GameOver -= HandleGameOver;
                    _sim.Resources.DepletionChanged -= HandleDepletion;
                    _sim.Synthesis.Started -= HandleSynthStarted;
                    _sim.Synthesis.Completed -= HandleSynthCompleted;
                    _sim.Synthesis.Cancelled -= HandleSynthCancelled;
                    _sim.Automation.Performed -= HandleAutomationPerformed;
                    _sim.Cargo.Delivered -= HandleCargoDelivered;
                    _sim.Supply.Changed -= HandleSupplyChanged;
                }
            }
            if (_build != null)
            {
                if (on)
                {
                    _build.SelectionChanged += HandleBuildSelection;
                    _build.CategoryChanged += HandleCategory;
                    _build.Rotated += HandleRotated;
                    _build.Placed += HandlePlaced;
                    _build.PlaceRejected += HandlePlaceRejected;
                    _build.Moved += HandleMoved;
                }
                else
                {
                    _build.SelectionChanged -= HandleBuildSelection;
                    _build.CategoryChanged -= HandleCategory;
                    _build.Rotated -= HandleRotated;
                    _build.Placed -= HandlePlaced;
                    _build.PlaceRejected -= HandlePlaceRejected;
                    _build.Moved -= HandleMoved;
                }
            }
            if (_selection != null)
            {
                if (on)
                {
                    _selection.SelectionChanged += HandleModuleSelection;
                    _selection.Removed += HandleRemoved;
                    _selection.RemoveRejected += HandleRemoveRejected;
                }
                else
                {
                    _selection.SelectionChanged -= HandleModuleSelection;
                    _selection.Removed -= HandleRemoved;
                    _selection.RemoveRejected -= HandleRemoveRejected;
                }
            }
            if (_actions != null)
            {
                if (on)
                {
                    _actions.ActionFailed += HandleActionFailed;
                    _actions.Rebuilt += HandleRebuilt;
                }
                else
                {
                    _actions.ActionFailed -= HandleActionFailed;
                    _actions.Rebuilt -= HandleRebuilt;
                }
            }
            if (_clock != null && _clock.Clock != null)
            {
                if (on)
                {
                    _clock.Clock.PausedChanged += HandlePaused;
                    _clock.Clock.SpeedChanged += HandleSpeed;
                }
                else
                {
                    _clock.Clock.PausedChanged -= HandlePaused;
                    _clock.Clock.SpeedChanged -= HandleSpeed;
                }
            }
            if (_meteors != null)
            {
                if (on)
                {
                    _meteors.Launched += HandleMeteorLaunched;
                    _meteors.Deflected += HandleDeflected;
                    _meteors.LaserFired += HandleLaser;
                    _meteors.Exploded += HandleExploded;
                    _meteors.Impacted += HandleImpacted;
                    _meteors.PassedAway += HandlePassedAway;
                }
                else
                {
                    _meteors.Launched -= HandleMeteorLaunched;
                    _meteors.Deflected -= HandleDeflected;
                    _meteors.LaserFired -= HandleLaser;
                    _meteors.Exploded -= HandleExploded;
                    _meteors.Impacted -= HandleImpacted;
                    _meteors.PassedAway -= HandlePassedAway;
                }
            }
            if (_storm != null)
            {
                if (on)
                    _storm.Sparked += HandleSpark;
                else
                    _storm.Sparked -= HandleSpark;
            }
        }

        // ---------------- 매 프레임: 반복음·음악 ----------------

        private void Update()
        {
            float remaining = CrisisRemaining();
            bool crisis = remaining >= 0f && !_gameOver;
            _alarm.Target = crisis ? 1f : 0f;
            // 남은 시간 10초부터 경보가 높고 빨라짐 — 2026-11-25 경보 위계: 고리 음 (라 · 솔 · 레)이 조 안에 남게 5도 위 한 계단 (미 · 레 · 라, 1.5배 빠름).
            // 예전 1 → 1.12배 연속은 반음 사이 음이 되어 위기 음악 (D 단조)과 어긋났음
            _alarm.Pitch = crisis && remaining <= AlarmUrgentSeconds ? Mathf.Pow(2f, 7f / 12f) : 1f;
            _stormLoop.Target = !_gameOver && StormActive() ? 1f : 0f;
            float hum = (_gameOver ? 0.35f : 1f) * (_inside ? InsideHum : 1f);
            _hum.Target = hum;
            UpdateHumTiers(hum);
            UpdateSynthLoop();
            UpdateImmigration();
            _noticeWatch.Tick(_inside, _gameOver);
            _zoomBed.Tick(_inside, _gameOver);   // D-4 바깥 줌 = 그 방 바탕 (P5)

            if (_restoreMusicAt > 0f && Time.unscaledTime >= _restoreMusicAt)
            {
                _restoreMusicAt = 0f;
                _audio.DuckMusic(1f);
            }
            UpdateMusic(crisis);
        }

        private void UpdateMusic(bool crisis)
        {
            if (_gameOver)
                return;
            bool day = !_sim.DayNight.Enabled || _sim.DayNight.IsDay(_sim.ElapsedSeconds);
            var wanted = crisis ? MusicState.Crisis : _inside ? MusicState.Interior : day ? MusicState.Day : MusicState.Night;
            if (wanted != _wantedState)
            {
                _wantedState = wanted;
                _wantedFor = 0f;
            }
            _wantedFor += Time.unscaledDeltaTime;
            // 처음 한 번은 바로, 이후에는 잠깐 유지되어야 전환 (경계에서 왔다 갔다 방지). 내부 들어가기 · 나오기는 바로
            bool doorway = wanted == MusicState.Interior || _musicState == MusicState.Interior;
            if (wanted == _musicState || (_musicState != MusicState.None && !doorway && _wantedFor < _musicHold))
                return;
            _musicState = wanted;
            _audio.PlayMusic(wanted == MusicState.Interior ? null
                : wanted == MusicState.Crisis ? _lib.MusicCrisis
                : wanted == MusicState.Night ? _lib.MusicNight : _lib.MusicDay);
        }

        private void LateUpdate()
        {
            if (_pendingClock == 0)
                return;
            int pending = _pendingClock;
            _pendingClock = 0;
            if (_gameOver || (_clock != null && _clock.InputLocked))
                return; // 결과 화면이 멈춘 것
            if (pending == 1)
                _audio.Play(_lib.UiPause);
            else if (pending == 2)
                _audio.Play(_lib.UiResume);
            else
                _audio.Play(_lib.UiSpeedUp, pitch: _pendingSpeedPitch);
        }

        private float CrisisRemaining()
        {
            var f = _sim.Failure;
            return MinActive(f.OxygenRemaining, MinActive(f.SatisfactionRemaining, f.CoreRemaining));
        }

        /// <summary>−1(진행 안 함)을 무시한 최소값.</summary>
        private static float MinActive(float a, float b)
        {
            if (a < 0f) return b;
            if (b < 0f) return a;
            return Mathf.Min(a, b);
        }

        private bool StormActive()
        {
            if (_storm != null)
                return _storm.StormActive;
            if (_sim.Radiation != null && _sim.Radiation.StormActive)
                return true; // M-122
            foreach (var a in _sim.Events.ActiveEvents)
            {
                if (a.Data is SolarStormEventData)
                    return true;
            }
            return false;
        }

        /// <summary>바깥 정거장 험 3단계: 두께 (모듈 수 · 등급)를 천천히 따라가며 이웃 단계끼리 같은 힘으로 교차 — 바탕 험과 같은 배율 (내부 · 게임 오버).</summary>
        private void UpdateHumTiers(float level)
        {
            float size = ExteriorHum.Thickness(_sim.Grid.ModuleCount, _sim.Progression.GradeIndex, _humTierModules, _humTierGrades);
            if (_humThicknessPreview >= 0f)
                _humThickness = _humThicknessPreview;
            else
                _humThickness = _humThickness < 0f ? size : Mathf.MoveTowards(_humThickness, size, _humTierSpeed * Time.unscaledDeltaTime);
            for (int k = 0; k < _humTiers.Length; k++)
                _humTiers[k].Target = ExteriorHum.TierWeight(_humThickness, k) * level;
        }

        // ---------------- 위치 ----------------

        private float Pan(Vector3 world)
        {
            if (_camera == null)
                return 0f;
            var v = _camera.WorldToViewportPoint(world);
            if (v.z < 0f)
                return 0f;
            return Mathf.Clamp((v.x - 0.5f) * 1.3f, -0.65f, 0.65f);
        }

        private float Pan(ModuleInstance module)
        {
            if (module == null)
                return 0f;
            Vector3 sum = Vector3.zero;
            foreach (var c in module.Cells)
                sum += GridConfig.CellToWorld(c);
            return Pan(sum / Mathf.Max(1, module.Cells.Count));
        }

        // ---------------- 건설·선택 ----------------

        private void HandleBuildSelection(ModuleData data)
        {
            if (data != null && _build.Moving != null)
                PlayMove(_lib.ModuleMovePick, _build.Moving);   // 2026-11-26 옮기기 시작 (BeginMove가 모듈을 들고 고스트로) = 집어 올림
            else if (data != null)
                _audio.Play(_lib.BuildSelect);
            else
                _audio.Play(_lib.UiClose, 0.6f);
        }

        // ---------------- 모듈 옮기기 (사운드 개편 2026-11-26, SOUND_LIST_NEW 2번 · 12-3b — 바깥 2D · 화면 좌우, 크기 3갈래 = BuildSound.MoveTone) ----------------

        private int _movedFrame = -1;

        private void PlayMove(SoundCue cue, ModuleInstance module)
        {
            var (pitch, volume) = BuildSound.MoveTone(BuildSound.SizeOf(module, _sim.Synthesis));
            _audio.Play(cue, volume, Pan(module), pitch: pitch);
        }

        /// <summary>옮기기 확정 = 내려놓음. 같은 클릭의 이동 모드 끝 (창 닫힘 0.6) · 옮긴 모듈 다시 선택 (창 열기 0.7)은 멈추거나 건너뜀 — 소리 하나.</summary>
        private void HandleMoved(ModuleInstance module)
        {
            _movedFrame = Time.frameCount;
            _audio.StopStartedThisFrame(_lib.UiClose);
            _audio.StopStartedThisFrame(_lib.UiOpen);
            PlayMove(_lib.ModuleMoveDrop, module);
        }

        private void HandleCategory(ModuleCategory _) => _audio.Play(_lib.UiTab);
        private void HandleRotated() => _audio.Play(_lib.BuildRotate);

        // 2026-11-25 사운드 개편 1-1: 배치 · 철거 = 모듈 크기 3갈래 (D-6 — 1칸 · 여러 칸 · 진화), 0.15초 안 여러 번 = 한 번 (D-2, BuildSound)
        private void HandlePlaced(ModuleInstance module)
        {
            if (!BuildSound.AdmitPlace(Time.unscaledTime))
                return;
            float pan = Pan(module);
            _audio.Play(BuildSound.PlaceCue(_lib, BuildSound.SizeOf(module, _sim.Synthesis)), pan: pan);
            _audio.Play(_lib.BuildConnect, pan: pan, delay: 0.28f); // 통로가 이어지는 소리
        }

        private void HandlePlaceRejected(PlacementResult _) => _audio.Play(_lib.UiError);

        private void HandleModuleSelection(ModuleInstance module)
        {
            if (module != null && Time.unscaledTime - _exitedAt > 0.2f && _movedFrame != Time.frameCount)   // 내부에서 나오며 고른 마지막 방 · 옮기기 확정으로 다시 고른 모듈은 소리 없이
                _audio.Play(_lib.UiOpen, 0.7f);
        }

        private void HandleRemoved(ModuleInstance module)
        {
            if (BuildSound.AdmitRemove(Time.unscaledTime))
                _audio.Play(BuildSound.RemoveCue(_lib, BuildSound.SizeOf(module, _sim.Synthesis)), pan: Pan(module));
        }
        private void HandleRemoveRejected(ModuleInstance _) => _audio.Play(_lib.UiError);
        private void HandleActionFailed(string _) => _audio.Play(_lib.UiError);

        private void HandleRebuilt(ModuleInstance module)
        {
            float pan = Pan(module);
            var size = BuildSound.SizeOf(module, _sim.Synthesis);   // 재건축은 같은 모듈 — 철거 · 배치 같은 크기
            float now = Time.unscaledTime;
            if (BuildSound.AdmitRemove(now))
                _audio.Play(BuildSound.RemoveCue(_lib, size), 0.8f, pan);
            if (BuildSound.AdmitPlace(now))
                _audio.Play(BuildSound.PlaceCue(_lib, size), pan: pan, delay: 0.35f);
        }

        // ---------------- 수리·파손 ----------------

        private void HandleDamaged(DamageInfo info)
        {
            if (_station.IsDamageHeld(info.Module))
                return; // 운석 충돌 순간에 소리 (HandleImpacted)
            _audio.Play(_lib.ModuleDamaged, pan: Pan(info.Module)); // 2026-11-25 균열만 (앞 "삐빅"은 위험 경보로)
            AlertSound.TryPlay(AlertLevel.Danger);
        }

        private void HandleRepairStarted(DamageInfo info) => _audio.Play(_lib.RepairStart, pan: Pan(info.Module));
        private void HandleRepaired(ModuleInstance module)
        {
            // 2026-11-25: 1인칭 현장 수리를 F 길게 누르기로 끝낸 순간이면 그 완료 화음 (hold_done) 하나만 — 수리 완료음과 겹치지 않게
            if (!AlertSound.PlayedThisFrame(l => l.HoldDone))
                _audio.Play(_lib.RepairDone, pan: Pan(module));
        }
        private void HandleDestroyed(ModuleInstance module)
        {
            _audio.Play(_lib.ModuleDestroyed, pan: Pan(module));
            AlertSound.TryPlay(AlertLevel.Critical); // 경보 위계: 모듈 파괴 = 치명
        }
        private void HandleMaintained(DurabilityInfo info) => _audio.Play(_lib.Maintain, pan: Pan(info.Module));

        /// <summary>
        /// 2026-11-26 정비 D-2 · M-49 (타이쿤판: main은 Automation.Executing으로 같은 규칙을 이미 가짐 — main에 옮길 땐 이 함수 대신 main 규칙 그대로):
        /// 정비 자동화가 같은 프레임에 모듈마다 정비 소리를 냈음 → 자동 정비 · 재건축은 무음 (막 울린 정비 소리를 멈춤), 일괄 정비는 한 번 (같은 프레임 나머지는 큐 쉬는 시간에 막힘).
        /// </summary>
        private void HandleAutomationPerformed(int maintained, int rebuilt, bool batch)
        {
            if (!batch)
                _audio.StopStartedThisFrame(_lib.Maintain);
            else if (maintained == 0)
                _audio.Play(_lib.Maintain);   // 재건축만 한 일괄 처리도 누른 것이니 한 번
        }

        // ---------------- 운석·방어·폭풍 ----------------

        // 2026-11-25 사운드 개편 1-1 (운석 · 방어 · 파손): 충돌 = 맞은 곳 3 (선체 · 장갑 · 실드) x 크기 2 (큰 = 1초에 한 번까지, 그 사이 = 작은 — MeteorSound),
        // 화면 가운데서 멀수록 작게 (가장자리 · 밖 0.6배, D-2). 1인칭 내부에서는 바깥이 안 보이므로 접근 "휙" · 레이저는 안 울리고 나머지는 절반 · 가운데 (D-10 먹먹한 쿵은 B 묶음)
        private const float InsideMeteor = 0.5f;
        private float _lastLargeImpactAt = -99f;

        private float MeteorGain(Vector3 world)
        {
            if (_inside)
                return InsideMeteor;
            return _camera != null ? MeteorSound.CenterGain(_camera.WorldToViewportPoint(world)) : 1f;
        }

        private float MeteorPan(Vector3 world) => _inside ? 0f : Pan(world);

        private bool NextImpactLarge()
        {
            float now = Time.unscaledTime;
            if (!MeteorSound.IsLarge(now, _lastLargeImpactAt))
                return false;
            _lastLargeImpactAt = now;
            return true;
        }

        // 2026-11-26 작업자 P4 (SOUND_LIST_NEW 2번 B): 습격 포격탄 = 휘파람 + "쿠르릉 펑" (운석과 같은 바위 연출이라 소리로 갈림), 실드 · 포탑 = 막은 · 쏜 모듈의 레시피 단계 (DefenseSound),
        // 실드에 튕겨 멀어지는 운석 = "휘우우" (D-12). 한 발의 정보는 MeteorFx가 알림을 부르는 동안만 (ShotRaidLabel · ShotDefender).
        private bool ShotIsRaid => _meteors != null && DefenseSound.IsRaidShell(_meteors.ShotRaidLabel != null, _meteors.ShotRaidLabel);

        private int DefenderTier()
        {
            var m = _meteors != null ? _meteors.ShotDefender : null;
            var recipe = m != null && m.Data != null ? _sim.Synthesis.RecipeFor(m.Data) : null;
            return DefenseSound.Tier(recipe != null ? recipe.Stage : 0);
        }

        private void HandleMeteorLaunched(Vector3 arrival, float seconds)
        {
            if (_inside)
                return;
            // 새 소리 (2026-11-25)의 가장 센 곳 = 1.28초 (0.1초 창 귀 감도) · 1.30초 (0.05초 창), 두 변형 같은 자리 — _incomingPeak 1.28 그대로
            bool raid = ShotIsRaid;
            float peak = raid ? DefenseSound.RaidPeak : _incomingPeak;
            _audio.Play(DefenseSound.IncomingCue(_lib, raid), MeteorGain(arrival), Pan(arrival), Mathf.Max(0f, seconds - peak));
        }

        /// <summary>실드 구면에 닿음 = 운석 쪽 몸통 (실드 충돌 크기 2갈래) + 실드 쪽 "우웅-팅" (단계마다 한 겹씩 더 — ShieldDeflect · Hex · Aurora · Crystal) 함께.</summary>
        private void HandleDeflected(Vector3 at)
        {
            float gain = MeteorGain(at), pan = MeteorPan(at);
            _audio.Play(MeteorSound.ImpactCue(_lib, MeteorSurface.Shield, NextImpactLarge()), gain, pan);
            _audio.Play(DefenseSound.ShieldCue(_lib, DefenderTier()), gain, pan);
        }

        private void HandleLaser(Vector3 muzzle, Vector3 _)
        {
            if (!_inside)
                _audio.Play(DefenseSound.LaserCue(_lib, DefenderTier()), MeteorGain(muzzle), Pan(muzzle));
        }

        /// <summary>공중 폭발: 포격탄 = "쿠르릉 펑" 작게 · 요새 포대 · 궤도 방어망 (별자리 · 수정 불꽃이 보임) = 불꽃 터짐 · 그 밖 = 운석 "퍽".</summary>
        private void HandleExploded(Vector3 at)
        {
            float gain = MeteorGain(at), pan = MeteorPan(at);
            if (ShotIsRaid && _lib.RaidImpact.HasClip)
                _audio.Play(_lib.RaidImpact, DefenseSound.RaidAirburstGain * gain, pan, 0.03f);
            else if (DefenderShowsFireworks() && _lib.FxFireworkBurst.HasClip)
                _audio.Play(_lib.FxFireworkBurst, gain, pan, 0.03f);
            else
                _audio.Play(_lib.MeteorExplode, gain, pan, 0.03f);
        }

        private bool DefenderShowsFireworks()
        {
            var m = _meteors != null ? _meteors.ShotDefender : null;
            return m != null && _station.TryGetView(m, out var view) && view.GetComponentInChildren<FireworkFx>() != null;
        }

        /// <summary>실드에 튕겨 우주로 멀어짐 (바깥만, 실드 소리 뒤 0.2초 — 접근 "휙" 꼬리 위에 잦아드는 쪽만).</summary>
        private void HandlePassedAway(Vector3 from)
        {
            if (!_inside)
                _audio.Play(_lib.ExtMeteorPass, MeteorGain(from), Pan(from), 0.2f);
        }

        private void HandleImpacted(Vector3 at, ModuleInstance hit)
        {
            float gain = MeteorGain(at), pan = MeteorPan(at);
            // 파손은 판정 순간 (연출보다 1.6초 앞)에 이미 적용됨 — 지금 파손이 아니면 방어 연구로 면한 충돌 (예전엔 면해도 균열 · 위험 경보가 울렸음)
            bool damaged = hit != null && _sim.Damage.IsDamaged(hit);
            var surface = MeteorSound.Surface(hit != null, hit != null && hit.Data != null && hit.Data.Armored, damaged);
            bool large = NextImpactLarge();
            bool raid = ShotIsRaid;
            // 포격탄 = 한 갈래 "쿠르릉 펑" (몰리면 작게 0.7 — 운석 큰 · 작은 갈래와 같은 1초 규칙)
            _audio.Play(DefenseSound.ImpactCue(_lib, raid, surface, large), raid && !large ? 0.7f * gain : gain, pan);
            if (!damaged)
                return;
            if (MeteorSound.CrackOnImpact(surface, damaged))
                _audio.Play(_lib.ModuleDamaged, 0.8f * gain, pan, 0.12f);
            AlertSound.TryPlay(AlertLevel.Danger); // 경보 위계: 모듈 파손 = 위험 (유성우에 몰려도 큐 쉬는 시간 3초)
        }

        // 2026-11-26: 바깥 태양광 판 스파크는 1인칭 내부에선 안 울림 (판이 안 보임 — 운석 레이저 · 접근 "휙"과 같은 규칙, 내부 스파크는 방 상태 층 · 사고가 따로)
        private void HandleSpark(Vector3 at)
        {
            if (!_inside)
                _audio.Play(_lib.StormSpark, Random.Range(0.6f, 1f), Pan(at));
        }

        // ---------------- 화물선 · 보급 상자 (사운드 개편 2026-11-26, SOUND_LIST_NEW 2번 — 바깥 2D · 화면 좌우) ----------------

        private const float CargoDockDelay = 1.5f;   // 화물선이 4.5초에 입구 앞에 섬 (AmbientSpace.DockShip) · 소리 3.0초 = 멈춤 "쿵"
        private const float CrateLandDelay = 5.6f;   // 보급선이 5초에 정거장 옆에 머묾 (AmbientSpace.SupplyShip) → 짐을 내림
        private int _lastCrateId = -1;

        /// <summary>화물 터미널 화물선 도킹: 바깥 = 터미널 쪽 좌우, 내부에선 그 터미널 방에 있을 때만 (도킹이 바로 벽 너머 — 이민선과 같은 규칙).</summary>
        private void HandleCargoDelivered(ModuleInstance terminal, ResourceType _, float __)
        {
            if (_gameOver || terminal == null)
                return;
            if (_inside)
            {
                if (Interior.InteriorRoomDisplay.PlayerIn(terminal))
                    _audio.Play(_lib.ShipCargoDock, 0.7f, delay: CargoDockDelay);
                return;
            }
            _audio.Play(_lib.ShipCargoDock, pan: Pan(terminal), delay: CargoDockDelay);
        }

        /// <summary>
        /// 보급 상자 생김 (보급선 도착 = SupplyCrateSystem.Spawn): 새 상자 (지난번보다 큰 번호)가 생긴 때만 — 줍기 · 회수 · 불러오기는 무음.
        /// 바깥 = 배가 머무는 순간 2D 한 번 (둘 넘으면 0.22초 뒤 한 번 더 — 개수만큼 조금 두껍게, D-2), 안 = InteriorSupply가 그 방 상자 자리에서 3D.
        /// </summary>
        private void HandleSupplyChanged()
        {
            int newest = 0, count = 0;
            ModuleInstance room = null;
            foreach (var crate in _sim.Supply.Crates)
            {
                if (crate.Id > _lastCrateId)
                {
                    count++;
                    room ??= crate.Module;
                }
                newest = Mathf.Max(newest, crate.Id);
            }
            _lastCrateId = Mathf.Max(_lastCrateId, newest);
            // 불러오기 직후 (Restore가 상자를 새 번호로 다시 놓음)는 무음
            if (count == 0 || _inside || _gameOver || Time.timeSinceLevelLoad < 3f)
                return;
            float pan = Pan(room);
            _audio.Play(_lib.CrateAppear, pan: pan, delay: CrateLandDelay);
            if (count > 1)
                _audio.Play(_lib.CrateAppear, 0.8f, pan, CrateLandDelay + 0.22f, 0.94f);
        }

        // ---------------- 이벤트·진행 ----------------

        private void HandleEventStarted(GameEventData data)
        {
            // 경보 위계 (2026-11-25, D-3): 나쁜 사건 (운석 · 태양 폭풍 · 산소 누출) = 위험, 좋은 사건 (보급선)은 위계 밖 그대로
            if (data.IsPositive)
                _audio.Play(_lib.EventPositive);
            else
                AlertSound.TryPlay(AlertLevel.Danger);
            if (data is OxygenLeakEventData)
                _audio.Play(_lib.OxygenLeak, delay: 0.6f);
            else if (data is SupplyShipEventData)
            {
                _audio.Play(_lib.SupplyArrive, delay: _supplyDelays.x);
                _audio.Play(_lib.SupplyArrive, 0.75f, delay: _supplyDelays.y, pitch: 0.92f);
            }
        }

        private void HandleEventEnded(ActiveEvent _) => AlertSound.TryPlay(AlertLevel.Info); // 경보 위계: 사건 끝남 = 안내 (예전 EventEnd 6.9 kHz "칭")

        private void HandleGradeChanged(int previous, int current)
        {
            var p = _sim.Progression;
            if (current > previous)
            {
                if (p.IsAtOrAboveVictory && !p.HasReachedVictory)
                    return; // 곧 VictoryReached → 완성 소리
                _audio.Play(_lib.GradeUp);
            }
            else
            {
                AlertSound.TryPlay(AlertLevel.Caution); // 경보 위계: 등급 하락 = 주의 (예전 GradeDown 사이렌)
            }
        }

        private void HandleVictory()
        {
            _audio.Play(_lib.Victory);   // 2026-11-26 바깥 알림: 밝은 성취 음악 3.4초 (예전 1차 합성 4.4초), 등급 상승 소리는 이 순간 안 울림 (HandleGradeChanged)
            _audio.DuckMusic(0.35f);
            _restoreMusicAt = Time.unscaledTime + 4f;
        }

        private void HandleGameOver()
        {
            _gameOver = true;
            _audio.Play(_lib.GameOver);
            _audio.PlayMusic(null);
        }

        private void HandleDepletion(ResourceType type, bool depleted)
        {
            if (!depleted || type == ResourceType.Metal)
                return;
            // 경보 위계: 자원 바닥 = 위험. 0 근처에서 오르내리면 매 틱 바뀌므로 같은 자원은 20초 안에 다시 울리지 않음 (D-2 반복 막기)
            float now = Time.unscaledTime;
            if (_depletedAlertAt.TryGetValue(type, out float at) && now - at < DepletedRepeat)
                return;
            _depletedAlertAt[type] = now;
            AlertSound.TryPlay(AlertLevel.Danger);
        }

        // ---------------- 합성 · 이민선 (사운드 개편 2026-11-25, SOUND_LIST_NEW 2번 — 바깥 2D · 화면 좌우, 내부에선 안 울림) ----------------

        private Vector3 SynthCenter(SynthesisJob job)
        {
            if (_synthCenters.TryGetValue(job, out var c))
                return c;
            var cells = StationGrid.ResolveCells(job.Recipe.Result.CellOffsets, job.Origin, job.Rotation);
            Vector3 sum = Vector3.zero;
            foreach (var cell in cells)
                sum += GridConfig.CellToWorld(cell);
            c = sum / Mathf.Max(1, cells.Length);
            _synthCenters[job] = c;
            return c;
        }

        /// <summary>합성 시작 = 비행 고리가 솟아 앉음 (SynthesisFx 시작 연출 1.4초와 같은 순간).</summary>
        private void HandleSynthStarted(SynthesisJob job)
        {
            if (_inside || _gameOver || job?.Recipe?.Result == null)
                return;
            _audio.Play(_lib.SynthStart, pan: Pan(SynthCenter(job)));
        }

        /// <summary>합성 완료 = 섬광 · 불꽃 터짐 · 고리가 날아오름 — 레시피 단계가 오를수록 화려하게 (4단계 = 3).</summary>
        private void HandleSynthCompleted(SynthesisJob job, ModuleInstance result)
        {
            float pan = job?.Recipe?.Result != null ? Pan(SynthCenter(job)) : Pan(result);
            _synthCenters.Remove(job);
            if (_inside || _gameOver || job?.Recipe == null)
                return;
            int stage = job.Recipe.Stage;
            _audio.Play(stage >= 3 ? _lib.SynthDone3 : stage == 2 ? _lib.SynthDone2 : _lib.SynthDone1, pan: pan);
        }

        private void HandleSynthCancelled(SynthesisJob job) => _synthCenters.Remove(job);

        /// <summary>진행 중 합성 루프 (용접 · 조립): 카메라에 가장 가까운 합성 하나 — 거리로 크기 · 화면 좌우, 파손 재료로 멈춤 · 게임 일시정지면 작게 (팔 · 불꽃이 멈춤).</summary>
        private void UpdateSynthLoop()
        {
            float level = 0f, pan = 0f;
            var jobs = _sim.Synthesis.Jobs;
            if (!_inside && !_gameOver && jobs.Count > 0 && _camera != null)
            {
                float best = float.MaxValue;
                SynthesisJob near = null;
                Vector3 at = Vector3.zero;
                foreach (var job in jobs)
                {
                    if (job?.Recipe?.Result == null)
                        continue;
                    var c = SynthCenter(job);
                    float d = Vector3.Distance(_camera.transform.position, c);
                    if (d < best)
                    {
                        best = d;
                        near = job;
                        at = c;
                    }
                }
                if (near != null)
                {
                    level = 1f - Mathf.Clamp01((best - SynthNear) / (SynthFar - SynthNear));
                    if (near.Paused || (_clock != null && _clock.Clock.IsPaused))
                        level *= 0.3f;
                    pan = Pan(at);
                }
            }
            _synthLoop.Target = level;
            if (_synthLoop.Source != null)
                _synthLoop.Source.panStereo = pan;
        }

        /// <summary>
        /// 이민선 도착 (이민 시계가 배를 부른 순간 — 주민이 내렸든 아니든): 가동 중인 여객 도킹 포트 쪽 좌우.
        /// 내부에선 그 포트 방에 있을 때만 (지금 있는 방 소리만 — 도킹이 바로 벽 너머).
        /// </summary>
        private void UpdateImmigration()
        {
            var im = _sim.Immigration;
            if (im == null)
                return;
            int visits = im.Visits;
            if (_immigrationVisits < 0 || visits < _immigrationVisits)
            {
                _immigrationVisits = visits;
                return;
            }
            if (visits == _immigrationVisits)
                return;
            _immigrationVisits = visits;
            if (_gameOver)
                return;
            ModuleInstance port = null;
            foreach (var m in _sim.Grid.Modules)
            {
                if (m.Data == null || !m.Data.ImmigrationPort || !_station.Connectivity.IsActive(m))
                    continue;
                if (!_inside || Interior.InteriorRoomDisplay.PlayerIn(m))
                {
                    port = m;
                    break;
                }
            }
            if (_inside)
            {
                if (port != null)
                    _audio.Play(_lib.ShipImmigrantArrive, 0.7f);
                return;
            }
            _audio.Play(_lib.ShipImmigrantArrive, pan: Pan(port));
        }

        // ---------------- 시간 조절 ----------------

        private void HandlePaused(bool paused) => _pendingClock = paused ? 1 : 2;

        private void HandleSpeed(float speed)
        {
            // 2026-11-25 UI 한 벌: 배속 소리 (도6으로 오르는 유리음)는 F 장조 음이라 음높이도 음계로 — 빨라짐 = 도6 (2배) · 레6 (4배), 느려짐 = 라5
            // (전엔 1.05 · 1.15 · 0.85배로 반음 사이 음이 되어 음악과 어긋났음)
            _pendingSpeedPitch = speed > _lastSpeed ? (speed >= 4f ? Mathf.Pow(2f, 2f / 12f) : 1f) : Mathf.Pow(2f, -3f / 12f);
            _lastSpeed = speed;
            if (_pendingClock == 0)
                _pendingClock = 3; // 재개와 함께 오면 재개 소리만
        }
    }
}
