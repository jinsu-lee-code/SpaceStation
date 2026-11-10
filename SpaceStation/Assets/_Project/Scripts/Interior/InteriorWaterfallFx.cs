using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 내부 폭포가 떨어지는 자리의 물보라 (궤도 도시 계단 폭포, 사용자 2026-11-11 "폭포도 이쁘게"): 떨어진 자리마다 입자 셋 —
    /// 피어오르는 물안개 (크고 옅게, 천천히) · 튀는 물방울 (작게, 위로 튀어 떨어짐) · 수면에 퍼지는 흰 거품 조각 (수면에 눕힌 판).
    /// 흰 구 덩어리 거품은 실사 물과 안 어울려 뺐음. 수면 물결은 KWS 물 (KWS_InteractWithWater)이 따로 냄.
    /// </summary>
    public sealed class InteriorWaterfallFx : MonoBehaviour
    {
        [SerializeField] private Vector3[] _landings;
        [SerializeField] private float[] _widths;
        [SerializeField] private Material _material;

        public void Configure(Vector3[] landings, float[] widths, Material material)
        {
            _landings = landings;
            _widths = widths;
            _material = material;
        }

        private void Start()
        {
            if (_material == null || _landings == null)
                return;
            for (int i = 0; i < _landings.Length; i++)
            {
                float w = _widths != null && i < _widths.Length ? _widths[i] : 0.8f;
                var at = _landings[i];
                Mist(at, w);
                Spray(at, w);
                Foam(at + Vector3.up * 0.015f, w);
            }
        }

        private ParticleSystem Make(string name, Vector3 local, Vector3 euler)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.Euler(euler);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = _material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        private static Gradient Fade(float alpha, float inT, float outT)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(alpha, inT), new GradientAlphaKey(alpha, outT), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        // 물안개: 떨어진 자리에서 천천히 피어올라 커지며 사라짐
        private void Mist(Vector3 at, float w)
        {
            var ps = Make("FallsMist", at, new Vector3(-90f, 0f, 0f));
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 2.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f * w + 0.1f, 0.6f * w + 0.2f);   // 크면 흰 구름 덩어리처럼 뭉쳐 보였음 (Unity 2026-11-11)
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.72f, 0.78f, 0.84f, 1f);   // 빛을 안 받는 입자라 어두운 정원에서 흰 덩어리로 튐 → 조금 낮춤
            main.gravityModifier = -0.015f;
            main.maxParticles = 60;
            var em = ps.emission;
            em.rateOverTime = 10f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 28f;
            sh.radius = 0.25f * w;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Fade(0.14f, 0.2f, 0.55f);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.7f));
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 0.4f;
            ps.Play();
        }

        // 물방울: 떨어진 자리에서 튀어 올라 다시 떨어짐
        private void Spray(Vector3 at, float w)
        {
            var ps = Make("FallsSpray", at, new Vector3(-90f, 0f, 0f));
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.1f, 2.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.055f);
            main.startColor = new Color(1f, 1f, 1f, 0.85f);
            main.gravityModifier = 0.9f;
            main.maxParticles = 120;
            var em = ps.emission;
            em.rateOverTime = 70f * w;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 34f;
            sh.radius = 0.3f * w;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Fade(0.85f, 0.05f, 0.7f);
            ps.Play();
        }

        // 수면 거품: 떨어진 자리 둘레로 퍼져 나가며 사라지는 흰 조각 (수면에 눕힌 판)
        private void Foam(Vector3 at, float w)
        {
            var ps = Make("FallsFoam", at, new Vector3(90f, 0f, 0f));
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.12f, 0.32f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.22f * w + 0.1f, 0.5f * w + 0.15f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.82f, 0.86f, 0.9f, 1f);
            main.gravityModifier = 0f;
            main.maxParticles = 50;
            var em = ps.emission;
            em.rateOverTime = 11f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Circle;
            sh.radius = 0.3f * w;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Fade(0.45f, 0.1f, 0.5f);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.4f));
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            ps.Play();
        }
    }
}
