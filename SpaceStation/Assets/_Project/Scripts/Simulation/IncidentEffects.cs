using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;

namespace SpaceStation.Simulation
{
    /// <summary>11-17 ④ 내부 사고 종류 (사용자 결정 2026-10-11).</summary>
    public enum IncidentKind
    {
        /// <summary>배관 누출 — 밸브 잠그기 (놓치면 물 · 산소가 샘).</summary>
        Leak,
        /// <summary>차단기 내려감 — 경고등 켜진 스위치 올리기 (놓치면 그 방 생산이 잠시 줄어듦).</summary>
        Breaker,
        /// <summary>작은 화재 — 소화기로 끄기 (놓치면 그 방 내구도 · 만족도 조금).</summary>
        Fire,
        /// <summary>환기 경보 — 필터 교체 (놓치면 산소가 조금 샘 · 만족도 조금).</summary>
        Filter,

        // CONNECT_1121 A6 단계별 상황 (사용자 결정 2026-11-27) — 끝에 더함 (번호 유지). 단계 · 놓침 손실은 IncidentCatalog
        /// <summary>1단계 홀로 패널 오류 — 경고 켜진 칸만 누르기.</summary>
        HoloPanel,
        /// <summary>1단계 냉각 코어 과열 — 냉각 카트리지 뽑아 끼우기.</summary>
        CoolantCore,
        /// <summary>1단계 정비 로봇 멈춤 — F 길게 재가동.</summary>
        MaintBot,
        /// <summary>2단계 놀이기구 · 쇼 멈춤 — 조작반의 경고등 켜진 레버만 올리기.</summary>
        RideStop,
        /// <summary>2단계 장식 조명 꺼짐 — 꺼진 장식을 찾아 F 길게.</summary>
        DecoLights,
        /// <summary>2단계 놀이 소품 흩어짐 — 바구니로 옮기기.</summary>
        ScatteredToys,
        /// <summary>3단계 물길 막힘 — 물꼬 맞추기.</summary>
        WaterChannel,
        /// <summary>3단계 등불 · 반딧불 꺼짐 — 깜빡인 순서대로 켜기.</summary>
        Lanterns,
        /// <summary>3단계 덩굴 웃자람 — F 길게로 쳐냄.</summary>
        Vines,
        /// <summary>4단계 신호 흐트러짐 — 신호 맞추기.</summary>
        SignalDrift,
        /// <summary>4단계 별빛 등불 순서 — 깜빡인 순서대로 켜기.</summary>
        StarLanterns,
    }

    /// <summary>놓친 상황의 손실 종류 (BALANCE 31번 값 그대로 — 새 상황은 지금 넷 중 하나 또는 만족도만).</summary>
    public enum IncidentLoss
    {
        /// <summary>자원이 샘 (누출 — 물 · 산소).</summary>
        Drain,
        /// <summary>그 방 생산 배율 (차단기). 만들거나 전력을 쓰는 방이 아니면 만족도만.</summary>
        Production,
        /// <summary>그 방 내구도 + 만족도 (화재).</summary>
        Damage,
        /// <summary>산소 + 만족도 (환기).</summary>
        Air,
        /// <summary>만족도만 (장식 · 소품 · 등불 — 분위기 문제).</summary>
        Mood,
    }

    /// <summary>
    /// CONNECT_1121 A6 단계별 상황 표 (사용자 결정 2026-11-27): 진화 단계마다 그 테마의 상황 (기본 = 지금 넷).
    /// 0 = 기본 · 1 = 미래 기술 · 2 = 테마파크 · 3 = 동화 · 정원 · 4 = 성운. 손실은 지금 사고 값을 다시 씀.
    /// </summary>
    public static class IncidentCatalog
    {
        private static readonly IncidentKind[] Basic = { IncidentKind.Leak, IncidentKind.Breaker, IncidentKind.Fire, IncidentKind.Filter };
        private static readonly IncidentKind[] Stage1 = { IncidentKind.HoloPanel, IncidentKind.CoolantCore, IncidentKind.MaintBot };
        private static readonly IncidentKind[] Stage2 = { IncidentKind.RideStop, IncidentKind.DecoLights, IncidentKind.ScatteredToys };
        private static readonly IncidentKind[] Stage3 = { IncidentKind.WaterChannel, IncidentKind.Lanterns, IncidentKind.Vines };
        private static readonly IncidentKind[] Stage4 = { IncidentKind.SignalDrift, IncidentKind.StarLanterns };

