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
            SetupEvo(connectors, flow);
            EditorSceneManager.MarkSceneDirty(station.gameObject.scene);

            ConnectorDepthBaker.MeasureAll();
            Debug.Log($"[ConnectorSetup] meshes {string.Join(",", meshes.Select(m => m.name))} hull {hull?.name} flow {flow?.name} ghost {ghost?.name}");
        }

        // ---------------- 진화 단계별 통로 (2026-11-17) ----------------

        private const string EvoModelPath = "Assets/_Project/Art/Models/Modules/SM_ConnectorsEvo.fbx";
        private const string EvoTextureFolder = "Assets/_Project/Art/Models/Modules/Textures/ConnectorsEvo";
        private const string EvoMaterialPath = "Assets/_Project/Art/Materials/M_ConnectorsEvo.mat";
        /// <summary>단계별 고리 간격 (Blender connector_builder RING_STEP과 같게).</summary>
        private static readonly float[] RingSteps = { 0.30f, 0.24f, 0.22f, 0.26f };

        /// <summary>
        /// SM_ConnectorsEvo.fbx (BlenderWork/Connectors/connector_builder.py --export): CN{단계}_Tube · Collar · Ring · Strip · Strut · Plate · VRing + CN_Lock.
        /// 재질 = 색표 텍스처 한 장 (M_ConnectorsEvo — 비활성 어둡게 MPB가 그대로 먹음), 띠 = ConnectorFlow.
        /// </summary>
        /// <summary>진화 통로만 다시 배선 (기본 통로 · 표면 깊이는 그대로).</summary>
        [MenuItem("SpaceStation/Art/Setup Evo Connectors")]
        public static void SetupEvoMenu()
        {
            var station = Object.FindFirstObjectByType<StationController>();
            var connectors = station != null ? station.GetComponent<StationConnectors>() : null;
            if (connectors == null)
            {
                Debug.LogError("[ConnectorSetup] 씬에 StationConnectors 없음 — Setup Connectors 먼저");
                return;
            }
            SetupEvo(connectors, ModuleFxMaterials.Get("ConnectorFlow"));
            EditorSceneManager.MarkSceneDirty(station.gameObject.scene);
        }

        private static void SetupEvo(StationConnectors connectors, Material flow)
        {
            if (AssetImporter.GetAtPath(EvoModelPath) is not ModelImporter importer)
            {
                Debug.LogWarning($"[ConnectorSetup] {EvoModelPath} 없음 — 진화 통로는 기본 모양");
                return;
            }
            var evo = EvoMaterial();
            importer.importAnimation = false;
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_ConnectorsEvo"), evo);
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Flow"), flow);
            importer.SaveAndReimport();

            var meshes = AssetDatabase.LoadAllAssetsAtPath(EvoModelPath).OfType<Mesh>().ToArray();
            Mesh Find(string name) => meshes.FirstOrDefault(m => m.name == name);
            var so = new SerializedObject(connectors);
            var stages = so.FindProperty("_stages");
            stages.arraySize = RingSteps.Length;
            for (int i = 0; i < RingSteps.Length; i++)
            {
                var s = stages.GetArrayElementAtIndex(i);
                string p = $"CN{i + 1}_";
                foreach (var part in new[] { "Tube", "Collar", "Ring", "Strip", "Strut", "Plate", "VRing" })
                    s.FindPropertyRelative(part).objectReferenceValue = Find(p + part);
                s.FindPropertyRelative("RingStep").floatValue = RingSteps[i];
            }
            so.FindProperty("_lockMesh").objectReferenceValue = Find("CN_Lock");
            so.FindProperty("_evoMaterial").objectReferenceValue = evo;
            so.ApplyModifiedProperties();
            Debug.Log($"[ConnectorSetup] 진화 통로 {meshes.Length}개 메시 · {evo.name}");
        }

        private static Material EvoMaterial()
        {
            Texture2D Tex(string name, bool srgb)
            {
                string path = $"{EvoTextureFolder}/{name}.png";
                if (AssetImporter.GetAtPath(path) is TextureImporter ti)
                {
                    ti.sRGBTexture = srgb;
                    ti.mipmapEnabled = false;
                    ti.filterMode = FilterMode.Point;
                    ti.wrapMode = TextureWrapMode.Clamp;
                    ti.textureCompression = TextureImporterCompression.Uncompressed;
                    ti.SaveAndReimport();
                }
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(EvoMaterialPath);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, EvoMaterialPath);
            }
            m.shader = shader;
            m.SetTexture("_BaseMap", Tex("T_ConnectorsEvo_BaseColor", true));
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_MetallicGlossMap", Tex("T_ConnectorsEvo_MetallicGloss", false));
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetFloat("_Smoothness", 1f);
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            m.SetTexture("_EmissionMap", Tex("T_ConnectorsEvo_Emission", true));
            m.SetColor("_EmissionColor", Color.white * 2.2f);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return m;
        }
    }
}
