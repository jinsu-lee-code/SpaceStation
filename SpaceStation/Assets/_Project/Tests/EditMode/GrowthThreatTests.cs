using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpaceStation.Tests
{
    /// <summary>
    /// 13-2 타이쿤판 성장 비례 위협: 기준점 보간 · 노출 면이 넓게 퍼질수록 커짐(촘촘하면 작음) · 이벤트 규모가 등급 대신 이 값 ·
    /// 모듈 수 → 강도 · 밀집도 → 파업 기준.
    /// </summary>
    public class GrowthThreatTests
    {
        private const float Eps = 1e-3f;
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private GrowthThreatConfig _threat;
        private ModuleData _core, _box;

        [SetUp]
        public void SetUp()
        {
            _config = Create<BalanceConfig>();
            var b = new SerializedObject(_config);
            b.FindProperty("_startingPopulation").intValue = 10;
            b.FindProperty("_showerMinGrade").intValue = 99;
            b.ApplyModifiedPropertiesWithoutUndo();

            _core = Module("MD_TestCore", so =>
            {
                so.FindProperty("_removable").boolValue = false;
                so.FindProperty("_housingCapacity").intValue = 20;
            });
            _box = Module("MD_TestBox", _ => { });

            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            var list = g.FindProperty("_grades");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).FindPropertyRelative("_minPopulation").intValue = 0;
            list.GetArrayElementAtIndex(0).FindPropertyRelative("_minModules").intValue = 0;
            var unlocks = list.GetArrayElementAtIndex(0).FindPropertyRelative("_unlocks");
            unlocks.arraySize = 1;
            unlocks.GetArrayElementAtIndex(0).objectReferenceValue = _box;
            g.ApplyModifiedPropertiesWithoutUndo();

            _threat = Create<GrowthThreatConfig>();
            _threat.Set(new List<ExposurePoint>
                {
                    new ExposurePoint(0f, 1f, 1f, 1f),
                    new ExposurePoint(20f, 5f, 7f, 0.5f),
                },
                new List<ModulePoint> { new ModulePoint(0f, 1f), new ModulePoint(10f, 2f) },
                0.8f, 10f);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void Config_InterpolatesAndClamps()
        {
            var mid = _threat.AtExposure(10f);
            Assert.AreEqual(3f, mid.HitsMin, Eps);
            Assert.AreEqual(4f, mid.HitsMax, Eps);
            Assert.AreEqual(0.75f, mid.Interval, Eps);
            Assert.AreEqual(5f, _threat.AtExposure(500f).HitsMin, Eps, "마지막 기준점 뒤는 그대로");
            Assert.AreEqual(1.5f, _threat.IntensityAt(5f), Eps);
            Assert.AreEqual(0f, _threat.CrowdingThreshold(0.8f), Eps);
            Assert.AreEqual(5f, _threat.CrowdingThreshold(0.9f), Eps);
            Assert.AreEqual(10f, _threat.CrowdingThreshold(1f), Eps);
        }

        [Test]
        public void EventScale_FollowsExposure_SpreadIsWorseThanCompact()
        {
            var line = Sim(out var lineThreat);
            Assert.AreSame(lineThreat, line.EventScale, "등급 대신 성장 비례");
            for (int x = 1; x <= 3; x++)
                Assert.IsTrue(line.TryPlace(_box, new Vector3Int(x, 0, 0), 0, out _));
            line.Tick(0.1f);

            var square = Sim(out var squareThreat);
            foreach (var p in new[] { new Vector3Int(1, 0, 0), new Vector3Int(0, 0, 1), new Vector3Int(1, 0, 1) })
                Assert.IsTrue(square.TryPlace(_box, p, 0, out _));
            square.Tick(0.1f);

            Assert.AreEqual(4, lineThreat.ModuleCount);
            Assert.AreEqual(18, lineThreat.Exposure, "일렬 4칸: 양 끝 5면 + 가운데 4면 × 2");
            Assert.AreEqual(16, squareThreat.Exposure, "2×2: 칸마다 4면");
            Assert.Greater(lineThreat.EventIntervalMultiplier, 0f);
            Assert.Less(lineThreat.EventIntervalMultiplier, squareThreat.EventIntervalMultiplier, "넓게 퍼지면 더 자주");
            Assert.AreEqual(Mathf.RoundToInt(_threat.AtExposure(18f).HitsMin), line.EventScale.MeteorHitsMin);
            Assert.AreEqual(_threat.IntensityAt(4f), line.EventIntensity, Eps, "모듈 수 → 강도");
        }

        [Test]
        public void Crowding_RaisesCrisisThreshold()
        {
            var policies = Create<PolicyConfig>();
            var sim = Sim(out var threat, policies);
            float baseline = policies.StrikeThreshold;
            sim.Resources.SetPopulation(10); // 수용 20 → 50%
            Assert.AreEqual(baseline, sim.Crisis.Threshold, Eps);
            sim.Resources.SetPopulation(20); // 100%
            Assert.AreEqual(1f, threat.Crowding, Eps);
            Assert.AreEqual(baseline + 10f, sim.Crisis.Threshold, Eps);
        }

        // ---------------- 도우미 ----------------

        private StationSimulation Sim(out GrowthThreatSystem threat, PolicyConfig policies = null)
        {
            var sim = new StationSimulation(new StationSimulationSettings
            {
                Balance = _config,
                Grades = _grades,
                CoreModule = _core,
                Events = new List<GameEventData>(),
                Policies = policies,
                Random01 = () => 0.5f,
                HazardRandom01 = () => 0.99f,
            });
            threat = GrowthThreatSystem.AttachTo(sim, _threat);
            return sim;
        }

        private ModuleData Module(string name, System.Action<SerializedObject> setup)
        {
            var m = Create<ModuleData>();
            m.name = name;
            var so = new SerializedObject(m);
            var offsets = so.FindProperty("_cellOffsets");
            offsets.arraySize = 1;
            offsets.GetArrayElementAtIndex(0).vector3IntValue = Vector3Int.zero;
            setup(so);
            so.ApplyModifiedPropertiesWithoutUndo();
            return m;
        }

        private T Create<T>() where T : ScriptableObject
        {
            var o = ScriptableObject.CreateInstance<T>();
            _created.Add(o);
            return o;
        }
    }
}
