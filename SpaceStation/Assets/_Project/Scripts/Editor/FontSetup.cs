using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 배포 준비: 게임 글꼴을 메이플스토리체(넥슨, Light + Bold)로 교체 (메뉴 SpaceStation/Art/Apply Game Font).
    /// - Art/Fonts/Maplestory SDF(Light, 동적) + Maplestory Bold SDF를 만들고 굵게(&lt;b&gt;)는 Bold 글꼴로 그린다
    /// - 두 씬·UI 프리팹의 모든 직렬화 참조(TMP 텍스트의 글꼴·재질, 런타임 창의 _font 필드)와 TMP 기본 글꼴을 바꾼다
    /// 이전 글꼴(Malgun SDF / malgun.ttf, Windows 시스템 글꼴이라 배포 불가)은 참조가 사라진 뒤 지운다.
    /// </summary>
    public static class FontSetup
    {
        private const string Folder = "Assets/_Project/Art/Fonts/";
        private const string LightPath = Folder + "Maplestory SDF.asset";
        private const string BoldPath = Folder + "Maplestory Bold SDF.asset";
        private static readonly string[] Scenes = { "Assets/_Project/Scenes/Main.unity", "Assets/_Project/Scenes/MainMenu.unity" };
        private static readonly string[] Prefabs = { "Assets/_Project/Prefabs/UI/PF_BuildButton.prefab", "Assets/_Project/Prefabs/UI/PF_BuildTab.prefab" };

        [MenuItem("SpaceStation/Art/Apply Game Font")]
        public static void Run()
        {
            var light = Ensure(LightPath, Folder + "Maplestory-Light.ttf");
            var bold = Ensure(BoldPath, Folder + "Maplestory-Bold.ttf");
            var so = new SerializedObject(light);
            var table = so.FindProperty("m_FontWeightTable");
            if (table != null && table.arraySize > 7)
            {
                table.GetArrayElementAtIndex(7).FindPropertyRelative("regularTypeface").objectReferenceValue = bold; // Bold(700)
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorUtility.SetDirty(light);
            AssetDatabase.SaveAssets();

            var old = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Folder + "Malgun SDF.asset");
            int replaced = 0;
            if (old != null)
            {
                var map = new Dictionary<Object, Object> { { old, light }, { old.material, light.material } };
                foreach (var path in Prefabs)
                {
                    var root = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        replaced += Replace(root.GetComponentsInChildren<Component>(true), map);
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(root);
                    }
                }
                foreach (var path in Scenes)
                {
                    var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    var all = new List<Component>();
                    foreach (var go in scene.GetRootGameObjects())
                        all.AddRange(go.GetComponentsInChildren<Component>(true));
                    replaced += Replace(all, map);
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
                EditorSceneManager.OpenScene(Scenes[0], OpenSceneMode.Single);
            }

            var settings = Resources.Load<TMP_Settings>("TMP Settings");
            if (settings != null)
            {
                var sso = new SerializedObject(settings);
                sso.FindProperty("m_defaultFontAsset").objectReferenceValue = light;
                sso.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(settings);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[FontSetup] 메이플스토리체 적용: 참조 {replaced}개 교체");
        }

        /// <summary>동적 SDF 글꼴 에셋 (아틀라스·재질을 하위 에셋으로 저장). 이미 있으면 그대로.</summary>
        private static TMP_FontAsset Ensure(string assetPath, string fontPath)
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing != null)
                return existing;
            var font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
            var fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
            fa.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            AssetDatabase.CreateAsset(fa, assetPath);
            fa.atlasTexture.name = fa.name + " Atlas";
            AssetDatabase.AddObjectToAsset(fa.atlasTexture, fa);
            fa.material.name = fa.name + " Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
            return fa;
        }

        private static int Replace(IEnumerable<Component> components, Dictionary<Object, Object> map)
        {
            int count = 0;
            foreach (var c in components)
            {
                if (c == null)
                    continue;
                var so = new SerializedObject(c);
                var it = so.GetIterator();
                bool changed = false;
                while (it.Next(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null)
                        continue;
                    if (map.TryGetValue(it.objectReferenceValue, out var to))
                    {
                        it.objectReferenceValue = to;
                        changed = true;
                        count++;
                    }
                }
                if (changed)
                    so.ApplyModifiedPropertiesWithoutUndo();
            }
            return count;
        }
    }
}
