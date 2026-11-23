using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Data;
using SpaceStation.Editor;
using SpaceStation.Interior;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>
    /// 식물 아틀라스 ③ 화분 식물 (2026-11-16): 아틀라스 화분 식물이 모두 화분 테 · 둘레 안 (템플릿에 놓고 광선으로 다시 잼) · 재질 M_Foliage 하나 ·
    /// 우리 화분 식물 크기 안 · 셰이더 시듦 (MPB _Wilt — 멀쩡하면 블록 없음) · 셰이더에 처짐.
    /// </summary>
    public class PlantVariantTests
    {
        [Test]
        public void AtlasPotPlants_FitTheirPots()
        {
            var ours = InteriorPlantVariants.Vertices(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Models/Interior/Plants/SM_PlantPot.fbx"));
            int atlas = 0;
            var kinds = new HashSet<PlantKind>();
            foreach (var guid in AssetDatabase.FindAssets("t:InteriorTemplate"))
            {
                var t = AssetDatabase.LoadAssetAtPath<InteriorTemplate>(AssetDatabase.GUIDToAssetPath(guid));
                if (t.Prefab == null)
                    continue;
                GameObject go = null;
                try
                {
                    for (int i = 0; i < t.PlantSpots.Count; i++)
                    {
                        var s = t.PlantSpots[i];
                        int v = System.Array.FindIndex(InteriorPlantVariants.Pot, p => p.Kind == s.Kind);
                        if (v < 0)
                            continue;
                        if (go == null)
                        {
                            go = (GameObject)PrefabUtility.InstantiatePrefab(t.Prefab);
                            go.transform.position = new Vector3(0f, -500f, 0f);
                            Physics.SyncTransforms();
                        }
                        var verts = InteriorPlantVariants.Vertices(InteriorPlantVariants.Model(InteriorPlantVariants.Pot[v].Mesh));
                        var clear = InteriorPlantVariants.Clearance(go.transform.TransformPoint(s.Position));
                        Assert.IsTrue(InteriorPlantVariants.Fits(verts, InteriorPlantVariants.Pot[v].Scale, s.Scale, clear, ours, out string why), $"{t.name} 자리 {i} {s.Kind}: {why}");
                        atlas++;
                        kinds.Add(s.Kind);
                    }
                }
                finally
                {
                    if (go != null)
                        Object.DestroyImmediate(go);
                }
            }
            Assert.Greater(atlas, 20, "아틀라스 화분 식물이 너무 적음 (Build Templates · Set Plant Spots 먼저)");
            Assert.GreaterOrEqual(kinds.Count, 3, "섞인 종류가 셋 미만");
        }

        [Test]
        public void AtlasPotPlants_OnFoliageWithinOurPot()
        {
            var foliage = AssetDatabase.LoadAssetAtPath<Material>(PlantAtlas.MaterialPath);
            foreach (var (kind, mesh, scale, _) in InteriorPlantVariants.Pot)
            {
                var model = InteriorPlantVariants.Model(mesh);
                Assert.IsNotNull(model, mesh);
                Assert.IsNotNull(model.GetComponent<LODGroup>(), $"{mesh}: 거리별 단순화 없음");
                foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
                    Assert.AreEqual(foliage, r.sharedMaterial, $"{mesh}/{r.name}");
                float rmax = 0f, ymax = 0f;
                foreach (var p in InteriorPlantVariants.Vertices(model))
                {
                    rmax = Mathf.Max(rmax, new Vector2(p.x, p.z).magnitude * scale);
                    ymax = Mathf.Max(ymax, p.y * scale);
                }
                Assert.LessOrEqual(rmax, 0.63f, $"{kind}: 반지름 (우리 화분 식물 0.66 안)");
                Assert.LessOrEqual(ymax, 0.96f, $"{kind}: 키 (우리 화분 식물 0.94)");
            }
        }

        [Test]
        public void ShaderWilt_SetsAndClearsBlock()
        {
            var go = Object.Instantiate(InteriorPlantVariants.Model(InteriorPlantVariants.Pot[0].Mesh));
            try
            {
                var plant = new InteriorPlants.Plant { Healthy = go, ShaderWilt = true };
                var block = new MaterialPropertyBlock();
                plant.SetWilted(true);
                Assert.IsTrue(plant.IsWilted);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    Assert.IsTrue(r.HasPropertyBlock(), $"{r.name}: 시든 블록 없음");
                    r.GetPropertyBlock(block);
                    Assert.AreEqual(1f, block.GetFloat("_Wilt"), 1e-4f, r.name);
                    Assert.IsTrue(r.gameObject.activeSelf, "모델 하나 — 숨기지 않음");
                }
                plant.SetWilted(false);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    Assert.IsFalse(r.HasPropertyBlock(), $"{r.name}: 멀쩡한데 블록이 남음 (SRP Batcher 깨짐)");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void FoliageShader_HasWiltDroop()
        {
            var shader = Shader.Find(PlantAtlas.ShaderName);
            Assert.IsNotNull(shader);
            Assert.GreaterOrEqual(shader.FindPropertyIndex("_WiltDroop"), 0, "시듦 처짐 속성 없음");
            Assert.GreaterOrEqual(shader.FindPropertyIndex("_Wilt"), 0);
        }
    }
}
