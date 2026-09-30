using System.Collections.Generic;
using System.Text;
using SpaceStation.Core;
using SpaceStation.Data;

namespace SpaceStation.UI
{
    /// <summary>HUD 표시 문자열 (자원 이름, 배치 불가 사유, 모듈 툴팁).</summary>
    public static class HudText
    {
        public const string Red = "#FF5A5A";
        public const string Yellow = "#FFD24A";
        public const string Orange = "#FFA040";
        public const string Muted = "#9AA4B2";

        public static string ResourceName(ResourceType type) => type.DisplayName();

        public static string PlacementReason(PlacementResult result)
        {
            switch (result)
            {
                case PlacementResult.Occupied: return "이미 사용 중인 공간";
                case PlacementResult.TerminalNeedsSingleContact: return "채굴 도킹은 정거장과 한 면만 맞닿아야 함";
                case PlacementResult.BlockedByTerminal: return "채굴 도킹의 다른 면에는 붙일 수 없음";
                case PlacementResult.InsufficientResources: return "자원 부족";
                case PlacementResult.ModuleLocked: return "아직 해금되지 않은 모듈 (등급 필요)";
                case PlacementResult.LimitReached: return "현재 등급의 최대 설치 수에 도달";
                case PlacementResult.InvalidDefinition: return "잘못된 모듈 정의";
                default: return string.Empty;
            }
        }

        /// <summary>"금속 20" 형식. 비용이 없으면 "무료".</summary>
        public static string Cost(IReadOnlyList<ResourceAmount> cost, float multiplier = 1f)
        {
            if (cost == null || cost.Count == 0)
                return "무료";
            var sb = new StringBuilder();
            foreach (var a in cost)
            {
                if (sb.Length > 0)
                    sb.Append(", ");
                sb.Append(ResourceName(a.Type)).Append(' ').Append((a.Amount * multiplier).ToString("0.#"));
            }
            return sb.ToString();
        }

        public static string ModuleTooltip(ModuleData data)
        {
            var sb = new StringBuilder(256);
            sb.Append("<b>").Append(data.DisplayName).Append("</b>");
            sb.Append($"  <color={Muted}>{data.CellOffsets.Count}칸</color>\n");
            sb.Append("비용: ").Append(Cost(data.BuildCost)).Append('\n');

            foreach (var a in data.Production)
            {
                if (a.Type == ResourceType.Power)
                    sb.Append("전력 공급 +").Append(a.Amount.ToString("0.#")).Append('\n');
            }
            foreach (var a in data.Consumption)
            {
                if (a.Type == ResourceType.Power)
                    sb.Append("전력 수요 -").Append(a.Amount.ToString("0.#")).Append('\n');
            }
            foreach (var a in data.Production)
            {
                if (a.Type != ResourceType.Power)
                    sb.Append("생산: ").Append(ResourceName(a.Type)).Append(" +").Append(a.Amount.ToString("0.##")).Append("/s\n");
            }
            foreach (var a in data.Consumption)
            {
                if (a.Type != ResourceType.Power)
                    sb.Append("소비: ").Append(ResourceName(a.Type)).Append(" -").Append(a.Amount.ToString("0.##")).Append("/s\n");
            }
            if (data.SolarPowered)
                sb.Append($"<color={Yellow}>낮/밤 주기에 따라 발전 (밤에는 발전 안 함)</color>\n");
            if (data.BatteryCapacity > 0f)
                sb.Append("배터리 용량 ").Append(data.BatteryCapacity.ToString("0"))
                  .Append(" · 충·방전 초당 ").Append(data.BatteryRate.ToString("0.#")).Append('\n');
            if (data.HousingCapacity > 0)
                sb.Append("수용 인구 +").Append(data.HousingCapacity).Append('\n');
            if (data.StorageBonus > 0f)
                sb.Append("저장 한도 +").Append(data.StorageBonus.ToString("0")).Append(" (산소·물·식량·금속)\n");
            if (data.TerminalOnly)
                sb.Append($"<color={Yellow}>말단 배치 전용: 정거장과 한 면만 맞닿아야 함</color>\n");
            if (data.IsShield)
                sb.Append("실드: 반경 ").Append(data.ShieldRadius).Append("칸 안 모듈로 오는 운석을 ")
                  .Append((data.ShieldReduction * 100f).ToString("0")).Append($"% 빗겨냄 <color={Muted}>(중첩 없음)</color>\n")
                  .Append($"<color={Yellow}>빗겨낸 운석 일부는 실드 범위 밖 모듈에 맞음</color>\n");
            if (data.IsTurret)
                sb.Append("포탑: 반경 ").Append(data.TurretRadius).Append("칸 안으로 오는 운석 완전 격추 ")
                  .Append((data.TurretInterceptChance * 100f).ToString("0")).Append($"% <color={Muted}>(포탑끼리 합산, 상한 있음)</color>\n");
            if (data.IsService)
                sb.Append(data.ServiceNeed.DisplayName()).Append(": 반경 ").Append(data.ServiceRadius)
                  .Append("칸 안 거주 모듈 주민 ").Append(data.ServiceCapacity).Append($"명 담당 <color={Muted}>(파손·비활성·전력 부족·노후 시 감소)</color>\n");
            if (data.IsDefense)
                sb.Append($"<color={Muted}>파손·비활성·전력 부족·노후 시 효과 감소</color>\n");
            if (data.RepairSlots > 0)
                sb.Append("동시 수리 슬롯 +").Append(data.RepairSlots).Append($" <color={Muted}>(파손·비활성 시 슬롯 없음)</color>\n");

            // 마지막 줄바꿈 제거
            if (sb.Length > 0 && sb[sb.Length - 1] == '\n')
                sb.Length--;
            return sb.ToString();
        }
    }
}
