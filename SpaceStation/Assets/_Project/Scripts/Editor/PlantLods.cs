using UnityEditor;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// ⑤-4 거리별 단순화 (2026-11-16): 우리 식물 모델 (plant_builder — 화분 식물 · 나무 · 소나무 · 꽃나무와 시든 것 여덟)을 LODGroup 프리팹으로.
    /// LOD0 = 원래 <c>SM_{이름}.fbx</c>, LOD1 · LOD2 = <c>SM_{이름}Lod1 · Lod2.fbx</c> (<c>plant_builder.py --lod 1 · 2</c> — 같은 잎 · 꽃 · 가지 자리에 해상도만 낮춤, 꽃나무 1.77만 → 0.48만 → 0.25만).
    /// Unity 자동 Mesh LOD는 작은 잎 · 꽃 덩어리를 통째로 지워 (꽃나무 LOD1 954 · 화분 LOD2 0) 쓰지 않음.
    /// 시듦은 지금처럼 시든 프리팹으로 바꿔 끼움 (<see cref="Interior.InteriorPlants"/>), 창밖 화면 복사본은 LOD0만 (InteriorExteriorView).
    /// </summary>
    public static class PlantLods
    {
        public const string Folder = "Assets/_Project/Prefabs/Interior/Plants";

        public static readonly string[] Names =
            { "PlantPot", "PlantPotWilted", "PlantTree", "PlantTreeWilted", "PlantPine", "PlantPineWilted", "PlantBlossom", "PlantBlossomWilted" };

        /// <summary>
        /// 화면 높이 비율 (LODGroup): LOD1 = 원래와 거의 구별 안 됨 → 일찍 (나무 약 7 m · 화분 약 4.5 m 너머, 시야 60°),
        /// LOD2 = 평평한 잎 → 나무 약 24 m 너머, 끝까지 그림 (잘라내지 않음 — 먼 정원 숲이 사라지지 않게).
        /// </summary>
        public static readonly float[] Heights = { 0.25f, 0.07f, 0f };

        public static string PrefabPath(string name) => Folder + "/PF_" + name + ".prefab";

        public static GameObject Prefab(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(name));

        [MenuItem("SpaceStation/Interior/Build Plant LOD Prefabs")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(Folder).Replace('\\', '/'), System.IO.Path.GetFileName(Folder));
            foreach (var name in Names)
            {
                var root = new GameObject(name);
                try
                {
                    var lods = new LOD[Heights.Length];
                    for (int l = 0; l < Heights.Length; l++)
                    {
                        string file = PlantAtlas.OursFolder + "SM_" + name + (l == 0 ? "" : "Lod" + l) + ".fbx";   // 밑줄 없이 (_LOD1이면 Unity가 LOD 그룹 규칙으로 메시를 버림)
                        var model = AssetDatabase.LoadAssetAtPath<GameObject>(file);
                        var src = model != null ? model.GetComponentInChildren<MeshRenderer>(true) : null;
                        if (src == null)
                        {
                            Debug.LogError("[PlantLods] 모델 없음: " + file);
                            return;
                        }
                        var go = new GameObject("LOD" + l);
                        go.transform.SetParent(root.transform, false);
                        if (src.transform != model.transform)
                        {
                            go.transform.localPosition = src.transform.localPosition;
                            go.transform.localRotation = src.transform.localRotation;
                            go.transform.localScale = src.transform.localScale;
                        }
                        go.AddComponent<MeshFilter>().sharedMesh = src.GetComponent<MeshFilter>().sharedMesh;
                        var r = go.AddComponent<MeshRenderer>();
                        r.sharedMaterials = src.sharedMaterials;
                        lods[l] = new LOD(Heights[l], new Renderer[] { r });
                    }
                    var group = root.AddComponent<LODGroup>();
                    group.SetLODs(lods);
                    group.RecalculateBounds();
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(name));
                }
                finally
                {
                    Object.DestroyImmediate(root);
                }
            }
            Debug.Log("[PlantLods] 식물 LOD 프리팹 " + Names.Length);
        }
    }
}
