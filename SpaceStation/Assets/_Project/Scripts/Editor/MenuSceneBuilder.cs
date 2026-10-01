using System.Collections.Generic;
using SpaceStation.Audio;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using SpaceStation.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 5-9 메뉴 구성. 여러 번 실행해도 결과가 같다.
    /// - Build Pause Menu: 게임 씬(Main) HUD에 ESC 일시정지 메뉴 생성·연결
    /// - Build Main Menu Scene: 게임 씬을 복사해 연출(정거장·조명·배경·후처리)만 남기고 전시 정거장·궤도 카메라·메뉴 UI를 얹음.
    ///   빌드 설정 = [MainMenu, Main]
    /// </summary>
    public static class MenuSceneBuilder
    {
        private const string GameScenePath = "Assets/_Project/Scenes/Main.unity";
        private const string MenuScenePath = "Assets/_Project/Scenes/MainMenu.unity";
        private const string LayoutPath = "Assets/_Project/Data/Showcase/ShowcaseLayout.asset";
        private const string DifficultyRoot = "Assets/_Project/Data/Difficulty/";
        private const string Title = "ANOTHER EARTH";

        // ---------------- 일시정지 메뉴 (게임 씬) ----------------

        [MenuItem("SpaceStation/Menu/Build Pause Menu")]
        public static void BuildPauseMenu()
        {
            var scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            var hud = GameObject.Find("HUD");
            var font = hud.GetComponentInChildren<TMP_Text>(true).font;
            var old = hud.transform.Find("PauseMenu");
            if (old != null)
                Object.DestroyImmediate(old.gameObject);

            var root = new GameObject("PauseMenu", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
            root.transform.SetParent(hud.transform, false);
            Stretch((RectTransform)root.transform);
            var dim = root.GetComponent<Image>();
            dim.color = new Color(0f, 0.01f, 0.03f, 0.6f);
            root.transform.SetAsLastSibling();

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            panel.transform.SetParent(root.transform, false);
            var prt = (RectTransform)panel.transform;
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(440f, 0f);
            HudStyler.Panel(panel, new Color(0.03f, 0.07f, 0.11f, 0.95f), HudTheme.Accent);
            var vl = panel.GetComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(40, 40, 30, 36);
            vl.spacing = 12;
            vl.childAlignment = TextAnchor.UpperCenter;
            vl.childControlWidth = true;
            vl.childControlHeight = true;
            vl.childForceExpandWidth = true;
            vl.childForceExpandHeight = false;
            panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var title = Text(panel.transform, "Title", "일시정지", font, 34f, TextAlignmentOptions.Center, 48f);
            title.fontStyle = FontStyles.Bold;
            var info = Text(panel.transform, "Info", "난이도", font, 17f, TextAlignmentOptions.Center, 30f);
            Spacer(panel.transform, 6f);
            var resume = MakeButton(panel.transform, "Resume", "재개  <size=70%><color=#AFC4D8>ESC</color></size>", font, 22f, 58f);
            var settings = MakeButton(panel.transform, "Settings", "설정  <size=70%><color=#AFC4D8>준비 중</color></size>", font, 22f, 58f);
            var restart = MakeButton(panel.transform, "Restart", "재시작", font, 22f, 58f);
            var menu = MakeButton(panel.transform, "MainMenu", "메인 메뉴로", font, 22f, 58f);

            var pause = root.AddComponent<PauseMenu>();
            Set(pause, "_clock", Object.FindFirstObjectByType<SimulationClock>());
            Set(pause, "_host", Object.FindFirstObjectByType<SimulationHost>());
            Set(pause, "_build", Object.FindFirstObjectByType<BuildController>());
            Set(pause, "_selection", Object.FindFirstObjectByType<ModuleSelectionController>());
            Set(pause, "_group", root.GetComponent<CanvasGroup>());
            Set(pause, "_panel", prt);
            Set(pause, "_info", info);
            Set(pause, "_resumeButton", resume);
            Set(pause, "_settingsButton", settings);
            Set(pause, "_restartButton", restart);
            Set(pause, "_mainMenuButton", menu);
            var group = root.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[MenuSceneBuilder] 일시정지 메뉴 생성");
        }

        // ---------------- 메인 메뉴 씬 ----------------

        [MenuItem("SpaceStation/Menu/Build Main Menu Scene")]
        public static void BuildMainMenuScene()
        {
            var game = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            var font = GameObject.Find("HUD").GetComponentInChildren<TMP_Text>(true).font;
            EditorSceneManager.SaveScene(game, MenuScenePath, true);
            var scene = EditorSceneManager.OpenScene(MenuScenePath, OpenSceneMode.Single);

            // 게임 전용 오브젝트·컴포넌트 제거
            DestroyRoot("HUD");
            DestroyRoot("DefenseRange");
            var station = Object.FindFirstObjectByType<StationController>();
            Remove<BuildController>(station.gameObject);
            Remove<ModuleSelectionController>(station.gameObject);
            var host = Object.FindFirstObjectByType<SimulationHost>();
            SetBool(host, "_enableDebugEventTrigger", false);
            var volume = GameObject.Find("GlobalVolume");
            if (volume != null)
                Remove<CrisisVignette>(volume);
            var cam = Camera.main;
            Remove<OrbitCameraController>(cam.gameObject);
            var audio = GameObject.Find("Audio");
            Remove<GameAudio>(audio);

            // 전시 정거장 + 궤도 카메라 + 메뉴 소리
            var showcase = GetOrAdd<ShowcaseStation>(station.gameObject);
            Set(showcase, "_station", station);
            Set(showcase, "_clock", Object.FindFirstObjectByType<SimulationClock>());
            Set(showcase, "_layout", AssetDatabase.LoadAssetAtPath<ShowcaseLayout>(LayoutPath));
            var orbit = GetOrAdd<MenuCameraOrbit>(cam.gameObject);
            Set(orbit, "_station", station);
            GetOrAdd<MenuAudio>(audio);

            BuildMenuCanvas(font);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MenuScenePath, true),
                new EditorBuildSettingsScene(GameScenePath, true),
            };
            Debug.Log("[MenuSceneBuilder] 메인 메뉴 씬 생성, 빌드 설정 [MainMenu, Main]");
        }

        private static void BuildMenuCanvas(TMP_FontAsset font)
        {
            var canvasGo = new GameObject("Menu", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // 왼쪽 가독성용 어두운 띠 (가장자리로 갈수록 투명)
            var shade = new GameObject("Shade", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            shade.transform.SetParent(canvasGo.transform, false);
            var srt = (RectTransform)shade.transform;
            srt.anchorMin = new Vector2(0f, 0f);
            srt.anchorMax = new Vector2(0f, 1f);
            srt.pivot = new Vector2(0f, 0.5f);
            srt.sizeDelta = new Vector2(900f, 0f);
            var raw = shade.GetComponent<RawImage>();
            raw.texture = ShadeTexture();
            raw.raycastTarget = false;

            // 제목
            var title = Text(canvasGo.transform, "Title", Title, font, 78f, TextAlignmentOptions.Left, 120f);
            title.fontStyle = FontStyles.Bold;
            title.characterSpacing = 10f;
            title.color = new Color(0.92f, 0.98f, 1f);
            Place((RectTransform)title.transform, new Vector2(140f, -230f), new Vector2(1000f, 120f));
            var bar = new GameObject("TitleBar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            bar.transform.SetParent(canvasGo.transform, false);
            bar.GetComponent<Image>().color = HudTheme.Accent;
            bar.GetComponent<Image>().raycastTarget = false;
            Place((RectTransform)bar.transform, new Vector2(144f, -340f), new Vector2(480f, 3f));

            // 메인 패널
            var main = Column(canvasGo.transform, "MainPanel", new Vector2(140f, -430f), 440f);
            var newGame = MakeButton(main.transform, "NewGame", "새 게임", font, 26f, 66f);
            var settings = MakeButton(main.transform, "Settings", "설정  <size=70%><color=#AFC4D8>준비 중</color></size>", font, 26f, 66f);
            var quit = MakeButton(main.transform, "Quit", "종료", font, 26f, 66f);

            // 난이도 패널
            var diff = Column(canvasGo.transform, "DifficultyPanel", new Vector2(140f, -400f), 560f);
            var header = Text(diff.transform, "Header", "난이도 선택", font, 30f, TextAlignmentOptions.Left, 46f);
            header.fontStyle = FontStyles.Bold;
            var presets = new List<DifficultyPreset>();
            var buttons = new List<Button>();
            var labels = new List<TMP_Text>();
            foreach (var file in new[] { "DIFF_Easy", "DIFF_Normal", "DIFF_Hard" })
            {
                presets.Add(AssetDatabase.LoadAssetAtPath<DifficultyPreset>(DifficultyRoot + file + ".asset"));
                var b = MakeButton(diff.transform, file, file, font, 26f, 104f);
                var label = b.GetComponentInChildren<TMP_Text>();
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.lineSpacing = -6f;
                label.margin = new Vector4(26f, 6f, 20f, 6f);
                b.GetComponent<UiSound>().Mode = UiSound.ClickMode.Click;
                buttons.Add(b);
                labels.Add(label);
            }
            Spacer(diff.transform, 4f);
            var back = MakeButton(diff.transform, "Back", "뒤로  <size=70%><color=#AFC4D8>ESC</color></size>", font, 20f, 50f);
            back.GetComponent<LayoutElement>().preferredWidth = 220f;

            var version = Text(canvasGo.transform, "Version", "v0.5 · Phase 5", font, 15f, TextAlignmentOptions.BottomRight, 30f);
            version.color = new Color(0.6f, 0.7f, 0.8f, 0.7f);
            var vrt = (RectTransform)version.transform;
            vrt.anchorMin = vrt.anchorMax = vrt.pivot = new Vector2(1f, 0f);
            vrt.anchoredPosition = new Vector2(-30f, 24f);
            vrt.sizeDelta = new Vector2(400f, 30f);

            var controller = canvasGo.AddComponent<MainMenuController>();
            Set(controller, "_mainPanel", main.GetComponent<CanvasGroup>());
            Set(controller, "_difficultyPanel", diff.GetComponent<CanvasGroup>());
            Set(controller, "_newGameButton", newGame);
            Set(controller, "_settingsButton", settings);
            Set(controller, "_quitButton", quit);
            Set(controller, "_backButton", back);
            SetArray(controller, "_difficulties", presets.ToArray());
            SetArray(controller, "_difficultyButtons", buttons.ToArray());
            SetArray(controller, "_difficultyLabels", labels.ToArray());
        }

        // ---------------- 도우미 ----------------

        private static GameObject Column(Transform parent, string name, Vector2 topLeft, float width)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = topLeft;
            rt.sizeDelta = new Vector2(width, 0f);
            var vl = go.GetComponent<VerticalLayoutGroup>();
            vl.spacing = 14;
            vl.childAlignment = TextAnchor.UpperLeft;
            vl.childControlWidth = true;
            vl.childControlHeight = true;
            vl.childForceExpandWidth = false;
            vl.childForceExpandHeight = false;
            go.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var group = go.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            return go;
        }

        private static Button MakeButton(Transform parent, string name, string label, TMP_FontAsset font, float fontSize, float height)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = height;
            le.preferredWidth = ((RectTransform)parent).sizeDelta.x > 0f ? ((RectTransform)parent).sizeDelta.x : 360f;
            var button = go.GetComponent<Button>();
            button.targetGraphic = go.GetComponent<Image>();
            var text = Text(go.transform, "Label", label, font, fontSize, TextAlignmentOptions.MidlineLeft, height);
            Stretch((RectTransform)text.transform);
            text.margin = new Vector4(26f, 0f, 16f, 0f);
            HudStyler.StyleButton(button, fontSize);
            go.AddComponent<UiSound>();
            return button;
        }

        private static TMP_Text Text(Transform parent, string name, string value, TMP_FontAsset font, float size, TextAlignmentOptions align, float height)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            t.font = font;
            t.text = value;
            HudStyler.StyleText(t, size);
            t.alignment = align;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            go.GetComponent<LayoutElement>().preferredHeight = height;
            return t;
        }

        private static void Spacer(Transform parent, float height)
        {
            var go = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = height;
        }

        private static void Place(RectTransform rt, Vector2 topLeft, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = topLeft;
            rt.sizeDelta = size;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>왼쪽이 진하고 오른쪽으로 사라지는 가로 그라데이션 (에셋으로 저장).</summary>
        private static Texture2D ShadeTexture()
        {
            const string path = "Assets/_Project/Art/UI/UI_MenuShade.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
                return existing;
            var tex = new Texture2D(256, 4, TextureFormat.RGBA32, false);
            for (int x = 0; x < 256; x++)
            {
                float k = x / 255f;
                float a = Mathf.Lerp(0.72f, 0f, Mathf.SmoothStep(0f, 1f, k));
                for (int y = 0; y < 4; y++)
                    tex.SetPixel(x, y, new Color(0.01f, 0.02f, 0.05f, a));
            }
            tex.Apply();
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void DestroyRoot(string name)
        {
            var go = GameObject.Find(name);
            if (go != null)
                Object.DestroyImmediate(go);
        }

        private static void Remove<T>(GameObject go) where T : Component
        {
            if (go == null)
                return;
            var c = go.GetComponent<T>();
            if (c != null)
                Object.DestroyImmediate(c);
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        private static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null)
            {
                Debug.LogWarning($"[MenuSceneBuilder] {target.GetType().Name}.{field} 없음");
                return;
            }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBool(Object target, string field, bool value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray(Object target, string field, Object[] values)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
