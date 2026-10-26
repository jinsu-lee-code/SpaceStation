using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Building
{
    /// <summary>
    /// 진화 실드 돔(오로라 등대) 실드 연출 (사용자 2026-10-26): 운석을 빗겨낼 때 구면 파문 대신 닿은 자리에 오로라 커튼 장벽이 일렁이며 막는다.
    /// MeteorFx가 실드 모듈에서 이 컴포넌트를 찾으면 파문 대신 Play를 부른다 (StationArtBuilder가 실드 돔 발생기에 자동 배선).
    /// 커튼 = 실드 구면에 붙은 조각 메시: 아래 밝은 단(흰빛 초록) → 초록 → 위로 보라로 옅어짐 · 세로 빛살 · 가로로 흐르는 주름,
    /// 닿은 자리가 잠깐 번쩍. 꼭짓점 색을 곱하는 가산 재질(M_AuroraBarrier) — 전체 세기 · 페이드는 _BaseColor로.
    /// 시간은 게임 시간(Time.deltaTime)을 따른다.
    /// </summary>
    public sealed class AuroraBarrierFx : MonoBehaviour
    {
        public const int Columns = 48;
        public const int Rows = 12;

        [SerializeField] private Material _material;
        [SerializeField] private float _duration = 1.6f;
        [Tooltip("커튼 폭 = 실드 반지름 × 이 비율 (최대 _maxWidth)")]
        [SerializeField] private float _widthRatio = 0.75f;
        [SerializeField] private float _maxWidth = 6f;
        [Tooltip("커튼 높이 = 폭 × 이 비율")]
        [SerializeField] private float _heightRatio = 0.55f;
        [Tooltip("주름 깊이 (구면 바깥 · 안쪽으로 일렁이는 폭)")]
        [SerializeField] private float _foldDepth = 0.28f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // 아래 밝은 단 → 초록 → 위 보라 (꼭짓점 색은 0~1, 밝기는 재질 HDR 색으로)
        private static readonly Color Hem = new Color(0.55f, 1f, 0.72f);
        private static readonly Color Green = new Color(0.14f, 0.8f, 0.38f);
        private static readonly Color Violet = new Color(0.5f, 0.2f, 0.85f);

        /// <summary>실드 구면(center, radius)의 contact 자리에 장벽을 한 번 펼친다.</summary>
        public void Play(Vector3 center, float radius, Vector3 contact)
        {
            if (_material != null && isActiveAndEnabled)
                StartCoroutine(Run(center, radius, contact));
        }

        private IEnumerator Run(Vector3 center, float radius, Vector3 contact)
        {
            // 모듈 아래에 두어 모듈이 사라지면 함께 사라짐 (월드 위치 · 회전 고정, 꼭짓점은 contact 기준)
            var go = new GameObject("AuroraBarrier");
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(contact, Quaternion.identity);
            var mesh = new Mesh { name = "AuroraBarrier" };
            mesh.MarkDynamic();
            var verts = new Vector3[(Columns + 1) * (Rows + 1)];
            var colors = new Color[verts.Length];
            float width = Mathf.Min(_maxWidth, radius * _widthRatio);
            float height = width * _heightRatio;
            Shape(verts, colors, center, radius, contact, width, height, 0f, 0f, 1f);
            mesh.vertices = verts;
            mesh.colors = colors;
            mesh.triangles = Triangles();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            var block = new MaterialPropertyBlock();
            Color tint = _material.GetColor(BaseColorId);

            float t = 0f;
            while (t < _duration && go != null)
            {
                t += Time.deltaTime;
                float k = t / _duration;
                float rise = Smooth(0f, 0.12f, t);                      // 확 펼쳐짐
                float fade = 1f - Smooth(0.35f, 1f, k);                 // 일렁이며 사라짐
                float flash = Mathf.Exp(-t * 5f);                       // 닿은 자리 번쩍
                Shape(verts, colors, center, radius, contact, width, height * Mathf.Lerp(0.6f, 1f, rise), _foldDepth * (0.5f + 0.5f * rise), t, flash);
                mesh.vertices = verts;
                mesh.colors = colors;
                mesh.RecalculateBounds();
                block.SetColor(BaseColorId, new Color(tint.r, tint.g, tint.b, tint.a * rise * fade));
                r.SetPropertyBlock(block);
                yield return null;
            }
            Destroy(go);
            Destroy(mesh);
        }

        /// <summary>a → b 구간에서 0 → 1로 부드럽게 (Mathf.SmoothStep은 값 보간이라 구간 경계용으로 따로).</summary>
        private static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

        private static int[] Triangles()
        {
            var tris = new int[Columns * Rows * 6];
            int n = 0;
            for (int j = 0; j < Rows; j++)
            {
                for (int i = 0; i < Columns; i++)
                {
                    int a = j * (Columns + 1) + i, b = a + 1, c = a + Columns + 1, d = c + 1;
                    tris[n++] = a; tris[n++] = c; tris[n++] = b;
                    tris[n++] = b; tris[n++] = c; tris[n++] = d;
                }
            }
            return tris;
        }

        /// <summary>
        /// 커튼 꼭짓점(contact 기준 상대 위치) · 색 (테스트에서 직접 호출). 구면 접평면 좌표 (x 가로, y 위)를 구면에 투영하고
        /// 주름만큼 바깥 · 안쪽으로 밈. 위로 갈수록 크게 펄럭이고 옅어지며, 양옆 끝 · 맨 위는 투명.
        /// </summary>
        public static void Shape(Vector3[] verts, Color[] colors, Vector3 center, float radius, Vector3 contact,
            float width, float height, float fold, float time, float flash)
        {
            Vector3 n = (contact - center).normalized;
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, n);
            if (up.sqrMagnitude < 0.01f)
                up = Vector3.ProjectOnPlane(Vector3.forward, n); // 바로 위에서 맞음
            up.Normalize();
            Vector3 right = Vector3.Cross(up, n);
            for (int j = 0; j <= Rows; j++)
            {
                float v = (float)j / Rows; // 0 = 아래 밝은 단, 1 = 위
                float y = Mathf.Lerp(-0.3f, 0.7f, v) * height;
                for (int i = 0; i <= Columns; i++)
                {
                    float u = (float)i / Columns;
                    float x = (u - 0.5f) * width;
                    float wave = Mathf.Sin(x * 2.1f + time * 2.6f) * 0.6f + Mathf.Sin(x * 4.7f - time * 3.9f + v * 2f) * 0.4f;
                    float sway = wave * fold * 0.6f * v;
                    Vector3 dir = (n * radius + right * (x + sway) + up * y).normalized;
                    verts[j * (Columns + 1) + i] = center + dir * (radius + wave * fold * (0.3f + 0.7f * v)) - contact;

                    Color c = v < 0.12f ? Color.Lerp(Hem, Green, v / 0.12f) : Color.Lerp(Green, Violet, Smooth(0.12f, 1f, v));
                    float edge = Smooth(0f, 0.2f, Mathf.Min(u, 1f - u));
                    float top = 1f - Smooth(0.5f, 1f, v);
                    float rays = 0.4f + 0.6f * Mathf.Abs(Mathf.Sin(x * 6.3f + Mathf.Sin(x * 1.7f + time) * 1.5f)); // 세로 빛살
                    float hit = flash * Mathf.Exp(-(x * x + y * y) / (0.04f * width * width));
                    float a = Mathf.Clamp01(edge * top * rays + hit * 0.6f * edge);
                    colors[j * (Columns + 1) + i] = new Color(Mathf.Min(1f, c.r + hit * 0.45f), Mathf.Min(1f, c.g + hit * 0.3f), Mathf.Min(1f, c.b + hit * 0.45f), a);
                }
            }
        }
    }
}
