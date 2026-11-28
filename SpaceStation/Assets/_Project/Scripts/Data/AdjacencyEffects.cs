namespace SpaceStation.Data
{
    /// <summary>
    /// CONNECT_1121 A14-2 인접 효과 종류의 성질 (2026-11-28, 3번 ④ V1): 더하는 값인지 · 낮을수록 좋은지 · 화면 이름 · 그 모듈에 뜻이 있는지.
    /// 기존 셋(생산 · 소비 · 수용)은 예전 그대로 (뜻 검사 없음).
    /// </summary>
    public static class AdjacencyEffects
    {
        /// <summary>효과 종류 수 (<see cref="AdjacencyEffect"/> 마지막 + 1).</summary>
        public const int Count = (int)AdjacencyEffect.DecodeSpeed + 1;

        /// <summary>합을 그대로 더하는 효과 (수용 +1 · 승객 +1 · 정거장 연구 속도 +10%). 나머지는 (1 + 합) 배율.</summary>
        public static bool IsAdditive(AdjacencyEffect e)
            => e == AdjacencyEffect.Housing || e == AdjacencyEffect.Passengers || e == AdjacencyEffect.ResearchSpeed;

        /// <summary>값이 %로 읽히는 효과 (사람 수인 수용 · 승객만 아님).</summary>
        public static bool IsPercent(AdjacencyEffect e) => e != AdjacencyEffect.Housing && e != AdjacencyEffect.Passengers;

        /// <summary>작을수록 좋은 효과 (소비 · 역병 유입 · 수리 시간 · 정책 대기 · 피격 · 튕김).</summary>
        public static bool LowerIsBetter(AdjacencyEffect e)
        {
            switch (e)
            {
                case AdjacencyEffect.Consumption:
                case AdjacencyEffect.PlagueChance:
                case AdjacencyEffect.RepairTime:
                case AdjacencyEffect.PolicyDelay:
                case AdjacencyEffect.HitWeight:
                case AdjacencyEffect.RicochetChance:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>화면 이름 (생산 · 소비는 모듈의 자원 이름을 쓰므로 여기선 일반 이름).</summary>
        public static string Name(AdjacencyEffect e)
        {
            switch (e)
            {
                case AdjacencyEffect.Production: return "생산";
                case AdjacencyEffect.Consumption: return "소비";
                case AdjacencyEffect.Housing: return "수용 인구";
                case AdjacencyEffect.ChargeRate: return "충방전 속도";
                case AdjacencyEffect.SpreadTime: return "번지기까지 시간";
                case AdjacencyEffect.ServiceCapacity: return "서비스 정원";
                case AdjacencyEffect.Passengers: return "이민선 승객";
                case AdjacencyEffect.PlagueChance: return "역병 유입 확률";
                case AdjacencyEffect.RepairTime: return "수리 시간";
                case AdjacencyEffect.CargoAmount: return "화물 양";
                case AdjacencyEffect.ResearchSpeed: return "연구 속도";
                case AdjacencyEffect.PolicyDelay: return "정책 시행 대기";
                case AdjacencyEffect.InterceptChance: return "격추 확률";
                case AdjacencyEffect.HitWeight: return "운석 피격";
                case AdjacencyEffect.RicochetChance: return "튕긴 운석이 맞을 확률";
                case AdjacencyEffect.RelayPower: return "통신 세기";
                case AdjacencyEffect.DecodeSpeed: return "신호 해독 속도";
                default: return e.ToString();
            }
        }

        /// <summary>
        /// 그 모듈에 이 효과가 뜻이 있는지 — 태그 · 모든 모듈 규칙이 엉뚱한 모듈에 붙지 않게 (예: 정숙 ← 소음원의 서비스 정원은 의료실 · 학교만,
        /// 연구 속도는 연구소 · 데이터 센터만). 기존 셋은 늘 참 (예전 결과 그대로).
        /// </summary>
        public static bool Relevant(AdjacencyEffect e, ModuleData data)
        {
            if (data == null)
                return false;
            switch (e)
            {
                case AdjacencyEffect.ChargeRate: return data.BatteryRate > 0f;
                case AdjacencyEffect.ServiceCapacity: return data.IsService;
                case AdjacencyEffect.Passengers: return data.ImmigrationPort;
                case AdjacencyEffect.PlagueChance: return data.Serves(ResidentNeed.Quarantine);
                case AdjacencyEffect.CargoAmount: return data.IsCargoTerminal;
                case AdjacencyEffect.ResearchSpeed: return data.ResearchSlots > 0 || data.ResearchSpeedBonus > 0f;
                case AdjacencyEffect.PolicyDelay: return data.Serves(ResidentNeed.Administration);
                case AdjacencyEffect.InterceptChance: return data.IsTurret;
                case AdjacencyEffect.RicochetChance: return data.IsShield;
                case AdjacencyEffect.RelayPower:
                case AdjacencyEffect.DecodeSpeed: return data.RelayPower > 0;
                default: return true; // 생산 · 소비 · 수용 · 번짐 · 수리 시간 · 피격 = 어느 모듈이든
            }
        }

        /// <summary>미리보기 아이콘 이름 (HUD_Icons — 색이 아니라 모양으로 구분, 색약 배려). 자원 효과는 호출자가 자원 아이콘을 씀.</summary>
        public static string IconName(AdjacencyEffect e)
        {
            switch (e)
            {
                case AdjacencyEffect.ChargeRate: return "battery";
                case AdjacencyEffect.SpreadTime: return "warning";
                case AdjacencyEffect.ServiceCapacity: return "satisfaction";
                case AdjacencyEffect.PlagueChance: return "event";
                case AdjacencyEffect.RepairTime: return "repair";
                case AdjacencyEffect.CargoAmount: return "module";
                case AdjacencyEffect.ResearchSpeed:
                case AdjacencyEffect.DecodeSpeed: return "research";
                case AdjacencyEffect.PolicyDelay: return "grade";
                case AdjacencyEffect.InterceptChance:
                case AdjacencyEffect.HitWeight:
                case AdjacencyEffect.RicochetChance: return "shield";
                case AdjacencyEffect.RelayPower: return "info";
                default: return "population";
            }
        }
    }
}
