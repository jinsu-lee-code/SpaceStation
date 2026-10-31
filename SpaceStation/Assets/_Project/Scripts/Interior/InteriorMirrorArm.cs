using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 자동화 센터 실습 팔 · 홀로 팔 (사용자 2026-10-31 자동화 센터 시안 1 "이대로 유니티로 옮겨줘"):
    /// 실습 정비 베이 로봇 팔이 고장 판의 금 두 줄(팔 각 ±SeamYaw)을 번갈아 용접한다 — 금 위에서 토치를 대고 몇 초 용접 (토치 끝 불꽃
    /// <see cref="InteriorFx.Spark"/>) → 아래 팔을 들어 토치를 떼고 → 다른 금으로 돌아 → 내려 다시 댐. 학습 코어 탁자의 홀로 팔
    /// (실습 팔을 줄인 것)이 같은 각도로 따라 움직여 "코어가 익힌 동작을 베이가 실행"하는 모습을 보인다.
    /// 피벗: Turret = 기둥 축 (위 · 아래 y축 회전), Fore = 팔꿈치 (Turret 자식, 로컬 z축 회전 — + = 토치를 듦). 모델 자세 = 판 가운데에 토치를 댄 채 각 0.
    /// 모듈이 멈추면(비활성 · 전력 부족) 그 자리에 멈춤.
    /// </summary>
    public sealed class InteriorMirrorArm : InteriorRoomDisplay
    {
        private const float WeldTime = 3f;
        private const float LiftTime = 0.7f;
        private const float SwingTime = 1.6f;
        private const float Pause = 0.6f;

        private enum Phase { Lift, Swing, Lower, Weld, Wait }

        [SerializeField] private Transform _turret;
        [SerializeField] private Transform _fore;
        [SerializeField] private Transform _holoTurret;
        [SerializeField] private Transform _holoFore;
        [SerializeField] private Vector3 _tip;       // Fore 로컬 — 판에 닿는 토치 끝
        [SerializeField] private Vector3 _normal;    // Fore 로컬 — 판 바깥 (불꽃 방향)
        [SerializeField] private float _seamYaw = 12f;
        [SerializeField] private float _liftPitch = 28f;
        [SerializeField] private Material _spark;

        private Quaternion _turretRest, _foreRest, _holoTurretRest, _holoForeRest;
        private float _yaw, _pitch, _fromYaw, _toYaw, _t, _timer, _nextSpark;
        private int _side = 1;
        private Phase _phase;
        private bool _ready;

        public void Configure(Transform turret, Transform fore, Transform holoTurret, Transform holoFore, Vector3 tip, Vector3 normal,
            float seamYaw, float liftPitch, Material spark)
        {
            _turret = turret;
            _fore = fore;
            _holoTurret = holoTurret;
            _holoFore = holoFore;
            _tip = tip;
            _normal = normal;
            _seamYaw = seamYaw;
            _liftPitch = liftPitch;
            _spark = spark;
        }

        private void Setup()
        {
            if (_ready || _turret == null || _fore == null)
                return;
            _ready = true;
            _turretRest = _turret.localRotation;
            _foreRest = _fore.localRotation;
            _holoTurretRest = _holoTurret != null ? _holoTurret.localRotation : Quaternion.identity;
            _holoForeRest = _holoFore != null ? _holoFore.localRotation : Quaternion.identity;
            // 모델 자세 (각 0, 판 가운데에 댐)에서 들어 올려 첫 금으로
            _yaw = 0f;
            _pitch = 0f;
            _side = Random.value < 0.5f ? -1 : 1;
            _phase = Phase.Lift;
            _t = 0f;
        }

        protected override void Refresh()
        {
            Setup();
        }

        private void Update()
        {
            if (!_ready || !Active)
                return;   // 모듈이 멈추면 그 자리에 멈춤
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            switch (_phase)
            {
                case Phase.Lift:
                    _t = Mathf.Min(1f, _t + dt / LiftTime);
                    _pitch = _liftPitch * Smooth(_t);
                    if (_t >= 1f)
                    {
                        _fromYaw = _yaw;
                        _toYaw = _seamYaw * _side;
                        _t = 0f;
                        _phase = Phase.Swing;
                    }
                    break;
                case Phase.Swing:
                    _t = Mathf.Min(1f, _t + dt / SwingTime);
                    _yaw = Mathf.Lerp(_fromYaw, _toYaw, Smooth(_t));
                    if (_t >= 1f)
                    {
                        _t = 0f;
                        _phase = Phase.Lower;
                    }
                    break;
                case Phase.Lower:
                    _t = Mathf.Min(1f, _t + dt / LiftTime);
                    _pitch = _liftPitch * (1f - Smooth(_t));
                    if (_t >= 1f)
                    {
                        _timer = WeldTime + Random.Range(-0.5f, 0.8f);
                        _phase = Phase.Weld;
                    }
                    break;
                case Phase.Weld:
                    _timer -= dt;
                    Sparks();
                    if (_timer <= 0f)
                    {
                        _timer = Pause;
                        _phase = Phase.Wait;
                    }
                    break;
                case Phase.Wait:
                    _timer -= dt;
                    if (_timer <= 0f)
                    {
                        _side = -_side;   // 다른 금으로
                        _t = 0f;
                        _phase = Phase.Lift;
                    }
                    break;
            }
            Apply();
        }

        private void Sparks()
        {
            if (_spark == null)
                return;
            float now = Time.unscaledTime;
            if (now < _nextSpark)
                return;
            _nextSpark = now + Random.Range(0.12f, 0.2f);
            InteriorFx.Spark(_fore, _fore.TransformPoint(_tip), _fore.TransformDirection(_normal) + Vector3.up * 0.3f, _spark, 4, 8, 0.8f);   // 현장 수리 용접과 같은 세기
        }

        private void Apply()
        {
            var yaw = Quaternion.AngleAxis(_yaw, Vector3.up);
            var pitch = Quaternion.AngleAxis(_pitch, Vector3.forward);
            _turret.localRotation = _turretRest * yaw;
            _fore.localRotation = _foreRest * pitch;
            if (_holoTurret != null)
                _holoTurret.localRotation = _holoTurretRest * yaw;
            if (_holoFore != null)
                _holoFore.localRotation = _holoForeRest * pitch;
        }

        private static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }
    }
}
