using System.Collections.Generic;
using SpaceStation.Building;
using SpaceStation.Data;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 12-0 (U-5) 모듈 프리팹마다 모델 모양 그대로의 선택용 MeshCollider를 굽는다 (<see cref="ModulePick"/>).
    /// 메시가 있는 오브젝트마다 자식 "Pick"(ModulePick 레이어, MeshCollider)을 하나씩 둔다 — 회전하는 부품(포탑 머리 등)도 따라 돈다.
    /// 다시 실행하면 기존 Pick을 지우고 새로 만든다. 모듈 아트를 다시 조립하면(StationArtBuilder) 자동으로 다시 실행된다.
    /// </summary>
    public static class ModulePickSetup
    {
        [MenuItem("SpaceStation/Modules/Add Pick Colliders")]
        public static void ApplyAll()
        {
            int layer = ModulePick.Layer;
            if (layer < 0)
            {
                Debug.LogError($"[ModulePickSetup] '{ModulePick.LayerName}' 레이어가 없음 (Project Settings > Tags and Layers에 추가)");
                return;
            }
            var done = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:ModuleData", new[] { "Assets/_Project/Data/Modules" }))
            {
                var data = AssetDatabase.LoadAssetAtPath<ModuleData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null || data.Prefab == null)
                    continue;
                string path = AssetDatabase.GetAssetPath(data.Prefab);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    int count = Apply(root, layer);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    done.Add($"{data.name.Replace("MD_", "")} {count}");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[ModulePickSetup] 선택용 콜라이더: " + string.Join(", ", done));
        }

        /// <summary>기존 Pick을 지우고 메시마다 Pick을 만든다. 반환: 만든 수.</summary>
        public static int Apply(GameObject root, int layer)
        {
            var old = new List<GameObject>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == ModulePick.ChildName && t.GetComponent<MeshCollider>() != null)
                    old.Add(t.gameObject);
            }
            foreach (var go in old)
                Object.DestroyImmediate(go);

            int count = 0;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || filter.GetComponent<Renderer>() == null)
                    continue;
                var pick = new GameObject(ModulePick.ChildName) { layer = layer };
                pick.transform.SetParent(filter.transform, false);
                pick.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                count++;
            }
            return count;
        }
    }
}
