using SpaceStation.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpaceStation.Interior
{
    /// <summary>
    /// Phase 11 내부 방문 1인칭 이동: 이동 키(카메라 앞뒤좌우와 같은 키, 7-5 설정 공유) + 마우스 시점, Shift 달리기, 중력.
    /// 시뮬레이션 일시정지와 무관하게 실제 시간으로 움직인다.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class FirstPersonController : MonoBehaviour
    {
        public const float EyeHeight = 1.6f;

        [SerializeField] private float _walkSpeed = 3.2f;
        [SerializeField] private float _runSpeed = 5.5f;
        [SerializeField] private float _lookDegreesPerPixel = 0.12f;
        [SerializeField] private float _gravity = 15f;

        private CharacterController _body;
        private Transform _eye;
        private float _pitch;
        private float _fallSpeed;

        public Transform Eye => _eye;

        private void Awake()
        {
            _body = GetComponent<CharacterController>();
            _body.height = 1.75f;
            _body.radius = 0.3f;
            _body.center = new Vector3(0f, _body.height * 0.5f, 0f);
            _body.stepOffset = 0.3f;
            _eye = new GameObject("Eye").transform;
            _eye.SetParent(transform, false);
            _eye.localPosition = new Vector3(0f, EyeHeight, 0f);
        }

        /// <summary>발 위치·바라보는 방향으로 순간 이동 (입장·해치).</summary>
        public void Teleport(Vector3 feet, float yaw, float pitch = 0f)
        {
            _body.enabled = false;
            transform.SetPositionAndRotation(feet, Quaternion.Euler(0f, yaw, 0f));
            _body.enabled = true;
            _pitch = Mathf.Clamp(pitch, -85f, 85f);
            _fallSpeed = 0f;
            _eye.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        private void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null)
                return;
            float dt = Time.unscaledDeltaTime;

            // 시점
            Vector2 look = mouse.delta.ReadValue() * (_lookDegreesPerPixel * Settings.GameSettings.OrbitSensitivity);
            transform.Rotate(0f, look.x, 0f, Space.Self);
            _pitch = Mathf.Clamp(_pitch - look.y, -85f, 85f);
            _eye.localRotation = Quaternion.Euler(_pitch, 0f, 0f);

            // 이동
            float right = Axis(GameAction.CameraRight, GameAction.CameraLeft);
            float forward = Axis(GameAction.CameraForward, GameAction.CameraBack);
            var keyboard = Keyboard.current;
            bool run = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
            var move = transform.right * right + transform.forward * forward;
            if (move.sqrMagnitude > 1f)
                move.Normalize();
            move *= run ? _runSpeed : _walkSpeed;

            _fallSpeed = _body.isGrounded ? 1f : _fallSpeed + _gravity * dt; // 바닥에 붙어 있도록 살짝 누름
            move.y = -_fallSpeed;
            _body.Move(move * dt);
        }

        private static float Axis(GameAction positive, GameAction negative)
        {
            return (KeyBindings.IsPressed(positive) ? 1f : 0f) - (KeyBindings.IsPressed(negative) ? 1f : 0f);
        }
    }
}
