using UnityEngine;

namespace SpaceStation.Building
{
    /// <summary>
    /// 5-5 실드 Emitter 연출 (Emitter 오브젝트에 붙임, StationArtBuilder가 자동 배선).
    /// - 프레임 띠 두 개(EmitterRingA/B)가 서로 다른 속도·방향으로 Y축 회전 (자이로스코프)
    /// - 반투명 외피 안에서 빛점들이 서로 다른 기울기의 궤도를 돌며 꼬리를 남김, 코어는 은은하게 맥동
    /// - ModuleView 상태: 비활성 = 회전이 서서히 멈추고 빛점이 꺼짐 / 파손 = 회전이 덜컥거리고 빛점이 깜빡임
    /// - 실드가 운석을 빗겨내면(ModuleView.Impulse) 코어가 번쩍이고 회전이 잠깐 빨라짐
    /// 빛점·꼬리는 Start에서 만들어 ModuleView 상태 틴트·선택 테두리 대상에서 빠진다.
    /// 배치 고스트(초기화 안 된 ModuleView)에서는 회전만 한다.
    /// </summary>
    public sealed class ShieldEmitterFx : MonoBehaviour
    {
        [SerializeField] private Transform _ringA;
        [SerializeField] private Transform _ringB;
        [SerializeField] private Transform _core;
        [SerializeField] private Material _orbMaterial;
        [SerializeField] private Material _trailMaterial;

        [Header("Rotation (deg/s)")]
        [SerializeField] private float _ringASpeed = 120f;
        [SerializeField] private float _ringBSpeed = -80f;

        [Header("Orbs")]
        [SerializeField, Range(1, 8)] private int _orbCount = 4;
        [SerializeField] private float _orbRadius = 0.125f;
        [SerializeField] private float _orbSize = 0.05f;
        [SerializeField] private float _orbSpeed = 260f;
        [SerializeField] private float _trailTime = 0.45f;

        [Header("Impulse (meteor deflected)")]
        [SerializeField] private float _impulseDuration = 1.2f;
        [Tooltip("플래시 순간 회전 배율 추가분")]
        [SerializeField] private float _impulseSpin = 5f;
        [SerializeField] private float _impulseCoreScale = 1.8f;

        private ModuleView _view;
        private bool _live;
        private Transform[] _orbs;
        private TrailRenderer[] _trails;
        private Quaternion[] _orbPlanes;
        private float[] _orbPhase;
        private float _spin = 1f;   // 현재 회전 배율 (상태를 부드럽게 따라감)
        private float _energy = 1f; // 빛점·코어 세기 0~1
        private float _impulse;     // 남은 플래시 1→0
        private Vector3 _coreScale = Vector3.one;
        private float _seed;

        public float Spin => _spin;
        public float Energy => _energy;

        private void Start()
        {
            _view = GetComponentInParent<ModuleView>();
            _live = _view != null && _view.Module != null;
            if (_core != null)
                _coreScale = _core.localScale;
            _seed = Random.value * 100f;
            if (!_live)
                return;
            _view.Impulse += HandleImpulse;
            CreateOrbs();
        }

        private void OnDestroy()
        {
            if (_view != null)
                _view.Impulse -= HandleImpulse;
        }

        private void HandleImpulse() => _impulse = 1f;

        private void Update() => Step(Time.deltaTime, Time.time);

