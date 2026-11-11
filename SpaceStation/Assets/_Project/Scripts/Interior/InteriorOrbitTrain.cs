using SpaceStation.Audio;
using SpaceStation.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 3단계 궤도 도시의 금빛 궤도열차 (사용자 2026-11-10 직접 체험 + 미니게임 "궤도열차를 정거장에 정확히 멈추기"):
    /// 역 (앞 오른쪽) 승강장에서 앞 차 운전석을 보고 F = 앉음 → W 가속 · S 브레이크로 섬을 한 바퀴 돌아 앞 차 코를 정지선에 맞춰 세움.
    /// 정지선 앞뒤 8 m 안에서 멈추면 판정 (거리 · 등급 · 최고 기록 — 색이 아니라 글로, 색약), 잠깐 보여 준 뒤 천천히 정지선까지 맞춰 섬. 8 m 넘게 지나치면 한 바퀴 더.
    /// 역에 서 있을 때만 F = 내림 (승강장으로). 운전석에 앉아 있는 동안 걷기 키는 열차 조작 (걷기는 멈춤, <see cref="FirstPersonController.RideAt"/>).
    /// 위치는 템플릿 루트 기준, 열차 조각은 허브 축 피벗 아래 (쉬는 자세 = 역). 시간은 실제 시간 (엘리베이터 · 은하철도와 같음). 값 = <see cref="OrbitalCity"/>.
    /// </summary>
    [DefaultExecutionOrder(-320)] // InteriorMode(-300)보다 먼저: 앉은 채 누른 F를 여기서 먼저 씀
    public sealed class InteriorOrbitTrain : MonoBehaviour
    {
        private enum Phase { Rest, Driving, Result, Align }

        private const float MaxSpeed = 7f, Accel = 1.6f, Brake = 3.2f, Coast = 0.12f, AlignSpeed = 0.8f;
        private const float JudgeZone = 8f, ResultSeconds = 3.5f, SeatedEye = 1.15f;

        [Tooltip("허브 축 — 열차 조각이 이 아래에서 돎 (쉬는 자세 = 역)")]
        [SerializeField] private Transform _pivot;
        [Tooltip("운전석 (피벗 아래, 보고 F = 앉기)")]
        [SerializeField] private Collider _seat;
        [SerializeField] private TMP_FontAsset _font;
        [Tooltip("역 기록판 자리 (템플릿 기준) · 바라보는 yaw")]
        [SerializeField] private Vector3 _boardPosition;
        [SerializeField] private float _boardYaw;

        private static float _best = -1f;

        private Phase _phase = Phase.Rest;
        private float _s;          // 쉬는 자리에서 달린 거리 (m, 앞 = 각이 늘어나는 쪽)
        private float _v;
        private float _resultTimer;
        private float _noticeTimer;
        private string _notice;
        private bool _seated;
        private int _standFrame = -1;
        private FirstPersonController _player;
        private TextMeshPro[] _boards;
        private Canvas _canvas;
        private TextMeshProUGUI _hint;
        private string _hintShown;

        public bool Seated => _seated;
        public bool Moving => _phase == Phase.Driving && _v > 0.01f || _phase == Phase.Align;
        /// <summary>쉬는 자리에서 달린 거리 (m).</summary>
        public float Distance => _s;
        public float Speed => _v;
        public static float Best => _best;

        public void Configure(Transform pivot, Collider seat, TMP_FontAsset font, Vector3 boardPosition, float boardYaw)
        {
            _pivot = pivot;
            _seat = seat;
            _font = font;
            _boardPosition = boardPosition;
            _boardYaw = boardYaw;
        }

        private void Awake()
        {
            if (_seat != null)
            {
                var use = _seat.gameObject.AddComponent<InteriorInteractable>();
                use.Tap = true;
                use.Prompt = () => "운전석에 앉기";
                use.Blocked = () => _phase != Phase.Rest ? "열차가 역에 서면 앉을 수 있어요" : null;
                use.Completed = Sit;
            }
            BuildBoards();
            UpdateBoards();
        }

        private void OnDisable()
        {
            // 내부에서 나가거나 방이 꺼지면 열차는 역에 바로 세우고 앉은 플레이어를 내려놓음
            ReleasePlayer();
            _phase = Phase.Rest;
            _s = 0f;
            _v = 0f;
            ApplyPose();
            ShowHint(null);
        }

        private void OnDestroy()
        {
            if (_canvas != null)
                Destroy(_canvas.gameObject);
        }

        private void Update()
        {
            if (_pivot == null)
                return;
            float dt = Time.unscaledDeltaTime;
            if (_player == null)
                _player = FindFirstObjectByType<FirstPersonController>();
            if (_seated && _phase == Phase.Rest && KeyBindings.WasPressed(GameAction.Interact))
                StandUp();
            if (_noticeTimer > 0f)
                _noticeTimer -= dt;
            switch (_phase)
            {
                case Phase.Rest:
                    if (_seated && KeyBindings.IsPressed(GameAction.CameraForward))
                    {
                        _phase = Phase.Driving;
                        AudioService.TryPlay(l => l.UiOpen);
                    }
                    break;
                case Phase.Driving:
                    Drive(dt);
                    break;
                case Phase.Result:
                    _resultTimer -= dt;
                    if (_resultTimer <= 0f)
                        _phase = Phase.Align;
                    break;
                case Phase.Align:
                    float target = OrbitalCity.Lap;
                    _s = Mathf.MoveTowards(_s, target, AlignSpeed * dt);
                    if (Mathf.Abs(_s - target) < 0.001f)
                    {
                        _s = 0f;
                        _phase = Phase.Rest;
                        AudioService.TryPlay(l => l.SupplyArrive);
                    }
                    break;
            }
            ApplyPose();
            UpdateHint();
        }

        private void Drive(float dt)
        {
            bool gas = KeyBindings.IsPressed(GameAction.CameraForward);
            bool brake = KeyBindings.IsPressed(GameAction.CameraBack);
            if (brake)
                _v = Mathf.Max(0f, _v - Brake * dt);
            else if (gas)
                _v = Mathf.Min(MaxSpeed, _v + Accel * dt);
            else
                _v = Mathf.Max(0f, _v - Coast * dt);
            _s += _v * dt;
            float lap = OrbitalCity.Lap;
            float error = _s - lap;   // − = 정지선 앞 (모자람), + = 지나침
            if (error > JudgeZone)
            {
                _s -= lap;
                Notice("정지선을 지나쳤어요! 한 바퀴 더!");   // 글꼴에 긴 줄표가 없어 □로 나왔음 (FontCoverageTests)
                return;
            }
            if (_v > 0f || gas)
                return;
            if (Mathf.Abs(error) <= JudgeZone && _s > lap * 0.5f)
                Judge(error);
            else if (_s > lap * 0.5f)
                Notice($"역까지 {-error:F0} m 남았어요 · W로 다시 출발");
        }

        private void Judge(float error)
        {
            float d = Mathf.Abs(error);
            bool record = _best < 0f || d < _best;
            if (record)
                _best = d;
            string where = d < 0.005f ? "정지선에 딱" : error < 0f ? $"정지선 {d:F2} m 앞" : $"정지선을 {d:F2} m 지나서";
            Notice($"{where}  ·  {OrbitalCity.Grade(d)}" + (record ? "  ·  최고 기록!" : $"  ·  최고 {_best:F2} m"), ResultSeconds + 1f);
            _phase = Phase.Result;
            _resultTimer = ResultSeconds;
            UpdateBoards();
            AudioService.TryPlay(l => record ? l.UiOpen : l.UiClose);
        }

        private void Notice(string text, float seconds = 3f)
        {
            _notice = text;
            _noticeTimer = seconds;
        }

        /// <summary>달린 거리 s의 피벗 회전 (각이 늘어나는 쪽 = Unity y 회전 −).</summary>
        public static Quaternion PoseAt(float s) => Quaternion.Euler(0f, -s / OrbitalCity.TrackRadius * Mathf.Rad2Deg, 0f);

        private void ApplyPose()
        {
            if (_pivot != null)
                _pivot.localRotation = PoseAt(_s);
        }

        // ---------------- 플레이어 ----------------

        /// <summary>쉬는 자세 피벗 기준 운전석 발 자리 (앞 차 가운데에서 계기판 쪽 0.15 m · 차 바닥).</summary>
        private static Vector3 SeatFeet()
        {
            var p = OrbitalCity.Polar(OrbitalCity.TrackRadius, OrbitalCity.FrontAngle + 0.15f / OrbitalCity.TrackRadius * Mathf.Rad2Deg, OrbitalCity.CarFloor);
            return p - OrbitalCity.Hub;
        }

        /// <summary>내리는 자리 = 운전석 옆 승강장 (바깥쪽).</summary>
        private Vector3 PlatformPoint() => transform.TransformPoint(OrbitalCity.Polar(OrbitalCity.PlatformInner + 0.9f, OrbitalCity.FrontAngle, OrbitalCity.PlatformTop));

        private void Sit()
        {
            if (_seated || _player == null || _phase != Phase.Rest || Time.frameCount == _standFrame)
                return; // 일어나기와 같은 프레임의 F (InteriorMode가 뒤에서 다시 부름)
            var feet = _pivot.TransformPoint(SeatFeet());
            float yaw = transform.eulerAngles.y + _pivot.localEulerAngles.y + OrbitalCity.ForwardYaw(OrbitalCity.FrontAngle);
            _player.RideAt(_pivot, feet, SeatedEye, yaw, 8f);
            _seated = true;
            if (_seat != null)
                _seat.enabled = false;
            Notice("W 출발 · S 브레이크 · 한 바퀴 돌아 앞 차 코를 금빛 정지선에 맞춰 세워요", 5f);
            AudioService.TryPlay(l => l.UiOpen);
        }

        private void StandUp()
        {
            _standFrame = Time.frameCount;
            ReleasePlayer();
            AudioService.TryPlay(l => l.UiClose);
        }

        private void ReleasePlayer()
        {
            if (!_seated)
                return;
            _seated = false;
            if (_seat != null)
                _seat.enabled = true;
            if (_player == null)
                return;
            var yaw = transform.eulerAngles.y + OrbitalCity.OutwardYaw(OrbitalCity.FrontAngle) + 180f;
            _player.RideAt(_pivot, PlatformPoint(), FirstPersonController.EyeHeight, yaw);
            _player.Dismount();
        }

        // ---------------- 기록판 · 안내 ----------------

        private void BuildBoards()
        {
            if (_font == null)
                return;
            _boards = new TextMeshPro[2];
            for (int k = 0; k < 2; k++)   // 앞뒤 두 장 (한 장이면 뒤에서 거울 글씨)
            {
                var go = new GameObject("TrainBoard" + k);
                go.transform.SetParent(transform, false);
                go.transform.localPosition = _boardPosition;
                go.transform.localRotation = Quaternion.Euler(0f, _boardYaw + 180f * k, 0f);
                var text = go.AddComponent<TextMeshPro>();
                text.font = _font;
                text.fontSize = 2.2f;
                text.enableAutoSizing = true;
                text.fontSizeMin = 1f;
                text.fontSizeMax = 2.2f;
                text.color = new Color(1f, 0.95f, 0.82f);
                text.alignment = TextAlignmentOptions.Center;
                text.rectTransform.sizeDelta = new Vector2(2.2f, 0.9f);
                text.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _boards[k] = text;
            }
        }

        private void UpdateBoards()
        {
            if (_boards == null)
                return;
            string best = _best < 0f ? "최고 기록 없음" : $"최고 기록  정지선에서 {_best:F2} m";
            foreach (var b in _boards)
                b.SetText($"궤도열차  정지선 맞추기\n<size=70%>{best}</size>");
        }

        private void UpdateHint()
        {
            string text = null;
            if (_seated)
            {
                string keys = $"{KeyBindings.Label(GameAction.CameraForward)} 가속 · {KeyBindings.Label(GameAction.CameraBack)} 브레이크";
                text = _phase switch
                {
                    Phase.Rest => $"{KeyBindings.Label(GameAction.CameraForward)} 출발   ·   {KeyBindings.Label(GameAction.Interact)} 내리기",
                    Phase.Driving => $"속도 {_v * 3.6f:F0} km/h   ·   정지선까지 {Mathf.Max(0f, OrbitalCity.Lap - _s):F1} m   ·   {keys}",
                    Phase.Result => "판정 중",
                    _ => "정지선에 맞춰 서는 중",
                };
                if (_noticeTimer > 0f && !string.IsNullOrEmpty(_notice))
                    text = _notice + "\n<size=80%>" + text + "</size>";
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
            var go = new GameObject("OrbitTrainHint", typeof(RectTransform));
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
            Place(back.rectTransform, new Vector2(960f, 96f));
            back.color = new Color(0.03f, 0.05f, 0.1f, 0.62f);
            back.raycastTarget = false;
            _hint = new GameObject("Hint", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            _hint.transform.SetParent(go.transform, false);
            Place(_hint.rectTransform, new Vector2(930f, 88f));
            if (_font != null)
                _hint.font = _font;
            _hint.fontSize = 26f;
            _hint.enableAutoSizing = true;
            _hint.fontSizeMin = 16f;
            _hint.fontSizeMax = 26f;
            _hint.color = new Color(0.92f, 0.96f, 1f);
            _hint.alignment = TextAlignmentOptions.Center;
            _hint.raycastTarget = false;
        }

        private static void Place(RectTransform rt, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, 84f);
            rt.sizeDelta = size;
        }
    }
}
