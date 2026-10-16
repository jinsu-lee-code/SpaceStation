using System;
using System.Collections.Generic;
using SpaceStation.Audio;
using SpaceStation.Core;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// 13-6a 왼쪽 위 [관리] 메뉴 (사용자 결정 2026-10-16). 창 버튼(연구 · 주민 · 합성 · 정책 · 외부 접촉 · 타이쿤판 시스템)을 버튼 하나로 묶는다.
    /// - [관리]를 누르면 아래로 칸이 펼쳐짐: 파트마다 아이콘 + 이름 + 단축키 키캡. 단축키는 그대로 (펼치지 않아도 바로 열림)
    /// - 열린 시스템만 순서대로 보임 (각 창이 <see cref="Entry.Visible"/>로 알림). 새로 열리면 '새로' 표시 + [관리]가 깜박임 (색이 아니라 글자 · 밝기)
    /// - 고를 것이 있는 칸(<see cref="Entry.Attention"/>)은 '!' 표시 + [관리] 깜박임
    /// - 칸을 누르거나 바깥을 누르거나 ESC · 다른 창이 열리면 접힘
    /// 각 창은 Start에서 <see cref="For"/>로 얻어 <see cref="Add"/>한다 (씬 연결 없음 — 첫 창이 만들면 나머지는 같은 것을 씀).
    /// </summary>
    public sealed class HudLauncher : MonoBehaviour
    {
        /// <summary>칸 순서 (작을수록 왼쪽).</summary>
        public static class Order
        {
            public const int Research = 10, Roster = 20, Recipes = 30, Policies = 40, Contact = 50, Systems = 60;
        }

        public sealed class Entry
        {
            /// <summary>아이콘 키 (HoloArt.LauncherIcon) · 정렬이 같을 때 구분.</summary>
            public string Key;
            public int Order;
            public string Name;
            /// <summary>단축키 글자 (없으면 키캡 없음).</summary>
            public Func<string> KeyLabel;
            /// <summary>시스템이 열렸는지 (null = 늘 열림).</summary>
            public Func<bool> Visible;
            /// <summary>고를 것 · 볼 것이 있음 ('!').</summary>
            public Func<bool> Attention;
            /// <summary>그 창이 열려 있음 (칸 강조).</summary>
            public Func<bool> IsOpen;
            public Action Click;

            internal Button Button;
            internal RectTransform Rect;
            internal Image Icon;
            internal TMP_Text KeyText, Badge;
            internal GameObject KeyCap;
            internal bool Shown, New;
        }

        private const float TileW = 100f, TileH = 104f, Gap = 8f, Pad = 10f;
        private const float BlinkSeconds = 0.45f;

        private static HudLauncher _instance;

        private HoloUi _ui;
        private readonly List<Entry> _entries = new List<Entry>();
        private RectTransform _button, _drawer;
        private TMP_Text _buttonLabel;
        private CanvasGroup _group;
        private UiTween _tween;
        private bool _open, _baseline;
        private float _nextRefresh;
        private string _shownLabel;

        public bool IsOpen => _open;

        /// <summary>창 루트(전체 화면 RectTransform)와 같은 부모 아래의 관리 메뉴 (없으면 만듦).</summary>
        public static HudLauncher For(HoloUi ui, Transform panelRoot)
        {
            if (_instance != null)
                return _instance;
            var rt = HoloUi.Rect("HudLauncher", panelRoot.parent != null ? panelRoot.parent : panelRoot);
            HoloUi.Stretch(rt);
            _instance = rt.gameObject.AddComponent<HudLauncher>();
            _instance.Build(ui);
            return _instance;
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
            HudWindows.Opened -= HandleWindowOpened;
            KeyBindings.Changed -= RefreshKeys;
        }

        public void Add(Entry entry)
        {
            if (entry == null || _entries.Contains(entry))
                return;
            BuildTile(entry);
            _entries.Add(entry);
            _entries.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Key, b.Key));
            _nextRefresh = 0f;
        }

        // ---------------- 구성 ----------------

        private void Build(HoloUi ui)
        {
            _ui = ui;
            var root = (RectTransform)transform;
            var button = _ui.TechButton(root, "관리", 19f, Toggle);
            _button = (RectTransform)button.transform;
            HoloUi.Place(_button, new Vector2(24f, -24f), new Vector2(150f, 44f));
            _buttonLabel = button.GetComponentInChildren<TMP_Text>();

            _drawer = HoloUi.Rect("LauncherDrawer", root);
            HoloUi.Place(_drawer, new Vector2(24f, -74f), new Vector2(TileW + Pad * 2f, TileH + Pad * 2f));
            _ui.Panel(_drawer.gameObject, new Color(HudTheme.PanelFill.r, HudTheme.PanelFill.g, HudTheme.PanelFill.b, 0.92f), HudTheme.AccentDim);
            _group = _drawer.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _tween = new UiTween(_drawer, _group, new Vector2(0f, 12f));
            HudWindows.Opened += HandleWindowOpened;
            KeyBindings.Changed += RefreshKeys;
        }

        private void BuildTile(Entry e)
        {
            var button = _ui.TechButton(_drawer, "", 15f, () => Pick(e));
            e.Button = button;
            e.Rect = (RectTransform)button.transform;
            HoloUi.Place(e.Rect, new Vector2(Pad, -Pad), new Vector2(TileW, TileH));
            foreach (var t in button.GetComponentsInChildren<TMP_Text>())
                if (t.text == "")
                    t.gameObject.SetActive(false); // 테크 버튼 기본 글자 칸은 안 씀

            var icon = HoloUi.Rect("Icon", e.Rect);
            HoloUi.Place(icon, new Vector2((TileW - 54f) / 2f, -12f), new Vector2(54f, 54f));
            e.Icon = icon.gameObject.AddComponent<Image>();
            e.Icon.sprite = _ui.Art != null ? _ui.Art.LauncherIcon(e.Key) : null;
            e.Icon.preserveAspect = true;
            e.Icon.raycastTarget = false;
            e.Icon.color = HudTheme.HoloBright;
            e.Icon.enabled = e.Icon.sprite != null;

            var name = _ui.Label(e.Rect, e.Name, 16f, TextAlignmentOptions.Center);
            HoloUi.Place(name.rectTransform, new Vector2(2f, -70f), new Vector2(TileW - 4f, 26f));
            HoloUi.Glow(name, 0.3f);
            if (!e.Icon.enabled)
                name.rectTransform.anchoredPosition = new Vector2(2f, -40f); // 아이콘이 없으면 가운데로

            // 오른쪽 위 키캡
            var cap = HoloUi.Rect("KeyCap", e.Rect);
            HoloUi.Place(cap, new Vector2(TileW - 32f, -6f), new Vector2(26f, 24f));
            var capImg = cap.gameObject.AddComponent<Image>();
            capImg.sprite = _ui.Art != null && _ui.Art.Button != null ? _ui.Art.Button : null;
            capImg.type = Image.Type.Sliced;
            capImg.color = HudTheme.HoloLow;
            capImg.raycastTarget = false;
            e.KeyText = _ui.Label(cap, "", 14f, TextAlignmentOptions.Center);
            HoloUi.Stretch(e.KeyText.rectTransform);
            e.KeyCap = cap.gameObject;

            // 왼쪽 위 표시 ('새로' · '!')
            e.Badge = _ui.Label(e.Rect, "", 13f, TextAlignmentOptions.TopLeft);
            HoloUi.Place(e.Badge.rectTransform, new Vector2(7f, -5f), new Vector2(60f, 20f));
            e.Badge.color = HudTheme.Warning;
            RefreshKey(e);
            e.Rect.gameObject.SetActive(false);
        }

        private void RefreshKeys()
        {
            foreach (var e in _entries)
                RefreshKey(e);
        }

        private static void RefreshKey(Entry e)
        {
            string key = e.KeyLabel?.Invoke();
            e.KeyCap.SetActive(!string.IsNullOrEmpty(key));
            e.KeyText.SetText(key ?? "");
        }

        // ---------------- 동작 ----------------

        private void Update()
        {
            if (_ui == null)
                return;
            _tween.Update();
            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 0.25f;
                Refresh();
            }
            Blink();
            if (!_open)
                return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !InputGate.EscapeConsumedThisFrame)
            {
                InputGate.ConsumeEscape();
                Close(sound: true);
                return;
            }
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && !Over(mouse.position.ReadValue()))
                Close(sound: false); // 바깥을 누르면 접힘 (그 클릭은 그대로 감)
        }

        private bool Over(Vector2 screen)
        {
            var canvas = GetComponentInParent<Canvas>();
            var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            return RectTransformUtility.RectangleContainsScreenPoint(_button, screen, cam) || RectTransformUtility.RectangleContainsScreenPoint(_drawer, screen, cam);
        }

        /// <summary>열린 칸 · 순서 · 표시를 다시 맞춤.</summary>
        private void Refresh()
        {
            int shown = 0;
            foreach (var e in _entries)
            {
                bool visible = e.Visible == null || e.Visible();
                if (visible && !e.Shown && _baseline)
                    e.New = true; // 판 도중에 새로 열림 (처음부터 열려 있던 것은 아님)
                e.Shown = visible;
                e.Rect.gameObject.SetActive(visible);
                if (!visible)
                    continue;
                e.Rect.anchoredPosition = new Vector2(Pad + shown * (TileW + Gap), -Pad);
                shown++;
                bool selected = e.IsOpen != null && e.IsOpen();
                if (e.Button.targetGraphic != null)
                    e.Button.targetGraphic.color = selected ? HudTheme.ButtonSelected : HudTheme.ButtonNormal;
            }
            if (_entries.Count > 0)
                _baseline = true;
            _drawer.sizeDelta = new Vector2(Pad * 2f + Mathf.Max(1, shown) * TileW + Mathf.Max(0, shown - 1) * Gap, TileH + Pad * 2f);
        }

        /// <summary>'!' · '새로' 표시와 [관리] 글자 (깜박임 = 밝기 · 굵기, 색에 기대지 않음).</summary>
        private void Blink()
        {
            bool on = Mathf.Repeat(Time.unscaledTime, BlinkSeconds * 2f) < BlinkSeconds;
            int attention = 0, fresh = 0;
            foreach (var e in _entries)
            {
                if (!e.Shown)
                    continue;
                bool alert = e.Attention != null && e.Attention();
                if (alert)
                    attention++;
                else if (e.New)
                    fresh++;
                string badge = alert ? "<b>! 확인</b>" : e.New ? "<b>새로</b>" : "";
                if (e.Badge.text != badge)
                    e.Badge.SetText(badge);
                e.Badge.alpha = alert && !on ? 0.35f : 1f;
            }
            string label;
            if (attention > 0)
                label = on ? "<b>관리  ! 확인</b>" : "관리  ! 확인";
            else if (fresh > 0)
                label = on ? $"<b>관리  새로 {fresh}</b>" : $"관리  새로 {fresh}";
            else
                label = _open ? "관리  ▲" : "관리  ▼";
            if (label != _shownLabel)
            {
                _shownLabel = label;
                _buttonLabel.SetText(label);
                _button.sizeDelta = new Vector2(attention > 0 || fresh > 0 ? 190f : 150f, 44f);
            }
        }

        public void Toggle()
        {
            if (_open)
                Close(sound: true);
            else
                Open();
        }

        private void Open()
        {
            _open = true;
            transform.SetAsLastSibling(); // 연구 · 합성 추적기 위로
            Refresh();
            _group.blocksRaycasts = true;
            _group.interactable = true;
            _tween.Play();
            _shownLabel = null;
            AudioService.TryPlay(l => l.UiOpen);
        }

        private void Close(bool sound)
        {
            if (!_open)
                return;
            _open = false;
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _tween.Hide();
            _shownLabel = null;
            foreach (var e in _entries)
                e.New = false; // 펼쳐서 봤으면 '새로'는 끝
            if (sound)
                AudioService.TryPlay(l => l.UiClose);
        }

        private void Pick(Entry e)
        {
            e.New = false;
            Close(sound: false);
            e.Click?.Invoke();
        }

        private void HandleWindowOpened(object window) => Close(sound: false);
    }
}