        /// <summary>한 프레임 진행 (테스트에서 직접 호출).</summary>
        public void Step(float dt, float time)
        {
            bool operational = !_live || _view.Operational;
            bool damaged = _live && _view.DamageVisual == ModuleDamageVisual.Damaged;
            float targetSpin = operational ? 1f : 0f;
            float targetEnergy = operational ? 1f : 0f;
            float spinRate = operational ? 0.8f : 0.5f; // 서서히 감속·가속
            if (damaged)
            {
                // 걸렸다 풀렸다 하는 회전 + 깜빡이는 에너지
                float n = Mathf.PerlinNoise(time * 3f, _seed);
                targetSpin *= n > 0.6f ? 0.05f : 0.35f + n;
                targetEnergy *= Mathf.PerlinNoise(time * 9f, _seed + 7f) > 0.55f ? 0.1f : 0.8f;
                spinRate = 4f;
            }

            _impulse = Mathf.Max(0f, _impulse - dt / Mathf.Max(0.01f, _impulseDuration));
            _spin = Mathf.MoveTowards(_spin, targetSpin, dt * spinRate);
            _energy = Mathf.MoveTowards(_energy, Mathf.Max(targetEnergy, _impulse), dt * 4f);

            float spin = _spin * (1f + _impulseSpin * _impulse * _impulse);
            if (_ringA != null)
                _ringA.Rotate(0f, _ringASpeed * spin * dt, 0f, Space.Self);
            if (_ringB != null)
                _ringB.Rotate(0f, _ringBSpeed * spin * dt, 0f, Space.Self);
            if (_core != null)
            {
                float pulse = Mathf.Lerp(0.55f, 1f, _energy) * (1f + 0.08f * Mathf.Sin(time * 4f));
                _core.localScale = _coreScale * (pulse + (_impulseCoreScale - 1f) * _impulse);
            }
            UpdateOrbs(dt, spin);
        }

        private void CreateOrbs()
        {
            _orbs = new Transform[_orbCount];
            _trails = new TrailRenderer[_orbCount];
            _orbPlanes = new Quaternion[_orbCount];
            _orbPhase = new float[_orbCount];
            for (int i = 0; i < _orbCount; i++)
            {
                var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                orb.name = "Orb" + i;
                Destroy(orb.GetComponent<Collider>());
                orb.transform.SetParent(transform, false);
                orb.transform.localScale = Vector3.one * _orbSize;
                var r = orb.GetComponent<MeshRenderer>();
                r.sharedMaterial = _orbMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;

                var trail = orb.AddComponent<TrailRenderer>();
                trail.time = _trailTime;
                trail.minVertexDistance = 0.008f;
                trail.widthMultiplier = _orbSize * 0.9f;
                trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
                var gradient = new Gradient();
                gradient.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
                trail.colorGradient = gradient;
                trail.sharedMaterial = _trailMaterial;
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                trail.receiveShadows = false;

                // 궤도면: 세로축 둘레로 고르게 돌리고 서로 다르게 기울임 (원자 모형)
                _orbPlanes[i] = Quaternion.AngleAxis(i * 180f / _orbCount, Vector3.up)
                                * Quaternion.AngleAxis(55f + 20f * (i % 2), Vector3.right);
                _orbPhase[i] = i * 360f / _orbCount;
                _orbs[i] = orb.transform;
                _trails[i] = trail;
            }
            UpdateOrbs(0f, 1f);
        }

        private void UpdateOrbs(float dt, float spin)
        {
            if (_orbs == null)
                return;
            bool visible = _energy > 0.05f;
            // 부품은 형제 관계일 수 있으므로 코어 위치를 이 오브젝트 기준으로 변환
            Vector3 center = _core != null ? transform.InverseTransformPoint(_core.position) : Vector3.zero;
            for (int i = 0; i < _orbs.Length; i++)
            {
                float dir = i % 2 == 0 ? 1f : -1.3f;
                _orbPhase[i] += _orbSpeed * Mathf.Lerp(0.3f, 1f, Mathf.Clamp01(spin)) * (1f + _impulse) * dir * dt;
                Vector3 local = _orbPlanes[i] * (Quaternion.AngleAxis(_orbPhase[i], Vector3.up) * Vector3.forward * _orbRadius);
                _orbs[i].localPosition = center + local;
                _orbs[i].localScale = Vector3.one * (_orbSize * Mathf.Lerp(0.3f, 1f, _energy) * (1f + _impulse));
                if (_orbs[i].gameObject.activeSelf != visible)
                {
                    _orbs[i].gameObject.SetActive(visible);
                    if (visible)
                        _trails[i].Clear();
                }
            }
        }
    }
}
