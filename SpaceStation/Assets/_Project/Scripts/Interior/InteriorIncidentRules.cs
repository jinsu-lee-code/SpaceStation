using System;
using System.Collections.Generic;
using SpaceStation.Data;
using SpaceStation.Simulation;

namespace SpaceStation.Interior
{
    /// <summary>11-17 ④ 차단기 스위치를 눌렀을 때.</summary>
    public enum SwitchPress
    {
        /// <summary>경고등 켜진 스위치를 올림 (아직 남음).</summary>
        Fixed,
        /// <summary>마지막 스위치까지 올림 — 해결.</summary>
        AllDone,
        /// <summary>멀쩡한 스위치를 건드림 — 스파크 후 처음부터.</summary>
        Wrong,
    }

    /// <summary>
    /// 11-17 ④ 내부 사고 규칙 (순수 계산, 테스트 대상). 간격 · 처리 시간 · 손실은 BALANCE 31번(<see cref="BalanceConfig"/>).
    /// - 종류: 같은 종류가 연달아 나오지 않게, 차단기는 생산하는 방에서만 (손실 = 생산 배율이라)
    /// - 차단기 미니게임: 스위치 6개 중 2~3개가 내려가고 경고등이 켜짐 → 켜진 것만 올리기, 시간 제한 없음, 틀리면 처음 상태로
    /// </summary>
    public static class InteriorIncidentRules
    {
        public const int Switches = 6;

        /// <summary>종류 고르기 (기본 방): 할 수 있는 것 중 직전과 다른 것 (하나뿐이면 그것).</summary>
        /// <param name="r">0~1 난수</param>
        public static IncidentKind Pick(bool breakerOk, IncidentKind? last, double r)
            => Pick(IncidentCatalog.KindsForStage(0), breakerOk, last, r) ?? IncidentKind.Leak;

        /// <summary>
        /// CONNECT A6 단계별 상황: kinds(그 단계 방의 상황) 가운데 직전과 다른 것. 차단기는 breakerOk일 때만. 고를 것이 없으면 null.
        /// </summary>
        public static IncidentKind? Pick(IReadOnlyList<IncidentKind> kinds, bool breakerOk, IncidentKind? last, double r)
        {
            var pool = new List<IncidentKind>(4);
            foreach (var k in kinds)
            {
                if (k == IncidentKind.Breaker && !breakerOk)
                    continue;
                if (last.HasValue && k == last.Value && kinds.Count > 1)
                    continue;
                pool.Add(k);
            }
            if (pool.Count == 0)
                return null;
            return pool[Math.Min(pool.Count - 1, (int)(Math.Max(0.0, r) * pool.Count))];
        }

        /// <summary>차단기는 무언가를 만들거나 전력을 쓰는 방에서만 (손실이 생산 · 가동 배율) — 제련소처럼 생산 목록이 비어 있는 변환 모듈도 전력을 씀.</summary>
        public static bool CanTrip(ModuleData data)
        {
            if (data == null)
                return false;
            foreach (var p in data.Production)
            {
                if (p.Amount > 0f)
                    return true;
            }
            foreach (var c in data.Consumption)
            {
                if (c.Type == ResourceType.Power && c.Amount > 0f)
                    return true;
            }
            return false;
        }

        /// <summary>새는 자원: 물을 만들거나 쓰는 방이면 물, 아니면 산소.</summary>
        public static ResourceType LeakResource(ModuleData data)
        {
            if (data != null)
            {
                foreach (var p in data.Production)
                {
                    if (p.Type == ResourceType.Water && p.Amount > 0f)
                        return ResourceType.Water;
                }
                foreach (var c in data.Consumption)
                {
                    if (c.Type == ResourceType.Water && c.Amount > 0f)
                        return ResourceType.Water;
                }
            }
            return ResourceType.Oxygen;
        }

        /// <summary>내려간 스위치(경고등 켜짐) 2~3개.</summary>
        public static bool[] TripPattern(Random rng) => TripPattern(rng, 2 + rng.Next(2));

        /// <summary>내려간 스위치 count개 (CONNECT A5 배터리 옆 방 = 1개).</summary>
        public static bool[] TripPattern(Random rng, int count)
        {
            var lit = new bool[Switches];
            count = Math.Max(1, Math.Min(Switches, count));
            while (count > 0)
            {
                int i = rng.Next(Switches);
                if (lit[i])
                    continue;
                lit[i] = true;
                count--;
            }
            return lit;
        }

        /// <summary>스위치 하나 누름: 켜진 것이면 끄고(올림), 아니면 틀림 (부르는 쪽이 처음 상태로 되돌림).</summary>
        public static SwitchPress Press(bool[] lit, int index)
        {
            if (index < 0 || index >= lit.Length || !lit[index])
                return SwitchPress.Wrong;
            lit[index] = false;
            foreach (bool l in lit)
            {
                if (l)
                    return SwitchPress.Fixed;
            }
            return SwitchPress.AllDone;
        }

        // ---------------- CONNECT A6 3 · 4단계 (사용자 결정 2026-11-27) ----------------

        /// <summary>3단계 ① 물꼬 판: 바퀴 셋 · 단 1 ~ 5 (바이오스피어 물꼬 맞추기와 같은 단 — <see cref="SluicePuzzle"/>). 4단계 ① 신호 다이얼도 같은 칸 수.</summary>
        public const int SluiceWheels = 3, Notches = 5, SignalDials = 2;

