using SpaceStation.Audio;
using SpaceStation.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 2단계 저장 단지 "별빛 회전목마 창고"의 회전목마 (MODELING 7-10 직접 체험 "회전목마 탑승", 값 = <see cref="StorageCarousel"/>).
    /// 판이 <see cref="_dwell"/>초 서 있다가 <see cref="_turn"/>° (두 바퀴 + 120°)를 천천히 돌고 섬 (가속 · 감속, 빠를 때 한 바퀴 20초). 캡슐은 기둥을 따라 오르내리고, 120°마다 서므로 설 때는 늘 쉬는 높이.
    /// 선 동안만 바깥 줄 캡슐 안장을 보고 F = 탐 (기둥을 앞에 두고 앉음), 다시 F = 내림 (돌 때는 멈추면 내림). 판 위에 서 있다가 출발하면 함께 실려 감 (걷기 멈춤 · 둘러보기 됨), 서면 다시 걸음.
    /// 도는 동안 울타리 트임 셋을 보이지 않는 벽으로 막음 (트임에 사람이 있으면 출발을 기다림). 집게 팔 둘은 도는 동안 캡슐 쪽으로 천천히 끄덕임.
    /// 안 줄 캡슐에 탄 주민 = 안장 자리에 생긴 주민을 그 캡슐로 옮겨 태움 (돌아다니지 않음). 위치는 템플릿 루트(이 물체) 기준, 시간은 실제 시간.
    /// </summary>
    [DefaultExecutionOrder(-320)] // InteriorMode(-300)보다 먼저: 앉은 채 누른 F를 여기서 먼저 씀
    public sealed class InteriorCarousel : MonoBehaviour
    {
        private enum Phase { Stopped, Running }

        private const float SignInterval = 0.5f;
        private const float RiderInterval = 0.5f;
        private const float Ramp = 0.15f;          // 가속 · 감속 시간 (전체의 비율)
        private const float ClawDip = 8f;          // 집게 팔이 숙이는 각
        private const float ClawPeriod = 5f;
        private const float SeatedEye = 0.78f;     // 안장 윗면 → 눈

        [Tooltip("판 피벗 (판 윗면 가운데) — 판 · 기둥 · 지붕 조각이 이 아래에서 돎")]
        [SerializeField] private Transform _disk;
        [Tooltip("캡슐 피벗 22 (판 피벗의 자식, StorageCarousel 번호)")]
        [SerializeField] private Transform[] _capsules;
        [Tooltip("바깥 줄 안장 (캡슐마다, 없으면 null)")]
        [SerializeField] private Transform[] _seats;
        [Tooltip("집게 팔 피벗 (벽 받침) · 숙이는 쪽 (z축 각 부호)")]
        [SerializeField] private Transform[] _claws;
        [SerializeField] private float[] _clawSigns;
        [Tooltip("도는 동안 켜는 트임 막이")]
        [SerializeField] private Collider[] _gateWalls;
        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private string _title = "별빛 회전목마";
        [SerializeField] private Vector3 _sign;
        [SerializeField] private float _dwell = 12f;
        [Tooltip("한 번 운행에 도는 각 (120의 배수)")]
        [SerializeField] private float _turn = 840f;
        [Tooltip("가장 빠를 때 (도 / 초)")]
        [SerializeField] private float _speed = 18f;
        [Tooltip("탄 주민 자리 (템플릿 기준, 판 0°) · 캡슐 번호")]
        [SerializeField] private Vector3[] _riderSeats = new Vector3[0];
        [SerializeField] private int[] _riderCapsules = new int[0];

        private Phase _phase = Phase.Stopped;
        private float _timer;
        private float _angle;
        private float _from;
        private float _clawClock;
        private bool _riding;
        private bool _seated;
        private int _ridingCapsule = -1;
        private int _standFrame = -1;
        private float _signTimer, _riderTimer;
        private Quaternion[] _clawRest;
        private Collider[] _seatColliders;
        private FirstPersonController _player;
        private TextMeshPro _signText, _signBack;
        private Canvas _canvas;
        private TextMeshProUGUI _hint;
        private string _hintShown;

        public bool Moving => _phase == Phase.Running;
        public bool Seated => _seated;
        public bool Riding => _riding;
        public float Angle => _angle;
        public int RidingCapsule => _ridingCapsule;

        public void Configure(Transform disk, Transform[] capsules, Transform[] seats, Transform[] claws, float[] clawSigns, Collider[] gateWalls,
            TMP_FontAsset font, Vector3 sign, Vector3[] riderSeats, int[] riderCapsules)
        {
            _disk = disk;
            _capsules = capsules;
            _seats = seats;
            _claws = claws;
            _clawSigns = clawSigns;
            _gateWalls = gateWalls;
            _font = font;
            _sign = sign;
            _riderSeats = riderSeats;
            _riderCapsules = riderCapsules;
        }

        /// <summary>한 번 운행 시간 (초) — 가장 빠를 때 _speed가 되게 (사다리꼴 속도).</summary>
        public float RunSeconds => _turn / (_speed * (1f - Ramp));

        private void Awake()
        {
            if (_disk == null)
                return;
            _seatColliders = new Collider[_seats.Length];
            for (int k = 0; k < _seats.Length; k++)
            {
                if (_seats[k] == null)
                    continue;
                _seatColliders[k] = _seats[k].GetComponent<Collider>();
                int capsule = k;
                var use = _seats[k].gameObject.AddComponent<InteriorInteractable>();
                use.Tap = true;
                use.Prompt = () => "회전목마 타기";
                use.Completed = () => Sit(capsule);
            }
            _clawRest = new Quaternion[_claws.Length];
            for (int k = 0; k < _claws.Length; k++)
                _clawRest[k] = _claws[k].localRotation;
            BuildSign();
            _timer = _dwell;
            UpdateColliders();
            Apply(0f);
        }

        private void OnDisable()
        {
            // 내부에서 나가거나 방이 꺼지면 판은 이번 운행 끝 자리에 바로 세우고 실려 가던 플레이어를 내려놓음
            if (_phase == Phase.Running)
            {
                _angle = _from + _turn;
                Stop();
            }
            ReleasePlayer();
            _clawClock = 0f;
            Apply(0f);
            ShowHint(null);
        }

        private void OnDestroy()
        {
            if (_canvas != null)
                Destroy(_canvas.gameObject);
        }

        private void Update()
        {
            if (_disk == null)
                return;
            float dt = Time.unscaledDeltaTime;
            if (_player == null)
                _player = FindFirstObjectByType<FirstPersonController>();
            if (_seated && KeyBindings.WasPressed(GameAction.Interact))
                StandUp();
            switch (_phase)
            {
                case Phase.Stopped:
                    _timer -= dt;
                    if (_timer <= 0f)
                    {
                        if (InGateZone())
                            _timer = 0.5f;   // 트임에 있으면 기다림
                        else
                            Depart();
                    }
                    break;
                case Phase.Running:
                    _timer += dt / RunSeconds;
                    _angle = _from + _turn * Profile(Mathf.Clamp01(_timer));
                    if (_timer >= 1f)
                    {
                        _angle = _from + _turn;
                        Stop();
                    }
                    break;
            }
            Apply(dt);
            CaptureRiders(dt);
            UpdateSign(dt);
            UpdateHint();
        }

        /// <summary>0 ~ 1 → 0 ~ 1 (사다리꼴 속도: 처음 · 끝 Ramp 동안 고르게 빨라지고 느려짐).</summary>
        public static float Profile(float u)
        {
            float v = 1f / (1f - Ramp);
            if (u < Ramp)
                return v * u * u / (2f * Ramp);
            if (u > 1f - Ramp)
                return 1f - v * (1f - u) * (1f - u) / (2f * Ramp);
            return v * (Ramp * 0.5f + (u - Ramp));
        }

        private void Depart()
        {
            _from = _angle;
            _phase = Phase.Running;
            _timer = 0f;
            if (_player != null && !_riding && OnDisk(_player.transform.position))
            {
                _player.Ride(_disk);
                _riding = true;
            }
            UpdateColliders();
            PlayNear(l => l.UiClose);
        }

        private void Stop()
        {
            _angle = Mathf.Repeat(_angle, 360f);
            _phase = Phase.Stopped;
            _timer = _dwell;
            if (_riding && !_seated && _player != null)
            {
                _player.Dismount();
                _riding = false;
            }
            UpdateColliders();
            PlayNear(l => l.UiOpen);
        }

        private void Apply(float dt)
        {
            _disk.localRotation = StorageCarousel.DiskRotation(_angle);
            for (int i = 0; i < _capsules.Length; i++)
                _capsules[i].localPosition = StorageCarousel.LocalPosition(i, _angle);
            // 집게 팔: 도는 동안 끄덕임, 서면 이번 끄덕임을 마치고 쉼
            float dip = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * _clawClock / ClawPeriod);
            if (_phase == Phase.Running || dip > 0.01f)
                _clawClock += dt;
            else
                _clawClock = 0f;
            dip = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * _clawClock / ClawPeriod);
            for (int k = 0; k < _claws.Length; k++)
                _claws[k].localRotation = _clawRest[k] * Quaternion.Euler(0f, 0f, _clawSigns[k] * ClawDip * dip);
        }

        /// <summary>안장은 선 동안만 (앉은 동안은 끔), 트임 막이는 도는 동안만.</summary>
        private void UpdateColliders()
        {
            bool stopped = _phase == Phase.Stopped;
            for (int k = 0; k < _seatColliders.Length; k++)
            {
                if (_seatColliders[k] != null)
                    _seatColliders[k].enabled = stopped && !_seated;
            }
            foreach (var wall in _gateWalls)
            {
                if (wall != null)
                    wall.enabled = !stopped;
            }
        }

        // ---------------- 플레이어 ----------------

        /// <summary>발이 도는 판 위인지 (판 가장자리 안쪽 · 판 윗면 높이).</summary>
        public bool OnDisk(Vector3 worldFeet)
        {
            var p = transform.InverseTransformPoint(worldFeet) - StorageCarousel.Center;
            return new Vector2(p.x, p.z).sqrMagnitude < StorageCarousel.DiskRadius * StorageCarousel.DiskRadius && p.y > -0.3f && p.y < 0.6f;
        }

        /// <summary>트임 (막이 자리)에 플레이어가 있음 — 출발하지 않음.</summary>
        private bool InGateZone()
        {
            if (_player == null || _riding)
                return false;
            var p = transform.InverseTransformPoint(_player.transform.position) - StorageCarousel.Center;
            float r = new Vector2(p.x, p.z).magnitude;
            if (r < 4.6f || r > 6.0f || p.y < -1f || p.y > 1.5f)
                return false;
            float deg = Mathf.Atan2(p.z, p.x) * Mathf.Rad2Deg;
            foreach (var gap in StorageCarousel.Gaps)
            {
                if (Mathf.Abs(Mathf.DeltaAngle(deg, gap)) < StorageCarousel.GapHalf + 4f)
                    return true;
            }
            return false;
        }

        /// <summary>캡슐 옆 판 위 (안쪽으로 0.75 m, 안장 자리 옆) — 내리는 자리.</summary>
        private Vector3 BesideFeet(int capsule)
        {
            float a = StorageCarousel.Angle(capsule) * Mathf.Deg2Rad;
            float r = StorageCarousel.Radius(capsule) - 0.75f;
            var local = new Vector3(r * Mathf.Cos(a), 0f, r * Mathf.Sin(a)) + StorageCarousel.LocalRotation(capsule) * new Vector3(0f, 0f, StorageCarousel.SaddleBack);
            return _disk.TransformPoint(local);
        }

        private void Sit(int capsule)
        {
            if (_seated || _player == null || _phase != Phase.Stopped || Time.frameCount == _standFrame)
                return; // 일어나기와 같은 프레임의 F (InteriorMode가 뒤에서 다시 부름)
            // 기둥 바로 뒤 가운데에 앉으면 기둥이 시야를 반으로 가름 → 안장 뒤 · 바깥쪽으로 조금, 처음 시선은 바깥 앞 (지나가는 풍경)
            var cap = _capsules[capsule];
            var feet = cap.TransformPoint(new Vector3(0.08f, StorageCarousel.SaddleUp, StorageCarousel.SaddleBack - 0.07f));
            _player.RideAt(cap, feet, SeatedEye, cap.eulerAngles.y + 40f, 8f);
            _riding = true;
            _seated = true;
            _ridingCapsule = capsule;
            UpdateColliders();
            AudioService.TryPlay(l => l.UiOpen);
        }

        private void StandUp()
        {
            if (_phase == Phase.Running)
                return;   // 도는 동안은 앉아 있음 (멈추면 내림)
            _standFrame = Time.frameCount;
            _seated = false;
            _player.RideAt(_disk, BesideFeet(_ridingCapsule), FirstPersonController.EyeHeight);
            _player.Dismount();
            _riding = false;
            _ridingCapsule = -1;
            UpdateColliders();
            AudioService.TryPlay(l => l.UiClose);
        }

        private void ReleasePlayer()
        {
            if (_seated)
            {
                _seated = false;
                if (_player != null)
                    _player.RideAt(_disk, BesideFeet(_ridingCapsule), FirstPersonController.EyeHeight);
            }
            if (_riding && _player != null)
                _player.Dismount();
            _riding = false;
            _ridingCapsule = -1;
        }

        private void PlayNear(System.Func<SoundLibrary, SoundCue> cue)
        {
            if (_player == null)
                return;
            var p = transform.InverseTransformPoint(_player.transform.position) - StorageCarousel.Center;
            if (_riding || p.sqrMagnitude < 144f)
                AudioService.TryPlay(cue);
        }

        // ---------------- 탄 주민 ----------------

        /// <summary>안장 자리 (판 0°)에 생긴 주민을 그 캡슐로 옮겨 태움 (판이 어디 있든 캡슐 기준 자리는 같음).</summary>
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
                if (figure.transform.parent != null && figure.transform.parent.parent == _disk)
                    continue;   // 이미 캡슐에 탐
                var p = transform.InverseTransformPoint(figure.transform.position);
                for (int i = 0; i < _riderSeats.Length; i++)
                {
                    var s = _riderSeats[i];
                    if (new Vector2(p.x - s.x, p.z - s.z).sqrMagnitude > 0.04f || Mathf.Abs(p.y - s.y) > 0.6f)
                        continue;
                    int capsule = _riderCapsules[i];
                    StorageCarousel.Pose(capsule, 0f, out var restPos, out var restRot);
                    var inv = Quaternion.Inverse(restRot);
                    var rot = Quaternion.Inverse(transform.rotation) * figure.transform.rotation;
                    var wander = figure.GetComponent<ResidentWander>();
                    if (wander != null)
                        Destroy(wander);
                    figure.transform.SetParent(_capsules[capsule], false);
                    figure.transform.localPosition = inv * (p - restPos);
                    figure.transform.localRotation = inv * rot;
                    break;
                }
            }
        }

        // ---------------- 간판 · 안내 ----------------

        /// <summary>간판 앞뒤 둘 (광장 · 회전목마 양쪽에서 읽힘): 뒷면을 그리지 않는 재질 사본이라 반대편에서 거울 글자가 겹치지 않음.</summary>
        private void BuildSign()
        {
            if (_font == null)
                return;
            _signText = SignFace("CarouselSign", 0f);
            _signBack = SignFace("CarouselSignBack", 180f);
        }

        private TextMeshPro SignFace(string name, float yaw)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = _sign;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var text = go.AddComponent<TextMeshPro>();
            text.font = _font;
            text.fontSize = 2.2f;
            text.color = new Color(0.92f, 0.96f, 1f);
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.rectTransform.sizeDelta = new Vector2(2.6f, 0.6f);
            text.fontMaterial.SetFloat(ShaderUtilities.ShaderTag_CullMode, (float)UnityEngine.Rendering.CullMode.Back);
            return text;
        }

        /// <summary>다음에 설 때까지 (초) — 서 있으면 0.</summary>
        public float SecondsUntilStop()
        {
            if (_phase != Phase.Running)
                return 0f;
            return (1f - Mathf.Clamp01(_timer)) * RunSeconds;
        }

        private void UpdateSign(float dt)
        {
            if (_signText == null)
                return;
            _signTimer -= dt;
            if (_signTimer > 0f)
                return;
            _signTimer = SignInterval;
            string status = _phase == Phase.Stopped
                ? $"멈춤 · 타세요 (출발까지 {Mathf.CeilToInt(Mathf.Max(0f, _timer))}초)"
                : $"운행 중 · 다음 멈춤 약 {Mathf.CeilToInt(SecondsUntilStop())}초";
            _signText.SetText($"{_title}\n<size=62%>{status}</size>");
            if (_signBack != null)
                _signBack.SetText($"{_title}\n<size=62%>{status}</size>");
        }

        private void UpdateHint()
        {
            string text = null;
            string key = KeyBindings.Label(GameAction.Interact);
            if (_seated)
                text = _phase == Phase.Running ? $"회전목마 · 멈추면 내릴 수 있음 (약 {Mathf.CeilToInt(SecondsUntilStop())}초)" : $"{key}  내리기   ·   곧 다시 출발";
            else if (_riding)
                text = "도는 판 위 · 멈추면 걸을 수 있음";
            else if (_player != null && _phase == Phase.Stopped && OnDisk(_player.transform.position))
                text = "바깥 줄 캡슐 안장을 보고 타기";
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
            var go = new GameObject("CarouselHint", typeof(RectTransform));
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
