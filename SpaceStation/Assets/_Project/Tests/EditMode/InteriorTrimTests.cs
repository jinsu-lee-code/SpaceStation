using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Editor;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Tests
{
    /// <summary>
    /// ⑤ 트림 (2026-11-16): 트림 재질 열둘 (4단계 성운 넷 2026-11-17) = 셰이더 InteriorTrim · 장식 종류, Blender 트림 내보내기로 다시 낸 방 (아콜로지 · AI 중추 …)은
    /// 트림 재질 면의 꼭짓점마다 16비트 트림 값 (선 거리 + 1 ≥ 0.98, 면 폭 > 0)이 있음, 묶기 (InteriorTrimImporter.Pack)는 위치 · 법선 · 삼각형을 그대로 둠.
    /// </summary>
    public class InteriorTrimTests
    {
        private static readonly (string Path, int Kind)[] Materials =
        {
            (InteriorSetup.Evo2GoldPath, InteriorSetup.TrimGold), (InteriorSetup.Evo3GoldPath, InteriorSetup.TrimGoldInlay),
            (InteriorSetup.Evo3PearlPath, InteriorSetup.TrimPearl), (InteriorSetup.Evo3NacrePath, InteriorSetup.TrimPearl),
            (InteriorSetup.EvoWhitePath, InteriorSetup.TrimPanel), (InteriorSetup.Evo2TrimPath, InteriorSetup.TrimMetal),
            (InteriorSetup.Evo2WallPath, InteriorSetup.TrimWall), (InteriorSetup.EvoDarkPath, InteriorSetup.TrimDarkGloss),
            // 4단계 성운 (2026-11-17 심우주 통신기지 · SM_InteriorKit_Evo4)
            (InteriorSetup.Evo4WallPath, InteriorSetup.TrimNebula), (InteriorSetup.Evo4DeckPath, InteriorSetup.TrimWall),
            (InteriorSetup.Evo4TrimPath, InteriorSetup.TrimMetal), (InteriorSetup.Evo4LowPath, InteriorSetup.TrimDarkGloss),
        };

        /// <summary>
        /// 트림 UV로 다시 내보낸 방 = 진화 내부 전부 (PF_Interior_Evo*, ⑤-2 2026-11-16 — BlenderWork/Tools/trim_uv_export.py).
        /// 새 진화 내부도 내보낼 때 트림 도구를 거쳐야 이 검사를 통과함.
        /// </summary>
        private static IEnumerable<string> Reexported()
        {
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("PF_Interior_Evo t:Prefab", new[] { "Assets/_Project/Prefabs/Interior" }))
            {
                var file = System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid));
                if (!file.StartsWith("PF_Interior_Evo"))
                    continue;
                n++;
                yield return file.Substring("PF_Interior_".Length);
            }
            Assert.GreaterOrEqual(n, 47, "진화 내부 프리팹 수");
        }

        [Test]
        public void TrimMaterials_UseTrimShader()
        {
            foreach (var (path, kind) in Materials)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                Assert.IsNotNull(m, path);
                Assert.AreEqual(InteriorSetup.TrimShaderName, m.shader.name, path);
                Assert.AreEqual(kind, Mathf.RoundToInt(m.GetFloat("_TrimKind")), path);
                Assert.IsFalse(ShaderUtil.ShaderHasError(m.shader), "트림 셰이더 오류");
            }
        }

        [Test]
        public void ReexportedRooms_HaveTrimValues()
        {
            var trim = new HashSet<Material>();
            foreach (var (path, _) in Materials)
                trim.Add(AssetDatabase.LoadAssetAtPath<Material>(path));
            foreach (var name in Reexported())
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Interior/PF_Interior_" + name + ".prefab");
                Assert.IsNotNull(prefab, name);
                int checkedVerts = 0, lined = 0;
                foreach (var r in prefab.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null || r.name.EndsWith("_Leaves"))
                        continue;
                    var mesh = mf.sharedMesh;
                    if (!AssetDatabase.GetAssetPath(mesh).StartsWith("Assets/_Project/Art/Models/Interior/"))
                        continue;   // 내부 FBX 메시만 (Unity 기본 원기둥 등은 UV1이 라이트맵 UV — 셰이더는 면 폭 0이면 트림으로 안 읽음)
                    var mats = r.sharedMaterials;
                    bool any = false;
                    for (int s = 0; s < mats.Length && s < mesh.subMeshCount; s++)
                        any |= trim.Contains(mats[s]);
                    if (!any)
                        continue;
                    if (!mesh.HasVertexAttribute(VertexAttribute.TexCoord1))
                        continue;   // 트림 면이 없는 메시 (칠 색만 트림 재질) — 방 전체에 값이 있는지는 아래 checkedVerts가 봄
                    Assert.AreEqual(VertexAttributeFormat.Float16, mesh.GetVertexAttributeFormat(VertexAttribute.TexCoord1), $"{r.name}: 16비트로 안 묶임 (InteriorTrimImporter)");
                    Assert.AreEqual(4, mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord2), $"{r.name}: UV2 = 선 2 · 3 · 면 폭");
                    using (var data = Mesh.AcquireReadOnlyMeshData(mesh))
                    {
                        var d = data[0];
                        var a = new NativeArray<Vector2>(d.vertexCount, Allocator.Temp);
                        var b = new NativeArray<Vector4>(d.vertexCount, Allocator.Temp);
                        d.GetUVs(1, a);
                        d.GetUVs(2, b);
                        var idx = new List<int>();
                        for (int s = 0; s < mats.Length && s < mesh.subMeshCount; s++)
                        {
                            if (!trim.Contains(mats[s]))
                                continue;
                            idx.Clear();
                            var ia = new NativeArray<int>(d.GetSubMesh(s).indexCount, Allocator.Temp);
                            d.GetIndices(ia, s);
                            foreach (int i in ia)
                            {
                                float m4 = Mathf.Min(Mathf.Min(a[i].x, a[i].y), Mathf.Min(b[i].x, b[i].y));
                                // 0 = 값 없음 (Blender 재질이 트림이 아닌데 템플릿이 트림 재질을 준 면 — 방마다 칠 색 "Paint"가 흰 몸체인 방): 장식 없이 그림
                                if (m4 == 0f && b[i].z == 0f)
                                    continue;
                                Assert.GreaterOrEqual(m4, 0.97f, $"{name}/{r.name} ({mats[s].name}) 꼭짓점 {i}: 트림 값이 음수 · 반쪽 ({a[i]}, {b[i]})");
                                Assert.Greater(b[i].z, 0f, $"{name}/{r.name} 꼭짓점 {i}: 면 폭 없음");
                                checkedVerts++;
                                if (m4 < 1.1f)
                                    lined++;
                            }
                            ia.Dispose();
                        }
                        a.Dispose();
                        b.Dispose();
                    }
                }
                Assert.Greater(checkedVerts, 300, $"{name}: 트림 재질 꼭짓점이 너무 적음");
                Assert.Greater(lined, checkedVerts / 20, $"{name}: 테두리 가까운 꼭짓점이 너무 적음 (트림 값이 다 '선 없음')");
            }
        }

        [Test]
        public void Pack_KeepsGeometry()
        {
            var m = new Mesh();
            var v = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0) };
            m.SetVertices(v);
            m.SetNormals(new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            m.SetUVs(0, new List<Vector2> { Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero });
            m.SetUVs(1, new List<Vector2> { new Vector2(1f, 2f), new Vector2(1.5f, 11f), new Vector2(1.016f, 3f), new Vector2(2f, 2f) });
            m.SetUVs(2, new List<Vector2> { new Vector2(11f, 11f), new Vector2(11f, 11f), new Vector2(1.2f, 11f), new Vector2(11f, 11f) });
            m.SetUVs(3, new List<Vector2> { new Vector2(0.05f, 0f), new Vector2(0.05f, 0f), new Vector2(10f, 0f), new Vector2(10f, 0f) });
            m.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            try
            {
                Assert.IsTrue(InteriorTrimImporter.Pack(m));
                Assert.IsFalse(m.HasVertexAttribute(VertexAttribute.TexCoord0), "빈 UV0는 뺌");
                Assert.IsFalse(m.HasVertexAttribute(VertexAttribute.TexCoord3), "UV3는 UV2에 묶임");
                Assert.AreEqual(VertexAttributeFormat.Float16, m.GetVertexAttributeFormat(VertexAttribute.TexCoord2));
                CollectionAssert.AreEqual(v, m.vertices);
                CollectionAssert.AreEqual(new[] { 0, 2, 1, 1, 2, 3 }, m.triangles);
                var a = new List<Vector2>();
                var b = new List<Vector4>();
                m.GetUVs(1, a);
                m.GetUVs(2, b);
                Assert.AreEqual(1.016f, a[2].x, 0.001f, "16비트 = 1 m 근처 1 mm 안");
                Assert.AreEqual(1.2f, b[2].x, 0.001f);
                Assert.AreEqual(0.05f, b[0].z, 0.0005f, "면 폭");
            }
            finally
            {
                Object.DestroyImmediate(m);
            }
        }
    }
}
