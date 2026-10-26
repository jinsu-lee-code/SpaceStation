using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Building
{
    /// <summary>
    /// 진화 실드 돔(오로라 등대) 실드 연출 (사용자 2026-10-26): 운석을 빗겨낼 때 기존 실드의 파란 구면 파문 대신 오로라 구면 파문 —
    /// 실드 구 전체가 기존처럼 살짝 부풀며 빛나다 사라지는데, 닿은 자리에서 시작해 구면을 따라 퍼지고(퍼지는 앞 가장자리가 밝음),
    /// 구를 가로로 감싸 물결치며 흐르는 오로라 띠 세 겹(청록 · 보라 · 하늘 — 아래 가장자리가 또렷하고 위로 길게 옅어짐)이 보인다.
    /// 기존 실드 막처럼 테두리(윤곽)가 밝고 가운데는 비쳐 정거장이 가려지지 않음, 닿은 자리는 잠깐 번쩍.
    /// 시안 경과: 구면 조각 위 세로 빛살 커튼 → "잡초 느낌" · 평면 원으로 퍼지는 조각 → "진짜 원" → 기존 실드처럼 구 전체로.
    /// MeteorFx가 실드 모듈에서 이 컴포넌트를 찾으면 파란 파문 대신 Play (StationArtBuilder가 실드 돔 발생기에 자동 배선).
    /// 꼭짓점 색을 곱하는 가산 재질(M_AuroraBarrier) — 색 · 알파는 매 프레임 꼭짓점 색으로(테두리는 카메라 방향에 따라), 전체 세기는 _BaseColor.
    /// 시간은 게임 시간(Time.deltaTime)을 따른다.
    /// </summary>
    public sealed class AuroraBarrierFx : MonoBehaviour
    {
        public const int Rings = 36;     // 위도 줄
        public const int Segments = 72;  // 경도 칸

        [SerializeField] private Material _material;
        [SerializeField] private float _duration = 1.6f;
        [Tooltip("닿은 자리에서 반대편까지 퍼지는 시간")]
        [SerializeField] private float _spreadTime = 0.9f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // 꼭짓점 색은 0~1 (밝기는 재질 HDR 색으로)
        private static readonly Color Shell = new Color(0.35f, 0.8f, 0.9f);    // 실드 막 윤곽
        private static readonly Color Front = new Color(0.75f, 0.97f, 1f);     // 퍼지는 앞 가장자리
        private static readonly Color Teal = new Color(0.3f, 1f, 0.7f);
        private static readonly Color Violet = new Color(0.62f, 0.3f, 1f);
        private static readonly Color Sky = new Color(0.45f, 0.85f, 1f);

        /// <summary>오로라 띠 하나: 높이(구 위 y, −1~1) · 둘레를 도는 물결(정수 번) · 아래 / 위 두께 · 색 · 세기.</summary>
        private struct Band
        {
            public float Offset, Amp, Speed, Phase, Below, Above, Weight;
            public int Waves;
            public Color Color;
        }

        private static readonly Band[] Bands =
        {
            new Band { Offset = -0.05f, Amp = 0.14f, Waves = 3, Speed = 0.9f, Phase = 0f, Below = 0.035f, Above = 0.15f, Weight = 1f, Color = Teal },
            new Band { Offset = 0.36f, Amp = 0.1f, Waves = 2, Speed = -0.7f, Phase = 1.7f, Below = 0.035f, Above = 0.12f, Weight = 0.9f, Color = Violet },
            new Band { Offset = -0.38f, Amp = 0.08f, Waves = 4, Speed = 1.2f, Phase = 0.6f, Below = 0.025f, Above = 0.07f, Weight = 0.7f, Color = Sky },
        };

        /// <summary>실드 구면(center, radius)의 contact 자리에서 오로라 파문을 한 번 퍼뜨린다.</summary>
        public void Play(Vector3 center, float radius, Vector3 contact)
        {
            if (_material != null && isActiveAndEnabled)
                StartCoroutine(Run(center, radius, contact));
        }

        private IEnumerator Run(Vector3 center, float radius, Vector3 contact)
        {
            // 모듈 아래에 두어 모듈이 사라지면 함께 사라짐 (월드 위치 · 회전 고정 — 꼭짓점 = 단위 구, 크기는 transform으로)
            var go = new GameObject("AuroraBarrier");
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(center, Quaternion.identity);
            var unit = SphereVertices();
            var colors = new Color[unit.Length];
            var mesh = new Mesh { name = "AuroraBarrier" };
            mesh.MarkDynamic();
            mesh.vertices = unit;
            mesh.triangles = SphereTriangles();
            mesh.colors = colors;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            var block = new MaterialPropertyBlock();
            Color tint = _material.GetColor(BaseColorId);
            Vector3 hitDir = (contact - center).normalized;

            float t = 0f;
            while (t < _duration && go != null)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / _duration);
                float scale = radius * Mathf.Lerp(0.96f, 1.04f, k);   // 기존 실드 파문처럼 살짝 부풂
                go.transform.localScale = Vector3.one * scale;
                float spread = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / _spreadTime), 1.5f);
                var cam = Camera.main;
                Vector3 eye = cam != null ? cam.transform.position : center + hitDir * radius * 3f;
                Paint(colors, unit, center, scale, hitDir, eye, t, spread, Mathf.Exp(-t * 5f));
                mesh.colors = colors;
                float fade = 1f - Smooth(0.4f, 1f, k);
                block.SetColor(BaseColorId, new Color(tint.r, tint.g, tint.b, tint.a * Smooth(0f, 0.08f, t) * fade));
                r.SetPropertyBlock(block);
                yield return null;
            }
            Destroy(go);
            Destroy(mesh);
        }

        /// <summary>단위 구 꼭짓점 (위도 Rings 줄 × 경도 Segments + 1 칸 — 이음매 칸은 겹침).</summary>
        public static Vector3[] SphereVertices()
        {
            var v = new Vector3[(Rings + 1) * (Segments + 1)];
            for (int j = 0; j <= Rings; j++)
            {
                float lat = Mathf.PI * j / Rings - Mathf.PI * 0.5f;
                for (int i = 0; i <= Segments; i++)
                {
                    float lon = 2f * Mathf.PI * i / Segments;
                    v[j * (Segments + 1) + i] = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                }
            }
            return v;
        }

        private static int[] SphereTriangles()
        {
            var tris = new int[Rings * Segments * 6];
            int n = 0;
            for (int j = 0; j < Rings; j++)
            {
                for (int i = 0; i < Segments; i++)
                {
                    int a = j * (Segments + 1) + i, b = a + 1, c = a + Segments + 1, d = c + 1;
                    tris[n++] = a; tris[n++] = c; tris[n++] = b;
                    tris[n++] = b; tris[n++] = c; tris[n++] = d;
                }
            }
            return tris;
        }

        /// <summary>
        /// 꼭짓점 색 · 알파 (테스트에서 직접 호출). unit = 단위 구 꼭짓점, radius = 지금 크기, hitDir = 중심 → 닿은 자리, eye = 카메라 위치,
        /// spread 0 → 1 = 닿은 자리에서 반대편까지 퍼진 정도, flash = 닿은 자리 번쩍 세기.
        /// 앞면(카메라 쪽)만 보이고 윤곽이 밝음(기존 실드 막과 같은 테두리 빛), 아직 안 퍼진 자리는 투명.
        /// </summary>
        public static void Paint(Color[] colors, Vector3[] unit, Vector3 center, float radius, Vector3 hitDir, Vector3 eye,
            float time, float spread, float flash)
        {
            float frontFade = 1f - Smooth(0.85f, 1f, spread); // 다 퍼지면 앞 가장자리는 사라짐
            for (int q = 0; q < unit.Length; q++)
            {
                Vector3 n = unit[q];
                Vector3 toEye = (eye - (center + n * radius)).normalized;
                float facing = Vector3.Dot(n, toEye);
                float front = Smooth(-0.12f, 0.05f, facing);                 // 뒷면은 감춤 (윤곽 근처만 이어짐)
                float rim = Mathf.Pow(1f - Mathf.Clamp01(facing), 2.5f);     // 윤곽이 밝음 (기존 실드 파문처럼)

                float ang = Mathf.Acos(Mathf.Clamp(Vector3.Dot(n, hitDir), -1f, 1f)) / Mathf.PI; // 0 닿은 자리 ~ 1 반대편
                float inside = 1f - Smooth(spread - 0.1f, spread + 0.01f, ang);
                float wd = (ang - spread) / 0.04f;
                float wave = Mathf.Exp(-wd * wd) * frontFade * (0.35f + 0.65f * rim); // 퍼지는 앞 가장자리 (가운데에서는 옅게)

                // 구를 가로로 감싸 흐르는 오로라 띠
                float lon = Mathf.Atan2(n.z, n.x);
                float sy = n.y;
                Color sum = Shell * (0.02f + 0.6f * rim);
                float weight = 0.02f + 0.6f * rim, glow = weight;
                float face = 0.15f + 0.85f * rim; // 가운데는 옅게 (정거장이 비쳐 보이게)
                foreach (var b in Bands)
                {
                    float line = b.Offset + b.Amp * Mathf.Sin(lon * b.Waves + time * b.Speed + b.Phase);
                    float d = sy - line;
                    float k = d < 0f ? Mathf.Exp(-(d * d) / (b.Below * b.Below)) : Mathf.Exp(-(d * d) / (b.Above * b.Above));
                    if (d > 0f)
                        k *= 0.8f + 0.2f * Mathf.Abs(Mathf.Sin(lon * 24f + b.Phase + time * 0.6f)); // 위쪽 옅은 빛살 결
                    k *= b.Weight * face;
                    sum += b.Color * k;
                    weight += k;
                    glow += k;
                }
                float hit = flash * Mathf.Exp(-(1f - Vector3.Dot(n, hitDir)) * 10f);
                sum = sum * inside + Front * wave;
                weight = weight * inside + wave;
                glow = glow * inside + wave + hit * 0.8f;
                Color c = weight > 1e-4f ? sum * (1f / weight) : Shell;
                c = Color.Lerp(c, Color.white, Mathf.Clamp01(hit) * 0.6f);
                colors[q] = new Color(c.r, c.g, c.b, Mathf.Clamp01(glow) * front);
            }
        }

        /// <summary>a → b 구간에서 0 → 1로 부드럽게 (Mathf.SmoothStep은 값 보간이라 구간 경계용으로 따로).</summary>
        private static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));
    }
}
