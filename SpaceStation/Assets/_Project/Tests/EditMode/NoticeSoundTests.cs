using NUnit.Framework;
using SpaceStation.Audio;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>바깥 알림 소리 규칙 (2026-11-26, SOUND_LIST_NEW 2번 · D-2 · D-3): 연결 변화 · 한 프레임 묶음 · 과열 반복 · 낮밤 · 접촉 세력 · 방향 좌우 · 위계 자리.</summary>
    public class NoticeSoundTests
    {
        [Test]
        public void Connect_NewModuleIsSilent()
        {
            Assert.AreEqual(ConnectChange.None, NoticeSound.Classify(false, false, true), "새로 지은 모듈의 첫 연결");
            Assert.AreEqual(ConnectChange.None, NoticeSound.Classify(false, false, false), "불러온 판");
            Assert.AreEqual(ConnectChange.None, NoticeSound.Classify(true, true, true));
            Assert.AreEqual(ConnectChange.Lost, NoticeSound.Classify(true, true, false));
            Assert.AreEqual(ConnectChange.Regained, NoticeSound.Classify(true, false, true));
        }

        [Test]
        public void Connect_ManyInOneFrameIsOneSound()
        {
            Assert.AreEqual(ConnectChange.None, NoticeSound.Pick(0, 0));
            Assert.AreEqual(ConnectChange.Lost, NoticeSound.Pick(7, 0), "다리 모듈 하나를 철거해 끝쪽 일곱이 꺼짐 = 소리 하나");
            Assert.AreEqual(ConnectChange.Regained, NoticeSound.Pick(0, 3));
            Assert.AreEqual(ConnectChange.Lost, NoticeSound.Pick(2, 2), "같으면 나쁜 쪽");
            Assert.AreEqual(ConnectChange.Regained, NoticeSound.Pick(1, 4), "옮기기로 하나 끊기고 넷이 이어짐");
        }

        [Test]
        public void Heat_RepeatsAtMostEvery30s()
        {
            Assert.IsFalse(NoticeSound.HeatWarn(0, 100f, -999f), "새로 위험 열에 든 모듈이 없으면");
            Assert.IsTrue(NoticeSound.HeatWarn(1, 100f, -999f));
            Assert.IsFalse(NoticeSound.HeatWarn(3, 120f, 100f), "30초 안");
            Assert.IsTrue(NoticeSound.HeatWarn(1, 130f, 100f));
            Assert.IsTrue(NoticeSound.HeatWarn(1, 5f, 300f), "플레이를 다시 켜 시계가 처음부터");
        }

        [Test]
        public void DayNight_OnlyOnChange()
        {
            Assert.AreEqual(0, NoticeSound.DayChange(-1, true), "처음 읽을 때는 소리 없음");
            Assert.AreEqual(0, NoticeSound.DayChange(1, true));
            Assert.AreEqual(1, NoticeSound.DayChange(0, true), "새벽");
            Assert.AreEqual(-1, NoticeSound.DayChange(1, false), "해 질 녘");
        }

        [Test]
        public void Contact_FactionSignalOnlyWhenDecoded()
        {
            var lib = ScriptableObject.CreateInstance<SoundLibrary>();
            try
            {
                Assert.AreSame(lib.AlertCaution, NoticeSound.ContactArriveCue(lib, ContactFaction.Outlaw, false), "정체 불명은 주의 경보 그대로 (소리로 정체가 새지 않게)");
                Assert.AreSame(lib.ContactArrive1, NoticeSound.ContactArriveCue(lib, ContactFaction.Merchant, true));
                Assert.AreSame(lib.ContactArrive2, NoticeSound.ContactArriveCue(lib, ContactFaction.Scholar, true));
                Assert.AreSame(lib.ContactArrive3, NoticeSound.ContactArriveCue(lib, ContactFaction.Outlaw, true));
                Assert.IsNull(NoticeSound.ContactArriveCue(null, ContactFaction.Merchant, true));
            }
            finally
            {
                Object.DestroyImmediate(lib);
            }
        }

        [Test]
        public void ShowerDirection_PansLeftRight()
        {
            Assert.AreEqual(0.6f, NoticeSound.DirectionPan(Vector3.right, Vector3.right), 1e-4f, "동쪽 = 카메라 오른쪽");
            Assert.AreEqual(-0.6f, NoticeSound.DirectionPan(Vector3.left, Vector3.right), 1e-4f);
            Assert.AreEqual(0f, NoticeSound.DirectionPan(Vector3.up, Vector3.right), 1e-4f, "위 · 아래는 가운데");
            Assert.AreEqual(0f, NoticeSound.DirectionPan(Vector3.forward, Vector3.right), 1e-4f);
            Assert.AreEqual(0f, NoticeSound.DirectionPan(Vector3.zero, Vector3.right), 1e-4f);
        }

        [Test]
        public void Hierarchy_HigherHoldsLower()
        {
            var last = new[] { -999f, -999f, 10f, -999f };   // 위험이 10초에 울림
            Assert.IsTrue(AlertSound.HeldByHigher((int)AlertLevel.Caution, 10.5f, last), "위험 뒤 1.2초 안 주의 (과열 · 유성우 · 접촉 도착 뜻 소리도) 생략");
            Assert.IsFalse(AlertSound.HeldByHigher((int)AlertLevel.Caution, 11.5f, last));
            Assert.IsFalse(AlertSound.HeldByHigher((int)AlertLevel.Danger, 10.5f, last), "같은 단계는 큐 쉬는 시간이 맡음");
            Assert.IsFalse(AlertSound.HeldByHigher((int)AlertLevel.Info, 5f, last), "플레이를 다시 켜 시계가 처음부터");
        }
    }
}
