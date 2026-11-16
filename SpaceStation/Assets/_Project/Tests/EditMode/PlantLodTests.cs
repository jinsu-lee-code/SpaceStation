using NUnit.Framework;
using SpaceStation.Editor;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>
    /// ⑤-4 거리별 단순화 (2026-11-16): 우리 식물 여덟 = LODGroup 프리팹 (LOD0 원래 · LOD1 · LOD2 plant_builder --lod, 같은 크기 · 식물 재질 하나 · 끝까지 그림),
    /// 동물 (SM_InteriorCritters) = Unity Mesh LOD, 식물 원본 FBX는 자동 Mesh LOD를 쓰지 않음 (작은 잎 · 꽃을 통째로 지웠음).
    /// </summary>
    public class PlantLodTests
    {
        [Test]
        public void PlantPrefabs_HaveThreeLods()
        {
            var foliage = AssetDatabase.LoadAssetAtPath<Material>(PlantAtlas.MaterialPath);
            foreach (var name in PlantLods.Names)
            {
                var prefab = PlantLods.Prefab(name);
                Assert.IsNotNull(prefab, name + ": 프리팹 없음 (SpaceStation/Interior/Build Plant LOD Prefabs)");
                var group = prefab.GetComponent<LODGroup>();
                Assert.IsNotNull(group, name);
                var lods = group.GetLODs();
                Assert.AreEqual(3, lods.Length, name);
                Assert.AreEqual(0f, lods[2].screenRelativeTransitionHeight, name + ": 마지막 단계는 잘라내지 않음 (먼 정원 숲)");
                long prev = long.MaxValue;
                Bounds b0 = default;
                for (int l = 0; l < lods.Length; l++)
                {
                    Assert.AreEqual(1, lods[l].renderers.Length, name);
                    var r = lods[l].renderers[0];
                    var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                    Assert.IsNotNull(mesh, name + " LOD" + l);
                    Assert.AreEqual(foliage, r.sharedMaterial, name + " LOD" + l + ": 식물 재질 하나");
                    long tris = 0;
                    for (int s = 0; s < mesh.subMeshCount; s++)
                        tris += mesh.GetIndexCount(s) / 3;
                    Assert.Less(tris, l == 0 ? long.MaxValue : prev * (l == 1 ? 0.6 : 0.85), name + " LOD" + l + ": 삼각형이 덜 줄었음");
                    prev = tris;
                    if (l == 0)
                        b0 = mesh.bounds;
                    else
                        Assert.Less((mesh.bounds.size - b0.size).magnitude, b0.size.magnitude * 0.1f, name + " LOD" + l + ": 크기가 LOD0과 다름 (잎 · 꽃 자리가 같아야 함)");
                }
            }
        }

        [Test]
        public void CrittersUseMeshLod_PlantSourcesDoNot()
        {
            var critters = (ModelImporter)AssetImporter.GetAtPath("Assets/_Project/Art/Models/Interior/SM_InteriorCritters.fbx");
            Assert.IsTrue(critters.generateMeshLods, "동물 = Unity Mesh LOD");
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Models/Interior/SM_InteriorCritters.fbx"))
            {
                if (o is Mesh m)
                    Assert.GreaterOrEqual(m.lodCount, 3, m.name);
            }
            foreach (var guid in AssetDatabase.FindAssets("SM_Plant t:Model", new[] { PlantAtlas.OursFolder.TrimEnd('/') }))
            {
                var imp = (ModelImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                Assert.IsFalse(imp.generateMeshLods, imp.assetPath + ": 식물은 자동 Mesh LOD 금지 (작은 잎 · 꽃을 통째로 지움 — LOD 프리팹을 씀)");
            }
        }
    }
}
