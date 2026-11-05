using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 2단계 정련 콤비나트 "작은 해 용광로": 해 둘레를 도는 쇳물 국자 셋 (받침 팔이 돎), 정해진 주형 위를 지날 때 국자가 바깥으로 기울어 쇳물 줄기를 붓고 불꽃이 튐
    /// (<see cref="InteriorFx.Spark"/> — 줄기가 닿는 주형에서만), 표면 홍염 고리가 천천히 돎, 꼭대기 배출구에서 금빛 불꽃 분수가 돔을 따라 흘러내림 (바깥 모듈 SparkEmitterFx의 분수를 내부 크기로).
    /// 주형 자리가 셋이고 국자 사이 각과 달라, 붓는 때가 어긋남. 모듈이 멈추면(비활성 · 전력 부족) 그 자리에 멈추고 분수도 꺼짐. 템플릿 빌더가 configure에서 붙임.
    /// </summary>
    public sealed class InteriorSunForge : InteriorRoomDisplay
    {
        [Tooltip("도는 받침 (피벗 = 돔 가운데 축). 국자 · 줄기 피벗이 이 아래")]
        [SerializeField] private Transform _carousel;
        [Tooltip("국자 피벗 (기울기 축 위)")]
        [SerializeField] private Transform[] _ladles;
        [Tooltip("쇳물 줄기 피벗 (맨 위, 모델 높이 비율로 늘임)")]
        [SerializeField] private Transform[] _streams;
        [Tooltip("국자 쉬는 자세 방위 (도, x = cos · z = sin)")]
        [SerializeField] private float[] _ladleAzimuth;
        [SerializeField] private Transform _flares;
        [Tooltip("붓는 주형 방위 (도)")]
        [SerializeField] private float[] _pourAzimuth;
        [Tooltip("불꽃이 튀는 주형 윗면: 가운데 반지름 · 높이 (템플릿 기준)")]
        [SerializeField] private float _moldRadius;
        [SerializeField] private float _moldY;
        [SerializeField] private Vector3 _center;
        [Tooltip("불꽃 분수 자리 (템플릿 기준)")]
        [SerializeField] private Vector3 _fountainAt;
        [SerializeField] private Material _spark;
        [SerializeField] private Material _fountainSpark;
        [SerializeField] private Material _fountainTrail;
        [SerializeField] private float _lapSeconds = 42f;
        [SerializeField] private float _flareSeconds = 90f;
        [SerializeField] private float _tiltDegrees = 36f;
        [Tooltip("주형 앞뒤로 기울기 시작하는 각 (도)")]
        [SerializeField] private float _pourWindow = 15f;
        [SerializeField] private float _sparkEvery = 0.28f;

        private float _angle;
        private float _flareAngle;
        private Vector3[] _tiltAxis;
        private float[] _sparkTimer;
        private Quaternion _flareRest;
        private ParticleSystem _fountain;
        private bool _emitting = true;

        public void Configure(Transform carousel, Transform[] ladles, Transform[] streams, float[] ladleAzimuth, Transform flares, float[] pourAzimuth,
            float moldRadius, float moldY, Vector3 center, Vector3 fountainAt, Material spark, Material fountainSpark, Material fountainTrail)
        {
            _carousel = carousel;
            _ladles = ladles;
            _streams = streams;
            _ladleAzimuth = ladleAzimuth;
            _flares = flares;
            _pourAzimuth = pourAzimuth;
            _moldRadius = moldRadius;
            _moldY = moldY;
            _center = center;
            _fountainAt = fountainAt;
            _spark = spark;
            _fountainSpark = fountainSpark;
            _fountainTrail = fountainTrail;
        }

        private void Awake()
        {
            if (_carousel == null)
                return;
            _tiltAxis = new Vector3[_ladles.Length];
            _sparkTimer = new float[_ladles.Length];
            for (int k = 0; k < _ladles.Length; k++)
            {
                float a = _ladleAzimuth[k] * Mathf.Deg2Rad;
                // + 각 = 바깥쪽 입이 내려감 (받침 팔 기준 고정 축)
                _tiltAxis[k] = Vector3.Cross(Vector3.up, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
                _streams[k].localScale = new Vector3(1f, 0f, 1f);
                _streams[k].gameObject.SetActive(false);
            }
            if (_flares != null)
                _flareRest = _flares.localRotation;
            if (_fountainSpark != null)
                _fountain = BuildFountain();
        }

        protected override void Refresh()
        {
        }

        private void Update()
        {
            if (_carousel == null)
                return;
            bool running = !Polled || Active;
            if (_fountain != null && running != _emitting)
            {
                _emitting = running;
                var emission = _fountain.emission;
                emission.enabled = running;
            }
            if (!running)
                return;
            float dt = Time.deltaTime;
            _angle = Mathf.Repeat(_angle + dt * 360f / _lapSeconds, 360f);
            _carousel.localRotation = Quaternion.AngleAxis(_angle, Vector3.up);
            if (_flares != null)
            {
                _flareAngle = Mathf.Repeat(_flareAngle + dt * 360f / _flareSeconds, 360f);
                _flares.localRotation = _flareRest * Quaternion.AngleAxis(-_flareAngle, Vector3.up);
            }
            for (int k = 0; k < _ladles.Length; k++)
                UpdateLadle(k, dt);
        }

        private void UpdateLadle(int k, float dt)
        {
            // 위에서 볼 때 + 회전 = 방위 감소 (x = cos, z = sin 기준)
            float az = _ladleAzimuth[k] - _angle;
            float best = 999f;
            float station = 0f;
            foreach (float pour in _pourAzimuth)
            {
                float d = Mathf.Abs(Mathf.DeltaAngle(az, pour));
                if (d < best)
                {
                    best = d;
                    station = pour;
                }
            }
            float u = Mathf.Clamp01(1f - best / _pourWindow);
            float tilt = u * u * (3f - 2f * u);
            _ladles[k].localRotation = Quaternion.AngleAxis(tilt * _tiltDegrees, _tiltAxis[k]);
            // 줄기: 많이 기울었을 때만
            float flow = Mathf.Clamp01((tilt - 0.72f) / 0.18f);
            bool pouring = flow > 0f;
            if (_streams[k].gameObject.activeSelf != pouring)
                _streams[k].gameObject.SetActive(pouring);
            if (pouring)
                _streams[k].localScale = new Vector3(1f, flow, 1f);
            if (flow < 1f)
            {
                _sparkTimer[k] = 0f;
                return;
            }
            _sparkTimer[k] -= dt;
            if (_sparkTimer[k] > 0f)
                return;
            _sparkTimer[k] = _sparkEvery;
            float a = az * Mathf.Deg2Rad;
            var at = transform.TransformPoint(new Vector3(_center.x + _moldRadius * Mathf.Cos(a), _moldY, _center.z + _moldRadius * Mathf.Sin(a)));
            InteriorFx.Spark(transform, at, transform.up, _spark, 6, 12, 1.2f);
        }

        /// <summary>바깥 모듈 불꽃 분수를 내부 크기로: 낮게 솟아 돔 둘레로 흘러내리며 반짝이다 꺼짐 (칸 위 경계를 넘지 않게 낮게).</summary>
        private ParticleSystem BuildFountain()
        {
            var go = new GameObject("SparkFountain");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = _fountainAt;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.duration = 1f;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.8f, 0.42f), new Color(1f, 0.97f, 0.85f));
            main.gravityModifier = 0.2f;
            main.maxParticles = 320;
            var emission = ps.emission;
            emission.rateOverTime = 90f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.rotation = new Vector3(-90f, 0f, 0f);   // 원뿔 기본 방향 +z → 위
            shape.angle = 62f;
            shape.radius = 0.3f;
            shape.arcMode = ParticleSystemShapeMultiModeValue.Random;
            shape.arcSpread = 0.1f;   // 열 갈래
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 1.4f;
            noise.sizeAmount = 0.6f;  // 반짝임
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.7f, 0.8f), new Keyframe(1f, 0f)));
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.85f, 0.5f), 0.45f), new GradientColorKey(new Color(1f, 0.55f, 0.25f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.02f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            color.color = new ParticleSystem.MinMaxGradient(g);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = _fountainSpark;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (_fountainTrail != null)
            {
                var tr = ps.trails;
                tr.enabled = true;
                tr.ratio = 0.6f;
                tr.lifetime = new ParticleSystem.MinMaxCurve(0.2f);
                tr.minVertexDistance = 0.04f;
                tr.dieWithParticles = true;
                tr.inheritParticleColor = true;
                tr.widthOverTrail = new ParticleSystem.MinMaxCurve(0.7f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
                r.trailMaterial = _fountainTrail;
            }
            ps.Play();
            return ps;
        }
    }
}
