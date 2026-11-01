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
    }
}
