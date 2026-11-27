using SpaceStation.Interior;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// CONNECT_1121 A6 단계별 내부 상황: 게임 씬 InteriorMode의 사고 설정(_incidentSettings)에 단계 상황 소품 모델을 연결한다.
    /// 모델 = BlenderWork/InteriorProps/stage_incident_builder.py → Art/Models/Interior/Incident/SM_Inc*.fbx. 이미 연결된 칸은 그대로 둔다.
    /// 새 소품을 만들면 <see cref="Props"/>에 한 줄 더하고 메뉴를 다시 실행 (main · 서사판 씬에도 같은 메뉴로).
    /// </summary>
    public static class StageIncidentSetup
    {
        private const string GameScene = "Assets/_Project/Scenes/Main.unity";
        private const string ModelFolder = "Assets/_Project/Art/Models/Interior/Incident/";

        /// <summary>(설정 칸 이름, 모델 파일 이름)</summary>
        private static readonly (string Field, string File)[] Props =
        {
            ("DecoBox", "SM_IncDecoBox.fbx"),   // 2단계 ② 장식 조명 꺼짐 — 조명 분전함
            ("RideConsole", "SM_IncRideConsole.fbx"),   // 2단계 ① 놀이기구 · 쇼 멈춤 — 운행 조작반
            ("Toys", "SM_IncToys.fbx"),   // 2단계 ③ 놀이 소품 흩어짐 — 바구니 · 소품
            ("Sluice", "SM_IncSluice.fbx"),   // 3단계 ① 물길 막힘 — 물가 물꼬 판
            ("Lantern", "SM_IncLantern.fbx"),   // 3단계 ② 등불 순서 — 정원 등불
            ("StarLantern", "SM_IncStarLantern.fbx"),   // 4단계 ② 별빛 등불
            ("Vines", "SM_IncVines.fbx"),   // 3단계 ③ 덩굴 웃자람
            ("SignalConsole", "SM_IncSignalConsole.fbx"),   // 4단계 ① 신호 흐트러짐 — 다이얼 조작반
            ("HoloPanel", "SM_IncHoloPanel.fbx"),   // 1단계 ① 홀로 패널 오류 — 벽 홀로 판
            ("CoolantCore", "SM_IncCoolantCore.fbx"),   // 1단계 ② 냉각 코어 과열 — 벽 냉각 장치
            ("RobotArm", "SM_IncRobotArm.fbx"),   // 1단계 ③ 정비 로봇 멈춤 — 바닥 로봇 팔 (관절 계층)
        };

        /// <summary>관절 계층 모델 (Blender bake 끔 → Unity가 축을 굽힘 — 체크리스트 55).</summary>
        private const string JointedModel = "SM_IncRobotArm.fbx";

        /// <summary>1단계 소품 발광 유리 (M_IncGlow 칸을 바꿔 끼움): 반투명 · 발광 URP Lit — 색은 InteriorIncidents가 블록으로 물들임.</summary>
        private const string GlowPath = "Assets/_Project/Art/Materials/FX/M_IncGlow.mat";

        [MenuItem("SpaceStation/Interior/Setup Stage Incidents (CONNECT A6)")]
        public static void Setup()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != GameScene)
                scene = EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
            var mode = Object.FindFirstObjectByType<InteriorMode>(FindObjectsInactive.Include);
            if (mode == null)
            {
                Debug.LogError("[StageIncidentSetup] InteriorMode 없음");
                return;
            }
            FixJointedImport();
            var so = new SerializedObject(mode);
            int linked = 0;
            var glow = so.FindProperty("_incidentSettings.Glow");
            if (glow != null && glow.objectReferenceValue == null)
            {
                glow.objectReferenceValue = GlowMaterial();
                linked++;
            }
            foreach (var (field, file) in Props)
            {
                var p = so.FindProperty("_incidentSettings." + field);
                if (p == null)
                {
                    Debug.LogError($"[StageIncidentSetup] 설정 칸 {field} 없음");
                    continue;
                }
                if (p.objectReferenceValue != null)
                    continue;
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelFolder + file);
                if (model == null)
                {
                    Debug.LogError($"[StageIncidentSetup] 모델 없음: {ModelFolder + file}");
                    continue;
                }
                p.objectReferenceValue = model;
                linked++;
            }
            if (linked == 0)
            {
                Debug.Log("[StageIncidentSetup] 이미 모두 연결됨");
                return;
            }
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[StageIncidentSetup] 단계 상황 소품 {linked}개 연결 · 씬 저장");
        }

        private static void FixJointedImport()
        {
            var importer = AssetImporter.GetAtPath(ModelFolder + JointedModel) as ModelImporter;
            if (importer == null || importer.bakeAxisConversion)
                return;
            importer.bakeAxisConversion = true;
            importer.SaveAndReimport();
            Debug.Log($"[StageIncidentSetup] {JointedModel}: bakeAxisConversion 켬");
        }

        private static Material GlowMaterial()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(GlowPath);
            if (m != null)
                return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_IncGlow" };
            m.SetFloat("_Surface", 1f);   // Transparent
            m.SetFloat("_Blend", 0f);     // Alpha
            m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            BaseShaderGUI.SetupMaterialBlendMode(m);   // 섞기 값은 URP와 같게 (InteriorSetup 유리와 같음)
            m.SetColor("_BaseColor", new Color(0.62f, 0.9f, 1f, 0.55f));
            m.SetFloat("_Smoothness", 0.85f);
            m.SetFloat("_Metallic", 0f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", new Color(0.62f, 0.9f, 1f));
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            AssetDatabase.CreateAsset(m, GlowPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[StageIncidentSetup] 발광 유리 재질 만듦: " + GlowPath);
            return m;
        }
    }
}
