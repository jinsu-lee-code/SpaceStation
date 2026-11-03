using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Building;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>치안 본부 순찰차 출동 길: 떠오른 자리에서 출발해 가속하며 멀어지고, 같은 길로 돌아온다.</summary>
    public class PatrolCarFxTests
    {
        [Test]
        public void FlightOffset_StartsAtCruiseAndAcceleratesAway()
        {
            var heading = new Vector3(-0.6f, 0f, -0.8f);
            const float cruise = 0.35f, climb = 0.7f, distance = 4f;
            Assert.That(Vector3.Distance(PatrolCarFx.FlightOffset(0f, heading, cruise, climb, distance), Vector3.up * cruise), Is.LessThan(1e-5f), "떠오른 자리에서 출발");
            var end = PatrolCarFx.FlightOffset(1f, heading, cruise, climb, distance);
            Assert.That(Vector3.Distance(end, Vector3.up * (cruise + climb) + heading * distance), Is.LessThan(1e-5f), "멀리 더 높이");
            float prevStep = 0f;
            var prev = PatrolCarFx.FlightOffset(0f, heading, cruise, climb, distance);
            for (int i = 1; i <= 10; i++)
            {
                var p = PatrolCarFx.FlightOffset(i / 10f, heading, cruise, climb, distance);
                float step = Vector3.Dot(p - prev, heading);
                Assert.Greater(step, prevStep - 1e-5f, "점점 빨라짐");
                prevStep = step;
                prev = p;
            }
        }

        /// <summary>경광등: 끄면 어둡고, 켜면 파랑 · 빨강이 번갈아 켜지며 둘이 함께 켜지는 때가 없다 (사용자 2026-11-03 둥근 빛 · 비슷한 두 색 → 렌즈 모양 파랑 · 빨강).</summary>
        [Test]
        public void Siren_AlternatesBlueAndRedOnly()
        {
            var car = new GameObject("Car");
            var material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            var siren = new PatrolSiren(car.transform, material, Vector3.left, Vector3.right, Vector3.one, 2f);
            Assert.AreEqual(0, siren.Lit, "시작은 꺼짐");
            siren.Tick(0.1f);
            Assert.AreEqual(0, siren.Lit, "켜기 전엔 번쩍이지 않음");
            siren.Start();
            var order = new List<int>();
            for (int i = 0; i < 200; i++)
            {
                siren.Tick(0.01f);
                int lit = siren.Lit;
                if (lit != 0 && (order.Count == 0 || order[order.Count - 1] != lit))
                    order.Add(lit);
            }
            Assert.GreaterOrEqual(order.Count, 4, "2초 동안 여러 번 바뀜");
            for (int i = 1; i < order.Count; i++)
                Assert.AreNotEqual(order[i - 1], order[i], "번갈아");
            Assert.Contains(1, order);
            Assert.Contains(2, order);
            siren.Stop();
            Assert.AreEqual(0, siren.Lit, "끄면 어두움");
            Object.DestroyImmediate(car);
            Object.DestroyImmediate(material);
        }
    }
}
