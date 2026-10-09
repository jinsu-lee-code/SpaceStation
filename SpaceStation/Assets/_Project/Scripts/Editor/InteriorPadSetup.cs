using SpaceStation.Interior;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 11-12 휴대 패드 모델 연결 (메뉴 SpaceStation/Interior/Wire Pad): Blender `BlenderWork/Pad/pad_builder.py`가 내보낸
    /// `SM_InteriorPad.fbx`(재질 = FBX 색 그대로 URP Lit)를 InteriorMode의 패드 설정에 넣고 Main 씬 저장.
    /// </summary>
    public static class InteriorPadSetup
    {
        private const string GameScene = "Assets/_Project/Scenes/Main.unity";
        private const string ModelPath = "Assets/_Project/Art/Models/Interior/SM_InteriorPad.fbx";

        [MenuItem("SpaceStation/Interior/Wire Pad")]
        public static void Wire()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[InteriorPadSetup] 플레이 모드에서는 실행하지 않음");
                return;
            }
            var imp = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (imp == null)
            {
                Debug.LogError("[InteriorPadSetup] 모델 없음: " + ModelPath);
                return;
            }
            if (imp.importAnimation || imp.animationType != ModelImporterAnimationType.None)
            {
                imp.importAnimation = false;
                imp.animationType = ModelImporterAnimationType.None;
                imp.importCameras = false;
                imp.importLights = false;
                imp.SaveAndReimport();
            }
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != GameScene)
                scene = EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
            var mode = Object.FindFirstObjectByType<InteriorMode>(FindObjectsInactive.Include);
            if (mode == null)
            {
                Debug.LogError("[InteriorPadSetup] InteriorMode 없음");
                return;
            }
            var so = new SerializedObject(mode);
            so.FindProperty("_padTuning").FindPropertyRelative("Model").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[InteriorPadSetup] 패드 모델 연결");
        }
    }
}
