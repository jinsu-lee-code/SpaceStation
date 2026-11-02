using SpaceStation.Audio;
using SpaceStation.Building;
using SpaceStation.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 2단계 대형 토러스 "은하철도 링"의 밤 열차 (사용자 2026-11-02 직접 체험 "기차 탑승 관람"): 고가 선로를 돌며 역 넷에 차례로 섬.
    /// 역에 서면 스크린 도어 · 열차 문이 함께 열림 (<see cref="_dwell"/>초), 다 닫히면 출발해 다음 역까지 <see cref="_travel"/>초. 문 자리에 플레이어가 있으면 닫지 않음.
    /// 문이 닫힐 때 객차 안에 선 플레이어는 함께 실려 감 (걷기 멈춤 · 둘러보기 됨, <see cref="FirstPersonController.Ride"/>), 역에서 문이 열리면 다시 걸음.
    /// 긴 의자 (사용자 2026-11-02 "의자 앉기도 넣어줘"): 보고 F = 앉음 (허브 쪽을 보고 눈높이가 낮아짐, 역에 서도 앉은 채), 다시 F = 일어남.
    /// 승강장 이름판 = 역 이름 + 다음 열차까지 시간 (색이 아니라 글로 — 색약). 위치는 모두 템플릿 루트(이 물체) 기준, 시간은 실제 시간 (엘리베이터와 같음).
    /// 쉬는 자세 = 0번 역 (남쪽, 각 270), 앞 = 각이 줄어드는 쪽 (위에서 보아 시계 방향 — 외형 열차와 같음). 템플릿 빌더가 configure에서 붙임.
    /// </summary>
    [DefaultExecutionOrder(-320)] // InteriorMode(-300)보다 먼저: 앉은 채 누른 F를 여기서 먼저 씀
    public sealed class InteriorTrain : MonoBehaviour
    {
        private enum Phase { Opening, Open, Closing, Moving }

        private const float RestAzimuth = 270f;
        private const float DoorZoneHalf = 0.97f;   // 문 앞 감지 (가운데에서 선로 방향 m)
        private const float SignInterval = 0.5f;

        [Tooltip("허브 축 — 열차 조각이 이 아래에서 돎")]
        [SerializeField] private Transform _pivot;
        [SerializeField] private Transform[] _doorsLeft;
        [SerializeField] private Transform[] _doorsRight;
        [Tooltip("스크린 도어 (역 × 객차 수)")]
        [SerializeField] private Transform[] _gatesLeft;
        [SerializeField] private Transform[] _gatesRight;
        [SerializeField] private Transform[] _seats;
        [SerializeField] private Transform _stack;
        [SerializeField] private Material _spark;
        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private string[] _stationNames;
        [Tooltip("역 가운데 ↔ 객차 문 가운데 (도)")]
        [SerializeField] private float _doorAngle;
        [Tooltip("역 가운데 ↔ 객차 끝 (도)")]
        [SerializeField] private float _carAngle;
        [Tooltip("객차 문 가운데 ↔ 의자 끝 (도)")]
        [SerializeField] private float _seatHalfAngle;
        [SerializeField] private float _innerRadius;
        [SerializeField] private float _outerRadius;
        [SerializeField] private float _seatRadius;
        [SerializeField] private float _aisleRadius;
        [Tooltip("객차 바닥 높이 (템플릿 기준)")]
        [SerializeField] private float _floorY;
        [Tooltip("이름판 자리: 허브에서 거리 · 높이")]
        [SerializeField] private Vector2 _sign;
        [SerializeField] private float _doorTravel = 0.72f;
        [SerializeField] private float _doorSeconds = 0.9f;
        [SerializeField] private float _dwell = 8f;
        [SerializeField] private float _travel = 6.5f;
        [Tooltip("앉았을 때 눈높이 (객차 바닥에서 m)")]
        [SerializeField] private float _seatedEye = 1.18f;

        private Phase _phase = Phase.Opening;
        private int _station;       // 서 있는 역 · 달리는 중이면 다음 역
        private float _open;
        private float _timer;
        private float _fromYaw;
        private bool _riding;
        private bool _seated;
        private int _standFrame = -1;
        private float _signTimer;
        private Vector3 _hub;
        private Vector3[] _doorLeftBase, _doorRightBase, _gateLeftBase, _gateRightBase;
        private Vector3[] _doorAxis, _gateAxis;
        private Collider[] _seatColliders;
        private FirstPersonController _player;
        private ParticleSystem _smoke;
        private TextMeshPro[] _signs;
        private Canvas _canvas;
        private TextMeshProUGUI _hint;
        private string _hintShown;

        public int Station => _station;
        public bool Moving => _phase == Phase.Moving;
        public bool Seated => _seated;
        public bool Riding => _riding;
        /// <summary>문 열림 정도 (0 ~ 1).</summary>
        public float DoorOpen => _open;

        public void Configure(Transform pivot, Transform[] doorsLeft, Transform[] doorsRight, Transform[] gatesLeft, Transform[] gatesRight, Transform[] seats, Transform stack, Material spark, TMP_FontAsset font,
            string[] stationNames, float doorAngle, float carAngle, float seatHalfAngle, float innerRadius, float outerRadius, float seatRadius, float aisleRadius, float floorY, Vector2 sign)
        {
            _pivot = pivot;
            _doorsLeft = doorsLeft;
            _doorsRight = doorsRight;
            _gatesLeft = gatesLeft;
            _gatesRight = gatesRight;
            _seats = seats;
            _stack = stack;
            _spark = spark;
            _font = font;
            _stationNames = stationNames;
            _doorAngle = doorAngle;
            _carAngle = carAngle;
            _seatHalfAngle = seatHalfAngle;
            _innerRadius = innerRadius;
            _outerRadius = outerRadius;
            _seatRadius = seatRadius;
            _aisleRadius = aisleRadius;
            _floorY = floorY;
            _sign = sign;
        }

        private void Awake()
        {
            if (_pivot == null)
                return;
            _hub = _pivot.localPosition;
            int cars = _doorsLeft.Length;
            _doorLeftBase = new Vector3[cars];
            _doorRightBase = new Vector3[cars];
            _doorAxis = new Vector3[cars];
            for (int k = 0; k < cars; k++)
            {
                _doorLeftBase[k] = _doorsLeft[k].localPosition;
                _doorRightBase[k] = _doorsRight[k].localPosition;
                _doorAxis[k] = Tangent(RestAzimuth + DoorOffset(k));
            }
            int gates = _gatesLeft.Length;
            _gateLeftBase = new Vector3[gates];
            _gateRightBase = new Vector3[gates];
            _gateAxis = new Vector3[gates];
            for (int i = 0; i < gates; i++)
            {
                _gateLeftBase[i] = _gatesLeft[i].localPosition;
                _gateRightBase[i] = _gatesRight[i].localPosition;
                int station = i / cars;
                _gateAxis[i] = Tangent(RestAzimuth + DoorOffset(i % cars) - station * 90f);
            }
            _seatColliders = new Collider[_seats.Length];
            for (int k = 0; k < _seats.Length; k++)
            {
                _seatColliders[k] = _seats[k].GetComponent<Collider>();
                var use = _seats[k].gameObject.AddComponent<InteriorInteractable>();
                use.Tap = true;
                use.Prompt = () => "앉기";
                use.Completed = Sit;
            }
            if (_stack != null && _spark != null)
            {
                _smoke = SparkEmitterFx.Build(_stack, SparkEmitterFx.Kind.StarSmoke, _spark, null);
                SetSmoke(false);
            }
            BuildSigns();
            _phase = Phase.Opening;
            _open = 0f;
        }

        private void OnDisable()
        {
            // 내부에서 나가거나 방이 꺼지면 열차는 다음 역에 바로 세우고 실려 가던 플레이어를 내려놓음
            if (_phase == Phase.Moving)
            {
                _fromYaw = _station * 90f;
                if (_pivot != null)
                    _pivot.localRotation = Quaternion.Euler(0f, _fromYaw, 0f);
                _phase = Phase.Opening;
                _open = 0f;
            }
            ReleasePlayer();
            ShowHint(null);
        }

        private void OnDestroy()
        {
            if (_canvas != null)
                Destroy(_canvas.gameObject);
        }

        private static Vector3 Tangent(float azimuth)
        {
            float a = azimuth * Mathf.Deg2Rad;
            return new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
        }

        private float DoorOffset(int car) => car == 0 ? -_doorAngle : _doorAngle;

        private void Update()
        {
            if (_pivot == null)
                return;
            float dt = Time.unscaledDeltaTime;
            if (_player == null)
                _player = FindFirstObjectByType<FirstPersonController>();
            if (_seated && KeyBindings.WasPressed(GameAction.Interact))
                StandUp();
            switch (_phase)
            {
                case Phase.Opening:
                    _open = Mathf.MoveTowards(_open, 1f, dt / _doorSeconds);
                    if (_open >= 1f)
                    {
                        _phase = Phase.Open;
                        _timer = _dwell;
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
                        Depart();
                    break;
                case Phase.Moving:
                    _timer += dt / _travel;
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_timer));
                    _pivot.localRotation = Quaternion.Euler(0f, _fromYaw + 90f * k, 0f);
                    if (_timer >= 1f)
                        Arrive();
                    break;
            }
            ApplyDoors();
            UpdateSigns(dt);
            UpdateHint();
        }

        private void Depart()
        {
            _fromYaw = _station * 90f;
            _station = (_station + 1) % StationCount;
            _phase = Phase.Moving;
            _timer = 0f;
            if (_player != null && !_riding && InCar(_player.transform.position))
            {
                _player.Ride(_pivot);
                _riding = true;
            }
            SetSmoke(true);
        }

        private void Arrive()
        {
            _pivot.localRotation = Quaternion.Euler(0f, _station * 90f, 0f);
            _phase = Phase.Opening;
            _open = 0f;
            SetSmoke(false);
            if (_riding && !_seated)
            {
                _player.Dismount();
                _riding = false;
            }
            PlayNear(l => l.SupplyArrive);
        }

        private int StationCount => _stationNames != null && _stationNames.Length > 0 ? _stationNames.Length : 4;

        private void ApplyDoors()
        {
            float e = Mathf.SmoothStep(0f, 1f, _open) * _doorTravel;
            for (int k = 0; k < _doorsLeft.Length; k++)
            {
                _doorsLeft[k].localPosition = _doorLeftBase[k] - _doorAxis[k] * e;
                _doorsRight[k].localPosition = _doorRightBase[k] + _doorAxis[k] * e;
            }
            int cars = _doorsLeft.Length;
            for (int i = 0; i < _gatesLeft.Length; i++)
            {
                float g = !Moving && i / cars == _station ? e : 0f;
                _gatesLeft[i].localPosition = _gateLeftBase[i] - _gateAxis[i] * g;
                _gatesRight[i].localPosition = _gateRightBase[i] + _gateAxis[i] * g;
            }
        }

        private void SetSmoke(bool on)
        {
            if (_smoke == null)
                return;
            var emission = _smoke.emission;
            emission.enabled = on;
        }

        // ---------------- 플레이어 ----------------

        /// <summary>객차 기준 좌표: 쉬는 자세의 (반지름, 쉴 때 방향에서 각, 객차 바닥에서 높이).</summary>
        private void TrainLocal(Vector3 world, out float radius, out float angle, out float height)
        {
            var p = _pivot.InverseTransformPoint(world);
            radius = new Vector2(p.x, p.z).magnitude;
            angle = Mathf.DeltaAngle(RestAzimuth, Mathf.Atan2(p.z, p.x) * Mathf.Rad2Deg);
            height = p.y - _floorY;
        }

        /// <summary>플레이어 발이 객차 안인지.</summary>
        public bool InCar(Vector3 worldFeet)
        {
            TrainLocal(worldFeet, out float r, out float a, out float h);
            return Mathf.Abs(a) <= _carAngle && r >= _innerRadius - 0.02f && r <= _outerRadius && h > -0.4f && h < 1.2f;
        }

        /// <summary>플레이어가 지금 역의 문 앞 (승강장 쪽 · 객차 쪽) — 문을 닫지 않음.</summary>
        private bool InDoorZone()
        {
            if (_player == null || _riding)
                return false;
            TrainLocal(_player.transform.position, out float r, out float a, out float h);
            if (h < -0.4f || h > 1.2f || r < _innerRadius - 0.52f || r > _innerRadius + 0.45f)
                return false;
            float halfAngle = DoorZoneHalf / r * Mathf.Rad2Deg;
            for (int k = 0; k < _doorsLeft.Length; k++)
            {
                if (Mathf.Abs(Mathf.DeltaAngle(a, DoorOffset(k))) < halfAngle)
                    return true;
            }
            return false;
        }

        private void Sit()
        {
            if (_seated || _player == null || Time.frameCount == _standFrame)
                return; // 일어나기와 같은 프레임의 F (InteriorMode가 뒤에서 다시 부름)
            TrainLocal(_player.transform.position, out _, out float a, out _);
            float car = a < 0f ? -_doorAngle : _doorAngle;
            float seat = Mathf.Clamp(a, car - _seatHalfAngle, car + _seatHalfAngle);
            var feet = _pivot.TransformPoint(PointAt(_seatRadius, seat));
            var inward = _pivot.TransformDirection(-Radial(seat));
            float yaw = Mathf.Atan2(inward.x, inward.z) * Mathf.Rad2Deg;
            _player.RideAt(_pivot, feet, _seatedEye, yaw);
            _riding = true;
            _seated = true;
            SetSeatColliders(false);
            AudioService.TryPlay(l => l.UiOpen);
        }

        private void StandUp()
        {
            _standFrame = Time.frameCount;
            _seated = false;
            SetSeatColliders(true);
            TrainLocal(_player.transform.position, out _, out float a, out _);
            _player.RideAt(_pivot, _pivot.TransformPoint(PointAt(_aisleRadius, a)), FirstPersonController.EyeHeight);
            if (!Moving)
            {
                _player.Dismount();
                _riding = false;
            }
            AudioService.TryPlay(l => l.UiClose);
        }

        private void ReleasePlayer()
        {
            if (_seated)
            {
                _seated = false;
                SetSeatColliders(true);
                if (_player != null)
                    _player.RideAt(_pivot, _pivot.TransformPoint(PointAt(_aisleRadius, 0f)), FirstPersonController.EyeHeight);
            }
            if (_riding && _player != null)
                _player.Dismount();
            _riding = false;
        }

        private void SetSeatColliders(bool on)
        {
            foreach (var c in _seatColliders)
            {
                if (c != null)
                    c.enabled = on;
            }
        }

        private Vector3 Radial(float angle)
        {
            float a = (RestAzimuth + angle) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        }

        private Vector3 PointAt(float radius, float angle) => Radial(angle) * radius + Vector3.up * _floorY;

        private void PlayNear(System.Func<SoundLibrary, SoundCue> cue)
        {
            if (_player == null)
                return;
            var local = transform.InverseTransformPoint(_player.transform.position) - _hub;
            var stop = Quaternion.Euler(0f, _station * 90f, 0f) * new Vector3(0f, 0f, -_sign.x);
            if (_riding || (new Vector2(local.x - stop.x, local.z - stop.z).magnitude < 9f && Mathf.Abs(local.y - _floorY) < 4f))
                AudioService.TryPlay(cue);
        }

        // ---------------- 이름판 · 안내 ----------------

        private string StationName(int station) => _stationNames != null && station < _stationNames.Length ? _stationNames[station] : "";

        private void BuildSigns()
        {
            if (_font == null || _stationNames == null)
                return;
            _signs = new TextMeshPro[_stationNames.Length];
            for (int q = 0; q < _signs.Length; q++)
            {
                var go = new GameObject("StationSign" + q);
                go.transform.SetParent(transform, false);
                var turn = Quaternion.Euler(0f, q * 90f, 0f);
                go.transform.localPosition = _hub + turn * new Vector3(0f, _sign.y, -_sign.x);
                go.transform.localRotation = turn * Quaternion.Euler(0f, 180f, 0f);
                var text = go.AddComponent<TextMeshPro>();
                text.font = _font;
                text.fontSize = 2.4f;
                text.color = new Color(0.92f, 0.96f, 1f);
                text.alignment = TextAlignmentOptions.Center;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.rectTransform.sizeDelta = new Vector2(2.6f, 0.6f);
                _signs[q] = text;
            }
        }

        private void UpdateSigns(float dt)
        {
            if (_signs == null)
                return;
            _signTimer -= dt;
            if (_signTimer > 0f)
                return;
            _signTimer = SignInterval;
            for (int q = 0; q < _signs.Length; q++)
            {
                string status;
                if (!Moving && q == _station)
                    status = _phase == Phase.Closing ? "문 닫힘 · 곧 출발" : "열차 도착 · 문 열림";
                else
                    status = $"다음 열차 약 {Mathf.CeilToInt(SecondsUntil(q))}초";
                _signs[q].SetText($"{StationName(q)}역\n<size=62%>{status}</size>");
            }
        }

        /// <summary>station 역에 다음 열차가 설 때까지 (초).</summary>
        public float SecondsUntil(int station)
        {
            float leg = _travel + _dwell + _doorSeconds * 2f;
            if (Moving)
                return (1f - Mathf.Clamp01(_timer)) * _travel + ((station - _station + StationCount) % StationCount) * leg;
            float stay = _phase switch
            {
                Phase.Opening => (1f - _open) * _doorSeconds + _dwell + _doorSeconds,
                Phase.Open => Mathf.Max(0f, _timer) + _doorSeconds,
                _ => _open * _doorSeconds,
            };
            int hops = (station - _station - 1 + StationCount * 2) % StationCount;
            return stay + _travel + hops * leg;
        }

        private void UpdateHint()
        {
            string text = null;
            bool inside = _riding || _seated || (_player != null && InCar(_player.transform.position));
            if (inside)
            {
                string where = Moving ? $"다음 역  {StationName(_station)}" : $"{StationName(_station)}역  정차 중";
                text = _seated ? $"{where}   ·   {KeyBindings.Label(GameAction.Interact)}  일어나기" : where;
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
            var go = new GameObject("TrainHint", typeof(RectTransform));
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
