using SpaceStation.Audio;
using SpaceStation.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 개방형 정원의 벤치 앉기 (방사선 차폐벽 "별자리 병풍 정원", 사용자 2026-11-05 시안 1): 보고 F = 앉음 (앉은 채 둘러보기), 다시 F = 앉기 전 자리로 일어남.
    /// 좌표 = 템플릿 기준. 주민이 앉은 자리는 비었을 때만. 실제 시간 (unscaled).
    /// </summary>
    [DefaultExecutionOrder(-320)] // InteriorMode(-300)보다 먼저: 앉은 채 누른 F를 여기서 먼저 씀 (늦게 돌면 앉자마자 같은 프레임에 일어남)
    public sealed class InteriorBenchSeats : InteriorRoomDisplay
    {
        private const float ResidentRadius = 0.2f;
        private const float SeatedEye = 0.72f;

        [Tooltip("앉는 자리: x, 앉는 면 높이, z, 바라보는 방향 (템플릿 기준)")]
        [SerializeField] private Vector4[] _seats = System.Array.Empty<Vector4>();
        [Tooltip("F로 앉는 면 상자 (템플릿 기준)")]
        [SerializeField] private Bounds[] _seatBoxes = System.Array.Empty<Bounds>();
        [Tooltip("앉았을 때 위아래 시선 (음수 = 위)")]
        [SerializeField] private float _seatedPitch = -6f;
        [Tooltip("앉는 면 → 눈 높이 (m)")]
        [SerializeField] private float _seatedEye = SeatedEye;
        [Tooltip("보고 F 안내 (선베드 = 눕기)")]
        [SerializeField] private string _prompt = "앉기";

        private FirstPersonController _player;
        private GameObject _seatRoot;
        private readonly Collider[] _overlap = new Collider[8];
        private bool _seated;
        private int _standFrame = -1, _aimFrame = -1, _aimSeat = -1;
        private Vector3 _standPoint;
        private Canvas _canvas;
        private TextMeshProUGUI _hint;

        /// <summary>플레이어가 이 벤치에 앉아 있음.</summary>
        public bool IsSeated => _seated;

        /// <summary>플레이어가 앉음 (드론 쇼 관람석 = 쇼 시작).</summary>
        public event System.Action SatDown;

        public void Configure(Vector4[] seats, Bounds[] seatBoxes, float seatedPitch, float seatedEye = SeatedEye, string prompt = "앉기")
        {
            _seats = seats;
            _seatBoxes = seatBoxes;
            _seatedPitch = seatedPitch;
            _seatedEye = seatedEye;
            _prompt = prompt;
        }

        private void Awake()
        {
            _seatRoot = new GameObject("BenchSeats");
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
                use.Prompt = () => _prompt;
                use.Blocked = () => AimSeat() >= 0 && ResidentOn(AimSeat()) ? "주민이 앉아 있는 자리" : null;
                use.Completed = Sit;
            }
        }

        private void OnDisable()
        {
            ReleasePlayer();
            ShowHint(false);
        }

        private void OnDestroy()
        {
            if (_canvas != null)
                Destroy(_canvas.gameObject);
        }

        protected override void Refresh()
        {
        }

        private void Update()
        {
            if (_player == null)
                _player = FindFirstObjectByType<FirstPersonController>();
            if (_seated && _player != null && !_player.Riding)
                ClearSeat();   // 앉은 채 패드로 빠른 이동 (Teleport가 탈것을 지움)
            if (_seated && KeyBindings.WasPressed(GameAction.Interact))
                StandUp();
        }

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
            _player.RideAt(transform, transform.TransformPoint(new Vector3(s.x, s.y, s.z)), _seatedEye, s.w + transform.eulerAngles.y, _seatedPitch);
            _seated = true;
            _seatRoot.SetActive(false);
            ShowHint(true);
            AudioService.TryPlay(l => l.UiOpen);
            SatDown?.Invoke();
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
            ClearSeat();
            if (_player != null)
            {
                _player.RideAt(transform, _standPoint, FirstPersonController.EyeHeight, null, 0f);
                _player.Dismount();
            }
        }

        private void ClearSeat()
        {
            _seated = false;
            if (_seatRoot != null)
                _seatRoot.SetActive(true);
            ShowHint(false);
        }

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
                var go = new GameObject("BenchHint", typeof(RectTransform));
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
