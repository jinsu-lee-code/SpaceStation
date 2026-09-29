using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    public class EventSchedulerTests
    {
        private readonly List<Object> _created = new List<Object>();
        private readonly Queue<float> _rolls = new Queue<float>();
        private float _defaultRoll;
        private List<GameEventData> _started;
        private List<GameEventData> _ended;

        [SetUp]
        public void SetUp()
        {
            _rolls.Clear();
            _defaultRoll = 0f;
            _started = new List<GameEventData>();
            _ended = new List<GameEventData>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        private float Roll() => _rolls.Count > 0 ? _rolls.Dequeue() : _defaultRoll;

        private EventScheduler Create(float grace = 180f, float min = 90f, float max = 150f)
        {
            var s = new EventScheduler(grace, min, max, Roll);
            s.EventStarted += e => _started.Add(e);
            s.EventEnded += a => _ended.Add(a.Data);
            return s;
        }

        [Test]
        public void FirstEvent_AfterGracePlusInterval()
        {
            var a = Event("A");
            var s = Create(); // roll 0 → 첫 간격 90 → 180 + 90 = 270초
            Ticks(s, 269, a);
            Assert.AreEqual(0, _started.Count);
            Ticks(s, 1, a);
            CollectionAssert.AreEqual(new[] { a }, _started);
        }

        [Test]
        public void FollowingEvents_UseRandomIntervalInRange()
        {
            var a = Event("A");
            _defaultRoll = 0.999f; // 간격 ≈ 150
            var s = Create(grace: 0f);
            Ticks(s, 149, a);
            Assert.AreEqual(0, _started.Count);
            Ticks(s, 1, a);
            Assert.AreEqual(1, _started.Count);
            Ticks(s, 150, a);
            Assert.AreEqual(2, _started.Count);
        }

        [Test]
        public void WeightedPick_FollowsWeights()
        {
            var light = Event("Light", weight: 1f);
            var heavy = Event("Heavy", weight: 3f);
            var pool = new[] { light, heavy };
            var s = Create();

            _rolls.Enqueue(0.2f); // 0.2 × 4 = 0.8 < 1 → Light
            Assert.AreSame(light, s.TriggerRandom(pool));
            _rolls.Enqueue(0.3f); // 1.2 → Heavy
            Assert.AreSame(heavy, s.TriggerRandom(pool));
        }

        [Test]
        public void ZeroWeight_NeverPickedRandomly_ButCanBeTriggered()
        {
            var never = Event("Never", weight: 0f);
            var s = Create();
            Assert.IsNull(s.TriggerRandom(new[] { never }));
            Assert.IsTrue(s.Trigger(never));
        }

        [Test]
        public void ActiveSameEvent_IsNotPickedAgain()
        {
            var storm = Event("Storm", duration: 60f);
            var other = Event("Other");
            var s = Create();
            s.Trigger(storm);

            _rolls.Enqueue(0f); // 원래라면 첫 후보(storm)
            Assert.AreSame(other, s.TriggerRandom(new[] { storm, other }));
            Assert.IsNull(s.TriggerRandom(new[] { storm }), "후보가 모두 진행 중이면 발생 없음");
            Assert.IsFalse(s.Trigger(storm));
        }

        [Test]
        public void TimedEvent_EndsAfterDuration()
        {
            var storm = Event("Storm", duration: 60f);
            var s = Create();
            s.Trigger(storm);
            Assert.AreEqual(1, s.ActiveEvents.Count);

            Ticks(s, 59, null);
            Assert.IsTrue(s.IsActive(storm));
            Assert.AreEqual(1f, s.ActiveEvents[0].Remaining, 1e-3f);
            Ticks(s, 1, null);
            Assert.IsFalse(s.IsActive(storm));
            CollectionAssert.AreEqual(new[] { storm }, _ended);
        }

        [Test]
        public void InstantEvent_FiresOnlyStarted()
        {
            var meteor = Event("Meteor");
            var s = Create();
            s.Trigger(meteor);
            Assert.AreEqual(0, s.ActiveEvents.Count);
            CollectionAssert.AreEqual(new[] { meteor }, _started);
            Ticks(s, 10, null);
            Assert.AreEqual(0, _ended.Count);
        }

        [Test]
        public void DifferentEvents_CanOverlap()
        {
            var storm = Event("Storm", duration: 200f);
            var meteor = Event("Meteor");
            var s = Create(grace: 0f, min: 90f, max: 90f);
            s.Trigger(storm);

            Ticks(s, 90, new[] { storm, meteor });
            Assert.IsTrue(s.IsActive(storm), "지속형 진행 중에도 타이머는 계속 돈다");
            CollectionAssert.AreEqual(new[] { storm, meteor }, _started);
        }

        [Test]
        public void IntervalMultiplier_AppliesToNextIntervals()
        {
            var a = Event("A");
            var s = Create(grace: 0f, min: 100f, max: 100f); // 첫 간격 100 (배율 설정 전)
            float multiplier = 0.4f;
            s.IntervalMultiplier = () => multiplier;
            Ticks(s, 100, a);
            Assert.AreEqual(1, _started.Count);
            Ticks(s, 39, a);
            Assert.AreEqual(1, _started.Count);
            Ticks(s, 1, a);
            Assert.AreEqual(2, _started.Count, "다음 간격 100 × 0.4 = 40");
        }

        [Test]
        public void DurationProvider_OverridesTimedDuration()
        {
            var storm = Event("Storm", duration: 60f);
            var s = Create();
            s.DurationProvider = d => d.Duration * 2f;
            s.Trigger(storm);
            Assert.AreEqual(120f, s.ActiveEvents[0].Duration, 1e-3f);
            Ticks(s, 119, null);
            Assert.IsTrue(s.IsActive(storm));
            Ticks(s, 1, null);
            Assert.IsFalse(s.IsActive(storm));
        }

        [Test]
        public void InvalidInterval_Throws()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new EventScheduler(0f, 100f, 50f, Roll));
        }

        // ---- helpers ----

        private static void Ticks(EventScheduler s, int count, params GameEventData[] pool)
        {
            for (int i = 0; i < count; i++)
                s.Tick(1f, pool);
        }

        private GameEventData Event(string name, float weight = 1f, float duration = 0f)
        {
            var e = ScriptableObject.CreateInstance<GameEventData>();
            e.name = name;
            _created.Add(e);
            var so = new SerializedObject(e);
            so.FindProperty("_displayName").stringValue = name;
            so.FindProperty("_weight").floatValue = weight;
            so.FindProperty("_duration").floatValue = duration;
            so.ApplyModifiedPropertiesWithoutUndo();
            return e;
        }
    }
}
