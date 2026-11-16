using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Editor
{
    /// <summary>
    /// ⑤ 트림 UV 묶기 (2026-11-16): 내부 FBX (<c>Art/Models/Interior/SM_Interior_*</c> · <c>SM_InteriorKit*</c>)를 가져올 때
    /// Blender 트림 내보내기 (<c>BlenderWork/Tools/trim_uv_export.py</c>)가 넣은 UV1 (선 0 · 1) · UV2 (선 2 · 3) · UV3 (면 폭)를
    /// UV1 = 16비트 2칸 · UV2 = 16비트 4칸 (선 2 · 3 · 면 폭 · 0, 둘 다 둘째 꼭짓점 스트림)으로 묶고, 원래 UV가 없던 조각의 빈 UV0 (모두 0)는 뺌.
    /// 32비트 UV 네 층 그대로면 방 메시 메모리가 두 배가 됐음 (아콜로지 11.9 → 23.1 MB). 16비트 = 1 m 근처 0.5 mm 단위 (모서리 빛 1.6 cm에 충분).
    /// 트림 UV가 없는 FBX (UV1 없음)는 손대지 않음. 셰이더 <c>SpaceStation/InteriorTrim</c>이 TEXCOORD1 · TEXCOORD2를 읽음.
    /// </summary>
    public sealed class InteriorTrimImporter : AssetPostprocessor
    {
        private const string Folder = "Assets/_Project/Art/Models/Interior/";

        /// <summary>묶는 방식을 바꾸면 올림 — 내부 FBX를 다시 가져옴.</summary>
        public override uint GetVersion() => 3;

        private bool IsInterior
        {
            get
            {
                string p = assetPath.Replace('\\', '/');
                if (!p.StartsWith(Folder) || p.Substring(Folder.Length).Contains("/") || !p.EndsWith(".fbx"))
                    return false;
                string n = System.IO.Path.GetFileName(p);
                return n.StartsWith("SM_Interior_") || n.StartsWith("SM_InteriorKit");
            }
        }

        /// <summary>
        /// 접선은 만들지 않음 — 내부 FBX 재질에 노멀맵이 없고 (2026-11-16 245종 확인), 접선을 읽는 셰이더는 Foliage뿐 (잎 조각은 InteriorFoliage가 다시 계산).
        /// 꼭짓점마다 16바이트 — 트림 UV로 늘어난 메모리를 되돌림.
        /// </summary>
        private void OnPreprocessModel()
        {
            if (IsInterior)
                ((ModelImporter)assetImporter).importTangents = ModelImporterTangents.None;
        }

        private void OnPostprocessModel(GameObject root)
        {
            if (!IsInterior)
                return;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh != null)
                    Pack(mf.sharedMesh);
            }
        }

        /// <summary>트림 UV를 16비트로 묶음 (UV1 없으면 그대로). 묶었으면 true.</summary>
        public static bool Pack(Mesh m)
        {
            if (!m.HasVertexAttribute(VertexAttribute.TexCoord1))
                return false;
            int n = m.vertexCount;
            var uv0 = new List<Vector2>();
            var a = new List<Vector2>();
            var b = new List<Vector2>();
            var c = new List<Vector2>();
            m.GetUVs(0, uv0);
            m.GetUVs(1, a);
            m.GetUVs(2, b);
            m.GetUVs(3, c);
            bool keep0 = false;
            foreach (var u in uv0)
            {
                if (u.x != 0f || u.y != 0f)
                {
                    keep0 = true;
                    break;
                }
            }
            var pos = m.vertices;
            var nrm = m.normals;
            var tan = m.tangents;
            var col = m.colors;
            var subs = new List<int[]>();
            var topo = new List<MeshTopology>();
            for (int s = 0; s < m.subMeshCount; s++)
            {
                subs.Add(m.GetIndices(s));
                topo.Add(m.GetTopology(s));
            }
            // 원래 있던 칸은 원래 형식 그대로 (꼭짓점 색 = 8비트 등)
            VertexAttributeDescriptor Same(VertexAttribute at) => new VertexAttributeDescriptor(at, m.GetVertexAttributeFormat(at), m.GetVertexAttributeDimension(at));
            var attrs = new List<VertexAttributeDescriptor> { Same(VertexAttribute.Position) };
            if (nrm.Length == n)
                attrs.Add(Same(VertexAttribute.Normal));
            if (tan.Length == n)
                attrs.Add(Same(VertexAttribute.Tangent));
            if (col.Length == n)
                attrs.Add(Same(VertexAttribute.Color));
            if (keep0)
                attrs.Add(Same(VertexAttribute.TexCoord0));
            // 트림 값은 둘째 스트림에 16비트 그대로 (SetUVs는 형식을 32비트로 되돌림)
            attrs.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float16, 2, 1));
            attrs.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float16, 4, 1));
            var half = new ushort[n * 6];
            for (int i = 0; i < n; i++)
            {
                var ai = i < a.Count ? a[i] : Vector2.zero;
                var bi = i < b.Count ? b[i] : Vector2.zero;
                var ci = i < c.Count ? c[i] : Vector2.zero;
                half[i * 6] = Mathf.FloatToHalf(ai.x);
                half[i * 6 + 1] = Mathf.FloatToHalf(ai.y);
                half[i * 6 + 2] = Mathf.FloatToHalf(bi.x);
                half[i * 6 + 3] = Mathf.FloatToHalf(bi.y);
                half[i * 6 + 4] = Mathf.FloatToHalf(ci.x);
                half[i * 6 + 5] = 0;
            }
            m.SetVertexBufferParams(n, attrs.ToArray());
            m.SetVertices(pos);
            if (nrm.Length == n)
                m.SetNormals(nrm);
            if (tan.Length == n)
                m.SetTangents(tan);
            if (col.Length == n)
                m.SetColors(col);
            if (keep0)
                m.SetUVs(0, uv0);
            m.SetVertexBufferData(half, 0, 0, half.Length, 1);
            m.subMeshCount = subs.Count;
            for (int s = 0; s < subs.Count; s++)
                m.SetIndices(subs[s], topo[s], s, false);
            m.RecalculateBounds();
            return true;
        }
    }
}
