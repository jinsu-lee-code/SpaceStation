using SpaceStation.Simulation;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 1단계 보안청 천장 포탑 받침 (사용자 2026-10-31 보안청 시안 1 "이대로 유니티로 옮겨줘"): 실제 유성우 · 포격 예보(<see cref="MeteorShowerSystem"/>)를 따라 돎.
    /// - 평소 = 몇 초마다 다른 방향으로 천천히 돌아 경계
    /// - 예보 (경보) = 오는 방향(모듈 로컬 — 레이더 상황판 운석 점과 같은 방향)으로 돌아 기다림
    /// - 낙하 중 = 그 방향을 향하면 1.2 ~ 2.2초마다 쏨: 쌍포 약실이 하나씩 뒤로 밀렸다(<see cref="RecoilBack"/>) 천천히 돌아옴
    ///   위 · 아래에서 오면 방향은 그대로 두고 쏘기만
    /// 포신 방향 = 받침 로컬 +z (모델 자세 = 북, 각 0). 약실은 받침 자식 — 받침 로컬 −z로 밀림.
    /// 모듈이 멈추면(비활성 · 전력 부족) 그 자리에 멈춤. 실제 시간(unscaled).
    /// </summary>
    public sealed class InteriorTurretAim : InteriorRoomDisplay
    {
        private const float AlertSpeed = 45f;     // 도/초 (경보 · 낙하)
        private const float PatrolSpeed = 14f;    // 도/초 (평소)
        private const float RecoilBack = 0.06f;
        private const float RecoilReturn = 0.55f;
        private const float SalvoGap = 0.28f;     // 두 약실 사이

        [SerializeField] private Transform _turret;
        [SerializeField] private Transform[] _breeches = System.Array.Empty<Transform>();
        [Tooltip("약실이 밀리는 거리 (m, 받침 로컬 −z)")]
        [SerializeField] private float _recoil = 0.14f;

        private Quaternion _rest;
        private Vector3[] _breechRest;
        private float[] _shotAt;
        private float _yaw, _target, _nextPatrol, _nextShot;
        private MeteorShowerSystem.ShowerPhase _phase;
        private bool _hasDir;
        private float _dirYaw;
        private bool _ready;

        public void Configure(Transform turret, Transform[] breeches, float recoil)
        {
            _turret = turret;
            _breeches = breeches;
            _recoil = recoil;
        }

        private void Setup()
        {
            if (_ready || _turret == null)
                return;
            _ready = true;
            _rest = _turret.localRotation;
            _breechRest = new Vector3[_breeches.Length];
            _shotAt = new float[_breeches.Length];
            for (int i = 0; i < _breeches.Length; i++)
            {
                _breechRest[i] = _breeches[i] != null ? _breeches[i].localPosition : Vector3.zero;
                _shotAt[i] = float.NegativeInfinity;
            }
            // 방에 들어올 때마다 다른 방향에서 경계 중
            _yaw = Random.Range(-180f, 180f);
            _target = _yaw;
            _nextPatrol = Time.unscaledTime + Random.Range(1f, 3f);
            Apply(Time.unscaledTime);
        }

        protected override void Refresh()
        {
            Setup();
            var shower = Station != null && Station.Simulation != null ? Station.Simulation.MeteorShower : null;
            if (!Active || shower == null)
            {
                _phase = MeteorShowerSystem.ShowerPhase.Idle;
                _hasDir = false;
                return;
            }
            _phase = shower.Phase;
            var local = Module != null ? WorldToLocalDir(Module, shower.Direction) : shower.Direction;
            _hasDir = _phase != MeteorShowerSystem.ShowerPhase.Idle && local.y == 0 && (local.x != 0 || local.z != 0);
            if (_hasDir)
                _dirYaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;   // 위 축 회전 각: +z = 0°, +x = 90°
        }

        private void Update()
        {
            if (!_ready || !Active)
                return;   // 모듈이 멈추면 그 자리에 멈춤
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            float now = Time.unscaledTime;
            bool alert = _phase != MeteorShowerSystem.ShowerPhase.Idle;
            if (alert)
                _target = _hasDir ? _dirYaw : _target;
            else if (now >= _nextPatrol)
            {
                // 경계: 지금 방향에서 30 ~ 110° 옆 부채꼴로
                _target = _yaw + Random.Range(30f, 110f) * (Random.value < 0.5f ? -1f : 1f);
                _nextPatrol = now + Random.Range(5f, 9f);
            }
            // 가까워질수록 느리게 (멈출 때 덜컥하지 않게)
            float delta = Mathf.DeltaAngle(_yaw, _target);
            float speed = (alert ? AlertSpeed : PatrolSpeed) * Mathf.Clamp01(Mathf.Abs(delta) / 15f + 0.12f);
            _yaw += Mathf.Clamp(delta, -speed * dt, speed * dt);
            if (_phase == MeteorShowerSystem.ShowerPhase.Falling && Mathf.Abs(Mathf.DeltaAngle(_yaw, _target)) < 3f && now >= _nextShot)
            {
                for (int i = 0; i < _shotAt.Length; i++)
                    _shotAt[i] = now + i * SalvoGap;
                _nextShot = now + Random.Range(1.2f, 2.2f);
            }
            Apply(now);
        }

        private void Apply(float now)
        {
            _turret.localRotation = _rest * Quaternion.AngleAxis(_yaw, Vector3.up);
            for (int i = 0; i < _breeches.Length; i++)
            {
                if (_breeches[i] == null)
                    continue;
                float t = now - _shotAt[i];
                float back = t < 0f ? 0f
                    : t < RecoilBack ? t / RecoilBack
                    : 1f - Smooth((t - RecoilBack) / RecoilReturn);
                _breeches[i].localPosition = _breechRest[i] + Vector3.back * (_recoil * back);
            }
        }

        private static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }
    }
}
