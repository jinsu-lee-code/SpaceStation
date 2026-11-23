using NUnit.Framework;
using SpaceStation.Interior;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>저장 단지 내부 회전목마 (Blender sc_builder와 같은 값): 설 때 쉬는 높이 · 운행 곡선 · 앉은 머리가 지붕 밑 · 캡슐과 탄 사람이 모델 · 집게 팔에 닿지 않음.</summary>
    public class StorageCarouselTests
    {
        [Test]
        public void Stops_AtRestHeights()
        {
            for (int k = 0; k < 6; k++)
            {
                for (int i = 0; i < StorageCarousel.Count; i++)
                    Assert.AreEqual(StorageCarousel.RestHeight(i), StorageCarousel.Height(i, 120f * k), 1e-4f, $"캡슐 {i} · {120 * k}°");
            }
            Assert.AreEqual(0f, Mathf.Repeat(840f, 120f), 1e-4f, "한 번 운행 = 120°의 배수");
            // 모델 캡슐 = 바깥 0번 자리 (x 16, 판 위 0.75)
            StorageCarousel.Pose(0, 0f, out var p, out var r);
            Assert.Less(Vector3.Distance(p, new Vector3(16f, StorageCarousel.DiskTop + 0.75f, 4f)), 1e-4f);
            Assert.Less(Quaternion.Angle(r, Quaternion.identity), 1e-3f);
        }

        [Test]
        public void Profile_SmoothAndComplete()
        {
            Assert.AreEqual(0f, InteriorCarousel.Profile(0f), 1e-5f);
            Assert.AreEqual(1f, InteriorCarousel.Profile(1f), 1e-5f);
            float prev = 0f;
            for (float u = 0.01f; u <= 1.0001f; u += 0.01f)
            {
                float s = InteriorCarousel.Profile(Mathf.Min(u, 1f));
                Assert.GreaterOrEqual(s, prev - 1e-6f, $"뒤로 감 u={u}");
                Assert.Less(s - prev, 0.0125f, $"튐 u={u}");
                prev = s;
            }
        }

        [Test]
        public void SeatedHead_UnderRoof()
        {
            for (float turn = 0f; turn < 360f; turn += 2f)
            {
                for (int i = 0; i < StorageCarousel.Count; i++)
                {
                    float h = StorageCarousel.Height(i, turn);
                    float head = StorageCarousel.DiskTop + h + StorageCarousel.SaddleUp + 0.95f;
                    Assert.Less(head, StorageCarousel.CeilingY - 0.2f, $"머리가 지붕 밑 0.2 안 캡슐 {i} · {turn}°");
                    Assert.Greater(h - 0.31f, 0.25f, $"발판이 판에 닿음 캡슐 {i} · {turn}°");
                }
            }
        }

        /// <summary>캡슐 몸 (발판 포함) + 앉은 사람 (안장 위 1 m)이 한 바퀴 동안 몸체 · 유리에 닿지 않고, 집게 팔 경계 상자와 0.08 m 떨어짐.</summary>
        [Test]
        public void Sweep_ClearsModelAndClaws()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Interior/PF_Interior_EvoStorageComplex.prefab");
            if (prefab == null)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            var root = Object.Instantiate(prefab);
            try
            {
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                foreach (var c in root.GetComponentsInChildren<Collider>(true))
                    c.enabled = c is MeshCollider && (c.name.EndsWith("_Shell") || c.name.EndsWith("_Glass"));
                Physics.SyncTransforms();
                var claws = new System.Collections.Generic.List<Bounds>();
                foreach (var rend in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (rend.name.Contains("_Claw"))
                    {
                        var b = rend.bounds;
                        b.Expand(new Vector3(0.16f, 0.3f, 0.16f));   // 끄덕임 (8°) · 여유
                        claws.Add(b);
                    }
                }
                Assert.AreEqual(2, claws.Count, "집게 팔 둘");
                int hits = 0;
                string first = null;
                for (float turn = 0f; turn < 360f; turn += 5f)
                {
                    for (int i = 0; i < StorageCarousel.Count; i++)
                    {
                        StorageCarousel.Pose(i, turn, out var p, out var r);
                        var body = p + r * new Vector3(0f, -0.06f, 0f);
                        var rider = p + r * new Vector3(0f, StorageCarousel.SaddleUp + 0.5f, StorageCarousel.SaddleBack);
                        var a = Physics.OverlapBox(body, new Vector3(0.4f, 0.31f, 0.5f), r);
                        var b = Physics.OverlapBox(rider, new Vector3(0.25f, 0.5f, 0.25f), r);
                        bool claw = false;
                        foreach (var cb in claws)
                            claw |= cb.Contains(rider) || cb.SqrDistance(rider) < 0.3f * 0.3f;
                        if (a.Length + b.Length > 0 || claw)
                        {
                            hits++;
                            first ??= $"캡슐 {i} · {turn}° {(a.Length > 0 ? a[0].name : b.Length > 0 ? b[0].name : "집게 팔")}";
                        }
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
