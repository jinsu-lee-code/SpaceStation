using SpaceStation.Building;
using SpaceStation.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 12-3b 합성 연출 설정 (메뉴 SpaceStation/Synthesis/Setup FX):
    /// 1) 재질 6종(URP Lit, Art/Materials/Synthesis) — 보라 형광 · 상태등은 발광(GI RealtimeEmissive, MODELING.md 6-1)
    /// 2) FBX(Art/Models/Synthesis, BlenderWork/SynthArm/arm_builder.py) 재질 연결 + 축 변환 굽기
    /// 3) 프리팹 3종(Prefabs/Synthesis): 고리 직선 · 모서리 토막, 팔(받침 포함) — 감싸는 루트(바깥 = −z, 위 = +y) 아래 모델
    /// 4) 게임 씬에 SynthesisFx 연결
    /// 덤: 진화 모듈 재질(12-3)의 GI 플래그를 RealtimeEmissive로 (발광 키워드가 저장 때 꺼지던 문제).
    /// </summary>
    public static class SynthesisFxSetup
    {
        private const string MaterialDir = "Assets/_Project/Art/Materials/Synthesis";
        private const string ModelDir = "Assets/_Project/Art/Models/Synthesis";
        private const string PrefabDir = "Assets/_Project/Prefabs/Synthesis";
        private const string GameScene = "Assets/_Project/Scenes/Main.unity";

        private static readonly (string Source, string Name, Color Color, float Metallic, float Smooth, Color Emission)[] Materials =
        {
            ("M_ArmBody", "M_Synth_Body", new Color(0.95f, 0.62f, 0.16f), 0.35f, 0.45f, Color.black),
            ("M_ArmDark", "M_Synth_Dark", new Color(0.09f, 0.1f, 0.115f), 0.55f, 0.4f, Color.black),
            ("M_ArmMetal", "M_Synth_Metal", new Color(0.62f, 0.65f, 0.69f), 0.85f, 0.65f, Color.black),
            ("M_ArmCopper", "M_Synth_Copper", new Color(0.72f, 0.38f, 0.18f), 0.8f, 0.55f, Color.black),
            ("M_ArmLight", "M_Synth_Light", new Color(0.3f, 0.85f, 1f), 0f, 0.4f, new Color(0.45f, 1.3f, 1.6f)),
            ("M_ArmGlow", "M_Synth_Glow", new Color(0.62f, 0.3f, 1f), 0f, 0.5f, new Color(1.4f, 0.62f, 2.4f)),
        };

        [MenuItem("SpaceStation/Synthesis/Setup FX")]
        public static void Setup()
        {
            EnsureFolder(MaterialDir);
            EnsureFolder(PrefabDir);
            Material glow = null;
            var made = new System.Collections.Generic.Dictionary<string, Material>();
            foreach (var d in Materials)
            {
                var m = MakeMaterial(d.Name, d.Color, d.Metallic, d.Smooth, d.Emission);
                made[d.Source] = m;
                if (d.Name == "M_Synth_Glow")
                    glow = m;
            }
            foreach (var model in new[] { "SM_SynthRing", "SM_SynthArm" })
            {
                var path = $"{ModelDir}/{model}.fbx";
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                if (importer == null)
                {
                    Debug.LogError($"[SynthesisFxSetup] 모델 없음: {path} (arm_builder.py로 내보내기)");
                    return;
                }
                importer.bakeAxisConversion = true;
                importer.importCameras = false;
                importer.importLights = false;
                importer.importAnimation = false;
                foreach (var kv in made)
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
                importer.SaveAndReimport();
            }

            var straight = MakePrefab("PF_SynthRingStraight", "SM_SynthRing", "SynthRing_Straight");
            var corner = MakePrefab("PF_SynthRingCorner", "SM_SynthRing", "SynthRing_Corner");
            var arm = MakePrefab("PF_SynthArm", "SM_SynthArm", null);
            FixEvolvedMaterials();
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
            var station = Object.FindFirstObjectByType<StationController>();
            var clock = Object.FindFirstObjectByType<SimulationClock>();
            var fx = Object.FindFirstObjectByType<SynthesisFx>();
            if (fx == null)
            {
                fx = new GameObject("SynthesisFx").AddComponent<SynthesisFx>();
                Undo.RegisterCreatedObjectUndo(fx.gameObject, "SynthesisFx");
            }
            var so = new SerializedObject(fx);
            so.FindProperty("_station").objectReferenceValue = station;
            so.FindProperty("_clock").objectReferenceValue = clock;
            so.FindProperty("_clipShader").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Art/Shaders/SynthesisClip.shader");
            so.FindProperty("_ringStraight").objectReferenceValue = straight;
            so.FindProperty("_ringCorner").objectReferenceValue = corner;
            so.FindProperty("_arm").objectReferenceValue = arm;
            so.FindProperty("_glowMaterial").objectReferenceValue = glow;
            so.FindProperty("_sparkMaterial").objectReferenceValue = ParticleMaterial();
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[SynthesisFxSetup] 합성 연출 재질 · 프리팹 · 씬 연결 완료");
        }

        private static Material MakeMaterial(string name, Color color, float metallic, float smooth, Color emission)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smooth);
            bool emissive = emission.maxColorComponent > 0f;
            mat.SetKeyword(new LocalKeyword(mat.shader, "_EMISSION"), emissive);
            mat.SetColor("_EmissionColor", emission);
            mat.globalIlluminationFlags = emissive ? MaterialGlobalIlluminationFlags.RealtimeEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// 입자 재질: 운석 불꽃(M_Spark, 가산 입자)을 복제하되 바탕색을 흰색 HDR로 — 입자 색(추진 보라 · 용접 주황)이 그대로 나오게
        /// (M_Spark는 바탕색이 주황이라 보라 추진 입자가 주황 점으로 보였음).
        /// </summary>
        private static Material ParticleMaterial()
        {
            string path = $"{MaterialDir}/M_Synth_Particle.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/FX/M_Spark.mat"));
                AssetDatabase.CreateAsset(mat, path);
            }
            var white = new Color(1.8f, 1.8f, 1.8f, 1f);
            mat.SetColor("_BaseColor", white);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", white);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>감싸는 루트 아래 모델(축 변환 회전 그대로)을 둔 프리팹. child가 있으면 그 오브젝트만 남김.</summary>
        private static GameObject MakePrefab(string name, string model, string child)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/{model}.fbx");
            var root = new GameObject(name);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            if (child != null)
            {
                var keep = inst.transform.Find(child);
                keep.SetParent(root.transform, true);
                Object.DestroyImmediate(inst);
            }
            else
                inst.transform.SetParent(root.transform, true);
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;
            }
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{name}.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static void FixEvolvedMaterials()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/_Project/Art/Materials/Evolved" }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                m.SetKeyword(new LocalKeyword(m.shader, "_EMISSION"), true);
                EditorUtility.SetDirty(m);
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
