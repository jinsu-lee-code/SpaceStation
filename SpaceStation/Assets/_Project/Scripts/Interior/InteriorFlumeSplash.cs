using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 화물 환적장 내부 큰 낙하 물보라 (바깥 모델 <see cref="SpaceStation.Building.FlumeRideFx"/>의 물보라를 사람 크기로): 물안개 · 물방울 · 은하수 보라 반짝이 · 물결 고리 셋,
    /// 관람석 쪽으로 날리는 물방울 (관람석 1 ~ 2줄에 떨어짐) · 탄 사람 앞 물안개. 발광 구 · 고정 물보라 메시 없음 — 입자만.
    /// 실제 후룸 낙하처럼 (사용자 2026-11-10 "물보라가 확 튀면 좋겠어"): 뱃머리 양옆으로 높이 치솟는 물날개 둘 (길게 늘인 물줄기 입자) · 앞으로 쏟아지는 물막 ·
    /// 천천히 퍼지며 떠오르는 물안개 구름 — 0.25초 동안 세 번에 나눠 터짐.
    /// 아무 보트나 물보라 자리를 지나면 <see cref="Splash"/> (빈 보트도). 위치는 템플릿 루트 기준.
    /// </summary>
    public sealed class InteriorFlumeSplash : MonoBehaviour
    {
        private const float Scale = 8f;   // 바깥 모델 × 8

        [SerializeField] private Material _sparkMaterial;
        [SerializeField] private Material _rippleMaterial;
        [Tooltip("관람석 쪽 물방울이 떨어지는 곳 (템플릿 x0, z0, x1, z1) · 높이")]
        [SerializeField] private Rect _stand = new Rect(11.3f, -2.2f, 1.3f, 1.9f);
        [SerializeField] private float _standY = -0.7f;

        private ParticleSystem _mist, _drops, _spray, _ripple, _sheet, _cloud;

        public void Configure(Material spark, Material ripple)
        {
            _sparkMaterial = spark;
            _rippleMaterial = ripple;
        }

        private void Prepare()
        {
            if (_mist != null || _sparkMaterial == null)
                return;
            _mist = Make("SplashMist", _sparkMaterial, 3.5f, 260);    // 물안개 위 끝 ≈ 0.67
            _drops = Make("SplashDrops", _sparkMaterial, 2.9f, 200);  // 물기둥 ≈ 1.23
            _spray = Make("SplashSpray", _sparkMaterial, 1f, 80);     // 관람석으로
            _ripple = Make("SplashRipple", _rippleMaterial != null ? _rippleMaterial : _sparkMaterial, 0f, 12);
            _sheet = Make("SplashSheet", _sparkMaterial, 1f, 900);    // 물날개 · 물막 (실제 중력)
            var sr = _sheet.GetComponent<ParticleSystemRenderer>();
            sr.renderMode = ParticleSystemRenderMode.Stretch;           // 날아가는 방향으로 늘인 물줄기
            sr.velocityScale = 0.05f;
            sr.lengthScale = 1.6f;
            _cloud = Make("SplashCloud", _sparkMaterial, -0.03f, 120);  // 천천히 떠오르는 물안개 구름
            var cs = _cloud.sizeOverLifetime;
            cs.enabled = true;
            cs.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(1f, 1.6f)));
            var r = _ripple.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            var size = _ripple.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.15f, 2.5f, 2.5f), new Keyframe(1f, 1f, 0.3f, 0.3f)));
        }

        /// <summary>물보라: at = 물 위 자리, dir = 보트가 가는 방향 (템플릿 기준).</summary>
        public void Splash(Vector3 at, Vector3 dir)
        {
            if (!isActiveAndEnabled)
                return;
            Prepare();
            if (_mist == null)
                return;
            var world = transform.TransformPoint(at);
            dir.y = 0f;
            var fwd = transform.TransformDirection(dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.back);
            var side = Vector3.Cross(Vector3.up, fwd);
            const float spread = 110f * Mathf.Deg2Rad;
            for (int i = 0; i < 90; i++)
            {
                float ph = Random.Range(-spread, spread);
                float lobe = 0.6f + 0.4f * Mathf.Sin(Mathf.Abs(ph) / spread * Mathf.PI);   // 양옆이 더 높은 두 봉우리
                var h = fwd * Mathf.Cos(ph) + side * Mathf.Sin(ph);
                bool drop = i % 2 == 1;
                var c = drop ? Color.Lerp(new Color(0.85f, 0.95f, 1f), Color.white, Random.value) : new Color(0.75f, 0.88f, 1f, 0.5f);
                if (i % 7 == 0)
                    c = new Color(0.78f, 0.55f, 1f, 0.9f);   // 은하수 보라 반짝이
                (drop ? _drops : _mist).Emit(new ParticleSystem.EmitParams
                {
                    position = world + h * 0.03f * Scale,
                    velocity = (h * Random.Range(0.25f, drop ? 0.7f : 0.5f) + Vector3.up * Random.Range(0.55f, drop ? 1.05f : 0.85f) * lobe) * Scale,
                    startSize = (drop ? Random.Range(0.014f, 0.026f) : Random.Range(0.04f, 0.08f)) * Scale,
                    startLifetime = drop ? Random.Range(0.6f, 1f) : Random.Range(0.7f, 1.1f),
                    startColor = c,
                    applyShapeToPosition = false,
                }, 1);
            }
            // 관람석 날림: 꼭대기가 물 위 0.6을 넘지 않는 포물선으로 관람석 1 ~ 2줄에 떨어짐
            float g = 9.81f;
            for (int i = 0; i < 40; i++)
            {
                var land = transform.TransformPoint(new Vector3(Random.Range(_stand.xMin, _stand.xMax), _standY, Random.Range(_stand.yMin, _stand.yMax)));
                float apex = Random.Range(0.35f, 0.6f);
                float vy = Mathf.Sqrt(2f * g * apex);
                float fall = Mathf.Max(0.05f, apex + world.y - land.y);
                float t = vy / g + Mathf.Sqrt(2f * fall / g);
                var flat = land - world;
                flat.y = 0f;
                _spray.Emit(new ParticleSystem.EmitParams
                {
                    position = world + Vector3.up * 0.1f,
                    velocity = flat / t + Vector3.up * vy,
                    startSize = Random.Range(0.05f, 0.12f),
                    startLifetime = t + 0.15f,
                    startColor = i % 6 == 0 ? new Color(0.78f, 0.55f, 1f, 0.8f) : new Color(0.8f, 0.92f, 1f, 0.75f),
                    applyShapeToPosition = false,
                }, 1);
            }
            StartCoroutine(Ripples(world + Vector3.up * 0.02f));
            StartCoroutine(Burst(world, fwd, side));
        }

        /// <summary>큰 물보라 본체: 뱃머리 양옆 물날개 (위 1.6 ~ 2.4 m, 옆 1.5 ~ 3 m) · 앞 물막 · 물안개 구름을 세 번에 나눠.</summary>
        private IEnumerator Burst(Vector3 world, Vector3 fwd, Vector3 side)
        {
            for (int wave = 0; wave < 3; wave++)
            {
                float k = 1f - 0.3f * wave;   // 첫 번이 가장 셈
                for (int s = -1; s <= 1; s += 2)
                {
                    for (int i = 0; i < 110; i++)
                    {
                        float out_ = Random.Range(1.6f, 3.4f) * k;
                        var v = side * (s * out_) + Vector3.up * Random.Range(4.2f, 6.8f) * k + fwd * Random.Range(-0.4f, 1.6f);
                        _sheet.Emit(new ParticleSystem.EmitParams
                        {
                            position = world + side * (s * Random.Range(0.35f, 0.6f)) + fwd * Random.Range(-0.3f, 0.6f),
                            velocity = v,
                            startSize = Random.Range(0.035f, 0.09f),
                            startLifetime = Random.Range(1.1f, 1.6f),
                            startColor = i % 9 == 0 ? new Color(0.8f, 0.6f, 1f, 0.9f) : new Color(0.86f, 0.95f, 1f, Random.Range(0.55f, 0.9f)),
                            applyShapeToPosition = false,
                        }, 1);
                    }
                }
                for (int i = 0; i < 90; i++)
                {
                    // 앞으로 쏟아지는 물막 (낮고 멀리)
                    float a = Random.Range(-0.7f, 0.7f);
                    var h = fwd * Mathf.Cos(a) + side * Mathf.Sin(a);
                    _sheet.Emit(new ParticleSystem.EmitParams
                    {
                        position = world + fwd * 0.9f,
                        velocity = h * Random.Range(2.2f, 4.2f) * k + Vector3.up * Random.Range(2.4f, 4.0f) * k,
                        startSize = Random.Range(0.03f, 0.07f),
                        startLifetime = Random.Range(0.9f, 1.3f),
                        startColor = new Color(0.86f, 0.95f, 1f, 0.8f),
                        applyShapeToPosition = false,
                    }, 1);
                }
                for (int i = 0; i < 30; i++)
                {
                    var p = world + side * Random.Range(-1.6f, 1.6f) + fwd * Random.Range(-0.5f, 1.5f) + Vector3.up * Random.Range(0.1f, 0.9f);
                    _cloud.Emit(new ParticleSystem.EmitParams
                    {
                        position = p,
                        velocity = (p - world).normalized * Random.Range(0.3f, 0.8f) + Vector3.up * Random.Range(0.2f, 0.6f),
                        startSize = Random.Range(0.5f, 1.0f),
                        startLifetime = Random.Range(1.6f, 2.6f),
                        startColor = new Color(0.82f, 0.92f, 1f, Random.Range(0.18f, 0.3f)),
                        applyShapeToPosition = false,
                    }, 1);
                }
                yield return new WaitForSecondsRealtime(0.12f);
            }
        }

        /// <summary>탄 사람 눈앞 물안개 (0.4초).</summary>
        public void Mist(Vector3 worldAt)
        {
            if (!isActiveAndEnabled)
                return;
            Prepare();
            if (_mist == null)
                return;
            for (int i = 0; i < 18; i++)
            {
                // 눈앞이라 작고 옅게 (크면 화면을 흰 덩어리로 가림 — 플레이 확인 2026-11-10)
                _mist.Emit(new ParticleSystem.EmitParams
                {
                    position = worldAt + Random.insideUnitSphere * 0.4f,
                    velocity = Random.insideUnitSphere * 0.5f + Vector3.up * 0.3f,
                    startSize = Random.Range(0.02f, 0.05f),
                    startLifetime = Random.Range(0.3f, 0.45f),
                    startColor = new Color(0.8f, 0.92f, 1f, 0.25f),
                    applyShapeToPosition = false,
                }, 1);
            }
        }

        private IEnumerator Ripples(Vector3 at)
        {
            for (int i = 0; i < 3; i++)
            {
                _ripple.Emit(new ParticleSystem.EmitParams
                {
                    position = at,
                    startSize = (0.32f + 0.08f * i) * Scale,
                    startLifetime = 1.2f,
                    startColor = new Color(0.8f, 0.92f, 1f, 0.7f - 0.15f * i),
                    applyShapeToPosition = false,
                }, 1);
                yield return new WaitForSecondsRealtime(0.18f);
            }
        }

        private ParticleSystem Make(string name, Material material, float gravity, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.useUnscaledTime = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
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
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
            color.color = grad;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }
    }
}
