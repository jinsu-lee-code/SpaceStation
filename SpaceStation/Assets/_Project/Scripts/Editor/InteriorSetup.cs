using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Interior;
using SpaceStation.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.Editor
{
    /// <summary>
    /// Phase 11-1 내부 방문 설정 (메뉴 SpaceStation/Interior/Setup):
    /// 그레이박스 재질 2개(URP Lit: 벽 공용 / 천장 조명 발광) → 게임 씬에 InteriorMode 배치·연결 → 선택 패널에 [들어가기] 줄 추가.
    /// 다시 실행하면 같은 구성으로 덮어쓴다.
    /// </summary>
    public static class InteriorSetup
    {
        private const string GameScene = "Assets/_Project/Scenes/Main.unity";
        private const string MaterialFolder = "Assets/_Project/Art/Materials/Interior";
        private const string GreyboxPath = MaterialFolder + "/M_InteriorGreybox.mat";
        private const string LightPath = MaterialFolder + "/M_InteriorLight.mat";

        [MenuItem("SpaceStation/Interior/Setup")]
        public static void Setup()
        {
            CreateMaterials();
            var scene = EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
            var greybox = AssetDatabase.LoadAssetAtPath<Material>(GreyboxPath);
            var light = AssetDatabase.LoadAssetAtPath<Material>(LightPath);

            var hud = GameObject.Find("HUD");
            var station = Object.FindFirstObjectByType<StationController>();
            var go = GameObject.Find("Interior");
            if (go == null)
                go = new GameObject("Interior");
            var mode = go.GetComponent<InteriorMode>();
            if (mode == null)
                mode = go.AddComponent<InteriorMode>();
            var so = new SerializedObject(mode);
            so.FindProperty("_station").objectReferenceValue = station;
            so.FindProperty("_selection").objectReferenceValue = Object.FindFirstObjectByType<ModuleSelectionController>();
            so.FindProperty("_build").objectReferenceValue = Object.FindFirstObjectByType<BuildController>();
            so.FindProperty("_clock").objectReferenceValue = Object.FindFirstObjectByType<SimulationClock>();
            so.FindProperty("_camera").objectReferenceValue = Camera.main;
            var sun = GameObject.Find("Directional Light");
            so.FindProperty("_sun").objectReferenceValue = sun != null ? sun.GetComponent<Light>() : null;
            so.FindProperty("_material").objectReferenceValue = greybox;
            so.FindProperty("_lightMaterial").objectReferenceValue = light;
            so.FindProperty("_hud").objectReferenceValue = hud != null ? hud.transform : null;
            so.FindProperty("_font").objectReferenceValue = hud != null ? hud.GetComponentInChildren<TMP_Text>(true).font : null;
            so.FindProperty("_fillSprite").objectReferenceValue = HudArtBuilder.Fill;
            so.ApplyModifiedPropertiesWithoutUndo();

            BuildEnterRow(mode);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[InteriorSetup] 내부 방문 설정 완료");
        }

        /// <summary>선택 패널 아래에 [들어가기] 버튼 줄 (재건축·철거 줄을 복제해 버튼 하나만 남김).</summary>
        private static void BuildEnterRow(InteriorMode mode)
        {
            var panel = Object.FindFirstObjectByType<SelectionActionsPanel>(FindObjectsInactive.Include);
            if (panel == null)
            {
                Debug.LogError("[InteriorSetup] SelectionActionsPanel 없음");
                return;
            }
            var root = panel.transform;
            var old = root.Find("Row3");
            if (old != null)
                Object.DestroyImmediate(old.gameObject);
            var row2 = root.Find("Row2");
            var row3 = Object.Instantiate(row2.gameObject, root);
            row3.name = "Row3";
            row3.transform.SetSiblingIndex(row2.GetSiblingIndex() + 1);
            Object.DestroyImmediate(row3.transform.Find("Demolish").gameObject);
            var enter = row3.transform.Find("Rebuild");
            enter.name = "Enter";
            var button = enter.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            var label = enter.GetComponentInChildren<TMP_Text>(true);
            label.text = "들어가기";

            var so = new SerializedObject(panel);
            so.FindProperty("_interior").objectReferenceValue = mode;
            so.FindProperty("_enterButton").objectReferenceValue = button;
            so.FindProperty("_enterLabel").objectReferenceValue = label;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void CreateMaterials()
        {
            System.IO.Directory.CreateDirectory(MaterialFolder);
            AssetDatabase.Refresh();
            var lit = Shader.Find("Universal Render Pipeline/Lit");

            var greybox = LoadOrCreate(GreyboxPath, lit);
            greybox.SetColor("_BaseColor", Color.white);
            greybox.SetFloat("_Smoothness", 0.25f);
            greybox.enableInstancing = true;
            EditorUtility.SetDirty(greybox);

            var light = LoadOrCreate(LightPath, lit);
            light.SetColor("_BaseColor", new Color(0.95f, 0.95f, 0.9f));
            light.EnableKeyword("_EMISSION");
            light.SetColor("_EmissionColor", new Color(1f, 0.95f, 0.85f) * 2.5f);
            light.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            light.enableInstancing = true;
            EditorUtility.SetDirty(light);
            AssetDatabase.SaveAssets();
        }

        private static Material LoadOrCreate(string path, Shader shader)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null)
                return mat;
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
