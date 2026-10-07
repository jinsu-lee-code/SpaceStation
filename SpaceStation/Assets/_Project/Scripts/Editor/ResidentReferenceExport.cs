using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 11-11b 주민 모델 레퍼런스 내보내기 (메뉴 SpaceStation/Interior/Export Resident Reference).
    /// 에셋 스토어 원본(LuceedStudio Little Guys Tall 2종)의 형태 · 본 위치를 <c>BlenderWork/_ref/</c>에 텍스트로 쓴다
    /// (v 정점 / f 삼각형 / b 본 이름 부모 x y z, Unity 좌표). Blender 텍스트 블록 <c>resident2_ref</c>의 <c>load_refs()</c>가 읽는다.
    /// 원본 형태라 공개 저장소에 올리지 않는다 (<c>BlenderWork/_ref/</c>는 .gitignore).
    /// </summary>
    public static class ResidentReferenceExport
    {
        private const string Root = "Assets/LuceedStudio/Character Lab/Little Guys/Little Guys - Free Sample/";

        private static readonly string[] Prefabs =
        {
            Root + "Man/Free Man/Free Man Tall.prefab",
            Root + "Woman/Free Woman/Free Woman Tall.prefab",
        };

        private static readonly string[] Names = { "ManTall", "WomanTall" };

        [MenuItem("SpaceStation/Interior/Export Resident Reference")]
        public static void Export()
        {
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../BlenderWork/_ref"));
            Directory.CreateDirectory(outDir);
            var ci = CultureInfo.InvariantCulture;
            for (int k = 0; k < Prefabs.Length; k++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs[k]);
                if (prefab == null)
                {
                    Debug.LogError("[ResidentReferenceExport] 원본 없음 (에셋 스토어에서 Little Guys - Free Sample을 받아 넣기): " + Prefabs[k]);
                    return;
                }
                var go = Object.Instantiate(prefab);
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
                var mesh = new UnityEngine.Mesh();
                smr.BakeMesh(mesh, false);
                var sb = new StringBuilder();
                foreach (var v in mesh.vertices)
                {
                    var w = smr.transform.TransformPoint(v);
                    sb.AppendFormat(ci, "v {0:F5} {1:F5} {2:F5}\n", w.x, w.y, w.z);
                }
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var t = mesh.GetTriangles(s);
                    for (int i = 0; i < t.Length; i += 3)
                        sb.AppendFormat("f {0} {1} {2}\n", t[i], t[i + 1], t[i + 2]);
                }
                foreach (var b in smr.bones)
                {
                    var p = b.position;
                    string parent = b.parent != null ? b.parent.name : "-";
                    sb.AppendFormat(ci, "b {0} {1} {2:F5} {3:F5} {4:F5}\n", b.name.Replace(' ', '_'), parent.Replace(' ', '_'), p.x, p.y, p.z);
                }
                File.WriteAllText(Path.Combine(outDir, Names[k] + ".txt"), sb.ToString());
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(go);
            }
            Debug.Log("[ResidentReferenceExport] 레퍼런스 2개 → " + outDir);
        }
    }
}