        /// <summary>목표 칸 (1 ~ <see cref="Notches"/>)과 처음 칸 — 처음은 목표와 다름 (이미 맞아 있으면 할 일이 없음).</summary>
        public static int[] NotchTargets(Random rng, int count, out int[] start)
        {
            var target = new int[count];
            start = new int[count];
            for (int i = 0; i < count; i++)
            {
                target[i] = 1 + rng.Next(Notches);
                start[i] = 1 + (target[i] - 1 + 1 + rng.Next(Notches - 1)) % Notches;   // 목표가 아닌 칸 하나
            }
            return target;
        }

        /// <summary>F 한 번 = 한 칸 (마지막 다음은 첫 칸 — 물꼬는 한 단 내림, 다이얼은 한 칸 돌림).</summary>
        public static int TurnNotch(int level) => level % Notches + 1;

        public static bool NotchesSolved(int[] levels, int[] targets)
        {
            for (int i = 0; i < levels.Length; i++)
            {
                if (levels[i] != targets[i])
                    return false;
            }
            return true;
        }

        /// <summary>3단계 ② 등불 순서 · 4단계 ② 별빛 등불: 등불 넷 · 순서 길이 3 ~ 4, 같은 등불이 바로 이어 나오지 않음 (드론 순서 기억하기의 작은 판 — <see cref="DroneMemoryGame"/>).</summary>
        public const int Lamps = 4, LampOrderMin = 3, LampOrderMax = 4;

        public static int[] LampOrder(Random rng)
        {
            int n = LampOrderMin + rng.Next(LampOrderMax - LampOrderMin + 1);
            var order = new int[n];
            for (int i = 0; i < n; i++)
            {
                int k;
                do
                    k = rng.Next(Lamps);
                while (i > 0 && k == order[i - 1]);
                order[i] = k;
            }
            return order;
        }

        /// <summary>순서 따라 하기 한 번: 맞으면 다음 (끝까지면 해결), 틀리면 처음부터 (부르는 쪽이 순서를 다시 보여 줌).</summary>
        public static SwitchPress PressLamp(int[] order, ref int entered, int lamp)
        {
            if (entered < 0 || entered >= order.Length || order[entered] != lamp)
            {
                entered = 0;
                return SwitchPress.Wrong;
            }
            entered++;
            return entered >= order.Length ? SwitchPress.AllDone : SwitchPress.Fixed;
        }

        public static string Title(IncidentKind kind)
        {
            switch (kind)
            {
                case IncidentKind.Leak: return "배관 누출";
                case IncidentKind.Breaker: return "차단기 내려감";
                case IncidentKind.Fire: return "작은 화재";
                case IncidentKind.Filter: return "환기 경보";
                // CONNECT A6 단계별 상황
                case IncidentKind.HoloPanel: return "홀로 패널 오류";
                case IncidentKind.CoolantCore: return "냉각 코어 과열";
                case IncidentKind.MaintBot: return "정비 로봇 멈춤";
                case IncidentKind.RideStop: return "놀이기구 멈춤";
                case IncidentKind.DecoLights: return "장식 조명 꺼짐";
                case IncidentKind.ScatteredToys: return "놀이 소품 흩어짐";
                case IncidentKind.WaterChannel: return "물길 막힘";
                case IncidentKind.Lanterns: return "등불 꺼짐";
                case IncidentKind.Vines: return "덩굴 웃자람";
                case IncidentKind.SignalDrift: return "신호 흐트러짐";
                case IncidentKind.StarLanterns: return "별빛 등불 꺼짐";
                default: return "상황";
            }
        }

        public static string Hint(IncidentKind kind)
        {
            switch (kind)
            {
                case IncidentKind.Leak: return "밸브를 잠그세요";
                case IncidentKind.Breaker: return "경고등 켜진 스위치만 올리세요";
                case IncidentKind.Fire: return "벽의 소화기로 끄세요";
                case IncidentKind.Filter: return "필터를 갈아 주세요";
                case IncidentKind.HoloPanel: return "경고 켜진 칸만 눌러 주세요";
                case IncidentKind.CoolantCore: return "냉각 카트리지를 갈아 주세요";
                case IncidentKind.MaintBot: return "로봇을 다시 켜 주세요";
                case IncidentKind.RideStop: return "조작반의 경고등 켜진 레버만 올리세요";
                case IncidentKind.DecoLights: return "조명 분전함을 찾아 고쳐 주세요";
                case IncidentKind.ScatteredToys: return "흩어진 소품을 바구니에 모아 주세요";
                case IncidentKind.WaterChannel: return "물꼬 판 바퀴를 돌려 깜빡이는 눈금에 맞춰 주세요";
                case IncidentKind.Lanterns: return "깜빡인 순서대로 등불을 켜 주세요";
                case IncidentKind.Vines: return "길을 막은 덩굴을 쳐내 주세요";
                case IncidentKind.SignalDrift: return "다이얼을 돌려 깜빡이는 칸에 맞춰 주세요";
                case IncidentKind.StarLanterns: return "깜빡인 순서대로 별빛 등불을 켜 주세요";
                default: return "";
            }
        }
    }
}
