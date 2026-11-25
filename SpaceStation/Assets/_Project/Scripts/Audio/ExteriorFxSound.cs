using UnityEngine;

namespace SpaceStation.Audio
{
    /// <summary>
    /// 사운드 개편 3번 · D-4 · D-12 (2026-11-26, 작업자 P4): 바깥 화면에서 진화 연출 · 오가는 배 소리를 얼마나 크게 · 먹먹하게 낼지 — 순수 함수 (EditMode 시험 ExteriorFxSoundTests).
    /// 바깥 연출은 멀리서 들리는 소리라 카메라 가까이 (줌) · 화면 가운데에 있을 때만 들리고, 멀수록 고음을 더 걷음 (벽 너머 같은 먹먹함).
    /// 카메라 거리는 OrbitCameraRig 3 ~ 80 (기본 12 — 이 거리에서 약 0.8).
    /// </summary>
    public static class ExteriorFxSound
    {
        /// <summary>이 거리 (카메라 ~ 연출, 월드 단위) 안 = 그대로.</summary>
        public const float Near = 9f;
        /// <summary>이 거리 밖 = 들리지 않음.</summary>
        public const float Far = 34f;
        /// <summary>화면 가장자리에서의 배율 (가운데 1), 가장자리 밖은 EdgeOut까지 0으로 내려감.</summary>
        public const float Edge = 0.2f;
        /// <summary>뷰포트 거리 (가운데 0 · 가장자리 1) 이 값에서 0 — 화면 밖으로 조금 나간 것까지 잦아들며 들림.</summary>
        public const float EdgeOut = 1.35f;
        /// <summary>가까이 · 멀리의 로우패스 (Hz) — 가까워도 먹먹하게 (바깥은 우주 너머 소리).</summary>
        public const float CutoffNear = 4200f, CutoffFar = 1100f;

        /// <summary>거리 배율: Near 안 1 → Far 밖 0 (제곱으로 — 줌 아웃하면 빨리 작아짐).</summary>
        public static float DistanceGain(float distance)
        {
            float k = 1f - Mathf.Clamp01((distance - Near) / (Far - Near));
            return k * k;
        }

        /// <summary>화면 자리 배율: 카메라 뒤 = 0, 가운데 1 → 가장자리 Edge → 가장자리 밖 EdgeOut에서 0.</summary>
        public static float CenterGain(Vector3 viewport)
        {
            if (viewport.z <= 0f)
                return 0f;
            float d = 2f * Mathf.Max(Mathf.Abs(viewport.x - 0.5f), Mathf.Abs(viewport.y - 0.5f));
            if (d <= 1f)
                return Mathf.Lerp(1f, Edge, d);
            return Mathf.Lerp(Edge, 0f, Mathf.Clamp01((d - 1f) / (EdgeOut - 1f)));
        }

        /// <summary>바깥 연출 소리 크기 (0 ~ 1) = 거리 배율 × 화면 자리 배율.</summary>
        public static float Level(float distance, Vector3 viewport) => DistanceGain(distance) * CenterGain(viewport);

        /// <summary>먹먹함 (로우패스 Hz): 가까울수록 밝게.</summary>
        public static float Cutoff(float distance)
            => Mathf.Lerp(CutoffNear, CutoffFar, Mathf.Clamp01((distance - Near) / (Far - Near)));

        /// <summary>화면 좌우 (GameAudio와 같은 폭).</summary>
        public static float Pan(Vector3 viewport) => viewport.z <= 0f ? 0f : Mathf.Clamp((viewport.x - 0.5f) * 1.3f, -0.65f, 0.65f);

        /// <summary>배 엔진 크기: 서 있어도 옅게 (0.3) 웅 · 빠를수록 커짐 (월드 단위 / 초, 8에서 1).</summary>
        public static float EngineLevel(float speed) => Mathf.Clamp01(0.3f + 0.7f * Mathf.Clamp01(speed / 8f));

        /// <summary>배 엔진 재생 높이: 배 크기 기준 (작은 배 높게) × 빠를수록 조금 높게 (0.92 ~ 1.08).</summary>
        public static float EnginePitch(float speed, float size) => size * Mathf.Lerp(0.92f, 1.08f, Mathf.Clamp01(speed / 10f));

        /// <summary>동시에 낼 바깥 연출 루프 수 (D-2 — 진화 모듈이 많아도 시끄럽지 않게, 가장 큰 것부터).</summary>
        public const int MaxLoops = 2;
        /// <summary>소리를 내는 진화 모듈 자리 수 (D-2 — 줌한 자리 둘레 연출이 한꺼번에 울리지 않게, 가장 큰 자리부터).</summary>
        public const int MaxSites = 2;
        /// <summary>이 크기 아래 자리는 소리 없음 (화면 가장자리 · 먼 자리).</summary>
        public const float MinSiteLevel = 0.15f;
    }
}
