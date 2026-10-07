using SpaceStation.Interior;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 11-11 주민 인물 모델 연결 (메뉴 SpaceStation/Interior/Wire Resident Models).
    /// LuceedStudio "Little Guys - Free Sample"의 남 · 여 × 보통 · 큰 체형 4개 프리팹을 InteriorMode에 순서대로 연결한다.
    /// 자세 · 옷 색은 실행 중에 <see cref="ResidentFigure"/> · <see cref="ResidentOutfits"/>가 만든다 (프리팹을 고치지 않음).
    /// </summary>
    public static class ResidentFigureSetup
    {
        private const string Root = "Assets/LuceedStudio/Character Lab/Little Guys/Little Guys - Free Sample/";

        // 순서 = InteriorResidents 모델 번호 (Man, Man Tall, Woman, Woman Tall)
        private static readonly string[] Models =
        {
            Root + "Man/Free Man/Free Man.prefab",
            Root + "Man/Free Man/Free Man Tall.prefab",
            Root + "Woman/Free Woman/Free Woman.prefab",
            Root + "Woman/Free Woman/Free Woman Tall.prefab",
        };

        [MenuItem("SpaceStation/Interior/Wire Resident Models")]
        public static void Wire()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[ResidentFigureSetup] 플레이 모드에서는 실행하지 않음");
                return;
            }
            var mode = Object.FindFirstObjectByType<InteriorMode>(FindObjectsInactive.Include);
            if (mode == null)
            {
                Debug.LogError("[ResidentFigureSetup] InteriorMode 없음 (Main 씬을 열고 다시)");
                return;
            }
            var so = new SerializedObject(mode);
            var list = so.FindProperty("_residentModels");
            list.arraySize = Models.Length;
            for (int i = 0; i < Models.Length; i++)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(Models[i]);
                if (model == null)
                {
                    Debug.LogError("[ResidentFigureSetup] 모델 없음: " + Models[i]);
                    return;
                }
                list.GetArrayElementAtIndex(i).objectReferenceValue = model;
            }
            so.FindProperty("_residentMaterial").objectReferenceValue = LitMaterial();
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(mode.gameObject.scene);
            EditorSceneManager.SaveScene(mode.gameObject.scene);
            Debug.Log("[ResidentFigureSetup] 인물 모델 4개 + 재질 연결");
        }

        /// <summary>
        /// 인물 재질: URP Lit (텍스처는 주민마다 MaterialPropertyBlock). 모델 원본 툰 셰이더(Luceed Studio/URP Stylized Combined)는
        /// 주광만 받아 태양이 꺼진 내부에서 새까맣게 보였음.
        /// </summary>
        private static Material LitMaterial()
        {
            const string path = "Assets/_Project/Art/Materials/Interior/M_ResidentLit.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Models[0]).GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial;
            m.SetTexture("_BaseMap", source.mainTexture);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0.12f);
            m.SetFloat("_Metallic", 0f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return m;
        }
    }
}
