namespace SpaceStation.Audio
{
    /// <summary>
    /// 사운드 개편 2번 B (2026-11-26, 작업자 P4): 습격 포격탄 · 실드 단계 · 포탑 단계 소리 고르기 — 순수 함수 (EditMode 시험 DefenseSoundTests), GameAudio가 부름.
    /// - 습격 포격 (방문 세력, MeteorShowerSystem.IsRaid · 이름 "포격")은 운석과 같은 바위 연출이라 소리로만 갈림: 떨어지는 휘파람 (raid_incoming) + "쿠르릉 펑" (raid_impact).
    ///   서사판 특별 유성우 ("큰 유성우" 등 같은 틀의 다른 이름)는 운석 소리 그대로.
    /// - 실드 · 포탑 단계 = 막은 실드 · 쏜 포탑 모듈의 레시피 단계 (보이는 효과와 같은 갈래): 실드 0 기본 · 1 매트릭스 (육각) · 2 실드 돔 (오로라) · 3 궤도 방어망 (수정),
    ///   포탑 0 기본 레이저 · 1 ~ 2 굵은 레이저 (보안청 · 방어 포대 · 요새 포대 · 치안 본부) · 3 궤도 방어망 프리즘 광선. 단계 칸에 클립이 없으면 아래 단계로.
    /// </summary>
    public static class DefenseSound
    {
        /// <summary>습격 포격의 화면 이름 (MeteorShowerSystem.BeginRaid 기본값).</summary>
        public const string RaidLabel = "포격";
        /// <summary>raid_incoming에서 휘파람이 끝나는 (도착) 자리 (초) — 비행 시간 − 이만큼 늦춰 울림.</summary>
        public const float RaidPeak = 1.55f;
        /// <summary>포탑이 공중에서 맞힌 포격탄 터짐의 크기 배율 (모듈에 맞으면 1).</summary>
        public const float RaidAirburstGain = 0.6f;

        /// <summary>습격 포격탄인지: 유성우 틀이 포격 중이고 이름이 "포격" (서사판 "큰 유성우"처럼 다른 이름이면 운석).</summary>
        public static bool IsRaidShell(bool isRaid, string label) => isRaid && label == RaidLabel;

        /// <summary>레시피 단계 → 소리 단계 0 ~ 3 (레시피가 없으면 0, 4단계 이상은 3).</summary>
        public static int Tier(int recipeStage) => recipeStage < 0 ? 0 : recipeStage > 3 ? 3 : recipeStage;

        /// <summary>실드가 튕겨냄 (실드 쪽 "우웅-팅"): 단계 칸에 클립이 없으면 아래 단계로 (끝 = 기본 ShieldDeflect).</summary>
        public static SoundCue ShieldCue(SoundLibrary lib, int tier)
        {
            if (lib == null)
                return null;
            for (int t = Tier(tier); t > 0; t--)
            {
                var cue = t == 3 ? lib.ShieldDeflectCrystal : t == 2 ? lib.ShieldDeflectAurora : lib.ShieldDeflectHex;
                if (cue != null && cue.HasClip)
                    return cue;
            }
            return lib.ShieldDeflect;
        }

        /// <summary>포탑 격추 레이저: 3 = 프리즘 광선 · 1 ~ 2 = 굵은 레이저 · 0 = 기본 (클립이 없으면 아래 단계로).</summary>
        public static SoundCue LaserCue(SoundLibrary lib, int tier)
        {
            if (lib == null)
                return null;
            tier = Tier(tier);
            if (tier >= 3 && lib.PrismBeam != null && lib.PrismBeam.HasClip)
                return lib.PrismBeam;
            if (tier >= 1 && lib.TurretLaser1 != null && lib.TurretLaser1.HasClip)
                return lib.TurretLaser1;
            return lib.TurretLaser;
        }

        /// <summary>날아오는 소리: 포격탄 = 휘파람 (클립이 없으면 운석 "휙").</summary>
        public static SoundCue IncomingCue(SoundLibrary lib, bool raid)
        {
            if (lib == null)
                return null;
            return raid && lib.RaidIncoming != null && lib.RaidIncoming.HasClip ? lib.RaidIncoming : lib.MeteorIncoming;
        }

        /// <summary>터지는 소리: 포격탄 = "쿠르릉 펑" (클립이 없으면 운석 갈래 — 맞은 곳 · 크기).</summary>
        public static SoundCue ImpactCue(SoundLibrary lib, bool raid, MeteorSurface surface, bool large)
        {
            if (lib == null)
                return null;
            if (raid && lib.RaidImpact != null && lib.RaidImpact.HasClip)
                return lib.RaidImpact;
            return MeteorSound.ImpactCue(lib, surface, large);
        }
    }
}
