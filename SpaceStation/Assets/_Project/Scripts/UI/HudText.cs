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
                case PlacementResult.DockNeedsBackContact: return "채굴 도킹은 뒷면(연결 칼라)으로 정거장에 붙여야 함";
                case PlacementResult.DockLaneBlocked: return "채굴 도킹 앞쪽 접근로가 막혀 있음 (입구 앞 2칸은 비어 있어야 함)";
                case PlacementResult.BlockedByDockLane: return "채굴 도킹 접근로 (채굴선이 드나드는 길이라 지을 수 없음)";
                case PlacementResult.NeedsSupport: return "코어 2층 옆에는 바로 붙일 수 없음 (바로 아래 1층에 모듈을 먼저 지어야 함)";
                case PlacementResult.CoreTopNeedsSideContact: return "코어 윗면에는 옆에 다른 모듈이 닿아야 설치 가능 (연결 통로 필요)";
                case PlacementResult.InsufficientResources: return "자원 부족";
                case PlacementResult.ModuleLocked: return "아직 해금되지 않은 모듈 (등급 필요)";
                case PlacementResult.LimitReached: return "현재 등급의 최대 설치 수에 도달";
                case PlacementResult.InvalidDefinition: return "잘못된 모듈 정의";
                default: return string.Empty;
            }
        }

        /// <summary>"(금속 아이콘) 20" 형식 (5-8: 자원 이름 대신 아이콘). 비용이 없으면 "무료".</summary>
        public static string Cost(IReadOnlyList<ResourceAmount> cost, float multiplier = 1f)
        {
            if (cost == null || cost.Count == 0)
                return "무료";
            var sb = new StringBuilder();
            foreach (var a in cost)
            {
                if (sb.Length > 0)
                    sb.Append("  ");
                sb.Append(HudTheme.Icon(a.Type)).Append(' ').Append((a.Amount * multiplier).ToString("0.#"));
            }
            return sb.ToString();
        }

        /// <param name="fx">Phase 6 연구 효과 (null이면 기본값). 비용·반경·수용·저장·배터리를 연구 반영 값으로 표시</param>
        public static string ModuleTooltip(ModuleData data, SpaceStation.Simulation.ResearchEffects fx = null)
        {
            var sb = new StringBuilder(256);
            sb.Append("<b>").Append(data.DisplayName).Append("</b>");
            sb.Append($"  <color={Muted}>{data.CellOffsets.Count}칸</color>\n");
            sb.Append("비용: ").Append(Cost(fx != null ? fx.BuildCost(data) : (IReadOnlyList<ResourceAmount>)data.BuildCost)).Append('\n');
            int shieldRadius = fx != null ? fx.ShieldRadius(data) : data.ShieldRadius;
            int turretRadius = fx != null ? fx.TurretRadius(data) : data.TurretRadius;
            int serviceRadius = fx != null ? fx.ServiceRadius(data) : data.ServiceRadius;
            int housing = fx != null ? fx.Housing(data) : data.HousingCapacity;
            float storage = fx != null ? fx.StorageBonus(data) : data.StorageBonus;
            float battery = fx != null ? fx.BatteryCapacity(data) : data.BatteryCapacity;

            foreach (var a in data.Production)
            {
                if (a.Type == ResourceType.Power)
                    sb.Append(data.OnDemandPower ? "전력 공급 최대 +" : "전력 공급 +").Append(a.Amount.ToString("0.#")).Append('\n');
            }
            if (data.OnDemandPower)
                sb.Append($"<color={Yellow}>다른 발전·배터리로 모자랄 때만 모자란 만큼 발전 (소비도 그 비율만큼)</color>\n");
            if (data.InputReserveRatio > 0f)
                sb.Append($"<color={Muted}>입력 자원이 저장 한도의 {data.InputReserveRatio * 100f:0}% 이하면 정지 (주민 몫 보호)</color>\n");
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
            if (battery > 0f)
                sb.Append("배터리 용량 ").Append(battery.ToString("0"))
                  .Append(" · 충·방전 초당 ").Append(data.BatteryRate.ToString("0.#")).Append('\n');
            if (housing > 0)
                sb.Append("수용 인구 +").Append(housing).Append('\n');
            if (storage > 0f)
                sb.Append("저장 한도 +").Append(storage.ToString("0")).Append(" (산소·물·식량·금속)\n");
            if (data.ResearchSlots > 0)
                sb.Append("동시 연구 +").Append(data.ResearchSlots).Append($" <color={Muted}>(선택 후 연구 창에서 시작, 파손·비활성 시 멈춤)</color>\n");
            if (data.IsCargoTerminal)
                sb.Append("화물선: ").Append(data.CargoInterval.ToString("0")).Append("초마다 가장 부족한 자원을 저장 한도의 ")
                  .Append((data.CargoFraction * 100f).ToString("0")).Append($"%만큼 <color={Muted}>(터미널마다 따로, 가동률만큼 느려짐)</color>\n");
            if (data.TerminalOnly)
                sb.Append($"<color={Yellow}>뒷면으로 정거장에 붙음 · 입구 앞 {data.ApproachLaneLength}칸은 접근로 (건설 불가)</color>\n");
            if (data.IsShield)
                sb.Append("실드: 반경 ").Append(shieldRadius).Append("칸 안 모듈로 오는 운석을 ")
                  .Append((data.ShieldReduction * 100f).ToString("0")).Append($"% 빗겨냄 <color={Muted}>(중첩 없음)</color>\n")
                  .Append($"<color={Yellow}>빗겨낸 운석 일부는 실드 범위 밖 모듈에 맞음</color>\n");
            if (data.IsTurret)
                sb.Append("포탑: 반경 ").Append(turretRadius).Append("칸 안으로 오는 운석 완전 격추 ")
                  .Append((data.TurretInterceptChance * (fx != null ? fx.TurretInterceptMultiplier : 1f) * 100f).ToString("0")).Append($"% <color={Muted}>(포탑끼리 합산, 상한 있음)</color>\n");
            if (data.IsDamageControl)
            {
                int controlRadius = fx != null ? fx.ControlRadius(data) : data.ControlRadius;
                sb.Append("손상 통제: 반경 ").Append(controlRadius).Append("칸 안 파손 모듈이 ")
                  .Append($"{data.ControlSpreadMultiplier:0.#}배 늦게 번지고, 방치 파괴까지 {data.ControlDestroyMultiplier:0.#}배 오래 버팀")
                  .Append($" <color={Muted}>(중첩 없음)</color>\n");
            }
            if (data.IsService)
                sb.Append(data.ServiceNeed.DisplayName()).Append(": 반경 ").Append(serviceRadius)
                  .Append("칸 안 거주 모듈 주민 ").Append(data.ServiceCapacity).Append($"명 담당 <color={Muted}>(파손·비활성·전력 부족·노후 시 감소)</color>\n");
            if (data.GrowthIntervalMultiplier < 0.999f)
                sb.Append("정거장 인구 증가 간격 -").Append(((1f - data.GrowthIntervalMultiplier) * 100f).ToString("0"))
                  .Append($"% <color={Muted}>(중첩 없음, 파손·비활성·전력 부족·노후 시 감소)</color>\n");
            if (data.IsDefense)
                sb.Append($"<color={Muted}>파손·비활성·전력 부족·노후 시 효과 감소</color>\n");
            if (data.MeteorWeightMultiplier > 1.001f)
                sb.Append("운석을 ").Append(data.MeteorWeightMultiplier.ToString("0.#")).Append($"배 잘 끌어당김 <color={Muted}>(외곽에 두면 다른 모듈 대신 맞음, 파손 중에는 효과 없음)</color>\n");
            if (data.Armored)
                sb.Append($"<color={Muted}>장갑: 파손돼도 산소 누출·이웃 확산 없음, 방치해도 파괴되지 않음</color>\n");
            if (data.RepairTimeMultiplier < 0.999f)
                sb.Append("수리 시간 ×").Append(data.RepairTimeMultiplier.ToString("0.##")).Append('\n');
            if (data.SpreadTimeMultiplier < 0.999f)
                sb.Append($"<color={Orange}>파손되면 이웃으로 {1f / data.SpreadTimeMultiplier:0.#}배 빨리 번짐</color>\n");
            if (data.RepairSlots > 0)
                sb.Append("동시 수리 슬롯 +").Append(data.RepairSlots).Append($" <color={Muted}>(파손·비활성 시 슬롯 없음)</color>\n");

            // 마지막 줄바꿈 제거
            if (sb.Length > 0 && sb[sb.Length - 1] == '\n')
                sb.Length--;
            return sb.ToString();
        }
    }
}
