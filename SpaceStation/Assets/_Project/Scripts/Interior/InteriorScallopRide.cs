using SpaceStation.Audio;
using SpaceStation.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 3단계 궤도 항만 "해마 가리비 배 뱃놀이" (사용자 결정 2026-11-16 체험, "가리비 배 뱃놀이 체험 진행해줘"): 해마 한 쌍이 끄는 가리비 배 넷이
    /// 석호 배 길 한 바퀴 (<see cref="OrbitalPort.CourseLoop"/> — 선착장 → 산호 섬 궁전 · 진주 계단 → 산호 방파제 · 왕관 바다 문 → 진주 잔교 · 큰 조개 부두 → 모래톱 → 선착장)를
    /// 천천히 돌고, 선착장 앞에 한 척씩 섬 (8초, 플레이어가 앉아 있으면 3초 뒤 출발). 앞 배와 3.6 m 간격 — 선착장에 배가 있으면 뒤에서 기다림.
    /// 플레이어 = 정차한 배 앉음판 왼쪽 · 오른쪽 (보고 F = 앉기, 정차 중 다시 F = 선착장 마루에 내림, 달리는 중이면 한 바퀴 더). 배는 물 위에서 살짝 출렁이고
    /// 앉은 시선은 배를 따라 돎 (출렁임 기울기는 따라가지 않음). 화면 아래 안내 줄 = 지나는 곳 이름, 선착장 안내판 = 다음 배까지 (색 대신 글 · 깜빡임).
    /// 모듈이 멈추면 돌던 배는 선착장까지 와서 기다림. 위치는 모두 템플릿 루트 (이 물체) 기준, 시간은 실제 시간. 템플릿 빌더가 configure에서 붙임.
    /// </summary>
    [DefaultExecutionOrder(-320)] // InteriorMode(-300)보다 먼저: 앉은 채 누른 F를 여기서 먼저 씀
    public sealed class InteriorScallopRide : InteriorRoomDisplay
    {
        private const float Accel = 0.45f, Brake = 0.55f;
        private const float SignInterval = 0.5f;

        [SerializeField] private Transform[] _boats;   // 배 피벗 (원점 = 배 가운데 수면, +z = 배 길 앞 — 해마 쪽)
        [SerializeField] private Transform[] _seats;   // 배마다 앉음판 왼쪽 · 오른쪽 (BoxCollider, 피벗 자식)
        [SerializeField] private float[] _startS;
        [SerializeField] private TMP_FontAsset _font;

        private float[] _s, _v;
        private bool[] _armed;
        private Collider[] _seatColliders;
        private int _docked = -1;
        private float _timer;
        private int _ride = -1, _rideSeat;
        private int _sitFrame = -1;
        private float _noteTimer;
        private Transform _anchor;
        private FirstPersonController _player;
        private TextMeshPro _signText;
        private float _signTimer;
        private Canvas _canvas;
        private TextMeshProUGUI _hint;
        private string _hintShown;

        public bool Riding => _ride >= 0;
        public int DockedBoat => _docked;
        public float BoatS(int b) => _s != null ? _s[b] : 0f;

        public void Configure(Transform[] boats, Transform[] seats, float[] startS, TMP_FontAsset font)
        {
            _boats = boats;
            _seats = seats;
            _startS = startS;
            _font = font;
        }

        protected override void Refresh() { }

        private bool Stopped => Polled && !Active;

        private void Awake()
        {
            if (_boats == null || _boats.Length == 0)
                return;
            int n = _boats.Length;
            float loop = OrbitalPort.CourseLoop;
            _s = new float[n];
            _v = new float[n];
            _armed = new bool[n];
            for (int b = 0; b < n; b++)
            {
                _s[b] = OrbitalPort.CourseWrap(_startS[b]);
                _armed[b] = _s[b] > loop * 0.5f;
                if (_s[b] < 0.01f)
                    _docked = b;
            }
            _seatColliders = new Collider[_seats.Length];
            for (int k = 0; k < _seats.Length; k++)
            {
                _seatColliders[k] = _seats[k].GetComponent<Collider>();
                int seat = k;
                var use = _seats[k].gameObject.AddComponent<InteriorInteractable>();
                use.Tap = true;
                use.Prompt = () => "가리비 배 타기";
                use.Blocked = () => SeatBlocked(seat);
                use.Completed = () => Sit(seat);
            }
            _anchor = new GameObject("RideAnchor").transform;
            _anchor.SetParent(transform, false);
            BuildSign();
            _timer = OrbitalPort.BoatDwell;
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
                if (_ride == _docked)
                    StandUp();
                else
                    _noteTimer = 2f;
            }
            Dock(dt);
            for (int b = 0; b < _boats.Length; b++)
            {
                if (b != _docked)
                    Move(b, dt);
            }
            Apply();
            if (_ride >= 0)
                PlaceAnchor();
            UpdateSign(dt);
            UpdateHint(dt);
        }

        // ---------------- 배 ----------------

        private void Move(int b, float dt)
        {
            float loop = OrbitalPort.CourseLoop;
            float s = _s[b];
            float v = Mathf.Min(OrbitalPort.BoatCruise, _v[b] + Accel * dt);
            // 멈출 자리: 선착장 (한 바퀴 돌아온 배) · 앞 배 뒤 3.6 m
            float stop = _armed[b] ? loop - s : float.MaxValue;
            stop = Mathf.Min(stop, LeadGap(b) - OrbitalPort.BoatGap);
            if (stop < float.MaxValue)
                v = Mathf.Min(v, Mathf.Sqrt(2f * Brake * Mathf.Max(0f, stop)));
            if (stop <= 0.002f)
                v = 0f;
            _v[b] = v;
            s += v * dt;
            if (_armed[b] && s >= loop - 0.003f)
            {
                s = 0f;
                _v[b] = 0f;
                _armed[b] = false;
                if (_docked < 0)
                    Arrive(b);
            }
            else if (!_armed[b] && s > loop * 0.5f)
                _armed[b] = true;
            _s[b] = OrbitalPort.CourseWrap(s);
        }

        /// <summary>바로 앞 배까지 배 길 거리 (가는 쪽으로).</summary>
        private float LeadGap(int b)
        {
            float best = float.MaxValue;
            for (int o = 0; o < _boats.Length; o++)
            {
                if (o == b)
                    continue;
                float gap = OrbitalPort.CourseWrap(_s[o] - _s[b]);
                if (gap > 1e-4f)
                    best = Mathf.Min(best, gap);
            }
            return best;
        }

        private void Arrive(int b)
        {
            _docked = b;
            _timer = OrbitalPort.BoatDwell;
            PlayNear(l => l.SupplyArrive);
            UpdateColliders();
        }

        private void Dock(float dt)
        {
            if (_docked < 0 || Stopped)
                return;   // 모듈이 멈춤: 선착장에서 기다림
            _timer -= dt;
            if (_ride == _docked)
                _timer = Mathf.Min(_timer, OrbitalPort.BoatDwellSeated);
            if (_timer > 0f)
                return;
            _v[_docked] = 0f;
            _docked = -1;
            PlayNear(l => l.UiClose);
            UpdateColliders();
        }

        /// <summary>배 자세: 배 길 위 + 물 위 살짝 출렁임 (위아래 1.2 cm · 옆 1.2° · 앞뒤 0.8°, 배마다 박자가 다름).</summary>
        private void Pose(int b, out Vector3 pos, out Quaternion yaw, out Quaternion sway)
        {
            OrbitalPort.CoursePose(_s[b], out var at, out var dir);
            float t = Time.unscaledTime;
            pos = new Vector3(at.x, OrbitalPort.LagoonLevel + 0.012f * Mathf.Sin(1.4f * t + 1.7f * b), at.y);
            yaw = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y));
            sway = Quaternion.Euler(0.8f * Mathf.Sin(0.9f * t + 2f * b), 0f, 1.2f * Mathf.Sin(1.1f * t + b));
        }

        private void Apply()
        {
            for (int b = 0; b < _boats.Length; b++)
            {
                Pose(b, out var p, out var yaw, out var sway);
                _boats[b].localPosition = p;
                _boats[b].localRotation = yaw * sway;
            }
        }

        private void UpdateColliders()
        {
            for (int k = 0; k < _seats.Length; k++)
            {
                if (_seatColliders[k] != null)
                    _seatColliders[k].enabled = _ride < 0 && k / 2 == _docked;
            }
        }

        // ---------------- 플레이어 ----------------

        private string SeatBlocked(int seat)
        {
            if (seat / 2 != _docked)
                return "곧 출발";
            return null;
        }

        private void Sit(int seat)
        {
            int b = seat / 2;
            if (_ride >= 0 || _player == null || b != _docked)
                return;
            _ride = b;
            _rideSeat = seat % 2;
            _sitFrame = Time.frameCount;
            PlaceAnchor();
            _player.RideAt(_anchor, _anchor.position, OrbitalPort.BoatSeatedEye, _anchor.eulerAngles.y, 0f);
            UpdateColliders();
            AudioService.TryPlay(l => l.UiOpen);
        }

        private void StandUp()
        {
            float yaw = transform.eulerAngles.y;   // 석호 (+z — 떠나는 배 · 회전목마)를 보고 선착장 마루에 섬 (해변 쪽은 벽이 2.4 m 앞이라 하늘 화면만 보였음)
            _player.RideAt(transform, transform.TransformPoint(OrbitalPort.BoatExit), FirstPersonController.EyeHeight, yaw);
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
            _player.Teleport(transform.TransformPoint(OrbitalPort.BoatExit), transform.eulerAngles.y);
            ClearRide();
        }

        private void ClearRide()
        {
            _ride = -1;
            UpdateColliders();
        }

        /// <summary>앉은 자리 고정점 = 출렁이지 않는 배 자세의 발 자리 (앞 = 배 길 앞) — 출렁임으로 시선이 흔들리지 않게.</summary>
        private void PlaceAnchor()
        {
            Pose(_ride, out var p, out var yaw, out _);
            _anchor.localPosition = p + yaw * OrbitalPort.BoatFeet(_rideSeat);
            _anchor.localRotation = yaw;
        }

        private void PlayNear(System.Func<SoundLibrary, SoundCue> cue)
        {
            if (_player == null)
                return;
            var p = transform.InverseTransformPoint(_player.transform.position) - OrbitalPort.Stop;
            if (_ride >= 0 || p.sqrMagnitude < 144f)
                AudioService.TryPlay(cue);
        }

        // ---------------- 안내판 · 안내 줄 (색 대신 글 · 깜빡임) ----------------

        private void BuildSign()
        {
            var font = _font != null ? _font : Font;
            if (font == null)
                return;
            // 한 장이 보는 사람 쪽으로 돎 (해변에서 오는 쪽 · 선착장 마루에서 돌아보는 쪽 — 고정 한 장은 뒤에서 뒤집혀 보였고,
            //  앞뒤 두 장은 글자가 양면으로 그려져 서로 뒤집힌 글자가 겹쳤음)
            var go = new GameObject("ScallopRideSign");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = OrbitalPort.BoatSign;
            _signText = go.AddComponent<TextMeshPro>();
            _signText.font = font;
            _signText.fontSize = 1.6f;
            _signText.color = new Color(0.98f, 0.95f, 0.86f);
            _signText.alignment = TextAlignmentOptions.Center;
            _signText.textWrappingMode = TextWrappingModes.NoWrap;
            _signText.rectTransform.sizeDelta = new Vector2(2.0f, 0.5f);
        }

        /// <summary>안내판을 보는 사람 쪽으로 (가로로만 — 글자가 보는 눈에서 똑바로).</summary>
        private void FaceSign()
        {
            var cam = Camera.main;
            if (_signText == null || cam == null)
                return;
            var away = _signText.transform.position - cam.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude > 1e-4f)
                _signText.transform.rotation = Quaternion.LookRotation(away, Vector3.up);
        }

        /// <summary>다음 배가 선착장에 서기까지 (초, 대략 — 앞 배를 기다리는 시간은 빼고).</summary>
        public float SecondsUntilBoat()
        {
            if (_docked >= 0)
                return 0f;
            float best = float.MaxValue, loop = OrbitalPort.CourseLoop;
            for (int b = 0; b < _boats.Length; b++)
                best = Mathf.Min(best, (loop - _s[b]) / OrbitalPort.BoatCruise);
            return best < float.MaxValue ? best : 0f;
        }

        private void UpdateSign(float dt)
        {
            if (_signText == null)
                return;
            FaceSign();
            _signTimer -= dt;
            if (_signTimer > 0f)
                return;
            _signTimer = SignInterval;
            bool blink = Mathf.Repeat(Time.unscaledTime, 1f) < 0.5f;
            string status;
            if (_docked >= 0 && Stopped)
                status = "운행 멈춤 · 배 대기";
            else if (_docked >= 0 && _timer < 2f)
                status = blink ? "곧 출발" : "";
            else if (_docked >= 0)
                status = $"배 도착 · 타세요 ({KeyBindings.Label(GameAction.Interact)})";
            else
                status = $"다음 배 약 {Mathf.CeilToInt(SecondsUntilBoat())}초";
            _signText.SetText($"해마 가리비 배\n<size=58%>{status}</size>");
        }

        private void UpdateHint(float dt)
        {
            string text = null;
            if (_ride >= 0)
            {
                if (_noteTimer > 0f)
                    _noteTimer -= dt;
                string key = KeyBindings.Label(GameAction.Interact);
                if (_ride == _docked)
                    text = $"선착장   ·   {key}  내리기";
                else if (_noteTimer > 0f)
                    text = $"선착장에 서면 {key}로 내림";
                else
                    text = "해마 가리비 배 · " + OrbitalPort.CourseSection(_s[_ride]);
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
            var go = new GameObject("ScallopRideHint", typeof(RectTransform));
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
            Place(back.rectTransform, new Vector2(660f, 52f));
            back.color = new Color(0.03f, 0.05f, 0.1f, 0.62f);
            back.raycastTarget = false;
            _hint = new GameObject("Hint", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            _hint.transform.SetParent(go.transform, false);
            Place(_hint.rectTransform, new Vector2(640f, 44f));
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
