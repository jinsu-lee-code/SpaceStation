using System.Collections.Generic;
using SpaceStation.Data;
using SpaceStation.Interior;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 11-12 휴대 패드 연결 (메뉴 SpaceStation/Interior/Wire Pad): Blender `BlenderWork/Pad/pad_builder.py`가 내보낸
    /// `SM_InteriorPad.fbx`(재질 = FBX 색 그대로 URP Lit)를 InteriorMode의 패드 설정에 넣고 Main 씬 저장.
    /// 11-13: 홀로그램 모형용 모듈 메시(<see cref="PadHoloSet"/> — 면 + 각진 모서리 선) 굽기, 홀로그램 재질(URP Particles/Unlit 가산),
    /// 홀로그램 테마 아트(<see cref="HoloArtBuilder"/>)도 함께 만들어 연결.
    /// </summary>
    public static class InteriorPadSetup
    {
        private const string GameScene = "Assets/_Project/Scenes/Main.unity";
        private const string ModelPath = "Assets/_Project/Art/Models/Interior/SM_InteriorPad.fbx";
        private const string HoloSetPath = "Assets/_Project/Art/Models/Interior/PadHoloSet.asset";
        private const string MaterialFolder = "Assets/_Project/Art/Materials/Interior";
        private const float SharpAngle = 38f;
        private const float MinEdge = 0.035f; // 칸 = 1 기준. 볼트 · 작은 홈은 작은 모형에서 잡음

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
            var holoSet = BakeHoloSet(out string bakeLog);
            var fill = HoloMaterial("M_PadHoloFill", additive: true);
            var line = HoloMaterial("M_PadHoloLine", additive: false); // 가산이면 밝은 방 벽 앞에서 하얗게 타서 분류 색이 안 보임
            var art = HoloArtBuilder.Build();

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
            var tuning = so.FindProperty("_padTuning");
            tuning.FindPropertyRelative("Model").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            tuning.FindPropertyRelative("Holo").objectReferenceValue = holoSet;
            tuning.FindPropertyRelative("HoloFill").objectReferenceValue = fill;
            tuning.FindPropertyRelative("HoloLine").objectReferenceValue = line;
            var font = so.FindProperty("_font").objectReferenceValue as TMPro.TMP_FontAsset;
            if (font != null && art.Font != font)
            {
                art.Font = font;
                EditorUtility.SetDirty(art);
                AssetDatabase.SaveAssets();
            }
            so.FindProperty("_holoArt").objectReferenceValue = art;
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[InteriorPadSetup] 패드 모델 · 홀로그램 모형 · 테마 아트 연결\n" + bakeLog);
        }

        /// <summary>모듈마다 프리팹 메시를 원점 기준 한 메시(면)로 합치고 각진 모서리 선 메시를 만들어 한 에셋에 묶는다.</summary>
        public static PadHoloSet BakeHoloSet(out string log)
        {
            var set = AssetDatabase.LoadAssetAtPath<PadHoloSet>(HoloSetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<PadHoloSet>();
                AssetDatabase.CreateAsset(set, HoloSetPath);
            }
            foreach (var sub in AssetDatabase.LoadAllAssetRepresentationsAtPath(HoloSetPath))
            {
                if (sub is Mesh)
                    Object.DestroyImmediate(sub, true);
            }
            var entries = new List<PadHoloSet.Entry>();
            var lines = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:ModuleData", new[] { "Assets/_Project/Data/Modules" }))
            {
                var data = AssetDatabase.LoadAssetAtPath<ModuleData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null || data.Prefab == null)
                    continue;
                var root = data.Prefab.transform;
                var combine = new List<CombineInstance>();
                var segments = new List<Vector3>();
                int verts = 0;
                foreach (var mf in data.Prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mesh = mf.sharedMesh;
                    if (mesh == null || mf.GetComponent<MeshRenderer>() == null)
                        continue;
                    var m = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                    for (int s = 0; s < mesh.subMeshCount; s++)
                        combine.Add(new CombineInstance { mesh = mesh, subMeshIndex = s, transform = m });
                    var local = new List<Vector3>();
                    HoloEdges.Extract(mesh.vertices, mesh.triangles, SharpAngle, MinEdge, local);
                    foreach (var p in local)
                        segments.Add(m.MultiplyPoint3x4(p));
                    verts += mesh.vertexCount;
                }
                if (combine.Count == 0)
                    continue;
                string key = data.name.Replace("MD_", "");
                var fill = new Mesh { name = "HoloFill_" + key, indexFormat = verts > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                fill.CombineMeshes(combine.ToArray(), true, true);
                Tint(fill);
                var edge = new Mesh { name = "HoloLines_" + key, indexFormat = segments.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                edge.SetVertices(segments);
                var idx = new int[segments.Count];
                for (int i = 0; i < idx.Length; i++)
                    idx[i] = i;
                edge.SetIndices(idx, MeshTopology.Lines, 0);
                Tint(edge);
                edge.RecalculateBounds();
                AssetDatabase.AddObjectToAsset(fill, set);
                AssetDatabase.AddObjectToAsset(edge, set);
                entries.Add(new PadHoloSet.Entry { Module = data, Fill = fill, Lines = edge });
                lines.Add($"{key}: 면 {fill.vertexCount} · 선 {segments.Count / 2}");
            }
            set.EditorSet(entries);
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            log = string.Join("\n", lines);
            return set;
        }

        /// <summary>정점 색 = 흰색, 알파는 아래 0.55 → 위 1 (투사기에서 멀수록 밝은 홀로그램). 원래 정점 색(FBX)은 무시.</summary>
        private static void Tint(Mesh mesh)
        {
            var v = mesh.vertices;
            var b = mesh.bounds;
            var colors = new Color[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                float h = b.size.y > 1e-4f ? (v[i].y - b.min.y) / b.size.y : 1f;
                colors[i] = new Color(1f, 1f, 1f, Mathf.Lerp(0.55f, 1f, h));
            }
            mesh.colors = colors;
            mesh.uv = null;
            mesh.uv2 = null;
        }

        /// <summary>
        /// 홀로그램 재질: URP Particles/Unlit 투명(깊이 안 씀 · 양면) — 면 = 가산(겹칠수록 밝음), 선 · 배경판 = 반투명 섞기.
        /// 색 · 세기는 렌더러마다 MaterialPropertyBlock(_BaseColor), 정점 색(알파)과 곱해짐.
        /// </summary>
        private static Material HoloMaterial(string name, bool additive)
        {
            if (!AssetDatabase.IsValidFolder(MaterialFolder))
                AssetDatabase.CreateFolder("Assets/_Project/Art/Materials", "Interior");
            string path = $"{MaterialFolder}/{name}.mat";
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            m.SetTexture("_BaseMap", null);
            m.SetFloat("_Surface", 1f);   // Transparent
            m.SetFloat("_Blend", additive ? 2f : 0f); // Additive · Alpha
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.SetKeyword(new LocalKeyword(m.shader, "_SURFACE_TYPE_TRANSPARENT"), true);
            m.renderQueue = (int)RenderQueue.Transparent + 50; // 방 유리 · 연기보다 나중 (패드 위에 뜸)
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetColor("_BaseColor", new Color(0.31f, 0.85f, 1f, 0.5f));
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
