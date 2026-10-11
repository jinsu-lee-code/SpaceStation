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

        /// <summary>종류 고르기: 할 수 있는 것 중 직전과 다른 것 (하나뿐이면 그것).</summary>
        /// <param name="r">0~1 난수</param>
        public static IncidentKind Pick(bool breakerOk, IncidentKind? last, double r)
        {
            var pool = new List<IncidentKind>(4);
            foreach (IncidentKind k in Enum.GetValues(typeof(IncidentKind)))
            {
                if (k == IncidentKind.Breaker && !breakerOk)
                    continue;
                if (last.HasValue && k == last.Value)
                    continue;
                pool.Add(k);
            }
            if (pool.Count == 0)
                return IncidentKind.Leak;
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
        public static bool[] TripPattern(Random rng)
        {
            var lit = new bool[Switches];
            int count = 2 + rng.Next(2);
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

        public static string Title(IncidentKind kind)
        {
            switch (kind)
            {
                case IncidentKind.Leak: return "배관 누출";
                case IncidentKind.Breaker: return "차단기 내려감";
                case IncidentKind.Fire: return "작은 화재";
                default: return "환기 경보";
            }
        }

        public static string Hint(IncidentKind kind)
        {
            switch (kind)
            {
                case IncidentKind.Leak: return "밸브를 잠그세요";
                case IncidentKind.Breaker: return "경고등 켜진 스위치만 올리세요";
                case IncidentKind.Fire: return "벽의 소화기로 끄세요";
                default: return "필터를 갈아 주세요";
            }
        }
    }
}
