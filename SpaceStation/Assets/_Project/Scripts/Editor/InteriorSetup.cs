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
    /// Phase 11 내부 방문 설정 (메뉴 SpaceStation/Interior/Setup):
    /// 재질(URP Lit: 그레이박스 대체용 / 천장 조명 발광 / 문 상태등) → 벽 키트 에셋(11-2a, FBX에서) → 게임 씬에 InteriorMode 배치·연결 → 선택 패널에 [들어가기] 줄 추가.
    /// 다시 실행하면 같은 구성으로 덮어쓴다.
    /// </summary>
    public static class InteriorSetup
    {
        private const string GameScene = "Assets/_Project/Scenes/Main.unity";
        private const string MaterialFolder = "Assets/_Project/Art/Materials/Interior";
        private const string GreyboxPath = MaterialFolder + "/M_InteriorGreybox.mat";
        private const string LightPath = MaterialFolder + "/M_InteriorLight.mat";
        private const string StatusPath = MaterialFolder + "/M_InteriorStatus.mat";
        public const string InteriorHullPath = MaterialFolder + "/M_InteriorHull.mat";
        public const string FabricPath = MaterialFolder + "/M_InteriorFabric.mat";
        public const string BlanketPath = MaterialFolder + "/M_InteriorBlanket.mat";
        public const string PlantPath = MaterialFolder + "/M_InteriorPlant.mat";
        public const string PlantLightPath = MaterialFolder + "/M_InteriorPlantLight.mat";
        public const string SoilPath = MaterialFolder + "/M_InteriorSoil.mat";
        public const string GrowPath = MaterialFolder + "/M_InteriorGrow.mat";
        public const string LiquidPath = MaterialFolder + "/M_InteriorO2Liquid.mat";
        public const string ScreenPath = MaterialFolder + "/M_InteriorScreen.mat";
        public const string MetalPath = MaterialFolder + "/M_InteriorMetal.mat";
        public const string WaterPath = MaterialFolder + "/M_InteriorWater.mat";
        public const string DevicePath = MaterialFolder + "/M_InteriorDevice.mat";
        public const string LockerPath = MaterialFolder + "/M_InteriorLocker.mat";
        public const string ClinicPath = MaterialFolder + "/M_InteriorClinic.mat";
        public const string SofaPath = MaterialFolder + "/M_InteriorSofa.mat";
        public const string RugPath = MaterialFolder + "/M_InteriorRug.mat";
        public const string InteriorHullDarkPath = MaterialFolder + "/M_InteriorHullDark.mat";
        private const string RpAssetPath = "Assets/Settings/PC_RPAsset.asset";
        private const string RendererPath = "Assets/Settings/PC_Renderer.asset";
        private const string InteriorRendererPath = "Assets/Settings/PC_InteriorRenderer.asset";

        /// <summary>
        /// 11-4 다듬기 B: 내부 전용 렌더러 (PC_Renderer 복제 + SSAO 강하게·넓게). 바깥(1칸 = 1유닛) 기준 SSAO는 8m 내부에서 거의 안 보이므로
        /// 들어가 있는 동안만 카메라가 이 렌더러를 쓴다. 파이프라인 에셋의 렌더러 목록에 넣고 그 번호를 돌려준다.
        /// </summary>
        private static int CreateInteriorRenderer()
        {
            if (AssetDatabase.LoadMainAssetAtPath(InteriorRendererPath) == null)
                AssetDatabase.CopyAsset(RendererPath, InteriorRendererPath);
            var renderer = AssetDatabase.LoadMainAssetAtPath(InteriorRendererPath);
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(InteriorRendererPath))
            {
                if (sub == null || sub.name != "ScreenSpaceAmbientOcclusion")
                    continue;
                var so = new SerializedObject(sub);
                so.FindProperty("m_Settings.Intensity").floatValue = 1.1f;
                so.FindProperty("m_Settings.Radius").floatValue = 0.6f;
                so.FindProperty("m_Settings.DirectLightingStrength").floatValue = 0.55f;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            var rp = AssetDatabase.LoadMainAssetAtPath(RpAssetPath);
            var rpSo = new SerializedObject(rp);
            var list = rpSo.FindProperty("m_RendererDataList");
            int index = -1;
            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == renderer)
                    index = i;
            }
            if (index < 0)
            {
                index = list.arraySize;
                list.arraySize++;
                list.GetArrayElementAtIndex(index).objectReferenceValue = renderer;
                rpSo.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
            return index;
        }
        private const string KitModelPath = "Assets/_Project/Art/Models/Interior/SM_InteriorKit.fbx";
        private const string KitPath = "Assets/_Project/Data/Interior/InteriorKit.asset";
        private const string StationMaterials = "Assets/_Project/Art/Materials/Station/";
        private const string AccentFolder = "Assets/_Project/Art/Materials/";
        private const string ModuleFolder = "Assets/_Project/Data/Modules/";

        /// <summary>
        /// 11-2a: FBX 조각(KIT_*)을 키트 에셋으로. 재질 슬롯 이름 → Hull·HullDark = 외부 공용 재질, Light = 천장 조명, Status = 문 상태등,
        /// Accent = 방마다 모듈 강조색(MD_X → M_Accent_X, 없으면 코어색).
        /// </summary>
        private static void CreateKit()
        {
            var parts = new System.Collections.Generic.Dictionary<string, GameObject>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(KitModelPath))
            {
                if (o is GameObject g && g.GetComponent<MeshFilter>() != null)
                    parts[g.name] = g;
            }
            if (parts.Count == 0)
            {
                Debug.LogError($"[InteriorSetup] 키트 모델 없음: {KitModelPath}");
                return;
            }
            // 11-4 다듬기 A: 내부는 외부 공용 재질 대신 같은 색의 트라이플래너 패널 재질
            var hull = AssetDatabase.LoadAssetAtPath<Material>(InteriorHullPath);
            var hullDark = AssetDatabase.LoadAssetAtPath<Material>(InteriorHullDarkPath);
            var light = AssetDatabase.LoadAssetAtPath<Material>(LightPath);
            var status = AssetDatabase.LoadAssetAtPath<Material>(StatusPath);
            var defaultAccent = AssetDatabase.LoadAssetAtPath<Material>(AccentFolder + "M_Accent_Core.mat");

            Data.InteriorKitPiece Piece(string name)
            {
                if (!parts.TryGetValue(name, out var g))
                {
                    Debug.LogError($"[InteriorSetup] 키트 조각 없음: {name}");
                    return null;
                }
                var slots = g.GetComponent<MeshRenderer>().sharedMaterials;
                var mats = new Material[slots.Length];
                int accent = -1, statusSlot = -1;
                for (int i = 0; i < slots.Length; i++)
                {
                    string slot = slots[i] != null ? slots[i].name : "";
                    switch (slot)
                    {
                        case "Hull": mats[i] = hull; break;
                        case "HullDark": mats[i] = hullDark; break;
                        case "Light": mats[i] = light; break;
                        case "Status": mats[i] = status; statusSlot = i; break;
                        case "Accent": mats[i] = defaultAccent; accent = i; break;
                        default:
                            Debug.LogWarning($"[InteriorSetup] {name}: 모르는 재질 슬롯 '{slot}' → Hull");
                            mats[i] = hull;
                            break;
                    }
                }
                return new Data.InteriorKitPiece(g.GetComponent<MeshFilter>().sharedMesh, mats, accent, statusSlot);
            }

            var modules = new System.Collections.Generic.List<Data.ModuleData>();
            var accents = new System.Collections.Generic.List<Material>();
            foreach (var guid in AssetDatabase.FindAssets("t:ModuleData", new[] { ModuleFolder.TrimEnd('/') }))
            {
                var data = AssetDatabase.LoadAssetAtPath<Data.ModuleData>(AssetDatabase.GUIDToAssetPath(guid));
                var m = AssetDatabase.LoadAssetAtPath<Material>(AccentFolder + "M_Accent_" + data.name.Replace("MD_", "") + ".mat");
                if (m == null)
                    continue;
                modules.Add(data);
                accents.Add(m);
            }

            var kit = AssetDatabase.LoadAssetAtPath<Data.InteriorKit>(KitPath);
            if (kit == null)
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(KitPath));
                kit = ScriptableObject.CreateInstance<Data.InteriorKit>();
                AssetDatabase.CreateAsset(kit, KitPath);
            }
            kit.EditorSet(Piece("KIT_Wall"), Piece("KIT_WallDoor"), Piece("KIT_DoorLeaf"), Piece("KIT_Floor"), Piece("KIT_Ceiling"),
                Piece("KIT_HatchFrame"), Piece("KIT_HatchLid"), modules, accents, defaultAccent);
            kit.EditorSetBalcony(Piece("KIT_Deck"), Piece("KIT_RailBar"), Piece("KIT_RailPost"), Piece("KIT_StairStep"), Piece("KIT_StairPole"));
            kit.EditorSetTube(Piece("KIT_Tube"), Piece("KIT_TubeCollar"));
            EditorUtility.SetDirty(kit);
            AssetDatabase.SaveAssets();
            Debug.Log($"[InteriorSetup] 키트 {(kit.IsComplete ? "완성" : "불완전")} · 강조색 {modules.Count}종");
        }

        [MenuItem("SpaceStation/Interior/Setup")]
        public static void Setup()
        {
            CreateMaterials();
            CreateKit();
            int interiorRenderer = CreateInteriorRenderer();
            var scene = EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
            var greybox = AssetDatabase.LoadAssetAtPath<Material>(GreyboxPath);

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
            so.FindProperty("_interiorRenderer").intValue = interiorRenderer;
            so.FindProperty("_kit").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Data.InteriorKit>(KitPath);
            // 11-3 모듈별 템플릿: Data/Interior 아래의 InteriorTemplate 전부
            var templates = so.FindProperty("_templates");
            var guids = AssetDatabase.FindAssets("t:InteriorTemplate", new[] { System.IO.Path.GetDirectoryName(KitPath).Replace('\\', '/') });
            templates.arraySize = guids.Length;
            for (int i = 0; i < guids.Length; i++)
                templates.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<Data.InteriorTemplate>(AssetDatabase.GUIDToAssetPath(guids[i]));
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

        internal static void CreateMaterials()
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
            light.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; // None이면 URP 재질 검사가 _EMISSION을 꺼 버림 (ModuleFxMaterials 2026-10-03)
            light.enableInstancing = true;
            EditorUtility.SetDirty(light);

            // 11-4 다듬기 A: 트라이플래너 패널 재질 (외부 M_Hull·M_HullDark와 같은 색·광택 + 패널 디테일)
            if (!System.IO.File.Exists(InteriorTextureBaker.NormalPath) || !System.IO.File.Exists(InteriorTextureBaker.MaskPath))
                InteriorTextureBaker.Bake();
            var tri = Shader.Find("SpaceStation/InteriorTriplanar");
            var detailN = AssetDatabase.LoadAssetAtPath<Texture2D>(InteriorTextureBaker.NormalPath);
            var detailM = AssetDatabase.LoadAssetAtPath<Texture2D>(InteriorTextureBaker.MaskPath);
            // 금속도는 외부보다 낮춤: 실내는 주변광이 약해 금속도가 높으면 점광원만 반사하고 전체가 어둡게 보임
            void Panel(string path, string sourceName, float grime, float metallic)
            {
                var source = AssetDatabase.LoadAssetAtPath<Material>(StationMaterials + sourceName);
                var m = LoadOrCreate(path, tri);
                m.shader = tri;
                m.SetColor("_BaseColor", source.GetColor("_BaseColor"));
                m.SetFloat("_Smoothness", source.GetFloat("_Smoothness"));
                m.SetFloat("_Metallic", metallic);
                m.SetTexture("_DetailNormal", detailN);
                m.SetTexture("_DetailMask", detailM);
                m.SetFloat("_Tiling", 0.5f);
                m.SetFloat("_NormalStrength", 1f);
                m.SetFloat("_OcclusionStrength", 0.85f);
                m.SetFloat("_GrimeStrength", grime);
                m.enableInstancing = true;
                EditorUtility.SetDirty(m);
            }
            Panel(InteriorHullPath, "M_Hull.mat", 0.3f, 0.3f);
            Panel(InteriorHullDarkPath, "M_HullDark.mat", 0.2f, 0.45f);

            // 11-5 천 재질 (베개·매트리스 = 흰 천, 담요 = 색 천): 같은 트라이플래너 셰이더에 직물 결 텍스처, 금속 0·광택 낮게
            if (!System.IO.File.Exists(InteriorTextureBaker.FabricNormalPath) || !System.IO.File.Exists(InteriorTextureBaker.FabricMaskPath))
                InteriorTextureBaker.BakeFabric();
            var fabricN = AssetDatabase.LoadAssetAtPath<Texture2D>(InteriorTextureBaker.FabricNormalPath);
            var fabricM = AssetDatabase.LoadAssetAtPath<Texture2D>(InteriorTextureBaker.FabricMaskPath);
            void Fabric(string path, Color color)
            {
                var m = LoadOrCreate(path, tri);
                m.shader = tri;
                m.SetColor("_BaseColor", color);
                m.SetFloat("_Smoothness", 0.04f); // 천은 거의 반사 없음 (0.12도 가까운 조명에 플라스틱처럼 번들거림)
                m.SetFloat("_Metallic", 0f);
                m.SetTexture("_DetailNormal", fabricN);
                m.SetTexture("_DetailMask", fabricM);
                m.SetFloat("_Tiling", 4f);
                m.SetFloat("_NormalStrength", 0.8f);
                m.SetFloat("_OcclusionStrength", 0.6f);
                m.SetFloat("_GrimeStrength", 0.5f);
                m.enableInstancing = true;
                EditorUtility.SetDirty(m);
            }
            Fabric(FabricPath, new Color(0.86f, 0.85f, 0.81f));
            Fabric(BlanketPath, new Color(0.56f, 0.42f, 0.27f)); // 차분한 황토색 (거주 강조색 노랑과 어울리게)
            Fabric(SofaPath, new Color(0.3f, 0.29f, 0.36f));     // 11-5 휴게실 소파 (보랏빛 회색)
            Fabric(RugPath, new Color(0.38f, 0.24f, 0.5f));      // 휴게실 러그·쿠션 (보라)

            // 11-5 농장: 잎(무광 초록) · 흙(어두운 갈색) · 생장등(분홍 발광)
            void Leaf(string path, Color color)
            {
                var m = LoadOrCreate(path, lit);
                m.SetColor("_BaseColor", color);
                m.SetFloat("_Smoothness", 0.35f); // 잎의 은은한 윤기
                m.SetFloat("_Metallic", 0f);
                m.enableInstancing = true;
                EditorUtility.SetDirty(m);
            }
            Leaf(PlantPath, new Color(0.3f, 0.62f, 0.22f));
            Leaf(PlantLightPath, new Color(0.55f, 0.78f, 0.3f));
            var soil = LoadOrCreate(SoilPath, lit);
            soil.SetColor("_BaseColor", new Color(0.2f, 0.14f, 0.1f));
            soil.SetFloat("_Smoothness", 0.05f);
            soil.SetFloat("_Metallic", 0f);
            soil.enableInstancing = true;
            EditorUtility.SetDirty(soil);
            var grow = LoadOrCreate(GrowPath, lit);
            grow.SetColor("_BaseColor", new Color(1f, 0.55f, 0.9f));
            grow.EnableKeyword("_EMISSION");
            grow.SetColor("_EmissionColor", new Color(1f, 0.32f, 0.82f) * 2.2f);
            grow.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; // None이면 URP 재질 검사가 _EMISSION을 꺼 버림 (ModuleFxMaterials 2026-10-03)
            grow.enableInstancing = true;
            EditorUtility.SetDirty(grow);

            // 11-5 산소 생성기: 탱크 속 청록 액체(발광, 매끈) · 콘솔 화면(어두운 청록 발광)
            // 액체 광택 0.9는 방 조명이 한쪽에 강한 반사 줄을 만들어 한쪽만 밝아 보였음 → 0.5
            Emissive(LiquidPath, new Color(0.3f, 0.85f, 0.95f), new Color(0.15f, 0.75f, 0.95f) * 1.6f, 0.5f);

            // 11-5 물 재활용기: 탱크 속 물 (푸른빛 은은한 발광, 광택 0.5 — MODELING.md 6-1)
            Emissive(WaterPath, new Color(0.22f, 0.58f, 0.88f), new Color(0.1f, 0.38f, 0.7f) * 1.4f, 0.5f);

            // 장비 재질 (벽 패널 무늬 없는 단색): 배관 금속 · 장비(이음·책상·화면 테두리) · 도장 사물함
            Plain(MetalPath, new Color(0.62f, 0.64f, 0.67f), 0.75f, 0.55f);
            Plain(DevicePath, new Color(0.2f, 0.21f, 0.23f), 0.2f, 0.45f);
            Plain(LockerPath, new Color(0.5f, 0.55f, 0.6f), 0.35f, 0.4f);
            Plain(ClinicPath, new Color(0.88f, 0.9f, 0.92f), 0.05f, 0.55f); // 11-5 의료 장비 흰 플라스틱
            Emissive(ScreenPath, new Color(0.05f, 0.18f, 0.22f), new Color(0.1f, 0.55f, 0.7f) * 1.8f, 0.85f);

            // 11-4 템플릿 창 유리: 반투명, 살짝 푸른 반사
            var glass = LoadOrCreate(MaterialFolder + "/M_InteriorGlass.mat", lit);
            glass.SetFloat("_Surface", 1f); // Transparent
            glass.SetFloat("_Blend", 0f);   // Alpha
            glass.SetFloat("_ZWrite", 0f);
            glass.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            glass.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            glass.SetOverrideTag("RenderType", "Transparent");
            glass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            glass.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            glass.SetColor("_BaseColor", new Color(0.55f, 0.75f, 0.9f, 0.12f));
            glass.SetFloat("_Smoothness", 0.95f);
            glass.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(glass);

            // 문 상태등: 색은 런타임 MaterialPropertyBlock (정상 청록 / 경고 빨강)
            var status = LoadOrCreate(StatusPath, lit);
            status.SetColor("_BaseColor", Color.white);
            status.EnableKeyword("_EMISSION");
            status.SetColor("_EmissionColor", Color.white * 2f);
            status.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; // None이면 URP 재질 검사가 _EMISSION을 꺼 버림 (ModuleFxMaterials 2026-10-03)
            status.enableInstancing = true;
            EditorUtility.SetDirty(status);
            AssetDatabase.SaveAssets();
        }

        /// <summary>단색 URP Lit 재질 (장비·가구: 벽 패널 트라이플래너를 쓰지 않음)</summary>
        private static void Plain(string path, Color color, float metallic, float smoothness)
        {
            var m = LoadOrCreate(path, Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
        }

        /// <summary>발광 URP Lit 재질 (조명 판·액체·화면 등)</summary>
        private static void Emissive(string path, Color baseColor, Color emission, float smoothness)
        {
            var m = LoadOrCreate(path, Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", baseColor);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 0f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; // None이면 URP 재질 검사가 _EMISSION을 꺼 버림 (ModuleFxMaterials 2026-10-03)
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
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
