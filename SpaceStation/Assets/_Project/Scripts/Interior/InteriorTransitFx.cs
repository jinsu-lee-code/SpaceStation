using System.Collections.Generic;
using SpaceStation.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 위아래 이동 장치 연출 (사용자 2026-11-20 "너무 밋밋 — 이펙트 넣어서 이쁘게" → 흩뿌린 빛 점은 "못생겼다, 단계별 모듈에 맞게"): 단계 테마마다 모양이 다름.
    /// 기본 (생존 공업 화물 칸) = 바닥 호박빛 작업등이 숨 쉬듯 · 탈 때 바닥 둘레 김.
    /// 1 (흰 세라믹 반중력 원판) = 청백 빛 고리가 원판에서 떠올라 사라짐 + 원판 밑 부양 빛.
    /// 2 (테마파크 혼천의) = 금빛 등불이 꼬리를 끌며 기울어진 두 궤도를 돎 + 꼭대기 별 반짝임.
    /// 3 (진주 · 금 정원 연꽃) = 분홍 · 진주빛 꽃잎이 팔랑이며 내려와 맴돎 + 연꽃 속 금빛 꽃가루.
    /// 4 (성운 은 원판) = 보라 · 파랑 성운 안개가 감돌고 네 갈래 별빛이 반짝임.
    /// 타는 동안 더 많이 · 빠르게 (기본은 그때만 김). 파티클은 방 뿌리에 둠 (장치 배율이 겹치지 않게), 재질 = 키트 (가산).
    /// </summary>
    public sealed class InteriorTransitFx : MonoBehaviour
    {
        private struct Part
        {
            public ParticleSystem System;
            public float Idle, Ride, RideSpeed;
        }

        private readonly List<Part> _parts = new List<Part>();
        private Transform _root;

        /// <summary>stage = 키트 단계 (0 = 기본), padTop = 발판 윗면 가운데 (월드), radius = 발판 반지름 · height = 빛 기둥 높이 · s = 장치 배율, accent = 방 강조색.</summary>
        public void Initialize(int stage, Vector3 padTop, float radius, float height, float s, Color accent, InteriorKit kit)
        {
            if (kit == null)
                return;
            _root = transform.parent != null ? transform.parent : transform;
            switch (Mathf.Clamp(stage, 0, 4))
            {
                case 0:
                    Glow(padTop, 2.2f * s, new Color(1f, 0.62f, 0.25f), 0.45f, kit.TransitMote);
                    Steam(padTop, 0.95f * s, s, kit.TransitMote);
                    break;
                case 1:
                    Glow(padTop, 2.4f * s, Color.Lerp(new Color(0.55f, 0.9f, 1f), accent, 0.3f), 0.35f, kit.TransitMote);
                    FieldRings(padTop, radius * 2.3f, height, kit.TransitRipple);
                    break;
                case 2:
                    Orbs(padTop + Vector3.up * (1.25f * s), 0.95f * s, s, Quaternion.Euler(-72f, 0f, 0f), kit.TransitMote);
                    Orbs(padTop + Vector3.up * (1.25f * s), 0.95f * s, s, Quaternion.Euler(-90f, 0f, 20f), kit.TransitMote);
                    Glints(padTop + Vector3.up * (height * 0.92f), 0.35f * s, 0.22f * s, 0.9f, new Color(1f, 0.95f, 0.8f), kit.TransitStar);
                    break;
                case 3:
                    Petals(padTop + Vector3.up * (2.0f * s), 1.0f * s, s, kit.TransitPetal);
                    Pollen(padTop + Vector3.up * (0.25f * s), 0.28f * s, s, kit.TransitMote);
                    break;
                default:
                    Nebula(padTop, height, s, kit.TransitCloud);
                    Stars(padTop, height, s, kit.TransitStar);
                    break;
            }
        }

        public void SetRiding(bool riding)
        {
            foreach (var p in _parts)
            {
                if (p.System == null)
                    continue;
                var emission = p.System.emission;
                emission.rateOverTime = riding ? p.Ride : p.Idle;
                var main = p.System.main;
                main.simulationSpeed = riding ? p.RideSpeed : 1f;
            }
        }

        private void OnDestroy()
        {
            foreach (var p in _parts)
            {
                if (p.System == null)
                    continue;
                if (Application.isPlaying)
                    Destroy(p.System.gameObject);
                else
                    DestroyImmediate(p.System.gameObject);   // 에디터 미리보기 (자리 사진)
            }
        }

        // ── 단계별 조각 ──

        /// <summary>바닥에 깔린 빛 웅덩이가 숨 쉬듯 밝아졌다 어두워짐 (겹친 두세 장).</summary>
        private void Glow(Vector3 at, float size, Color color, float alpha, Material material)
        {
            var ps = Make("TransitGlow", at + Vector3.up * 0.02f, Quaternion.identity, material, ParticleSystemRenderMode.HorizontalBillboard, 3, 0.45f, 0.9f, 1.5f);
            if (ps == null)
                return;
            var main = ps.main;
            main.startLifetime = 4.4f;
            main.startSize = size;
            main.startColor = color;
            Fade(ps, alpha, 0.5f, 0.5f);
        }

        /// <summary>기본 화물 칸: 탈 때만 바닥 둘레에서 김이 뿜어져 오름.</summary>
        private void Steam(Vector3 at, float radius, float s, Material material)
        {
            var ps = Make("TransitSteam", at, Quaternion.Euler(-90f, 0f, 0f), material, ParticleSystemRenderMode.Billboard, 60, 0f, 18f, 1f);
            if (ps == null)
                return;
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 1.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f * s, 0.55f * s);
            main.startColor = new Color(0.9f, 0.93f, 0.97f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            shape.radiusThickness = 0f;
            Velocity(ps, 0.05f, 0.5f, 0.8f);
            var velocity = ps.velocityOverLifetime;
            velocity.radial = new ParticleSystem.MinMaxCurve(0.25f, 0.4f);
            Grow(ps, 1f, 2.6f);
            Fade(ps, 0.4f, 0.15f, 0.5f);
        }

        /// <summary>1단계: 원판에서 청백 빛 고리가 천천히 떠올라 좁아지며 사라짐 (반중력 층).</summary>
        private void FieldRings(Vector3 at, float size, float height, Material material)
        {
            var ps = Make("TransitFieldRings", at + Vector3.up * 0.05f, Quaternion.identity, material, ParticleSystemRenderMode.HorizontalBillboard, 10, 0.8f, 2.4f, 1.8f);
            if (ps == null)
                return;
            var main = ps.main;
            main.startLifetime = 3f;
            main.startSize = size;
            main.startColor = new Color(0.72f, 0.95f, 1f);
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(height * 0.3f, height * 0.3f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            Grow(ps, 1f, 0.7f);
            Fade(ps, 0.55f, 0.15f, 0.6f);
        }

        /// <summary>2단계: 금빛 등불 몇 개가 꼬리를 끌며 혼천의 둘레를 돎 (궤도 하나 — 기울기는 tilt).</summary>
        private void Orbs(Vector3 at, float radius, float s, Quaternion tilt, Material material)
        {
            var ps = Make("TransitOrbs", at, tilt, material, ParticleSystemRenderMode.Billboard, 6, 1.0f, 2.5f, 2.2f);
            if (ps == null)
                return;
            var main = ps.main;
            main.startLifetime = 5f;
            main.startSize = 0.11f * s;
            main.startColor = new Color(1f, 0.82f, 0.42f);
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            shape.radiusThickness = 0f;
            Velocity(ps, 0f, -0.04f, 0.04f);
            Orbit(ps, 1.3f, 1.5f);
            Fade(ps, 1f, 0.1f, 0.85f);
            var trails = ps.trails;
            trails.enabled = true;
            trails.lifetime = 0.45f;
            trails.minVertexDistance = 0.02f;
            trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            trails.inheritParticleColor = true;
            var tail = new Gradient();
            tail.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) });
            trails.colorOverTrail = tail;
            ps.GetComponent<ParticleSystemRenderer>().trailMaterial = material;
        }

        /// <summary>반짝임 (네 갈래 별빛이 커졌다 작아짐).</summary>
        private void Glints(Vector3 at, float radius, float size, float rate, Color color, Material material)
        {
            var ps = Make("TransitGlints", at, Quaternion.identity, material, ParticleSystemRenderMode.Billboard, 8, rate, rate * 3f, 1.5f);
            if (ps == null)
                return;
            var main = ps.main;
            main.startLifetime = 1.2f;
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.7f, size);
            main.startColor = color;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 0.5f);
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;
            Twinkle(ps);
        }

        /// <summary>3단계: 꽃잎이 팔랑이며 천천히 내려와 연꽃 둘레를 맴돎.</summary>
        private void Petals(Vector3 at, float radius, float s, Material material)
        {
            var ps = Make("TransitPetals", at, Quaternion.Euler(-90f, 0f, 0f), material, ParticleSystemRenderMode.Billboard, 14, 1.3f, 3.5f, 2f);
            if (ps == null)
                return;
            var main = ps.main;
            main.startLifetime = 7f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f * s, 0.15f * s);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.68f, 0.84f), new Color(1f, 0.93f, 0.95f));   // 꽃잎 재질 = 알파 섞기 (가산이면 풀밭 위에서 분홍이 빠짐)
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            Velocity(ps, 0.02f, -0.14f, -0.08f);
            Orbit(ps, 0.35f, 0.55f);
            var spin = ps.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-1.2f, 1.2f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.2f;
            noise.frequency = 0.6f;
            noise.scrollSpeed = 0.3f;
            Fade(ps, 0.85f, 0.1f, 0.8f);
        }

        /// <summary>3단계: 연꽃 속에서 금빛 꽃가루가 피어오름.</summary>
        private void Pollen(Vector3 at, float radius, float s, Material material)
        {
            var ps = Make("TransitPollen", at, Quaternion.Euler(-90f, 0f, 0f), material, ParticleSystemRenderMode.Billboard, 30, 4f, 12f, 2f);
            if (ps == null)
                return;
            var main = ps.main;
            main.startLifetime = 4f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f * s, 0.05f * s);
            main.startColor = new Color(1f, 0.86f, 0.45f);
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            Velocity(ps, 0.02f, 0.18f, 0.3f);
            Orbit(ps, 0.5f, 0.7f);
            Fade(ps, 0.9f, 0.15f, 0.6f);
        }

        /// <summary>4단계: 보라 · 파랑 · 분홍 성운 안개가 기둥 둘레를 천천히 감돎.</summary>
        private void Nebula(Vector3 padTop, float height, float s, Material material)
        {
            var ps = Make("TransitNebula", padTop + Vector3.up * (height * 0.45f), Quaternion.Euler(-90f, 0f, 0f), material, ParticleSystemRenderMode.Billboard, 8, 0.7f, 2f, 2.5f);
            if (ps == null)
                return;
            var main = ps.main;
            main.startLifetime = 8f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.8f * s, 1.3f * s);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var colors = new Gradient();
            colors.SetKeys(new[] { new GradientColorKey(new Color(0.6f, 0.4f, 1f), 0f), new GradientColorKey(new Color(0.35f, 0.55f, 1f), 0.5f), new GradientColorKey(new Color(0.9f, 0.45f, 0.9f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            main.startColor = new ParticleSystem.MinMaxGradient(colors) { mode = ParticleSystemGradientMode.RandomColor };
            Volume(ps, 1.4f * s, height * 0.8f);
            Velocity(ps, 0.01f, -0.02f, 0.02f);
            Orbit(ps, 0.25f, 0.35f);
            var spin = ps.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
            Fade(ps, 0.22f, 0.3f, 0.7f);
        }

        /// <summary>4단계: 네 갈래 별빛이 기둥 둘레에서 돌며 반짝임.</summary>
        private void Stars(Vector3 padTop, float height, float s, Material material)
        {
            var ps = Make("TransitStars", padTop + Vector3.up * (height * 0.5f), Quaternion.Euler(-90f, 0f, 0f), material, ParticleSystemRenderMode.Billboard, 20, 3.5f, 10f, 2f);
            if (ps == null)
                return;
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.18f * s, 0.3f * s);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 0.5f);
            main.startColor = new Color(0.88f, 0.88f, 1f);
            Volume(ps, 1.7f * s, height * 0.9f);
            Velocity(ps, 0f, 0.02f, 0.06f);
            Orbit(ps, 0.3f, 0.45f);
            Twinkle(ps);
        }

        // ── 공용 ──

        private ParticleSystem Make(string name, Vector3 at, Quaternion rotation, Material material, ParticleSystemRenderMode mode, int max, float idle, float ride, float rideSpeed)
        {
            if (material == null)
                return null;
            var go = new GameObject(name);
            go.transform.SetParent(_root, true);
            go.transform.SetPositionAndRotation(at, rotation);   // 세운 것 (−90° x) = 원 · 상자 모양이 바닥에 눕고 로컬 z = 위
            go.transform.localScale = Vector3.one;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.duration = 5f;
            main.prewarm = true;
            main.startSpeed = 0f;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.useUnscaledTime = true;
            var emission = ps.emission;
            emission.rateOverTime = idle;
            var shape = ps.shape;
            shape.enabled = mode != ParticleSystemRenderMode.HorizontalBillboard;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = mode;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Play();
            _parts.Add(new Part { System = ps, Idle = idle, Ride = ride, RideSpeed = rideSpeed });
            return ps;
        }

        /// <summary>세운 파티클의 속도 (로컬 z = 위, 옆으로 ±side).</summary>
        private static void Velocity(ParticleSystem ps, float side, float upMin, float upMax)
        {
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(-side, side);
            velocity.y = new ParticleSystem.MinMaxCurve(-side, side);
            velocity.z = new ParticleSystem.MinMaxCurve(upMin, upMax);
        }

        /// <summary>세운 파티클이 위 축 둘레를 돎 (rad/s) — 세 축 곡선 모양이 같아야 함.</summary>
        private static void Orbit(ParticleSystem ps, float min, float max)
        {
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.orbitalY = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.orbitalZ = new ParticleSystem.MinMaxCurve(min, max);
        }

        /// <summary>기둥 둘레 상자꼴 부피 (폭 width · 높이 height, 세운 파티클 가운데 기준).</summary>
        private static void Volume(ParticleSystem ps, float width, float height)
        {
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(width, width, height);
        }

        private static void Fade(ParticleSystem ps, float peak, float fadeIn, float hold)
        {
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peak, fadeIn), new GradientAlphaKey(peak * 0.8f, hold), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
        }

        private static void Grow(ParticleSystem ps, float from, float to)
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, from, 1f, to));
        }

        private static void Twinkle(ParticleSystem ps)
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.45f, 1f), new Keyframe(1f, 0f)));
            Fade(ps, 1f, 0.2f, 0.7f);
        }
    }
}
