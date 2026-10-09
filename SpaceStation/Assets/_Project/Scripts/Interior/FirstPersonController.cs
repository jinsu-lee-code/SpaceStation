using SpaceStation.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpaceStation.Interior
{
    /// <summary>
    /// Phase 11 내부 방문 1인칭 이동: 이동 키(카메라 앞뒤좌우와 같은 키, 7-5 설정 공유) + 마우스 시점, Shift 달리기, 중력.
    /// 시뮬레이션 일시정지와 무관하게 실제 시간으로 움직인다.
    /// 11-9 걸음: 실제로 움직인 수평 거리로 걸음 위상을 진행 (벽에 막히면 멈춤) → 걸음마다 <see cref="Footstep"/>,
    /// 시점은 걸음마다 한 번 아래로(발이 닿을 때) + 두 걸음에 한 번 좌우로 흔들림 (설정 "내부 걸음 시점 흔들림"으로 끔).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class FirstPersonController : MonoBehaviour
    {
        public const float EyeHeight = 1.6f;

        [SerializeField] private float _walkSpeed = 3.2f;
        [SerializeField] private float _runSpeed = 5.5f;
        [SerializeField] private float _lookDegreesPerPixel = 0.12f;
        [SerializeField] private float _gravity = 15f;
        [Header("11-9 걸음")]
        [Tooltip("걸음 한 번의 거리 (걷기 / 달리기, m)")]
        [SerializeField] private Vector2 _stride = new Vector2(1.45f, 1.9f);
        [Tooltip("시점 위아래 흔들림 (걷기 / 달리기, m)")]
        [SerializeField] private Vector2 _bobHeight = new Vector2(0.035f, 0.055f);
        [Tooltip("시점 좌우 흔들림 (걷기 / 달리기, m)")]
        [SerializeField] private Vector2 _bobSway = new Vector2(0.018f, 0.028f);
        [Tooltip("멈추거나 공중일 때 흔들림이 0으로 돌아가는 속도 (초당)")]
        [SerializeField] private float _bobReturn = 4f;

        private CharacterController _body;
        private Transform _eye;
        private float _pitch;
        private float _fallSpeed;
        private float _stepPhase;   // 걸음 수 (정수를 넘을 때마다 발소리)
        private float _bobWeight;
        private float _runBlend;

        public Transform Eye => _eye;

        /// <summary>11-12 휴대 패드 화면 확대 중: 시점 · 이동 멈춤 (중력만).</summary>
        public bool Frozen { get; set; }

        /// <summary>발이 바닥에 닿을 때 (달리는 중이면 true).</summary>
        public event System.Action<bool> Footstep;

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
            if (!Frozen)
            {
                Vector2 look = mouse.delta.ReadValue() * (_lookDegreesPerPixel * Settings.GameSettings.OrbitSensitivity);
                transform.Rotate(0f, look.x, 0f, Space.Self);
                _pitch = Mathf.Clamp(_pitch - look.y, -85f, 85f);
                _eye.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
            }

            // 이동
            float right = Frozen ? 0f : Axis(GameAction.CameraRight, GameAction.CameraLeft);
            float forward = Frozen ? 0f : Axis(GameAction.CameraForward, GameAction.CameraBack);
            var keyboard = Keyboard.current;
            bool run = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
            var move = transform.right * right + transform.forward * forward;
            if (move.sqrMagnitude > 1f)
                move.Normalize();
            move *= run ? _runSpeed : _walkSpeed;

            _fallSpeed = _body.isGrounded ? 1f : _fallSpeed + _gravity * dt; // 바닥에 붙어 있도록 살짝 누름
            move.y = -_fallSpeed;
            var before = transform.position;
            _body.Move(move * dt);
            UpdateSteps(transform.position - before, run, dt);
        }

        /// <summary>실제로 움직인 수평 거리만큼 걸음 위상을 진행하고 시점을 흔든다.</summary>
        private void UpdateSteps(Vector3 moved, bool run, float dt)
        {
            moved.y = 0f;
            float dist = moved.magnitude;
            bool walking = _body.isGrounded && dt > 0f && dist / dt > 0.5f;
            _runBlend = Mathf.MoveTowards(_runBlend, run ? 1f : 0f, dt * 3f);
            _bobWeight = Mathf.MoveTowards(_bobWeight, walking ? 1f : 0f, dt * _bobReturn);
            if (walking)
            {
                float stride = Mathf.Lerp(_stride.x, _stride.y, _runBlend);
                float before = _stepPhase;
                _stepPhase += dist / stride;
                if (Mathf.Floor(_stepPhase) > Mathf.Floor(before))
                    Footstep?.Invoke(run);
            }
            else if (_bobWeight <= 0f)
            {
                _stepPhase = Mathf.Round(_stepPhase); // 멈추면 다음 걸음이 한 걸음 뒤에 나도록 정렬
            }

            Vector3 offset = Vector3.zero;
            if (Settings.GameSettings.HeadBob && _bobWeight > 0f)
            {
                float phase = _stepPhase * Mathf.PI; // 걸음마다 π
                float h = Mathf.Lerp(_bobHeight.x, _bobHeight.y, _runBlend);
                float s = Mathf.Lerp(_bobSway.x, _bobSway.y, _runBlend);
                // 걸음 경계(발이 닿는 순간)에서 가장 낮고 걸음 가운데서 가장 높게 (−|cos|의 꺾임 없이 매끄러운 −cos 2φ)
                offset.y = -h * 0.5f * Mathf.Cos(2f * phase);
                offset.x = s * Mathf.Sin(phase);
                offset *= _bobWeight;
            }
            _eye.localPosition = new Vector3(offset.x, EyeHeight + offset.y, 0f);
        }

        private static float Axis(GameAction positive, GameAction negative)
        {
            return (KeyBindings.IsPressed(positive) ? 1f : 0f) - (KeyBindings.IsPressed(negative) ? 1f : 0f);
        }
    }
}
