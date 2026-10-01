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

        private static Material Unlit(string name, Color hdr)
        {
            var m = Load(name, Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", hdr);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material Additive(string name, Color hdr)
        {
            var m = Load(name, Shader.Find("Universal Render Pipeline/Particles/Unlit"));
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
