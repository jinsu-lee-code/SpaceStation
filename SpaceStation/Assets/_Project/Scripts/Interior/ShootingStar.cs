using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 별똥별 하나 (궤도 방어망 오로라 보호막 · 별똥별 막기 같이 씀): 출발점 → 닿을 자리로 곧게 날아가는 빛줄 (LineRenderer, M_FireworkLine).
    /// 머리만 환하고 꼬리는 길게 옅어짐 (0.13 · 1.6 m는 가는 흰 줄, 0.28 · 2.4 m는 흰 송곳처럼 보였음), 꼬리 = 지나온 길이까지만. 시간은 부르는 쪽이 넘김.
    /// </summary>
    public sealed class ShootingStar
    {
        public const float Tail = 3.2f, Width = 0.17f;

        public readonly LineRenderer Line;
        public readonly Vector3 From, To;
        public readonly float Duration;
        public float T { get; private set; }

        public Vector3 Head => Vector3.Lerp(From, To, Mathf.Clamp01(T / Duration));
        public bool Arrived => T >= Duration;

        /// <param name="from">출발점 (월드)</param>
        /// <param name="to">닿을 자리 (월드)</param>
        /// <param name="speed">빠르기 (m/초)</param>
        public ShootingStar(Transform parent, Material material, Vector3 from, Vector3 to, float speed)
        {
            From = from;
            To = to;
            Duration = Mathf.Max(0.05f, (to - from).magnitude / Mathf.Max(0.1f, speed));
            Line = new GameObject("ShootingStar").AddComponent<LineRenderer>();
            Line.transform.SetParent(parent, false);
            Line.useWorldSpace = true;
            Line.positionCount = 2;
            Line.sharedMaterial = material;
            Line.shadowCastingMode = ShadowCastingMode.Off;
            Line.receiveShadows = false;
            Line.textureMode = LineTextureMode.Stretch;
            Line.widthCurve = new AnimationCurve(new Keyframe(0f, Width), new Keyframe(0.15f, Width * 0.55f), new Keyframe(1f, 0.01f));
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.98f, 0.94f), 0f), new GradientColorKey(new Color(0.62f, 0.84f, 1f), 0.35f), new GradientColorKey(new Color(0.5f, 0.7f, 1f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.45f, 0.25f), new GradientAlphaKey(0f, 1f) });
            Line.colorGradient = gradient;
            Apply();
        }

        public void Step(float dt)
        {
            T += dt;
            Apply();
        }

        public void Destroy()
        {
            if (Line != null)
                Object.Destroy(Line.gameObject);
        }

        /// <summary>머리에서 흩뿌리는 반짝이 묶음 (부르는 쪽이 하나 만들어 두고 <see cref="EmitTrail"/>로 씀 — 가산 빛 점, 실제 시간).</summary>
        public static ParticleSystem CreateSparks(Transform parent, Material material)
        {
            var go = new GameObject("ShootingStarSparks");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            main.maxParticles = 400;
            main.useUnscaledTime = true;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        /// <summary>날아가는 머리에서 옅은 반짝이를 흩뿌림 (꼬리 뒤로 잠깐 남음).</summary>
        public void EmitTrail(ParticleSystem sparks)
        {
            if (sparks == null)
                return;
            var head = Head;
            var back = (From - To).normalized;
            for (int k = 0; k < 3; k++)
            {
                sparks.Emit(new ParticleSystem.EmitParams
                {
                    position = head + back * Random.Range(0f, 0.3f),
                    velocity = back * Random.Range(0.3f, 0.9f) + Random.insideUnitSphere * 0.35f,
                    startLifetime = Random.Range(0.35f, 0.7f),
                    startSize = Random.Range(0.05f, 0.11f),
                    startColor = Color.Lerp(new Color(0.7f, 0.88f, 1f), Color.white, Random.value),
                    applyShapeToPosition = false,
                }, 1);
            }
        }

        private void Apply()
        {
            var head = Head;
            var back = (From - To).normalized;
            float tail = Mathf.Min(Tail, (head - From).magnitude);
            Line.SetPosition(0, head);
            Line.SetPosition(1, head + back * Mathf.Max(tail, 0.05f));
        }
    }
}
