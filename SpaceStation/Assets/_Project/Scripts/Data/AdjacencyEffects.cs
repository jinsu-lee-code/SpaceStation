using System;
using System.Text;

namespace SpaceStation.Data
{
    /// <summary>
    /// CONNECT_1121 A5 · A8 인접 관계의 안쪽 얼굴 (3번 ④ V2, 2026-11-28 — 관계 하나가 바깥 수치와 안쪽 상황을 함께 가짐).
    /// 그 관계의 대상 방에서 생긴 사고가 이 표시대로 달라진다 (원인 · 과정 · 결과). 새 값은 끝에 (비트 유지).
    /// </summary>
    [Flags]
    public enum InnerFace
    {
        None = 0,
        /// <summary>누출 때 옆 수원 방 문가에 차단 밸브가 하나 더 (어느 쪽이든 잠그면 해결) + 놓쳐도 물 회수.</summary>
        LeakNeighborValve = 1 << 0,
        /// <summary>화재 때 정비 베이 쪽 문 옆에 소화기가 하나 더.</summary>
        FireDoorExtinguisher = 1 << 1,
        /// <summary>화재 때 불길 두 곳 · 처리 시간 짧음 (나쁜 이웃 — 인화 위험).</summary>
        FireTwoFlames = 1 << 2,
        /// <summary>차단기 때 내려간 스위치 하나만.</summary>
        BreakerOneSwitch = 1 << 3,
        /// <summary>차단기 때 정상 스위치를 눌러도 처음으로 안 돌아감 (연구소 = 순서 힌트, 사용자 결정 2026-11-28).</summary>
        BreakerForgiving = 1 << 4,
        /// <summary>차단기를 놓쳐도 손실 없음 (이웃 실드가 받쳐 줌).</summary>
        BreakerBacked = 1 << 5,
        /// <summary>환기 경보 때 필터 교체 없이 환기구만 (짧게).</summary>
        FilterEasy = 1 << 6,
        /// <summary>환기 경보 때 필터 두 장 (나쁜 이웃 — 소음원 먼지).</summary>
        FilterTwo = 1 << 7,
        /// <summary>그 방에서 환기 경보가 뽑힐 확률 2배 (나쁜 이웃, 사용자 결정 2026-11-28 — 전체 빈도는 그대로).</summary>
        FilterFrequent = 1 << 8,
        /// <summary>환기 경보가 방사선 경보 모습으로 (노란 띠 · 제목).</summary>
        FilterRadiation = 1 << 9,
        /// <summary>놓친 사고의 만족도 손실을 막음 (의료).</summary>
        MissNoMood = 1 << 10,
        /// <summary>놓친 사고를 대신 처리 — 손실 없음 (코어).</summary>
        MissNoLoss = 1 << 11,
        /// <summary>놓친 사고 손실 절반 (손상 통제실 ← 정비).</summary>
        MissHalf = 1 << 12,
        /// <summary>놓친 사고가 이웃 방으로 번지지 않음 (손상 통제실).</summary>
        NoSpread = 1 << 13,

        // V3 (2026-11-28): 현장 수리 · 주민 부탁
        /// <summary>현장 수리 F 시간 짧게 (정비 베이).</summary>
        RepairQuick = 1 << 14,
        /// <summary>현장 수리 손상 지점 하나 적게 (창고 공구 · 장갑 — 여럿이어도 하나, 최소 1).</summary>
        RepairFewer = 1 << 15,
        /// <summary>가져다주기 물건이 그 이웃 방에 (옆집 · 옆방).</summary>
        FetchNearby = 1 << 16,
        /// <summary>가져다주기 물건을 이 방에서 바로 (상점).</summary>
        FetchHere = 1 << 17,
        /// <summary>말동무 보상 ↑ (옆집 이웃).</summary>
        TalkBonus = 1 << 18,
        /// <summary>부탁 보상 ↑.</summary>
        RequestBonus = 1 << 19,
        /// <summary>부탁이 가벼움 (F 짧게).</summary>
        RequestLight = 1 << 20,
        /// <summary>부탁 보상 ↓ (나쁜 이웃 — 소음 · 북적임).</summary>
        RequestPenalty = 1 << 21,
        /// <summary>소음 민원 (나쁜 이웃 — 옆 소음원 방에 흡음 덮개 달기, 사용자 결정 2026-11-28).</summary>
        NoiseComplaint = 1 << 22,
    }

