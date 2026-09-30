using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>4-10 추가 실패 조건 (BALANCE 21번).</summary>
    public class FailureTests
    {
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<BalanceConfig>();
            _created.Add(_config);
            var so = new SerializedObject(_config);
            so.FindProperty("_oxygenFailSeconds").floatValue = 60f;
            so.FindProperty("_satisfactionFailSeconds").floatValue = 90f;
            so.FindProperty("_coreCollapseSeconds").floatValue = 30f;
            so.FindProperty("_coreCollapseMinNeighbors").intValue = 2;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        private static GameOverReason Run(FailureMonitor m, int seconds, bool o2, float sat, int neighbors, int down)
        {
            var reason = GameOverReason.None;
            for (int i = 0; i < seconds && reason == GameOverReason.None; i++)
                reason = m.Tick(1f, o2, sat, neighbors, down);
            return reason;
        }

        [Test]
        public void Oxygen_FailsAfter60s_ResetsWhenRecovered()
        {
            var m = new FailureMonitor(_config);
            Assert.AreEqual(GameOverReason.None, Run(m, 59, true, 50f, 0, 0));
            Assert.AreEqual(1f, m.OxygenRemaining, 1e-3f);
            m.Tick(1f, false, 50f, 0, 0);
            Assert.AreEqual(-1f, m.OxygenRemaining, "회복하면 초기화");
            Assert.AreEqual(GameOverReason.None, Run(m, 59, true, 50f, 0, 0));
            Assert.AreEqual(GameOverReason.Oxygen, m.Tick(1f, true, 50f, 0, 0));
        }

        [Test]
        public void Satisfaction_FailsAfter90sAtZero()
        {
            var m = new FailureMonitor(_config);
            Assert.AreEqual(GameOverReason.None, Run(m, 89, false, 0f, 0, 0));
            Assert.AreEqual(GameOverReason.Satisfaction, m.Tick(1f, false, 0f, 0, 0));
        }

        [Test]
        public void CoreCollapse_NeedsMinNeighbors_AllDown()
        {
            var m = new FailureMonitor(_config);
            Assert.AreEqual(GameOverReason.None, Run(m, 100, false, 50f, 1, 1), "이웃 1개는 보호");
            Assert.AreEqual(GameOverReason.None, Run(m, 100, false, 50f, 3, 2), "하나라도 멀쩡하면 안전");
            Assert.AreEqual(GameOverReason.CoreCollapse, Run(m, 30, false, 50f, 3, 3));
        }

        [Test]
        public void ZeroSetting_DisablesCondition()
        {
            var so = new SerializedObject(_config);
            so.FindProperty("_oxygenFailSeconds").floatValue = 0f;
            so.ApplyModifiedPropertiesWithoutUndo();
            var m = new FailureMonitor(_config);
            Assert.AreEqual(GameOverReason.None, Run(m, 1000, true, 50f, 0, 0));
            Assert.AreEqual(-1f, m.OxygenRemaining);
        }

        [Test]
        public void Session_FailOnce_KeepsFirstReason()
        {
            var s = new GameSession();
            int fired = 0;
            s.GameOver += () => fired++;
            s.Fail(GameOverReason.Oxygen);
            s.Fail(GameOverReason.CoreCollapse);
            s.ObservePopulation(5);
            s.ObservePopulation(0);
            Assert.AreEqual(1, fired);
            Assert.AreEqual(GameOverReason.Oxygen, s.Reason);
        }
    }
}
