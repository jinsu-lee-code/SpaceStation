using SpaceStation.Audio;
using SpaceStation.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.Interior
{
    /// <summary>
    /// AI 중추 체험 "정원 드론 타고 뇌 위 날기" (사용자 2026-11-16 체험 1안, 값 = <see cref="AICore"/>): 드론 둥지 탑 옥상 착륙장의 큰 정원 드론 바구니 앉음판을 보고 F = 앉기 →
    /// 잠깐 뒤 떠올라 (<see cref="AICore.DroneLiftTime"/>초) 날 길 (<see cref="AICore.DroneRoutePose"/> — 드론 마당 앞마당 위 → 미로 위 타원 한 바퀴: 뒤 반구 금빛 신경 → 장미 문 → 앞 반구 보라 신경)을
    /// 천천히 날고 착륙장에 내려앉음 → F = 착륙장에 내림 (나는 중 F는 안내만). 드론은 나아가는 쪽을 보고 (앉은 자리 = 서쪽 트임을 봄) 살짝 앞으로 숙이며 위아래로 출렁임.
    /// 아무도 타지 않으면 착륙장에 서 있음. 화면 아래 안내 줄 = 지나는 곳 이름. 위치는 템플릿 루트 (이 물체) 기준, 시간은 실제 시간. 템플릿 빌더가 configure에서 붙임.
    /// </summary>
    [DefaultExecutionOrder(-320)] // InteriorMode(-300)보다 먼저: 앉은 채 누른 F를 여기서 먼저 씀
    public sealed class InteriorDroneRide : MonoBehaviour
    {
        public enum State { Docked, Boarding, Lift, Cruise, Land }
        private const float TiltDeg = 5f, BobHeight = 0.03f;

        [Tooltip("정원 드론 (따로 조각 Drone, 원점 = 바구니 밑 — 착륙장 가운데)")]
        [SerializeField] private Transform _drone;
        [Tooltip("앉음판 왼쪽 · 오른쪽 (BoxCollider, 드론 자식)")]
        [SerializeField] private Transform[] _seats;
        [SerializeField] private TMP_FontAsset _font;

        private State _state = State.Docked;
        private float _timer, _s, _v;
        private int _seat = -1, _sitFrame = -1;
        private float _noteTimer;
        private Collider[] _seatColliders;
        private Transform _anchor;
        private FirstPersonController _player;
        private Quaternion _yaw = Quaternion.identity;
        private Canvas _canvas;
        private TextMeshProUGUI _hint;
        private string _hintShown;

        public State Current => _state;
        public bool Riding => _seat >= 0;
        public float RouteS => _s;

        public void Configure(Transform drone, Transform[] seats, TMP_FontAsset font)
        {
            _drone = drone;
            _seats = seats;
            _font = font;
        }

        private void Awake()
        {
            if (_drone == null || _seats == null)
                return;
            _seatColliders = new Collider[_seats.Length];
            for (int k = 0; k < _seats.Length; k++)
            {
                _seatColliders[k] = _seats[k].GetComponent<Collider>();
                int seat = k;
                var use = _seats[k].gameObject.AddComponent<InteriorInteractable>();
                use.Tap = true;
                use.Prompt = () => "정원 드론 타기  <size=80%>(미로 위를 한 바퀴)</size>";
                use.Blocked = () => _state == State.Docked ? null : "곧 떠요";
                use.Completed = () => Sit(seat);
            }
            _anchor = new GameObject("DroneRideAnchor").transform;
            _anchor.SetParent(_drone, false);
            Place(AICore.DronePad, Quaternion.identity);
            UpdateColliders();
        }

        private void OnDisable()
        {
            ReleasePlayer();
            _state = State.Docked;
            _s = _v = 0f;
            if (_drone != null)
                Place(AICore.DronePad, Quaternion.identity);
            ShowHint(null);
        }

        private void OnDestroy()
        {
            if (_canvas != null)
                Destroy(_canvas.gameObject);
        }

        private void Update()
        {
            if (_drone == null)
                return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (_player == null)
                _player = FindFirstObjectByType<FirstPersonController>();
            if (_seat >= 0 && (_player == null || !_player.Riding))
                ClearRide();   // 패드 순간이동 등으로 탄 상태가 끊김
            if (_seat >= 0 && Time.frameCount != _sitFrame && KeyBindings.WasPressed(GameAction.Interact))
            {
                if (_state == State.Docked || _state == State.Boarding)
                    StandUp();
                else
                    _noteTimer = 2f;
            }
            Fly(dt);
            UpdateHint(dt);
        }

        // ---------------- 날기 ----------------

        private void Fly(float dt)
        {
            var pad = AICore.DronePad;
            var above = new Vector3(pad.x, AICore.DroneTransitY, pad.z);
            switch (_state)
            {
                case State.Docked:
                    Place(pad, Quaternion.identity);
                    break;
                case State.Boarding:
                    _timer -= dt;
                    Place(pad, Quaternion.identity);
                    if (_timer <= 0f)
                    {
                        _state = State.Lift;
                        _timer = 0f;
                        AudioService.TryPlay(l => l.UiOpen);
                    }
                    break;
                case State.Lift:
                {
                    _timer += dt;
                    float t = Mathf.SmoothStep(0f, 1f, _timer / AICore.DroneLiftTime);
                    Place(Vector3.Lerp(pad, above, t) + Bob(t), _yaw);
                    if (_timer >= AICore.DroneLiftTime)
                    {
                        _state = State.Cruise;
                        _s = _v = 0f;
                    }
                    break;
                }
                case State.Cruise:
                {
                    float length = AICore.DroneRouteLength;
                    float left = length - _s;
                    _v = Mathf.Min(AICore.DroneCruise, _v + AICore.DroneAccel * dt, Mathf.Sqrt(2f * AICore.DroneAccel * Mathf.Max(0f, left)) + 0.05f);
                    _s = Mathf.Min(length, _s + _v * dt);
                    AICore.DroneRoutePose(_s, out var at, out var dir);
                    var want = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0f, 90f, 0f);   // 드론 −x (앉은 사람 앞) = 나아가는 쪽
                    _yaw = Quaternion.Slerp(_yaw, want, 1f - Mathf.Exp(-dt * 2.5f));
                    float tilt = TiltDeg * Mathf.Clamp01(_v / AICore.DroneCruise);
                    Place(at + Bob(1f), _yaw * Quaternion.AngleAxis(tilt, Vector3.forward));   // 앞 (−x)으로 살짝 숙임 (+z 축으로 + = +x가 들림)
                    if (_s >= length - 0.01f)
                    {
                        _state = State.Land;
                        _timer = 0f;
                    }
                    break;
                }
                case State.Land:
                {
                    _timer += dt;
                    float t = Mathf.SmoothStep(0f, 1f, _timer / AICore.DroneLandTime);
                    _yaw = Quaternion.Slerp(_yaw, Quaternion.identity, 1f - Mathf.Exp(-dt * 3f));
                    Place(Vector3.Lerp(above, pad, t) + Bob(1f - t), t > 0.98f ? Quaternion.identity : _yaw);
                    if (_timer >= AICore.DroneLandTime)
                    {
                        _state = State.Docked;
                        _yaw = Quaternion.identity;
                        UpdateColliders();
                        AudioService.TryPlay(l => l.SupplyArrive);
                    }
                    break;
                }
            }
        }

        private static Vector3 Bob(float amount) => Vector3.up * (BobHeight * amount * Mathf.Sin(Time.unscaledTime * 1.7f));

        private void Place(Vector3 at, Quaternion rot)
        {
            _drone.localPosition = at;
            _drone.localRotation = rot;
        }

        private void UpdateColliders()
        {
            if (_seatColliders == null)
                return;
            for (int k = 0; k < _seatColliders.Length; k++)
                if (_seatColliders[k] != null)
                    _seatColliders[k].enabled = _seat < 0 && _state == State.Docked;
        }

        // ---------------- 플레이어 ----------------

        private void Sit(int seat)
        {
            if (_seat >= 0 || _player == null || _state != State.Docked)
                return;
            _seat = seat;
            _sitFrame = Time.frameCount;
            _anchor.localPosition = AICore.DroneFeet(seat);
            _anchor.localRotation = Quaternion.Euler(0f, -90f, 0f);   // 앉은 사람 앞 = 드론 −x (서쪽 트임)
            _player.RideAt(_anchor, _anchor.position, AICore.DroneSeatedEye, _anchor.eulerAngles.y, AICore.DroneSeatedPitch);   // 처음 시선 = 아래로 (수평이면 먼 벽 하늘 화면만 보였음)
            _state = State.Boarding;
            _timer = AICore.DroneBoardDelay;
            UpdateColliders();
            AudioService.TryPlay(l => l.UiOpen);
        }

        private void StandUp()
        {
            float yaw = transform.eulerAngles.y + 270f;   // 서쪽 (미로 · 정자 쪽)을 보고 착륙장에 섬
            _player.RideAt(transform, transform.TransformPoint(AICore.DronePad + AICore.DroneExit), FirstPersonController.EyeHeight, yaw);
            _player.Dismount();
            _state = State.Docked;
            ClearRide();
            AudioService.TryPlay(l => l.UiClose);
        }

        private void ReleasePlayer()
        {
            if (_seat < 0 || _player == null)
            {
                _seat = -1;
                return;
            }
            _player.Teleport(transform.TransformPoint(AICore.DronePad + AICore.DroneExit), transform.eulerAngles.y + 270f);
            ClearRide();
        }

        private void ClearRide()
        {
            _seat = -1;
            if (_state == State.Boarding)
                _state = State.Docked;
            UpdateColliders();
        }

        // ---------------- 안내 줄 ----------------

        private void UpdateHint(float dt)
        {
            string text = null;
            if (_seat >= 0)
            {
                if (_noteTimer > 0f)
                    _noteTimer -= dt;
                string key = KeyBindings.Label(GameAction.Interact);
                if (_state == State.Docked)
                    text = $"착륙장   ·   {key}  내리기";
                else if (_state == State.Boarding)
                    text = $"정원 드론 · 곧 떠요   ·   {key}  내리기";
                else if (_noteTimer > 0f)
                    text = $"착륙장에 내려앉으면 {key}로 내림";
                else if (_state == State.Lift)
                    text = "정원 드론 · 떠오르는 중";
                else if (_state == State.Land)
                    text = "정원 드론 · 착륙장에 내려앉는 중";
                else
                    text = "정원 드론 · " + AICore.DroneSection(_s);
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
            var go = new GameObject("DroneRideHint", typeof(RectTransform));
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
            PlaceRect(back.rectTransform, new Vector2(660f, 52f));
            back.color = new Color(0.03f, 0.05f, 0.1f, 0.62f);
            back.raycastTarget = false;
            _hint = new GameObject("Hint", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            _hint.transform.SetParent(go.transform, false);
            PlaceRect(_hint.rectTransform, new Vector2(640f, 44f));
            if (_font != null)
                _hint.font = _font;
            _hint.fontSize = 26f;
            _hint.color = new Color(0.92f, 0.96f, 1f);
            _hint.alignment = TextAlignmentOptions.Center;
            _hint.raycastTarget = false;
            _hint.textWrappingMode = TextWrappingModes.NoWrap;
        }

        private static void PlaceRect(RectTransform rt, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, 64f);
            rt.sizeDelta = size;
        }
    }
}
