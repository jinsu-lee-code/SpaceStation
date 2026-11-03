using SpaceStation.Building;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 2단계 치안 본부 광장의 떠 있는 순찰차 (사용자 2026-11-03 시안 1, 바깥 <see cref="Building.PatrolCarFx"/>와 같은 연출을 실내 크기로):
    /// 덕트 팬 아래 추진 불꽃 · 살짝 떠서 출렁임, 가동 중 가끔 출동 — 경광등 번쩍임 (<see cref="PatrolSiren"/>) → 떠오름 → 돌아섬 → 앞으로 숙이고 가속하며 작아져 사라짐 → 잠시 뒤 같은 길로 돌아와 내려앉음.
    /// 하늘 껍질 안에서 끝나야 하므로 실제 거리는 짧고, 작아지며 멀어지는 것처럼 보임. 위치 = 프리팹 로컬 (차 피벗 = 차 바닥 가운데, 앞 = +x), 실제 시간.
    /// </summary>
    public sealed class InteriorPatrolCar : InteriorRoomDisplay
    {
        private enum Phase { Parked, Alert, Lift, Turn, Fly, Away, Return, TurnBack, Land }

        private const float AlertTime = 0.7f, LiftTime = 1.2f, TurnTime = 0.8f, FlyTime = 2.6f;
        private const float PlumeRadius = 0.11f;
        private static readonly Color PlumeColor = new Color(0.55f, 0.8f, 1f, 1f);

        [SerializeField] private Transform _car;
        [SerializeField] private Vector3[] _thrusters;
        [SerializeField] private Vector3 _sirenA;
        [SerializeField] private Vector3 _sirenB;
        [SerializeField] private Vector3 _sirenSize = new Vector3(0.2f, 0.14f, 0.34f);
        [SerializeField] private Material _plumeMaterial;
        [Tooltip("출동 방향 (프리팹 로컬 가로)")]
        [SerializeField] private Vector3 _heading = Vector3.left;
        [Tooltip("날아가는 거리 (m) — 하늘 껍질 안")]
        [SerializeField] private float _flyDistance = 1.9f;
        [SerializeField] private float _hover = 0.05f;
        [SerializeField] private float _cruiseHeight = 1.7f;
        [SerializeField] private float _flyClimb = 1.1f;
        [Tooltip("출동 간격 (초, 최소 ~ 최대)")]
        [SerializeField] private Vector2 _dispatchEvery = new Vector2(25f, 50f);
        [SerializeField] private Vector2 _awaySeconds = new Vector2(6f, 10f);

        private Phase _phase = Phase.Parked;
        private float _timer;
        private float _clock;
        private float _seed;
        private Vector3 _home;
        private Quaternion _homeRotation;
        private Quaternion _face;
        private ParticleSystem[] _plumes;
        private PatrolSiren _siren;
        private Collider[] _colliders;
        private float _thrust = -2f;
        private bool _built;

        public bool Flying => _phase != Phase.Parked;
        public string PhaseName => _phase.ToString();

        public void Configure(Transform car, Vector3[] thrusters, Vector3 sirenA, Vector3 sirenB, Vector3 sirenSize, Material plumeMaterial, Vector3 heading, float flyDistance)
        {
            _car = car;
            _thrusters = thrusters;
            _sirenA = sirenA;
            _sirenB = sirenB;
            _sirenSize = sirenSize;
            _plumeMaterial = plumeMaterial;
            _heading = heading.normalized;
            _flyDistance = flyDistance;
        }

        protected override void Refresh()
        {
        }

        private void OnDisable()
        {
            // 방을 나가면 제자리로
            if (!_built)
                return;
            _phase = Phase.Parked;
            _timer = NextWait();
            _car.localScale = Vector3.one;
            SetColliders(true);
            SetSiren(false);
            SetThrust(0f);
        }

        private void Build()
        {
            _built = true;
            _home = _car.localPosition;
            _homeRotation = _car.localRotation;
            _seed = Random.value * 100f;
            _timer = Random.Range(8f, _dispatchEvery.x);
            _face = Quaternion.Euler(0f, Mathf.Atan2(-_heading.z, _heading.x) * Mathf.Rad2Deg, 0f);
            _colliders = _car.GetComponentsInChildren<Collider>(true);
            _plumes = new ParticleSystem[_thrusters.Length];
            for (int i = 0; i < _thrusters.Length; i++)
                _plumes[i] = Plume(_thrusters[i]);
            _siren = new PatrolSiren(_car, _plumeMaterial, _sirenA, _sirenB, _sirenSize, 1.4f, 4.5f, 1f);
            SetThrust(0f);
        }

        /// <summary>바로 출동 (가동 중이면 스스로 가끔 부름 — 확인용으로 직접 불러도 됨).</summary>
        public void Dispatch()
        {
            if (!_built || _phase != Phase.Parked)
                return;
            Enter(Phase.Alert);
            SetSiren(true);
        }

        private void Enter(Phase phase)
        {
            _phase = phase;
            _clock = 0f;
        }

        private float NextWait() => Random.Range(_dispatchEvery.x, _dispatchEvery.y);

        private void Update()
        {
            if (_car == null || _plumeMaterial == null)
                return;
            if (!_built)
                Build();
            float dt = Time.unscaledDeltaTime;
            _clock += dt;
            _siren.Tick(dt);
            switch (_phase)
            {
                case Phase.Parked:
                {
                    float t = Time.unscaledTime;
                    _car.localPosition = _home + Vector3.up * (_hover + Mathf.Sin(t * 1.7f + _seed) * 0.03f);
                    _car.localRotation = _homeRotation * Quaternion.Euler(Mathf.Sin(t * 1.1f + _seed) * 1.5f, 0f, Mathf.Sin(t * 0.9f + _seed * 2f) * 1.5f);
                    if (Polled && Active)
                    {
                        _timer -= dt;
                        if (_timer <= 0f)
                            Dispatch();
                    }
                    break;
                }
                case Phase.Alert:
                    SetThrust(0.4f * Mathf.Clamp01(_clock / AlertTime));
                    if (_clock >= AlertTime)
                    {
                        SetColliders(false);
                        Enter(Phase.Lift);
                    }
                    break;
                case Phase.Lift:
                {
                    float k = Mathf.Clamp01(_clock / LiftTime);
                    SetThrust(Mathf.Lerp(0.4f, 1f, k));
                    _car.localPosition = _home + Vector3.up * Mathf.Lerp(_hover, _cruiseHeight, Ease(k));
                    _car.localRotation = _homeRotation * Quaternion.Euler(0f, 0f, 6f * Mathf.Sin(Mathf.PI * k));
                    if (k >= 1f)
                        Enter(Phase.Turn);
                    break;
                }
                case Phase.Turn:
                {
                    float k = Mathf.Clamp01(_clock / TurnTime);
                    _car.localRotation = Quaternion.Slerp(_homeRotation, _face, Ease(k));
                    if (k >= 1f)
                        Enter(Phase.Fly);
                    break;
                }
                case Phase.Fly:
                {
                    float k = Mathf.Clamp01(_clock / FlyTime);
                    Pose(k, -12f * Mathf.Sin(Mathf.PI * Mathf.Min(1f, k * 1.6f) * 0.5f));
                    if (k >= 1f)
                    {
                        SetThrust(-1f);
                        SetSiren(false);
                        _timer = Random.Range(_awaySeconds.x, _awaySeconds.y);
                        Enter(Phase.Away);
                    }
                    break;
                }
                case Phase.Away:
                    _timer -= dt;
                    if (_timer <= 0f)
                    {
                        SetThrust(1f);
                        SetSiren(true);
                        Enter(Phase.Return);
                    }
                    break;
                case Phase.Return:
                {
                    float k = Mathf.Clamp01(_clock / FlyTime);
                    float r = 1f - k;
                    Pose(r, 10f * Mathf.Sin(Mathf.PI * Mathf.Min(1f, r * 1.6f) * 0.5f));   // 감속하며 앞이 들림
                    if (k >= 1f)
                        Enter(Phase.TurnBack);
                    break;
                }
                case Phase.TurnBack:
                {
                    float k = Mathf.Clamp01(_clock / TurnTime);
                    _car.localRotation = Quaternion.Slerp(_face, _homeRotation, Ease(k));
                    if (k >= 1f)
                        Enter(Phase.Land);
                    break;
                }
                case Phase.Land:
                {
                    float k = Mathf.Clamp01(_clock / LiftTime);
                    SetThrust(Mathf.Lerp(1f, 0f, k));
                    _car.localPosition = _home + Vector3.up * Mathf.Lerp(_cruiseHeight, _hover, Ease(k));
                    if (k >= 1f)
                    {
                        SetSiren(false);
                        SetColliders(true);
                        _car.localScale = Vector3.one;
                        _timer = NextWait();
                        Enter(Phase.Parked);
                    }
                    break;
                }
            }
        }

        /// <summary>날아가는 길: k = 0 떠오른 자리 ~ 1 멀리 (가속, k² 거리) · 멀수록 작아짐.</summary>
        private void Pose(float k, float pitch)
        {
            _car.localPosition = _home + Vector3.up * (_cruiseHeight + _flyClimb * k) + _heading * (_flyDistance * k * k);
            _car.localRotation = _face * Quaternion.Euler(0f, 0f, pitch);
            _car.localScale = Vector3.one * Mathf.Lerp(1f, 0.02f, Ease(k));
        }

        private void SetColliders(bool on)
        {
            foreach (var c in _colliders)
            {
                if (c != null)
                    c.enabled = on;
            }
        }

        private void SetSiren(bool on)
        {
            if (on)
                _siren.Start();
            else
                _siren.Stop();
        }

        /// <summary>추진 세기: −1 = 끔, 0 = 대기 (짧은 불꽃), 1 = 비행 (길고 센 불꽃).</summary>
        private void SetThrust(float level)
        {
            if (_plumes == null || Mathf.Abs(level - _thrust) < 0.02f)
                return;
            _thrust = level;
            foreach (var ps in _plumes)
            {
                var emission = ps.emission;
                emission.enabled = level >= 0f;
                if (level < 0f)
                    continue;
                emission.rateOverTime = Mathf.Lerp(26f, 40f, level);
                var main = ps.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(Mathf.Lerp(0.1f, 0.18f, level), Mathf.Lerp(0.16f, 0.32f, level));
                main.startSpeed = new ParticleSystem.MinMaxCurve(Mathf.Lerp(1.0f, 1.6f, level), Mathf.Lerp(1.4f, 2.8f, level));
            }
        }

        // 바깥 순찰차 · 합성 고리와 같은 불꽃 — 흰 심에서 청백으로 옅어지며 아래로
        private ParticleSystem Plume(Vector3 localPosition)
        {
            var go = new GameObject("Plume");
            go.transform.SetParent(_car, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.LookRotation(Vector3.down);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startSize = new ParticleSystem.MinMaxCurve(PlumeRadius * 1.4f, PlumeRadius * 2.2f);
            main.startColor = new ParticleSystem.MinMaxGradient(PlumeColor, new Color(0.85f, 0.95f, 1f, 1f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 60;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 6f;
            shape.radius = PlumeRadius * 0.5f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.1f));
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(PlumeColor, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            color.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = _plumeMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        private static float Ease(float k) => k * k * (3f - 2f * k);
    }
}
