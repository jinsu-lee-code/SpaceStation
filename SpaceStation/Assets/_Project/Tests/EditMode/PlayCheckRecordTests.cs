using NUnit.Framework;
using SpaceStation.PlayCheck;

namespace SpaceStation.Tests.EditMode
{
    /// <summary>플레이 기록 (CONNECT_1121 V3, 검증용 빌드에서만) — 세는 것 · 시간 나누기 · 파일 본문.</summary>
    public class PlayCheckRecordTests
    {
        private static PlayCheckRecord NewRecord()
        {
            var names = new[] { "등급 없음", "소형", "중형", "대형" };
            var r = new PlayCheckRecord("2026-11-26 10:00", "0.11.0", g => names[g]);
            r.BeginGame("새 게임", 1, 0f);
            return r;
        }

        [Test]
        public void Entries_CountContextsAndNothing()
        {
            var r = NewRecord();
            r.Entered("배터리", EntryContext.Damaged | EntryContext.Crates, "파손 2 · 보급 상자 1");
            r.Exited("배터리");
            r.Entered("코어", EntryContext.None, "");
            Assert.AreEqual(2, r.Entries);
            Assert.AreEqual(1, r.EntryContexts[EntryContext.Damaged]);
            Assert.AreEqual(1, r.EntryContexts[EntryContext.Crates]);
            Assert.IsFalse(r.EntryContexts.ContainsKey(EntryContext.Synthesized));
            Assert.AreEqual(1, r.EntriesWithNothing);
        }

        [Test]
        public void Advance_SplitsOutsideInsideByGrade()
        {
            var r = NewRecord();
            r.Advance(10f, 20f, false, 2f);      // 소형 바깥: 실제 10초 · 게임 20초 (2배속)
            r.Entered("코어", EntryContext.None, "");
            r.Advance(5f, 20f, true, 1f);        // 소형 안: 실제 5초, 일시정지라 게임 0초
            r.Exited("코어");
            r.GradeChanged(2);
            r.Advance(3f, 23f, false, 1f);       // 중형 바깥
            Assert.AreEqual(10f, r.GradeTimes[1][0], 1e-4f);
            Assert.AreEqual(5f, r.GradeTimes[1][1], 1e-4f);
            Assert.AreEqual(20f, r.GradeTimes[1][2], 1e-4f);
            Assert.AreEqual(0f, r.GradeTimes[1][3], 1e-4f);
            Assert.AreEqual(3f, r.GradeTimes[2][0], 1e-4f);
            Assert.AreEqual(5f, r.PausedRealSeconds, 1e-4f);
            Assert.AreEqual(10f, r.SpeedRealSeconds[2f], 1e-4f);
            Assert.AreEqual(2, r.MaxGrade);
        }

        [Test]
        public void AdvanceMenu_CountsOnlyRealTime()
        {
            var r = NewRecord();
            r.Advance(10f, 10f, false, 1f);
            r.AdvanceMenu(30f);                  // 메인 메뉴 30초: 바깥 · 일시정지에 안 들어감
            Assert.AreEqual(40f, r.RealSeconds, 1e-4f);
            Assert.AreEqual(30f, r.MenuRealSeconds, 1e-4f);
            Assert.AreEqual(10f, r.GradeTimes[1][0], 1e-4f);
            Assert.AreEqual(0f, r.PausedRealSeconds, 1e-4f);
        }

        [Test]
        public void Text_HasSummaryAnswersAndEvents()
        {
            var r = NewRecord();
            r.Advance(60f, 60f, false, 1f);
            r.Entered("배터리", EntryContext.Damaged, "파손 1");
            r.IncidentResolved("배관 누출");
            r.IncidentMissed("화재");
            r.WindowOpened("Research");
            r.WindowOpened("Research");
            r.Answered("게임 시간 90분", new[] { "고장 난 걸 고치러", "", "합성" });
            string text = r.ToText("2026-11-26 11:30:00");
            StringAssert.Contains("내부 진입 1회", text);
            StringAssert.Contains("파손 1", text);
            StringAssert.Contains("처리 1 · 놓침 1", text);
            StringAssert.Contains("Research 2", text);
            StringAssert.Contains("고장 난 걸 고치러", text);
            StringAssert.Contains("(빈칸)", text);
            StringAssert.Contains("안에 들어감: 배터리", text);
            StringAssert.Contains("[게임 01:00 | 실제 01:00]", text);
        }

        [Test]
        public void Clock_FormatsHours()
        {
            Assert.AreEqual("05:07", PlayCheckRecord.Clock(307f));
            Assert.AreEqual("1:30:00", PlayCheckRecord.Clock(5400f));
        }
    }
}
