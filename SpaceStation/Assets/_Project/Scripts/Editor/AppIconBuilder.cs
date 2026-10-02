using System.IO;
using SpaceStation.Data;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 배포 준비: 실행 파일 아이콘 (메뉴 SpaceStation/Build/Make App Icon).
    /// 코어 모듈 프리팹을 빈 임시 씬에서 조명 3개로 투명 배경 렌더 → 둥근 사각형 남색 그라데이션 타일 위에 합성
    /// → Art/UI/AppIcon.png, 플레이어 설정(Standalone) 아이콘으로 지정.
    /// </summary>
    public static class AppIconBuilder
    {
        private const string OutPath = "Assets/_Project/Art/UI/AppIcon.png";
        private const int Size = 1024;

        [MenuItem("SpaceStation/Build/Make App Icon")]
        public static void Make()
        {
            var core = AssetDatabase.LoadAssetAtPath<ModuleData>("Assets/_Project/Data/Modules/MD_Core.asset");
            var shot = RenderModule(core.Prefab, new Vector3(0.5f, 0.32f, 0.5f), 38f, 27f, 5.5f);
            var icon = Compose(shot);
            Object.DestroyImmediate(shot);
            Directory.CreateDirectory(Path.GetDirectoryName(OutPath));
            File.WriteAllBytes(OutPath, icon.EncodeToPNG());
            Object.DestroyImmediate(icon);
            AssetDatabase.ImportAsset(OutPath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(OutPath);
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(OutPath);
            var target = NamedBuildTarget.Standalone;
            var sizes = PlayerSettings.GetIconSizes(target, IconKind.Application);
            var icons = new Texture2D[sizes.Length];
            for (int i = 0; i < icons.Length; i++)
                icons[i] = tex;
            PlayerSettings.SetIcons(target, icons, IconKind.Application);
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { tex }, IconKind.Any); // 기본 아이콘
            AssetDatabase.SaveAssets();
            Debug.Log($"[AppIconBuilder] {OutPath} → 실행 파일 아이콘 ({sizes.Length}개 크기)");
        }

        /// <summary>빈 임시 씬에 프리팹 하나 + 조명 3개 + 카메라 → 투명 배경 렌더.</summary>
        private static Texture2D RenderModule(GameObject prefab, Vector3 focus, float yaw, float pitch, float distance)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                Light(scene, new Vector3(40f, -30f, 0f), new Color(1f, 0.96f, 0.9f), 2.4f);   // 키
                Light(scene, new Vector3(20f, 150f, 0f), new Color(0.45f, 0.75f, 1f), 1.6f);  // 림 (하늘색)
                Light(scene, new Vector3(-30f, 60f, 0f), new Color(0.6f, 0.62f, 0.7f), 0.6f); // 아래 채움
                var camGo = new GameObject("IconCamera");
                SceneManager.MoveGameObjectToScene(camGo, scene);
                var cam = camGo.AddComponent<Camera>();
                cam.scene = scene;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.fieldOfView = 30f;
                cam.nearClipPlane = 0.05f;
                var data = camGo.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = false;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                var rot = Quaternion.Euler(pitch, yaw, 0f);
                camGo.transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);
                var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
                cam.targetTexture = rt;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                cam.targetTexture = null;
                rt.Release();
                return tex;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static void Light(UnityEngine.SceneManagement.Scene scene, Vector3 euler, Color color, float intensity)
        {
            var go = new GameObject("IconLight");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.rotation = Quaternion.Euler(euler);
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = color;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
        }

        /// <summary>둥근 사각형 타일(남색 방사 그라데이션 + 하늘색 테두리) 위에 렌더를 얹는다.</summary>
        private static Texture2D Compose(Texture2D shot)
        {
            var outTex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var px = shot.GetPixels();
            var result = new Color[Size * Size];
            float radius = Size * 0.18f, inset = Size * 0.02f, border = Size * 0.012f;
            var inner = new Color(0.07f, 0.17f, 0.3f, 1f);
            var outer = new Color(0.015f, 0.03f, 0.07f, 1f);
            var rim = new Color(0.3f, 0.85f, 1f, 1f);
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float d = RoundedRectDistance(x + 0.5f, y + 0.5f, inset, Size - inset, radius); // 음수 = 안쪽
                    float a = Mathf.Clamp01(0.5f - d);
                    float r = Vector2.Distance(new Vector2(x, y), new Vector2(Size * 0.5f, Size * 0.55f)) / (Size * 0.62f);
                    var bg = Color.Lerp(inner, outer, Mathf.SmoothStep(0f, 1f, r));
                    float rimK = Mathf.Clamp01(1f - Mathf.Abs(d + border) / border);
                    bg = Color.Lerp(bg, rim, rimK * 0.9f);
                    var s = px[y * Size + x];
                    var c = Color.Lerp(bg, new Color(s.r, s.g, s.b, 1f), s.a);
                    c.a = a;
                    result[y * Size + x] = c;
                }
            }
            outTex.SetPixels(result);
            outTex.Apply();
            return outTex;
        }

        private static float RoundedRectDistance(float x, float y, float min, float max, float r)
        {
            float cx = Mathf.Clamp(x, min + r, max - r), cy = Mathf.Clamp(y, min + r, max - r);
            return Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy)) - r;
        }
    }
}
