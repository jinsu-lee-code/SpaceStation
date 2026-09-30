using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 5-5 텍스처 모델: 원본 AI 모델의 텍스처(Art/Models/Modules/Textures/{모듈}/T_{모듈}_BaseColor|Normal|ORM.png)로
    /// URP Lit 재질 M_{모듈}_Tex를 만든다. FBX 재질 슬롯 "Tex_{모듈}"이 이 재질에 연결된다.
    /// - ORM(R 차폐, G 거칠기, B 금속) → URP용 T_{모듈}_MaskMap(R 금속, G 차폐, A 매끈함) 변환
    /// - 채도가 높은 색 영역(모듈 색 표식·창) → T_{모듈}_Emission (밤에 은은하게 빛남, ModuleView가 상태별로 조절)
    /// </summary>
    public static class ModuleTextureMaterials
    {
        public const string SlotPrefix = "Tex_";
        private const string TextureRoot = "Assets/_Project/Art/Models/Modules/Textures/";
        private const int MaxSize = 1024;
        private const float EmissionIntensity = 0.9f;
        private const float EmissionMinSaturation = 0.4f;
        private const float EmissionMinValue = 0.35f;

        /// <summary>
        /// 모듈 텍스처 폴더의 세트(T_{이름}_BaseColor.png)마다 재질을 만들거나 갱신한다.
        /// 반환: FBX 슬롯 이름 "Tex_{이름}" → 재질. 부품별 텍스처(예: TurretHead, ShieldEmitter)도 같은 폴더에 둔다.
        /// </summary>
        public static Dictionary<string, Material> EnsureAll(string key)
        {
            var result = new Dictionary<string, Material>();
            string dir = TextureRoot + key + "/";
            if (!Directory.Exists(dir))
                return result;
            foreach (var file in Directory.GetFiles(dir, "T_*_BaseColor.png"))
            {
                string fileName = Path.GetFileName(file);
                string name = fileName.Substring(2, fileName.Length - 2 - "_BaseColor.png".Length);
                result[SlotPrefix + name] = Ensure(dir, name);
            }
            return result;
        }

        private static Material Ensure(string dir, string key)
        {
            string basePath = dir + $"T_{key}_BaseColor.png";
            string normalPath = dir + $"T_{key}_Normal.png";
            string ormPath = dir + $"T_{key}_ORM.png";

            string maskPath = dir + $"T_{key}_MaskMap.png";
            string emissionPath = dir + $"T_{key}_Emission.png";
            var baseTex = LoadPixels(basePath);
            if (File.Exists(ormPath))
                WriteMaskMap(LoadPixels(ormPath), maskPath);
            WriteEmission(baseTex, emissionPath);
            Object.DestroyImmediate(baseTex);

            Configure(basePath, TextureImporterType.Default, true);
            Configure(normalPath, TextureImporterType.NormalMap, false);
            Configure(ormPath, TextureImporterType.Default, false);
            Configure(maskPath, TextureImporterType.Default, false);
            Configure(emissionPath, TextureImporterType.Default, true);

            string matPath = dir + $"M_{key}_Tex.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, matPath);
            }
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(basePath));
            m.SetColor("_BaseColor", Color.white);

            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            m.SetTexture("_BumpMap", normal);
            m.SetFloat("_BumpScale", 1f);
            m.SetKeyword(new LocalKeyword(m.shader, "_NORMALMAP"), normal != null);

            var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);
            m.SetTexture("_MetallicGlossMap", mask);
            m.SetTexture("_OcclusionMap", mask); // URP는 차폐를 G에서 읽음
            m.SetFloat("_Metallic", 1f);
            m.SetFloat("_Smoothness", 1f); // 맵의 A가 매끈함 (배율 1)
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            m.SetFloat("_OcclusionStrength", 1f);
            m.SetKeyword(new LocalKeyword(m.shader, "_METALLICSPECGLOSSMAP"), mask != null);
            m.SetKeyword(new LocalKeyword(m.shader, "_OCCLUSIONMAP"), mask != null);

            m.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(emissionPath));
            m.SetKeyword(new LocalKeyword(m.shader, "_EMISSION"), true);
            m.SetColor("_EmissionColor", Color.white * EmissionIntensity);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Texture2D LoadPixels(string path)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            tex.LoadImage(File.ReadAllBytes(path));
            return tex;
        }

        private static void WriteMaskMap(Texture2D orm, string path)
        {
            var src = orm.GetPixels32();
            var dst = new Color32[src.Length];
            for (int i = 0; i < src.Length; i++)
                dst[i] = new Color32(src[i].b, src[i].r, 0, (byte)(255 - src[i].g));
            Save(dst, orm.width, orm.height, path);
            Object.DestroyImmediate(orm);
        }

        private static void WriteEmission(Texture2D baseTex, string path)
        {
            // 원본 텍스처 전체를 절반 크기로 (발광 맵은 해상도가 낮아도 충분)
            int w = Mathf.Max(1, baseTex.width / 2), h = Mathf.Max(1, baseTex.height / 2);
            var dst = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Color c = baseTex.GetPixel(x * 2, y * 2); // 선형으로 읽혀도 sRGB 값 그대로 (색 판정만 함)
                    Color.RGBToHSV(c, out _, out float s, out float v);
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(EmissionMinSaturation, EmissionMinSaturation + 0.15f, s))
                              * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(EmissionMinValue, EmissionMinValue + 0.15f, v));
                    dst[y * w + x] = new Color(c.r * k, c.g * k, c.b * k, 1f);
                }
            }
            Save(dst, w, h, path);
        }

        private static void Save(Color32[] pixels, int w, int h, string path)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            tex.SetPixels32(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
        }

        private static void Configure(string path, TextureImporterType type, bool srgb)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti))
                return;
            if (ti.textureType == type && ti.sRGBTexture == srgb && ti.maxTextureSize == MaxSize && ti.mipmapEnabled)
                return;
            ti.textureType = type;
            ti.sRGBTexture = srgb;
            ti.maxTextureSize = MaxSize;
            ti.mipmapEnabled = true;
            ti.SaveAndReimport();
        }
    }
}
