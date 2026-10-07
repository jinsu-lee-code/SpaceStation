using SpaceStation.Interior;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 11-11 주민 인물 프리팹 3개(서기 · 앉기 · 작업)와 재질을 만들고 InteriorMode에 연결한다 (메뉴 SpaceStation/Interior/Build Resident Figures).
    /// 모델: Art/Models/Interior/SM_Resident.fbx (Blender Interior_Kit.blend 텍스트 블록 resident_builder) — 자세마다 _Body · _Head 두 조각.
    /// 프리팹: 루트(ResidentFigure + CapsuleCollider: 플레이어가 지나가지 못하고, 바라보기 판정에 쓰임) → Body, HeadPivot(목) → Head.
    /// 재질은 흰색 바탕 — 색은 ResidentFigure가 MaterialPropertyBlock으로 칠한다.
    /// </summary>
    public static class ResidentFigureSetup
    {
        private const string ModelPath = "Assets/_Project/Art/Models/Interior/SM_Resident.fbx";
        private const string MaterialFolder = "Assets/_Project/Art/Materials/Interior";
        private const string PrefabFolder = "Assets/_Project/Prefabs/Interior/";

        private struct PoseDef
        {
            public string Name;
            public Vector3 Neck;          // resident_builder의 NECKS (목 = 머리 회전 중심)
            public float CapsuleHeight;
        }

        // 순서 = ResidentPose (Stand, Sit, Work)
        private static readonly PoseDef[] Poses =
        {
            new PoseDef { Name = "Stand", Neck = new Vector3(0f, 1.5f, 0f), CapsuleHeight = 1.75f },
            new PoseDef { Name = "Sit", Neck = new Vector3(0f, 1.07f, -0.02f), CapsuleHeight = 1.3f },
            new PoseDef { Name = "Work", Neck = new Vector3(0f, 1.5f, 0.03f), CapsuleHeight = 1.75f },
        };

        [MenuItem("SpaceStation/Interior/Build Resident Figures")]
        public static void Build()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[ResidentFigureSetup] 플레이 모드에서는 실행하지 않음");
                return;
            }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError("[ResidentFigureSetup] 모델 없음: " + ModelPath);
                return;
            }
            var suit = Mat("M_ResidentSuit", 0.25f);
            var accent = Mat("M_ResidentAccent", 0.35f);
            var skin = Mat("M_ResidentSkin", 0.3f);
            var hair = Mat("M_ResidentHair", 0.15f);
            var device = AssetDatabase.LoadAssetAtPath<Material>(InteriorSetup.DevicePath);

            var figures = new ResidentFigure[Poses.Length];
            for (int i = 0; i < Poses.Length; i++)
            {
                var pose = Poses[i];
                var bodySrc = model.transform.Find($"INT_Resident_{pose.Name}_Body");
                var headSrc = model.transform.Find($"INT_Resident_{pose.Name}_Head");
                if (bodySrc == null || headSrc == null)
                {
                    Debug.LogError($"[ResidentFigureSetup] {pose.Name}: 조각 없음");
                    return;
                }
                var root = new GameObject("PF_Resident_" + pose.Name);
                var body = Part("Body", bodySrc, root.transform, Vector3.zero, suit, accent, skin, hair, device);
                var pivot = new GameObject("HeadPivot").transform;
                pivot.SetParent(root.transform, false);
                pivot.localPosition = pose.Neck;
                var head = Part("Head", headSrc, pivot, -pose.Neck, suit, accent, skin, hair, device);

                var capsule = root.AddComponent<CapsuleCollider>();
                capsule.radius = 0.24f;
                capsule.height = pose.CapsuleHeight;
                capsule.center = new Vector3(0f, pose.CapsuleHeight * 0.5f, pose.Name == "Sit" ? 0.15f : 0f);
                var figure = root.AddComponent<ResidentFigure>();
                figure.EditorSet(body, head, pivot);

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "PF_Resident_" + pose.Name + ".prefab");
                Object.DestroyImmediate(root);
                figures[i] = prefab.GetComponent<ResidentFigure>();
            }

            var mode = Object.FindFirstObjectByType<InteriorMode>(FindObjectsInactive.Include);
            if (mode != null)
            {
                var so = new SerializedObject(mode);
                var list = so.FindProperty("_residentFigures");
                list.arraySize = figures.Length;
                for (int i = 0; i < figures.Length; i++)
                    list.GetArrayElementAtIndex(i).objectReferenceValue = figures[i];
                so.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(mode.gameObject.scene);
                EditorSceneManager.SaveScene(mode.gameObject.scene);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[ResidentFigureSetup] 인물 프리팹 {figures.Length}개, InteriorMode 연결 {(mode != null ? "함" : "못 찾음 (Main 씬을 열고 다시)")}");
        }

        private static Renderer Part(string name, Transform source, Transform parent, Vector3 offset,
            Material suit, Material accent, Material skin, Material hair, Material device)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = offset + source.localPosition;
            go.transform.localRotation = source.localRotation;
            go.transform.localScale = source.localScale;
            go.AddComponent<MeshFilter>().sharedMesh = source.GetComponent<MeshFilter>().sharedMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var slots = source.GetComponent<MeshRenderer>().sharedMaterials;
            var mats = new Material[slots.Length];
            for (int i = 0; i < slots.Length; i++)
            {
                string slot = slots[i] != null ? slots[i].name : "";
                mats[i] = slot == "Suit" ? suit : slot == "SuitAccent" ? accent : slot == "Skin" ? skin : slot == "Hair" ? hair : device;
            }
            r.sharedMaterials = mats;
            return r;
        }

        private static Material Mat(string name, float smoothness)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 0f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
