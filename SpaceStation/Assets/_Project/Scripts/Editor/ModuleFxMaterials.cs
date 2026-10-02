using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 5-5 모듈 연출 재질 (Art/Materials/FX). FBX 재질 슬롯 "FX_{이름}"은 M_{이름}에 연결된다.
    /// - M_ShieldShell: 반투명 외피 (SpaceStation/ShieldShell)
    /// - M_ShieldCore / M_ShieldOrb: HDR 단색 (블룸으로 빛남)
    /// - M_ShieldTrail: 빛점 꼬리 (가산 반투명 파티클)
    /// - M_FarmGlass: 수경 농장 온실 유리 (반투명 Lit)
    /// - M_FarmGrow: 수경 농장 생장등 (불투명 Lit + HDR 발광)
    /// </summary>
    public static class ModuleFxMaterials
    {
        public const string SlotPrefix = "FX_";
        private const string Folder = "Assets/_Project/Art/Materials/FX";

        public static Material Get(string name)
        {
            switch (name)
            {
                case "ShieldShell": return ShieldShell();
                case "ShieldCore": return Unlit("M_ShieldCore", new Color(1.4f, 3.0f, 5.0f, 1f));
                case "ShieldOrb": return Unlit("M_ShieldOrb", new Color(2.2f, 4.2f, 6.0f, 1f));
                case "ShieldTrail": return Additive("M_ShieldTrail", new Color(0.6f, 1.8f, 4.2f, 1f));
                case "ConnectorFlow": return ConnectorFlow();
                // 5-7 이벤트·배경 연출
                case "MeteorGlow": return Additive("M_MeteorGlow", new Color(2.4f, 0.9f, 0.25f, 0.6f), soft: false);
                case "MeteorTrail": return Additive("M_MeteorTrail", new Color(2.0f, 0.7f, 0.2f, 1f));
                case "MeteorFlash": return Additive("M_MeteorFlash", new Color(2.5f, 1.2f, 0.4f, 1f), soft: false);
                case "TurretLaser": return Additive("M_TurretLaser", new Color(4f, 0.35f, 0.3f, 1f));
                case "Spark": return Additive("M_Spark", new Color(2.2f, 1.4f, 0.7f, 1f));
                case "StormSpark": return Additive("M_StormSpark", new Color(1.6f, 2.2f, 4f, 1f));
                case "Dust": return Additive("M_Dust", new Color(0.9f, 0.95f, 1.1f, 1f));
                case "ShipEngine": return Additive("M_ShipEngine", new Color(0.6f, 1.6f, 3.5f, 1f));
                case "ShieldRipple": return ShieldRipple();
                case "FarmGlass": return Glass("M_FarmGlass", new Color(0.62f, 1f, 0.84f, 0.26f), new Color(0.05f, 0.16f, 0.11f));
                case "FarmGrow": return Emissive("M_FarmGrow", new Color(0.35f, 0.9f, 0.4f, 1f), new Color(0.5f, 2.6f, 0.7f, 1f));
                case "FusionPlasma": return Emissive("M_FusionPlasma", new Color(0.3f, 0.75f, 1f, 1f), new Color(0.3f, 1.3f, 3.0f, 1f));
                case "Rock": return Rock();
                default: return null;
            }
        }

        private static Material ShieldShell()
        {
            var m = Load("M_ShieldShell", Shader.Find("SpaceStation/ShieldShell"));
            m.SetColor("_BaseColor", Color.white);
            // 가운데는 비치고 가장자리만 빛나게 (안쪽 코어·빛점이 보이도록)
            m.SetColor("_ShellColor", new Color(0.12f, 0.45f, 1.4f, 1f));
            m.SetFloat("_FillAlpha", 0.05f);
            m.SetFloat("_RimPower", 2.6f);
            m.SetFloat("_RimStrength", 1.5f);
            m.SetFloat("_WaveStrength", 0.28f);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>5-7 실드가 운석을 빗겨낼 때 범위 구면 파문 (알파는 MeteorFx가 _BaseColor로 페이드).</summary>
        private static Material ShieldRipple()
        {
            var m = Load("M_ShieldRipple", Shader.Find("SpaceStation/ShieldShell"));
            m.SetColor("_BaseColor", Color.white);
            m.SetColor("_ShellColor", new Color(0.3f, 1.0f, 2.6f, 1f));
            m.SetFloat("_FillAlpha", 0f);
            m.SetFloat("_RimPower", 4.5f);
            m.SetFloat("_RimStrength", 1.8f);
            m.SetFloat("_WaveStrength", 0.12f);
            m.SetFloat("_WaveScale", 3f);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>반투명 유리 (수경 농장 온실, 2026-10-02): URP Lit 투명 + 반사 + 은은한 발광 (밤에도 윤곽이 보이게).</summary>
        private static Material Glass(string name, Color tint, Color emission)
        {
            var m = Load(name, Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", 0.92f);
            m.SetFloat("_Surface", 1f);   // Transparent
            m.SetFloat("_Blend", 0f);     // Alpha
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.SetKeyword(new LocalKeyword(m.shader, "_SURFACE_TYPE_TRANSPARENT"), true);
            m.SetKeyword(new LocalKeyword(m.shader, "_EMISSION"), true);
            m.SetColor("_EmissionColor", emission);
            // GI 플래그가 EmissiveIsBlack이면 URP 재질 검사가 _EMISSION을 꺼 버린다 (2026-10-03 발견)
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetOverrideTag("RenderType", "Transparent");
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// 불투명 발광체 (7-4 수경 농장 생장등): URP Lit + HDR 발광 → 블룸으로 빛남.
        /// Lit이라 ModuleView가 발광 세기를 상태별로 조절한다 (비활성 소등, 파손 깜빡임).
        /// </summary>
        private static Material Emissive(string name, Color baseColor, Color hdrEmission)
        {
            var m = Load(name, Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", baseColor);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", 0.6f);
            m.SetKeyword(new LocalKeyword(m.shader, "_EMISSION"), true);
            m.SetColor("_EmissionColor", hdrEmission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>5-7 운석·배경 소행성 바위.</summary>
        private static Material Rock()
        {
            var m = Load("M_Rock", Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", new Color(0.24f, 0.21f, 0.19f, 1f));
            m.SetFloat("_Metallic", 0.05f);
            m.SetFloat("_Smoothness", 0.15f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>5-6 연결 통로 불빛 띠 (코어에서 바깥으로 흐름).</summary>
        private static Material ConnectorFlow()
        {
            var m = Load("M_ConnectorFlow", Shader.Find("SpaceStation/ConnectorFlow"));
            m.SetColor("_BaseColor", new Color(0.25f, 1.1f, 2.6f, 1f));
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material Unlit(string name, Color hdr)
        {
            var m = Load(name, Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", hdr);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>둥근 부드러운 점 텍스처 (입자·꼬리가 네모로 보이지 않도록). 없으면 만든다.</summary>
        private static Texture2D SoftDot()
        {
            string path = $"{Folder}/T_SoftDot.png";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null)
                return tex;
            const int size = 64;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    a = a * a * (3f - 2f * a); // 부드러운 가장자리
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            t.Apply();
            System.IO.File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                ti.alphaIsTransparency = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.mipmapEnabled = true;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Material Additive(string name, Color hdr, bool soft = true)
        {
            var m = Load(name, Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            // 메시(구·바위)에 쓰는 재질은 UV가 없거나 의미가 없으므로 텍스처 없이
            m.SetTexture("_BaseMap", soft ? SoftDot() : null);
            m.SetFloat("_Surface", 1f);    // Transparent
            m.SetFloat("_Blend", 2f);      // Additive
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.SetKeyword(new LocalKeyword(m.shader, "_SURFACE_TYPE_TRANSPARENT"), true);
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetColor("_BaseColor", hdr);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material Load(string name, Shader shader)
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/_Project/Art/Materials", "FX");
            string path = $"{Folder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader)
            {
                m.shader = shader;
            }
            return m;
        }
    }
}
