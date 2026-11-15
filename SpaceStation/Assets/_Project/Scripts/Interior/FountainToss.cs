using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 궤도 항만 미니게임 "인어 분수 진주 던지기" 판 (사용자 2026-11-16 "미니게임 진행해줘"). Unity 없이 시험하는 순수 규칙 (좌표 = 템플릿 x-z 평면).
    /// 진주 일곱 개: 분수 물 위를 도는 빛 고리 셋 (큰 고리 1점 · 중간 고리 2점 · 작은 고리 3점 — 작을수록 빨리 돎)을 보고 F = 보는 자리로 진주가 날아감 (<see cref="FlightTime"/>초 —
    /// 고리가 도는 만큼 앞을 보고 던져야 함). 떨어지는 때 고리 안이면 그 점수 (겹치면 높은 쪽), 물이면 0, 분수 밖이면 빗나감. 일곱 개가 다 떨어지면 끝.
    /// </summary>
    public sealed class FountainToss
    {
        public enum Phase { Idle, Play, Done }

        public const int PearlCount = 7;
        public const float FlightTime = 0.8f, Cooldown = 0.45f;
        /// <summary>진주가 날아가는 가장 먼 수평 거리 (m).</summary>
        public const float MaxThrow = 6f;
        /// <summary>떨어진 자리: 분수 밖 (빗나감) · 물 (0점).</summary>
        public const int Outside = -2, Water = -1;

        public readonly struct Ring
        {
            public readonly float Orbit, Speed, Start, Radius;
            public readonly int Points;

            public Ring(float orbit, float speed, float start, float radius, int points)
            {
                Orbit = orbit;
                Speed = speed;
                Start = start;
                Radius = radius;
                Points = points;
            }
        }

        /// <summary>
        /// 고리 셋 (꼬리 기둥 둘레를 돎 — 가운데에서 거리 · 빠르기 rad/초 (− = 반대로) · 처음 각 · 반지름 · 점수). 거리는 ± <see cref="Wobble"/>만큼 출렁임:
        /// 고리 둘레가 꼬리 기둥 밖 (꼬리가 수면 위 0.5 m쯤에서 가운데에서 0.5 m 기울어 고리 밑으로 나옴 — 큰 고리 0.42 m · 거리 1.25 m는 그 밑을 지났음) · 분수 테 (물 반지름 1.86) 안:
        /// 꼬리 공식 (op3_builder fountain)으로 2분을 재면 꼬리와 0.12 m · 테와 0.1 m — OrbitalPortTests.FountainToss_Wired가 광선으로 잼.
        /// </summary>
        public static readonly Ring[] Rings =
        {
            new Ring(1.36f, 0.32f, 0f, 0.32f, 1),
            new Ring(1.3f, -0.5f, 2.1f, 0.26f, 2),
            new Ring(1.36f, 0.75f, 4.2f, 0.18f, 3),
        };
        public const float Wobble = 0.04f;

        public struct Landing
        {
            public Vector2 At;
            /// <summary>점수 (0 = 물 · 빗나감) · 맞은 고리 (<see cref="Water"/> · <see cref="Outside"/>).</summary>
            public int Points, Ring;
        }

        private readonly Vector2 _center;
        private readonly float _radius;
        private readonly List<(Vector2 To, float At)> _flying = new List<(Vector2, float)>();
        private readonly Queue<Landing> _landed = new Queue<Landing>();
        private float _cool;

        public Phase Current { get; private set; }
        /// <summary>판이 시작된 뒤 지난 시간 (초 — 고리 자리의 시계).</summary>
        public float Time { get; private set; }
        public int Score { get; private set; }
        public int PearlsLeft { get; private set; }
        public int Hits { get; private set; }
        public int Flying => _flying.Count;

        public bool Playing => Current == Phase.Play;
        public bool CanThrow => Playing && PearlsLeft > 0 && _cool <= 0f;

        /// <param name="center">분수 가운데 (x, z)</param>
        /// <param name="radius">물 반지름 (테 안쪽)</param>
        public FountainToss(Vector2 center, float radius)
        {
            _center = center;
            _radius = radius;
            PearlsLeft = PearlCount;
        }

        public void Start()
        {
            Current = Phase.Play;
            Time = 0f;
            Score = Hits = 0;
            PearlsLeft = PearlCount;
            _cool = 0f;
            _flying.Clear();
            _landed.Clear();
        }

        public void Stop()
        {
            Current = Phase.Idle;
            _flying.Clear();
            _landed.Clear();
        }

        /// <summary>판을 일찍 끝냄 (분수를 떠남) — 날아가던 진주는 버리고 지금 점수로 끝.</summary>
        public void Finish()
        {
            if (Current != Phase.Play)
                return;
            _flying.Clear();
            Current = Phase.Done;
        }

        /// <summary>고리 i의 가운데 (판 시계 t초).</summary>
        public Vector2 RingCenter(int i, float t)
        {
            var r = Rings[i];
            float a = r.Start + r.Speed * t;
            float orbit = r.Orbit + Wobble * Mathf.Sin(t * 0.9f + i * 2.1f);
            return _center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * orbit;
        }

        /// <summary>t초에 at에 떨어진 진주 (점수 · 맞은 고리).</summary>
        public Landing Judge(Vector2 at, float t)
        {
            var l = new Landing { At = at, Points = 0, Ring = Water };
            if ((at - _center).magnitude > _radius)
            {
                l.Ring = Outside;
                return l;
            }
            for (int i = 0; i < Rings.Length; i++)
            {
                if ((at - RingCenter(i, t)).magnitude <= Rings[i].Radius && Rings[i].Points > l.Points)
                {
                    l.Points = Rings[i].Points;
                    l.Ring = i;
                }
            }
            return l;
        }

        /// <summary>진주를 던짐 (떨어질 자리 = 보는 자리): 남은 진주 · 쉬는 틈이 있어야 함.</summary>
        public bool Throw(Vector2 to)
        {
            if (!CanThrow)
                return false;
            PearlsLeft--;
            _cool = Cooldown;
            _flying.Add((to, Time + FlightTime));
            return true;
        }

        public void Tick(float dt)
        {
            if (Current != Phase.Play)
                return;
            Time += dt;
            _cool -= dt;
            for (int i = _flying.Count - 1; i >= 0; i--)
            {
                if (_flying[i].At > Time)
                    continue;
                var l = Judge(_flying[i].To, _flying[i].At);
                Score += l.Points;
                if (l.Points > 0)
                    Hits++;
                _landed.Enqueue(l);
                _flying.RemoveAt(i);
            }
            if (PearlsLeft == 0 && _flying.Count == 0)
                Current = Phase.Done;
        }

        /// <summary>떨어진 진주 하나를 꺼냄 (물보라 · 반짝임을 그리는 쪽이 씀).</summary>
        public bool PopLanding(out Landing landing)
        {
            if (_landed.Count > 0)
            {
                landing = _landed.Dequeue();
                return true;
            }
            landing = default;
            return false;
        }
    }
}
