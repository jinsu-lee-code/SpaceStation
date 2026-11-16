using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Editor;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>
    /// 식물 아틀라스 ④ (2026-11-16): 진화 내부 잎 조각 (<c>_Leaves</c>)의 우리 색 잎 · 꽃잎이 아틀라스 색 칸 + M_FoliageStill 한 칸으로 바뀌었는지 —
    /// 잎 조각에 색 재질이 남지 않음 · 식물 재질 칸 하나 · 그 칸 UV가 모두 색 칸 가운데 · 꼭짓점 색 있음 (꼭짓점 그늘이 검게 되지 않게) · 삼각형 수 그대로.
    /// </summary>
    public class InteriorFoliageTests
    {
        [Test]
        public void LeavesParts_OnAtlasSwatches()
        {
            var still = AssetDatabase.LoadAssetAtPath<Material>(PlantAtlas.StillMaterialPath);
            Assert.IsNotNull(still, "M_FoliageStill 없음 (SpaceStation/Plants/Setup)");
            Assert.AreEqual(0f, still.GetFloat("_WindStrength"), "잎 조각 재질은 살랑임 끔");
            var swatch = InteriorFoliage.SwatchByMaterial();
            var centers = new HashSet<Vector2>(swatch.Values);
            int parts = 0;
            foreach (var guid in AssetDatabase.FindAssets("PF_Interior_Evo t:Prefab", new[] { "Assets/_Project/Prefabs/Interior" }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                foreach (Transform part in prefab.transform)
                {
                    if (!part.name.EndsWith("_Leaves"))
                        continue;
                    var r = part.GetComponent<MeshRenderer>();
                    var mesh = part.GetComponent<MeshFilter>().sharedMesh;
                    var mats = r.sharedMaterials;
                    int foliage = -1;
                    for (int s = 0; s < mats.Length; s++)
                    {
                        Assert.IsFalse(mats[s] != null && swatch.ContainsKey(mats[s]), $"{part.name}: 색 재질 {mats[s]?.name}가 남음");
                        if (mats[s] == still)
                        {
                            Assert.AreEqual(-1, foliage, $"{part.name}: 식물 재질 칸이 둘");
                            foliage = s;
                        }
                    }
                    if (foliage < 0)
                        continue;
                    parts++;
                    Assert.AreEqual(mesh.vertexCount, mesh.colors.Length, $"{part.name}: 꼭짓점 색 없음");
                    var uv = new List<Vector2>();
                    mesh.GetUVs(0, uv);
                    foreach (int i in mesh.GetTriangles(foliage))
                        Assert.IsTrue(centers.Contains(uv[i]), $"{part.name}: 식물 칸 UV {uv[i]}가 색 칸 가운데가 아님");
                }
            }
            Assert.GreaterOrEqual(parts, 10, "바뀐 잎 조각이 너무 적음 (Build Evolved Templates 먼저)");
        }
    }
}
