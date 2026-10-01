using System.Linq;
using SpaceStation.Building;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 5-6 연결 통로 배선 (메뉴 SpaceStation/Art/Setup Connectors).
    /// SM_Connectors.fbx(CN_Tube/CN_Collar/CN_Strut/CN_Plate)의 재질 슬롯을 텍스처 재질에 연결하고,
    /// 열린 씬의 StationController에 StationConnectors를 붙여 메시·재질을 넣은 뒤, 모든 모듈의 표면 깊이를 측정한다.
    /// </summary>
    public static class ConnectorSetup
    {
        private const string ModelPath = "Assets/_Project/Art/Models/Modules/SM_Connectors.fbx";

        [MenuItem("SpaceStation/Art/Setup Connectors")]
        public static void Setup()
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError($"[ConnectorSetup] {ModelPath} 없음");
                return;
            }
            var textured = ModuleTextureMaterials.EnsureAll("Connectors");
            bool changed = false;
            foreach (var slot in textured)
            {
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), slot.Key), slot.Value);
                changed = true;
            }
            if (importer.importAnimation)
            {
                importer.importAnimation = false;
                changed = true;
            }
            if (changed)
                importer.SaveAndReimport();

            var meshes = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Mesh>().ToArray();
            Mesh Find(string name) => meshes.FirstOrDefault(m => m.name == name);
            var hull = textured.Values.FirstOrDefault();
            var flow = ModuleFxMaterials.Get("ConnectorFlow");
            var ghost = AssetDatabase.FindAssets("M_Hologram t:Material")
                .Select(g => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault();

            var station = Object.FindFirstObjectByType<StationController>();
            if (station == null)
            {
                Debug.LogError("[ConnectorSetup] 씬에 StationController 없음");
                return;
            }
            var connectors = station.GetComponent<StationConnectors>();
            if (connectors == null)
                connectors = Undo.AddComponent<StationConnectors>(station.gameObject);
            var so = new SerializedObject(connectors);
            so.FindProperty("_station").objectReferenceValue = station;
            so.FindProperty("_tubeMesh").objectReferenceValue = Find("CN_Tube");
            so.FindProperty("_collarMesh").objectReferenceValue = Find("CN_Collar");
            so.FindProperty("_strutMesh").objectReferenceValue = Find("CN_Strut");
            so.FindProperty("_plateMesh").objectReferenceValue = Find("CN_Plate");
            so.FindProperty("_stripMesh").objectReferenceValue = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            so.FindProperty("_hullMaterial").objectReferenceValue = hull;
            so.FindProperty("_flowMaterial").objectReferenceValue = flow;
            so.FindProperty("_ghostMaterial").objectReferenceValue = ghost;
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(station.gameObject.scene);

            ConnectorDepthBaker.MeasureAll();
            Debug.Log($"[ConnectorSetup] meshes {string.Join(",", meshes.Select(m => m.name))} hull {hull?.name} flow {flow?.name} ghost {ghost?.name}");
        }
    }
}
