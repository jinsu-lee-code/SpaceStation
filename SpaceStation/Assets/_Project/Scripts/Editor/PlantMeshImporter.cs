using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 식물 메시 가져오기 (`Art/Plants/Meshes` — URP 3D Sample 원본 FBX 그대로): UV를 식물 아틀라스 칸으로 옮기고 (FBX 재질 이름 → `PlantAtlas.ToAtlas`),
    /// 재질 칸 (잎 · 줄기)을 하나로 합쳐 `M_Foliage` 하나를 씀 (식물 하나 = 그리기 한 번). 거리별 단순화 (_LOD0 ~ 2)는 FBX 이름 그대로 Unity가 LODGroup을 만듦.
    /// 우리 식물 모델 (`Art/Models/Interior/Plants/SM_Plant*` — UV 없는 색 재질 기하학, ④ 2026-11-16)은 재질 "M_{이름}"마다 그 색 칸 가운데 UV
    /// (`PlantAtlas.SwatchUV`, 재질 경계에서 함께 쓰던 꼭짓점은 칸마다 나눔) · 흰 꼭짓점 색 (Foliage 꼭짓점 그늘 = R)으로 바꿔 같은 `M_Foliage` 하나.
    /// </summary>
    public sealed class PlantMeshImporter : AssetPostprocessor
    {
        private bool IsPlant => assetPath.Replace('\\', '/').StartsWith(PlantAtlas.MeshFolder);

        private bool IsOurs
        {
            get
            {
                string p = assetPath.Replace('\\', '/');
                return p.StartsWith(PlantAtlas.OursFolder) && System.IO.Path.GetFileName(p).StartsWith("SM_Plant") && p.EndsWith(".fbx");
            }
        }

        /// <summary>칸 표 (`PlantAtlas.ToAtlas` · 색 칸)를 바꾸면 올림 — 식물 메시를 모두 다시 가져옴.</summary>
        public override uint GetVersion() => 4;

        private void OnPreprocessModel()
        {
            if (!IsPlant)
                return;
            var mi = (ModelImporter)assetImporter;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;   // 재질 이름 (칸 고르기)이 필요해서 — 쓰지는 않음
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.importAnimation = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.addCollider = false;
            mi.isReadable = false;
        }

        private void OnPostprocessModel(GameObject root)
        {
            if (IsOurs)
            {
                ToSwatches(root);
                return;
            }
            if (!IsPlant)
                return;
            context.DependsOnSourceAsset(PlantAtlas.MaterialPath);      // 재질이 생기거나 바뀌면 다시 가져옴 (빠지면 "inconsistent result" 경고 — 재질 유무에 따라 결과가 달랐음)
            var foliage = AssetDatabase.LoadAssetAtPath<Material>(PlantAtlas.MaterialPath);
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null)
                    continue;
                var mats = r.sharedMaterials;
                var uv = new List<Vector2>();
                mesh.GetUVs(0, uv);
                var done = new bool[uv.Count];
                var tris = new List<int>();
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    string name = s < mats.Length && mats[s] != null ? mats[s].name : "";
                    var idx = mesh.GetTriangles(s);
                    foreach (int v in idx)
                    {
                        if (done[v])
                            continue;
                        uv[v] = PlantAtlas.ToAtlas(name, uv[v]);
                        done[v] = true;
                    }
                    tris.AddRange(idx);
                }
                mesh.SetUVs(0, uv);
                mesh.subMeshCount = 1;
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                if (foliage != null)
                    r.sharedMaterials = new[] { foliage };
            }
        }

        /// <summary>우리 식물 모델: 재질 칸마다 색 칸 UV · 흰 꼭짓점 색 → 재질 칸 하나 (`M_Foliage`). 모르는 재질이 있으면 그 렌더러는 그대로 두고 알림.</summary>
        private void ToSwatches(GameObject root)
        {
            context.DependsOnSourceAsset(PlantAtlas.MaterialPath);
            var foliage = AssetDatabase.LoadAssetAtPath<Material>(PlantAtlas.MaterialPath);
            if (foliage == null)
                return;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null)
                    continue;
                var mats = r.sharedMaterials;
                var at = new Vector2[mesh.subMeshCount];
                bool ok = true;
                for (int s = 0; s < mesh.subMeshCount && ok; s++)
                {
                    string name = s < mats.Length && mats[s] != null ? mats[s].name : "";
                    if (name.StartsWith("M_"))
                        name = name.Substring(2);
                    if (!PlantAtlas.SwatchUV(name, out at[s]))
                    {
                        Debug.LogWarning($"[PlantMeshImporter] {assetPath}: 재질 {name}의 색 칸이 없음 (PlantAtlas.SwatchMaterials) — 그대로 둠");
                        ok = false;
                    }
                }
                if (!ok)
                    continue;
                var v = mesh.vertices;
                var n = mesh.normals;
                bool hasN = n.Length == v.Length;
                var nv = new List<Vector3>(v.Length);
                var nn = new List<Vector3>(v.Length);
                var nuv = new List<Vector2>(v.Length);
                var map = new Dictionary<long, int>();
                var tris = new List<int>();
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    foreach (int i in mesh.GetTriangles(s))
                    {
                        long key = (long)i * 256 + s;
                        if (!map.TryGetValue(key, out int k))
                        {
                            k = nv.Count;
                            nv.Add(v[i]);
                            if (hasN)
                                nn.Add(n[i]);
                            nuv.Add(at[s]);
                            map[key] = k;
                        }
                        tris.Add(k);
                    }
                }
                // Clear() 하면 가져오는 중인데도 메시가 "읽기 금지"가 되어 삼각형을 못 넣음 → 그대로 덮어씀 (꼭짓점 수는 늘기만 함)
                if (nv.Count > 65535)
                    mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(nv);
                if (hasN)
                    mesh.SetNormals(nn);
                var white = new Color[nv.Count];
                for (int i = 0; i < white.Length; i++)
                    white[i] = Color.white;
                mesh.SetColors(white);
                mesh.SetUVs(0, nuv);
                mesh.subMeshCount = 1;
                mesh.SetTriangles(tris, 0);
                if (!hasN)
                    mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                r.sharedMaterials = new[] { foliage };
            }
        }
    }
}
