using System;
using System.Collections.Generic;
using System.IO;
using SpaceStation.Data;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 5-8 SF 홀로그램 HUD 아트 생성 (메뉴 SpaceStation/UI/Build Hud Art). 모두 코드로 그려 외부 에셋이 필요 없다.
    /// - 패널: UI_HoloFill(모서리 깎인 반투명 바탕, 색은 Image.color) + UI_HoloFrame(테두리 + 모서리 브래킷)
    /// - 버튼: UI_HoloButton(대각 모서리 + 테두리, 색은 Image.color)
    /// - 아이콘: 벡터 도형 아틀라스 → TMP 스프라이트 에셋(HUD_Icons), TMP 기본 스프라이트 에셋으로 등록 → 모든 텍스트에서 &lt;sprite name=..&gt;
    /// - 모듈 썸네일: 프리팹을 임시 카메라로 렌더링 → ModuleData.Icon
    /// </summary>
    public static class HudArtBuilder
    {
        private const string Root = "Assets/_Project/Art/UI";
        private const string ThumbDir = Root + "/Thumbs";
        private const int IconSize = 64;

        [MenuItem("SpaceStation/UI/Build Hud Art")]
        public static void BuildAll()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(ThumbDir);
            BuildPanelSprites();
            BuildIcons();
            BuildThumbnails();
            AssetDatabase.SaveAssets();
            Debug.Log("[HudArtBuilder] 패널·버튼 스프라이트, 아이콘, 썸네일 생성 완료");
        }

        // ---------------- 패널 / 버튼 ----------------

        public static Sprite Fill => AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/UI_HoloFill.png");
        public static Sprite Frame => AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/UI_HoloFrame.png");
        public static Sprite Button => AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/UI_HoloButton.png");

        private static void BuildPanelSprites()
        {
            const int s = 64, cut = 9;
            var all = new[] { true, true, true, true }; // BL, BR, TR, TL
            Write("UI_HoloFill.png", s, s, (x, y) =>
            {
                float sd = Chamfer(x, y, s, s, cut, all);
                if (sd > 0f) return Color.clear;
                float a = Mathf.Lerp(0.86f, 1f, y / s); // 위가 조금 밝게
                return new Color(1f, 1f, 1f, a);
            }, 16);
            Write("UI_HoloFrame.png", s, s, (x, y) =>
            {
                float sd = Chamfer(x, y, s, s, cut, all);
                if (sd > 0f || sd < -2.6f) return Color.clear;
                bool corner = (Mathf.Min(x, s - x) < 18f && Mathf.Min(y, s - y) < 18f);
                if (corner) return Color.white;            // 모서리 브래킷 (밝고 굵게)
                return sd > -1.3f ? new Color(1f, 1f, 1f, 0.45f) : Color.clear; // 가는 테두리
            }, 18);
            const int b = 48, bc = 8;
            var diag = new[] { false, true, false, true }; // BR, TL만 깎음
            Write("UI_HoloButton.png", b, b, (x, y) =>
            {
                float sd = Chamfer(x, y, b, b, bc, diag);
                if (sd > 0f) return Color.clear;
                if (sd > -1.6f) return Color.white;                        // 테두리
                return new Color(1f, 1f, 1f, Mathf.Lerp(0.42f, 0.6f, y / b)); // 바탕
            }, 12);
        }

        /// <summary>모서리를 깎은 사각형의 부호 거리 (안쪽 음수). cuts = BL, BR, TR, TL.</summary>
        private static float Chamfer(float x, float y, float w, float h, float c, bool[] cuts)
        {
            float sd = Mathf.Max(Mathf.Max(-x, x - w), Mathf.Max(-y, y - h));
            const float k = 0.70710678f;
            if (cuts[0]) sd = Mathf.Max(sd, (c - x - y) * k);
            if (cuts[1]) sd = Mathf.Max(sd, (c - (w - x) - y) * k);
            if (cuts[2]) sd = Mathf.Max(sd, (c - (w - x) - (h - y)) * k);
            if (cuts[3]) sd = Mathf.Max(sd, (c - x - (h - y)) * k);
            return sd;
        }

        private static void Write(string file, int w, int h, Func<float, float, Color> shade, int border)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // 4×4 슈퍼샘플링
                    Color sum = Color.clear;
                    for (int sy = 0; sy < 4; sy++)
                        for (int sx = 0; sx < 4; sx++)
                            sum += shade(x + (sx + 0.5f) / 4f, y + (sy + 0.5f) / 4f);
                    tex.SetPixel(x, y, sum / 16f);
                }
            }
            tex.Apply();
            string path = $"{Root}/{file}";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spriteBorder = new Vector4(border, border, border, border);
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.filterMode = FilterMode.Bilinear;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();
        }

        // ---------------- 아이콘 ----------------

        private delegate bool IconShape(Vector2 p);

        /// <summary>(이름, 색, 도형). 도형 좌표는 -1~1 (위 = +y).</summary>
        private static readonly (string name, Color color, IconShape shape)[] Icons =
        {
            ("power", new Color(1f, 0.85f, 0.3f), p => Poly(p, new Vector2(0.15f, 0.95f), new Vector2(-0.55f, -0.05f), new Vector2(-0.02f, -0.05f),
                new Vector2(-0.2f, -0.95f), new Vector2(0.55f, 0.15f), new Vector2(0.05f, 0.15f))),
            ("oxygen", new Color(0.45f, 0.9f, 1f), p => Ring(p, new Vector2(-0.2f, -0.15f), 0.55f, 0.18f) || Disc(p, new Vector2(0.5f, 0.5f), 0.28f) || Disc(p, new Vector2(0.62f, -0.55f), 0.17f)),
            ("water", new Color(0.35f, 0.6f, 1f), p => Disc(p, new Vector2(0f, -0.3f), 0.58f) || Poly(p, new Vector2(0f, 0.95f), new Vector2(-0.5f, -0.05f), new Vector2(0.5f, -0.05f))),
            ("food", new Color(0.5f, 0.95f, 0.45f), p => (Disc(p, new Vector2(-0.38f, 0.1f), 0.8f) && Disc(p, new Vector2(0.38f, 0.1f), 0.8f) && p.y > -0.55f)
                || Capsule(p, new Vector2(0f, -0.95f), new Vector2(0f, -0.4f), 0.09f)),
            ("metal", new Color(0.78f, 0.82f, 0.88f), p => Hex(p, 0.9f) && !Disc(p, Vector2.zero, 0.35f)),
            ("population", new Color(0.9f, 0.95f, 1f), p => Disc(p, new Vector2(0f, 0.45f), 0.35f) || (Disc(p, new Vector2(0f, -0.85f), 0.75f) && p.y < -0.12f)),
            ("satisfaction", new Color(1f, 0.6f, 0.75f), p => Heart(p)),
            ("repair", new Color(1f, 0.65f, 0.3f), p => Gear(p)),
            ("warning", new Color(1f, 0.38f, 0.32f), p => Poly(p, new Vector2(0f, 0.92f), new Vector2(-0.95f, -0.8f), new Vector2(0.95f, -0.8f))
                && !Capsule(p, new Vector2(0f, 0.38f), new Vector2(0f, -0.2f), 0.1f) && !Disc(p, new Vector2(0f, -0.5f), 0.11f)),
            ("grade", new Color(0.75f, 0.6f, 1f), p => Star(p)),
            ("battery", new Color(0.5f, 1f, 0.6f), p => (Box(p, new Vector2(-0.1f, 0f), new Vector2(0.75f, 0.45f)) && !Box(p, new Vector2(-0.1f, 0f), new Vector2(0.6f, 0.3f)))
                || Box(p, new Vector2(0.75f, 0f), new Vector2(0.1f, 0.2f)) || Box(p, new Vector2(-0.35f, 0f), new Vector2(0.3f, 0.22f))),
            ("sun", new Color(1f, 0.85f, 0.4f), p => Disc(p, Vector2.zero, 0.42f) || Rays(p)),
            ("moon", new Color(0.65f, 0.75f, 1f), p => Disc(p, Vector2.zero, 0.8f) && !Disc(p, new Vector2(0.38f, 0.28f), 0.66f)),
            ("pause", Color.white, p => Box(p, new Vector2(-0.35f, 0f), new Vector2(0.18f, 0.75f)) || Box(p, new Vector2(0.35f, 0f), new Vector2(0.18f, 0.75f))),
            ("play", Color.white, p => Poly(p, new Vector2(-0.55f, 0.8f), new Vector2(-0.55f, -0.8f), new Vector2(0.8f, 0f))),
            ("event", new Color(0.31f, 0.85f, 1f), p => Poly(p, new Vector2(0f, 0.95f), new Vector2(-0.95f, 0f), new Vector2(0f, -0.95f), new Vector2(0.95f, 0f))
                && !Poly(p, new Vector2(0f, 0.55f), new Vector2(-0.55f, 0f), new Vector2(0f, -0.55f), new Vector2(0.55f, 0f))),
            ("module", new Color(0.31f, 0.85f, 1f), p => Hex(p, 0.9f) && !Hex(p, 0.6f) || Hex(p, 0.32f)),
            ("info", new Color(0.31f, 0.85f, 1f), p => Ring(p, Vector2.zero, 0.82f, 0.16f) || Capsule(p, new Vector2(0f, -0.45f), new Vector2(0f, 0.05f), 0.11f) || Disc(p, new Vector2(0f, 0.35f), 0.12f)),
        };

        private static void BuildIcons()
        {
            // 높이를 2의 거듭제곱으로 (아니면 임포트 때 크기가 바뀌어 글리프 좌표가 어긋남)
            int cols = 8, rows = Mathf.NextPowerOfTwo(Mathf.CeilToInt(Icons.Length / (float)cols));
            int w = cols * IconSize, h = rows * IconSize;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels(new Color[w * h]);
            for (int i = 0; i < Icons.Length; i++)
            {
                int ox = (i % cols) * IconSize, oy = (rows - 1 - i / cols) * IconSize;
                var (_, color, shape) = Icons[i];
                for (int y = 0; y < IconSize; y++)
                {
                    for (int x = 0; x < IconSize; x++)
                    {
                        int hit = 0;
                        for (int sy = 0; sy < 4; sy++)
                            for (int sx = 0; sx < 4; sx++)
                            {
                                var p = new Vector2((x + (sx + 0.5f) / 4f) / IconSize * 2f - 1f, (y + (sy + 0.5f) / 4f) / IconSize * 2f - 1f) * 1.12f;
                                if (shape(p)) hit++;
                            }
                        tex.SetPixel(ox + x, oy + y, new Color(color.r, color.g, color.b, hit / 16f));
                    }
                }
            }
            tex.Apply();
            string texPath = Root + "/HUD_Icons.png";
            File.WriteAllBytes(texPath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(texPath);
            var ti = (TextureImporter)AssetImporter.GetAtPath(texPath);
            ti.textureType = TextureImporterType.Default;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = true;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();
            var sheet = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

            string assetPath = Root + "/HUD_Icons.asset";
            var asset = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(assetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
                AssetDatabase.CreateAsset(asset, assetPath);
            }
            // 새 에셋은 버전이 비어 있어 TMP가 구버전 변환(UpgradeSpriteAsset)을 시도하다 실패하므로 현재 버전으로 표시
            var vso = new SerializedObject(asset);
            vso.FindProperty("m_Version").stringValue = "1.1.0";
            vso.ApplyModifiedPropertiesWithoutUndo();
            asset.spriteSheet = sheet;
            if (asset.material == null)
            {
                var mat = new Material(Shader.Find("TextMeshPro/Sprite")) { name = "HUD_Icons Material" };
                AssetDatabase.AddObjectToAsset(mat, asset);
                asset.material = mat;
            }
            asset.material.mainTexture = sheet;
            asset.spriteGlyphTable.Clear();
            asset.spriteCharacterTable.Clear();
            for (int i = 0; i < Icons.Length; i++)
            {
                int ox = (i % cols) * IconSize, oy = (rows - 1 - i / cols) * IconSize;
                var glyph = new TMP_SpriteGlyph((uint)i, new GlyphMetrics(IconSize, IconSize, 0f, IconSize * 0.8f, IconSize * 1.08f),
                    new GlyphRect(ox, oy, IconSize, IconSize), 1f, 0);
                asset.spriteGlyphTable.Add(glyph);
                asset.spriteCharacterTable.Add(new TMP_SpriteCharacter((uint)(0xE000 + i), glyph) { name = Icons[i].name });
            }
            asset.UpdateLookupTables();
            EditorUtility.SetDirty(asset);

            // TMP 기본 스프라이트 에셋으로 등록
            var settings = Resources.Load<TMP_Settings>("TMP Settings");
            if (settings != null)
            {
                var so = new SerializedObject(settings);
                so.FindProperty("m_defaultSpriteAsset").objectReferenceValue = asset;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(settings);
            }
            else
            {
                Debug.LogWarning("[HudArtBuilder] TMP Settings를 찾지 못함 — 텍스트마다 스프라이트 에셋을 지정해야 함");
            }
        }

        // 도형 도우미
        private static bool Disc(Vector2 p, Vector2 c, float r) => (p - c).sqrMagnitude <= r * r;
        private static bool Ring(Vector2 p, Vector2 c, float r, float t) { float d = (p - c).magnitude; return d <= r && d >= r - t; }
        private static bool Box(Vector2 p, Vector2 c, Vector2 half) => Mathf.Abs(p.x - c.x) <= half.x && Mathf.Abs(p.y - c.y) <= half.y;
        private static bool Capsule(Vector2 p, Vector2 a, Vector2 b, float r)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).sqrMagnitude <= r * r;
        }
        private static bool Poly(Vector2 p, params Vector2[] v)
        {
            bool inside = false;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i++)
            {
                if ((v[i].y > p.y) != (v[j].y > p.y) && p.x < (v[j].x - v[i].x) * (p.y - v[i].y) / (v[j].y - v[i].y) + v[i].x)
                    inside = !inside;
            }
            return inside;
        }
        private static bool Hex(Vector2 p, float r)
        {
            var q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y));
            return q.y <= r * 0.866f && q.x * 0.866f + q.y * 0.5f <= r * 0.866f;
        }
        private static bool Star(Vector2 p)
        {
            var v = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.PI / 2f + i * Mathf.PI / 5f;
                float r = i % 2 == 0 ? 0.95f : 0.42f;
                v[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            }
            return Poly(p, v);
        }
        private static bool Heart(Vector2 p)
        {
            var q = new Vector2(p.x, p.y + 0.15f) * 1.15f;
            float x = q.x, y = q.y;
            float a = x * x + y * y - 0.5f;
            return a * a * a - x * x * y * y * y <= 0f;
        }
        private static bool Gear(Vector2 p)
        {
            float r = p.magnitude;
            if (r < 0.28f) return false;
            float a = Mathf.Atan2(p.y, p.x);
            float tooth = Mathf.Cos(a * 8f) > 0.3f ? 0.92f : 0.7f;
            return r <= tooth;
        }
        private static bool Rays(Vector2 p)
        {
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                if (Capsule(p, d * 0.6f, d * 0.9f, 0.08f)) return true;
            }
            return false;
        }

        // ---------------- 모듈 썸네일 ----------------

        private static void BuildThumbnails()
        {
            const int size = 160;
            var origin = new Vector3(8000f, 8000f, 8000f);
            var camGo = new GameObject("ThumbCam");
            var lightGo = new GameObject("ThumbFill");
            var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            try
            {
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.fieldOfView = 24f;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 50f;
                cam.targetTexture = rt;
                var urp = camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                urp.renderPostProcessing = false;
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 0.6f;
                light.color = new Color(0.75f, 0.85f, 1f);
                lightGo.transform.rotation = Quaternion.Euler(25f, 140f, 0f);

                foreach (var guid in AssetDatabase.FindAssets("t:ModuleData", new[] { "Assets/_Project/Data/Modules" }))
                {
                    var data = AssetDatabase.LoadAssetAtPath<ModuleData>(AssetDatabase.GUIDToAssetPath(guid));
                    if (data == null || data.Prefab == null)
                        continue;
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(data.Prefab);
                    try
                    {
                        inst.transform.position = origin;
                        var renderers = inst.GetComponentsInChildren<Renderer>();
                        if (renderers.Length == 0)
                            continue;
                        var b = renderers[0].bounds;
                        foreach (var r in renderers)
                            b.Encapsulate(r.bounds);
                        var dir = new Vector3(0.75f, 0.62f, 1f).normalized; // 앞(+Z)·오른쪽 위에서
                        float radius = b.extents.magnitude;
                        float dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.02f;
                        camGo.transform.position = b.center + dir * dist;
                        camGo.transform.LookAt(b.center);
                        cam.Render();

                        RenderTexture.active = rt;
                        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                        tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                        tex.Apply();
                        RenderTexture.active = null;
                        string key = data.name.Replace("MD_", "");
                        string path = $"{ThumbDir}/TH_{key}.png";
                        File.WriteAllBytes(path, tex.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(tex);
                        AssetDatabase.ImportAsset(path);
                        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                        ti.textureType = TextureImporterType.Sprite;
                        ti.alphaIsTransparency = true;
                        ti.mipmapEnabled = false;
                        ti.SaveAndReimport();
                        var so = new SerializedObject(data);
                        so.FindProperty("_icon").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                        so.ApplyModifiedPropertiesWithoutUndo();
                        EditorUtility.SetDirty(data);
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(inst);
                    }
                }
            }
            finally
            {
                RenderTexture.active = null;
                UnityEngine.Object.DestroyImmediate(camGo);
                UnityEngine.Object.DestroyImmediate(lightGo);
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }
    }
}
