using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Interior;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>
    /// 엘리베이터 통로 검사 (사용자 2026-11-03 치안 본부: 지붕 판이 통로를 가로질러 올라갈 때 화면이 가려지고, 도착하면 그 판에 밀려 차 아래로 떨어져 샤프트에 갇힘 —
    /// 탈 때는 충돌을 꺼 두었다가 내릴 때 켜므로 겹친 면에 밀려남):
    /// 내부 프리팹마다 차가 오가는 구간 (안쪽 반지름 안, 첫 층 바닥 ~ 끝 층 차 지붕)을 세로로 지나는 조각 면이 없어야 함. 차 · 문짝 · 호출 버튼은 뺌.
    /// </summary>
    public class InteriorLiftShaftTests
    {
        [Test]
        public void NoSurfaceCrossesAnyLiftShaft()
        {
            var problems = new List<string>();
            int lifts = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project/Prefabs/Interior" }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                var lift = prefab != null ? prefab.GetComponent<InteriorLift>() : null;
                if (lift == null)
                    continue;
                lifts++;
                Scan(prefab, lift, problems);
            }
            Assert.Greater(lifts, 0, "엘리베이터가 있는 프리팹을 찾지 못함");
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        private static void Scan(GameObject prefab, InteriorLift lift, List<string> problems)
        {
            var so = new SerializedObject(lift);
            var car = (Transform)so.FindProperty("_car").objectReferenceValue;
            var center = so.FindProperty("_center").vector3Value;
            float inner = so.FindProperty("_innerRadius").floatValue;
            var stops = so.FindProperty("_stops");
            float low = float.MaxValue, high = float.MinValue;
            for (int i = 0; i < stops.arraySize; i++)
            {
                float s = stops.GetArrayElementAtIndex(i).floatValue;
                low = Mathf.Min(low, s);
                high = Mathf.Max(high, s);
            }
            float y0 = center.y + low + 0.05f;
            float y1 = high + car.GetComponent<MeshFilter>().sharedMesh.bounds.max.y;
            var samples = new List<Vector2> { new Vector2(center.x, center.z) };
            foreach (float r in new[] { inner * 0.5f, inner })
            {
                for (int k = 0; k < 12; k++)
                {
                    float a = 30f * k * Mathf.Deg2Rad;
                    samples.Add(new Vector2(center.x + r * Mathf.Cos(a), center.z + r * Mathf.Sin(a)));
                }
            }
            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || filter.transform.IsChildOf(car) || filter.name.Contains("LiftDoor") || filter.name.Contains("LiftCall") || filter.name.Contains("LiftBtn"))
                    continue;
                var toRoot = prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                var v = filter.sharedMesh.vertices;
                var t = filter.sharedMesh.triangles;
                for (int i = 0; i < t.Length; i += 3)
                {
                    var a = toRoot.MultiplyPoint3x4(v[t[i]]);
                    var b = toRoot.MultiplyPoint3x4(v[t[i + 1]]);
                    var c = toRoot.MultiplyPoint3x4(v[t[i + 2]]);
                    if (Mathf.Max(a.x, b.x, c.x) < center.x - inner || Mathf.Min(a.x, b.x, c.x) > center.x + inner
                        || Mathf.Max(a.z, b.z, c.z) < center.z - inner || Mathf.Min(a.z, b.z, c.z) > center.z + inner
                        || Mathf.Max(a.y, b.y, c.y) < y0 || Mathf.Min(a.y, b.y, c.y) > y1)
                        continue;
                    float den = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                    if (Mathf.Abs(den) < 1e-7f)
                        continue;
                    foreach (var s in samples)
                    {
                        float u = ((b.z - c.z) * (s.x - c.x) + (c.x - b.x) * (s.y - c.z)) / den;
                        float w = ((c.z - a.z) * (s.x - c.x) + (a.x - c.x) * (s.y - c.z)) / den;
                        float e = 1f - u - w;
                        if (u < 0f || w < 0f || e < 0f)
                            continue;
                        float y = u * a.y + w * b.y + e * c.y;
                        if (y >= y0 && y <= y1)
                        {
                            problems.Add($"{prefab.name}: {filter.name} 면이 통로를 가로지름 (y {y:F2}, 지점 {s})");
                            goto NextPart;
                        }
                    }
                }
                NextPart:;
            }
        }
    }
}
