using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 창고 모듈 모델 (5-5, 컨셉 이미지 기반). 메뉴 SpaceStation/Art/Rebuild Storage Model.
    /// 모따기 기둥 4개(갈색 캡·띠) + 패널 벽 + 팔레트 받침 + 지붕 통풍구.
    /// 앞면(+Z)은 열린 적재칸 — 안쪽에 대형 컨테이너 2개와 작은 상자 더미.
    /// 부품을 재질별로 합쳐 메시 하나(서브메시 Hull / HullDark / Accent)로 저장한다.
    /// 전체 크기는 셀(1)의 약 95% 안쪽 (±0.475).
    /// </summary>
    public static class StorageModelBuilder
    {
        private const string MeshFolder = "Assets/_Project/Art/Meshes";
        private const string MeshPath = MeshFolder + "/SM_Storage.asset";
        private const int Hull = 0, Dark = 1, Accent = 2;

        private const float Post = 0.385f;   // 모서리 기둥 중심 (±)
        private const float WallOut = 0.42f; // 벽 바깥면

        [MenuItem("SpaceStation/Art/Rebuild Storage Model")]
        public static void RebuildMenu() => StationArtBuilder.RebuildModule("Storage");

        private static Mesh _cube;
        private static Mesh _prism;
        private static List<CombineInstance>[] _parts;

        /// <summary>모델을 조립해 parent 아래 "Model" 자식으로 붙인다.</summary>
        public static GameObject Build(Transform parent, Material hull, Material hullDark, Material accent)
        {
            _cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            _prism = CreatePrism(0.2f);
            _parts = new[] { new List<CombineInstance>(), new List<CombineInstance>(), new List<CombineInstance>() };

            BuildBase();
            BuildPosts();
            BuildRoof();
            BuildWalls();
            BuildOpening();
            BuildCargo();

            var mesh = Combine();
            Object.DestroyImmediate(_prism);

            var go = new GameObject("Model");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { hull, hullDark, accent };
            return go;
        }

        // ---------- 부위 ----------

        private static void BuildBase()
        {
            // 팔레트 받침 + 기둥 아래 발
            Box(Dark, new Vector3(0f, -0.425f, 0f), new Vector3(0.8f, 0.05f, 0.8f));
            foreach (var c in Corners())
                Prism(Dark, new Vector3(c.x * Post, -0.45f, c.y * Post), new Vector3(0.17f, 0.06f, 0.17f));
            // 앞쪽 팔레트 턱 + 지게차 홈 느낌의 받침대
            Box(Dark, new Vector3(0f, -0.395f, 0.4f), new Vector3(0.6f, 0.03f, 0.05f));
            for (int i = -1; i <= 1; i += 2)
                Box(Dark, new Vector3(i * 0.19f, -0.455f, 0f), new Vector3(0.14f, 0.05f, 0.7f));
        }

        private static void BuildPosts()
        {
            foreach (var c in Corners())
            {
                float x = c.x * Post, z = c.y * Post;
                Prism(Hull, new Vector3(x, -0.01f, z), new Vector3(0.15f, 0.82f, 0.15f));
                Prism(Accent, new Vector3(x, -0.36f, z), new Vector3(0.175f, 0.12f, 0.175f)); // 아래 캡
                Prism(Accent, new Vector3(x, 0.01f, z), new Vector3(0.165f, 0.08f, 0.165f));  // 가운데 띠
                Prism(Accent, new Vector3(x, 0.4f, z), new Vector3(0.18f, 0.14f, 0.18f));     // 위 캡
                Box(Dark, new Vector3(x, 0.472f, z), new Vector3(0.08f, 0.008f, 0.08f));     // 캡 윗면 판
                // 바깥 두 면의 세로 슬릿
                float face = Post + 0.075f + 0.002f;
                foreach (float y in new[] { 0.19f, -0.18f })
                {
                    Box(Dark, new Vector3(c.x * face, y, z), new Vector3(0.006f, 0.14f, 0.035f));
                    Box(Dark, new Vector3(x, y, c.y * face), new Vector3(0.035f, 0.14f, 0.006f));
                }
            }
        }

        private static void BuildRoof()
        {
            Box(Hull, new Vector3(0f, 0.4f, 0f), new Vector3(0.66f, 0.09f, 0.66f));
            // 윗면 네 칸 판 (사이 홈이 보이게)
            for (int qx = -1; qx <= 1; qx += 2)
                for (int qz = -1; qz <= 1; qz += 2)
                    Box(Hull, new Vector3(qx * 0.165f, 0.45f, qz * 0.165f), new Vector3(0.3f, 0.012f, 0.3f));
            // 통풍구
            Box(Dark, new Vector3(0.04f, 0.4575f, -0.2f), new Vector3(0.26f, 0.004f, 0.05f));
            Box(Dark, new Vector3(0.2f, 0.4575f, 0.08f), new Vector3(0.05f, 0.004f, 0.13f));
            Box(Dark, new Vector3(-0.1f, 0.4575f, 0.2f), new Vector3(0.24f, 0.004f, 0.05f));
            // 윗 모서리 들보 (모따기)
            for (int s = -1; s <= 1; s += 2)
            {
                Prism(Hull, new Vector3(0f, 0.395f, s * Post), new Vector3(0.12f, 0.62f, 0.12f), Quaternion.Euler(0f, 0f, 90f));
                Prism(Hull, new Vector3(s * Post, 0.395f, 0f), new Vector3(0.12f, 0.62f, 0.12f), Quaternion.Euler(90f, 0f, 0f));
            }
        }

        private static void BuildWalls()
        {
            const float mid = WallOut - 0.02f;
            // 뒤(-Z), 좌우(±X). 앞(+Z)은 열린 적재칸
            Wall(new Vector3(0f, 0f, -1f), new Vector3(1f, 0f, 0f), mid);
            Wall(new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f), mid);
            Wall(new Vector3(-1f, 0f, 0f), new Vector3(0f, 0f, 1f), mid);
        }

        /// <summary>벽 한 면: 바탕판 + 위아래 돋을판 + 갈색 가로 띠.</summary>
        private static void Wall(Vector3 normal, Vector3 along, float mid)
        {
            Vector3 Size(float length, float height, float thick) => along * length + Vector3.up * height + Abs(normal) * thick;
            Box(Hull, normal * mid + Vector3.up * -0.03f, Size(0.62f, 0.74f, 0.04f));
            Box(Hull, normal * (WallOut + 0.004f) + Vector3.up * 0.195f, Size(0.54f, 0.25f, 0.008f));
            Box(Hull, normal * (WallOut + 0.004f) + Vector3.up * -0.215f, Size(0.54f, 0.33f, 0.008f));
            Box(Accent, normal * (WallOut + 0.005f) + Vector3.up * 0.01f, Size(0.62f, 0.07f, 0.02f));
            // 안쪽 어두운 내벽
            Box(Dark, normal * (WallOut - 0.045f) + Vector3.up * -0.03f, Size(0.62f, 0.74f, 0.01f));
        }

        private static void BuildOpening()
        {
            // 입구 위 갈색 띠 + 위 모서리 경사 보강재 + 천장 내벽
            Box(Accent, new Vector3(0f, 0.315f, 0.4f), new Vector3(0.62f, 0.04f, 0.05f));
            for (int s = -1; s <= 1; s += 2)
                Box(Hull, new Vector3(s * 0.285f, 0.275f, 0.4f), new Vector3(0.1f, 0.03f, 0.06f), Quaternion.Euler(0f, 0f, -s * 45f));
            Box(Dark, new Vector3(0f, 0.35f, 0f), new Vector3(0.76f, 0.01f, 0.76f));
        }

        private static void BuildCargo()
        {
            // 대형 컨테이너 2단 (+X 쪽)
            Container(new Vector3(0.18f, -0.245f, -0.06f));
            Container(new Vector3(0.18f, 0.085f, -0.06f));
            // 작은 상자 더미 (-X 쪽)
            Crate(new Vector3(-0.26f, -0.33f, 0.12f), new Vector3(0.13f, 0.14f, 0.16f));
            Crate(new Vector3(-0.1f, -0.325f, 0.1f), new Vector3(0.15f, 0.15f, 0.18f));
            Crate(new Vector3(-0.18f, -0.16f, 0.05f), new Vector3(0.3f, 0.18f, 0.24f));
            Crate(new Vector3(-0.17f, 0.03f, 0.02f), new Vector3(0.26f, 0.18f, 0.22f));
            Box(Dark, new Vector3(-0.2f, 0.2f, -0.22f), new Vector3(0.22f, 0.16f, 0.2f)); // 안쪽 그늘진 상자
        }

        /// <summary>골판 컨테이너 (앞면 기준 크기 0.34 x 0.31 x 0.6).</summary>
        private static void Container(Vector3 c)
        {
            const float w = 0.34f, h = 0.31f, d = 0.6f;
            float front = c.z + d * 0.5f;
            Box(Dark, c, new Vector3(w, h, d));
            for (int i = 0; i < 6; i++)
            {
                float x = c.x - 0.13f + i * 0.052f;
                Box(Dark, new Vector3(x, c.y, front + 0.006f), new Vector3(0.018f, h - 0.06f, 0.012f));
            }
            // 갈색 테두리 + 모서리 쇠
            Box(Accent, new Vector3(c.x, c.y + h * 0.5f - 0.0125f, front + 0.007f), new Vector3(w, 0.025f, 0.014f));
            Box(Accent, new Vector3(c.x, c.y - h * 0.5f + 0.0125f, front + 0.007f), new Vector3(w, 0.025f, 0.014f));
            for (int s = -1; s <= 1; s += 2)
            {
                Box(Accent, new Vector3(c.x + s * (w * 0.5f - 0.0125f), c.y, front + 0.007f), new Vector3(0.025f, h, 0.014f));
                Box(Accent, new Vector3(c.x + s * (w * 0.5f - 0.0125f), c.y + h * 0.5f - 0.0125f, c.z), new Vector3(0.027f, 0.027f, d));
                for (int t = -1; t <= 1; t += 2)
                    Box(Accent, new Vector3(c.x + s * (w * 0.5f - 0.015f), c.y + t * (h * 0.5f - 0.015f), front + 0.01f), new Vector3(0.04f, 0.04f, 0.02f));
            }
        }

        /// <summary>작은 상자: 밝은 몸체 + 8개 모서리 갈색 보호대 + 앞면 잠금판.</summary>
        private static void Crate(Vector3 c, Vector3 size)
        {
            Box(Hull, c, size);
            Vector3 half = size * 0.5f;
            const float g = 0.035f;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        Box(Accent, c + new Vector3(sx * (half.x - g * 0.4f), sy * (half.y - g * 0.4f), sz * (half.z - g * 0.4f)), Vector3.one * g);
            Box(Dark, c + new Vector3(0f, 0f, half.z + 0.003f), new Vector3(size.x * 0.28f, size.y * 0.22f, 0.006f));
        }

        // ---------- 도우미 ----------

        private static IEnumerable<Vector2> Corners()
        {
            yield return new Vector2(-1f, -1f);
            yield return new Vector2(1f, -1f);
            yield return new Vector2(-1f, 1f);
            yield return new Vector2(1f, 1f);
        }

        private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        private static void Box(int mat, Vector3 center, Vector3 size, Quaternion? rotation = null) =>
            _parts[mat].Add(new CombineInstance { mesh = _cube, transform = Matrix4x4.TRS(center, rotation ?? Quaternion.identity, size) });

        /// <summary>세로(Y) 모서리를 깎은 팔각 기둥. 회전하면 가로 들보로도 쓴다.</summary>
        private static void Prism(int mat, Vector3 center, Vector3 size, Quaternion? rotation = null) =>
            _parts[mat].Add(new CombineInstance { mesh = _prism, transform = Matrix4x4.TRS(center, rotation ?? Quaternion.identity, size) });

        /// <summary>1x1x1 팔각 기둥 메시 (chamfer = 한 변에서 깎는 비율). 면마다 정점을 나눠 평면 셰이딩.</summary>
        private static Mesh CreatePrism(float chamfer)
        {
            const float h = 0.5f;
            float c = chamfer;
            var ring = new[]
            {
                new Vector2(h, -h + c), new Vector2(h, h - c), new Vector2(h - c, h), new Vector2(-h + c, h),
                new Vector2(-h, h - c), new Vector2(-h, -h + c), new Vector2(-h + c, -h), new Vector2(h - c, -h),
            };
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            for (int i = 0; i < ring.Length; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Length];
                var a0 = new Vector3(a.x, -h, a.y);
                var b0 = new Vector3(b.x, -h, b.y);
                var a1 = new Vector3(a.x, h, a.y);
                var b1 = new Vector3(b.x, h, b.y);
                var normal = new Vector3(a.x + b.x, 0f, a.y + b.y).normalized;
                Tri(v, n, uv, t, a0, a1, b1, normal);
                Tri(v, n, uv, t, a0, b1, b0, normal);
            }
            for (int i = 1; i < ring.Length - 1; i++)
            {
                Tri(v, n, uv, t, new Vector3(ring[0].x, h, ring[0].y), new Vector3(ring[i].x, h, ring[i].y), new Vector3(ring[i + 1].x, h, ring[i + 1].y), Vector3.up);
                Tri(v, n, uv, t, new Vector3(ring[0].x, -h, ring[0].y), new Vector3(ring[i].x, -h, ring[i].y), new Vector3(ring[i + 1].x, -h, ring[i + 1].y), Vector3.down);
            }
            var mesh = new Mesh { name = "Prism" };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>삼각형을 normal 쪽이 앞면(시계 방향)이 되도록 추가.</summary>
        private static void Tri(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f)
                (b, c) = (c, b);
            int i = v.Count;
            v.Add(a);
            v.Add(b);
            v.Add(c);
            for (int k = 0; k < 3; k++)
                n.Add(normal);
            uv.Add(new Vector2(a.x + a.z, a.y));
            uv.Add(new Vector2(b.x + b.z, b.y));
            uv.Add(new Vector2(c.x + c.z, c.y));
            t.Add(i);
            t.Add(i + 1);
            t.Add(i + 2);
        }

        /// <summary>재질별로 합친 뒤 서브메시 3개짜리 메시로 저장 (기존 에셋이 있으면 내용만 교체해 GUID 유지).</summary>
        private static Mesh Combine()
        {
            var groups = new CombineInstance[_parts.Length];
            for (int i = 0; i < _parts.Length; i++)
            {
                var m = new Mesh();
                m.CombineMeshes(_parts[i].ToArray(), true, true);
                groups[i] = new CombineInstance { mesh = m, transform = Matrix4x4.identity };
            }
            var combined = new Mesh { name = "SM_Storage" };
            combined.CombineMeshes(groups, false, true);
            combined.RecalculateBounds();
            foreach (var g in groups)
                Object.DestroyImmediate(g.mesh);

            if (!AssetDatabase.IsValidFolder(MeshFolder))
                AssetDatabase.CreateFolder("Assets/_Project/Art", "Meshes");
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(combined, MeshPath);
                return combined;
            }
            EditorUtility.CopySerialized(combined, existing);
            existing.name = "SM_Storage";
            Object.DestroyImmediate(combined);
            EditorUtility.SetDirty(existing);
            return existing;
        }
    }
}
