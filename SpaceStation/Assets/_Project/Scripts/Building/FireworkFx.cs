using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Building
{
    /// <summary>
    /// 진화 요새 포대(별 요새) 불꽃놀이 연출 (사용자 2026-10-26, StationArtBuilder가 자동 배선).
    /// - 포탑 요격: MeteorFx가 포탑 모듈에서 이 컴포넌트를 찾으면 공중 폭발 대신 Burst — 별자리 불꽃
    ///   (빛 점이 꼬리를 끌며 별자리 별 자리로 퍼져 반짝이고, 별을 잇는 빛줄이 차례로 그어진 뒤 불티가 되어 사라짐).
    ///   별자리 5종(북두칠성 · 카시오페이아 · 오리온 · 북십자 · 전갈자리) 중 무작위, 바로 앞과 같은 것은 피함.
    /// - 박격포대: 가동 중이면 가끔 뒤 능보 포구에서 불꽃이 올라가 터짐 (별자리 또는 둥근 불꽃).
    /// 별자리 면은 카메라를 향하고 위쪽은 월드 위. 시간은 게임 시간. 비활성 · 고스트에서는 박격포대가 쉰다.
    /// </summary>
    public sealed class FireworkFx : MonoBehaviour
    {
        /// <summary>별자리: 별 위치(가로 · 세로, 대략 −1~1)와 잇는 빛줄(별 번호 쌍).</summary>
        public sealed class Constellation
        {
            public string Name;
            public Vector2[] Stars;
            public int[] Lines; // 두 개씩 한 줄
        }

        public static readonly Constellation[] Sky =
        {
            new Constellation
            {
                Name = "북두칠성",
                Stars = new[] { new Vector2(0.62f, 0.36f), new Vector2(0.62f, -0.12f), new Vector2(0.15f, -0.22f), new Vector2(0.1f, 0.2f), new Vector2(-0.22f, 0.3f), new Vector2(-0.55f, 0.34f), new Vector2(-0.92f, 0.1f) },
                Lines = new[] { 0, 1, 1, 2, 2, 3, 3, 0, 3, 4, 4, 5, 5, 6 },
            },
            new Constellation
            {
                Name = "카시오페이아",
                Stars = new[] { new Vector2(-0.92f, 0.3f), new Vector2(-0.46f, -0.28f), new Vector2(0f, 0.08f), new Vector2(0.46f, -0.32f), new Vector2(0.9f, 0.26f) },
                Lines = new[] { 0, 1, 1, 2, 2, 3, 3, 4 },
            },
            new Constellation
            {
                Name = "오리온",
                Stars = new[] { new Vector2(-0.48f, 0.78f), new Vector2(0.42f, 0.66f), new Vector2(-0.16f, 0.05f), new Vector2(0f, 0f), new Vector2(0.16f, -0.05f), new Vector2(-0.38f, -0.78f), new Vector2(0.48f, -0.72f) },
                Lines = new[] { 0, 1, 0, 2, 1, 4, 2, 3, 3, 4, 2, 5, 4, 6 },
            },
            new Constellation
            {
                Name = "북십자 (백조자리)",
                Stars = new[] { new Vector2(0f, 0.88f), new Vector2(0f, 0.26f), new Vector2(0.02f, -0.88f), new Vector2(-0.62f, 0.12f), new Vector2(0.58f, 0.42f) },
                Lines = new[] { 0, 1, 1, 2, 3, 1, 1, 4 },
            },
            new Constellation
            {
                Name = "전갈자리", // 남십자는 북십자와 같은 십자라 겹쳐 보여 교체 (사용자 2026-10-26 "좀 더 다양하게")
                Stars = new[]
                {
                    new Vector2(-0.86f, 0.8f), new Vector2(-0.8f, 0.52f), new Vector2(-0.72f, 0.26f), new Vector2(-0.52f, 0.46f), new Vector2(-0.32f, 0.3f), new Vector2(-0.15f, 0.1f),
                    new Vector2(0f, -0.2f), new Vector2(0.08f, -0.5f), new Vector2(0.42f, -0.78f), new Vector2(0.78f, -0.6f), new Vector2(0.72f, -0.32f),
                },
                Lines = new[] { 0, 1, 1, 2, 1, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10 },
            },
        };

        [SerializeField] private Material _sparkMaterial;   // 가산 빛 점 (별 · 불티 · 꼬리)
        [SerializeField] private Material _lineMaterial;    // 별을 잇는 빛줄 · 입자 꼬리
        [Tooltip("박격포대 포구 (모델 기준 위치)")]
        [SerializeField] private Vector3[] _mortars;
        [Tooltip("박격포대 불꽃 평균 간격 (게임 초)")]
        [SerializeField] private float _showInterval = 22f;
        [Tooltip("요격 별자리 폭")]
        [SerializeField] private float _burstSize = 1.8f;
        [Tooltip("박격포대 별자리 폭")]
        [SerializeField] private float _showSize = 1.2f;

        private const float Total = 2.3f;   // 별자리 불꽃 길이
        private const float Arrive = 0.38f; // 별이 자리에 닿는 시간
        private static readonly Color StarColor = new Color(1f, 0.9f, 0.62f, 1f);
        private static readonly Color LineColor = new Color(0.72f, 0.9f, 1f, 1f);
        private static readonly Color[] Palette =
        {
            new Color(1f, 0.55f, 0.5f), new Color(1f, 0.84f, 0.4f), new Color(0.75f, 0.9f, 1f), new Color(1f, 1f, 0.92f),
        };

        private ModuleView _view;
        private bool _live;
        private float _timer;
        private int _last = -1;

        private void Start()
        {
            _view = GetComponentInParent<ModuleView>();
            _live = _view != null && _view.Module != null;
            _timer = _showInterval * Random.Range(0.3f, 1f);
        }

        /// <summary>별자리 무작위 (바로 앞과 같은 별자리는 피함, 사용자 2026-10-26).</summary>
        private Constellation Pick()
        {
            int i = Random.Range(0, Sky.Length - 1);
            if (_last >= 0 && i >= _last)
                i++;
            else if (_last < 0)
                i = Random.Range(0, Sky.Length);
            _last = i;
            return Sky[i];
        }

        private void Update()
        {
            if (!_live || _mortars == null || _mortars.Length == 0 || !_view.Operational)
                return;
            _timer -= Time.deltaTime;
            if (_timer > 0f)
                return;
            _timer = _showInterval * Random.Range(0.6f, 1.4f);
            Launch();
        }

        /// <summary>박격포대 불꽃 한 발 (가동 중이면 Update가 가끔 부름 — 확인 · 연출용으로 직접 불러도 됨).</summary>
        public void Launch()
        {
            if (_sparkMaterial != null && _mortars != null && _mortars.Length > 0 && isActiveAndEnabled)
                StartCoroutine(Show());
        }

        /// <summary>포탑 요격 자리에 별자리 불꽃 (별자리는 무작위).</summary>
        public void Burst(Vector3 at)
        {
            if (_sparkMaterial != null && isActiveAndEnabled)
                StartCoroutine(ConstellationBurst(at, _burstSize, Pick()));
        }

        /// <summary>별자리 별의 월드 위치 (테스트에서 직접 호출): 면은 카메라를 향하고(forward) 위쪽은 월드 위.</summary>
        public static void Layout(Constellation c, Vector3 center, Vector3 forward, float size, Vector3[] result)
        {
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, forward);
            if (up.sqrMagnitude < 1e-4f)
                up = Vector3.ProjectOnPlane(Vector3.forward, forward);
            up.Normalize();
            Vector3 right = Vector3.Cross(up, forward).normalized;
            for (int i = 0; i < c.Stars.Length; i++)
                result[i] = center + (right * c.Stars[i].x + up * c.Stars[i].y) * (size * 0.5f);
        }

        private Vector3 ViewForward(Vector3 at)
        {
            var cam = Camera.main;
            return cam != null ? (at - cam.transform.position).normalized : Vector3.forward;
        }

        private IEnumerator ConstellationBurst(Vector3 at, float size, Constellation c)
        {
            int n = c.Stars.Length;
            var targets = new Vector3[n];
            Layout(c, at, ViewForward(at), size, targets);

            var stars = NewSystem("Stars", true, 0f, Total);
            var glitter = NewSystem("Glitter", false, 0.06f, Total);
            Flash(glitter, at, size * 0.5f);
            // 별마다 시드 = 번호 + 1 (입자 순서는 프레임마다 바뀔 수 있어 시드로 자기 별 자리를 찾음 — 순서로 짝지으면 꼬리가 다른 별 쪽으로 꺾임)
            for (int i = 0; i < n; i++)
                stars.Emit(new ParticleSystem.EmitParams { position = at, startLifetime = Total, startSize = 0.1f, startColor = StarColor, applyShapeToPosition = false, randomSeed = (uint)(i + 1) }, 1);
            var parts = new ParticleSystem.Particle[n];
            var lines = new LineRenderer[c.Lines.Length / 2];
            bool arrived = false;
            float t = 0f;
            while (t < Total && stars != null)
            {
                t += Time.deltaTime;
                float spread = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / Arrive), 3f); // 빠르게 퍼졌다 멈춤
                float fade = 1f - Smooth(1.35f, Total, t);
                float drop = Smooth(1.35f, Total, t) * 0.15f * size;              // 끝에 불티처럼 살짝 내려앉음
                int count = stars.GetParticles(parts);
                for (int p = 0; p < count; p++)
                {
                    int i = Mathf.Clamp((int)parts[p].randomSeed - 1, 0, n - 1);
                    float twinkle = t > Arrive ? 1f + 0.35f * Mathf.Sin(t * 18f + i * 2.1f) : 1f;
                    parts[p].position = Vector3.LerpUnclamped(at, targets[i], spread) + Vector3.down * drop;
                    parts[p].startSize = 0.1f * twinkle * Mathf.Lerp(0.5f, 1f, fade);
                    var col = StarColor;
                    col.a = fade;
                    parts[p].startColor = col;
                }
                stars.SetParticles(parts, count);
                if (!arrived && t >= Arrive)
                {
                    arrived = true;
                    for (int i = 0; i < n; i++)
                        Sparkle(glitter, targets[i], StarColor, 5, 0.3f * size);
                }
                // 별을 잇는 빛줄이 차례로 그어짐
                for (int k = 0; k < lines.Length; k++)
                {
                    float start = Arrive + 0.05f + k * 0.07f;
                    if (t < start)
                        continue;
                    if (lines[k] == null)
                        lines[k] = NewLine(stars.transform);
                    Vector3 a = targets[c.Lines[k * 2]] + Vector3.down * drop, b = targets[c.Lines[k * 2 + 1]] + Vector3.down * drop;
                    lines[k].SetPosition(0, a);
                    lines[k].SetPosition(1, Vector3.Lerp(a, b, Smooth(start, start + 0.14f, t)));
                    var lc = LineColor;
                    lc.a = fade * 0.85f;
                    lines[k].startColor = lines[k].endColor = lc;
                }
                yield return null;
            }
        }

        private IEnumerator Show()
        {
            var muzzle = transform.TransformPoint(_mortars[Random.Range(0, _mortars.Length)]);
            var apex = muzzle + Vector3.up * Random.Range(1.4f, 2f) + new Vector3(Random.Range(-0.3f, 0.3f), 0f, Random.Range(-0.3f, 0.3f));
            var shell = NewSystem("Shell", true, 0f, 0.1f);
            shell.Emit(new ParticleSystem.EmitParams { position = muzzle, startLifetime = 1.2f, startSize = 0.05f, startColor = StarColor, applyShapeToPosition = false }, 1);
            var parts = new ParticleSystem.Particle[1];
            float t = 0f;
            const float rise = 0.8f;
            while (t < rise && shell != null)
            {
                t += Time.deltaTime;
                float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / rise), 2f);
                if (shell.GetParticles(parts) > 0)
                {
                    parts[0].position = Vector3.Lerp(muzzle, apex, k) + Vector3.right * (Mathf.Sin(t * 30f) * 0.01f);
                    shell.SetParticles(parts, 1);
                }
                yield return null;
            }
            if (shell != null)
                shell.Clear();
            if (Random.value < 0.5f)
                yield return ConstellationBurst(apex, _showSize, Pick());
            else
                RoundBurst(apex);
        }

        /// <summary>둥근 불꽃: 한 색으로 사방에 퍼지며 꼬리를 끌고 천천히 내려앉음.</summary>
        private void RoundBurst(Vector3 at)
        {
            var ps = NewSystem("Round", true, 0.08f, 0.1f);
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.drag = 1.6f; // 퍼지다 멈춤
            Flash(ps, at, 0.5f);
            Sparkle(ps, at, Palette[Random.Range(0, Palette.Length)], 36, 1.6f);
        }

        private static void Flash(ParticleSystem ps, Vector3 at, float size) =>
            ps.Emit(new ParticleSystem.EmitParams { position = at, startLifetime = 0.22f, startSize = size, startColor = Color.white, applyShapeToPosition = false }, 1);

        private static void Sparkle(ParticleSystem ps, Vector3 at, Color color, int count, float speed)
        {
            for (int i = 0; i < count; i++)
            {
                var c = Color.Lerp(color, Color.white, Random.value * 0.3f);
                ps.Emit(new ParticleSystem.EmitParams
                {
                    position = at,
                    velocity = Random.onUnitSphere * speed * Random.Range(0.6f, 1f),
                    startLifetime = Random.Range(0.6f, 1.1f),
                    startSize = Random.Range(0.025f, 0.05f),
                    startColor = c,
                    applyShapeToPosition = false,
                }, 1);
            }
        }

        /// <summary>한 번 쓰고 스스로 지워지는 입자 묶음 (월드 공간, 방출은 Emit으로만).</summary>
        private ParticleSystem NewSystem(string name, bool trails, float gravity, float duration)
        {
            var go = new GameObject("Firework_" + name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = duration; // 이 시간 동안은 입자가 없어도 남아 있음 (늦게 Emit하는 묶음)
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravity;
            main.maxParticles = 200;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;
            var size = ps.sizeOverLifetime;
            size.enabled = gravity > 0f; // 흩날리는 불티만 줄어듦 (별 · 포탄은 크기를 직접 정함)
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = _sparkMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (trails && _lineMaterial != null)
            {
                var tr = ps.trails;
                tr.enabled = true;
                tr.lifetime = new ParticleSystem.MinMaxCurve(0.14f);
                tr.minVertexDistance = 0.02f;
                tr.dieWithParticles = true;
                tr.inheritParticleColor = true;
                tr.widthOverTrail = new ParticleSystem.MinMaxCurve(0.5f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
                r.trailMaterial = _lineMaterial;
            }
            ps.Play();
            return ps;
        }

        private LineRenderer NewLine(Transform parent)
        {
            var l = new GameObject("Line").AddComponent<LineRenderer>();
            l.transform.SetParent(parent, false);
            l.useWorldSpace = true;
            l.positionCount = 2;
            l.widthMultiplier = 0.022f;
            l.sharedMaterial = _lineMaterial;
            l.shadowCastingMode = ShadowCastingMode.Off;
            l.receiveShadows = false;
            l.numCapVertices = 2;
            return l;
        }

        /// <summary>a → b 구간에서 0 → 1로 부드럽게.</summary>
        private static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));
    }
}
