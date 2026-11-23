using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 식물 아틀라스 ④ (2026-11-16): 진화 내부 잎 조각 (<c>INT_{이름}_Leaves</c> — 충돌 없는 잎 · 꽃잎 · 덩굴)의 우리 색 재질 칸
    /// (Plant · PlantLight · Evo3Leaf2 · 꽃 색 …, <see cref="PlantAtlas.SwatchMaterials"/> — 재질 표에서 같은 재질로 가는 Evo3Leaf · Evo3Moss도)을
    /// 식물 아틀라스 색 칸 UV + 재질 <c>M_FoliageStill</c> 한 칸으로 합침: 양면 (열린 판 꽃잎 · 잎이 방향 정리로 뒤집혀 한쪽에서 안 보이던 것이 풀림) · 빛 비침,
    /// 재질 칸 열 개 남짓 → 하나. 흙 · 천 · 줄기 껍질 같은 다른 재질 칸은 그대로 둠. 살랑임은 끈 재질 (잎 조각 원점 = 방 원점이라 잎 전체가 같이 흔들려 줄기에서 떨어져 보임).
    /// 템플릿 빌드 (<see cref="InteriorTemplateBuilder"/>)가 조각마다 부르고, 바꾼 메시는 <c>Art/Models/Interior/Generated/SM_Interior_{이름}_Leaves.asset</c>.
    /// 모든 꼭짓점이 칸 가운데 한 UV라 밉맵이 늘 0단계 (이웃 색 칸이 번지지 않음).
    /// </summary>
    public static class InteriorFoliage
    {
        private const string Folder = "Assets/_Project/Art/Models/Interior/Generated/";

        /// <summary>재질 → 색 칸 가운데 UV (재질 표가 다른 이름을 같은 재질로 보내도 잡히게 재질로 찾음).</summary>
        public static Dictionary<Material, Vector2> SwatchByMaterial()
        {
            var map = new Dictionary<Material, Vector2>();
            foreach (var (n, path) in PlantAtlas.SwatchMaterials)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m != null && !map.ContainsKey(m) && PlantAtlas.SwatchUV(n, out var uv))
                    map[m] = uv;
            }
            return map;
        }

        /// <summary>잎 조각이면 색 재질 칸을 아틀라스 한 칸으로 바꿈 (바꿨으면 true).</summary>
        public static bool Convert(string name, GameObject part)
        {
            if (!part.name.EndsWith("_Leaves"))
                return false;
            var filter = part.GetComponent<MeshFilter>();
            var renderer = part.GetComponent<MeshRenderer>();
            var still = AssetDatabase.LoadAssetAtPath<Material>(PlantAtlas.StillMaterialPath);
            if (filter == null || renderer == null || filter.sharedMesh == null || still == null)
                return false;
            var swatch = SwatchByMaterial();
            var src = filter.sharedMesh;
            var mats = renderer.sharedMaterials;
            bool any = false;
            for (int s = 0; s < mats.Length && s < src.subMeshCount; s++)
                any |= mats[s] != null && swatch.ContainsKey(mats[s]);
            if (!any)
                return false;

            var v = src.vertices;
            var n = src.normals;
            var t = src.tangents;
            var c = src.colors;
            var uv = new List<Vector2>();
            src.GetUVs(0, uv);
            bool hasN = n.Length == v.Length, hasT = t.Length == v.Length, hasC = c.Length == v.Length, hasUV = uv.Count == v.Length;
            var nv = new List<Vector3>(v.Length);
            var nn = new List<Vector3>(v.Length);
            var nt = new List<Vector4>(v.Length);
            var nc = new List<Color>(v.Length);
            var nuv = new List<Vector2>(v.Length);
            var map = new Dictionary<long, int>();
            int Add(int i, int group, Vector2? at)
            {
                long key = (long)i * 256 + group;
                if (map.TryGetValue(key, out int k))
                    return k;
                k = nv.Count;
                nv.Add(v[i]);
                if (hasN)
                    nn.Add(n[i]);
                if (hasT)
                    nt.Add(t[i]);
                nc.Add(hasC ? c[i] : Color.white);   // 꼭짓점 색 없으면 흰색 (Foliage 꼭짓점 그늘 = R)
                nuv.Add(at ?? (hasUV ? uv[i] : Vector2.zero));
                map[key] = k;
                return k;
            }
            var subTris = new List<List<int>>();
            var subMats = new List<Material>();
            int foliage = -1;
            for (int s = 0; s < src.subMeshCount; s++)
            {
                var m = s < mats.Length ? mats[s] : null;
                int target, group;
                Vector2? at = null;
                if (m != null && swatch.TryGetValue(m, out var sw))
                {
                    if (foliage < 0)
                    {
                        foliage = subTris.Count;
                        subTris.Add(new List<int>());
                        subMats.Add(still);
                    }
                    target = foliage;
                    group = 1 + s;
                    at = sw;
                }
                else
                {
                    target = subTris.Count;
                    subTris.Add(new List<int>());
                    subMats.Add(m);
                    group = 0;
                }
                foreach (int i in src.GetTriangles(s))
                    subTris[target].Add(Add(i, group, at));
            }

            string path = Folder + "SM_Interior_" + name + "_Leaves.asset";
            var mesh = new Mesh { name = "SM_Interior_" + name + "_Leaves" };   // 파일 이름과 같게 (빌드마다 이름 변경이 생기지 않게)
            mesh.indexFormat = nv.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(nv);
            if (hasN)
                mesh.SetNormals(nn);
            mesh.SetColors(nc);
            mesh.SetUVs(0, nuv);
            mesh.subMeshCount = subTris.Count;
            for (int s = 0; s < subTris.Count; s++)
                mesh.SetTriangles(subTris[s], s, false);
            mesh.RecalculateBounds();
            if (!hasN)
                mesh.RecalculateNormals();
            if (hasT)
                mesh.SetTangents(nt);
            else
                mesh.RecalculateTangents();

            System.IO.Directory.CreateDirectory(Folder);
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                Object.DestroyImmediate(mesh);
                mesh = existing;
            }
            else
                AssetDatabase.CreateAsset(mesh, path);
            filter.sharedMesh = mesh;
            renderer.sharedMaterials = subMats.ToArray();
            Debug.Log($"[InteriorFoliage] {name}: 잎 조각 재질 칸 {mats.Length} → {subMats.Count} (아틀라스 색 칸 {subTris[foliage].Count / 3} 삼각형)");
            return true;
        }
    }
}
