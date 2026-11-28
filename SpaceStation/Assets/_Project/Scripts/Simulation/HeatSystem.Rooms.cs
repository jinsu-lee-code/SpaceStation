using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// CONNECT_1121 3번 ⑤ — 방의 온도 (A10 안쪽 얼굴) · 배전 범위 (A11 과부하, 안쪽만). 사용자 결정 2026-11-28 (추천안).
    /// - 따뜻한 방 = 온기 구간의 따뜻함 모듈 (<see cref="IsWarm"/>): 주민 부탁 가볍게 · 보상 ↑
    /// - 과열 방 = 한도를 넘은 방 (<see cref="IsOverheated"/>): 화재가 잦고 불길 두 곳
    /// - 서늘한 방 = 동작 중인 방열판이 면으로 맞닿은 방 (<see cref="IsCoolRoom"/>): 차단기가 덜 내려감
    /// - 과부하 = 발전 모듈 · 배터리에서 <see cref="HeatPowerConfig.GridRange"/>칸 밖에 있는 대전력 기본 모듈 (<see cref="IsOverloaded"/>, 발열원 · 진화 모듈 제외):
    ///   방 안 차단기가 잦고 내려간 스위치가 하나 더. 바깥 수치 벌칙 없음. 범위 안에 배터리를 두면 풀림. 열 규칙과 상관없이 처음부터.
    /// 설정(<see cref="Comfort"/>)이 없으면 모두 꺼짐 (캠페인 그대로).
    /// </summary>
    public sealed partial class HeatSystem
    {
        private readonly List<ModuleInstance> _roomScratch = new List<ModuleInstance>();
        private HashSet<ModuleData> _evolved;

        /// <summary>서늘한 방: 열 규칙이 열렸고, 동작 중인 방열판이 면으로 맞닿음.</summary>
        public bool IsCoolRoom(ModuleInstance module)
        {
            if (Comfort == null || !Active || module == null)
                return false;
            Sim.Grid.GetNeighborModules(module, _roomScratch);
            foreach (var n in _roomScratch)
            {
                if (n != module && RadiatorWorking(n))
                    return true;
            }
            return false;
        }

        /// <summary>전력 소비 합 (초당).</summary>
        private static float PowerUse(ModuleData d)
        {
            float p = 0f;
            foreach (var c in d.Consumption)
            {
                if (c.Type == ResourceType.Power)
                    p += c.Amount;
            }
            return p;
        }

        /// <summary>대전력 기본 모듈: 전력 소비 ≥ 기준 · 발열원 아님 · 진화(레시피 결과) 아님.</summary>
        public bool IsHeavyBasic(ModuleData data)
        {
            if (Comfort == null || data == null || data.HeatOutput > 0f || PowerUse(data) < Comfort.HeavyPower - 1e-4f)
                return false;
            if (_evolved == null)
            {
                _evolved = new HashSet<ModuleData>();
                var recipes = Sim.Synthesis != null ? Sim.Synthesis.Recipes : null;
                if (recipes != null)
                {
                    foreach (var r in recipes)
                    {
                        if (r != null && r.Result != null)
                            _evolved.Add(r.Result);
                    }
                }
            }
            return !_evolved.Contains(data);
        }

        /// <summary>배전 기준점: 전력을 내거나 배터리 (코어 · 태양광 · 연료전지 · 핵융합로 · 배터리).</summary>
        public static bool IsPowerSource(ModuleData data)
        {
            if (data == null)
                return false;
            if (data.BatteryCapacity > 0f)
                return true;
            foreach (var p in data.Production)
            {
                if (p.Type == ResourceType.Power && p.Amount > 0f)
                    return true;
            }
            return false;
        }

        private static int Distance(IReadOnlyList<Vector3Int> a, IReadOnlyList<Vector3Int> b)
        {
            int best = int.MaxValue;
            foreach (var p in a)
            {
                foreach (var q in b)
                {
                    int d = Math.Max(Math.Abs(p.x - q.x), Math.Max(Math.Abs(p.y - q.y), Math.Abs(p.z - q.z)));
                    if (d < best)
                        best = d;
                }
            }
            return best;
        }

        /// <summary>그 칸들이 가동 중인 발전 · 배터리에서 배전 범위 안인지 (exclude = 옮기는 그 모듈).</summary>
        public bool InReach(IReadOnlyList<Vector3Int> cells, ModuleInstance exclude = null)
        {
            if (Comfort == null || cells == null)
                return true;
            foreach (var m in Sim.Grid.Modules)
            {
                if (m == exclude || !IsPowerSource(m.Data) || !Sim.Connectivity.IsActive(m) || Sim.Damage.IsDamaged(m))
                    continue;
                if (Distance(cells, m.Cells) <= Comfort.GridRange)
                    return true;
            }
            return false;
        }

        /// <summary>과부하: 가동 중인 대전력 기본 모듈이 배전 범위 밖.</summary>
        public bool IsOverloaded(ModuleInstance module)
            => Comfort != null && module != null && IsHeavyBasic(module.Data) && Sim.Connectivity.IsActive(module) && !InReach(module.Cells, module);

        // ---------------- 글 ----------------

        /// <summary>모듈 정보 창 한 줄 (과부하일 때만).</summary>
        public string OverloadText(ModuleInstance module)
            => IsOverloaded(module) ? $"배전 범위 밖 (발전 · 배터리에서 {Comfort.GridRange}칸 넘음) · 방 안 차단기가 잦고 스위치 하나 더 · 가까이 배터리를 두면 풀려요" : null;

        /// <summary>배치 · 옮기기 안내 한 줄: 대전력 모듈은 그 자리가 배전 범위 밖인지, 발전 · 배터리는 범위에 새로 넣는 곳 수.</summary>
        public string OverloadPreviewText(ModuleData data, IReadOnlyList<Vector3Int> cells, ModuleInstance moving)
        {
            if (Comfort == null || data == null || cells == null)
                return null;
            if (IsHeavyBasic(data))
                return InReach(cells, moving) ? null : $"배전 범위 밖 · {Comfort.GridRange}칸 안에 발전 · 배터리가 없어 방 안 차단기가 잦아요";
            if (!IsPowerSource(data))
                return null;
            int fixedCount = 0;
            foreach (var m in Sim.Grid.Modules)
            {
                if (m == moving || !IsOverloaded(m))
                    continue;
                if (Distance(cells, m.Cells) <= Comfort.GridRange)
                    fixedCount++;
            }
            return fixedCount > 0 ? $"배전 범위에 넣음 {fixedCount}곳 (방 안 차단기 보통으로)" : null;
        }

        /// <summary>방 안 상태 화면 줄 (CONNECT ⑤ — "온기 · …" · "위험 · …" · "도움 · …", 색이 아니라 글로).</summary>
        public void AppendRoomLines(List<string> lines, ModuleInstance module)
        {
            if (Comfort == null || module == null)
                return;
            if (IsWarm(module))
                lines.Add("온기 · ♨ 따뜻한 방: 부탁 가볍게 · 보상 ↑");
            if (Active && IsOverheated(module))
                lines.Add($"위험 · 과열 (열 {HeatOf(module):0.0} / 한도 {LimitOf(module):0.#}): 화재 잦음 · 불길 두 곳");
            if (IsCoolRoom(module))
                lines.Add("도움 · 서늘한 방 (옆 방열판): 차단기 덜 내려감");
            if (IsOverloaded(module))
                lines.Add($"위험 · 배전 범위 밖: 차단기 잦음 · 스위치 하나 더 ({Comfort.GridRange}칸 안에 배터리)");
        }
    }
}
