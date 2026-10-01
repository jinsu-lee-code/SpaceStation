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

        private enum MusicState { None, Day, Night, Crisis }

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
            _stormLoop = _audio.CreateLoop(_lib.StormLoop);
            _stormLoop.FadeSpeed = 0.5f;
            _alarm = _audio.CreateLoop(_lib.AlarmLoop);
            _alarm.FadeSpeed = 2f;
            _lastSpeed = _clock != null ? _clock.Clock.Speed : 1f;

            Subscribe(true);
        }

        private void OnDestroy()
        {
            if (_lib != null)
                Subscribe(false);
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
                    _sim.Progression.FinalGradeReached += HandleVictory;
                    _sim.Session.GameOver += HandleGameOver;
                    _sim.Resources.DepletionChanged += HandleDepletion;
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
                    _sim.Progression.FinalGradeReached -= HandleVictory;
                    _sim.Session.GameOver -= HandleGameOver;
                    _sim.Resources.DepletionChanged -= HandleDepletion;
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
                }
                else
                {
                    _build.SelectionChanged -= HandleBuildSelection;
                    _build.CategoryChanged -= HandleCategory;
                    _build.Rotated -= HandleRotated;
                    _build.Placed -= HandlePlaced;
                    _build.PlaceRejected -= HandlePlaceRejected;
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
                }
                else
                {
                    _meteors.Launched -= HandleMeteorLaunched;
                    _meteors.Deflected -= HandleDeflected;
                    _meteors.LaserFired -= HandleLaser;
                    _meteors.Exploded -= HandleExploded;
                    _meteors.Impacted -= HandleImpacted;
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
            // 남은 시간 30초 이하부터 경보가 조금씩 높고 빨라짐
            _alarm.Pitch = crisis ? 1f + 0.12f * Mathf.Clamp01(1f - remaining / 30f) : 1f;
            _stormLoop.Target = !_gameOver && StormActive() ? 1f : 0f;
            _hum.Target = _gameOver ? 0.35f : 1f;

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
            var wanted = crisis ? MusicState.Crisis : day ? MusicState.Day : MusicState.Night;
            if (wanted != _wantedState)
            {
                _wantedState = wanted;
                _wantedFor = 0f;
            }
            _wantedFor += Time.unscaledDeltaTime;
            // 처음 한 번은 바로, 이후에는 잠깐 유지되어야 전환 (경계에서 왔다 갔다 방지)
            if (wanted == _musicState || (_musicState != MusicState.None && _wantedFor < _musicHold))
                return;
            _musicState = wanted;
            _audio.PlayMusic(wanted == MusicState.Crisis ? _lib.MusicCrisis
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
            foreach (var a in _sim.Events.ActiveEvents)
            {
                if (a.Data is SolarStormEventData)
                    return true;
            }
            return false;
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
            if (data != null)
                _audio.Play(_lib.BuildSelect);
            else
                _audio.Play(_lib.UiClose, 0.6f);
        }

        private void HandleCategory(ModuleCategory _) => _audio.Play(_lib.UiTab);
        private void HandleRotated() => _audio.Play(_lib.BuildRotate);

        private void HandlePlaced(ModuleInstance module)
        {
            float pan = Pan(module);
            _audio.Play(_lib.BuildPlace, pan: pan);
            _audio.Play(_lib.BuildConnect, pan: pan, delay: 0.28f); // 통로가 이어지는 소리
        }

        private void HandlePlaceRejected(PlacementResult _) => _audio.Play(_lib.UiError);

        private void HandleModuleSelection(ModuleInstance module)
        {
            if (module != null)
                _audio.Play(_lib.UiOpen, 0.7f);
        }

        private void HandleRemoved(ModuleInstance module) => _audio.Play(_lib.BuildRemove, pan: Pan(module));
        private void HandleRemoveRejected(ModuleInstance _) => _audio.Play(_lib.UiError);
        private void HandleActionFailed(string _) => _audio.Play(_lib.UiError);

        private void HandleRebuilt(ModuleInstance module)
        {
            float pan = Pan(module);
            _audio.Play(_lib.BuildRemove, 0.8f, pan);
            _audio.Play(_lib.BuildPlace, pan: pan, delay: 0.35f);
        }

        // ---------------- 수리·파손 ----------------

        private void HandleDamaged(DamageInfo info)
        {
            if (_station.IsDamageHeld(info.Module))
                return; // 운석 충돌 순간에 소리 (HandleImpacted)
            _audio.Play(_lib.ModuleDamaged, pan: Pan(info.Module));
        }

        private void HandleRepairStarted(DamageInfo info) => _audio.Play(_lib.RepairStart, pan: Pan(info.Module));
        private void HandleRepaired(ModuleInstance module) => _audio.Play(_lib.RepairDone, pan: Pan(module));
        private void HandleDestroyed(ModuleInstance module) => _audio.Play(_lib.ModuleDestroyed, pan: Pan(module));
        private void HandleMaintained(DurabilityInfo info) => _audio.Play(_lib.Maintain, pan: Pan(info.Module));

        // ---------------- 운석·방어·폭풍 ----------------

        private void HandleMeteorLaunched(Vector3 arrival, float seconds)
        {
            _audio.Play(_lib.MeteorIncoming, pan: Pan(arrival), delay: Mathf.Max(0f, seconds - _incomingPeak));
        }

        private void HandleDeflected(Vector3 at) => _audio.Play(_lib.ShieldDeflect, pan: Pan(at));
        private void HandleLaser(Vector3 muzzle, Vector3 _) => _audio.Play(_lib.TurretLaser, pan: Pan(muzzle));
        private void HandleExploded(Vector3 at) => _audio.Play(_lib.MeteorExplode, pan: Pan(at), delay: 0.03f);

        private void HandleImpacted(Vector3 at, ModuleInstance hit)
        {
            float pan = Pan(at);
            _audio.Play(_lib.MeteorImpact, pan: pan);
            if (hit != null)
                _audio.Play(_lib.ModuleDamaged, 0.8f, pan, 0.15f);
        }

        private void HandleSpark(Vector3 at) => _audio.Play(_lib.StormSpark, Random.Range(0.6f, 1f), Pan(at));

        // ---------------- 이벤트·진행 ----------------

        private void HandleEventStarted(GameEventData data)
        {
            _audio.Play(data.IsPositive ? _lib.EventPositive : _lib.EventNegative);
            if (data is OxygenLeakEventData)
                _audio.Play(_lib.OxygenLeak, delay: 0.6f);
            else if (data is SupplyShipEventData)
            {
                _audio.Play(_lib.SupplyArrive, delay: _supplyDelays.x);
                _audio.Play(_lib.SupplyArrive, 0.75f, delay: _supplyDelays.y, pitch: 0.92f);
            }
        }

        private void HandleEventEnded(ActiveEvent _) => _audio.Play(_lib.EventEnd);

        private void HandleGradeChanged(int previous, int current)
        {
            var p = _sim.Progression;
            if (current > previous)
            {
                if (p.IsFinalGrade && !p.HasReachedFinalGrade)
                    return; // 곧 FinalGradeReached → 완성 소리
                _audio.Play(_lib.GradeUp);
            }
            else
            {
                _audio.Play(_lib.GradeDown);
            }
        }

        private void HandleVictory()
        {
            _audio.Play(_lib.Victory);
            _audio.DuckMusic(0.35f);
            _restoreMusicAt = Time.unscaledTime + 5f;
        }

        private void HandleGameOver()
        {
            _gameOver = true;
            _audio.Play(_lib.GameOver);
            _audio.PlayMusic(null);
        }

        private void HandleDepletion(ResourceType type, bool depleted)
        {
            if (depleted && type != ResourceType.Metal)
                _audio.Play(_lib.ResourceDepleted);
        }

        // ---------------- 시간 조절 ----------------

        private void HandlePaused(bool paused) => _pendingClock = paused ? 1 : 2;

        private void HandleSpeed(float speed)
        {
            _pendingSpeedPitch = speed > _lastSpeed ? Mathf.Lerp(1f, 1.15f, Mathf.InverseLerp(1f, 4f, speed)) : 0.85f;
            _lastSpeed = speed;
            if (_pendingClock == 0)
                _pendingClock = 3; // 재개와 함께 오면 재개 소리만
        }
    }
}
