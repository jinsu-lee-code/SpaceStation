using SpaceStation.Audio;
using SpaceStation.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 2단계 화물 환적장 "은하수 후룸 항구" 보트 탑승 (MODELING 7-10 직접 체험 "후룸 탑승", 사용자 2026-11-09 "유니티로 옮겨줘"):
    /// 보트 셋이 물길 한 바퀴 (<see cref="CargoFlumeCourse"/>)를 돌고, 동쪽 부두 정차장에 한 척씩 섬 — 문짝 · 안전 손잡이가 열리고 크레인 B가 고물 컨테이너를 싣거나 내림
    /// (<see cref="InteriorStorageCrane.RequestDock"/>), 8초 (플레이어가 앉아 있으면 3초) 뒤 닫고 출발. 앞 보트와 3.1 m 간격 (항구 대기 줄).
    /// 플레이어 = 정차한 보트의 앞줄 두 자리 (보고 F = 앉기, 정차 · 열림에서 다시 F = 내림, 달리는 중이면 한 바퀴 더). 앉은 시선은 보트 앞뒤 기울기를 따라감
    /// (<see cref="FirstPersonController.RideAt"/> followPitch, 사용자 2026-11-09 — 걸음 흔들림을 끄면 수평). 뒷줄 = 탄 주민 (자리에 생긴 주민을 보트로 옮겨 태움).
    /// 아무 보트나 물보라 자리를 지나면 물보라 (<see cref="InteriorFlumeSplash"/>). 모듈이 멈추면 돌던 보트는 정차장까지 와서 문을 연 채 멈춤.
    /// 위치는 모두 템플릿 루트(이 물체) 기준, 시간은 실제 시간. 템플릿 빌더가 configure에서 붙임.
    /// </summary>
    [DefaultExecutionOrder(-320)] // InteriorMode(-300)보다 먼저: 앉은 채 누른 F를 여기서 먼저 씀
    public sealed class InteriorFlumeRide : InteriorRoomDisplay
    {
        private enum Phase { Running, Opening, Open, Closing, BarsDown }

        private const float Gap = 3.1f;            // 앞 보트와 간격 (보트 2.3 + 0.8)
        private const float Accel = 0.6f, Brake = 0.8f;
        private const float GateTravel = 1.62f, GateSeconds = 0.9f, BarSeconds = 0.5f;
        private const float Dwell = 8f, DwellSeated = 3f, CraneWaitMax = 4f;
        private const float SeatedEye = 1.10f;
        private const float RiderInterval = 0.5f, SignInterval = 0.5f;
        private static readonly Rect GateZone = new Rect(10.80f, 6.90f, 1.65f, 0.90f);   // 문짝 앞 (x, z, 폭, 깊이)

        [SerializeField] private Transform[] _boats;        // 보트 피벗 (원점 = 흘수선 가운데, +z = 앞)
        [SerializeField] private Transform[] _seats;        // 보트마다 앞줄 왼쪽 · 오른쪽 자리 (BoxCollider, 보트 피벗 자식)
        [SerializeField] private Transform[] _barsFront;    // 앞줄 손잡이 경첩 (보트 피벗 자식, 모델 = 올림 70°)
        [SerializeField] private Transform[] _barsRear;
        [SerializeField] private Transform[] _cargoAnchors; // 고물 컨테이너 자리 (바닥 가운데, 보트 피벗 자식)
        [SerializeField] private Transform[] _cargo;        // 보트마다 지금 실린 컨테이너 (없으면 null)
        [SerializeField] private float[] _startS;
        [SerializeField] private Transform _gate;
        [SerializeField] private Transform[] _lugs;          // 체인 고리
        [SerializeField] private InteriorStorageCrane _crane;
        [SerializeField] private InteriorFlumeSplash _splash;
        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private Vector3 _sign = new Vector3(11.6f, 0.7f, 7.70f);
        [SerializeField] private Vector3[] _riderSeats = new Vector3[0];
        [SerializeField] private int[] _riderBoats = new int[0];

        private float[] _s, _v, _bar;   // _bar = 손잡이 내림 (0 = 올림, 1 = 내림)
        private bool[] _armed;
        private Vector3[] _restPos;
        private Quaternion[] _restRot;
        private Collider[] _seatColliders;
        private int _docked = -1;
        private Phase _phase = Phase.Open;
        private float _timer, _wait, _gateOpen = 1f, _lugPhase;
        private Vector3 _gateBase;
        private bool _craneAsked;
        private int _ride = -1, _rideSeat;
        private int _sitFrame = -1;
        private float _bobTimer, _noteTimer;
        private Transform _anchor;
        private FirstPersonController _player;
        private TextMeshPro _signText;
        private float _signTimer, _riderTimer;
        private Canvas _canvas;
        private TextMeshProUGUI _hint;
        private string _hintShown;

        public bool Riding => _ride >= 0;
        public int DockedBoat => _docked;
        public float BoatS(int b) => _s != null ? _s[b] : 0f;

        public void Configure(Transform[] boats, Transform[] seats, Transform[] barsFront, Transform[] barsRear, Transform[] cargoAnchors, Transform[] cargo, float[] startS,
            Transform gate, Transform[] lugs, InteriorStorageCrane crane, InteriorFlumeSplash splash, TMP_FontAsset font, Vector3 sign, Vector3[] riderSeats, int[] riderBoats)
        {
            _boats = boats;
            _seats = seats;
            _barsFront = barsFront;
            _barsRear = barsRear;
            _cargoAnchors = cargoAnchors;
            _cargo = cargo;
            _startS = startS;
            _gate = gate;
            _lugs = lugs;
            _crane = crane;
            _splash = splash;
            _font = font;
            _sign = sign;
            _riderSeats = riderSeats;
            _riderBoats = riderBoats;
        }

        protected override void Refresh() { }

        private bool Stopped => Polled && !Active;

        private void Awake()
        {
            if (_boats == null || _boats.Length == 0)
                return;
            int n = _boats.Length;
            _s = new float[n];
            _v = new float[n];
            _bar = new float[n];
            _armed = new bool[n];
            _restPos = new Vector3[n];
            _restRot = new Quaternion[n];
            _seatColliders = new Collider[_seats.Length];
            for (int b = 0; b < n; b++)
            {
                _s[b] = CargoFlumeCourse.Wrap(_startS[b]);
                _armed[b] = _s[b] > CargoFlumeCourse.Loop * 0.5f;
                CargoFlumeCourse.Pose(_s[b], out _restPos[b], out _restRot[b]);
                if (_s[b] < 0.01f)
                    _docked = b;
            }
            for (int k = 0; k < _seats.Length; k++)
            {
                _seatColliders[k] = _seats[k].GetComponent<Collider>();
                int seat = k;
                var use = _seats[k].gameObject.AddComponent<InteriorInteractable>();
                use.Tap = true;
                use.Prompt = () => "보트 타기";
                use.Blocked = () => SeatBlocked(seat);
                use.Completed = () => Sit(seat);
            }
            if (_gate != null)
                _gateBase = _gate.localPosition;
            _anchor = new GameObject("RideAnchor").transform;
            _anchor.SetParent(transform, false);
            BuildSign();
            _timer = Dwell;
            _phase = _docked >= 0 ? Phase.Open : Phase.Running;
            Apply();
            UpdateColliders();
        }

        private void OnDisable()
        {
            ReleasePlayer();
            ShowHint(null);
        }

        private void OnDestroy()
        {
            if (_canvas != null)
                Destroy(_canvas.gameObject);
        }

        private void Update()
        {
            if (_s == null)
                return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (_player == null)
                _player = FindFirstObjectByType<FirstPersonController>();
            if (_ride >= 0 && (_player == null || !_player.Riding))
                ClearRide();   // 패드 순간이동 등으로 탄 상태가 끊김
            if (_ride >= 0 && Time.frameCount != _sitFrame && KeyBindings.WasPressed(GameAction.Interact))
            {
                if (_ride == _docked && _phase == Phase.Open)
                    StandUp();
                else
                    _noteTimer = 2f;
            }
            Station(dt);
            for (int b = 0; b < _boats.Length; b++)
            {
                if (b != _docked)
                    Move(b, dt);
            }
            _lugPhase = Mathf.Repeat(_lugPhase + dt * CargoFlumeCourse.LiftChainSpeed, 1000f);
            Apply();
            UpdateAnchor(dt);
            CaptureRiders(dt);
            UpdateSign(dt);
            UpdateHint(dt);
        }

        // ---------------- 보트 ----------------

        private void Move(int b, float dt)
        {
            float loop = CargoFlumeCourse.Loop;
            float s = _s[b];
            float table = CargoFlumeCourse.Speed(s);
            float v = CargoFlumeCourse.OnSlope(s) ? table : Mathf.Min(table, _v[b] + Accel * dt);
            // 멈출 자리: 정차장 (한 바퀴 돌아온 보트) · 앞 보트 뒤 3.1
            float stop = float.MaxValue;
            if (_armed[b])
                stop = loop - s;
            int lead = (b + _boats.Length - 1) % _boats.Length;
            float gap = CargoFlumeCourse.Wrap(_s[lead] - s);
            if (gap < 1e-4f)
                gap = loop;
            stop = Mathf.Min(stop, gap - Gap);
            if (stop < float.MaxValue)
                v = Mathf.Min(v, Mathf.Sqrt(2f * Brake * Mathf.Max(0f, stop)));
            if (stop <= 0.002f)
                v = 0f;
            _v[b] = v;
            float before = s;
            s += v * CargoFlumeCourse.PlanFactor(s) * dt;
            if (_armed[b] && s >= loop - 0.003f)
            {
                if (_docked < 0 && v < 0.35f || s >= loop)
                {
                    s = 0f;
                    _v[b] = 0f;
                    _armed[b] = false;
                    if (_docked < 0)
                        Arrive(b);
                }
            }
            else if (!_armed[b] && s > loop * 0.5f)
                _armed[b] = true;
            s = CargoFlumeCourse.Wrap(s);
            float splash = CargoFlumeCourse.SplashS;
            if (before < splash && s >= splash)
                Splash(b);
            _s[b] = s;
        }

        private void Arrive(int b)
        {
            _docked = b;
            _phase = Phase.Opening;
            _craneAsked = false;
            PlayNear(l => l.SupplyArrive);
            PlayNear(l => l.UiOpen);
            UpdateColliders();
        }

        private void Station(float dt)
        {
            if (_docked < 0)
            {
                _gateOpen = Mathf.MoveTowards(_gateOpen, 0f, dt / GateSeconds);
                return;
            }
            int b = _docked;
            switch (_phase)
            {
                case Phase.Opening:
                    _gateOpen = Mathf.MoveTowards(_gateOpen, 1f, dt / GateSeconds);
                    _bar[b] = Mathf.MoveTowards(_bar[b], 0f, dt / BarSeconds);
                    if (!_craneAsked)
                        AskCrane(b);
                    if (_gateOpen >= 1f && _bar[b] <= 0f)
                    {
                        _phase = Phase.Open;
                        _timer = Dwell;
                        _wait = 0f;
                        UpdateColliders();
                    }
                    break;
                case Phase.Open:
                    if (Stopped)
                        break;   // 모듈이 멈춤: 문을 연 채 기다림
                    _timer -= dt;
                    if (_ride == b)
                        _timer = Mathf.Min(_timer, DwellSeated);
                    if (_timer > 0f)
                        break;
                    if (CraneHolds())
                    {
                        _wait += dt;
                        break;
                    }
                    if (InGateZone())
                    {
                        _timer = 0.5f;   // 문 앞에 있으면 기다림
                        break;
                    }
                    FinishCrane(b);
                    _phase = Phase.Closing;
                    PlayNear(l => l.UiClose);
                    UpdateColliders();
                    break;
                case Phase.Closing:
                    if (InGateZone())
                    {
                        _phase = Phase.Opening;   // 사이에 들어오면 다시 엶
                        UpdateColliders();
                        break;
                    }
                    _gateOpen = Mathf.MoveTowards(_gateOpen, 0f, dt / GateSeconds);
                    if (_gateOpen <= 0f)
                        _phase = Phase.BarsDown;
                    break;
                case Phase.BarsDown:
                    _bar[b] = Mathf.MoveTowards(_bar[b], 1f, dt / BarSeconds);
                    if (_bar[b] >= 1f)
                    {
                        _docked = -1;
                        _phase = Phase.Running;
                        _v[b] = 0f;
                        UpdateColliders();
                    }
                    break;
            }
        }

        private void AskCrane(int b)
        {
            _craneAsked = true;
            if (_crane != null && _cargoAnchors != null && b < _cargoAnchors.Length && _cargoAnchors[b] != null)
                _crane.RequestDock(_cargoAnchors[b], _cargo[b]);
        }

        /// <summary>크레인이 정차 일 중이라 보트를 붙잡음: 고물 위에서 내리는 중이면 끝까지, 아니면 최대 4초 (멈춘 모듈이면 바로 건너뜀).</summary>
        private bool CraneHolds()
        {
            if (_crane == null || !_crane.DockBusy)
                return false;
            if (_crane.DockCommitted)
                return true;
            if (!_crane.Running || _wait > CraneWaitMax)
            {
                _crane.CancelDock();
                return _crane.DockBusy;
            }
            return true;
        }

        private void FinishCrane(int b)
        {
            if (_crane != null && !_crane.DockBusy && _craneAsked)
                _cargo[b] = _crane.DockResult;
        }

        private void Splash(int b)
        {
            if (_splash == null)
                return;
            float s = CargoFlumeCourse.SplashS;
            var at = CargoFlumeCourse.Point(s);
            _splash.Splash(at, CargoFlumeCourse.Point(s + 0.5f) - CargoFlumeCourse.Point(s - 0.5f));
            if (_ride == b && _player != null)
            {
                var eye = _player.Eye;
                _splash.Mist(eye.position + eye.forward * 0.6f);
                _bobTimer = 0.4f;
            }
        }

        private void Apply()
        {
            for (int b = 0; b < _boats.Length; b++)
            {
                CargoFlumeCourse.Pose(_s[b], out var p, out var r);
                _boats[b].localPosition = p;
                _boats[b].localRotation = r;
                float a = -70f * Mathf.SmoothStep(0f, 1f, _bar[b]);
                if (_barsFront != null && _barsFront[b] != null)
                    _barsFront[b].localRotation = Quaternion.AngleAxis(a, Vector3.right);
                if (_barsRear != null && _barsRear[b] != null)
                    _barsRear[b].localRotation = Quaternion.AngleAxis(a, Vector3.right);
            }
            if (_gate != null)
                _gate.localPosition = _gateBase + Vector3.left * (Mathf.SmoothStep(0f, 1f, _gateOpen) * GateTravel);
            if (_lugs != null && _lugs.Length > 0)
            {
                float s0 = CargoFlumeCourse.LiftStart + 0.2f, len = CargoFlumeCourse.LiftLength - 0.4f;
                bool run = !Stopped;
                for (int k = 0; k < _lugs.Length; k++)
                {
                    float s = s0 + Mathf.Repeat((run ? _lugPhase : 0f) + len * k / _lugs.Length, len);
                    CargoFlumeCourse.Pose(s, out var p, out var r);
                    p.y = CargoFlumeCourse.Water(s);
                    _lugs[k].localPosition = p;
                    _lugs[k].localRotation = r;
                }
            }
        }

        private void UpdateColliders()
        {
            bool open = _docked >= 0 && (_phase == Phase.Open || _phase == Phase.Opening);
            for (int k = 0; k < _seats.Length; k++)
            {
                if (_seatColliders[k] != null)
                    _seatColliders[k].enabled = _ride < 0 && open && k / 2 == _docked;
            }
        }

        // ---------------- 플레이어 ----------------

        private static readonly Vector3[] Feet = { new Vector3(-0.25f, 0.10f, 0.33f), new Vector3(0.25f, 0.10f, 0.33f) };

        private string SeatBlocked(int seat)
        {
            int b = seat / 2;
            if (b != _docked || _phase != Phase.Open)
                return "곧 출발";
            return null;
        }

        private void Sit(int seat)
        {
            int b = seat / 2;
            if (_ride >= 0 || _player == null || b != _docked || _phase != Phase.Open)
                return;
            _ride = b;
            _rideSeat = seat % 2;
            _sitFrame = Time.frameCount;
            PlaceAnchor(0f);
            _player.RideAt(_anchor, _anchor.position, SeatedEye, _boats[b].eulerAngles.y, 0f, followPitch: true);
            UpdateColliders();
            AudioService.TryPlay(l => l.UiOpen);
        }

        private void StandUp()
        {
            int b = _ride;
            var seatWorld = _boats[b].TransformPoint(Feet[_rideSeat]);
            var exit = transform.InverseTransformPoint(seatWorld);
            exit = new Vector3(exit.x, -1.3f, 7.20f);
            float yaw = (transform.rotation * Quaternion.Euler(0f, 180f, 0f)).eulerAngles.y;
            _player.RideAt(transform, transform.TransformPoint(exit), FirstPersonController.EyeHeight, yaw);
            _player.Dismount();
            ClearRide();
            AudioService.TryPlay(l => l.UiClose);
        }

        private void ReleasePlayer()
        {
            if (_ride < 0 || _player == null)
            {
                _ride = -1;
                return;
            }
            float yaw = (transform.rotation * Quaternion.Euler(0f, 180f, 0f)).eulerAngles.y;
            _player.Teleport(transform.TransformPoint(new Vector3(12.0f, -1.3f, 7.20f)), yaw);
            ClearRide();
        }

        private void ClearRide()
        {
            _ride = -1;
            _bobTimer = 0f;
            UpdateColliders();
        }

        /// <summary>앉은 자리 고정점: 보트와 함께 기우는 발 자리를 똑바로 선 몸으로 옮김 (리프트 · 낙하에서 몸이 뒤로 밀리지 않게), 앞 방향 = 보트 앞 (기울기 포함, 옆 기울기 없음).</summary>
        private void PlaceAnchor(float bob)
        {
            var boat = _boats[_ride];
            _anchor.position = boat.TransformPoint(Feet[_rideSeat]) + (boat.up - Vector3.up) * SeatedEye + Vector3.up * bob;
            _anchor.rotation = Quaternion.LookRotation(boat.forward, Vector3.up);
        }

        private void UpdateAnchor(float dt)
        {
            if (_ride < 0)
                return;
            float bob = 0f;
            if (_bobTimer > 0f)
            {
                _bobTimer -= dt;
                if (Settings.GameSettings.HeadBob)
                    bob = Mathf.Sin(_bobTimer * 40f) * 0.04f * (_bobTimer / 0.4f);
            }
            PlaceAnchor(bob);
        }

        private bool InGateZone()
        {
            if (_player == null || _ride >= 0)
                return false;
            var p = transform.InverseTransformPoint(_player.transform.position);
            return GateZone.Contains(new Vector2(p.x, p.z)) && p.y > -1.8f && p.y < 0.8f;
        }

        private void PlayNear(System.Func<SoundLibrary, SoundCue> cue)
        {
            if (_player == null)
                return;
            var p = transform.InverseTransformPoint(_player.transform.position) - new Vector3(CargoFlumeCourse.StationX, -1.3f, CargoFlumeCourse.LaneZ);
            if (_ride >= 0 || p.sqrMagnitude < 144f)
                AudioService.TryPlay(cue);
        }

        // ---------------- 탄 주민 ----------------

        /// <summary>뒷줄 자리에 생긴 주민을 그 보트로 옮겨 태움 (주민은 방을 지을 때 처음 자세 자리에 생김).</summary>
        private void CaptureRiders(float dt)
        {
            if (_riderSeats == null || _riderSeats.Length == 0)
                return;
            _riderTimer -= dt;
            if (_riderTimer > 0f)
                return;
            _riderTimer = RiderInterval;
            var room = transform.parent != null ? transform.parent : transform;
            foreach (var figure in room.GetComponentsInChildren<ResidentFigure>())
            {
                var parent = figure.transform.parent;
                if (parent != null && System.Array.IndexOf(_boats, parent) >= 0)
                    continue;   // 이미 보트에 탐
                var p = transform.InverseTransformPoint(figure.transform.position);
                for (int i = 0; i < _riderSeats.Length; i++)
                {
                    var seat = _riderSeats[i];
                    if (new Vector2(p.x - seat.x, p.z - seat.z).sqrMagnitude > 0.04f || Mathf.Abs(p.y - seat.y) > 0.6f)
                        continue;
                    int b = _riderBoats[i];
                    var wander = figure.GetComponent<ResidentWander>();
                    if (wander != null)
                        Destroy(wander);
                    var inv = Quaternion.Inverse(_restRot[b]);
                    var rot = figure.transform.localRotation;
                    figure.transform.SetParent(_boats[b], true);
                    figure.transform.localPosition = inv * (p - _restPos[b]);
                    figure.transform.localRotation = inv * rot;
                    break;
                }
            }
        }

        // ---------------- 안내판 · 안내 줄 (색 대신 글 · 깜빡임) ----------------

        private void BuildSign()
        {
            var font = _font != null ? _font : Font;
            if (font == null)
                return;
            var go = new GameObject("FlumeSign");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = _sign;
            _signText = go.AddComponent<TextMeshPro>();
            _signText.font = font;
            _signText.fontSize = 1.1f;
            _signText.color = new Color(0.92f, 0.96f, 1f);
            _signText.alignment = TextAlignmentOptions.Center;
            _signText.textWrappingMode = TextWrappingModes.NoWrap;
            _signText.rectTransform.sizeDelta = new Vector2(1.2f, 0.26f);
        }

        /// <summary>다음 보트가 정차장에 서기까지 (초, 대략).</summary>
        public float SecondsUntilBoat()
        {
            if (_docked >= 0)
                return 0f;
            float best = float.MaxValue;
            for (int b = 0; b < _boats.Length; b++)
            {
                if (!_armed[b])
                    continue;
                float d = CargoFlumeCourse.Loop - _s[b];
                float t = d > CargoFlumeCourse.Loop - CargoFlumeCourse.SegmentStart(14) ? (d - 12f) / 1.75f + 12f / 1.2f : d / 1.2f;
                best = Mathf.Min(best, t);
            }
            return best < float.MaxValue ? best : 0f;
        }

        private void UpdateSign(float dt)
        {
            if (_signText == null)
                return;
            _signTimer -= dt;
            if (_signTimer > 0f)
                return;
            _signTimer = SignInterval;
            bool blink = Mathf.Repeat(Time.unscaledTime, 1f) < 0.5f;
            string status;
            if (_docked >= 0 && _phase == Phase.Open)
                status = Stopped ? "운행 멈춤 · 보트 대기" : "보트 도착 · 타세요 (F)";
            else if (_docked >= 0)
                status = blink ? "곧 출발" : "";
            else
                status = $"다음 보트 약 {Mathf.CeilToInt(SecondsUntilBoat())}초";
            _signText.SetText($"은하수 후룸\n<size=62%>{status}</size>");
        }

        private void UpdateHint(float dt)
        {
            string text = null;
            if (_ride >= 0)
            {
                if (_noteTimer > 0f)
                    _noteTimer -= dt;
                string key = KeyBindings.Label(GameAction.Interact);
                if (_ride == _docked && _phase == Phase.Open)
                    text = $"정차장   ·   {key}  내리기";
                else if (_noteTimer > 0f)
                    text = $"정차하면 {key}로 내림";
                else
                    text = CargoFlumeCourse.SectionName(_s[_ride]);
            }
            ShowHint(text);
        }

        private void ShowHint(string text)
        {
            if (text == _hintShown)
                return;
            _hintShown = text;
            if (text == null)
            {
                if (_canvas != null)
                    _canvas.gameObject.SetActive(false);
                return;
            }
            EnsureCanvas();
            _canvas.gameObject.SetActive(true);
            _hint.SetText(text);
        }

        private void EnsureCanvas()
        {
            if (_canvas != null)
                return;
            var go = new GameObject("FlumeHint", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 300;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            var back = new GameObject("Back", typeof(RectTransform)).AddComponent<Image>();
            back.transform.SetParent(go.transform, false);
            Place(back.rectTransform, new Vector2(620f, 52f));
            back.color = new Color(0.03f, 0.05f, 0.1f, 0.62f);
            back.raycastTarget = false;
            _hint = new GameObject("Hint", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            _hint.transform.SetParent(go.transform, false);
            Place(_hint.rectTransform, new Vector2(600f, 44f));
            var font = _font != null ? _font : Font;
            if (font != null)
                _hint.font = font;
            _hint.fontSize = 26f;
            _hint.color = new Color(0.92f, 0.96f, 1f);
            _hint.alignment = TextAlignmentOptions.Center;
            _hint.raycastTarget = false;
            _hint.textWrappingMode = TextWrappingModes.NoWrap;
        }

        private static void Place(RectTransform rt, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, 64f);
            rt.sizeDelta = size;
        }
    }
}
