using NUnit.Framework;
using SpaceStation.Interior;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>화물 환적장 내부 후룸 코스 (Blender ct_builder와 같은 값): 닫힌 한 바퀴 · 칸 안 · 리프트 34.4° · 물보라 자리 · 속도 · 보트가 모델에 닿지 않음.</summary>
    public class CargoFlumeCourseTests
    {
        [Test]
        public void Loop_MatchesBlender()
        {
            Assert.AreEqual(73.979f, CargoFlumeCourse.Loop, 0.01f);
            Assert.AreEqual(34.4f, CargoFlumeCourse.LiftAngle, 0.15f);
            Assert.AreEqual(37.485f, CargoFlumeCourse.SplashS, 0.01f);
            Assert.AreEqual(29.465f, CargoFlumeCourse.TunnelIn, 0.01f);
            Assert.AreEqual(33.185f, CargoFlumeCourse.TunnelOut, 0.01f);
        }

        [Test]
        public void Course_ClosedContinuousAndInsideCells()
        {
            float loop = CargoFlumeCourse.Loop;
            var prev = CargoFlumeCourse.Point(0f);
            for (float s = 0.05f; s <= loop + 1e-3f; s += 0.05f)
            {
                var p = CargoFlumeCourse.Point(s);
                Assert.Less(Vector3.Distance(p, prev), 0.12f, $"끊김 s={s}");
                Assert.That(p.x, Is.InRange(-3.45f, 19.45f), $"칸 밖 x s={s}");
                Assert.That(p.z, Is.InRange(-3.45f, 11.45f), $"칸 밖 z s={s}");
                Assert.LessOrEqual(p.y + 0.45f, 3.95f, $"테가 천장 위 s={s}");
                prev = p;
            }
            Assert.Less(Vector3.Distance(CargoFlumeCourse.Point(loop), CargoFlumeCourse.Point(0f)), 1e-3f);
            Assert.AreEqual(CargoFlumeCourse.LowWater, CargoFlumeCourse.Water(0f), 1e-4f);
            Assert.AreEqual(CargoFlumeCourse.HighWater, CargoFlumeCourse.Water(30f), 1e-4f);
        }

        [Test]
        public void Speeds_ChainSteadyDropFastSplashSlows()
        {
            float lift = CargoFlumeCourse.LiftStart + CargoFlumeCourse.LiftLength * 0.5f;
            Assert.AreEqual(CargoFlumeCourse.LiftChainSpeed, CargoFlumeCourse.Speed(lift), 1e-3f);
            float splash = CargoFlumeCourse.Speed(CargoFlumeCourse.SplashS - 0.02f);
            Assert.That(splash, Is.InRange(6.0f, 6.9f), "물보라 직전 속도 (설계 6.46)");
            Assert.Less(CargoFlumeCourse.Speed(CargoFlumeCourse.SplashDragEnd + 0.3f), 1.9f, "물보라 못에서 느려짐");
            for (float s = 0f; s < CargoFlumeCourse.Loop; s += 0.25f)
                Assert.Greater(CargoFlumeCourse.Speed(s), 0.05f, $"멈춤 s={s}");
        }

        [Test]
        public void Pose_StaysLevelSideways()
        {
            for (float s = 0f; s < CargoFlumeCourse.Loop; s += 0.5f)
            {
                CargoFlumeCourse.Pose(s, out _, out var r);
                Assert.Less(Mathf.Abs((r * Vector3.right).y), 1e-3f, $"옆으로 기움 s={s}");
            }
        }

        /// <summary>보트 몸 (2.3 × 1.1, 용골 −0.25 ~ 뱃전 +0.66) + 앉은 머리 (+1.80)가 290곳에서 프리팹 몸체 · 유리 · 터널에 닿지 않음.</summary>
        [Test]
        public void BoatSweep_ClearsModel()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Interior/PF_Interior_EvoCargoTransfer.prefab");
            if (prefab == null)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            var root = Object.Instantiate(prefab);
            try
            {
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var keep = new System.Collections.Generic.List<Collider>();
                foreach (var c in root.GetComponentsInChildren<MeshCollider>(true))
                {
                    if (c.name.EndsWith("_Shell") || c.name.EndsWith("_Glass"))
                        keep.Add(c);
                }
                foreach (var c in root.GetComponentsInChildren<Collider>(true))
                {
                    if (!keep.Contains(c))
                        c.enabled = false;
                }
                Physics.SyncTransforms();
                int hits = 0;
                string first = null;
                float loop = CargoFlumeCourse.Loop;
                for (int i = 0; i < 290; i++)
                {
                    float s = loop * i / 290f;
                    CargoFlumeCourse.Pose(s, out var p, out var r);
                    var boatCenter = p + r * new Vector3(0f, 0.22f, 0f);
                    var headCenter = p + r * new Vector3(0f, 1.25f, 0.05f);
                    var a = Physics.OverlapBox(boatCenter, new Vector3(0.53f, 0.40f, 1.13f), r);
                    var b = Physics.OverlapBox(headCenter, new Vector3(0.40f, 0.55f, 0.5f), r);
                    if (a.Length + b.Length > 0)
                    {
                        hits++;
                        first ??= $"s={s:F2} {(a.Length > 0 ? a[0].name : b[0].name)}";
                    }
                }
                Assert.AreEqual(0, hits, first);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