    /// <summary>
    /// CONNECT_1121 A14-2 인접 효과 종류의 성질 (2026-11-28, 3번 ④ V1): 더하는 값인지 · 낮을수록 좋은지 · 화면 이름 · 그 모듈에 뜻이 있는지.
    /// 기존 셋(생산 · 소비 · 수용)은 예전 그대로 (뜻 검사 없음).
    /// </summary>
    public static class AdjacencyEffects
    {
        /// <summary>바깥 효과 종류 수 (<see cref="AdjacencyEffect.DecodeSpeed"/> + 1 — <see cref="AdjacencyEffect.InnerOnly"/>는 바깥 값이 없어 뺌).</summary>
        public const int Count = (int)AdjacencyEffect.DecodeSpeed + 1;

        /// <summary>나쁜 이웃이 주는 안쪽 얼굴 (그 밖은 좋은 이웃).</summary>
        public const InnerFace BadInner = InnerFace.FireTwoFlames | InnerFace.FilterTwo | InnerFace.FilterFrequent | InnerFace.FilterRadiation
                                          | InnerFace.RequestPenalty | InnerFace.NoiseComplaint;

        /// <summary>안쪽 얼굴 짧은 설명 ("누출 때 옆방에서도 잠금 · 물 회수"), 여럿이면 " · "로.</summary>
        public static string InnerText(InnerFace face)
        {
            if (face == InnerFace.None)
                return string.Empty;
            var sb = new StringBuilder();
            void Add(InnerFace f, string text)
            {
                if ((face & f) == 0)
                    return;
                if (sb.Length > 0)
                    sb.Append(" · ");
                sb.Append(text);
            }
            Add(InnerFace.LeakNeighborValve, "누출 때 옆방에서도 잠금 · 물 회수");
            Add(InnerFace.FireDoorExtinguisher, "화재 때 문 옆 소화기 하나 더");
            Add(InnerFace.FireTwoFlames, "화재 때 불길 두 곳 · 시간 짧음");
            Add(InnerFace.BreakerOneSwitch, "차단기 때 스위치 하나만");
            Add(InnerFace.BreakerForgiving, "차단기 때 틀려도 처음으로 안 돌아감");
            Add(InnerFace.BreakerBacked, "차단기를 놓쳐도 손실 없음");
            Add(InnerFace.FilterEasy, "환기 경보 때 필터 교체 없이 환기구만");
            Add(InnerFace.FilterTwo, "환기 경보 때 필터 두 장");
            Add(InnerFace.FilterFrequent, "환기 경보가 잦음");
            Add(InnerFace.FilterRadiation, "방사선 경보");
            Add(InnerFace.MissNoMood, "놓친 사고의 만족도 손실 막음");
            Add(InnerFace.MissNoLoss, "놓친 사고를 대신 처리");
            Add(InnerFace.MissHalf, "놓친 사고 손실 절반");
            Add(InnerFace.NoSpread, "놓친 사고가 번지지 않음");
            Add(InnerFace.RepairQuick, "현장 수리 빨리 (F 짧게)");
            Add(InnerFace.RepairFewer, "현장 수리 손상 지점 하나 적게");
            Add(InnerFace.FetchNearby, "가져다주기 물건이 바로 옆방에");
            Add(InnerFace.FetchHere, "가져다주기 물건을 이 방에서 바로");
            Add(InnerFace.TalkBonus, "말동무 보상 ↑");
            Add(InnerFace.RequestLight, "부탁이 가벼움");
            Add(InnerFace.RequestBonus, "부탁 보상 ↑");
            Add(InnerFace.RequestPenalty, "부탁 보상 ↓");
            Add(InnerFace.NoiseComplaint, "소음 민원");
            return sb.ToString();
        }

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
                case AdjacencyEffect.InnerOnly: return "안쪽";
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
