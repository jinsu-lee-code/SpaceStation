using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 내부 분수 물줄기 (궤도 항만 인어 꼬리 분수, 2026-11-16 — 꼬리 끝 공중에 박아 둔 물방울 구슬 메시는 떠 있는 조각이라 입자로 바꿈):
    /// 솟는 자리에서 사방으로 뿜어 포물선으로 떨어지는 물줄기 (늘인 물방울) · 물줄기가 떨어지는 고리 자리의 흰 거품 (수면에 눕힌 판) · 옅은 물안개.
    /// 재질 = 폭포 물보라와 같은 부드러운 흰 입자. 수면 물결은 KWS 물이 따로 냄. 위치는 템플릿 루트 (이 물체) 기준.
    /// </summary>
    public sealed class InteriorFountainFx : MonoBehaviour
    {
        [SerializeField] private Vector3 _tip;
        [SerializeField] private float _waterY;
        [Tooltip("물줄기가 수면에 닿는 고리 반지름 (솟는 자리에서 수평)")]
        [SerializeField] private float _reach = 1.0f;
        [SerializeField] private Material _material;

        public void Configure(Vector3 tip, float waterY, float reach, Material material)
        {
            _tip = tip;
            _waterY = waterY;
            _reach = reach;
            _material = material;
        }

        private void Start()
        {
            if (_material == null)
                return;
            Jet();
            Foam();
            Mist();
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

        /// <summary>솟는 속도 (m/초) · 위로 기운 각 — 수평 거리 _reach에서 수면 (_tip.y − _waterY 아래)에 닿는 포물선.</summary>
        private void Launch(out float speed, out float lifetime)
        {
            const float g = 9.81f, up = 50f * Mathf.Deg2Rad;
            float drop = Mathf.Max(0.2f, _tip.y - _waterY);
            // 수평 x = v cos · t, 높이 = v sin · t − g t² / 2 = −drop → t를 x로 바꿔 풂
            float c = Mathf.Cos(up);
            float v2 = g * _reach * _reach / (2f * c * c * (_reach * Mathf.Tan(up) + drop));
            speed = Mathf.Sqrt(v2);
            lifetime = _reach / (speed * c);
        }

        // 물줄기: 꼬리 끝에서 위로 기울여 사방으로 — 늘인 물방울이 포물선으로 떨어져 고리 자리에서 사라짐
        private void Jet()
        {
            Launch(out float speed, out float life);
            var ps = Make("FountainJet", _tip, new Vector3(-90f, 0f, 0f));
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.9f, life * 1.02f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.94f, speed * 1.04f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
            main.startColor = new Color(0.92f, 0.96f, 1f, 0.8f);
            main.gravityModifier = 1f;
            main.maxParticles = 600;
            var em = ps.emission;
            em.rateOverTime = 320f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 40f;                 // 위 (-90°)에서 40° = 수평에서 50°
            sh.radius = 0.04f;
            sh.radiusThickness = 0f;        // 가장자리에서만 → 고르게 사방
            sh.arcMode = ParticleSystemShapeMultiModeValue.Random;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Fade(0.8f, 0.04f, 0.85f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.05f;
            r.lengthScale = 2f;
            ps.Play();
        }

        // 거품: 물줄기가 떨어지는 고리 자리 (수면에 눕힌 판)
        private void Foam()
        {
            var ps = Make("FountainFoam", new Vector3(_tip.x, _waterY + 0.015f, _tip.z), new Vector3(90f, 0f, 0f));
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 1.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.82f, 0.86f, 0.9f, 1f);
            main.gravityModifier = 0f;
            main.maxParticles = 80;
            var em = ps.emission;
            em.rateOverTime = 36f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Circle;
            sh.radius = _reach;
            sh.radiusThickness = 0.15f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Fade(0.42f, 0.1f, 0.5f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            ps.Play();
        }

        // 물안개: 고리 자리에서 옅게 피어오름 (크면 흰 구름 덩어리 — 폭포 물보라와 같은 값)
        private void Mist()
        {
            var ps = Make("FountainMist", new Vector3(_tip.x, _waterY + 0.05f, _tip.z), new Vector3(-90f, 0f, 0f));
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.72f, 0.78f, 0.84f, 1f);
            main.gravityModifier = -0.015f;
            main.maxParticles = 40;
            var em = ps.emission;
            em.rateOverTime = 8f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Circle;
            sh.radius = _reach;
            sh.radiusThickness = 0.2f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Fade(0.1f, 0.2f, 0.55f);
            ps.Play();
        }
    }
}
