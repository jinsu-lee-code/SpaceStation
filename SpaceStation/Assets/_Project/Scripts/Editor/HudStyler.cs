using SpaceStation.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 5-8 SF 홀로그램 스타일 일괄 적용 (메뉴 SpaceStation/UI/Apply Holo Style). 여러 번 실행해도 결과가 같다.
    /// - 패널: 모서리 깎인 반투명 바탕(UI_HoloFill) + 청록 테두리·모서리 브래킷(Frame 자식)
    /// - 버튼: UI_HoloButton + 호버·누름 색 변화
    /// - 글자: 크기 위계(제목/본문/보조), 밝은 글자색
    /// - 건설 버튼 프리팹: 모듈 썸네일(Thumb) 추가, 크기 확대 → 탭·상태 표시줄 위치 조정
    /// </summary>
    public static class HudStyler
    {
        private const string ButtonPrefab = "Assets/_Project/Prefabs/UI/PF_BuildButton.prefab";
        private const string TabPrefab = "Assets/_Project/Prefabs/UI/PF_BuildTab.prefab";
        private static readonly Color TextColor = new Color(0.91f, 0.96f, 1f, 1f);

        [MenuItem("SpaceStation/UI/Apply Holo Style")]
        public static void Apply()
        {
            if (HudArtBuilder.Fill == null)
                HudArtBuilder.BuildAll();
            var hud = GameObject.Find("HUD");
            if (hud == null)
            {
                Debug.LogError("[HudStyler] 씬에 HUD 없음");
                return;
            }
            var root = hud.transform;

            // 시간 조절
            var time = root.Find("TimeControls");
            foreach (var b in time.GetComponentsInChildren<Button>(true))
                StyleButton(b, 18f);
            var timeLayout = time.GetComponent<HorizontalLayoutGroup>();
            timeLayout.spacing = 6;

            // 자원 패널
            var resource = root.Find("ResourcePanel");
            Panel(resource.gameObject, HudTheme.PanelFill, HudTheme.AccentDim);
            var rl = resource.GetComponent<VerticalLayoutGroup>();
            rl.padding = new RectOffset(20, 20, 16, 16);
            rl.spacing = 10;
            ((RectTransform)resource).sizeDelta = new Vector2(440f, ((RectTransform)resource).sizeDelta.y);
            foreach (var t in resource.GetComponentsInChildren<TMP_Text>(true))
                StyleText(t, 18f);
            var tooltip = root.Find("Tooltip").GetComponent<TooltipView>();
            Set(resource.GetComponent<ResourcePanel>(), "_tooltip", tooltip);

            // 건설 메뉴 + 탭 + 상태 표시줄
            var build = root.Find("BuildMenu");
            Panel(build.gameObject, HudTheme.PanelFill, HudTheme.AccentDim);
            var bl = build.GetComponent<HorizontalLayoutGroup>();
            bl.padding = new RectOffset(10, 10, 10, 10);
            bl.spacing = 8;
            // 버튼 높이 118 + 위아래 여백 → 메뉴 높이 138 (메뉴는 세로 크기를 직접 정함)
            var brt = (RectTransform)build;
            brt.sizeDelta = new Vector2(brt.sizeDelta.x, 138f);
            var tabs = (RectTransform)root.Find("BuildTabs");
            tabs.anchoredPosition = new Vector2(0f, 162f);
            tabs.GetComponent<HorizontalLayoutGroup>().spacing = 4;
            foreach (var t in tabs.GetComponentsInChildren<TMP_Text>(true))
                StyleText(t, 14f, muted: true);
            var status = (RectTransform)root.Find("StatusBar");
            status.anchoredPosition = new Vector2(0f, 204f);
            foreach (var t in status.GetComponentsInChildren<TMP_Text>(true))
                StyleText(t, 18f);

            // 이벤트 배너
            var banner = root.Find("EventBanner");
            Panel(banner.gameObject, HudTheme.PanelFill, HudTheme.Negative);
            ((RectTransform)banner).sizeDelta = new Vector2(640f, ((RectTransform)banner).sizeDelta.y);
            var bvl = banner.GetComponent<VerticalLayoutGroup>();
            bvl.padding = new RectOffset(22, 22, 12, 14);
            bvl.spacing = 4;
            StyleText(banner.Find("Title").GetComponent<TMP_Text>(), 24f);
            StyleText(banner.Find("Description").GetComponent<TMP_Text>(), 16f, muted: true);
            Set(banner.GetComponent<EventBanner>(), "_frame", banner.Find("Frame").GetComponent<Image>());

            // 선택 패널
            var selection = root.Find("SelectionActions");
            Panel(selection.gameObject, HudTheme.PanelFill, HudTheme.AccentDim);
            var sl = selection.GetComponent<VerticalLayoutGroup>();
            sl.padding = new RectOffset(16, 16, 14, 16);
            sl.spacing = 8;
            StyleText(selection.Find("Title").GetComponent<TMP_Text>(), 19f);
            foreach (var b in selection.GetComponentsInChildren<Button>(true))
                StyleButton(b, 16f);

            // 툴팁
            var tip = root.Find("Tooltip");
            Panel(tip.gameObject, new Color(0.02f, 0.05f, 0.08f, 0.95f), HudTheme.Accent);
            tip.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(16, 16, 12, 12);
            StyleText(tip.GetComponentInChildren<TMP_Text>(true), 16f);

            // 결과 화면
            var result = root.Find("ResultScreen/Panel");
            Panel(result.gameObject, new Color(0.03f, 0.07f, 0.11f, 0.95f), HudTheme.Accent);
            foreach (var b in result.GetComponentsInChildren<Button>(true))
                StyleButton(b, 20f);
            StyleText(result.Find("Title").GetComponent<TMP_Text>(), 44f);
            StyleText(result.Find("Stats").GetComponent<TMP_Text>(), 20f);

            // 파손 마커
            var marker = root.Find("DamageMarkers/MarkerTemplate");
            if (marker != null)
            {
                var img = marker.GetComponent<Image>();
                img.sprite = HudArtBuilder.Button;
                img.type = Image.Type.Sliced;
                img.color = new Color(0.75f, 0.2f, 0.16f, 0.95f);
                StyleText(marker.GetComponentInChildren<TMP_Text>(true), 16f);
            }

            StyleBuildButtonPrefab();
            StyleTabPrefab();
            EditorSceneManager.MarkSceneDirty(hud.scene);
            Debug.Log("[HudStyler] 홀로그램 스타일 적용 완료");
        }

        // ---------------- 도우미 ----------------

        /// <summary>바탕 + Frame 자식 (레이아웃 무시, 전체 늘림, 입력 무시).</summary>
        internal static void Panel(GameObject go, Color fill, Color frame)
        {
            var img = go.GetComponent<Image>();
            img.sprite = HudArtBuilder.Fill;
            img.type = Image.Type.Sliced;
            img.color = fill;
            var f = go.transform.Find("Frame");
            if (f == null)
            {
                var fgo = new GameObject("Frame", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
                fgo.transform.SetParent(go.transform, false);
                f = fgo.transform;
            }
            f.SetAsLastSibling();
            var rt = (RectTransform)f;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            f.GetComponent<LayoutElement>().ignoreLayout = true;
            var fi = f.GetComponent<Image>();
            fi.sprite = HudArtBuilder.Frame;
            fi.type = Image.Type.Sliced;
            fi.color = frame;
            fi.raycastTarget = false;
        }

        internal static void StyleButton(Button b, float fontSize)
        {
            var img = b.targetGraphic as Image;
            if (img != null)
            {
                img.sprite = HudArtBuilder.Button;
                img.type = Image.Type.Sliced;
                img.color = HudTheme.ButtonNormal;
            }
            var colors = b.colors;
            colors.normalColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.6f, 0.6f, 0.6f, 1f);
            colors.selectedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.55f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            b.colors = colors;
            foreach (var t in b.GetComponentsInChildren<TMP_Text>(true))
                StyleText(t, fontSize);
        }

        internal static void StyleText(TMP_Text t, float size, bool muted = false)
        {
            if (t == null)
                return;
            t.fontSize = size;
            t.color = muted ? new Color(0.68f, 0.76f, 0.85f, 1f) : TextColor;
            t.richText = true;
            EditorUtility.SetDirty(t);
        }

        private static void Set(Object target, string field, Object value)
        {
            if (target == null)
                return;
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null)
            {
                Debug.LogWarning($"[HudStyler] {target.GetType().Name}.{field} 없음");
                return;
            }
            p.objectReferenceValue = value;
            so.ApplyModifiedProperties();
        }

        private static void StyleBuildButtonPrefab()
        {
            var root = PrefabUtility.LoadPrefabContents(ButtonPrefab);
            try
            {
                var le = root.GetComponent<LayoutElement>();
                le.preferredWidth = 150f;
                le.preferredHeight = 118f;
                StyleButton(root.GetComponent<Button>(), 16f);

                var thumb = root.transform.Find("Thumb");
                if (thumb == null)
                {
                    var go = new GameObject("Thumb", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                    go.transform.SetParent(root.transform, false);
                    thumb = go.transform;
                }
                thumb.SetSiblingIndex(0);
                var trt = (RectTransform)thumb;
                trt.anchorMin = new Vector2(0f, 1f);
                trt.anchorMax = new Vector2(1f, 1f);
                trt.pivot = new Vector2(0.5f, 1f);
                trt.sizeDelta = new Vector2(-16f, 66f);
                trt.anchoredPosition = new Vector2(0f, -4f);
                var timg = thumb.GetComponent<Image>();
                timg.raycastTarget = false;
                timg.preserveAspect = true;

                var label = (RectTransform)root.transform.Find("Label");
                label.anchorMin = new Vector2(0f, 0f);
                label.anchorMax = new Vector2(1f, 0f);
                label.pivot = new Vector2(0.5f, 0f);
                label.sizeDelta = new Vector2(-10f, 46f);
                label.anchoredPosition = new Vector2(0f, 4f);
                var tmp = label.GetComponent<TMP_Text>();
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.lineSpacing = -8f;

                Set(root.GetComponent<BuildButtonView>(), "_thumb", timg);
                PrefabUtility.SaveAsPrefabAsset(root, ButtonPrefab);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void StyleTabPrefab()
        {
            var root = PrefabUtility.LoadPrefabContents(TabPrefab);
            try
            {
                var le = root.GetComponent<LayoutElement>();
                le.preferredWidth = 104f;
                le.preferredHeight = 32f;
                StyleButton(root.GetComponent<Button>(), 16f);
                PrefabUtility.SaveAsPrefabAsset(root, TabPrefab);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
