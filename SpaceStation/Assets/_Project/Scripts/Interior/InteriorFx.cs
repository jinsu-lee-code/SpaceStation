using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Interior
{
    /// <summary>내부 연출용 파티클 (11-10 파손 연출에서 꺼내 11-17 현장 수리 지점과 공용).</summary>
    public static class InteriorFx
    {
        /// <summary>한 번 튀는 스파크 + 순간 주황빛 (끝나면 스스로 지워짐). normal = 튀는 방향.</summary>
        public static void Spark(Transform parent, Vector3 position, Vector3 normal, Material material, int min = 14, int max = 26, float flashIntensity = 5f)
        {
            if (material == null)
                return;
            var go = new GameObject("Spark");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(normal));
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 0.2f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.75f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.8f, 0.4f), new Color(1f, 0.55f, 0.2f));
            main.gravityModifier = 1.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)min, (short)max) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.radius = 0.02f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            ps.Play();

            // 순간 빛은 따로 자식 오브젝트로: URP가 Light에 UniversalAdditionalLightData를 붙여 Light 컴포넌트만은 지울 수 없음
            if (flashIntensity <= 0f)
                return;
            var flashGo = new GameObject("Flash");
            flashGo.transform.SetParent(go.transform, false);
            var flash = flashGo.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.shadows = LightShadows.None;
            flash.color = new Color(1f, 0.65f, 0.3f);
            flash.intensity = flashIntensity;
            flash.range = 3f;
            Object.Destroy(flashGo, 0.08f);
        }

        /// <summary>
        /// 한 점에서 계속 피어오르는 작은 연기 · 김 (손상 지점). direction = 처음 뿜는 방향(벽 법선 등), 이후 위로.
        /// </summary>
        public static ParticleSystem Plume(Transform parent, Vector3 position, Vector3 direction, Material material, Color color, float rate, float size, float speed)
        {
            if (material == null)
                return null;
            var go = new GameObject("Plume");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction.sqrMagnitude > 1e-4f ? direction : Vector3.up));
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.duration = 4f;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.6f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = color;
            main.maxParticles = 80;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;
            var emission = ps.emission;
            emission.rateOverTime = rate;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 18f;
            shape.radius = 0.04f;
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.04f, 0.04f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.18f, 0.35f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.04f, 0.04f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.45f, 0.12f), new GradientAlphaKey(0.2f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = gradient;
            var sizeOver = ps.sizeOverLifetime;
            sizeOver.enabled = true;
            sizeOver.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 2.2f));
            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Play();
            return ps;
        }
    }
}
