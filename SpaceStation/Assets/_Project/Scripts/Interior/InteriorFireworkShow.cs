using System.Collections.Generic;
using SpaceStation.Audio;
using SpaceStation.Building;
using SpaceStation.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 2단계 요새 포대 폭죽놀이 직관 (사용자 2026-11-05 시안 1 "좋아 이대로 유니티로 옮겨줘"): 불꽃은 하늘 화면에 터진다 —
    /// 내부 칸은 천장까지 5m뿐이라 안에 띄우지 않고, 바깥 모델의 <see cref="FireworkFx"/>를 그대로 보여 줌 (하늘 = 실제 바깥 화면, <see cref="InteriorExteriorView"/>가 불꽃은 숨기지 않음).
    /// 바깥 포구 × 8 = 내부 박격포대 자리라, 안에서 보면 내부 대롱 위로 올라가 터지는 것처럼 보임. 쏠 때마다 내부 대롱 넷을 번갈아 포구 불티 (<see cref="InteriorFx.Spark"/>), 터질 때 연병장이 번쩍임.
    /// 관람석: 보고 F = 앉음 (뒤 능보 쪽 하늘을 올려다봄), 다시 F = 앉기 전 자리로 일어남. 쇼 레버: 당기면 24초 동안 연달아 쏨.
    /// 모듈이 꺼지면 쏘지 않음 (평소 불꽃도 <see cref="FireworkFx"/>가 멈춤). 실제 시간 (unscaled).
    /// </summary>
    public sealed class InteriorFireworkShow : InteriorRoomDisplay
    {
        private const float ShowSeconds = 24f;
        private const float ShowGap = 0.75f;
        private const float LeverPulled = -48f;
        private const float BurstDelay = 0.8f;     // 포구 → 터짐 (FireworkFx 오르는 시간)
        private const float ResidentRadius = 0.2f;
        private const float SeatedEye = 0.72f;
        private const float SeatedPitch = -38f;    // 음수 = 위 (뒤 능보 위 불꽃이 화면 가운데쯤)

        [Tooltip("앉는 자리: x, 앉는 면 높이, z, 바라보는 방향 (템플릿 기준)")]
        [SerializeField] private Vector4[] _seats = System.Array.Empty<Vector4>();
        [Tooltip("F로 앉는 면 상자 (템플릿 기준 — 관람석 줄마다 하나)")]
        [SerializeField] private Bounds[] _seatBoxes = System.Array.Empty<Bounds>();
        [Tooltip("박격포대 포구: 바깥 포구 번호 × 대롱 수 순서 (템플릿 기준)")]
        [SerializeField] private Vector3[] _muzzles = System.Array.Empty<Vector3>();
        [SerializeField] private int _tubesPerBattery = 4;
        [SerializeField] private Transform _lever;
        [SerializeField] private Vector3 _flashAt;
        [SerializeField] private Material _spark;

        private static readonly Color[] FlashColors =
        {
            new Color(1f, 0.72f, 0.62f), new Color(1f, 0.88f, 0.56f), new Color(0.75f, 0.88f, 1f), new Color(1f, 1f, 0.92f),
        };

        private FireworkFx _fireworks;
        private FirstPersonController _player;
        private GameObject _seatRoot;
        private Light _flash;
        private readonly List<float> _bursts = new List<float>();
        private readonly Collider[] _overlap = new Collider[8];
        private int[] _nextTube;
        private float _showLeft, _gap, _flashLevel;
        private float _leverAngle;
        private bool _seated;
        private int _standFrame = -1, _aimFrame = -1, _aimSeat = -1;
        private Vector3 _standPoint;
        private Canvas _canvas;
        private TextMeshProUGUI _hint;

        public void Configure(Vector4[] seats, Bounds[] seatBoxes, Vector3[] muzzles, int tubesPerBattery, Transform lever, Vector3 flashAt, Material spark)
        {
            _seats = seats;
            _seatBoxes = seatBoxes;
            _muzzles = muzzles;
            _tubesPerBattery = tubesPerBattery;
            _lever = lever;
            _flashAt = flashAt;
            _spark = spark;
        }

        private void Awake()
        {
            _nextTube = new int[Mathf.Max(1, _muzzles.Length / Mathf.Max(1, _tubesPerBattery))];
            _seatRoot = new GameObject("FireworkSeats");
            _seatRoot.transform.SetParent(transform, false);
            foreach (var b in _seatBoxes)
            {
                var box = _seatRoot.AddComponent<BoxCollider>();
                box.center = b.center;
                box.size = b.size;
            }
            if (_seatBoxes.Length > 0)
            {
                var use = _seatRoot.AddComponent<InteriorInteractable>();
                use.Tap = true;
                use.Prompt = () => "앉아서 불꽃 보기";
                use.Blocked = () => AimSeat() >= 0 && ResidentOn(AimSeat()) ? "주민이 앉아 있는 자리" : null;
                use.Completed = Sit;
            }
            if (_lever != null)
            {
                var use = _lever.GetComponentInChildren<Collider>() != null ? _lever.GetComponentInChildren<Collider>().gameObject.AddComponent<InteriorInteractable>() : null;
                if (use != null)
                {
                    use.Tap = true;
                    use.Prompt = () => "불꽃 쇼 시작";
                    use.Blocked = () => !Active ? "전력이 없어 쏠 수 없음" : _showLeft > 0f ? "불꽃 쇼 중" : _fireworks == null ? "포대 준비 중" : null;
                    use.Completed = StartShow;
                }
            }
            var go = new GameObject("FireworkFlash");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = _flashAt;
            _flash = go.AddComponent<Light>();
            _flash.type = LightType.Point;
            _flash.range = 26f;
            _flash.intensity = 0f;
            _flash.shadows = LightShadows.None;
            _flash.enabled = false;
        }

        private void OnDisable()
        {
            ReleasePlayer();
            if (_fireworks != null)
                _fireworks.Launched -= OnLaunched;
            _fireworks = null;
            _showLeft = 0f;
            _bursts.Clear();
            ShowHint(false);
        }

        private void OnDestroy()
        {
            if (_canvas != null)
                Destroy(_canvas.gameObject);
        }

        protected override void Refresh()
        {
            if (_fireworks != null || Station == null || Module == null || !isActiveAndEnabled)
                return;
            if (Station.TryGetView(Module, out var view) && view != null)
            {
                _fireworks = view.GetComponentInChildren<FireworkFx>(true);
                if (_fireworks != null)
                    _fireworks.Launched += OnLaunched;
            }
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (_player == null)
                _player = FindFirstObjectByType<FirstPersonController>();
            if (_seated && KeyBindings.WasPressed(GameAction.Interact))
                StandUp();
            // 쇼: 포구를 번갈아 연달아
            if (_showLeft > 0f)
            {
                _showLeft -= dt;
                _gap -= dt;
                if (_gap <= 0f && _fireworks != null && Active)
                {
                    _gap = ShowGap * Random.Range(0.7f, 1.3f);
                    _fireworks.Launch();
                }
            }
            if (_lever != null)
            {
                float target = _showLeft > 1f ? LeverPulled : 0f;
                _leverAngle = Mathf.MoveTowards(_leverAngle, target, (_showLeft > 1f ? 240f : 40f) * dt);
                _lever.localRotation = Quaternion.Euler(_leverAngle, 0f, 0f);
            }
            // 터질 때 연병장이 번쩍임
            for (int i = _bursts.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime < _bursts[i])
                    continue;
                _bursts.RemoveAt(i);
                _flashLevel = 1f;
                _flash.color = FlashColors[Random.Range(0, FlashColors.Length)];
                AudioService.TryPlay(l => l.MeteorExplode, 0.4f);
            }
            if (_flashLevel > 0f)
            {
                _flashLevel = Mathf.Max(0f, _flashLevel - dt * 1.6f);
                _flash.enabled = _flashLevel > 0f;
                _flash.intensity = 5f * _flashLevel * _flashLevel;
            }
        }

        private void StartShow()
        {
            if (_fireworks == null || !Active || _showLeft > 0f)
                return;
            _showLeft = ShowSeconds;
            _gap = 0f;
            AudioService.TryPlay(l => l.UiOpen);
        }

        private void OnLaunched(int mortar)
        {
            _bursts.Add(Time.unscaledTime + BurstDelay);
            if (_muzzles.Length == 0 || _tubesPerBattery <= 0)
                return;
            int battery = mortar % _nextTube.Length;
            int tube = _nextTube[battery]++ % _tubesPerBattery;
            int index = battery * _tubesPerBattery + tube;
            if (index < _muzzles.Length)
                InteriorFx.Spark(transform, transform.TransformPoint(_muzzles[index]), transform.up, _spark, 14, 24, 3.5f);
        }

        // ---------------- 관람석 ----------------

        private int AimSeat()
        {
            if (_aimFrame == Time.frameCount)
                return _aimSeat;
            _aimFrame = Time.frameCount;
            _aimSeat = -1;
            if (_player == null)
                return -1;
            var eye = _player.Eye;
            if (!Physics.Raycast(eye.position, eye.forward, out var hit, 5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) || hit.collider.gameObject != _seatRoot)
                return -1;
            var local = transform.InverseTransformPoint(hit.point);
            float best = float.MaxValue;
            for (int i = 0; i < _seats.Length; i++)
            {
                var s = _seats[i];
                float d = new Vector2(local.x - s.x, local.z - s.z).sqrMagnitude + 2f * Mathf.Abs(local.y - s.y);
                if (d < best)
                {
                    best = d;
                    _aimSeat = i;
                }
            }
            return _aimSeat;
        }

        private bool ResidentOn(int seat)
        {
            var s = _seats[seat];
            var world = transform.TransformPoint(new Vector3(s.x, s.y + 0.25f, s.z));
            int n = Physics.OverlapSphereNonAlloc(world, ResidentRadius, _overlap, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (_overlap[i].GetComponentInParent<ResidentFigure>() != null)
                    return true;
            }
            return false;
        }

        private void Sit()
        {
            int seat = AimSeat();
            if (_seated || _player == null || seat < 0 || Time.frameCount == _standFrame || ResidentOn(seat))
                return; // 일어나기와 같은 프레임의 F (InteriorMode가 뒤에서 다시 부름)
            var s = _seats[seat];
            _standPoint = _player.transform.position;
            _player.RideAt(transform, transform.TransformPoint(new Vector3(s.x, s.y, s.z)), SeatedEye, s.w + transform.eulerAngles.y, SeatedPitch);
            _seated = true;
            _seatRoot.SetActive(false);
            ShowHint(true);
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
            if (_seatRoot != null)
                _seatRoot.SetActive(true);
            ShowHint(false);
            if (_player != null)
            {
                _player.RideAt(transform, _standPoint, FirstPersonController.EyeHeight, null, 0f);
                _player.Dismount();
            }
        }

        // ---------------- 안내 ----------------

        private void ShowHint(bool on)
        {
            if (!on)
            {
                if (_canvas != null)
                    _canvas.gameObject.SetActive(false);
                return;
            }
            if (_canvas == null)
            {
                var go = new GameObject("FireworkHint", typeof(RectTransform));
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
                if (Font != null)
                    _hint.font = Font;
                _hint.fontSize = 26f;
                _hint.color = new Color(0.92f, 0.96f, 1f);
                _hint.alignment = TextAlignmentOptions.Center;
                _hint.raycastTarget = false;
                _hint.textWrappingMode = TextWrappingModes.NoWrap;
            }
            _canvas.gameObject.SetActive(true);
            _hint.SetText($"마우스  둘러보기   ·   {KeyBindings.Label(GameAction.Interact)}  일어나기");
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
