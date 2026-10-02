using UnityEngine;
using UnityEngine.InputSystem;

namespace SpaceStation.Core
{
    /// <summary>
    /// 궤도 카메라 입력. 계산은 <see cref="OrbitCameraRig"/>이 담당한다.
    /// 휠 드래그: 회전 / Shift+휠 드래그: 화면 평행 이동 / 휠: 줌
    /// WASD: 수평 이동 / Space: 위, Ctrl: 아래 / Q·E: 좌우 회전
    /// </summary>
    public sealed class OrbitCameraController : MonoBehaviour
    {
        [Header("Initial")]
        [SerializeField] private Vector3 _focus = Vector3.zero;
        [SerializeField] private float _yaw = 45f;
        [SerializeField] private float _pitch = 30f;
        [SerializeField] private float _distance = 12f;

        [Header("Limits")]
        [SerializeField] private float _minDistance = 3f;
        [SerializeField] private float _maxDistance = 80f;
        [SerializeField] private float _minPitch = -85f;
        [SerializeField] private float _maxPitch = 85f;

        [Header("Speed")]
        [SerializeField] private float _orbitDegreesPerPixel = 0.25f;
        [SerializeField] private float _keyOrbitDegreesPerSecond = 90f;
        [SerializeField] private float _panScreenPerPixel = 0.0015f;
        [SerializeField] private float _moveUnitsPerSecond = 8f;
        [SerializeField] private float _zoomPerNotch = 1f;

        private OrbitCameraRig _rig;

        public OrbitCameraRig Rig => _rig;

        private void Awake()
        {
            _rig = new OrbitCameraRig(_focus, _yaw, _pitch, _distance)
            {
                MinDistance = _minDistance,
                MaxDistance = _maxDistance,
                MinPitch = _minPitch,
                MaxPitch = _maxPitch,
            };
            Apply();
        }

        private void Update()
        {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse == null || keyboard == null || InputGate.Blocked)
                return;

            bool shift = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            float orbit = _orbitDegreesPerPixel * Settings.GameSettings.OrbitSensitivity; // 5-10 감도

            if (mouse.middleButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                if (shift)
                    _rig.PanScreen(-delta.x * _panScreenPerPixel, -delta.y * _panScreenPerPixel);
                else
                    _rig.Orbit(delta.x * orbit, -delta.y * orbit);
            }

            // Input System 스크롤은 한 칸당 약 120 (Windows 기준)
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0f)
                _rig.Zoom(Mathf.Sign(scroll) * _zoomPerNotch * Settings.GameSettings.ZoomSensitivity);

            float dt = Time.unscaledDeltaTime; // 시뮬레이션 배속/일시정지와 무관하게 카메라는 움직여야 함
            float right = Axis(keyboard.dKey, keyboard.aKey);
            float forward = Axis(keyboard.wKey, keyboard.sKey);
            float up = (keyboard.spaceKey.isPressed ? 1f : 0f)
                - (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed ? 1f : 0f);
            float yawKey = Axis(keyboard.eKey, keyboard.qKey);

            if (right != 0f || forward != 0f)
                _rig.PanHorizontal(right * _moveUnitsPerSecond * dt, forward * _moveUnitsPerSecond * dt);
            if (up != 0f)
                _rig.MoveVertical(up * _moveUnitsPerSecond * dt);
            if (yawKey != 0f)
                _rig.Orbit(-yawKey * _keyOrbitDegreesPerSecond * Settings.GameSettings.OrbitSensitivity * dt, 0f);

            Apply();
        }

        /// <summary>세이브 복원: 저장 당시 보던 위치로.</summary>
        public void Restore(Vector3 focus, float yaw, float pitch, float distance)
        {
            _rig.Set(focus, yaw, pitch, distance);
            Apply();
        }

        private void Apply()
        {
            transform.SetPositionAndRotation(_rig.Position, _rig.Rotation);
        }

        private static float Axis(UnityEngine.InputSystem.Controls.KeyControl positive, UnityEngine.InputSystem.Controls.KeyControl negative)
        {
            return (positive.isPressed ? 1f : 0f) - (negative.isPressed ? 1f : 0f);
        }
    }
}
