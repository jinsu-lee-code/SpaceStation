using SpaceStation.Audio;
using SpaceStation.Core;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 심우주 통신기지 미니게임 "신호 맞추기" (사용자 2026-11-17 미니게임 1안, 판 = <see cref="SignalTuneGame"/>, 값 = <see cref="DeepSpaceComm"/>):
    /// 가운데 관측실 휜 책상을 보고 F = 조종대 앞에 서서 화면을 내려다봄 → 가운데 화면에 목표 파형 (굵고 옅은 선)과 흔들리는 받은 파형 (가늘고 환한 선) →
    /// A · D (← · →, 바퀴) = 빈도, W · S (↑ · ↓) = 세기 다이얼로 받은 파형을 목표에 겹침 → 스페이스 · 클릭 = 맞춤 (겹친 정도 = 점수, 15초가 지나면 저절로).
    /// 다섯 신호 합계 · 최고 기록. 왼쪽 화면 = 겹침 막대 · %, 오른쪽 화면 = 신호 번호 · 합계 (색이 아니라 굵기 · 밝기 · 글, 색약).
    /// 클릭 = 다시 (끝난 뒤), F · ESC = 내려옴. 실제 시간.
    /// 세 화면은 이 조종대 전용 (<see cref="InteriorRoomDisplay"/> — 화면 가운데 점 _waveCenter · _meterCenter · _scoreCenter로 공용 상태 화면이 비켜 감):
    /// 평소엔 가운데 화면에 잔잔한 대기 파형, 왼쪽 "신호 대기", 오른쪽 최고 기록 (모듈이 꺼지면 꺼짐).
    /// </summary>
    [DefaultExecutionOrder(-320)] // InteriorMode(-300)보다 먼저: 선 채 누른 F · ESC를 여기서 먼저 씀
    public sealed class InteriorSignalTune : InteriorRoomDisplay
    {
        private const int WavePoints = 72;
        private const float FreqRate = 1.3f, AmpRate = 0.55f, WheelStep = 0.08f;
        private const float WaveWidth = 0.4f, WaveHeight = 0.105f;
        private static int _best = -1;

        [Tooltip("시작 상자 (보이지 않음 — 휜 책상 위, 보고 F)")]
        [SerializeField] private Collider _start;
        [SerializeField] private Material _lineMaterial;
        [Tooltip("화면 셋 위 짙은 판 (밝은 화면 위 흰 선 · 글이 묻혔음 — 밝기 대비)")]
        [SerializeField] private Material _screenBack;
        [SerializeField] private TMP_FontAsset _font;
        [Tooltip("전용 화면 셋 가운데 (가운데 = 파형 · 왼쪽 = 겹침 · 오른쪽 = 점수) — 공용 상태 화면이 이 자리를 비킴")]
        [SerializeField] private Vector3 _waveCenter;
        [SerializeField] private Vector3 _meterCenter;
        [SerializeField] private Vector3 _scoreCenter;

        private readonly SignalTuneGame _game = new SignalTuneGame();
        private FirstPersonController _player;
        private bool _standing;
        private int _mountFrame, _standFrame = -1;
        private Vector3 _standPoint;
        private LineRenderer _target, _received, _bar;
        private TextMeshPro _meterText, _scoreText;
        private float _phase;
        private bool _recorded;
        private int _streak;   // 연달아 또렷하게 (85점 이상) 맞춘 수 (점수 "띵" 음높이) — 85점 아래 · 시간 다 됨 · 새 판 = 0
        private float _staticSent;   // 무전 잡음 소리에 마지막으로 보낸 크기
        private int _tickLeft = -1;  // 남은 시간 째깍 — 마지막으로 본 남은 초
        private InteriorHintLine _hint;

        public SignalTuneGame Game => _game;
        public bool Standing => _standing;
        public static int Best => _best;

        public void Configure(Collider start, Material line, Material screenBack, TMP_FontAsset font)
        {
            _start = start;
            _lineMaterial = line;
            _screenBack = screenBack;
            _font = font;
            _waveCenter = DeepSpaceComm.ScreenCenter(1);
            _meterCenter = DeepSpaceComm.ScreenCenter(2);
            _scoreCenter = DeepSpaceComm.ScreenCenter(0);
        }

        protected override void Refresh()
        {
        }

        /// <summary>대기 화면 (서 있지 않을 때 · 모듈이 켜져 있을 때).</summary>
        private bool Idle => !_standing && (!Polled || Active);

        private void Awake()
        {
            if (_start != null)
            {
                var use = _start.gameObject.AddComponent<InteriorInteractable>();
                use.Tap = true;
                use.Prompt = () => "신호 맞추기" + (_best < 0 ? "" : $"  <size=80%>(최고 기록 {_best}점)</size>");
                use.Completed = Mount;
            }
            for (int k = 0; k < DeepSpaceComm.ScreenAngles.Length && _screenBack != null; k++)
                CreateQuad("SignalScreenBack" + k, DeepSpaceComm.ScreenCenter(k), DeepSpaceComm.ScreenNormal(k), DeepSpaceComm.ScreenUp(k),
                    new Vector2(DeepSpaceComm.ScreenWidth - 0.02f, DeepSpaceComm.ScreenHeight - 0.02f), _screenBack, 0.002f);
            _target = NewLine("SignalTarget", WavePoints, 0.012f, new Color(0.75f, 0.85f, 1f, 0.5f));
            _received = NewLine("SignalReceived", WavePoints, 0.0045f, Color.white);
            _bar = NewLine("SignalMeter", 2, 0.03f, Color.white);
            _meterText = ScreenText("SignalMeterText", 2);
            _scoreText = ScreenText("SignalScoreText", 0);
            _hint = new InteriorHintLine(transform, "SignalTuneHint", _font);
        }

        private void OnDisable()
        {
            Release();
            _game.Stop();
            ShowScreens(false);
            if (_staticSent > 0f)
                AudioService.SetRideLoop(l => l.DscStaticLoop, 0f);
            _staticSent = 0f;
            _hint?.Show(null);
        }

        private void OnDestroy() => _hint?.Destroy();

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (_player == null)
                _player = FindFirstObjectByType<FirstPersonController>();
            if (_standing && _player != null && !_player.Riding)
                ClearStand();
            if (_standing)
                Controls(dt);
            var before = _game.Current;
            _game.Step(dt);
            if (before == SignalTuneGame.Phase.Tune && _game.Current == SignalTuneGame.Phase.Locked)
            {
                _streak = 0;
                AudioService.TryPlay(l => l.GameMiss);   // 시간이 다 되어 저절로 맞춤
            }
            if (_game.Current == SignalTuneGame.Phase.Done && !_recorded)
                Record();
            UpdateRadioSound();
            _phase += dt * 0.35f;
            DrawScreens();
            UpdateHint();
        }

        // ---------------- 서기 ----------------

        private void Mount()
        {
            if (_standing || _player == null || Time.frameCount == _standFrame)
                return;
            _standPoint = _player.transform.position;
            _player.RideAt(transform, transform.TransformPoint(DeepSpaceComm.ConsoleStand), FirstPersonController.EyeHeight, DeepSpaceComm.ConsoleYaw + transform.eulerAngles.y, DeepSpaceComm.ConsolePitch);
            _standing = true;
            _mountFrame = Time.frameCount;
            if (_start != null)
                _start.enabled = false;
            Begin();
            AudioService.TryPlay(l => l.GameStart);
        }

        private void Begin()
        {
            _game.Begin();
            _recorded = false;
            _streak = 0;
            ShowScreens(true);
        }

        private void Controls(float dt)
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !InputGate.EscapeConsumedThisFrame)
            {
                InputGate.ConsumeEscape();
                StepDown();
                return;
            }
            if (Time.frameCount != _mountFrame && KeyBindings.WasPressed(GameAction.Interact))
            {
                StepDown();
                return;
            }
            bool click = mouse != null && mouse.leftButton.wasPressedThisFrame;
            if (_game.Current == SignalTuneGame.Phase.Done)
            {
                if (click)
                {
                    Begin();
                    AudioService.TryPlay(l => l.GameStart);
                }
                return;
            }
            if (keyboard == null)
                return;
            float df = 0f, da = 0f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
                df -= FreqRate * dt;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
                df += FreqRate * dt;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
                da -= AmpRate * dt;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
                da += AmpRate * dt;
            if (mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                    df += Mathf.Sign(scroll) * WheelStep;
            }
            _game.Adjust(df, da);
            if ((click || keyboard.spaceKey.wasPressedThisFrame) && _game.Lock())
            {
                if (_game.LastScore >= 85)
                    AudioService.TryPlayNote(l => l.DscTuneLock, 1 + Mathf.Min(++_streak, 4));   // 또렷함: 파 → 솔 → 라 → 위 도 (연달아 맞출수록 위)
                else
                {
                    _streak = 0;
                    AudioService.TryPlayNote(l => l.DscTuneLock, 0, 0.55f);   // 덜 맞음: 작게 도
                }
            }
        }

        /// <summary>
        /// 무전 잡음 (조작대에 선 동안 2D 루프): 맞추는 중 크기 = 1 − 겹친 정도 (맞을수록 걷힘), 맞춘 뒤 · 끝 · 내림 = 0.
        /// 남은 시간 째깍: 신호마다 5초부터 1초마다.
        /// </summary>
        private void UpdateRadioSound()
        {
            bool tuning = _standing && (_game.Current == SignalTuneGame.Phase.Ready || _game.Current == SignalTuneGame.Phase.Tune);
            float level = tuning ? 1f - _game.Match : 0f;
            if (level > 0f || _staticSent > 0f)
            {
                _staticSent = level;
                AudioService.SetRideLoop(l => l.DscStaticLoop, level);
            }
            int left = _game.Current == SignalTuneGame.Phase.Tune ? Mathf.CeilToInt(_game.Remaining) : -1;
            if (left != _tickLeft && left >= 1 && left <= 5)
                AudioService.TryPlay(l => l.GameTick);
            _tickLeft = left;
        }

        private void StepDown()
        {
            _standFrame = Time.frameCount;
            Release();
            _game.Stop();
            ShowScreens(false);
            AudioService.TryPlay(l => l.RideLeave);
        }

        private void Release()
        {
            if (!_standing)
                return;
            ClearStand();
            if (_player != null)
            {
                _player.RideAt(transform, _standPoint, FirstPersonController.EyeHeight, null, 0f);
                _player.Dismount();
            }
        }

        private void ClearStand()
        {
            _standing = false;
            if (_start != null)
                _start.enabled = true;
            _game.Stop();
            ShowScreens(false);
        }

        private void Record()
        {
            _recorded = true;
            bool record = _game.Total > 0 && _game.Total > _best;
            if (record)
                _best = _game.Total;
            AudioService.TryPlay(l => record ? l.GameRecord : _game.Total > 0 ? l.GameClear : l.GameFail);
        }

        // ---------------- 화면 ----------------

        private void ShowScreens(bool on)
        {
            if (_target != null)
                _target.enabled = on;
            if (_received != null)
                _received.enabled = on;
            if (_bar != null)
                _bar.enabled = on;
            if (_meterText != null)
                _meterText.gameObject.SetActive(on);
            if (_scoreText != null)
                _scoreText.gameObject.SetActive(on);
        }

        private void DrawScreens()
        {
            if (_target == null)
                return;
            ShowScreens(_standing || Idle);
            if (!_standing)
            {
                if (Idle)
                    DrawIdle();
                return;
            }
            _target.enabled = true;
            const int k = 1;
            var c = DeepSpaceComm.ScreenCenter(k);
            var n = DeepSpaceComm.ScreenNormal(k);
            var right = DeepSpaceComm.ScreenRight(k);
            var up = DeepSpaceComm.ScreenUp(k);
            bool tuning = _game.Current == SignalTuneGame.Phase.Tune;
            float jitter = _game.Current == SignalTuneGame.Phase.Locked ? 0.02f : _game.Jitter;
            float t = Time.unscaledTime;
            for (int i = 0; i < WavePoints; i++)
            {
                float x = i / (float)(WavePoints - 1);
                var basePos = c + right * ((x - 0.5f) * WaveWidth);
                float yt = SignalTuneGame.Wave(x, _game.TargetFreq, _game.TargetAmp, _phase);
                float noise = (Mathf.PerlinNoise(x * 9f, t * 3.1f) - 0.5f) * 2f + 0.35f * Mathf.Sin(x * 70f + t * 23f);
                float yr = SignalTuneGame.Wave(x, _game.Freq, _game.Amp, _phase) + jitter * noise;
                _target.SetPosition(i, transform.TransformPoint(basePos + up * (yt * WaveHeight) + n * 0.004f));
                _received.SetPosition(i, transform.TransformPoint(basePos + up * (Mathf.Clamp(yr, -1.1f, 1.1f) * WaveHeight) + n * 0.008f));
            }
            // 왼쪽 화면 (153°): 겹침 막대 (길이) · %
            int m = 2;
            var mc = DeepSpaceComm.ScreenCenter(m) + DeepSpaceComm.ScreenNormal(m) * 0.006f - DeepSpaceComm.ScreenUp(m) * 0.06f;
            var mr = DeepSpaceComm.ScreenRight(m);
            float match = _game.Current == SignalTuneGame.Phase.Locked || _game.Current == SignalTuneGame.Phase.Done ? _game.LastScore / 100f : tuning ? _game.Match : 0f;
            _bar.SetPosition(0, transform.TransformPoint(mc - mr * 0.17f));
            _bar.SetPosition(1, transform.TransformPoint(mc - mr * 0.17f + mr * (0.34f * Mathf.Max(0.02f, match))));
            _meterText.SetText(_game.Current == SignalTuneGame.Phase.Locked ? $"맞춤 {_game.LastScore}점" : $"겹침 {Mathf.RoundToInt(match * 100f)}%");
            _scoreText.SetText(_game.Current == SignalTuneGame.Phase.Done ? $"합계 {_game.Total}점\n최고 {Mathf.Max(_best, 0)}점"
                : $"신호 {Mathf.Min(_game.Round + 1, SignalTuneGame.Rounds)} / {SignalTuneGame.Rounds}\n합계 {_game.Total}점");
        }

        /// <summary>대기 화면: 가운데 = 잔잔히 흐르는 받은 파형 (목표 선 없음), 왼쪽 = 신호 대기, 오른쪽 = 최고 기록 · 하는 법.</summary>
        private void DrawIdle()
        {
            const int k = 1;
            var c = DeepSpaceComm.ScreenCenter(k);
            var n = DeepSpaceComm.ScreenNormal(k);
            var right = DeepSpaceComm.ScreenRight(k);
            var up = DeepSpaceComm.ScreenUp(k);
            float t = Time.unscaledTime;
            _target.enabled = false;
            for (int i = 0; i < WavePoints; i++)
            {
                float x = i / (float)(WavePoints - 1);
                float y = SignalTuneGame.Wave(x, 2.2f, 0.35f, _phase) + 0.08f * (Mathf.PerlinNoise(x * 7f, t * 0.8f) - 0.5f);
                _received.SetPosition(i, transform.TransformPoint(c + right * ((x - 0.5f) * WaveWidth) + up * (y * WaveHeight) + n * 0.008f));
            }
            int m = 2;
            var mc = DeepSpaceComm.ScreenCenter(m) + DeepSpaceComm.ScreenNormal(m) * 0.006f - DeepSpaceComm.ScreenUp(m) * 0.06f;
            var mr = DeepSpaceComm.ScreenRight(m);
            float pulse = 0.25f + 0.2f * Mathf.Sin(t * 1.3f);
            _bar.SetPosition(0, transform.TransformPoint(mc - mr * 0.17f));
            _bar.SetPosition(1, transform.TransformPoint(mc - mr * 0.17f + mr * (0.34f * pulse)));
            _meterText.SetText("신호 대기 중");
            _scoreText.SetText(_best < 0 ? "신호 맞추기\n책상을 보고 F" : $"신호 맞추기\n최고 {_best}점");
        }

        private LineRenderer NewLine(string name, int points, float width, Color color)
        {
            var line = new GameObject(name).AddComponent<LineRenderer>();
            line.transform.SetParent(transform, false);
            line.useWorldSpace = true;
            line.positionCount = points;
            line.sharedMaterial = _lineMaterial;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.widthMultiplier = width;
            line.startColor = line.endColor = color;
            line.numCapVertices = 2;
            return line;
        }

        /// <summary>화면 k 위 글 (화면 면 앞 6 mm, 서서 읽는 쪽).</summary>
        private TextMeshPro ScreenText(string name, int k)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var n = DeepSpaceComm.ScreenNormal(k);
            go.transform.localPosition = DeepSpaceComm.ScreenCenter(k) + n * 0.006f + (k == 2 ? DeepSpaceComm.ScreenUp(k) * 0.045f : Vector3.zero);
            go.transform.localRotation = Quaternion.LookRotation(-n, DeepSpaceComm.ScreenUp(k));
            var text = go.AddComponent<TextMeshPro>();
            if (_font != null)
                text.font = _font;
            text.rectTransform.sizeDelta = new Vector2(DeepSpaceComm.ScreenWidth - 0.06f, k == 2 ? 0.1f : DeepSpaceComm.ScreenHeight - 0.05f);
            text.enableAutoSizing = true;
            text.fontSizeMin = 0.05f;
            text.fontSizeMax = 0.6f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(0.9f, 0.97f, 1f);
            return text;
        }

        // ---------------- 안내 ----------------

        private void UpdateHint()
        {
            if (!_standing)
            {
                _hint.Show(null);
                return;
            }
            string f = KeyBindings.Label(GameAction.Interact);
            const string keys = "<size=80%>A · D = 빈도 · W · S = 세기 · 스페이스 (클릭) = 맞춤</size>";
            switch (_game.Current)
            {
                case SignalTuneGame.Phase.Ready:
                    _hint.Show($"신호 {_game.Round + 1} / {SignalTuneGame.Rounds} 들어오는 중…  굵은 선 = 목표 파형\n{keys}");
                    break;
                case SignalTuneGame.Phase.Tune:
                    _hint.Show($"가는 선 (받은 파형)을 굵은 선에 겹치세요  ·  남은 시간 {Mathf.CeilToInt(_game.Remaining)}초  ·  겹침 {_game.ScoreNow}%\n{keys}");
                    break;
                case SignalTuneGame.Phase.Locked:
                    _hint.Show($"신호 {_game.Round + 1}: {_game.LastScore}점" + (_game.LastScore >= 85 ? "  · 또렷하게 받았어요!" : ""));
                    break;
                case SignalTuneGame.Phase.Done:
                    bool record = _game.Total > 0 && _game.Total >= _best;
                    _hint.Show($"다섯 신호 합계 {_game.Total}점  ·  " + (record ? "최고 기록!" : $"최고 {Mathf.Max(_best, 0)}점") + $"\n<size=80%>클릭 = 다시 · {f} · ESC = 내려오기</size>");
                    break;
                default:
                    _hint.Show(null);
                    break;
            }
        }
    }
}
