using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 식물 메시 가져오기 (`Art/Plants/Meshes` — URP 3D Sample 원본 FBX 그대로): UV를 식물 아틀라스 칸으로 옮기고 (FBX 재질 이름 → `PlantAtlas.ToAtlas`),
    /// 재질 칸 (잎 · 줄기)을 하나로 합쳐 `M_Foliage` 하나를 씀 (식물 하나 = 그리기 한 번). 거리별 단순화 (_LOD0 ~ 2)는 FBX 이름 그대로 Unity가 LODGroup을 만듦.
    /// </summary>
    public sealed class PlantMeshImporter : AssetPostprocessor
    {
        private bool IsPlant => assetPath.Replace('\\', '/').StartsWith(PlantAtlas.MeshFolder);

        /// <summary>칸 표 (`PlantAtlas.ToAtlas`)를 바꾸면 올림 — 식물 메시를 모두 다시 가져옴.</summary>
        public override uint GetVersion() => 2;

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
    }
}