        /// <summary>그 단계 방의 상황 종류 (4단계 위는 4단계 것).</summary>
        public static IReadOnlyList<IncidentKind> KindsForStage(int stage)
        {
            switch (stage)
            {
                case 0: return Basic;
                case 1: return Stage1;
                case 2: return Stage2;
                case 3: return Stage3;
                default: return stage < 0 ? Basic : Stage4;
            }
        }

        /// <summary>상황이 속한 단계.</summary>
        public static int StageOf(IncidentKind kind)
        {
            for (int s = 0; s <= 4; s++)
            {
                foreach (var k in KindsForStage(s))
                {
                    if (k == kind)
                        return s;
                }
            }
            return 0;
        }

        /// <summary>놓쳤을 때 손실 종류.</summary>
        public static IncidentLoss LossOf(IncidentKind kind)
        {
            switch (kind)
            {
                case IncidentKind.Leak:
                case IncidentKind.WaterChannel:
                    return IncidentLoss.Drain;
                case IncidentKind.Breaker:
                case IncidentKind.HoloPanel:
                case IncidentKind.MaintBot:
                case IncidentKind.RideStop:
                case IncidentKind.SignalDrift:
                    return IncidentLoss.Production;
                case IncidentKind.Fire:
                case IncidentKind.CoolantCore:
                case IncidentKind.Vines:
                    return IncidentLoss.Damage;
                case IncidentKind.Filter:
                    return IncidentLoss.Air;
                default:
                    return IncidentLoss.Mood;
            }
        }
    }

    /// <summary>
    /// 11-17 ④ 놓친 사고의 손실 (BALANCE 31번, 순수 C#): 상한이 있는 짧은 손실만 — 정해진 시간(게임 초)이 지나면 정비 인력이 처리한 것으로 끝남.
    /// 자원 누출 = 시간 동안 나눠서 빠짐, 차단기 = 그 방 생산 배율. 저장하지 않음 (2분짜리 짧은 효과).
    /// </summary>
    public sealed class IncidentEffects
    {
        private sealed class Drain
        {
            public ResourceType Type;
            public float PerSecond;
            public float Left;
        }

        private readonly List<Drain> _drains = new List<Drain>();
        private readonly Dictionary<ModuleInstance, (float Multiplier, float Left)> _trips = new Dictionary<ModuleInstance, (float, float)>();
        private readonly List<ModuleInstance> _ended = new List<ModuleInstance>();

        /// <summary>
        /// 13-6 고리: 놓친 사고의 손실 배율 (1 = BALANCE 31번 그대로, 0 = 손실 없이 정비 인력이 처리). 타이쿤판이 정한다.
        /// </summary>
        public float MissScale { get; set; } = 1f;

        /// <summary>차단기가 내려간 방의 생산 배율 (없으면 1).</summary>
        public float ProductionMultiplier(ModuleInstance module) =>
            module != null && _trips.TryGetValue(module, out var t) ? t.Multiplier : 1f;

        public bool IsTripped(ModuleInstance module) => module != null && _trips.ContainsKey(module);

        /// <summary>지금 이어지는 손실 수 (누출 + 차단기).</summary>
        public int ActiveCount => _drains.Count + _trips.Count;

        internal void AddDrain(ResourceType type, float total, float seconds)
        {
            if (total <= 0f || seconds <= 0f)
                return;
            _drains.Add(new Drain { Type = type, PerSecond = total / seconds, Left = seconds });
        }

        internal void Trip(ModuleInstance module, float multiplier, float seconds)
        {
            if (module == null || seconds <= 0f)
                return;
            _trips[module] = (multiplier, seconds);
        }

        internal void Tick(float dt, ResourceSimulation resources)
        {
            if (dt <= 0f)
                return;
            for (int i = _drains.Count - 1; i >= 0; i--)
            {
                var d = _drains[i];
                float step = Math.Min(dt, d.Left);
                resources.RemoveStock(d.Type, d.PerSecond * step);
                d.Left -= step;
                if (d.Left <= 1e-4f)
                    _drains.RemoveAt(i);
            }
            _ended.Clear();
            var keys = new List<ModuleInstance>(_trips.Keys);
            foreach (var m in keys)
            {
                var t = _trips[m];
                t.Left -= dt;
                if (t.Left <= 0f)
                    _ended.Add(m);
                else
                    _trips[m] = t;
            }
            foreach (var m in _ended)
                _trips.Remove(m);
        }
    }
}
