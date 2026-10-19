using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Core;
using SpaceStation.Simulation;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>
    /// 13-6b 이민선 길 · 장면 카메라 자리 (사용자 요청 2026-10-19: 카메라 자리에 모듈이 있으면 다른 각도로):
    /// 길이 모듈을 지나면 반대쪽으로 휨 · 따라가는 카메라가 막히면 다른 후보 · 고정 시점은 자리 · 시야가 막히지 않는 곳, 모두 막히면 위에서 내려다봄 · 독백 때 덜 다가감.
    /// </summary>
    public class ArkShotTests
    {
        private static readonly Vector3 Out = Vector3.forward;
        private static readonly Vector3 Nose = new Vector3(0f, 0f, 0.5f + ArkDocking.Gap);
        private static Vector3 Side => Vector3.Cross(Vector3.up, Out).normalized; // +X
        private const float ShipLength = 2.2f;
        private const float CutT = 0.72f;

        private static Vector3 Path(Vector3 side, float t) => ArkShot.Approach(Nose, Out, side, t);

        [Test]
        public void ApproachSide_FlipsWhenModulesBlockTheDefaultSide()
        {
            var cells = new HashSet<Vector3Int>();
            System.Func<Vector3Int, bool> occupied = cells.Contains;
            Assert.AreEqual(1f, ArkShot.PickSide(occupied, s => t => Path(s, t), Side), "비어 있으면 기본 쪽");

            // 기본 쪽(+X) 접근로 자리에 모듈 벽
            for (int x = 1; x <= 8; x++)
                for (int y = 0; y <= 5; y++)
                    for (int z = 5; z <= 25; z++)
                        cells.Add(new Vector3Int(x, y, z));
            Assert.Greater(ArkShot.PathHits(occupied, t => Path(Side, t)), 0);
            Assert.AreEqual(0, ArkShot.PathHits(occupied, t => Path(-Side, t)));
            Assert.AreEqual(-1f, ArkShot.PickSide(occupied, s => t => Path(s, t), Side), "막히면 반대쪽으로 휨");
        }

        [Test]
        public void Follow_PicksAnotherAngleWhenTheDefaultSpotIsBlocked()
        {
            var cells = new HashSet<Vector3Int>();
            System.Func<Vector3Int, bool> occupied = cells.Contains;
            System.Func<float, Vector3> path = t => Path(Side, t);
            Assert.AreEqual(0, ArkShot.PickFollow(occupied, path, CutT, ShipLength), "비어 있으면 기본 (왼쪽 위)");

            // 기본 자리(배 왼쪽 위)마다 모듈
            var (side, lift) = ArkShot.FollowOptions[0];
            for (int i = 0; i <= 40; i++)
            {
                float t = CutT * i / 40f;
                var p = path(t);
                var cam = ArkShot.FollowCamera(p, (path(t + 0.01f) - p).normalized, ShipLength, side, lift);
                cells.Add(GridConfig.WorldToCell(cam));
            }
            Assert.AreEqual(1, ArkShot.PickFollow(occupied, path, CutT, ShipLength), "오른쪽 위로");
        }

        [Test]
        public void Fixed_AvoidsBlockedSpotOrSight_ElseLooksDownFromAbove()
        {
            var cells = new HashSet<Vector3Int>();
            System.Func<Vector3Int, bool> occupied = cells.Contains;
            var p = Path(Side, CutT);
            var cutShip = p - (Path(Side, CutT + 0.01f) - p).normalized * (ShipLength * 0.5f);
            var docked = Nose + Out * (ShipLength * 0.5f);
            var first = ArkShot.PickFixed(occupied, Nose, cutShip, docked, 0.45f, 1f, 0f, out bool overhead);
            Assert.IsFalse(overhead);

            // 카메라 자리는 비었지만 배와의 사이를 모듈이 가림 → 다른 자리
            cells.Add(GridConfig.WorldToCell(Vector3.Lerp(first, cutShip, 0.5f)));
            var second = ArkShot.PickFixed(occupied, Nose, cutShip, docked, 0.45f, 1f, 0f, out overhead);
            Assert.IsFalse(overhead);
            Assert.AreNotEqual(first, second);
            Assert.IsTrue(ArkShot.Free(occupied, second));
            Assert.IsTrue(ArkShot.Clear(occupied, second, cutShip, 0f));
            Assert.IsTrue(ArkShot.Clear(occupied, second, docked, 0f));

            // 후보 높이를 모두 덮는 두꺼운 층 → 정거장 꼭대기보다 위에서 내려다봄
            System.Func<Vector3Int, bool> slab = c => c.y >= 1 && c.y <= 12;
            var above = ArkShot.PickFixed(slab, Nose, cutShip, docked, 0.45f, 1f, 12f, out overhead);
            Assert.IsTrue(overhead);
            Assert.GreaterOrEqual(above.y, 15f - 1e-3f);
        }

        [Test]
        public void PushIn_StopsShortOfModules()
        {
            var cells = new HashSet<Vector3Int>();
            System.Func<Vector3Int, bool> occupied = cells.Contains;
            var from = new Vector3(10f, 4f, 0f);
            var look = Vector3.zero;
            Assert.AreEqual(Vector3.Lerp(from, look, 0.4f), ArkShot.PushIn(occupied, from, look, 0.4f), "비어 있으면 다 다가감");
            cells.Add(GridConfig.WorldToCell(Vector3.Lerp(from, look, 0.4f)));
            var p = ArkShot.PushIn(occupied, from, look, 0.4f);
            Assert.Less(Vector3.Distance(from, p), Vector3.Distance(from, Vector3.Lerp(from, look, 0.4f)) - 0.5f, "모듈 앞에서 멈춤");
            Assert.IsTrue(ArkShot.Free(occupied, p));
        }
    }
}
