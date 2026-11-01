using NUnit.Framework;
using SpaceStation.Building;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>드론 정비창 라이트 쇼: 대형마다 드론이 옥상 위 원 안에 겹치지 않게 놓이고, 대형을 옮겨 갈 때 튀지 않는다.</summary>
    public class DroneShowFxTests
    {
        private const int Count = 30;

        [Test]
        public void Formations_StayOverRoofWithoutOverlap()
        {
            var p = new Vector3[Count];
            for (int f = 0; f < DroneShowFx.FormationCount; f++)
            {
                foreach (float time in new[] { 0f, 3.7f, 41f })
                {
                    DroneShowFx.Formation(f, Count, time, p);
                    float closest = float.MaxValue;
                    for (int i = 0; i < Count; i++)
                    {
                        Assert.LessOrEqual(new Vector2(p[i].x, p[i].z).magnitude, 1.1f, $"대형 {f} 드론 {i}: 옥상 위 원 안");
                        Assert.LessOrEqual(Mathf.Abs(p[i].y), 0.12f, $"대형 {f} 드론 {i}: 높이는 낮게");
                        for (int j = i + 1; j < Count; j++)
                            closest = Mathf.Min(closest, Vector3.Distance(p[i], p[j]));
                    }
                    Assert.Greater(closest, 0.03f, $"대형 {f}: 드론끼리 겹치지 않음");
                }
            }
        }

        [Test]
        public void Pose_MovesSmoothlyThroughWholeShow()
        {
            const float hold = 5f, move = 2.4f;
            var prev = new Vector3[Count];
            var now = new Vector3[Count];
            var a = new Vector3[Count];
            var b = new Vector3[Count];
            DroneShowFx.Pose(0f, Count, hold, move, prev, a, b);
            const float dt = 1f / 60f;
            float fastest = 0f;
            for (float t = dt; t < (hold + move) * DroneShowFx.FormationCount + 1f; t += dt)
            {
                DroneShowFx.Pose(t, Count, hold, move, now, a, b);
                for (int i = 0; i < Count; i++)
                    fastest = Mathf.Max(fastest, Vector3.Distance(prev[i], now[i]) / dt);
                System.Array.Copy(now, prev, Count);
            }
            Assert.Less(fastest, 3f, "대형이 바뀌는 순간에도 드론이 순간이동하지 않음 (반지름 1 기준 초당 3 미만)");
        }
    }
}
