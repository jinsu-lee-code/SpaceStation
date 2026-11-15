using System.Collections.Generic;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 수리 사령부 체험 ② "경종 → 반딧불 드론 등불 쇼" 순서 (TASKS 2026-11-13 "금빛 경종을 울리면 반딧불 드론 떼가 고목을 한 바퀴 돌며 가지마다 등불을 켬"). Unity 없이 시험하는 순수 규칙.
    /// 쉼 → 종 (흔들리며 켜져 있던 등불이 스러짐 · 공방 안 드론이 깨어남) → 떠오름 (공방 둘에서 나와 고목 둘레로) →
    /// 돌기 (떼가 고목을 한 바퀴 돌며, 떼가 지나는 등불마다 드론 하나가 날아가 켜고 돌아옴) → 반짝임 (다 켠 자리에서 떼가 환히 빛나며 살짝 퍼짐) → 돌아감 (공방으로) → 쉼.
    /// (관측대 위로 올라 흩어지던 끝 장면은 빈터 · 나무집 마루에서 관측대 바닥 · 잎 구름에 가려 안 보여 뺌 — 2026-11-15 플레이)
    /// 등불은 쇼가 끝나도 켜진 채 (다시 울리면 스러졌다가 다시 켜짐). 각 = 고목 축 둘레 x-z 수학 각 (0° = +x, 90° = +z), 떼는 각이 커지는 쪽으로 돎.
    /// </summary>
    public sealed class FireflyLanternShow
    {
        public enum Phase { Idle, Ring, Rise, Circle, Finale, Return }

        public const float RingTime = 2.2f, RiseTime = 4.5f, FinaleTime = 2.6f, ReturnTime = 6f;
        /// <summary>떼가 고목을 도는 빠르기 (도/초).</summary>
        public const float OrbitSpeed = 34f;
        /// <summary>떼 → 등불로 날아감 (끝 = 등불이 켜지는 순간) · 등불 옆에 머묾 (그 뒤 같은 시간에 떼로 돌아감).</summary>
        public const float VisitFly = 1.2f, VisitHover = 0.8f;
        /// <summary>떼가 돌기 시작하는 각 (공방 둘 205° · 300° 사이 — 나무집 쪽).</summary>
        public const float StartAngle = 252f;

        private readonly float[] _depart;   // 돌기 시작부터 드론 k가 떼를 떠나는 시각 (초)
        private readonly bool[] _lit;
        private readonly float[] _litFor;   // 켜진 뒤 지난 시간 (번쩍임)
        private readonly bool[] _fading;    // 종이 울릴 때 켜져 있던 등불 (종 동안 스러짐)

        public FireflyLanternShow(IReadOnlyList<float> lanternAngles)
        {
            int n = lanternAngles.Count;
            _depart = new float[n];
            _lit = new bool[n];
            _litFor = new float[n];
            _fading = new bool[n];
            float last = 0f;
            for (int k = 0; k < n; k++)
            {
                float d = (lanternAngles[k] - StartAngle) % 360f;
                if (d < 0f)
                    d += 360f;
                _depart[k] = d / OrbitSpeed;
                if (_depart[k] > last)
                    last = _depart[k];
                _litFor[k] = 99f;
            }
            CircleTime = last + 2f * VisitFly + VisitHover + 0.4f;
        }

        public int LanternCount => _depart.Length;
        public Phase Current { get; private set; }
        /// <summary>지금 단계에 든 뒤 지난 시간 (초).</summary>
        public float PhaseTime { get; private set; }
        public bool Running => Current != Phase.Idle;
        /// <summary>떼가 고목을 도는 단계 길이 (마지막 등불을 켠 드론이 떼로 돌아올 때까지).</summary>
        public float CircleTime { get; }
        public float TotalTime => RingTime + RiseTime + CircleTime + FinaleTime + ReturnTime;
        /// <summary>이번 쇼에서 켠 등불 수.</summary>
        public int LitCount { get; private set; }

        /// <summary>종 줄을 당김 (쉬는 중에만). 시작했으면 true.</summary>
        public bool Start()
        {
            if (Running)
                return false;
            for (int k = 0; k < _lit.Length; k++)
                _fading[k] = _lit[k];
            LitCount = 0;
            Enter(Phase.Ring);
            return true;
        }

        /// <summary>방을 나가면: 쉼 · 등불 모두 꺼짐 (희미한 불씨).</summary>
        public void Reset()
        {
            for (int k = 0; k < _lit.Length; k++)
            {
                _lit[k] = _fading[k] = false;
                _litFor[k] = 99f;
            }
            LitCount = 0;
            Enter(Phase.Idle);
        }

        /// <summary>떼 머리의 각 (도, 360을 넘어 계속 커짐) — 떠오름 = 시작 각, 돌기 = 한 바퀴 남짓 (반짝임 · 돌아감은 돌기를 마친 각에서).</summary>
        public float SwarmAngle => Current switch
        {
            Phase.Circle => StartAngle + OrbitSpeed * PhaseTime,
            Phase.Finale or Phase.Return => CircleEndAngle,
            _ => StartAngle,
        };

        /// <summary>돌기를 마칠 때 떼 머리 각.</summary>
        public float CircleEndAngle => StartAngle + OrbitSpeed * CircleTime;

        /// <summary>드론 k (등불 k를 켬)이 떼에서 등불 쪽으로 간 정도 0 (떼 안) ~ 1 (등불 옆) — 돌기 단계에서만.</summary>
        public float Visit(int k)
        {
            if (Current != Phase.Circle || k < 0 || k >= _depart.Length)
                return 0f;
            float t = PhaseTime - _depart[k];
            if (t <= 0f)
                return 0f;
            if (t < VisitFly)
                return Smooth(t / VisitFly);
            t -= VisitFly;
            if (t < VisitHover)
                return 1f;
            t -= VisitHover;
            return t < VisitFly ? 1f - Smooth(t / VisitFly) : 0f;
        }

        public bool IsLit(int k) => _lit[k];

        /// <summary>등불 밝기 0 (희미한 불씨) ~ 1 (환히 켜짐): 종이 울리면 켜져 있던 등불이 스러지고, 드론이 닿는 순간 켜짐.</summary>
        public float Glow(int k)
        {
            if (Current == Phase.Ring && _fading[k])
                return 1f - Smooth(PhaseTime / RingTime);
            return _lit[k] ? 1f : 0f;
        }

        /// <summary>켜지는 순간의 번쩍임 0 ~ 1 (0.6초 안에 잦아듦).</summary>
        public float Flash(int k)
        {
            float t = _litFor[k];
            return t < 0.6f ? 1f - t / 0.6f : 0f;
        }

        /// <summary>한 프레임 — 이번에 켜진 등불 번호를 lit에 더함.</summary>
        public void Step(float dt, List<int> lit)
        {
            for (int k = 0; k < _litFor.Length; k++)
                _litFor[k] += dt;
            if (!Running)
                return;
            float before = PhaseTime;
            PhaseTime += dt;
            if (Current == Phase.Circle)
            {
                for (int k = 0; k < _depart.Length; k++)
                {
                    float at = _depart[k] + VisitFly;
                    if (!_lit[k] && before < at && PhaseTime >= at)
                    {
                        _lit[k] = true;
                        _litFor[k] = PhaseTime - at;
                        LitCount++;
                        lit?.Add(k);
                    }
                }
            }
            float length = Current switch
            {
                Phase.Ring => RingTime,
                Phase.Rise => RiseTime,
                Phase.Circle => CircleTime,
                Phase.Finale => FinaleTime,
                _ => ReturnTime,
            };
            if (PhaseTime < length)
                return;
            if (Current == Phase.Ring)
            {
                for (int k = 0; k < _lit.Length; k++)
                    _lit[k] = _fading[k] = false;
            }
            Enter(Current == Phase.Return ? Phase.Idle : Current + 1);
        }

        private void Enter(Phase phase)
        {
            Current = phase;
            PhaseTime = 0f;
        }

        private static float Smooth(float x)
        {
            x = x < 0f ? 0f : x > 1f ? 1f : x;
            return x * x * (3f - 2f * x);
        }
    }
}
