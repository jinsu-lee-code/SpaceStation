using SpaceStation.Audio;
using SpaceStation.Core;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 전망실 망원경 (진화 1단계 중력 거주구 — 사용자 2026-10-29 "망원경에 상호작용으로 실제 망원경으로 둘러보는 상호작용"):
    /// 머리를 보고 F 한 번 → 망원경 화면 (둥근 시야 · 십자선 · 배율). 마우스 = 돌려 보기 (쉴 때 방향 좌우 ±<see cref="_yawLimit"/>, 위아래 <see cref="_pitchRange"/>),
    /// 휠 = 배율, F · ESC · 오른쪽 클릭 = 그만 보기 (패드 키를 누르면 그만 보고 패드).
    /// 화면은 <see cref="InteriorExteriorView.RenderLookout"/>이 바깥 정거장을 바로 그림 — 전망실 창과 같이 탑 꼭대기 위로 들어 올림 (자기 링 · 꼬리가 내려다보임).
    /// 머리 조각(경통)은 시선을 따라 피벗을 축으로 돎 (위아래는 손잡이에 닿지 않게 <see cref="_headPitchRange"/>까지만). 보는 동안 플레이어는 멈춤.
    /// </summary>
    [DefaultExecutionOrder(-320)] // InteriorMode(-300) · 패드(-310)보다 먼저: 같은 프레임의 F · ESC를 여기서 먼저 씀
    public sealed class InteriorTelescope : MonoBehaviour
    {
        [SerializeField] private Transform _head;
        [Tooltip("머리 회전 중심 (템플릿 기준)")]
        [SerializeField] private Vector3 _pivot;
        [Tooltip("쉴 때 머리 들림 (모델 그대로의 각, 도)")]
        [SerializeField] private float _restTilt = 8f;
        [Tooltip("회전 중심 → 접안경 (m) — 화면 카메라 자리")]
        [SerializeField] private float _eyeBack = 0.5f;
        [SerializeField] private float _yawLimit = 80f;
        [Tooltip("시선 위아래 (도, 위 +)")]
        [SerializeField] private Vector2 _pitchRange = new Vector2(-40f, 30f);
        [Tooltip("경통 모델 위아래 (손잡이에 닿지 않게)")]
        [SerializeField] private Vector2 _headPitchRange = new Vector2(-18f, 30f);
        [Tooltip("세로 시야각 (도) — 작을수록 확대")]
        [SerializeField] private Vector2 _fovRange = new Vector2(6f, 24f);
        [SerializeField] private float _startFov = 15f;
        [Tooltip("처음 들여다볼 때 시선 (도, 위 +) — 쉴 때 머리 들림 그대로면 하늘만 보여 정거장 쪽으로 살짝 내림")]
        [SerializeField] private float _startPitch = -6f;
        [Tooltip("바깥 카메라 들어 올림 (내부 m) — 전망실 창(InteriorLookoutWindow.Lift)과 같게")]
        [SerializeField] private float _lift = 3.8f;
        [SerializeField] private TMP_FontAsset _font;

        private const int TextureSize = 1024;
        private const float ViewSize = 940f; // 둥근 시야 지름 (기준 해상도 1080 세로)

        private Vector3 _basePosition;
        private Quaternion _baseRotation;
        private Collider _collider;
        private float _yaw;
        private float _pitch;
        private float _fov;
        private float _baseFov = 60f;
        private bool _active;
        private int _exitFrame = -1;
        private FirstPersonController _player;
        private InteriorExteriorView _view;
        private RenderTexture _texture;
        private Texture2D _lensTexture;
        private Canvas _canvas;
        private RawImage _viewImage;
        private TextMeshProUGUI _zoom;
        private TextMeshProUGUI _hint;

        public bool Active => _active;

        public void Configure(Transform head, Vector3 pivot, float restTilt, float lift, TMP_FontAsset font)
        {
            _head = head;
            _pivot = pivot;
            _restTilt = restTilt;
            _lift = lift;
            _font = font;
        }

        private void Awake()
        {
            if (_head == null)
                return;
            _basePosition = _head.localPosition;
            _baseRotation = _head.localRotation;
            _collider = _head.GetComponent<Collider>();
            _pitch = _startPitch; // 머리 모델은 처음 볼 때까지 쉴 때 자세, 그 뒤로는 마지막 시선 그대로
            _fov = _startFov;
            var use = _head.gameObject.AddComponent<InteriorInteractable>();
            use.Tap = true;
            use.Prompt = () => "망원경 보기";
            use.Completed = Enter;
        }

        private void OnDisable()
        {
            if (_active)
                Exit(silent: true);
        }

        private void OnDestroy()
        {
            if (_texture != null)
            {
                _texture.Release();
                Destroy(_texture);
            }
            if (_lensTexture != null)
                Destroy(_lensTexture);
        }

        private void Enter()
        {
            if (_active || Time.frameCount == _exitFrame)
                return; // 그만 보기와 같은 프레임의 F (InteriorMode가 뒤에서 다시 부름)
            _player = FindFirstObjectByType<FirstPersonController>();
            _view = FindFirstObjectByType<InteriorExteriorView>();
            if (_player == null || _view == null)
                return;
            var cam = _player.GetComponentInChildren<Camera>();
            _baseFov = cam != null ? cam.fieldOfView : 60f;
            _active = true;
            _player.Frozen = true;
            _view.Suspended = true; // 창 화면은 망원경이 덮는 동안 쉼
            if (_collider != null)
                _collider.enabled = false; // 보는 동안 "F 망원경 보기" 안내가 뜨지 않게
            EnsureOverlay();
            _canvas.gameObject.SetActive(true);
            _hint.SetText($"마우스  둘러보기   ·   휠  확대   ·   {KeyBindings.Label(GameAction.Interact)} / ESC  그만 보기");
            AudioService.TryPlay(l => l.UiOpen);
        }

        private void Exit(bool silent = false)
        {
            _active = false;
            _exitFrame = Time.frameCount;
            if (_player != null)
                _player.Frozen = false;
            if (_view != null)
                _view.Suspended = false;
            if (_collider != null)
                _collider.enabled = true;
            if (_canvas != null)
                _canvas.gameObject.SetActive(false);
            if (!silent)
                AudioService.TryPlay(l => l.UiClose);
        }

        private void Update()
        {
            if (!_active)
                return;
            if (_player == null || _view == null)
            {
                Exit(silent: true);
                return;
            }
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !InputGate.EscapeConsumedThisFrame)
            {
                InputGate.ConsumeEscape(); // 내부 나가기보다 먼저
                Exit();
                return;
            }
            if (KeyBindings.WasPressed(GameAction.Interact) || KeyBindings.WasPressed(GameAction.Pad) || (mouse != null && mouse.rightButton.wasPressedThisFrame))
            {
                Exit();
                return;
            }
            if (mouse == null)
                return;
            // 확대할수록 천천히 (화면에서 같은 거리만큼 움직이게)
            float k = 0.12f * Settings.GameSettings.OrbitSensitivity * (_fov / _baseFov);
            var look = mouse.delta.ReadValue() * k;
            _yaw = Mathf.Clamp(_yaw + look.x, -_yawLimit, _yawLimit);
            _pitch = Mathf.Clamp(_pitch + look.y, _pitchRange.x, _pitchRange.y);
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
                _fov = Mathf.Clamp(_fov * (scroll > 0f ? 0.8f : 1.25f), _fovRange.x, _fovRange.y);
        }

        private void LateUpdate()
        {
            if (!_active || _head == null)
                return;
            ApplyHead();
            var parent = _head.parent;
            var look = Quaternion.Euler(-_pitch, _yaw, 0f);
            var eye = parent.TransformPoint(_pivot + look * new Vector3(0f, 0f, -_eyeBack));
            _view.RenderLookout(_texture, eye, parent.rotation * look, _fov, _lift);
            _zoom.SetText($"×{_baseFov / _fov:0.0}");
        }

        /// <summary>경통: 쉴 때 자세(+z로 _restTilt 들림)에서 지금 시선까지 피벗을 축으로 돌림.</summary>
        private void ApplyHead()
        {
            float headPitch = Mathf.Clamp(_pitch, _headPitchRange.x, _headPitchRange.y);
            var turn = Quaternion.Euler(-headPitch, _yaw, 0f) * Quaternion.Inverse(Quaternion.Euler(-_restTilt, 0f, 0f));
            _head.localRotation = turn * _baseRotation;
            _head.localPosition = _pivot + turn * (_basePosition - _pivot);
        }

        // ---------------- 화면 ----------------

        private void EnsureOverlay()
        {
            if (_texture == null)
            {
                _texture = new RenderTexture(TextureSize, TextureSize, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "TelescopeView" };
                _texture.Create();
            }
            if (_canvas != null)
                return;
            var go = new GameObject("TelescopeOverlay", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 400; // 내부 안내 · 패드 글보다 위
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            var black = NewChild<Image>(go.transform, "Black");
            Stretch(black.rectTransform);
            black.color = Color.black;
            black.raycastTarget = false;

            _viewImage = NewChild<RawImage>(go.transform, "View");
            Center(_viewImage.rectTransform, new Vector2(0f, 20f), new Vector2(ViewSize, ViewSize));
            _viewImage.texture = _texture;
            _viewImage.raycastTarget = false;

            var lens = NewChild<RawImage>(go.transform, "Lens");
            Center(lens.rectTransform, new Vector2(0f, 20f), new Vector2(ViewSize, ViewSize));
            _lensTexture = LensTexture(512);
            lens.texture = _lensTexture;
            lens.raycastTarget = false;

            // 십자선 (가운데는 비움)
            var reticle = new Color(0.85f, 0.95f, 1f, 0.7f);
            foreach (var (pos, size) in new[]
                     {
                         (new Vector2(-38f, 20f), new Vector2(44f, 2f)), (new Vector2(38f, 20f), new Vector2(44f, 2f)),
                         (new Vector2(0f, 58f), new Vector2(2f, 44f)), (new Vector2(0f, -18f), new Vector2(2f, 44f)),
                     })
            {
                var bar = NewChild<Image>(go.transform, "Reticle");
                Center(bar.rectTransform, pos, size);
                bar.color = reticle;
                bar.raycastTarget = false;
            }

            _zoom = NewText(go.transform, "Zoom", 34f, new Color(0.88f, 0.95f, 1f));
            Center(_zoom.rectTransform, new Vector2(ViewSize * 0.36f, 20f - ViewSize * 0.4f), new Vector2(160f, 50f));
            _zoom.alignment = TextAlignmentOptions.Center;
            _hint = NewText(go.transform, "Hint", 26f, new Color(0.8f, 0.84f, 0.9f));
            Center(_hint.rectTransform, new Vector2(0f, -510f), new Vector2(1400f, 44f));
            _hint.alignment = TextAlignmentOptions.Center;
            go.SetActive(false);
        }

        /// <summary>렌즈 테: 둥근 시야 바깥 = 검정, 가장자리 안쪽 = 살짝 어둡게 (렌즈 비네트), 테두리 = 옅은 하늘색 선.</summary>
        private static Texture2D LensTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "TelescopeLens", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            var rim = new Color(0.55f, 0.78f, 0.95f);
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = new Vector2(x + 0.5f - half, y + 0.5f - half).magnitude / half;
                    float dark = Mathf.Max(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.8f, 1f, d)) * 0.55f, Mathf.InverseLerp(0.975f, 0.992f, d));
                    float ring = Mathf.Clamp01(1f - Mathf.Abs(d - 0.982f) / 0.01f);
                    var c = rim * ring;
                    pixels[y * size + x] = new Color(c.r, c.g, c.b, Mathf.Max(dark, ring));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        private static T NewChild<T>(Transform parent, string name) where T : Component
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.AddComponent<T>();
        }

        private TextMeshProUGUI NewText(Transform parent, string name, float size, Color color)
        {
            var text = NewChild<TextMeshProUGUI>(parent, name);
            if (_font != null)
                text.font = _font;
            text.fontSize = size;
            text.color = color;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return text;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void Center(RectTransform rt, Vector2 position, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }
    }
}
