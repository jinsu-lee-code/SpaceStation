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
    /// 재질(URP Lit: 그레이박스 대체용 / 천장 조명 발광 / 문 상태등, 11-10 연기 파티클) → 벽 키트 에셋(11-2a, FBX에서) → 게임 씬에 InteriorMode 배치·연결 → 선택 패널에 [들어가기] 줄 추가.
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
        public const string CratePath = MaterialFolder + "/M_InteriorCrate.mat";
        public const string HazardPath = MaterialFolder + "/M_InteriorHazard.mat";
        public const string RockPath = MaterialFolder + "/M_InteriorRock.mat";
        public const string MoltenPath = MaterialFolder + "/M_InteriorMolten.mat";
        public const string ScreenDarkPath = MaterialFolder + "/M_InteriorScreenDark.mat";
        public const string ScreenGridPath = MaterialFolder + "/M_InteriorScreenGrid.mat";
        public const string PlasmaPath = MaterialFolder + "/M_InteriorPlasma.mat";
        public const string SolarCellPath = MaterialFolder + "/M_InteriorSolarCell.mat";
        public const string ShieldPath = MaterialFolder + "/M_InteriorShield.mat";
        public const string RedPath = MaterialFolder + "/M_InteriorRed.mat";
        public const string CulturePath = MaterialFolder + "/M_InteriorCulture.mat";
        public const string WindowPath = MaterialFolder + "/M_InteriorWindow.mat";
        public const string SofaPath = MaterialFolder + "/M_InteriorSofa.mat";
        public const string RugPath = MaterialFolder + "/M_InteriorRug.mat";
        public const string InteriorHullDarkPath = MaterialFolder + "/M_InteriorHullDark.mat";
        // 12-5 소품 색 (발광 없음, 2026-10-19): 발광 재질(Accent · Water · Light)을 색으로 쓴 소품이 평평하게 빛나 "텍스처가 안 들어간" 것처럼 보였음
        public const string PaperPath = MaterialFolder + "/M_InteriorPaper.mat";
        public const string PropBluePath = MaterialFolder + "/M_InteriorPropBlue.mat";
        public const string PropGreenPath = MaterialFolder + "/M_InteriorPropGreen.mat";
        public const string PropOrangePath = MaterialFolder + "/M_InteriorPropOrange.mat";
        public const string PropPurplePath = MaterialFolder + "/M_InteriorPropPurple.mat";
        public const string PropYellowPath = MaterialFolder + "/M_InteriorPropYellow.mat";
        public const string FishPath = MaterialFolder + "/M_InteriorFish.mat";
        public const string IcePath = MaterialFolder + "/M_InteriorIce.mat";
        public const string AquaPath = MaterialFolder + "/M_InteriorAqua.mat";
        public const string ConcretePath = MaterialFolder + "/M_InteriorConcrete.mat";   // 12-5 방사선 차폐실 벽 · 원통 천장
        // 진화 모듈 내부 1단계 키트 (2026-10-29, Blender evo_kit): 흰 세라믹 몸체 · 밝은 회백 바닥 · 짙은 남색 홈 · 화면 테
        public const string EvoWhitePath = MaterialFolder + "/M_InteriorEvoWhite.mat";
        public const string EvoFloorPath = MaterialFolder + "/M_InteriorEvoFloor.mat";
        public const string EvoDarkPath = MaterialFolder + "/M_InteriorEvoDark.mat";
        // 투명한 물 (2026-10-29 수소 플랜트 전기분해조 — 사용자 "위에 보이는 물 표면이 어색": 통 속을 물 덩어리로, 속 거품 · 전극이 비침)
        public const string WaterClearPath = MaterialFolder + "/M_InteriorWaterClear.mat";
        private const string SmokePath = MaterialFolder + "/M_InteriorSmoke.mat";
        private const string DustPath = "Assets/_Project/Art/Materials/FX/M_Dust.mat";
        internal const string SparkPath ="Assets/_Project/Art/Materials/FX/M_Spark.mat";
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

        /// <summary>진화 1단계 키트 조각 (Blender Interior_Evo.blend evo1_kit_builder — 기본 조각과 같은 규격, 이름 = 기본 이름 + _Evo1).</summary>
        public const string EvoKit1Path = "Assets/_Project/Art/Models/Interior/SM_InteriorKit_Evo1.fbx";
        private const string RecipeFolder = "Assets/_Project/Data/Recipes";

        /// <summary>단계 키트 FBX의 조각 (없으면 빈 목록). 게임 중 InteriorSpots가 벽 패널 모양을 읽으므로 Read/Write를 켬 (기본 키트와 같음).</summary>
        private static System.Collections.Generic.Dictionary<string, GameObject> StageParts(string path)
        {
            var parts = new System.Collections.Generic.Dictionary<string, GameObject>();
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer))
                return parts;
            if (!importer.isReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
            }
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (o is GameObject g && g.GetComponent<MeshFilter>() != null)
                    parts[g.name] = g;
            }
            return parts;
        }

        /// <summary>레시피 단계가 stage인 진화 모듈 (그 단계 키트를 쓰는 방).</summary>
        private static System.Collections.Generic.List<Data.ModuleData> ModulesOfStage(int stage)
        {
            var list = new System.Collections.Generic.List<Data.ModuleData>();
            foreach (var guid in AssetDatabase.FindAssets("t:RecipeData", new[] { RecipeFolder }))
            {
                var r = AssetDatabase.LoadAssetAtPath<Data.RecipeData>(AssetDatabase.GUIDToAssetPath(guid));
                if (r != null && r.Stage == stage && r.Result != null && !list.Contains(r.Result))
                    list.Add(r.Result);
            }
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return list;
        }

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
            var window = AssetDatabase.LoadAssetAtPath<Material>(WindowPath);
            var defaultAccent = AssetDatabase.LoadAssetAtPath<Material>(AccentFolder + "M_Accent_Core.mat");

            var evoWhite = AssetDatabase.LoadAssetAtPath<Material>(EvoWhitePath);
            var evoFloor = AssetDatabase.LoadAssetAtPath<Material>(EvoFloorPath);
            var evoDark = AssetDatabase.LoadAssetAtPath<Material>(EvoDarkPath);

            Data.InteriorKitPiece Piece(string name, System.Collections.Generic.Dictionary<string, GameObject> from = null)
            {
                if (!(from ?? parts).TryGetValue(name, out var g))
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
                        case "Window": mats[i] = window; break; // 11-8 튜브 현창 유리 (바깥 카메라 화면)
                        case "EvoWhite": mats[i] = evoWhite; break; // 진화 1단계 키트 (매끈한 흰 세라믹)
                        case "EvoFloor": mats[i] = evoFloor; break;
                        case "EvoDark": mats[i] = evoDark; break;
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
            kit.EditorSetTube(Piece("KIT_Tube"), Piece("KIT_TubeCollar"), Piece("KIT_TubeWindow"));
            // 진화 단계별 조각 (사용자 2026-10-29 "1단계 진화 모듈은 매끈한 백색의 벽"): 1단계 = Interior_Evo.blend evo1_kit_builder
            var stages = new System.Collections.Generic.List<Data.InteriorKitStage>();
            var evo1 = StageParts(EvoKit1Path);
            if (evo1.Count > 0)
            {
                stages.Add(new Data.InteriorKitStage(1, ModulesOfStage(1),
                    Piece("KIT_Wall_Evo1", evo1), Piece("KIT_WallDoor_Evo1", evo1), Piece("KIT_DoorLeaf_Evo1", evo1), Piece("KIT_Floor_Evo1", evo1),
                    Piece("KIT_Ceiling_Evo1", evo1), Piece("KIT_HatchFrame_Evo1", evo1), Piece("KIT_HatchLid_Evo1", evo1)));
            }
            kit.EditorSetStages(stages);
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
            // 11-10 상태 연출: 파손 방 연기·스파크·경광등
            so.FindProperty("_smokeMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(SmokePath);
            so.FindProperty("_sparkMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(SparkPath);
            so.FindProperty("_beaconMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(StatusPath);
            so.ApplyModifiedPropertiesWithoutUndo();

            BuildEnterRow(mode);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            // [들어가기] 줄을 다시 만들면 같은 줄의 12-3b [옮기기] 버튼이 지워짐 → 다시 붙임
            // (2026-10-19: Build New Basic Templates → 이 메뉴를 돌린 tycoon 씬에서 [옮기기]가 사라졌음)
            MoveSetup.Setup();
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

        /// <summary>화면 재질 광택. 0.85는 방 천장 조명이 화면 위에 작고 강한 하얀 반사점(블룸)으로 비쳤음 (2026-10-19, 허브 키오스크 · 센서 레이더) → 0.55</summary>
        private const float ScreenSmoothness = 0.55f;

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
            // 12-5 콘크리트 (방사선 차폐실): 같은 트라이플래너 셰이더에 거푸집 · 폼타이 구멍 텍스처. 실내 조명이 따뜻해 살짝 푸른 회색이어야 회색으로 보임 (MODELING 7-7)
            if (!System.IO.File.Exists(InteriorTextureBaker.ConcreteNormalPath) || !System.IO.File.Exists(InteriorTextureBaker.ConcreteMaskPath))
                InteriorTextureBaker.BakeConcrete();
            var concrete = LoadOrCreate(ConcretePath, tri);
            concrete.shader = tri;
            concrete.SetColor("_BaseColor", new Color(0.44f, 0.47f, 0.52f));
            concrete.SetFloat("_Smoothness", 0.12f);
            concrete.SetFloat("_Metallic", 0f);
            concrete.SetTexture("_DetailNormal", AssetDatabase.LoadAssetAtPath<Texture2D>(InteriorTextureBaker.ConcreteNormalPath));
            concrete.SetTexture("_DetailMask", AssetDatabase.LoadAssetAtPath<Texture2D>(InteriorTextureBaker.ConcreteMaskPath));
            concrete.SetFloat("_Tiling", 0.5f);
            concrete.SetFloat("_NormalStrength", 0.9f);
            concrete.SetFloat("_OcclusionStrength", 0.8f);
            concrete.SetFloat("_GrimeStrength", 0.65f);
            concrete.enableInstancing = true;
            EditorUtility.SetDirty(concrete);

            // 진화 모듈 내부 1단계 키트 (2026-10-29): 외형 흰 패널 · 짙은 유리 띠와 같은 계열의 매끈한 재질 (패널 무늬 · 때 없음)
            //   (이름을 Plain으로 했다가 아래 소품 재질 함수 Plain(경로, 색, 금속도, 광택)을 가려 소품 22종의 금속도 · 광택이 뒤바뀐 적 있음 — 2026-10-29)
            void EvoSurface(string path, Color color, float metallic, float smoothness)
            {
                var m = LoadOrCreate(path, lit);
                m.shader = lit;
                m.SetColor("_BaseColor", color);
                m.SetFloat("_Metallic", metallic);
                m.SetFloat("_Smoothness", smoothness);
                m.enableInstancing = true;
                EditorUtility.SetDirty(m);
            }
            EvoSurface(EvoWhitePath, new Color(0.9f, 0.92f, 0.95f), 0f, 0.6f);
            EvoSurface(EvoFloorPath, new Color(0.6f, 0.62f, 0.67f), 0.05f, 0.45f);
            EvoSurface(EvoDarkPath, new Color(0.035f, 0.045f, 0.1f), 0.2f, 0.8f);

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
            // 11-6 제련소 쇳물·갓 부은 주괴. 첫 값(발광 ×2.6, 녹색 0.38)은 블룸에 노랗게 날아가 버터처럼 보였음 → 붉은 주황으로 낮춤
            Emissive(MoltenPath, new Color(0.9f, 0.25f, 0.04f), new Color(1f, 0.19f, 0.015f) * 1.6f, 0.3f);
            // 11-6 화면 바탕(어두운 남색, 약한 발광) + 격자 줄(흐린 청록) — 그 위에 밝은 Screen 요소로 화면 구성
            Emissive(ScreenDarkPath, new Color(0.02f, 0.05f, 0.08f), new Color(0.015f, 0.07f, 0.11f), ScreenSmoothness);
            Emissive(ScreenGridPath, new Color(0.04f, 0.15f, 0.2f), new Color(0.04f, 0.2f, 0.26f) * 1.2f, ScreenSmoothness);
            // 11-6 핵융합로 플라스마 관찰창 (분홍 보랏빛 — 외부 M_FusionPlasma와 같은 계열)
            Emissive(PlasmaPath, new Color(0.75f, 0.35f, 0.95f), new Color(0.8f, 0.3f, 1f) * 1.5f, 0.3f);
            // 11-7 실드 방출기 코어·에너지 빔 (밝은 하늘색)
            Emissive(ShieldPath, new Color(0.45f, 0.8f, 1f), new Color(0.3f, 0.7f, 1f) * 2.0f, 0.4f);
            // 12-5 단백질 배양실 배양액 (크림빛 호박색, 은은한 발광 — 탱크 유리 속 · 콘솔 수위 막대)
            Emissive(CulturePath, new Color(1f, 0.78f, 0.42f), new Color(1f, 0.6f, 0.25f) * 1.1f, 0.5f);

            // 장비 재질 (벽 패널 무늬 없는 단색): 배관 금속 · 장비(이음·책상·화면 테두리) · 도장 사물함
            Plain(MetalPath, new Color(0.62f, 0.64f, 0.67f), 0.75f, 0.55f);
            Plain(DevicePath, new Color(0.2f, 0.21f, 0.23f), 0.2f, 0.45f);
            Plain(LockerPath, new Color(0.5f, 0.55f, 0.6f), 0.35f, 0.4f);
            Plain(ClinicPath, new Color(0.88f, 0.9f, 0.92f), 0.05f, 0.55f); // 11-5 의료 장비 흰 플라스틱
            Plain(CratePath, new Color(0.78f, 0.47f, 0.18f), 0.25f, 0.35f); // 11-6 화물 상자·컨테이너 주황 도장
            Plain(HazardPath, new Color(0.9f, 0.68f, 0.1f), 0.2f, 0.4f);    // 11-6 경고 줄무늬 노랑 (검정 칸 = Device)
            Plain(RockPath, new Color(0.3f, 0.27f, 0.25f), 0.35f, 0.25f);   // 11-6 광석 (컨베이어 고무 벨트도)
            Plain(SolarCellPath, new Color(0.06f, 0.12f, 0.32f), 0.7f, 0.85f); // 11-7 태양전지 면 (짙은 남색, 매끈)
            Plain(RedPath, new Color(0.72f, 0.1f, 0.08f), 0.3f, 0.45f);        // 11-7 소화기·비상 설비 빨강 도장
            // 12-5 소품 색 (발광 없음): 종이 · 색 소품(모자 · 책 · 가방 · 과일) · 생선 · 얼음 · 빛나지 않는 물(병 속)
            Plain(PaperPath, new Color(0.9f, 0.86f, 0.76f), 0f, 0.2f);
            Plain(PropBluePath, new Color(0.18f, 0.4f, 0.78f), 0.05f, 0.45f);
            Plain(PropGreenPath, new Color(0.3f, 0.6f, 0.2f), 0f, 0.5f);
            Plain(PropOrangePath, new Color(0.95f, 0.47f, 0.08f), 0f, 0.5f);
            Plain(PropPurplePath, new Color(0.35f, 0.13f, 0.42f), 0f, 0.55f);
            Plain(PropYellowPath, new Color(0.95f, 0.8f, 0.18f), 0f, 0.5f);
            Plain(FishPath, new Color(0.52f, 0.6f, 0.68f), 0.55f, 0.7f);
            Plain(IcePath, new Color(0.82f, 0.9f, 0.96f), 0f, 0.85f);
            Plain(AquaPath, new Color(0.4f, 0.7f, 0.92f), 0f, 0.9f);
            Emissive(ScreenPath, new Color(0.05f, 0.18f, 0.22f), new Color(0.1f, 0.55f, 0.7f) * 1.8f, ScreenSmoothness);

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

            // 투명한 물: 유리보다 짙은 하늘색 반투명 + 은은한 빛 (통 속 물 덩어리 — 속의 거품 · 전극이 비침).
            // 사용자 2026-10-29 "투명도를 조금만 줄여 · 반사광이 생김" → 알파 0.38 → 0.55, 매끈함 0.92 → 0.3 + 반사 하이라이트 · 환경 반사 끔
            //   (물 윗면이 천장 조명을 하얗게 비췄음)
            var waterClear = LoadOrCreate(WaterClearPath, lit);
            waterClear.SetFloat("_Surface", 1f); // Transparent
            waterClear.SetFloat("_Blend", 0f);   // Alpha
            waterClear.SetFloat("_ZWrite", 0f);
            waterClear.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            waterClear.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            waterClear.SetOverrideTag("RenderType", "Transparent");
            waterClear.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            waterClear.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            waterClear.SetColor("_BaseColor", new Color(0.35f, 0.75f, 0.95f, 0.55f));
            waterClear.SetFloat("_Smoothness", 0.3f);
            waterClear.SetFloat("_Metallic", 0f);
            waterClear.SetFloat("_SpecularHighlights", 0f);
            waterClear.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            waterClear.SetFloat("_EnvironmentReflections", 0f);
            waterClear.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            waterClear.EnableKeyword("_EMISSION");
            waterClear.SetColor("_EmissionColor", new Color(0.05f, 0.22f, 0.32f));
            waterClear.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(waterClear);

            // 11-8 바깥 창: 바깥 카메라 화면을 보여주는 유리 (InteriorExteriorView). 안쪽 유리(탱크·냉장고 등)는 위 M_InteriorGlass 그대로
            var window = LoadOrCreate(WindowPath, Shader.Find(Interior.InteriorExteriorView.WindowShaderName));
            window.SetColor("_Tint", new Color(0.9f, 0.96f, 1f));
            window.SetColor("_RimColor", new Color(0.3f, 0.4f, 0.5f));
            window.SetFloat("_RimPower", 4f);
            window.SetFloat("_Streak", 0.03f);
            EditorUtility.SetDirty(window);

            // 문 상태등: 색은 런타임 MaterialPropertyBlock (정상 청록 / 경고 빨강)
            var status = LoadOrCreate(StatusPath, lit);
            status.SetColor("_BaseColor", Color.white);
            status.EnableKeyword("_EMISSION");
            status.SetColor("_EmissionColor", Color.white * 2f);
            status.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; // None이면 URP 재질 검사가 _EMISSION을 꺼 버림 (ModuleFxMaterials 2026-10-03)
            status.enableInstancing = true;
            EditorUtility.SetDirty(status);

            // 11-10 파손 방 연기: 우주 먼지(M_Dust)와 같은 부드러운 입자 텍스처, 가산 대신 알파 블렌드 (가산이면 연기가 빛나 보임).
            // 조명을 받는 Particles/Lit — Unlit은 어두운 비상 조명 방에서 회색으로 떠 보였음 (경광등 붉은빛을 받아야 함)
            var dust = AssetDatabase.LoadAssetAtPath<Material>(DustPath);
            var particlesLit = Shader.Find("Universal Render Pipeline/Particles/Lit");
            var smoke = LoadOrCreate(SmokePath, particlesLit);
            smoke.shader = particlesLit;
            if (dust != null)
                smoke.SetTexture("_BaseMap", dust.GetTexture("_BaseMap"));
            smoke.SetFloat("_Surface", 1f); // Transparent
            smoke.SetFloat("_Blend", 0f);   // Alpha
            smoke.SetFloat("_ZWrite", 0f);
            smoke.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            smoke.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            smoke.SetOverrideTag("RenderType", "Transparent");
            smoke.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            smoke.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            smoke.SetColor("_BaseColor", new Color(0.6f, 0.6f, 0.62f));
            smoke.SetFloat("_Smoothness", 0f);
            smoke.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(smoke);
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
