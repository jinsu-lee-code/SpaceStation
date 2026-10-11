using TMPro;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 11-17 ③ 주민 머리 위 말풍선: 둥근 판 + 아래 꼬리(코드로 만든 메시) + 글. 늘 카메라를 보고, 멀수록 조금 커져 읽힘.
    /// 원점 = 꼬리 끝 (머리 위 점에 둠). 판 색은 크림색, 테두리는 요청 종류 색.
    /// </summary>
    public sealed class SpeechBubble : MonoBehaviour
    {
        private const float Pad = 0.07f;
        private const float Radius = 0.08f;
        private const float Tail = 0.09f;
        private const float Border = 0.018f;
        private const float MaxWidth = 1.25f;
        private static readonly Color Paper = new Color(0.97f, 0.96f, 0.92f);
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private TextMeshPro _text;
        private MeshFilter _fill, _edge;
        private MeshRenderer _edgeRenderer;
        private MaterialPropertyBlock _block;
        private string _shown;

        public static SpeechBubble Create(Transform parent, TMP_FontAsset font, Material material)
        {
            var go = new GameObject("SpeechBubble");
            go.transform.SetParent(parent, false);
            var b = go.AddComponent<SpeechBubble>();
            b._block = new MaterialPropertyBlock();
            b._edge = Part(go.transform, "Edge", material, 0.003f, out b._edgeRenderer);
            b._fill = Part(go.transform, "Fill", material, 0f, out var fill);
            b._block.SetColor(BaseColorId, Paper);
            fill.SetPropertyBlock(b._block);

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            textGo.transform.localPosition = new Vector3(0f, 0f, -0.004f);
            b._text = textGo.AddComponent<TextMeshPro>();
            if (font != null)
                b._text.font = font;
            b._text.fontSize = 0.85f;
            b._text.alignment = TextAlignmentOptions.Center;
            b._text.textWrappingMode = TextWrappingModes.NoWrap;
            b._text.color = new Color(0.16f, 0.19f, 0.24f);
            b._text.rectTransform.pivot = new Vector2(0.5f, 0f);
            return b;
        }

        private static MeshFilter Part(Transform parent, string name, Material material, float z, out MeshRenderer renderer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0f, z);
            var mf = go.AddComponent<MeshFilter>();
            renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return mf;
        }

        /// <summary>글 바꾸기 (같으면 그대로). accent = 테두리 색.</summary>
        public void Set(string text, Color accent)
        {
            _block.Clear();
            _block.SetColor(BaseColorId, accent);
            _edgeRenderer.SetPropertyBlock(_block);
            if (text == _shown)
                return;
            _shown = text;
            _text.SetText(text);
            // 긴 대사(말동무)는 MaxWidth에서 줄바꿈 — 한 줄로 두면 말풍선이 화면을 가로지름
            _text.textWrappingMode = TextWrappingModes.NoWrap;
            var size = _text.GetPreferredValues(text);
            if (size.x > MaxWidth)
            {
                _text.textWrappingMode = TextWrappingModes.Normal;
                size = _text.GetPreferredValues(text, MaxWidth, 0f);
                size.x = Mathf.Min(size.x, MaxWidth);
            }
            float w = Mathf.Max(0.5f, size.x + Pad * 2f);
            float h = Mathf.Max(0.22f, size.y + Pad * 2f);
            _text.rectTransform.sizeDelta = new Vector2(size.x + 0.02f, size.y);
            _text.rectTransform.localPosition = new Vector3(0f, Tail + Pad, -0.004f);
            _fill.sharedMesh = Build(_fill.sharedMesh, w, h, 0f);
            _edge.sharedMesh = Build(_edge.sharedMesh, w, h, Border);
        }

        /// <summary>꼬리 끝을 <paramref name="tip"/>에, 눈을 보게. 멀수록 커짐(5m까지 1배, 최대 1.8배).</summary>
        public void Place(Vector3 tip, Transform eye)
        {
            float d = Vector3.Distance(tip, eye.position);
            transform.position = tip;
            transform.rotation = Quaternion.LookRotation(tip - eye.position, Vector3.up);
            transform.localScale = Vector3.one * Mathf.Clamp(d / 5f, 0.75f, 1.8f);
        }

        /// <summary>둥근 판(가로 w · 세로 h, 아래 끝 = 꼬리 높이) + 꼬리. grow = 테두리용으로 키우는 폭.</summary>
        private static Mesh Build(Mesh mesh, float w, float h, float grow)
        {
            if (mesh == null)
                mesh = new Mesh { name = "SpeechBubble" };
            mesh.Clear();
            const int seg = 6;
            float hw = w / 2f + grow, y0 = Tail - grow, y1 = Tail + h + grow, r = Radius + grow;
            var verts = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();
            var center = new Vector3(0f, (y0 + y1) / 2f, 0f);
            verts.Add(center);
            // 둘레 (반시계): 오른쪽 아래 → 오른쪽 위 → 왼쪽 위 → 왼쪽 아래 모서리, 아래 변 가운데에 꼬리
            var corners = new[]
            {
                (new Vector2(hw - r, y0 + r), -90f),
                (new Vector2(hw - r, y1 - r), 0f),
                (new Vector2(-hw + r, y1 - r), 90f),
                (new Vector2(-hw + r, y0 + r), 180f),
            };
            foreach (var (c, a0) in corners)
            {
                for (int i = 0; i <= seg; i++)
                {
                    float a = (a0 + 90f * i / seg) * Mathf.Deg2Rad;
                    verts.Add(new Vector3(c.x + Mathf.Cos(a) * r, c.y + Mathf.Sin(a) * r, 0f));
                }
            }
            // 아래 변: 왼쪽 아래 모서리 끝 → 꼬리 왼쪽 → 꼬리 끝 → 꼬리 오른쪽 → 처음(오른쪽 아래 모서리 시작)
            float tw = 0.06f + grow;
            verts.Add(new Vector3(-tw, y0, 0f));
            verts.Add(new Vector3(0f, grow > 0f ? -grow * 1.6f : 0f, 0f));
            verts.Add(new Vector3(tw, y0, 0f));
            int ring = verts.Count - 1;
            for (int i = 1; i <= ring; i++)
            {
                int j = i == ring ? 1 : i + 1;
                // 카메라 쪽(−z)에서 보이게: 시계 방향
                tris.Add(0);
                tris.Add(j);
                tris.Add(i);
            }
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
