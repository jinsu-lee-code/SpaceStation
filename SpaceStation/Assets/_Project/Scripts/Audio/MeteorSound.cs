using UnityEngine;

namespace SpaceStation.Audio
{
    /// <summary>운석이 부딪힌 곳 (SOUND_LIST_NEW D-6): 선체 = 금속 찢김 · 장갑 = 둔탁한 쿵 · 실드 = 에너지 튕김.</summary>
    public enum MeteorSurface
    {
        Hull,
        Armor,
        Shield,
    }

    /// <summary>
    /// 사운드 개편 1-1 A (2026-11-25): 운석 충돌 소리 갈래 (맞은 곳 3 × 크기 2)와 반복 막기 (D-2) — 순수 함수 (EditMode 시험 MeteorSoundTests), GameAudio가 부름.
    /// 시뮬레이션에는 운석 크기 · 피해량 차이가 없어서 (모든 운석 = 파손 1 + 같은 내구도 피해) 크기는 소리 쪽에서 정함:
    /// 큰 충돌은 LargeGap초에 한 번까지, 그 사이 (유성우 · 한 사건의 여러 발이 0.22초 간격으로 몰림)는 작은 충돌 — "쿵 툭 툭 툭 쿵 …".
    /// </summary>
    public static class MeteorSound
    {
        /// <summary>큰 충돌 사이 최소 간격 (실시간 초).</summary>
        public const float LargeGap = 1f;
        /// <summary>화면 가장자리 · 밖에서 난 소리의 크기 배율 (D-2 "화면 가운데서 멀수록 작게").</summary>
        public const float EdgeGain = 0.6f;

        /// <summary>
        /// 맞은 곳: 부딪힌 모듈이 없음 (실드에 튕긴 뒤 다른 실드에 또 막힘) = 실드 · 장갑 모듈 (8-5 장갑 격벽과 진화판) = 장갑 ·
        /// 맞았지만 파손되지 않음 (Phase 6 방어 연구 면역) = 장갑 (막아 낸 둔탁한 쿵) · 나머지 = 선체.
        /// 실드 구면에 닿는 순간 (MeteorFx.Deflected)은 GameAudio가 바로 실드로 부름.
        /// </summary>
        public static MeteorSurface Surface(bool hitModule, bool armored, bool damaged)
        {
            if (!hitModule)
                return MeteorSurface.Shield;
            return armored || !damaged ? MeteorSurface.Armor : MeteorSurface.Hull;
        }

        /// <summary>큰 충돌인지: 마지막 큰 충돌에서 gap초가 지났거나 시계가 처음부터 (now &lt; lastLargeAt — 플레이를 다시 켬)면 참.</summary>
        public static bool IsLarge(float now, float lastLargeAt, float gap = LargeGap)
            => now - lastLargeAt >= gap || now < lastLargeAt;

        /// <summary>
        /// 균열 (module_damaged)을 덧댈지: 파손된 장갑만 (둔탁한 쿵에는 찢김이 없음). 선체 충돌은 소리에 찢김이 이미 들어 있어 덧대지 않음
        /// (D-2 같은 때 여러 개 = 한 번 — 유성우에 충돌 · 균열 · 경보 셋이 겹쳤음).
        /// </summary>
        public static bool CrackOnImpact(MeteorSurface surface, bool damaged)
            => damaged && surface == MeteorSurface.Armor;

        /// <summary>화면 자리 (뷰포트 x · y 0 ~ 1, z = 카메라 앞 거리)로 크기 배율: 가운데 1 → 가장자리 · 밖 · 카메라 뒤 EdgeGain.</summary>
        public static float CenterGain(Vector3 viewport, float edge = EdgeGain)
        {
            if (viewport.z < 0f)
                return edge;
            float d = 2f * Mathf.Max(Mathf.Abs(viewport.x - 0.5f), Mathf.Abs(viewport.y - 0.5f));
            return Mathf.Lerp(1f, edge, Mathf.Clamp01(d));
        }

        /// <summary>갈래 칸 고르기 — 그 칸에 클립이 없으면 옛 한 칸 (fallback = MeteorImpact).</summary>
        public static SoundCue ImpactCue(SoundLibrary lib, MeteorSurface surface, bool large)
        {
            if (lib == null)
                return null;
            SoundCue cue;
            switch (surface)
            {
                case MeteorSurface.Armor: cue = large ? lib.MeteorImpactArmorL : lib.MeteorImpactArmorS; break;
                case MeteorSurface.Shield: cue = large ? lib.MeteorImpactShieldL : lib.MeteorImpactShieldS; break;
                default: cue = large ? lib.MeteorImpactHullL : lib.MeteorImpactHullS; break;
            }
            return cue != null && cue.HasClip ? cue : lib.MeteorImpact;
        }
    }
}
