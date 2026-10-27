using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Building
{
    /// <summary>
    /// 진화 화물 환적장 은하수 후룸라이드 (사용자 2026-10-27 "물자가 돌아다니게 · 큰 낙하 직후 고정된 형태가 아니라 진짜 물보라",
    /// StationArtBuilder가 자동 배선): 화물 보트들이 물길 한 바퀴(항구 못 → 오른쪽 물길 → 머리핀 → 체인 리프트 → 능선 → 정상 터널 →
    /// 큰 낙하 → 물보라 못 → 앞 · 왼쪽 물길 → 항구 부두 앞)를 쉬지 않고 돈다. 오르막은 체인이 일정하게 끌어 올리고, 내리막은 높이만큼
    /// 빨라졌다가 평지에서 물에 밀려 느려짐. 보트가 큰 낙하 바닥에 닿을 때마다 입자 물보라 (앞 · 양옆으로 솟았다 떨어지는 물안개 ·
    /// 물방울 · 은하수 보라 반짝이 + 퍼지는 물결 고리 셋). 모델의 구운 보트 · 고정 물보라는 Blender에서 지웠다 (cargo_flume_split).
    /// 보트 위치 = 물길 중심 + 위로 0.008 (모델 기준). 가동 중에만 움직임 (멈추면 그 자리에 섬). 고스트에서는 빌더가 놓은 자리 그대로.
    /// </summary>
    public sealed class FlumeRideFx : MonoBehaviour
    {
        [Tooltip("보트 (StationArtBuilder가 모델의 템플릿 보트 셋을 복제해 넣음)")]
        [SerializeField] private Transform[] _boats;
        [Tooltip("물길 한 바퀴 (모델 기준 물길 중심, 닫힌 고리)")]
        [SerializeField] private Vector3[] _path;
        [SerializeField] private Material _sparkMaterial;  // 물안개 · 물방울 (가산)
        [SerializeField] private Material _rippleMaterial; // 물결 고리
        [SerializeField] private float _baseSpeed = 0.3f;
        [SerializeField] private float _liftSpeed = 0.2f;
        [Tooltip("내리막 가속 (모델 단위 / 초²)")]
        [SerializeField] private float _gravity = 1.6f;
        [Tooltip("평지에서 기본 속도로 돌아오는 거리")]
        [SerializeField] private float _drag = 0.18f;
        [SerializeField] private float _boatLift = 0.008f;

        private float[] _time;  // 점마다 누적 시간 (고리 끝 = 한 바퀴)
        private float _loopTime;
        private int _splashIndex = -1;
        private float _clock;
        private float[] _boatPhase;
        private Quaternion[] _boatRotation;
        private ModuleView _view;
        private bool _live;
        private ParticleSystem _mist, _drops, _ripple;

        public float LoopTime { get { Prepare(); return _loopTime; } }
        public int SplashIndex { get { Prepare(); return _splashIndex; } }

        private void Start()
        {
            _view = GetComponentInParent<ModuleView>();
            _live = _view != null && _view.Module != null;
            if (_live)
                Play();
        }

        /// <summary>물보라 입자를 만들고 운행 시작 (Start가 부름 — 확인용으로 미리보기에서 직접 불러도 됨).</summary>
        public void Play()
        {
            if (_mist != null || _sparkMaterial == null)
                return;
            Prepare();
            _mist = Splash("SplashMist", _sparkMaterial, 0.12f, 220);
            _drops = Splash("SplashDrops", _sparkMaterial, 0.2f, 160);
            _ripple = Splash("SplashRipple", _rippleMaterial != null ? _rippleMaterial : _sparkMaterial, 0f, 12);
            var r = _ripple.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            var size = _ripple.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.15f, 2.5f, 2.5f), new Keyframe(1f, 1f, 0.3f, 0.3f)));
            if (!_live)
                _clock = 0f;
        }

        private void Update()
        {
            if (_path == null || _path.Length < 2 || _boats == null || _mist == null)
                return;
            if (_live && !_view.Operational)
                return;
            float before = _clock;
            _clock += Time.deltaTime;
            Apply(_clock, before);
        }

        /// <summary>
        /// 점마다 속도: 오르막(기울기 &gt; 0.05) = 체인 리프트 일정 속도, 내리막 = 높이를 잃은 만큼 빨라짐 (v² += 2g·낙차),
        /// 평지 = drag 거리 만큼에 걸쳐 기본 속도로 돌아옴. 고리라 첫 점 속도 = 기본.
        /// </summary>
        public static float[] Speeds(Vector3[] path, float baseSpeed, float liftSpeed, float gravity, float drag)
        {
            var v = new float[path.Length];
            float speed = baseSpeed;
            v[0] = speed;
            for (int i = 1; i < path.Length; i++)
            {
                var d = path[i] - path[i - 1];
                float ds = d.magnitude;
                float slope = ds > 1e-6f ? d.y / ds : 0f;
                if (slope > 0.05f)
                    speed = liftSpeed;
                else if (slope < -0.05f)
                    speed = Mathf.Sqrt(speed * speed - 2f * gravity * d.y);
                else
                    speed = baseSpeed + (speed - baseSpeed) * Mathf.Exp(-ds / drag);
                v[i] = speed;
            }
            return v;
        }

        private void Prepare()
        {
            if (_time != null || _path == null || _path.Length < 2)
                return;
            int n = _path.Length;
            var speed = Speeds(_path, _baseSpeed, _liftSpeed, _gravity, _drag);
            _time = new float[n + 1];
            float fastest = 0f;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                float ds = Vector3.Distance(_path[i], _path[j]);
                _time[i + 1] = _time[i] + ds / Mathf.Max(0.05f, (speed[i] + speed[j]) * 0.5f);
                if (speed[i] > fastest)
                {
                    fastest = speed[i];
                    _splashIndex = i; // 가장 빠른 곳 = 큰 낙하 바닥
                }
            }
            _loopTime = _time[n];
            int count = _boats != null ? _boats.Length : 0;
            _boatPhase = new float[count];
            _boatRotation = new Quaternion[count];
            for (int b = 0; b < count; b++)
                _boatPhase[b] = _loopTime * b / Mathf.Max(1, count);
        }

        /// <summary>time 때 보트를 제자리에 놓음 (before = 직전 시간 — 그 사이 큰 낙하 바닥을 지난 보트가 있으면 물보라). 빌더가 0으로 불러 처음 자리에 놓음.</summary>
        public void Apply(float time, float before = float.NaN)
        {
            Prepare();
            if (_time == null || _boats == null)
                return;
            bool first = float.IsNaN(before);
            for (int b = 0; b < _boats.Length; b++)
            {
                if (_boats[b] == null)
                    continue;
                float t = Mathf.Repeat(time + _boatPhase[b], _loopTime);
                Sample(t, out var pos, out var fwd);
                var up = Vector3.ProjectOnPlane(Vector3.up, fwd).normalized;
                var side = Vector3.Cross(fwd, up);
                var rot = Quaternion.LookRotation(side, up); // 보트 로컬 x = 앞 (Cross(up, side) = fwd)
                // 물길 이음매(항구 → 물길 첫 점)의 꺾임은 천천히 돌아섬
                _boatRotation[b] = first ? rot : Quaternion.Slerp(_boatRotation[b], rot, 1f - Mathf.Exp(-Time.deltaTime * 10f));
                _boats[b].localPosition = pos + Vector3.up * _boatLift;
                _boats[b].localRotation = _boatRotation[b];
                if (!first && _splashIndex >= 0)
                {
                    float tb = Mathf.Repeat(before + _boatPhase[b], _loopTime);
                    float ts = _time[_splashIndex];
                    bool crossed = tb <= t ? tb < ts && ts <= t : ts > tb || ts <= t;
                    if (crossed)
                        Splash(_path[_splashIndex], _path[(_splashIndex + 1) % _path.Length] - _path[_splashIndex]);
                }
            }
        }

        private void Sample(float t, out Vector3 pos, out Vector3 fwd)
        {
            int lo = 0, hi = _path.Length;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (_time[mid] <= t) lo = mid;
                else hi = mid;
            }
            int n = _path.Length;
            float k = Mathf.InverseLerp(_time[lo], _time[lo + 1], t);
            var a = _path[lo];
            var c = _path[(lo + 1) % n];
            pos = Vector3.Lerp(a, c, k);
            // 앞 방향은 앞뒤 두 점씩 넓게 보아 매끈하게
            fwd = (_path[(lo + 2) % n] - _path[(lo - 1 + n) % n]).normalized;
            if (fwd.sqrMagnitude < 1e-6f)
                fwd = Vector3.right;
        }

        /// <summary>큰 낙하 물보라 (모델 기준 at, 보트가 가던 방향 dir): 앞 · 양옆 부채꼴로 솟았다 떨어지는 물안개 · 물방울 · 보라 반짝이 + 물결 고리 셋.</summary>
        public void Splash(Vector3 at, Vector3 dir)
        {
            if (_mist == null || !isActiveAndEnabled)
                return;
            var world = transform.TransformPoint(at);
            dir.y = 0f;
            var fwd = transform.TransformDirection(dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.back);
            var side = Vector3.Cross(Vector3.up, fwd);
            float scale = transform.lossyScale.x;
            const float spread = 110f * Mathf.Deg2Rad;
            for (int i = 0; i < 70; i++)
            {
                float ph = Random.Range(-spread, spread);
                float lobe = 0.6f + 0.4f * Mathf.Sin(Mathf.Abs(ph) / spread * Mathf.PI); // 양옆이 더 높은 두 봉우리
                var h = fwd * Mathf.Cos(ph) + side * Mathf.Sin(ph);
                bool drop = i % 2 == 1;
                var c = drop ? Color.Lerp(new Color(0.85f, 0.95f, 1f), Color.white, Random.value) : new Color(0.75f, 0.88f, 1f, 0.5f);
                if (i % 7 == 0)
                    c = new Color(0.78f, 0.55f, 1f, 0.9f); // 은하수 보라 반짝이
                (drop ? _drops : _mist).Emit(new ParticleSystem.EmitParams
                {
                    position = world + h * 0.03f * scale,
                    velocity = (h * Random.Range(0.25f, drop ? 0.7f : 0.5f) + Vector3.up * Random.Range(0.55f, drop ? 1.05f : 0.85f) * lobe) * scale,
                    startSize = (drop ? Random.Range(0.014f, 0.026f) : Random.Range(0.04f, 0.08f)) * scale,
                    startLifetime = drop ? Random.Range(0.6f, 1f) : Random.Range(0.7f, 1.1f),
                    startColor = c,
                    applyShapeToPosition = false,
                }, 1);
            }
            StartCoroutine(Ripples(world + Vector3.up * 0.002f * scale, scale));
        }

        private IEnumerator Ripples(Vector3 at, float scale)
        {
            for (int i = 0; i < 3; i++)
            {
                _ripple.Emit(new ParticleSystem.EmitParams
                {
                    position = at,
                    startSize = (0.32f + 0.08f * i) * scale,
                    startLifetime = 1.2f,
                    startColor = new Color(0.8f, 0.92f, 1f, 0.7f - 0.15f * i),
                    applyShapeToPosition = false,
                }, 1);
                yield return new WaitForSeconds(0.18f);
            }
        }

        private ParticleSystem Splash(string name, Material material, float gravity, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f; // 속도를 안 준 Emit(물결 고리)이 기본 속도 5로 날아가지 않게
            main.gravityModifier = gravity;
            main.maxParticles = max;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var size = ps.sizeOverLifetime;
            size.enabled = gravity > 0f;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(0.4f, 1.2f), new Keyframe(1f, 0.9f)));
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
            color.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }
    }
}
