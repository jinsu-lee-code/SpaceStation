using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Core;

namespace SpaceStation.Tests
{
    public class TickClockTests
    {
        private TickClock _clock;
        private List<long> _ticks;

        [SetUp]
        public void SetUp()
        {
            _clock = new TickClock(1f, 8);
            _ticks = new List<long>();
            _clock.Ticked += t => _ticks.Add(t);
        }

        [Test]
        public void Advance_AccumulatesUntilInterval()
        {
            _clock.Advance(0.4f);
            _clock.Advance(0.4f);
            Assert.AreEqual(0, _ticks.Count);
            _clock.Advance(0.4f);
            CollectionAssert.AreEqual(new long[] { 1 }, _ticks);
            Assert.AreEqual(0.2f, _clock.Progress, 1e-4f);
        }

        [Test]
        public void Advance_MultipleTicksInOneCall()
        {
            _clock.Advance(3.5f);
            CollectionAssert.AreEqual(new long[] { 1, 2, 3 }, _ticks);
            Assert.AreEqual(3, _clock.TickCount);
            Assert.AreEqual(3f, _clock.SimulatedSeconds, 1e-4f);
        }

        [Test]
        public void Speed_MultipliesTickRate()
        {
            _clock.SetSpeed(4f);
            _clock.Advance(1f);
            Assert.AreEqual(4, _ticks.Count);

            _clock.SetSpeed(2f);
            _clock.Advance(1f);
            Assert.AreEqual(6, _ticks.Count);
        }

        [Test]
        public void Pause_StopsTicks_AndResumeContinues()
        {
            _clock.Advance(0.5f);
            _clock.SetPaused(true);
            _clock.Advance(10f);
            Assert.AreEqual(0, _ticks.Count);

            _clock.SetPaused(false);
            _clock.Advance(0.5f);
            Assert.AreEqual(1, _ticks.Count, "일시정지 전 누적분은 유지");
        }

        [Test]
        public void MaxTicksPerAdvance_DropsExcess()
        {
            _clock.Advance(100.25f);
            Assert.AreEqual(8, _ticks.Count);
            Assert.AreEqual(0.25f, _clock.Progress, 1e-3f);
        }

        [Test]
        public void Events_FireOnlyOnChange()
        {
            int speedEvents = 0, pauseEvents = 0;
            _clock.SpeedChanged += _ => speedEvents++;
            _clock.PausedChanged += _ => pauseEvents++;

            _clock.SetSpeed(1f);
            _clock.SetSpeed(2f);
            _clock.SetPaused(false);
            _clock.TogglePause();
            _clock.TogglePause();

            Assert.AreEqual(1, speedEvents);
            Assert.AreEqual(2, pauseEvents);
        }

        [Test]
        public void InvalidArguments_Throw()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new TickClock(0f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => _clock.SetSpeed(0f));
        }
    }
}
