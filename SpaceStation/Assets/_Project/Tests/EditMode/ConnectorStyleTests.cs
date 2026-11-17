using NUnit.Framework;
using SpaceStation.Core;

namespace SpaceStation.Tests
{
    /// <summary>진화 단계별 연결 통로 모양: 높은 단계 쪽 · 3단계부터 다리 데크 · 데크가 2단계 이하에 닿는 끝 = 에어록 문 · 위아래는 기둥만.</summary>
    public class ConnectorStyleTests
    {
        [Test]
        public void HigherStageWins()
        {
            Assert.AreEqual(0, ConnectorStyle.For(0, 0, false).Stage);
            Assert.AreEqual(1, ConnectorStyle.For(0, 1, false).Stage);
            Assert.AreEqual(2, ConnectorStyle.For(2, 1, false).Stage);
            Assert.AreEqual(4, ConnectorStyle.For(3, 4, true).Stage);
        }

        [Test]
        public void DeckFromStageThree_LockOnLowerSide()
        {
            var s = ConnectorStyle.For(3, 1, false);
            Assert.IsTrue(s.IsDeck);
            Assert.IsFalse(s.LockAtA, "3단계 쪽은 바닥이 이어짐");
            Assert.IsTrue(s.LockAtB, "1단계 쪽 = 에어록 문");

            var both = ConnectorStyle.For(3, 4, false);
            Assert.IsTrue(both.IsDeck);
            Assert.IsFalse(both.LockAtA || both.LockAtB, "3단계 이상끼리는 문 없이 바닥만");

            var basic = ConnectorStyle.For(0, 4, false);
            Assert.IsTrue(basic.LockAtA, "기본 모듈도 에어록 문");
        }

        [Test]
        public void InteriorDoors_OpenAirlockNormal()
        {
            Assert.AreEqual(DoorKind.Open, ConnectorStyle.DoorAt(3, 4), "3단계 이상끼리 = 바닥이 이어짐");
            Assert.AreEqual(DoorKind.Open, ConnectorStyle.DoorAt(3, 1), "다리 쪽 (3단계)은 트임");
            Assert.AreEqual(DoorKind.Airlock, ConnectorStyle.DoorAt(1, 3), "낮은 쪽 = 에어록 문");
            Assert.AreEqual(DoorKind.Airlock, ConnectorStyle.DoorAt(2, 4), "2단계도 3단계 다리와 붙으면 에어록");
            Assert.AreEqual(DoorKind.Airlock, ConnectorStyle.DoorAt(2, 1), "현관의 정원 쪽 끝");
            Assert.AreEqual(DoorKind.Normal, ConnectorStyle.DoorAt(1, 2), "현관의 닫힌 쪽 끝");
            Assert.AreEqual(DoorKind.Open, ConnectorStyle.DoorAt(2, 2), "2 ↔ 2 = 트인 문간");
            Assert.AreEqual(DoorKind.Normal, ConnectorStyle.DoorAt(0, 1));
        }

        [Test]
        public void Vestibule_OnlyStageTwoWithLowerSide()
        {
            Assert.IsTrue(ConnectorStyle.ForInterior(2, 0).IsVestibule);
            Assert.IsTrue(ConnectorStyle.ForInterior(1, 2).IsVestibule);
            Assert.IsFalse(ConnectorStyle.ForInterior(2, 2).IsVestibule, "2 ↔ 2 = 보관함 없는 트인 문간");
            Assert.IsFalse(ConnectorStyle.ForInterior(3, 1).IsVestibule, "3단계 = 다리");
        }

        [Test]
        public void BelowDeck_NoLock_VerticalNeverDeck()
        {
            var s = ConnectorStyle.For(2, 1, false);
            Assert.IsFalse(s.IsDeck);
            Assert.IsFalse(s.LockAtA || s.LockAtB);

            var v = ConnectorStyle.For(3, 1, true);
            Assert.AreEqual(3, v.Stage);
            Assert.IsFalse(v.LockAtA || v.LockAtB, "위아래는 데크 · 에어록 없음");
        }
    }
}
