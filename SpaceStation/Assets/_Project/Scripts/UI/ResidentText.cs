using System.Text;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;

namespace SpaceStation.UI
{
    /// <summary>Phase 10 주민 표시 문구 (명단 창·선택 패널·알림 공용).</summary>
    public static class ResidentText
    {
        public const string Good = "#7CFF9A";
        public const string Bad = "#FF8A7A";

        /// <summary>"두부 기관사" · "두부 선임 기관사": 이름 + 직함 (11-11d, 직함은 특성에서 + 승진 단계 — <see cref="ResidentConfig.TitleOf(System.Collections.Generic.IEnumerable{ResidentTrait}, int)"/>).</summary>
        public static string FullName(ResidentConfig config, Resident r)
        {
            string title = config != null ? config.TitleOf(r.Traits, r.Rank) : null;
            return string.IsNullOrEmpty(title) ? r.Name : $"{r.Name} {title}";
        }

        public static string TraitName(ResidentConfig config, ResidentTrait trait)
        {
            var def = config != null ? config.Get(trait) : null;
            string name = def != null ? def.DisplayName : trait.ToString();
            return $"<color={(def == null || def.Positive ? Good : Bad)}>{name}</color>";
        }

        /// <summary>13-3 공통 고리: 주민 종류 표시 (예: "토끼", null = 표시 없음). 서사판 종족이 정함 — 특성 앞에 "종류 | 개인"으로.</summary>
        public static System.Func<Resident, string> KindTag;

        /// <summary>"기술자·낙천가" (색 포함). 종류 표시가 있으면 "토끼 | 기술자·낙천가".</summary>
        public static string Traits(ResidentConfig config, Resident r)
        {
            var sb = new StringBuilder();
            string kind = KindTag?.Invoke(r);
            if (!string.IsNullOrEmpty(kind))
                sb.Append("<color=#E8D9B5>").Append(kind).Append("</color><color=#9AA4B2> | </color>");
            int head = sb.Length;
            foreach (var t in r.Traits)
            {
                if (sb.Length > head)
                    sb.Append("<color=#9AA4B2>·</color>");
                sb.Append(TraitName(config, t));
            }
            if (sb.Length == head)
                sb.Append($"<color={HudText.Muted}>특성 없음</color>");
            return sb.ToString();
        }

        public static string Reason(PopulationChangeReason? reason)
        {
            switch (reason)
            {
                case PopulationChangeReason.OxygenDepleted: return "산소 고갈";
                case PopulationChangeReason.LowSatisfaction: return "만족도 낮음";
                case PopulationChangeReason.Overcrowded: return "수용 인구 초과";
                case PopulationChangeReason.Crisis: return "사회 위기";
                case PopulationChangeReason.Growth: return "새로 도착";
                default: return null;
            }
        }

        /// <summary>집 이름: "거주 모듈 #3" (번호 = 명단의 집 순서), 없으면 "집 없음".</summary>
        public static string HomeName(ResidentRoster roster, ModuleInstance home)
        {
            if (home == null)
                return $"<color={Bad}>집 없음</color>";
            int index = -1;
            var homes = roster.Homes;
            for (int i = 0; i < homes.Count; i++)
                if (homes[i] == home)
                    index = i;
            string name = home.Data != null ? home.Data.DisplayName : "거주";
            return index >= 0 ? $"{name} #{index + 1}" : name;
        }

        /// <summary>집 환경 태그 (그 주민 기준 좋음/나쁨 색, resident가 null이면 중립).</summary>
        public static string Environment(HomeEnvironment env, Resident resident = null)
        {
            var sb = new StringBuilder();
            void Tag(HomeEnvironment flag, string label, bool good, bool bad)
            {
                if ((env & flag) == 0)
                    return;
                string color = good ? Good : bad ? Bad : HudText.Muted;
                if (sb.Length > 0)
                    sb.Append(' ');
                sb.Append($"<color={color}>{label}</color>");
            }
            bool Has(ResidentTrait t) => resident != null && resident.Has(t);
            Tag(HomeEnvironment.Radiation, "방사선", Has(ResidentTrait.RadiationTolerant), Has(ResidentTrait.RadiationSensitive));
            Tag(HomeEnvironment.Noise, "소음", false, Has(ResidentTrait.NoiseSensitive));
            Tag(HomeEnvironment.Nature, "녹지", Has(ResidentTrait.NatureLover), false);
            Tag(HomeEnvironment.Gravity, "중력", Has(ResidentTrait.GravityLover), false);
            Tag(HomeEnvironment.Social, "여가", Has(ResidentTrait.Sociable), false);
            return sb.ToString();
        }

        public static string Signed(float v) => v > 0f ? $"+{v:0.#}" : $"{v:0.#}";
    }
}
