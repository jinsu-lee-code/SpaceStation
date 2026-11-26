using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpaceStation.PlayCheck
{
    /// <summary>
    /// 끝 질문 창 (CONNECT_1121 V3, 검증용 빌드에서만): 질문 셋 + 여러 줄 답 칸 + [저장] · [건너뛰기].
    /// 검증용 빌드 전용 오버레이라 장면에 두지 않고 코드로 만든다 (기본 TMP 글꼴 = Maplestory SDF).
    /// </summary>
    public sealed class PlayCheckQuestions : MonoBehaviour
    {
        private static readonly Color Back = new Color(0.03f, 0.05f, 0.09f, 0.94f);
        private static readonly Color Field = new Color(0.10f, 0.13f, 0.19f, 1f);
        private static readonly Color Accent = new Color(0.55f, 0.82f, 1f, 1f);

        private Canvas _canvas;
        private TMP_Text _title;
        private TMP_Text[] _labels;
        private TMP_InputField[] _inputs;
        private Action<string[]> _done;

        public bool Visible => _canvas != null && _canvas.enabled;

        public static PlayCheckQuestions Create(Transform parent)
        {
            var go = new GameObject("PlayCheckQuestions", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var q = go.AddComponent<PlayCheckQuestions>();
            q.Build();
            return q;
        }

        private void Build()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 32000; // 모든 창 위
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            var dim = Panel("Dim", transform, new Color(0f, 0f, 0f, 0.55f));
            Stretch(dim.rectTransform);

            var box = Panel("Box", transform, Back);
            var rt = box.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(1100f, 860f);

            _title = Label("Title", box.transform, 40f, FontStyles.Bold);
            Place(_title.rectTransform, 40f, 30f, 1020f, 60f);

            var sub = Label("Sub", box.transform, 24f, FontStyles.Normal);
            sub.color = new Color(1f, 1f, 1f, 0.7f);
            sub.text = "생각나는 만큼만 짧게 적어 주세요. 비워 두어도 됩니다. 답은 플레이 기록 파일에 저장됩니다.";
            Place(sub.rectTransform, 40f, 92f, 1020f, 40f);

            _labels = new TMP_Text[3];
            _inputs = new TMP_InputField[3];
            for (int i = 0; i < 3; i++)
            {
                float y = 150f + i * 200f;
                _labels[i] = Label($"Q{i}", box.transform, 27f, FontStyles.Bold);
                _labels[i].color = Accent;
                Place(_labels[i].rectTransform, 40f, y, 1020f, 44f);
                _inputs[i] = Input($"A{i}", box.transform);
                Place((RectTransform)_inputs[i].transform, 40f, y + 48f, 1020f, 132f);
            }

            var save = Button("Save", box.transform, "저장", true, () => Finish(true));
            Place((RectTransform)save.transform, 1060f - 260f - 220f - 20f, 770f, 260f, 64f);
            var skip = Button("Skip", box.transform, "건너뛰기", false, () => Finish(false));
            Place((RectTransform)skip.transform, 1060f - 220f, 770f, 220f, 64f);

            _canvas.enabled = false;
        }

        public void Show(string when, string[] questions, Action<string[]> done)
        {
            _done = done;
            _title.text = when == "게임 시간 90분" ? "게임 시간 90분 · 세 가지만 물어볼게요" : $"{when} · 나가기 전에 세 가지만 물어볼게요";
            for (int i = 0; i < _labels.Length; i++)
            {
                _labels[i].text = i < questions.Length ? $"{i + 1}. {questions[i]}" : "";
                _inputs[i].text = "";
            }
            _canvas.enabled = true;
            EnsureEventSystem();
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(_inputs[0].gameObject);
            _inputs[0].ActivateInputField();
            Cursor.lockState = CursorLockMode.None; // 1인칭 안에서도 마우스를 쓸 수 있게
            Cursor.visible = true;
        }

        private void Finish(bool save)
        {
            _canvas.enabled = false;
            string[] answers = null;
            if (save)
            {
                answers = new string[_inputs.Length];
                for (int i = 0; i < _inputs.Length; i++)
                    answers[i] = _inputs[i].text;
            }
            var done = _done;
            _done = null;
            done?.Invoke(answers);
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
                return;
            var es = new GameObject("EventSystem (PlayCheck)", typeof(EventSystem));
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        // ---- 만들기 도우미 ----

        private static Image Panel(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            return img;
        }

        private static TMP_Text Label(string name, Transform parent, float size, FontStyles style)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            t.fontSize = size;
            t.fontStyle = style;
            t.color = Color.white;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.alignment = TextAlignmentOptions.TopLeft;
            t.raycastTarget = false;
            return t;
        }

        private static TMP_InputField Input(string name, Transform parent)
        {
            var bg = Panel(name, parent, Field);
            var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            area.transform.SetParent(bg.transform, false);
            var art = (RectTransform)area.transform;
            Stretch(art);
            art.offsetMin = new Vector2(16f, 10f);
            art.offsetMax = new Vector2(-16f, -10f);
            var text = Label("Text", area.transform, 26f, FontStyles.Normal);
            Stretch(text.rectTransform);
            var hint = Label("Placeholder", area.transform, 26f, FontStyles.Italic);
            hint.text = "여기에 적기";
            hint.color = new Color(1f, 1f, 1f, 0.3f);
            Stretch(hint.rectTransform);
            var input = bg.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = art;
            input.textComponent = text;
            input.placeholder = hint;
            input.lineType = TMP_InputField.LineType.MultiLineNewline;
            input.characterLimit = 2000;
            input.targetGraphic = bg;
            return input;
        }

        private static Button Button(string name, Transform parent, string label, bool primary, Action onClick)
        {
            var bg = Panel(name, parent, primary ? new Color(0.20f, 0.45f, 0.70f, 1f) : new Color(0.22f, 0.25f, 0.30f, 1f));
            var b = bg.gameObject.AddComponent<Button>();
            b.targetGraphic = bg;
            b.onClick.AddListener(() => onClick());
            var t = Label("Label", bg.transform, 28f, FontStyles.Bold);
            t.text = label;
            t.alignment = TextAlignmentOptions.Center;
            Stretch(t.rectTransform);
            return b;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        /// <summary>상자 왼쪽 위 기준 (x, y) 위치 · 크기.</summary>
        private static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }
    }
}
