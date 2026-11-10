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
        public const string PlasmaHotPath = MaterialFolder + "/M_InteriorPlasmaHot.mat";
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
        // 진화 2단계 키트 (2026-11-02 그리드 허브 — 미드나잇 · 은빛 · 금빛, 바깥 정원: Blender evo2_kit)
        public const string Evo2DeckPath = MaterialFolder + "/M_InteriorEvo2Deck.mat";
        public const string Evo2WallPath = MaterialFolder + "/M_InteriorEvo2Wall.mat";
        public const string Evo2TrimPath = MaterialFolder + "/M_InteriorEvo2Trim.mat";
        public const string Evo2GoldPath = MaterialFolder + "/M_InteriorEvo2Gold.mat";
        public const string Evo2PathPath = MaterialFolder + "/M_InteriorEvo2Path.mat";
        public const string Evo2BedPath = MaterialFolder + "/M_InteriorEvo2Bed.mat";
        public const string Evo2IcePath = MaterialFolder + "/M_InteriorEvo2Ice.mat";
        public const string Evo2GlassPath = MaterialFolder + "/M_InteriorEvo2Glass.mat";
        public const string Evo2EggGlassPath = MaterialFolder + "/M_InteriorEvo2EggGlass.mat";
        public const string Evo2ClearGlassPath = MaterialFolder + "/M_InteriorEvo2ClearGlass.mat";
        public const string Evo2WindowPath = MaterialFolder + "/M_InteriorEvo2Window.mat";
        public const string Evo2StarDomePath = MaterialFolder + "/M_InteriorEvo2StarDome.mat";
        public const string Evo2StarLinesPath = MaterialFolder + "/M_InteriorEvo2StarLines.mat";
        public const string Evo2CurtainPath = MaterialFolder + "/M_InteriorEvo2Curtain.mat";
        public const string Evo2SkyPath = MaterialFolder + "/M_InteriorEvo2Sky.mat";
        public const string Evo2MirrorPath = MaterialFolder + "/M_InteriorEvo2Mirror.mat";
        public const string Evo2VioletPath = MaterialFolder + "/M_InteriorEvo2Violet.mat";
        public const string Evo2HeatPath = MaterialFolder + "/M_InteriorEvo2Heat.mat";
        public const string Evo2WaterGlowPath = MaterialFolder + "/M_InteriorEvo2WaterGlow.mat";
        public const string Evo2LawnPath = MaterialFolder + "/M_InteriorEvo2Lawn.mat";     // 바이오돔 잔디 판 (T_InteriorLawn, UV = 위에서 내려다본 평면 3m 한 장)
        public const string Evo2SoilPath = MaterialFolder + "/M_InteriorEvo2Soil.mat";     // 바이오돔 텃밭 흙 (T_InteriorSoil)
        public const string Evo2PondPath = MaterialFolder + "/M_InteriorEvo2Pond.mat";     // 호수 · 연못 (바깥 M_PureWater와 같은 셰이더, 잔물결 간격만 내부 크기로)
        public const string Evo2FallsPath = MaterialFolder + "/M_InteriorEvo2Falls.mat";   // 폭포 · 분수 (바깥 M_Falls와 같은 셰이더, 물줄기 간격만 내부 크기로)
        public const string Evo2RockPath = MaterialFolder + "/M_InteriorEvo2Rock.mat";     // 바이오돔 바위 · 물가 돌 (외형의 밝은 돌, M_InteriorRock은 거의 검게 보임)
        public const string Evo2BarPath = MaterialFolder + "/M_InteriorEvo2Bar.mat";       // 식량 공장 프로틴 바 (외형의 산호빛)
        public const string Evo2SunPath = MaterialFolder + "/M_InteriorEvo2Sun.mat";       // 정련 콤비나트 작은 해 (잡음 플라즈마, 주황)
        public const string Evo2SolarFlarePath = MaterialFolder + "/M_InteriorEvo2SolarFlare.mat";   // 정련 콤비나트 홍염 빛 띠 (LineRenderer)
        public const string Evo2MembranePath = MaterialFolder + "/M_InteriorEvo2Membrane.mat";   // 정련 콤비나트 격납고 입구 에너지 막 (압력 막 셰이더, 큰 아치 · 옅게)
        public const string CloudPath = "Assets/_Project/Art/Materials/FX/M_Cloud.mat";    // 바깥 구름과 같은 재질
        public const string Evo2StarSeaPath = MaterialFolder + "/M_InteriorEvo2StarSea.mat";     // 실드 돔 별빛 유리 바다
        public const string Evo2AuroraPath = MaterialFolder + "/M_InteriorEvo2Aurora.mat";       // 실드 돔 내부 오로라 (바깥 M_AuroraCurtain을 베끼고 움직임만 내부 크기로)
        public const string Evo2BeaconBeamPath = MaterialFolder + "/M_InteriorEvo2BeaconBeam.mat";   // 실드 돔 등대 빛 (찬 빛, 빛기둥 셰이더)
        public const string Evo2BeaconCorePath = MaterialFolder + "/M_InteriorEvo2BeaconCore.mat";   // 실드 돔 렌즈 속 빛 (잡음 플라즈마, 하늘색)
        public const string Evo2RiverPath = MaterialFolder + "/M_InteriorEvo2River.mat";     // 방사선 차폐벽 은하수 강 (바깥 M_MilkyRiver를 베끼고 좌표 · 물가만 내부 크기로)
        public const string Evo2StarPath = MaterialFolder + "/M_InteriorEvo2Star.mat";       // 병풍 별 (별마다 다른 박자로 반짝임, 꼭짓점 색 R = 위상)
        public const string Evo2MountPath = MaterialFolder + "/M_InteriorEvo2Mount.mat";     // 병풍 은빛 산
        public const string Evo2SandPath = MaterialFolder + "/M_InteriorEvo2Sand.mat";       // 달빛 자갈 마당
        public const string Evo2MoonPath = MaterialFolder + "/M_InteriorEvo2Moon.mat";       // 달 등 (은은한 흰빛)
        public const string Evo2LemonPath = MaterialFolder + "/M_InteriorEvo2Lemon.mat";
        // 진화 3단계 (진주 · 금 · 살아 있는 정원 + 빛과 수정, 궤도 도시부터 2026-11-10): 잔디 · 길 · 돌 · 흙 · 물 · 폭포는 2단계 재질, 수정 · 무지개는 바깥 FX 재질 (ModuleFxMaterials)
        public const string Evo3PearlPath = MaterialFolder + "/M_InteriorEvo3Pearl.mat";     // 진주빛 몸체 (따뜻한 흰빛 · 은은한 윤기)
        public const string Evo3GoldPath = MaterialFolder + "/M_InteriorEvo3Gold.mat";       // 금빛 테 · 난간 손잡이 (2단계 금보다 밝고 매끈)
        // 3단계 물 · 폭포 (사용자 2026-11-11): 물 = KWS 실사 물 (KwsWaterBuilder · InteriorKwsWater), 아래 재질은 KWS 패키지가 없는 PC에서 보이는 수면 (만화풍 ToonWater)
        public const string Evo3WaterPath = MaterialFolder + "/M_InteriorEvo3Water.mat";
        public const string Evo3FallsPath = MaterialFolder + "/M_InteriorEvo3Falls.mat";     // 실사 물줄기 (WaterFalls: 맑은 턱 → 흰 물 · 굴절 · 찢긴 옆, UV u 가로 · v 아래로)
        public const string FallsMaskPath = "Assets/_Project/Art/Textures/T_WaterFallsMask.png";   // R 물줄기 결 · G 거품 덩어리 · B 찢김 (sRGB 끔)
        public const string FallsMistPath = MaterialFolder + "/M_InteriorFallsMist.mat";     // 폭포 물안개 · 물방울 · 수면 거품 입자 (부드러운 점, 반투명)
        public const string Evo3RoofPath = MaterialFolder + "/M_InteriorEvo3Roof.mat";       // 마을 집 · 옹벽 집 처마 (복숭아빛 기와)
        public const string Evo3DoorPath = MaterialFolder + "/M_InteriorEvo3Door.mat";       // 집 문짝 (하늘빛)
        // 바이오스피어 계단 밭 작물 · 들꽃 (2026-11-11, Blender bio3_builder와 같은 색)
        public const string Evo3WheatPath = MaterialFolder + "/M_InteriorEvo3Wheat.mat";
        public const string Evo3LavenderPath = MaterialFolder + "/M_InteriorEvo3Lavender.mat";
        public const string Evo3TulipPath = MaterialFolder + "/M_InteriorEvo3Tulip.mat";
        public const string Evo3FieldPath = MaterialFolder + "/M_InteriorEvo3Field.mat";     // 밀밭 흙 (황토빛)
        public const string Evo3FlowerPath = MaterialFolder + "/M_InteriorEvo3Flower.mat";   // 해바라기 꽃잎 · 노란 꽃
        public const string Evo3Leaf2Path = MaterialFolder + "/M_InteriorEvo3Leaf2.mat";     // 양배추 · 밝은 잎
        public const string Evo3BarkPath = MaterialFolder + "/M_InteriorEvo3Bark.mat";       // 통나무 · 바구니
        public const string Evo3FallsColumnPath = MaterialFolder + "/M_InteriorEvo3FallsColumn.mat";   // 둥근 폭포 기둥 (WaterFalls, 옆 흐림 끔 — 원기둥 UV에서 한쪽이 지워졌음)
        public const string Evo3SpillPath = MaterialFolder + "/M_InteriorEvo3Spill.mat";     // 단 사이 작은 폭포 (WaterFalls 맑은 변형 — 맑은 물 판이 파란 유리처럼 보였음)
        public const string SoftRainbowPath = MaterialFolder + "/M_InteriorSoftRainbow.mat";   // 오로라처럼 옅은 무지개 띠 (SoftRainbow, 발끝 흐림)
        public const string SoftHaloPath = MaterialFolder + "/M_InteriorSoftHalo.mat";         // 프리즘 둘레 옅은 무지개 고리 (끝 없음)
        public const string Evo3BlossomPath = MaterialFolder + "/M_InteriorEvo3Blossom.mat"; // 열차 방석 (연분홍 천)     // 병풍 해 · 세 날개 표지 · 석등 불빛 (레몬빛)
        public const string LightShaftPath = MaterialFolder + "/M_InteriorLightShaft.mat";
        public const string SuitHelmetPath = MaterialFolder + "/M_InteriorSuitHelmet.mat";
        public const string ResidentSuitModelPath = "Assets/_Project/Art/Models/Interior/SM_ResidentSuit.fbx";
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
        public const string EvoKit2Path = "Assets/_Project/Art/Models/Interior/SM_InteriorKit_Evo2.fbx";
        public const string EvoKit3Path = "Assets/_Project/Art/Models/Interior/SM_InteriorKit_Evo3.fbx";
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
            var evo2Deck = AssetDatabase.LoadAssetAtPath<Material>(Evo2DeckPath);
            var evo2Wall = AssetDatabase.LoadAssetAtPath<Material>(Evo2WallPath);
            var evo2Trim = AssetDatabase.LoadAssetAtPath<Material>(Evo2TrimPath);
            var evo2Gold = AssetDatabase.LoadAssetAtPath<Material>(Evo2GoldPath);
            var evo2Glass = AssetDatabase.LoadAssetAtPath<Material>(Evo2GlassPath);
            var evo2Sky = AssetDatabase.LoadAssetAtPath<Material>(Evo2SkyPath);
            var evo2ClearGlass = AssetDatabase.LoadAssetAtPath<Material>(Evo2ClearGlassPath);
            var evo3Pearl = AssetDatabase.LoadAssetAtPath<Material>(Evo3PearlPath);
            var evo3Gold = AssetDatabase.LoadAssetAtPath<Material>(Evo3GoldPath);
            var evo2Path = AssetDatabase.LoadAssetAtPath<Material>(Evo2PathPath);
            var prism = ModuleFxMaterials.Get("Prism");

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
                        case "Evo2Deck": mats[i] = evo2Deck; break; // 진화 2단계 키트 (미드나잇)
                        case "Evo2Wall": mats[i] = evo2Wall; break;
                        case "Evo2Trim": mats[i] = evo2Trim; break;
                        case "Evo2Gold": mats[i] = evo2Gold; break;
                        case "Evo2Glass": mats[i] = evo2Glass; break;   // 닫힌 문 자리 = 유리 난간 + 하늘 조각 (열린 갑판)
                        case "Evo2Sky": mats[i] = evo2Sky; break;
                        case "Evo2ClearGlass": mats[i] = evo2ClearGlass; break;   // 실내형 방의 닫힌 문 자리 = 아치 창
                        case "Evo3Pearl": mats[i] = evo3Pearl; break;   // 진화 3단계 키트 (진주 · 금 · 수정)
                        case "Evo3Gold": mats[i] = evo3Gold; break;
                        case "Evo3Path": mats[i] = evo2Path; break;
                        case "Evo3Prism": mats[i] = prism; break;
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
            var evo2 = StageParts(EvoKit2Path);   // 2단계 = Interior_Evo.blend evo2_kit_builder (미드나잇 · 은빛 테 · 금빛 문틀)
            if (evo2.Count > 0)
            {
                stages.Add(new Data.InteriorKitStage(2, ModulesOfStage(2),
                    Piece("KIT_Wall_Evo2", evo2), Piece("KIT_WallDoor_Evo2", evo2), Piece("KIT_DoorLeaf_Evo2", evo2), Piece("KIT_Floor_Evo2", evo2),
                    Piece("KIT_Ceiling_Evo2", evo2), Piece("KIT_HatchFrame_Evo2", evo2), Piece("KIT_HatchLid_Evo2", evo2), Piece("KIT_WallSealed_Evo2", evo2), Piece("KIT_SkyPatch_Evo2", evo2)));
            }
            var evo3 = StageParts(EvoKit3Path);   // 3단계 = Interior_Evo.blend evo3_kit_builder (진주 · 금빛 테 · 수정 난간)
            if (evo3.Count > 0)
            {
                // 내부 템플릿이 있는 3단계 모듈만 — 템플릿 없는 방의 대체 벽까지 난간 + 하늘 조각이 되면 우주로 뚫린 방이 됨 (템플릿을 만들 때마다 Setup으로 다시 넣음)
                var withInterior = ModulesOfStage(3).FindAll(m => AssetDatabase.FindAssets("IT_" + m.name.Replace("MD_", "").Replace("_", "") + " t:InteriorTemplate").Length > 0);
                stages.Add(new Data.InteriorKitStage(3, withInterior,
                    Piece("KIT_Wall_Evo3", evo3), Piece("KIT_WallDoor_Evo3", evo3), Piece("KIT_DoorLeaf_Evo3", evo3), Piece("KIT_Floor_Evo3", evo3),
                    Piece("KIT_Ceiling_Evo3", evo3), Piece("KIT_HatchFrame_Evo3", evo3), Piece("KIT_HatchLid_Evo3", evo3), Piece("KIT_WallSealed_Evo3", evo3), Piece("KIT_SkyPatch_Evo3", evo3)));
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
            // 3단계 정원 나무 (plant_builder build_pine · build_blossom, 2026-11-10 궤도 도시 — 화분 · 나무는 처음에 손으로 연결해 둠)
            var plants = so.FindProperty("_plantSettings");
            foreach (var (field, file) in new[] { ("Pine", "SM_PlantPine"), ("PineWilted", "SM_PlantPineWilted"), ("Blossom", "SM_PlantBlossom"), ("BlossomWilted", "SM_PlantBlossomWilted") })
                plants.FindPropertyRelative(field).objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Models/Interior/Plants/" + file + ".fbx");
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
            // 진화 2단계 (미드나잇 · 은빛 · 금빛): 짙은 몸체는 방 조명에서 묻히지 않게 외형보다 조금 밝게
            EvoSurface(Evo2DeckPath, new Color(0.13f, 0.17f, 0.3f), 0.1f, 0.55f);
            EvoSurface(Evo2WallPath, new Color(0.17f, 0.23f, 0.4f), 0.1f, 0.35f);   // 광택이 높으면 짙은 천장에 조명이 작은 해처럼 비침
            EvoSurface(Evo2TrimPath, new Color(0.8f, 0.83f, 0.88f), 0.45f, 0.7f);
            EvoSurface(Evo2GoldPath, new Color(0.93f, 0.7f, 0.3f), 0.5f, 0.65f);
            EvoSurface(Evo2VioletPath, new Color(0.66f, 0.42f, 0.95f), 0.1f, 0.55f);   // 융합 플랜트 코일 · 뚜껑 (바깥 모델 모듈 색 — 사용자 2026-10-23 보라. Blender 값 (0.5, 0.25, 0.9)은 선형이라 여기 값이 더 밝음)
            EvoSurface(Evo2PathPath, new Color(0.78f, 0.69f, 0.58f), 0f, 0.35f);
            EvoSurface(Evo2BedPath, new Color(0.26f, 0.19f, 0.15f), 0f, 0.2f);
            EvoSurface(Evo2RockPath, new Color(0.72f, 0.67f, 0.6f), 0f, 0.12f);
            EvoSurface(Evo2BarPath, new Color(0.93f, 0.56f, 0.46f), 0f, 0.35f);
            EvoSurface(Evo2MountPath, new Color(0.55f, 0.62f, 0.76f), 0.25f, 0.45f);
            EvoSurface(Evo2SandPath, new Color(0.3f, 0.35f, 0.46f), 0f, 0.2f);
            EvoSurface(Evo3PearlPath, new Color(0.95f, 0.92f, 0.87f), 0.15f, 0.72f);
            EvoSurface(Evo3GoldPath, new Color(0.96f, 0.76f, 0.36f), 0.65f, 0.72f);
            EvoSurface(Evo3BlossomPath, new Color(0.97f, 0.7f, 0.8f), 0f, 0.3f);
            EvoSurface(Evo3RoofPath, new Color(0.93f, 0.6f, 0.5f), 0f, 0.35f);    // 시안 2 마을 집 (Blender Evo3Roof와 같은 색)
            EvoSurface(Evo3DoorPath, new Color(0.45f, 0.68f, 0.78f), 0.1f, 0.5f);
            EvoSurface(Evo3WheatPath, new Color(0.95f, 0.84f, 0.55f), 0f, 0.25f);   // 밝은 밀빛 (주황 원뿔처럼 보였음)
            EvoSurface(Evo3LavenderPath, new Color(0.62f, 0.48f, 0.92f), 0f, 0.3f);
            EvoSurface(Evo3TulipPath, new Color(0.95f, 0.3f, 0.35f), 0f, 0.35f);
            EvoSurface(Evo3FieldPath, new Color(0.62f, 0.48f, 0.3f), 0f, 0.08f);
            EvoSurface(Evo3FlowerPath, new Color(1f, 0.82f, 0.25f), 0f, 0.3f);
            EvoSurface(Evo3Leaf2Path, new Color(0.45f, 0.68f, 0.3f), 0f, 0.25f);
            EvoSurface(Evo3BarkPath, new Color(0.5f, 0.36f, 0.25f), 0f, 0.15f);
            // 바이오돔 잔디 · 흙 (사용자 2026-11-03 "나무와 풀 디테일"): 색 판이 아니라 풀잎 · 흙 무늬 텍스처
            void Ground(string path, string texture, float smoothness)
            {
                var m = LoadOrCreate(path, lit);
                m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(InteriorTextureBaker.Folder + texture));
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_Metallic", 0f);
                m.SetFloat("_Smoothness", smoothness);
                m.enableInstancing = true;
                EditorUtility.SetDirty(m);
            }
            Ground(Evo2LawnPath, "T_InteriorLawn.png", 0.12f);
            Ground(Evo2SoilPath, "T_InteriorSoil.png", 0.08f);

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
            // 잡음 플라즈마 (사용자 2026-11-02 "내부에 노이즈 — 제대로 된 플라즈마"), 뒤가 비치면 안 되는 관찰창에도 쓰여 불투명. 내부는 미터 단위라 덩어리 값이 바깥(모델 기준 14)보다 작음.
            // 처음 만들 때만 값을 넣음 (슬라이더로 맞춘 값을 덮어쓰지 않게)
            void InteriorPlasma(string path, Color core, Color edge, float scale)
            {
                var shader = Shader.Find("SpaceStation/Plasma");
                var old = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (old != null && old.shader == shader)
                    return;
                var m = LoadOrCreate(path, shader);
                m.shader = shader;
                m.shaderKeywords = new string[0];
                m.SetColor("_BaseColor", Color.white);
                m.SetColor("_CoreColor", core);
                m.SetColor("_EdgeColor", edge);
                m.SetFloat("_Scale", scale);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
                m.SetFloat("_ZWrite", 1f);
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                m.enableInstancing = true;
                EditorUtility.SetDirty(m);
            }
            InteriorPlasma(PlasmaPath, new Color(2f, 1.05f, 2.5f), new Color(0.85f, 0.22f, 1.4f), 2.4f);
            InteriorPlasma(PlasmaHotPath, new Color(3.4f, 2f, 3.8f), new Color(1.7f, 0.7f, 2.5f), 3.2f);   // 융합 발전소 고리의 밝은 덩이
            InteriorPlasma(Evo2SunPath, new Color(3f, 1.4f, 0.32f), new Color(1.75f, 0.5f, 0.06f), 1.8f);   // 정련 콤비나트 작은 해
            InteriorPlasma(Evo2BeaconCorePath, new Color(2.2f, 2.8f, 3.2f), new Color(0.4f, 1.1f, 2.2f), 2.6f);   // 실드 돔 렌즈 속 빛
            Emissive(Evo2MoonPath, new Color(1f, 0.96f, 0.82f), new Color(1f, 0.93f, 0.76f) * 0.72f, 0.12f);
            Emissive(Evo2LemonPath, new Color(1f, 0.86f, 0.3f), new Color(1f, 0.76f, 0.16f) * 1.05f, 0.5f);
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
            // 2단계 바깥 하늘: 창과 같은 셰이더, 유리 느낌 없이 (껍데기 면이 보이지 않게 — InteriorExteriorView가 창처럼 그림)
            var sky = LoadOrCreate(Evo2SkyPath, Shader.Find(Interior.InteriorExteriorView.WindowShaderName));
            sky.SetColor("_Tint", Color.white);
            sky.SetColor("_RimColor", Color.black);
            sky.SetFloat("_RimPower", 8f);
            sky.SetFloat("_Streak", 0f);
            EditorUtility.SetDirty(sky);

            // 2단계 유리 · 압력 막 · 우주복 헬멧 (M_InteriorGlass와 같은 반투명 꼴), 청백 빛줄
            void Clear(string path, Color color, float smoothness, Color emission)
            {
                var m = LoadOrCreate(path, lit);
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_ZWrite", 0f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetOverrideTag("RenderType", "Transparent");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                m.SetColor("_BaseColor", color);
                m.SetFloat("_Smoothness", smoothness);
                m.SetFloat("_Metallic", 0f);
                if (emission != Color.black)
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", emission);
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                EditorUtility.SetDirty(m);
            }
            Clear(Evo2GlassPath, new Color(0.5f, 0.85f, 0.85f, 0.2f), 0.95f, Color.black);
            Clear(Evo2EggGlassPath, new Color(0.5f, 0.85f, 0.85f, 0.1f), 0.95f, Color.black);   // 아콜로지 유리 알: 두 겹이라 더 옅게, 큰 곡면에서 등불 하이라이트가 긴 띠로 번져 하이라이트 끔
            var eggGlass = LoadOrCreate(Evo2EggGlassPath, lit);
            eggGlass.SetFloat("_SpecularHighlights", 0f);
            eggGlass.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            EditorUtility.SetDirty(eggGlass);
            // 대형 토러스 큰 유리 (창 · 지붕 · 벽, 여러 겹으로 겹침): 환경 반사 · 하이라이트를 끔 (겹치면 바깥이 초록으로 탁해지고 반사의 지평선이 비침), 어두운 색 (가까운 등불에 물들어 빛나지 않게)
            Clear(Evo2ClearGlassPath, new Color(0.1f, 0.14f, 0.16f, 0.07f), 0.95f, Color.black);
            var clearGlass = LoadOrCreate(Evo2ClearGlassPath, lit);
            clearGlass.SetFloat("_SpecularHighlights", 0f);
            clearGlass.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            clearGlass.SetFloat("_EnvironmentReflections", 0f);
            clearGlass.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            EditorUtility.SetDirty(clearGlass);
            EditorUtility.SetDirty(LoadOrCreate(Evo2StarDomePath, Shader.Find("SpaceStation/StellaDome")));     // 시민 회관 상영 하늘 (슬라이더 = 재질)
            EditorUtility.SetDirty(LoadOrCreate(Evo2StarLinesPath, Shader.Find("SpaceStation/StellaLines")));   // 시민 회관 별자리
            EditorUtility.SetDirty(LoadOrCreate(Evo2CurtainPath, Shader.Find("SpaceStation/PressureVeil")));   // 압력 막 (사용자 2026-11-02 "신비감", UV = 아치 기준 미터)
            EditorUtility.SetDirty(LoadOrCreate(Evo2SolarFlarePath, Shader.Find("SpaceStation/SolarFlare")));
            var membrane = LoadOrCreate(Evo2MembranePath, Shader.Find("SpaceStation/PressureVeil"));   // 격납고 입구: 아치 치수가 달라 따로 (기본 값이면 반폭 1m 밖이 전부 테두리로 빛남), 채굴선이 비쳐 보이게 옅게
            membrane.SetVector("_Arch", new Vector4(2.2f, 0.93f, 0f, 0f));
            membrane.SetFloat("_Veil", 0.22f);
            membrane.SetFloat("_Glow", 0.38f);
            EditorUtility.SetDirty(membrane);
            Clear(SuitHelmetPath, new Color(0.8f, 0.92f, 1f, 0.14f), 0.97f, Color.black);
            Clear(Evo2WaterGlowPath, new Color(0.45f, 0.85f, 1f, 0.5f), 0.55f, new Color(0.07f, 0.3f, 0.42f));   // 융합 플랜트 물 캡슐 · 물 달: 은은히 빛나는 물 (속 거품 · 전극이 비침)
            // 바이오돔 물: 바깥 재질을 그대로 베끼고 간격만 8배 (바깥 1칸 = 1, 내부 1칸 = 8m — 셰이더가 월드 좌표로 무늬를 그림)
            void Water8(string path, string source, string keyword, string toggle, string property, Vector4 value)
            {
                var from = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/FX/" + source + ".mat");
                var m = LoadOrCreate(path, from.shader);
                m.CopyPropertiesFromMaterial(from);
                m.renderQueue = from.renderQueue;
                m.EnableKeyword(keyword);
                m.SetFloat(toggle, 1f);
                m.SetVector(property, value);
                EditorUtility.SetDirty(m);
            }
            // 윤슬 세기 0: 눈앞에서는 규칙적인 흰 점 무늬로 보임. 물줄기: 가늘고 긴 결이 아래로 (칸이 넓으면 벽돌 무늬)
            Water8(Evo2PondPath, "M_PureWater", "_FLOW_POOL", "_FlowPool", "_Ripple", new Vector4(8.75f, 1f, 0.1f, 0f));
            Water8(Evo2FallsPath, "M_Falls", "_FLOW_FALL", "_FlowFall", "_Flow", new Vector4(0.9f, 1.8f, 0.045f, 0.45f));
            ToonWaterMaterials();
            var heat = LoadOrCreate(Evo2HeatPath, lit);   // 융합 플랜트 열 관 · 열 띠: 주황 빛 (모듈 색 Accent는 빛나면 희게 날아감)
            heat.SetColor("_BaseColor", new Color(1f, 0.55f, 0.2f));
            heat.EnableKeyword("_EMISSION");
            heat.SetColor("_EmissionColor", new Color(1f, 0.36f, 0.05f) * 1.5f);
            heat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            heat.enableInstancing = true;
            EditorUtility.SetDirty(heat);
            var houseWindow = LoadOrCreate(Evo2WindowPath, lit);   // 대형 토러스 집 창: 따뜻한 불빛 (Light는 하얗게 번져 창살이 묻힘)
            houseWindow.SetColor("_BaseColor", new Color(1f, 0.86f, 0.62f));
            houseWindow.EnableKeyword("_EMISSION");
            houseWindow.SetColor("_EmissionColor", new Color(1f, 0.74f, 0.42f) * 1.05f);
            houseWindow.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            houseWindow.enableInstancing = true;
            EditorUtility.SetDirty(houseWindow);
            var ice = LoadOrCreate(Evo2IcePath, lit);
            ice.SetColor("_BaseColor", new Color(0.8f, 0.93f, 1f));
            ice.EnableKeyword("_EMISSION");
            ice.SetColor("_EmissionColor", new Color(0.75f, 0.92f, 1f) * 1.3f);
            ice.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            ice.enableInstancing = true;
            EditorUtility.SetDirty(ice);
            // 거울 해바라기 거울 (바깥 M_Mirror와 같은 값)
            var mirror = LoadOrCreate(Evo2MirrorPath, lit);
            mirror.SetColor("_BaseColor", new Color(0.78f, 0.88f, 0.98f));
            mirror.SetFloat("_Metallic", 0.9f);
            mirror.SetFloat("_Smoothness", 0.9f);
            mirror.EnableKeyword("_EMISSION");
            mirror.SetColor("_EmissionColor", new Color(0.04f, 0.06f, 0.09f));
            mirror.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mirror.enableInstancing = true;
            EditorUtility.SetDirty(mirror);
            // 내부 빛기둥 (바깥 M_LightShaft와 따로 조절 — 안에서는 빛기둥이 눈앞 가까이 지나감): 없을 때만 값을 넣음, 사용자가 슬라이더로 맞춘 값을 덮어쓰지 않게
            if (AssetDatabase.LoadAssetAtPath<Material>(LightShaftPath) == null)
            {
                var shaft = LoadOrCreate(LightShaftPath, Shader.Find("SpaceStation/LightShaft"));
                shaft.SetFloat("_Intensity", 0.12f);
                shaft.SetFloat("_MirrorGlow", 0.25f);
                shaft.enableInstancing = true;
                EditorUtility.SetDirty(shaft);
            }
            // 실드 돔: 바다 · 오로라 · 등대 빛 (없을 때만 값을 넣음, 슬라이더로 맞춘 값을 덮어쓰지 않게)
            EditorUtility.SetDirty(LoadOrCreate(Evo2StarSeaPath, Shader.Find("SpaceStation/StarSea")));
            if (AssetDatabase.LoadAssetAtPath<Material>(Evo2AuroraPath) == null)
            {
                var from = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/FX/M_AuroraCurtain.mat");
                var aurora = LoadOrCreate(Evo2AuroraPath, from.shader);
                aurora.CopyPropertiesFromMaterial(from);
                aurora.renderQueue = from.renderQueue;
                aurora.SetFloat("_Intensity", 0.35f);
                aurora.SetFloat("_Wander", 0.35f);
                aurora.SetFloat("_Sway", 0.35f);
                EditorUtility.SetDirty(aurora);
            }
            if (AssetDatabase.LoadAssetAtPath<Material>(Evo2BeaconBeamPath) == null)
            {
                var beacon = LoadOrCreate(Evo2BeaconBeamPath, Shader.Find("SpaceStation/LightShaft"));
                beacon.SetColor("_BaseColor", new Color(0.6f, 1.25f, 1.35f));
                beacon.SetFloat("_Intensity", 0.16f);
                beacon.SetFloat("_Attenuation", 1.6f);
                beacon.SetFloat("_Glint", 0.6f);
                beacon.SetFloat("_MirrorGlow", 0.3f);
                beacon.enableInstancing = true;
                EditorUtility.SetDirty(beacon);
            }

            // 방사선 차폐벽: 은하수 강 · 병풍 별 (없을 때만 값을 넣음, 슬라이더로 맞춘 값을 덮어쓰지 않게)
            if (AssetDatabase.LoadAssetAtPath<Material>(Evo2RiverPath) == null)
            {
                var from = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/FX/M_MilkyRiver.mat");
                var river = LoadOrCreate(Evo2RiverPath, from.shader);
                river.CopyPropertiesFromMaterial(from);
                river.SetFloat("_CoordScale", 0.125f);
                river.SetFloat("_BankPeriod", 0.575f);    // rw_builder PDX 2.3 × 2 ÷ 8
                river.SetFloat("_BankOffset", 0.1375f);   // 앞 꼭짓점 x = 1.1 ÷ 8
                river.SetFloat("_BankFront", -0.0741f);   // 강 앞 끝 z = -0.593 ÷ 8
                river.SetFloat("_BankAmp", 0.1212f);
                river.SetFloat("_BankBack", 0.2375f);     // 강 뒤 끝 z = 1.9 ÷ 8
                river.SetFloat("_StarSize", 0.38f);       // 가까이서 보므로 별을 작게 (바깥 크기면 눈송이처럼 보임)
                river.SetFloat("_StarDensity", 0.6f);
                river.enableInstancing = true;
                EditorUtility.SetDirty(river);
            }
            if (AssetDatabase.LoadAssetAtPath<Material>(Evo2StarPath) == null)
            {
                var star = LoadOrCreate(Evo2StarPath, Shader.Find("SpaceStation/Twinkle"));
                star.SetColor("_BaseColor", new Color(1.3f, 1.35f, 1.5f));
                star.SetFloat("_Min", 0.35f);
                star.SetFloat("_Peak", 1.7f);
                star.SetFloat("_Speed", 1.2f);
                star.enableInstancing = true;
                EditorUtility.SetDirty(star);
            }

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

        // 3단계 물 · 폭포: 값은 셰이더 기본값 (ToonWater · WaterFalls 머리글 참고) — 셰이더를 바꾸면 기본값으로 되돌림, 폭포만 무늬 텍스처를 꽂음
        public static void ToonWaterMaterials()
        {
            Material Fresh(string path, string shaderName)
            {
                var shader = Shader.Find(shaderName);
                var m = LoadOrCreate(path, shader);
                if (m.shader != shader)
                {
                    m.shader = shader;
                    m.CopyPropertiesFromMaterial(new Material(shader));
                }
                return m;
            }
            var water = Fresh(Evo3WaterPath, "SpaceStation/ToonWater");
            EditorUtility.SetDirty(water);
            var falls = Fresh(Evo3FallsPath, "SpaceStation/WaterFalls");
            falls.SetTexture("_MaskTex", AssetDatabase.LoadAssetAtPath<Texture2D>(FallsMaskPath));
            falls.renderQueue = -1;
            EditorUtility.SetDirty(falls);
            var column = Fresh(Evo3FallsColumnPath, "SpaceStation/WaterFalls");   // 둥근 기둥: 옆 흐림 · 찢김 끔, 결은 둘레를 따라 촘촘히
            column.SetTexture("_MaskTex", AssetDatabase.LoadAssetAtPath<Texture2D>(FallsMaskPath));
            column.SetFloat("_EdgeSoft", 0.01f);
            column.SetFloat("_EdgeBreak", 0f);
            column.SetVector("_StreakScale", new Vector4(2.4f, 1.1f, 0f, 0f));
            column.renderQueue = -1;
            EditorUtility.SetDirty(column);
            var spill = Fresh(Evo3SpillPath, "SpaceStation/WaterFalls");   // 0.3 m 안 짧은 폭포: 거의 맑게, 끝에서만 희게, 결은 성기게
            spill.SetTexture("_MaskTex", AssetDatabase.LoadAssetAtPath<Texture2D>(FallsMaskPath));
            spill.SetFloat("_ClearUntil", 0.55f);
            spill.SetFloat("_WhiteFrom", 1f);
            spill.SetFloat("_ClearAlpha", 0.45f);
            spill.SetFloat("_EdgeSoft", 0.12f);
            spill.SetVector("_StreakScale", new Vector4(1.4f, 0.45f, 0f, 0f));
            spill.renderQueue = -1;
            EditorUtility.SetDirty(spill);
            var rainbow = Fresh(SoftRainbowPath, "SpaceStation/SoftRainbow");
            rainbow.SetFloat("_Intensity", 0.32f);
            rainbow.SetFloat("_EndFade", 0.25f);
            EditorUtility.SetDirty(rainbow);
            var halo = Fresh(SoftHaloPath, "SpaceStation/SoftRainbow");
            halo.SetFloat("_Intensity", 0.26f);
            halo.SetFloat("_EndFade", 0f);
            EditorUtility.SetDirty(halo);
            // 물안개 · 물방울 · 거품 입자: URP Particles/Unlit 반투명 섞기 + 부드러운 점 (T_SoftDot)
            var mist = LoadOrCreate(FallsMistPath, Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            mist.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/Materials/FX/T_SoftDot.png"));
            mist.SetColor("_BaseColor", Color.white);
            mist.SetFloat("_Surface", 1f);
            mist.SetFloat("_Blend", 0f);   // Alpha
            mist.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mist.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mist.SetFloat("_ZWrite", 0f);
            mist.SetKeyword(new UnityEngine.Rendering.LocalKeyword(mist.shader, "_SURFACE_TYPE_TRANSPARENT"), true);
            mist.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 20;
            mist.SetOverrideTag("RenderType", "Transparent");
            EditorUtility.SetDirty(mist);
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
