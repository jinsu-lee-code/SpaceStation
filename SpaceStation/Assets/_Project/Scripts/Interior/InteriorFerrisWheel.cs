using SpaceStation.Audio;
using SpaceStation.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 2단계 식량 공장 "유리 바퀴 관람차"의 바퀴 하나 (MODELING 7-10 직접 체험 "관람차 탑승"): 바퀴가 곤돌라 한 칸씩 돌고 섬.
    /// 곤돌라는 바퀴 핀에 매달려 늘 수평 (회전 없이 위치만 돎). 맨 아래에 선 곤돌라만 다리 · 승강장 문 · 곤돌라 문이 열림 (<see cref="_dwell"/>초), 다 닫히면 다음 칸까지 <see cref="_travel"/>초.
    /// 문 자리에 플레이어가 있으면 닫지 않음. 문이 닫힐 때 곤돌라 안에 선 플레이어는 함께 실려 감 (걷기 멈춤 · 둘러보기 됨, <see cref="FirstPersonController.Ride"/>), 다시 맨 아래에서 문이 열리면 걸음.
    /// 벤치: 보고 F = 앉음 (문 쪽을 보고, 맨 아래에서도 앉은 채 계속 돎), 다시 F = 일어남. 곤돌라에 탄 주민 = 벤치 자리에 생긴 주민을 곤돌라로 옮겨 태움 (돌아다니지 않음).
    /// 위치는 모두 템플릿 루트(이 물체) 기준, 시간은 실제 시간 (엘리베이터 · 기차와 같음). 템플릿 빌더가 configure에서 붙임.
    /// </summary>
    [DefaultExecutionOrder(-320)] // InteriorMode(-300)보다 먼저: 앉은 채 누른 F를 여기서 먼저 씀
    public sealed class InteriorFerrisWheel : MonoBehaviour
    {
        private enum Phase { Extending, Opening, Open, Closing, Retracting, Moving }

        private const float Step = 72f;
        private const float SignInterval = 0.5f;
        private const float RiderInterval = 0.5f;

        [Tooltip("바퀴 축 — 바퀴 조각이 이 아래에서 돎 (템플릿 z축)")]
        [SerializeField] private Transform _wheel;
        [Tooltip("곤돌라 핀 자리 (쉬는 자세 = 0번이 맨 아래, 각 270 + 72 × 번호)")]
        [SerializeField] private Transform[] _pods;
        [SerializeField] private Transform[] _doors;
        [SerializeField] private Transform[] _seats;
        [SerializeField] private Transform _bridge;
        [SerializeField] private Transform _gateLeft;
        [SerializeField] private Transform _gateRight;
        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private string _title;
        [Tooltip("간판 자리 (템플릿 기준)")]
        [SerializeField] private Vector3 _sign;
        [Tooltip("곤돌라 핀 → 바닥")]
        [SerializeField] private float _hang = 2.25f;
        [SerializeField] private float _doorTravel = 0.98f;
        [SerializeField] private float _gateTravel = 0.62f;
        [SerializeField] private float _bridgeTravel = 1.3f;
        [SerializeField] private float _doorSeconds = 0.9f;
        [SerializeField] private float _bridgeSeconds = 1f;
        [SerializeField] private float _dwell = 8f;
        [SerializeField] private float _travel = 7f;
        [Tooltip("처음 쉬는 시간에 더함 (바퀴 둘이 엇갈리게)")]
        [SerializeField] private float _startDelay;
        [Tooltip("앉았을 때 눈높이 (곤돌라 바닥에서 m)")]
        [SerializeField] private float _seatedEye = 1.12f;
        [Tooltip("탄 주민 자리 (템플릿 기준, 쉬는 자세) · 곤돌라 번호")]
        [SerializeField] private Vector3[] _riderSeats = new Vector3[0];
        [SerializeField] private int[] _riderPods = new int[0];

        private Phase _phase = Phase.Open;
        private int _bottom;            // 맨 아래 곤돌라
        private float _open = 1f;       // 문 열림 (0 ~ 1)
        private float _extend = 1f;     // 다리 나옴 (0 ~ 1)
        private float _timer;
        private float _angle;
        private float _from;
        private bool _riding;
        private bool _seated;
        private int _ridingPod = -1;
        private int _standFrame = -1;
        private float _signTimer, _riderTimer;
        private Vector3 _hub;
        private Vector3[] _podRest, _doorBase;
        private Vector3 _bridgeBase, _gateLeftBase, _gateRightBase;
        private Collider[][] _podColliders;
        private Collider[] _seatColliders;
        private FirstPersonController _player;
        private TextMeshPro _signText;
        private Canvas _canvas;
        private TextMeshProUGUI _hint;
        private string _hintShown;

        public int Bottom => _bottom;
        public bool Moving => _phase == Phase.Moving;
        public bool Seated => _seated;
        public bool Riding => _riding;
        public bool DoorsOpen => _phase == Phase.Open;
        public int RidingPod => _ridingPod;

        public void Configure(Transform wheel, Transform[] pods, Transform[] doors, Transform[] seats, Transform bridge, Transform gateLeft, Transform gateRight, TMP_FontAsset font, string title,
            Vector3 sign, float hang, float startDelay, Vector3[] riderSeats, int[] riderPods)
        {
            _wheel = wheel;
            _pods = pods;
            _doors = doors;
            _seats = seats;
            _bridge = bridge;
            _gateLeft = gateLeft;
            _gateRight = gateRight;
            _font = font;
            _title = title;
            _sign = sign;
            _hang = hang;
            _startDelay = startDelay;
            _riderSeats = riderSeats;
            _riderPods = riderPods;
        }

        private void Awake()
        {
            if (_wheel == null)
                return;
            _hub = _wheel.localPosition;
            int n = _pods.Length;
            _podRest = new Vector3[n];
            _doorBase = new Vector3[n];
            _podColliders = new Collider[n][];
            _seatColliders = new Collider[n];
            for (int k = 0; k < n; k++)
            {
                _podRest[k] = _pods[k].localPosition;
                _doorBase[k] = _doors[k].localPosition;
                _podColliders[k] = _pods[k].GetComponentsInChildren<Collider>(true);
                _seatColliders[k] = _seats[k].GetComponent<Collider>();
                int pod = k;
                var use = _seats[k].gameObject.AddComponent<InteriorInteractable>();
                use.Tap = true;
                use.Prompt = () => "앉기";
                use.Completed = () => Sit(pod);
            }
            _bridgeBase = _bridge.localPosition;
            _gateLeftBase = _gateLeft.localPosition;
            _gateRightBase = _gateRight.localPosition;
            BuildSign();
            _timer = _dwell + _startDelay;
            UpdateColliders();
            Apply();
        }

        private void OnDisable()
        {
            // 내부에서 나가거나 방이 꺼지면 바퀴는 다음 칸에 바로 세우고 실려 가던 플레이어를 내려놓음
            if (_phase == Phase.Moving)
            {
                _angle = _from + Step;
                Stop();
                Apply();
            }
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
            if (_wheel == null)
                return;
            float dt = Time.unscaledDeltaTime;
            if (_player == null)
                _player = FindFirstObjectByType<FirstPersonController>();
            if (_seated && KeyBindings.WasPressed(GameAction.Interact))
                StandUp();
            switch (_phase)
            {
                case Phase.Extending:
                    _extend = Mathf.MoveTowards(_extend, 1f, dt / _bridgeSeconds);
                    if (_extend >= 1f)
                    {
                        _phase = Phase.Opening;
                        PlayNear(l => l.UiOpen);
                    }
                    break;
                case Phase.Opening:
                    _open = Mathf.MoveTowards(_open, 1f, dt / _doorSeconds);
                    if (_open >= 1f)
                    {
                        _phase = Phase.Open;
                        _timer = _dwell;
                        if (_riding && !_seated && _ridingPod == _bottom)
                        {
                            _player.Dismount();
                            _riding = false;
                            _ridingPod = -1;
                            UpdateColliders();
                        }
                    }
                    break;
                case Phase.Open:
                    _timer -= dt;
                    if (_timer <= 0f)
                    {
                        if (InDoorZone())
                            _timer = 0.5f; // 문 앞에 있으면 기다림
                        else
                        {
                            _phase = Phase.Closing;
                            PlayNear(l => l.UiClose);
                        }
                    }
                    break;
                case Phase.Closing:
                    if (InDoorZone())
                    {
                        _phase = Phase.Opening; // 사이에 들어오면 다시 엶
                        break;
                    }
                    _open = Mathf.MoveTowards(_open, 0f, dt / _doorSeconds);
                    if (_open <= 0f)
                        _phase = Phase.Retracting;
                    break;
                case Phase.Retracting:
                    _extend = Mathf.MoveTowards(_extend, 0f, dt / _bridgeSeconds);
                    if (_extend <= 0f)
                        Depart();
                    break;
                case Phase.Moving:
                    _timer += dt / _travel;
                    _angle = _from + Step * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_timer));
                    if (_timer >= 1f)
                    {
                        _angle = _from + Step;
                        Stop();
                    }
                    break;
            }
            Apply();
            CaptureRiders(dt);
            UpdateSign(dt);
            UpdateHint();
        }

        private void Depart()
        {
            _from = _angle;
            _phase = Phase.Moving;
            _timer = 0f;
            if (_player != null && !_riding && InPod(_bottom, _player.transform.position))
            {
                _player.Ride(_pods[_bottom]);
                _riding = true;
                _ridingPod = _bottom;
            }
            UpdateColliders();
        }

        private void Stop()
        {
            _angle = Mathf.Repeat(_angle, 360f);
            _bottom = (_bottom + _pods.Length - 1) % _pods.Length;
            _phase = Phase.Extending;
            UpdateColliders();
        }

        private void Apply()
        {
            _wheel.localRotation = Quaternion.Euler(0f, 0f, _angle);
            var turn = Quaternion.Euler(0f, 0f, _angle);
            for (int k = 0; k < _pods.Length; k++)
            {
                _pods[k].localPosition = _hub + turn * (_podRest[k] - _hub);
                float d = k == _bottom ? Mathf.SmoothStep(0f, 1f, _open) * _doorTravel : 0f;
                _doors[k].localPosition = _doorBase[k] + Vector3.right * d;
            }
            float e = Mathf.SmoothStep(0f, 1f, _open) * _gateTravel;
            _gateLeft.localPosition = _gateLeftBase + Vector3.left * e;
            _gateRight.localPosition = _gateRightBase + Vector3.right * e;
            _bridge.localPosition = _bridgeBase + Vector3.back * ((1f - Mathf.SmoothStep(0f, 1f, _extend)) * _bridgeTravel);
        }

        /// <summary>충돌체는 맨 아래에 선 곤돌라만 (돌아가는 동안은 끔), 벤치는 그 곤돌라 + 타고 있는 곤돌라.</summary>
        private void UpdateColliders()
        {
            for (int k = 0; k < _pods.Length; k++)
            {
                bool stopped = k == _bottom && _phase != Phase.Moving;
                foreach (var c in _podColliders[k])
                {
                    if (c != null)
                        c.enabled = stopped;
                }
                if (_seatColliders[k] != null)
                    _seatColliders[k].enabled = !_seated && (stopped || k == _ridingPod);
            }
        }

        // ---------------- 플레이어 ----------------

        /// <summary>플레이어 발이 k번 곤돌라 안인지.</summary>
        public bool InPod(int k, Vector3 worldFeet)
        {
            var p = transform.InverseTransformPoint(worldFeet) - _pods[k].localPosition;
            float h = p.y + _hang;
            return Mathf.Abs(p.x) < 0.92f && Mathf.Abs(p.z) < 0.92f && h > -0.4f && h < 1.2f;
        }

        /// <summary>플레이어가 다리 · 문턱에 있음 — 문을 닫지 않음.</summary>
        private bool InDoorZone()
        {
            if (_player == null || _riding)
                return false;
            var bottomFloor = _pods[_bottom].localPosition + Vector3.down * _hang;
            var p = transform.InverseTransformPoint(_player.transform.position) - bottomFloor;
            return Mathf.Abs(p.x) < 0.75f && p.z > -2.85f && p.z < -0.62f && p.y > -0.5f && p.y < 1.3f;
        }

        private void Sit(int pod)
        {
            if (_seated || _player == null || Time.frameCount == _standFrame)
                return; // 일어나기와 같은 프레임의 F (InteriorMode가 뒤에서 다시 부름)
            var feet = _pods[pod].TransformPoint(new Vector3(-0.36f, -_hang, 0.48f));
            float yaw = (transform.rotation * Quaternion.LookRotation(Vector3.back)).eulerAngles.y;
            _player.RideAt(_pods[pod], feet, _seatedEye, yaw);
            _riding = true;
            _seated = true;
            _ridingPod = pod;
            UpdateColliders();
            AudioService.TryPlay(l => l.UiOpen);
        }

        private void StandUp()
        {
            _standFrame = Time.frameCount;
            _seated = false;
            _player.RideAt(_pods[_ridingPod], _pods[_ridingPod].TransformPoint(new Vector3(0f, -_hang, -0.1f)), FirstPersonController.EyeHeight);
            if (_phase != Phase.Moving && _ridingPod == _bottom)
            {
                _player.Dismount();
                _riding = false;
                _ridingPod = -1;
            }
            UpdateColliders();
            AudioService.TryPlay(l => l.UiClose);
        }

        private void ReleasePlayer()
        {
            if (_seated)
            {
                _seated = false;
                if (_player != null)
                    _player.RideAt(_pods[_ridingPod], _pods[_ridingPod].TransformPoint(new Vector3(0f, -_hang, -0.1f)), FirstPersonController.EyeHeight);
            }
            if (_riding && _player != null)
                _player.Dismount();
            _riding = false;
            _ridingPod = -1;
        }

        private void PlayNear(System.Func<SoundLibrary, SoundCue> cue)
        {
            if (_player == null)
                return;
            var p = transform.InverseTransformPoint(_player.transform.position) - _hub;
            if (_riding || p.sqrMagnitude < 144f)
                AudioService.TryPlay(cue);
        }

        // ---------------- 탄 주민 ----------------

        /// <summary>벤치 자리에 생긴 주민을 곤돌라로 옮겨 태움 (주민은 방을 지을 때 쉬는 자세 자리에 생김, 바퀴가 어디 있든 같음).</summary>
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
                if (figure.transform.parent != null && figure.transform.parent.parent == transform)
                    continue;   // 이미 곤돌라에 탐
                var p = transform.InverseTransformPoint(figure.transform.position);
                for (int i = 0; i < _riderSeats.Length; i++)
                {
                    var s = _riderSeats[i];
                    if (new Vector2(p.x - s.x, p.z - s.z).sqrMagnitude > 0.04f || Mathf.Abs(p.y - s.y) > 0.6f)
                        continue;
                    int pod = _riderPods[i];
                    var wander = figure.GetComponent<ResidentWander>();
                    if (wander != null)
                        Destroy(wander);
                    figure.transform.SetParent(_pods[pod], true);
                    figure.transform.localPosition = p - _podRest[pod];
                    break;
                }
            }
        }

        // ---------------- 간판 · 안내 ----------------

        private void BuildSign()
        {
            if (_font == null)
                return;
            var go = new GameObject("WheelSign");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = _sign;
            _signText = go.AddComponent<TextMeshPro>();
            _signText.font = _font;
            _signText.fontSize = 2.2f;
            _signText.color = new Color(0.92f, 0.96f, 1f);
            _signText.alignment = TextAlignmentOptions.Center;
            _signText.textWrappingMode = TextWrappingModes.NoWrap;
            _signText.rectTransform.sizeDelta = new Vector2(2.6f, 0.6f);
        }

        /// <summary>다음 곤돌라가 맨 아래에 서서 문이 열릴 때까지 (초).</summary>
        public float SecondsUntilOpen()
        {
            switch (_phase)
            {
                case Phase.Moving:
                    return (1f - Mathf.Clamp01(_timer)) * _travel + _bridgeSeconds + _doorSeconds;
                case Phase.Extending:
                    return (1f - _extend) * _bridgeSeconds + _doorSeconds;
                case Phase.Opening:
                    return (1f - _open) * _doorSeconds;
                default:
                    return 0f;
            }
        }

        private void UpdateSign(float dt)
        {
            if (_signText == null)
                return;
            _signTimer -= dt;
            if (_signTimer > 0f)
                return;
            _signTimer = SignInterval;
            string status = _phase switch
            {
                Phase.Open => "문 열림 · 타세요",
                Phase.Closing or Phase.Retracting => "문 닫힘 · 곧 출발",
                _ => $"다음 곤돌라 약 {Mathf.CeilToInt(SecondsUntilOpen())}초",
            };
            _signText.SetText($"{_title}\n<size=62%>{status}</size>");
        }

        private void UpdateHint()
        {
            string text = null;
            if (_riding)
            {
                int stops = (_bottom - _ridingPod + _pods.Length) % _pods.Length;
                string where = stops == 0 ? (Moving ? "곧 맨 아래" : "맨 아래 · 내릴 수 있음") : $"맨 아래까지 {stops}칸";
                text = _seated ? $"{where}   ·   {KeyBindings.Label(GameAction.Interact)}  일어나기" : where;
            }
            else if (_player != null && _phase != Phase.Moving && InPod(_bottom, _player.transform.position))
                text = _phase == Phase.Open ? "관람차 · 벤치에 앉거나 서서 출발을 기다림" : "곧 출발";
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
            var go = new GameObject("WheelHint", typeof(RectTransform));
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
            if (_font != null)
                _hint.font = _font;
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
