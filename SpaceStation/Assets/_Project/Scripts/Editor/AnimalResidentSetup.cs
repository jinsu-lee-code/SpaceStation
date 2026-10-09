using System.Collections.Generic;
using SpaceStation.Data;
using SpaceStation.Interior;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 11-11d 동물 주민 연결 (메뉴 SpaceStation/Interior/Wire Animal Residents).
    /// Blender `BlenderWork/Animal/animal_builder.py`가 내보낸 `SM_Animal_<종류>.fbx` 8개를 <see cref="AnimalModelSet"/>에 묶고,
    /// 재질 슬롯마다 공유 URP Lit 재질을 만들어 InteriorMode에 연결한다 (Main 씬 저장).
    /// 종류별 색 = Blender `SPECIES` 표와 같은 값 + 털색 후보 (주민마다 하나).
    /// </summary>
    public static class AnimalResidentSetup
    {
        private const string GameScene = "Assets/_Project/Scenes/Main.unity";
        private const string ModelDir = "Assets/_Project/Art/Models/Resident/Animal/";
        private const string MatDir = "Assets/_Project/Art/Materials/Interior/Animal/";
        private const string SetPath = "Assets/_Project/Data/Residents/AnimalModelSet.asset";

        private static Color C(float r, float g, float b) => new Color(r, g, b);

        private static AnimalSpecies S(string id, string name, Color[] fur, Color light, Color accent, Color dark, Color? beak = null) => new AnimalSpecies
        {
            Id = id, DisplayName = name, FurColors = fur, Light = light, Accent = accent, Dark = dark, Beak = beak ?? C(1f, 0.64f, 0.18f),
            Model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelDir + "SM_Animal_" + char.ToUpperInvariant(id[0]) + id.Substring(1) + ".fbx"),
        };

        private static AnimalSpecies[] Species()
        {
            var pink = C(0.95f, 0.62f, 0.66f);
            return new[]
            {
                S("rabbit", "토끼", new[] { C(0.93f, 0.90f, 0.86f), C(0.95f, 0.85f, 0.70f), C(0.62f, 0.45f, 0.32f), C(0.62f, 0.62f, 0.65f), C(0.42f, 0.30f, 0.24f) },
                    C(1f, 0.99f, 0.97f), pink, C(0.25f, 0.2f, 0.2f)),
                S("cat", "고양이", new[] { C(0.96f, 0.72f, 0.42f), C(0.60f, 0.60f, 0.64f), C(0.97f, 0.90f, 0.78f), C(0.26f, 0.25f, 0.27f), C(0.72f, 0.56f, 0.40f) },
                    C(1f, 0.96f, 0.9f), C(0.95f, 0.6f, 0.62f), C(0.78f, 0.48f, 0.25f)),
                S("dog", "강아지", new[] { C(0.88f, 0.72f, 0.50f), C(0.97f, 0.95f, 0.92f), C(0.60f, 0.42f, 0.28f), C(0.24f, 0.22f, 0.22f) },
                    C(1f, 0.97f, 0.92f), C(0.93f, 0.6f, 0.6f), C(0.5f, 0.33f, 0.22f)),
                S("bear", "곰", new[] { C(0.56f, 0.39f, 0.26f), C(0.38f, 0.26f, 0.18f), C(0.78f, 0.58f, 0.36f) },
                    C(0.88f, 0.75f, 0.58f), C(0.92f, 0.58f, 0.55f), C(0.16f, 0.11f, 0.09f)),
                S("fox", "여우", new[] { C(0.95f, 0.52f, 0.20f), C(0.85f, 0.38f, 0.18f), C(0.94f, 0.94f, 0.96f) },
                    C(1f, 0.98f, 0.95f), C(0.93f, 0.58f, 0.55f), C(0.2f, 0.14f, 0.12f)),
                S("panda", "판다", new[] { C(0.97f, 0.97f, 0.95f) },
                    C(1f, 1f, 1f), pink, C(0.13f, 0.13f, 0.14f)),
                S("koala", "코알라", new[] { C(0.63f, 0.64f, 0.68f), C(0.74f, 0.74f, 0.77f), C(0.52f, 0.50f, 0.52f) },
                    C(0.96f, 0.95f, 0.95f), C(0.93f, 0.62f, 0.66f), C(0.18f, 0.18f, 0.2f)),
                S("penguin", "펭귄", new[] { C(0.17f, 0.20f, 0.28f), C(0.12f, 0.12f, 0.14f), C(0.30f, 0.36f, 0.46f) },
                    C(1f, 1f, 0.99f), pink, C(0.1f, 0.1f, 0.12f), C(1f, 0.64f, 0.18f)),
            };
        }

        [MenuItem("SpaceStation/Interior/Wire Animal Residents")]
        public static void Wire()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[AnimalResidentSetup] 플레이 모드에서는 실행하지 않음");
                return;
            }
            foreach (var guid in AssetDatabase.FindAssets("SM_Animal_ t:Model", new[] { ModelDir.TrimEnd('/') }))
            {
                var imp = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as ModelImporter;
                if (imp == null || (!imp.importAnimation && !imp.importCameras && !imp.importLights))
                    continue;
                imp.importAnimation = false;
                imp.importCameras = false;
                imp.importLights = false;
                imp.SaveAndReimport();
            }

            var species = Species();
            foreach (var s in species)
                if (s.Model == null)
                    Debug.LogError("[AnimalResidentSetup] 모델 없음: " + s.Id);

            var set = AssetDatabase.LoadAssetAtPath<AnimalModelSet>(SetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<AnimalModelSet>();
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SetPath));
                AssetDatabase.CreateAsset(set, SetPath);
            }
            set.EditorSet(species, Materials());
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != GameScene)
                scene = EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
            var mode = Object.FindFirstObjectByType<InteriorMode>(FindObjectsInactive.Include);
            if (mode == null)
            {
                Debug.LogError("[AnimalResidentSetup] InteriorMode 없음");
                return;
            }
            var so = new SerializedObject(mode);
            so.FindProperty("_animalSet").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AnimalModelSet>(SetPath);
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[AnimalResidentSetup] 동물 {species.Length}종 연결");
        }

        /// <summary>슬롯마다 공유 재질 (색은 주민마다 MaterialPropertyBlock — 눈만 재질 색).</summary>
        private static Material[] Materials()
        {
            System.IO.Directory.CreateDirectory(MatDir);
            var list = new List<Material>();
            foreach (var slot in new[] { AnimalModelSet.Fur, AnimalModelSet.FurLight, AnimalModelSet.Pink, AnimalModelSet.Eye, AnimalModelSet.EyeHi, AnimalModelSet.Dark, AnimalModelSet.Beak })
            {
                string path = MatDir + "M_Animal_" + slot + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null)
                {
                    m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    AssetDatabase.CreateAsset(m, path);
                }
                bool eye = slot == AnimalModelSet.Eye;
                m.SetColor("_BaseColor", eye ? C(0.05f, 0.04f, 0.05f) : Color.white);
                m.SetFloat("_Smoothness", eye ? 0.75f : slot == AnimalModelSet.EyeHi ? 0.5f : slot == AnimalModelSet.Beak ? 0.35f : 0.15f);
                m.SetFloat("_Metallic", 0f);
                m.enableInstancing = true;
                EditorUtility.SetDirty(m);
                list.Add(m);
            }
            AssetDatabase.SaveAssets();
            return list.ToArray();
        }
    }
}
