using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Building
{
    /// <summary>
    /// 진화 치안 본부 광장의 떠 있는 순찰차 (사용자 2026-10-27 "실제 비행이 가능한 느낌 — 합성 때 쓴 추진체 이펙트 · 출동 연출",
    /// StationArtBuilder가 "PatrolCar_*" 부품마다 자동 배선 — 부품 원점 = 차 바닥 가운데, 앞 = +x):
    /// - 네 모서리 덕트 팬 아래로 합성 고리와 같은 추진 불꽃 (흰 심 → 청백, 아래로 뿜음), 살짝 떠올라 출렁이며 대기.
    /// - 가동 중이면 가끔 출동: 경광등이 좌우 번갈아 번쩍 → 추진을 세게 하며 떠오름 → 바깥쪽으로 돌아섬 → 앞으로 숙이고
    ///   가속하며 날아가 멀어지며 사라짐 → 잠시 뒤 같은 길로 돌아와 제자리에 내려앉음.
    /// 움직임은 게임 시간. 비활성이면 출동하지 않음. 고스트(배치 미리보기)에서는 아무것도 하지 않음.
    /// </summary>
    public sealed class PatrolCarFx : MonoBehaviour
    {
        [SerializeField] private Material _plumeMaterial;
        [Tooltip("덕트 팬 아래 (부품 기준)")]
        [SerializeField] private Vector3[] _thrusters =
        {
            new Vector3(0.046f, 0.0025f, 0.041f), new Vector3(0.046f, 0.0025f, -0.041f), new Vector3(-0.046f, 0.0025f, 0.041f), new Vector3(-0.046f, 0.0025f, -0.041f),
        };
        [Tooltip("지붕 경광등 왼쪽 · 오른쪽 (부품 기준)")]
        [SerializeField] private Vector3 _sirenA = new Vector3(-0.031f, 0.047f, -0.011f);
        [SerializeField] private Vector3 _sirenB = new Vector3(-0.031f, 0.047f, 0.011f);
        [SerializeField] private Color _plumeColor = new Color(0.55f, 0.8f, 1f, 1f);
        [Tooltip("대기 중 떠 있는 높이 (모델 위치 위로 — 추진 불꽃이 보이게)")]
        [SerializeField] private float _hover = 0.035f;
        [Tooltip("출동 간격 (게임 초, 최소 ~ 최대)")]
        [SerializeField] private Vector2 _dispatchEvery = new Vector2(25f, 50f);
        [SerializeField] private float _cruiseHeight = 0.35f;
        [SerializeField] private float _flyDistance = 4f;
        [SerializeField] private float _flyClimb = 0.7f;

        private const float AlertTime = 0.7f, LiftTime = 1.2f, TurnTime = 0.8f, FlyTime = 2.6f;

        private ParticleSystem[] _plumes;
        private ParticleSystem _siren;
        private ModuleView _view;
        private bool _live;
        private bool _built;
        private bool _flying;
        private Vector3 _home;
        private Quaternion _homeRotation;
        private float _timer;
        private float _seed;
        private float _thrust = -1f;

        public bool Flying => _flying;

        private void Start()
        {
            _view = GetComponentInParent<ModuleView>();
            _live = _view != null && _view.Module != null;
            if (_live)
                Play();
        }

        /// <summary>추진 불꽃을 켜고 대기 시작 (Start가 부름 — 확인용으로 미리보기에서 직접 불러도 됨).</summary>
        public void Play()
        {
            if (_built || _plumeMaterial == null)
                return;
            _built = true;
            _home = transform.localPosition;
            _homeRotation = transform.localRotation;
            _seed = Random.value * 100f;
            _timer = Random.Range(8f, _dispatchEvery.x);
            _plumes = new ParticleSystem[_thrusters.Length];
            for (int i = 0; i < _thrusters.Length; i++)
                _plumes[i] = Plume(_thrusters[i]);
            _siren = Siren();
            SetThrust(0f);
        }

        private void Update()
        {
            if (!_built || _flying)
                return;
            // 대기: 살짝 떠서 출렁임
            float bob = Mathf.Sin(Time.time * 1.7f + _seed) * 0.004f;
            transform.localPosition = _home + Vector3.up * (_hover + bob);
            transform.localRotation = _homeRotation * Quaternion.Euler(Mathf.Sin(Time.time * 1.1f + _seed) * 1.5f, 0f, Mathf.Sin(Time.time * 0.9f + _seed * 2f) * 1.5f);
            if (!_live || !_view.Operational)
                return;
            _timer -= Time.deltaTime;
            if (_timer <= 0f)
                Dispatch();
        }

        /// <summary>바로 출동 (가동 중이면 Update가 가끔 부름 — 확인 · 연출용으로 직접 불러도 됨).</summary>
        public void Dispatch()
        {
            if (!_built || _flying || !isActiveAndEnabled)
                return;
            StartCoroutine(Mission());
        }

        /// <summary>
        /// 날아가는 길 (집 기준 부모 좌표 오프셋): k = 0 → 떠오른 자리(cruise 높이), 1 → 멀리 (heading 쪽 distance, climb만큼 더 높이).
        /// 가속하며 멀어짐 (k² 거리) — 돌아올 때는 같은 길을 거꾸로 따라 감속하며 내려옴.
        /// </summary>
        public static Vector3 FlightOffset(float k, Vector3 heading, float cruise, float climb, float distance)
        {
            k = Mathf.Clamp01(k);
            return Vector3.up * (cruise + climb * k) + heading * (distance * k * k);
        }

        private IEnumerator Mission()
        {
            _flying = true;
            var heading = Heading();
            var face = Quaternion.Euler(0f, Mathf.Atan2(-heading.z, heading.x) * Mathf.Rad2Deg, 0f) * _homeRotation;
            var start = transform.localPosition;
            // 경광등 · 추진 세짐
            var sirenOn = StartCoroutine(SirenLoop());
            yield return Phase(AlertTime, k => SetThrust(0.4f * k));
            // 떠오름 (살짝 앞이 들림)
            yield return Phase(LiftTime, k =>
            {
                float e = Ease(k);
                SetThrust(Mathf.Lerp(0.4f, 1f, k));
                transform.localPosition = Vector3.Lerp(start, _home + Vector3.up * _cruiseHeight, e);
                transform.localRotation = _homeRotation * Quaternion.Euler(0f, 0f, 6f * Mathf.Sin(Mathf.PI * k));
            });
            // 바깥쪽으로 돌아섬
            yield return Phase(TurnTime, k => transform.localRotation = Quaternion.Slerp(_homeRotation, face, Ease(k)));
            // 앞으로 숙이고 가속하며 멀어져 사라짐
            yield return Phase(FlyTime, k =>
            {
                transform.localPosition = _home + FlightOffset(k, heading, _cruiseHeight, _flyClimb, _flyDistance);
                transform.localRotation = face * Quaternion.Euler(0f, 0f, -12f * Mathf.Sin(Mathf.PI * Mathf.Min(1f, k * 1.6f) * 0.5f));
                transform.localScale = Vector3.one * (1f - Ease(Mathf.InverseLerp(0.75f, 1f, k)));
            });
            SetThrust(-1f);
            StopCoroutine(sirenOn);
            yield return new WaitForSeconds(Random.Range(6f, 10f));
            // 같은 길로 돌아옴 (감속)
            sirenOn = StartCoroutine(SirenLoop());
            SetThrust(1f);
            yield return Phase(FlyTime, k =>
            {
                float r = 1f - k;
                transform.localPosition = _home + FlightOffset(r, heading, _cruiseHeight, _flyClimb, _flyDistance);
                transform.localRotation = face * Quaternion.Euler(0f, 0f, 10f * Mathf.Sin(Mathf.PI * Mathf.Min(1f, r * 1.6f) * 0.5f)); // 감속하며 앞이 들림
                transform.localScale = Vector3.one * Ease(Mathf.InverseLerp(0f, 0.25f, k));
            });
            yield return Phase(TurnTime, k => transform.localRotation = Quaternion.Slerp(face, _homeRotation, Ease(k)));
            // 내려앉음
            var top = transform.localPosition;
            yield return Phase(LiftTime, k =>
            {
                SetThrust(Mathf.Lerp(1f, 0.2f, k));
                transform.localPosition = Vector3.Lerp(top, _home + Vector3.up * _hover, Ease(k));
            });
            StopCoroutine(sirenOn);
            SetThrust(0f);
            transform.localScale = Vector3.one;
            _timer = Random.Range(_dispatchEvery.x, _dispatchEvery.y);
            _flying = false;
        }

        /// <summary>
        /// 출동 방향 (부모 좌표 가로): 차 앞(+x)쪽 + 조금 흔들림 — 떠올라 높이를 잡은 뒤라 앞 건물 위로 넘어감.
        /// (처음엔 모듈 가운데에서 바깥쪽이었는데, 치안 본부는 광장이 앞(카메라 쪽)이라 차가 화면으로 달려들어 어색했음)
        /// </summary>
        private Vector3 Heading()
        {
            var ahead = _homeRotation * Vector3.right;
            ahead.y = 0f;
            if (ahead.sqrMagnitude < 1e-4f)
                ahead = Vector3.right;
            return Quaternion.Euler(0f, Random.Range(-20f, 20f), 0f) * ahead.normalized;
        }

        private IEnumerator Phase(float seconds, System.Action<float> step)
        {
            float t = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime;
                step(Mathf.Clamp01(t / seconds));
                yield return null;
            }
        }

        private IEnumerator SirenLoop()
        {
            bool left = false;
            while (true)
            {
                left = !left;
                _siren.Emit(new ParticleSystem.EmitParams
                {
                    position = left ? _sirenA : _sirenB,
                    startSize = left ? 0.07f : 0.055f,
                    startColor = left ? new Color(0.45f, 0.65f, 1f, 1f) : Color.white,
                    startLifetime = 0.09f,
                    applyShapeToPosition = false,
                }, 1);
                yield return new WaitForSeconds(0.12f);
            }
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
                main.startSpeed = new ParticleSystem.MinMaxCurve(Mathf.Lerp(0.25f, 0.35f, level), Mathf.Lerp(0.35f, 0.6f, level));
            }
        }

        // 합성 고리 추진 불꽃(SynthesisFx.Plume)과 같은 모양 — 흰 심에서 청백으로 옅어지며 아래로
        private ParticleSystem Plume(Vector3 localPosition)
        {
            const float radius = 0.015f; // 덕트 팬 지름보다 조금 크게 — 게임 카메라 거리에서도 불꽃이 보이게
            var go = new GameObject("Plume");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.LookRotation(Vector3.down);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startSize = new ParticleSystem.MinMaxCurve(radius * 1.4f, radius * 2.2f);
            main.startColor = new ParticleSystem.MinMaxGradient(_plumeColor, new Color(0.85f, 0.95f, 1f, 1f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 60;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 6f;
            shape.radius = radius * 0.5f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.1f));
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(_plumeColor, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            color.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = _plumeMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        private ParticleSystem Siren()
        {
            var go = new GameObject("Siren");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local; // 차와 함께 움직임
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startSpeed = 0f; // 경광등 자리에 머묾 (기본 속도 5면 날아감)
            main.maxParticles = 8;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
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
