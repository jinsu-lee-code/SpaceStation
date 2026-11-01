using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Building
{
    /// <summary>
    /// 진화 모듈에서 뿜어 나오는 반짝이 입자 (사용자 2026-10-27 플레이 점검, StationArtBuilder가 자동 배선 —
    /// 모델에 정지 입자로 구워 둔 같은 연출은 가져올 때 지움, EvoBakedFxStrip):
    /// - StarSmoke: 대형 토러스 열차 굴뚝 — 칙칙폭폭 박자로 별빛 가루가 뿜어져 올라와 흩어지며 사라짐
    ///   (월드 공간이라 달리는 열차 뒤로 가루 꼬리가 남음).
    /// - SparkFountain: 정련 콤비나트 돔 꼭대기 배출구 — 열 갈래 불꽃이 낮게 솟아 돔 둘레로 흘러내리며 반짝이다 꺼짐.
    /// 가동 중에만 뿜음 (비활성이면 멈추고 남은 입자만 사라짐). 고스트(배치 미리보기)에서는 만들지 않음.
    /// </summary>
    public sealed class SparkEmitterFx : MonoBehaviour
    {
        public enum Kind { StarSmoke, SparkFountain }

        [SerializeField] private Kind _kind;
        [SerializeField] private Material _sparkMaterial; // 가산 빛 점
        [SerializeField] private Material _trailMaterial; // 불꽃 꼬리 (분수만)

        private ParticleSystem _ps;
        private ModuleView _view;
        private bool _live;
        private bool _emitting = true;

        public ParticleSystem Particles => _ps;

        private void Start()
        {
            _view = GetComponentInParent<ModuleView>();
            _live = _view != null && _view.Module != null;
            if (_live)
                Play();
        }

        /// <summary>입자 묶음을 만들고 뿜기 시작 (Start가 부름 — 확인용으로 미리보기에서 직접 불러도 됨).</summary>
        public void Play()
        {
            if (_ps == null && _sparkMaterial != null)
                _ps = Build(transform, _kind, _sparkMaterial, _trailMaterial);
        }

        private void Update()
        {
            if (_ps == null || !_live)
                return;
            bool on = _view.Operational;
            if (on == _emitting)
                return;
            _emitting = on;
            var emission = _ps.emission;
            emission.enabled = on;
        }

        /// <summary>parent 아래에 입자 묶음을 만든다 (방출 방향 = parent의 위).</summary>
        public static ParticleSystem Build(Transform parent, Kind kind, Material spark, Material trail)
        {
            var go = new GameObject(kind.ToString());
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.duration = 1f;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emission = ps.emission;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.rotation = new Vector3(-90f, 0f, 0f); // 원뿔 기본 방향 +z → 위
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = spark;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;

            if (kind == Kind.StarSmoke)
            {
                // 게임 카메라 거리(약 12m)에서도 보이게 크고 밝게
                main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.6f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.22f, 0.36f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.028f, 0.055f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.95f, 0.78f), new Color(0.85f, 0.93f, 1f)); // 입자 색은 0~1 (더 밝게는 재질 HDR)
                main.maxParticles = 300;
                // 칙칙폭폭: 0.4초마다 한 번씩 뭉텅 + 사이사이 조금
                emission.rateOverTime = 10f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 9, 14, 0, 0.4f) });
                shape.angle = 20f;
                shape.radius = 0.012f;
                // 뿜어진 뒤 느려져 떠다니며 흩어짐
                var limit = ps.limitVelocityOverLifetime;
                limit.enabled = true;
                limit.drag = 1.3f;
                var noise = ps.noise;
                noise.enabled = true;
                noise.strength = 0.14f;
                noise.frequency = 2.6f;
                noise.scrollSpeed = 0.5f;
                noise.sizeAmount = 0.5f; // 떠다니며 반짝반짝 커졌다 작아짐
                size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.18f, 1.1f), new Keyframe(0.6f, 0.8f), new Keyframe(1f, 0f)));
                color.color = new ParticleSystem.MinMaxGradient(Fade(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.95f, 0.8f), 0.5f), new GradientColorKey(new Color(0.75f, 0.85f, 1f), 1f) },
                    0.06f, 0.55f, 0.75f));
            }
            else
            {
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.2f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.36f, 0.5f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.026f, 0.045f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.8f, 0.42f), new Color(1f, 0.97f, 0.85f));
                main.gravityModifier = 0.09f; // 낮게 솟아 돔 둘레로 흘러내림 (칸 위 경계를 크게 넘지 않음)
                main.maxParticles = 360;
                emission.rateOverTime = 120f;
                // 열 갈래 물줄기: 원뿔 둘레 10곳에서만 나옴
                shape.angle = 50f;
                shape.radius = 0.035f;
                shape.arcMode = ParticleSystemShapeMultiModeValue.Random;
                shape.arcSpread = 0.1f;
                var noise = ps.noise;
                noise.enabled = true;
                noise.strength = 0.05f;
                noise.frequency = 4f;
                noise.sizeAmount = 0.6f; // 반짝임
                size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.7f, 0.8f), new Keyframe(1f, 0f)));
                color.color = new ParticleSystem.MinMaxGradient(Fade(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.85f, 0.5f), 0.45f), new GradientColorKey(new Color(1f, 0.55f, 0.25f), 1f) },
                    0.02f, 0.6f, 1f));
                if (trail != null)
                {
                    var tr = ps.trails;
                    tr.enabled = true;
                    tr.ratio = 0.6f;
                    tr.lifetime = new ParticleSystem.MinMaxCurve(0.2f);
                    tr.minVertexDistance = 0.01f;
                    tr.dieWithParticles = true;
                    tr.inheritParticleColor = true;
                    tr.widthOverTrail = new ParticleSystem.MinMaxCurve(0.7f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
                    r.trailMaterial = trail;
                }
            }
            ps.Play();
            return ps;
        }

        /// <summary>빠르게 나타나 (inEnd까지) 머물다 (holdEnd부터) 사라지는 투명도.</summary>
        private static Gradient Fade(GradientColorKey[] colors, float inEnd, float holdEnd, float holdAlpha)
        {
            var g = new Gradient();
            g.SetKeys(colors, new[]
            {
                new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, inEnd), new GradientAlphaKey(holdAlpha, holdEnd), new GradientAlphaKey(0f, 1f),
            });
            return g;
        }
    }
}
