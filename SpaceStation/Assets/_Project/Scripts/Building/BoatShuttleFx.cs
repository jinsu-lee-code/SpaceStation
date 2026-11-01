using UnityEngine;

namespace SpaceStation.Building
{
    /// <summary>
    /// 궤도 항만 해마 가리비 배 넷 (사용자 2026-10-29 플레이 "해마가 해변까지 올라오면서 회전" → "뒤쪽 호 왕복" 선택, StationArtBuilder가 자동 배선):
    /// 한 덩어리로 회전목마 둘레를 돌던 배를 한 척씩 나눈 부품 "PortBoat_*"(원점 = 배 가운데)이 해변 쪽을 피한 뒤쪽 호를 줄지어 따라가고,
    /// 호 끝에서 잠시 멈췄다가 해마가 안쪽(회전목마 쪽)으로 돌아서서 반대로 돌아온다. 물 위에서 살짝 출렁임.
    /// 움직임은 게임 시간. 비활성이면 그 자리에 떠 있음. 고스트(배치 미리보기)에서는 멈춤.
    /// </summary>
    public sealed class BoatShuttleFx : MonoBehaviour
    {
        [Tooltip("배 (호를 따라가는 순서대로)")]
        [SerializeField] private Transform[] _boats = new Transform[0];
        [Tooltip("배마다 모델에서 놓였던 각 (라디안, 가운데 기준 x축에서 z축 쪽으로) — 배 메시의 앞(해마 쪽) = 이 각에서 각이 커지는 쪽")]
        [SerializeField] private float[] _homeAngles = new float[0];
        [Tooltip("회전목마 가운데 (부모 기준)")]
        [SerializeField] private Vector3 _pivot;
        [Tooltip("배 길 반지름: x · 앞쪽(−z) · 뒤쪽(+z) — 뒤쪽은 산호 방파제를 피해 조금 납작하게")]
        [SerializeField] private Vector3 _radii = new Vector3(0.72f, 0.72f, 0.64f);
        [Tooltip("배 가운데가 갈 수 있는 각 범위 (라디안, 낮음 < 높음, 뒤쪽을 지남) — 에디터가 해변 · 높은 지형을 피해 고름")]
        [SerializeField] private Vector2 _range = new Vector2(-0.6f, 3.8f);
        [Tooltip("배 사이 각 (라디안)")]
        [SerializeField] private float _spacing = 0.87f;
        [Tooltip("나아가는 빠르기 (라디안/초)")]
        [SerializeField] private float _speed = 0.1f;
        [SerializeField] private float _pause = 1.5f;
        [SerializeField] private float _turnTime = 2.2f;

        private ModuleView _view;
        private bool _live;
        private bool _built;
        private float _u;          // 맨 앞 배(배열 0)의 각
        private float _dir = 1f;   // +1 = 각이 커지는 쪽
        private float _flip;       // 0 = 각이 커지는 쪽을 봄, −180 = 반대쪽 (돌 때 해마가 안쪽을 지남)
        private float _wait;       // 멈춤 · 돌기 남은 시간
        private float _turnFrom, _turnTo;
        private float _clock;

        public float Angle => _u;
        public float Direction => _dir;

        private void Start()
        {
            _view = GetComponentInParent<ModuleView>();
            _live = _view != null && _view.Module != null;
            if (_live)
                Play();
        }

        /// <summary>배를 호 위 첫 자리에 놓고 움직임 시작 (Start가 부름 — 확인용으로 미리보기에서 직접 불러도 됨).</summary>
        public void Play()
        {
            if (_built || _boats.Length == 0)
                return;
            _built = true;
            float home = _homeAngles.Length > 0 ? _homeAngles[0] : _range.x;
            _u = Mathf.Clamp(_range.x + Mathf.Repeat(home - _range.x, 2f * Mathf.PI), _range.x, MaxU); // 범위가 0°를 넘어가도 이어지게
            Step(0f);
        }

        private float MaxU => Mathf.Max(_range.x, _range.y - _spacing * (_boats.Length - 1));

        private void Update()
        {
            if (_built)
                Step(Time.deltaTime);
        }

        /// <summary>한 프레임 진행 (확인용으로 직접 불러도 됨).</summary>
        public void Step(float dt)
        {
            _clock += dt;
            bool operational = !_live || _view.Operational;
            if (operational)
            {
                if (_wait > 0f)
                {
                    _wait -= dt;
                    float k = Mathf.Clamp01(1f - Mathf.Max(0f, _wait) / _turnTime);
                    _flip = Mathf.Lerp(_turnFrom, _turnTo, k * k * (3f - 2f * k));
                }
                else
                {
                    _u += _dir * _speed * dt;
                    if (_u >= MaxU || _u <= _range.x)
                    {
                        _u = Mathf.Clamp(_u, _range.x, MaxU);
                        _dir = -_dir;
                        _turnFrom = _flip;
                        _turnTo = _dir > 0f ? 0f : -180f;
                        _wait = _pause + _turnTime; // 앞 _pause 동안은 그대로 (k = 0), 그 뒤 돌아섬
                    }
                }
            }
            for (int i = 0; i < _boats.Length; i++)
            {
                var boat = _boats[i];
                if (boat == null)
                    continue;
                float a = _u + _spacing * i;
                float home = i < _homeAngles.Length ? _homeAngles[i] : a;
                float bob = Mathf.Sin(_clock * 1.6f + i * 1.7f);
                boat.localPosition = _pivot + PathOffset(_radii, a) + Vector3.up * (0.0015f * bob);
                boat.localRotation = Quaternion.AngleAxis(_flip + PathYaw(_radii, home, a), Vector3.up) * Quaternion.AngleAxis(1.5f * bob, Vector3.forward);
            }
        }

        /// <summary>배 길 위 자리 (가운데 기준): 각 a에서 x 반지름 · 앞(−z) / 뒤(+z) 반지름이 다른 타원.</summary>
        public static Vector3 PathOffset(Vector3 radii, float a)
        {
            float s = Mathf.Sin(a);
            return new Vector3(radii.x * Mathf.Cos(a), 0f, (s < 0f ? radii.y : radii.z) * s);
        }

        /// <summary>
        /// 모델에서 원(반지름 같음) 위 각 home에 놓였던 배를 길 위 각 a로 옮길 때 돌릴 각 (Unity 위쪽 축, 도) — 배 앞 = 각이 커지는 쪽 접선.
        /// </summary>
        public static float PathYaw(Vector3 radii, float home, float a)
        {
            var from = new Vector3(-Mathf.Sin(home), 0f, Mathf.Cos(home));
            var to = PathOffset(radii, a + 0.002f) - PathOffset(radii, a - 0.002f);
            return Vector3.SignedAngle(from, to, Vector3.up);
        }
    }
}
