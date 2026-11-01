using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Building
{
    /// <summary>
    /// 진화 그리드 허브 "번개 정원"의 전기 (사용자 2026-10-27 "전기 이펙트를 만들어서 실제 전류가 흐르는 모습",
    /// StationArtBuilder가 자동 배선 — 모델에 구운 멈춘 번개 줄은 가져올 때 지움, EvoBakedFxStrip):
    /// - 테슬라 탑 꼭대기 에너지 구 → 번개 나무 여섯으로 번개가 제각각 박자로 내리침. 치는 동안 갈래 모양이 계속 바뀌며 번쩍이고,
    ///   밝은 전류 덩이가 탑에서 나무 쪽으로 흘러감. 가끔 중간에서 잔가지가 갈라졌다 사라짐. 닿는 곳마다 불티 · 섬광.
    /// - 이웃 나무끼리 (0-1, 2-3, 4-5) 가끔 번개가 튐. 에너지 구 둘레엔 짧은 전기 덩굴이 늘 지직거림 (플라스마 공처럼).
    /// 번개 = 가운데 흰 심 + 넓고 옅은 푸른 빛 (선 둘). 가동 중에만 침. 고스트(배치 미리보기)에서는 만들지 않음.
    /// </summary>
    public sealed class LightningFx : MonoBehaviour
    {
        [SerializeField] private Material _lineMaterial;  // 가산 빛 띠 (가로질러 부드러움)
        [SerializeField] private Material _sparkMaterial; // 가산 빛 점
        [Tooltip("탑 꼭대기 에너지 구 가운데 (모델 기준)")]
        [SerializeField] private Vector3 _hub;
        [SerializeField] private float _hubRadius = 0.085f;
        [Tooltip("번개 나무 꼭대기 불꽃 구 가운데 (모델 기준)")]
        [SerializeField] private Vector3[] _nodes;
        [SerializeField] private float _nodeRadius = 0.034f;
        [Tooltip("번개가 튀는 이웃 나무 (번호 둘씩)")]
        [SerializeField] private int[] _links = { 0, 1, 2, 3, 4, 5 };
        [SerializeField] private Color _coreColor = new Color(0.9f, 0.95f, 1f, 1f);
        [SerializeField] private Color _glowColor = new Color(0.45f, 0.62f, 1f, 1f);
        [Tooltip("흰 심 굵기 · 푸른 빛 번짐 굵기 — 게임 카메라 거리(약 12m)에서도 보이게")]
        [SerializeField] private float _coreWidth = 0.016f;
        [SerializeField] private float _glowWidth = 0.09f;

        private const int Points = 17; // 2⁴ + 1 (가운데 점 나누기)

        private sealed class Arc
        {
            public LineRenderer Core, Glow, Branch;
            public int From = -1, To; // From −1 = 탑
            public bool Crackle;      // 에너지 구 둘레 짧은 덩굴
            public float Next, End, Rejag, Pulse, Strength;
            public readonly Vector3[] Pts = new Vector3[Points];
            public readonly Vector3[] BranchPts = new Vector3[9];
        }

        private Arc[] _arcs;
        private ParticleSystem _sparks;
        private ModuleView _view;
        private bool _live;
        private float _clock;
        private readonly GradientColorKey[] _colorKeys = new GradientColorKey[2];
        private readonly GradientAlphaKey[] _alphaKeys = new GradientAlphaKey[5];
        private readonly Gradient _gradient = new Gradient();

        public int ArcCount => _arcs != null ? _arcs.Length : 0;

        private void Start()
        {
            _view = GetComponentInParent<ModuleView>();
            _live = _view != null && _view.Module != null;
            if (_live)
                Play();
        }

        /// <summary>번개를 만들고 치기 시작 (Start가 부름 — 확인용으로 미리보기에서 직접 불러도 됨).</summary>
        public void Play()
        {
            if (_arcs != null || _lineMaterial == null || _nodes == null)
                return;
            int links = _links != null ? _links.Length / 2 : 0;
            const int crackles = 3;
            _arcs = new Arc[_nodes.Length + links + crackles];
            int k = 0;
            for (int i = 0; i < _nodes.Length; i++)
                _arcs[k++] = NewArc(-1, i, false);
            for (int i = 0; i < links; i++)
                _arcs[k++] = NewArc(_links[i * 2], _links[i * 2 + 1], false);
            for (int i = 0; i < crackles; i++)
                _arcs[k++] = NewArc(-1, -1, true);
            _sparks = Sparks();
        }

        private Arc NewArc(int from, int to, bool crackle)
        {
            var a = new Arc { From = from, To = to, Crackle = crackle };
            a.Glow = NewLine("Glow", _glowWidth * (crackle ? 0.5f : 1f), Points);
            a.Core = NewLine("Core", _coreWidth * (crackle ? 0.6f : 1f), Points);
            if (!crackle)
                a.Branch = NewLine("Branch", _coreWidth * 0.7f, a.BranchPts.Length);
            a.Next = Random.Range(0.1f, 1.5f);
            return a;
        }

        private void Update()
        {
            if (_arcs == null)
                return;
            bool on = !_live || _view.Operational;
            _clock += Time.deltaTime;
            foreach (var a in _arcs)
                Step(a, on);
        }

        private void Step(Arc a, bool on)
        {
            float t = _clock;
            if (t >= a.End)
            {
                // 쉬는 중: 다음 칠 때가 되면 시작 (비활성이면 안 침)
                Show(a, false);
                if (!on || t < a.Next)
                    return;
                // 게임 카메라 거리에서도 늘 어딘가 번개가 보이게 자주 (탑 → 나무 여섯이 제각각)
                float length = a.Crackle ? Random.Range(0.25f, 0.7f) : a.From < 0 ? Random.Range(0.18f, 0.55f) : Random.Range(0.12f, 0.35f);
                a.End = t + length;
                a.Next = a.End + (a.Crackle ? Random.Range(0.03f, 0.25f) : a.From < 0 ? Random.Range(0.15f, 1.1f) : Random.Range(0.6f, 2.2f));
                a.Rejag = 0f;
                a.Pulse = 0f;
                if (!a.Crackle)
                    Strike(a);
            }
            a.Rejag -= Time.deltaTime;
            if (a.Rejag <= 0f)
            {
                a.Rejag = Random.Range(0.035f, 0.07f); // 지직지직 모양이 바뀜
                Build(a);
            }
            a.Pulse += Time.deltaTime * 3.2f;
            Paint(a);
            Show(a, true);
        }

        /// <summary>번개가 막 칠 때: 닿는 곳 불티 + 양끝 섬광.</summary>
        private void Strike(Arc a)
        {
            Ends(a, out var from, out var to);
            var wFrom = transform.TransformPoint(from);
            var wTo = transform.TransformPoint(to);
            float scale = transform.lossyScale.x;
            Flash(wFrom, 0.12f * scale);
            Flash(wTo, 0.1f * scale);
            for (int i = 0; i < 10; i++)
            {
                _sparks.Emit(new ParticleSystem.EmitParams
                {
                    position = wTo,
                    velocity = (Random.onUnitSphere + Vector3.up * 0.6f) * Random.Range(0.25f, 0.6f) * scale,
                    startSize = Random.Range(0.012f, 0.024f) * scale,
                    startLifetime = Random.Range(0.25f, 0.5f),
                    startColor = Color.Lerp(_coreColor, _glowColor, Random.value * 0.5f),
                    applyShapeToPosition = false,
                }, 1);
            }
        }

        private void Flash(Vector3 at, float size) =>
            _sparks.Emit(new ParticleSystem.EmitParams { position = at, startSize = size, startLifetime = 0.12f, startColor = _coreColor, applyShapeToPosition = false }, 1);

        private void Ends(Arc a, out Vector3 from, out Vector3 to)
        {
            if (a.Crackle)
            {
                // 에너지 구 겉에서 바깥으로 짧게 (매번 다른 방향, 위쪽 반구 위주)
                var dir = Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) * 0.8f + 0.2f;
                dir.Normalize();
                var side = Vector3.Cross(dir, Random.onUnitSphere).normalized;
                from = _hub + dir * _hubRadius * 0.9f;
                to = _hub + (dir + side * 0.6f).normalized * (_hubRadius + Random.Range(0.05f, 0.11f));
                return;
            }
            var src = a.From < 0 ? _hub : _nodes[a.From];
            float srcR = a.From < 0 ? _hubRadius : _nodeRadius;
            var dst = _nodes[a.To];
            var d = (dst - src).normalized;
            from = src + d * srcR * 0.9f;
            to = dst - d * _nodeRadius * 0.9f;
        }

        private void Build(Arc a)
        {
            Ends(a, out var from, out var to);
            Jag(from, to, a.Crackle ? 0.3f : 0.22f, a.Pts);
            a.Core.SetPositions(a.Pts);
            a.Glow.SetPositions(a.Pts);
            a.Strength = Random.Range(0.55f, 1f); // 번쩍임
            if (a.Branch == null)
                return;
            // 잔가지: 절반쯤 확률로 중간에서 갈라져 짧게 뻗음
            bool branch = Random.value < 0.5f;
            a.Branch.enabled = branch;
            if (!branch)
                return;
            int at = Random.Range(Points / 4, Points * 3 / 4);
            var start = a.Pts[at];
            var main = (to - start);
            var off = Vector3.Cross(main.normalized, Random.onUnitSphere).normalized;
            var end = start + (main.normalized + off * Random.Range(0.6f, 1.1f)).normalized * main.magnitude * Random.Range(0.3f, 0.5f);
            JagShort(start, end, a.BranchPts);
            a.Branch.SetPositions(a.BranchPts);
        }

        /// <summary>가운데 점 나누기 번개 (17점): 구간마다 가운데를 가로로 무작위로 밀어 큰 꺾임 위에 잔 꺾임이 겹침.</summary>
        public static void Jag(Vector3 from, Vector3 to, float roughness, Vector3[] pts)
        {
            int last = pts.Length - 1;
            pts[0] = from;
            pts[last] = to;
            var dir = (to - from).normalized;
            for (int step = last; step > 1; step /= 2)
            {
                int half = step / 2;
                for (int i = half; i < last; i += step)
                {
                    var a = pts[i - half];
                    var b = pts[i + half];
                    var perp = Vector3.ProjectOnPlane(Random.insideUnitSphere, dir);
                    pts[i] = (a + b) * 0.5f + perp * (Vector3.Distance(a, b) * roughness); // 옆으로는 구간 길이 × roughness 안 (전체 합 &lt; 길이 × 2·roughness)
                }
            }
        }

        private static void JagShort(Vector3 from, Vector3 to, Vector3[] pts)
        {
            var dir = (to - from).normalized;
            float len = Vector3.Distance(from, to);
            for (int i = 0; i < pts.Length; i++)
            {
                float t = i / (float)(pts.Length - 1);
                var perp = Vector3.ProjectOnPlane(Random.insideUnitSphere, dir);
                pts[i] = Vector3.Lerp(from, to, t) + perp * (len * 0.12f * Mathf.Sin(Mathf.PI * t));
            }
        }

        /// <summary>밝기: 번쩍임 세기 × 끝으로 갈수록 살짝 옅게 + 흘러가는 밝은 전류 덩이.</summary>
        private void Paint(Arc a)
        {
            float s = a.Strength;
            float p = Mathf.Repeat(a.Pulse, 1.3f) - 0.15f; // 탑 → 나무로 흘러감
            _colorKeys[0] = new GradientColorKey(_coreColor, 0f);
            _colorKeys[1] = new GradientColorKey(_coreColor, 1f);
            _alphaKeys[0] = new GradientAlphaKey(0.75f * s, 0f);
            _alphaKeys[1] = new GradientAlphaKey(0.75f * s, Mathf.Clamp01(p - 0.12f));
            _alphaKeys[2] = new GradientAlphaKey(1f, Mathf.Clamp01(p));
            _alphaKeys[3] = new GradientAlphaKey(0.75f * s, Mathf.Clamp01(p + 0.12f));
            _alphaKeys[4] = new GradientAlphaKey(0.55f * s, 1f);
            _gradient.SetKeys(_colorKeys, _alphaKeys);
            a.Core.colorGradient = _gradient; // startColor · endColor를 다시 넣으면 흐르는 덩이가 지워짐
            var glow = _glowColor;
            glow.a = 0.5f * s;
            a.Glow.startColor = glow;
            glow.a = 0.32f * s;
            a.Glow.endColor = glow;
            if (a.Branch != null && a.Branch.enabled)
            {
                var c = _coreColor;
                c.a = 0.7f * s;
                a.Branch.startColor = c;
                c.a = 0f;
                a.Branch.endColor = c;
            }
        }

        private static void Show(Arc a, bool on)
        {
            if (a.Core.enabled == on)
                return;
            a.Core.enabled = on;
            a.Glow.enabled = on;
            if (a.Branch != null && !on)
                a.Branch.enabled = false;
        }

        private LineRenderer NewLine(string name, float width, int count)
        {
            var go = new GameObject("Lightning" + name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = count;
            lr.widthMultiplier = width;
            lr.alignment = LineAlignment.View;
            lr.textureMode = LineTextureMode.Stretch;
            lr.numCornerVertices = 1;
            lr.sharedMaterial = _lineMaterial;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            return lr;
        }

        private ParticleSystem Sparks()
        {
            var go = new GameObject("LightningSparks");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f; // 속도를 준 불티만 날아감 (섬광은 제자리)
            main.gravityModifier = 0.25f;
            main.maxParticles = 300;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = _sparkMaterial != null ? _sparkMaterial : _lineMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }
    }
}
