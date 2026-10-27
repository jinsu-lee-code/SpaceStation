using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 구워 둔 정지 연출 지우기 (2026-10-27, 사용자 플레이 점검): Blender 모델에 멈춘 입자로 구워 둔 연출 가운데
    /// Unity가 움직이는 연출로 다시 그리는 곳을 가져올 때 지운다 (EvoFxDepthOffset이 겹침 고치기 전에 부름 — 규칙을 바꾸면
    /// 그쪽 GetVersion을 올린다). Blender 원본은 그대로 두어 미리보기 그림 · 다른 모델과 같은 틀을 유지.
    /// - 대형 토러스 열차 별 연기 (Fx_GrandTorusTrain의 반짝임 전부) → SparkEmitterFx 별빛 가루
    /// - 정련 콤비나트 돔 꼭대기 불꽃 분수 (Emblem_Fx의 반짝임 중 배출구 둘레 반지름 0.3 · 높이 0.36 위) → SparkEmitterFx 불꽃 분수
    /// - 드론 정비창 옥상 드론 무리 + 잇는 빛줄 (SpinDish_DroneDepotSwarm · Fx_DroneDepotSwarm 전부) → DroneShowFx
    /// </summary>
    public static class EvoBakedFxStrip
    {
        private struct Rule
        {
            public string Model;    // FBX 파일 이름 (확장자 없이)
            public string Object;   // 메시 오브젝트 이름
            public string Material; // 재질 이름 끝 ("TwinkleWhite" = FX_ · M_ 둘 다), null = 모든 재질
            public bool Region;     // true면 아래 범위 안 삼각형만 (메시 자기 기준 — Emblem_Fx는 모델 기준과 같음)
            public Vector2 Center;  // 가로 (x, z)
            public float Radius, MinY;
        }

        private static readonly Rule[] Rules =
        {
            new Rule { Model = "SM_Evo_GrandTorus", Object = "Fx_GrandTorusTrain", Material = "TwinkleWhite" },
            new Rule { Model = "SM_Evo_RefineryCombine", Object = "Emblem_Fx_RefineryCombine", Material = "TwinkleWhite", Region = true, Center = new Vector2(1f, 0.5f), Radius = 0.3f, MinY = 0.36f },
            new Rule { Model = "SM_Evo_DroneDepot", Object = "SpinDish_DroneDepotSwarm" },
            new Rule { Model = "SM_Evo_DroneDepot", Object = "Fx_DroneDepotSwarm" },
        };

        /// <summary>모델 이름에 맞는 규칙대로 삼각형을 지우고 지운 수를 돌려줌.</summary>
        public static int Strip(GameObject root, string modelName)
        {
            int removed = 0;
            foreach (var rule in Rules)
            {
                if (rule.Model != modelName)
                    continue;
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.name != rule.Object || mf.sharedMesh == null)
                        continue;
                    var mr = mf.GetComponent<MeshRenderer>();
                    var mats = mr != null ? mr.sharedMaterials : null;
                    removed += StripMesh(mf.sharedMesh, mats, rule);
                }
            }
            return removed;
        }

        private static int StripMesh(Mesh mesh, Material[] mats, Rule rule)
        {
            var verts = mesh.vertices;
            int removed = 0;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                if (rule.Material != null && (mats == null || s >= mats.Length || mats[s] == null || !mats[s].name.EndsWith(rule.Material)))
                    continue;
                var tris = mesh.GetTriangles(s);
                var keep = new List<int>(tris.Length);
                for (int i = 0; i + 2 < tris.Length; i += 3)
                {
                    if (Inside(rule, verts[tris[i]], verts[tris[i + 1]], verts[tris[i + 2]]))
                    {
                        removed++;
                        continue;
                    }
                    keep.Add(tris[i]);
                    keep.Add(tris[i + 1]);
                    keep.Add(tris[i + 2]);
                }
                if (keep.Count != tris.Length)
                    mesh.SetTriangles(keep, s, true);
            }
            return removed;
        }

        private static bool Inside(Rule rule, Vector3 a, Vector3 b, Vector3 c)
        {
            if (!rule.Region)
                return true;
            var m = (a + b + c) / 3f;
            return m.y >= rule.MinY && (new Vector2(m.x, m.z) - rule.Center).magnitude <= rule.Radius;
        }
    }
}
