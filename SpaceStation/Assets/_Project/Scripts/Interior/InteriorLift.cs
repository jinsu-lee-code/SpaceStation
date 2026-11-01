using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 모듈 내부 유리 엘리베이터 (사용자 결정 2026-10-29 "유리 엘리베이터 탑승" — 여러 층 진화 모듈 공통, 첫 사용 = 거주 타워):
    /// 캡슐이 층 사이를 실제로 오르내리고, 캡슐 안에서 층 버튼을 누른 플레이어는 함께 실려 감 (<see cref="FirstPersonController.Ride"/>).
    /// 층마다 샤프트 문짝 두 장은 캡슐이 그 층에 서 있을 때만 열림 (빈 샤프트로 떨어지지 않게 — 문짝에 충돌 상자).
    /// 움직이기 전 문이 다 닫힌 뒤 출발, 도착하면 그 층 문이 열림. 템플릿 빌더(InteriorTemplateBuilder)가 configure에서 붙임.
    /// 위치는 모두 템플릿 루트(이 물체) 기준. 시간은 실제 시간 (내부 모드는 게임 배속과 무관하게 걷는 것과 같게).
    /// </summary>
    public sealed class InteriorLift : MonoBehaviour
    {
        [SerializeField] private Transform _car;
        [Tooltip("층마다 캡슐 높이 이동량 (0 = 모델에 놓인 자리 = 1층)")]
        [SerializeField] private float[] _stops;
        [SerializeField] private Transform[] _doorsLeft;
        [SerializeField] private Transform[] _doorsRight;
        [Tooltip("문짝이 옆으로 열리는 거리 (m)")]
        [SerializeField] private float _doorTravel = 0.52f;
        [Tooltip("문 열림 방향 (템플릿 기준, 오른쪽 문짝이 이쪽으로)")]
        [SerializeField] private Vector3 _doorAxis = Vector3.right;
        [Tooltip("샤프트 가운데 (템플릿 기준, y = 1층 캡슐 바닥)")]
        [SerializeField] private Vector3 _center;
        [Tooltip("캡슐 안쪽 반지름 — 이 안에 선 플레이어만 실림")]
        [SerializeField] private float _innerRadius = 0.85f;
        [SerializeField] private float _speed = 1.8f;
        [SerializeField] private float _doorSpeed = 1.6f;

        private Vector3 _carBase;
        private Vector3[] _leftBase, _rightBase;
        private float[] _open;
        private float _y;
        private int _level;
        private int _target = -1;
        private float _from, _travel, _t;
        private FirstPersonController _rider;

        public int Level => _level;
        public int StopCount => _stops != null ? _stops.Length : 0;
        public bool Moving => _target >= 0;

        public void Configure(Transform car, float[] stops, Transform[] doorsLeft, Transform[] doorsRight, Vector3 center, Vector3 doorAxis, float innerRadius)
        {
            _car = car;
            _stops = stops;
            _doorsLeft = doorsLeft;
            _doorsRight = doorsRight;
            _center = center;
            _doorAxis = doorAxis;
            _innerRadius = innerRadius;
        }

        private void Awake()
        {
            int n = StopCount;
            _carBase = _car != null ? _car.localPosition : Vector3.zero;
            _open = new float[n];
            _leftBase = new Vector3[n];
            _rightBase = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                _leftBase[i] = _doorsLeft[i] != null ? _doorsLeft[i].localPosition : Vector3.zero;
                _rightBase[i] = _doorsRight[i] != null ? _doorsRight[i].localPosition : Vector3.zero;
            }
        }

        private void OnDisable()
        {
            // 내부에서 나가거나 방이 꺼지면 실려 가던 플레이어를 내려놓고 캡슐은 목표 층에 바로 세움
            if (_target >= 0)
                Arrive();
        }

        /// <summary>플레이어 발이 캡슐 안 (캡슐 바닥 높이 근처 · 안쪽 반지름 안)인지.</summary>
        public bool Contains(Vector3 worldFeet)
        {
            var p = transform.InverseTransformPoint(worldFeet) - (_center + Vector3.up * _y);
            return new Vector2(p.x, p.z).magnitude < _innerRadius && p.y > -0.3f && p.y < 1.2f;
        }

        /// <summary>호출 버튼: 캡슐을 level 층으로 (이미 그 층이면 문만 열린 채).</summary>
        public void Call(int level)
        {
            if (!Moving && level != _level)
                Begin(level, null);
        }

        /// <summary>캡슐 안 층 버튼: 캡슐 안에 선 플레이어를 싣고 level 층으로.</summary>
        public void Ride(int level, FirstPersonController player)
        {
            if (Moving || level == _level || level < 0 || level >= StopCount)
                return;
            Begin(level, player != null && Contains(player.transform.position) ? player : null);
        }

        private void Begin(int level, FirstPersonController rider)
        {
            _target = level;
            _from = _y;
            _travel = Mathf.Max(0.01f, Mathf.Abs(_stops[level] - _y) / _speed);
            _t = -1f; // 문이 다 닫힐 때까지 기다림
            _rider = rider;
            if (_rider != null)
                _rider.Ride(_car);
        }

        private void Update()
        {
            if (_car == null || StopCount == 0)
                return;
            float dt = Time.unscaledDeltaTime;
            // 문: 캡슐이 서 있는 층만 열림
            bool allClosed = true;
            for (int i = 0; i < StopCount; i++)
            {
                float want = !Moving && i == _level ? 1f : 0f;
                _open[i] = Mathf.MoveTowards(_open[i], want, _doorSpeed * dt);
                float e = Mathf.SmoothStep(0f, 1f, _open[i]);
                var axis = _doorAxis.normalized * (_doorTravel * e);
                if (_doorsLeft[i] != null)
                    _doorsLeft[i].localPosition = _leftBase[i] - axis;
                if (_doorsRight[i] != null)
                    _doorsRight[i].localPosition = _rightBase[i] + axis;
                if (_open[i] > 0.001f)
                    allClosed = false;
            }
            if (!Moving)
                return;
            if (_t < 0f)
            {
                if (!allClosed)
                    return;
                _t = 0f;
            }
            _t += dt / _travel;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t));
            _y = Mathf.Lerp(_from, _stops[_target], k);
            _car.localPosition = _carBase + Vector3.up * _y;
            if (_t >= 1f)
                Arrive();
        }

        private void Arrive()
        {
            _y = _stops[_target];
            if (_car != null)
                _car.localPosition = _carBase + Vector3.up * _y;
            _level = _target;
            _target = -1;
            if (_rider != null)
                _rider.Dismount();
            _rider = null;
        }
    }
}
