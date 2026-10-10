using System.Collections.Generic;
using System.IO;
using SpaceStation.Audio;
using SpaceStation.Core;
using SpaceStation.Save;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// 저장·불러오기 창 (Phase 6, 메인 메뉴·ESC 메뉴 공용).
    /// - 저장: 수동 슬롯 3칸, 이미 있으면 덮어쓰기 확인
    /// - 불러오기: 자동 + 수동 3칸, [불러오기] [삭제(확인)]. 게임 중이면 "저장하지 않은 진행은 사라집니다" 확인
    /// 칸마다 썸네일 + 저장 시각·등급·인구·모듈 수·플레이 시간·난이도. 호환되지 않는 저장은 표시만.
    /// 내용은 처음 열 때 코드로 만든다. ESC: 확인창 → 닫기 (InputGate.ConsumeEscape로 다른 ESC 처리 건너뜀).
    /// </summary>
    [DefaultExecutionOrder(-300)]
    public sealed class SaveLoadPanel : MonoBehaviour
    {
        private enum Mode { Save, Load }

        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private Sprite _fillSprite;
        [SerializeField] private Sprite _frameSprite;
        [SerializeField] private Sprite _buttonSprite;
        [Tooltip("11-15 테크 홀로그램 창")]
        [SerializeField] private HoloArt _holoArt;
        [Tooltip("게임 씬: 불러오기 전에 지금 판이 사라진다고 확인")]
        [SerializeField] private bool _inGame;

        private const float RowHeight = 132f;
        private const float WindowWidth = 980f;

        private sealed class Row
        {
            public string Slot;
            public RawImage Thumb;
            public TMP_Text Title;
            public TMP_Text Detail;
            public Button Primary;
            public TMP_Text PrimaryLabel;
            public Button Delete;
            public SaveSlotInfo Info;
        }

        private HoloUi _ui;
        private bool _built;
        private bool _open;
        private Mode _mode;
        private CanvasGroup _group;
        private RectTransform _window;
        private UiTween _tween;
        private HoloFx _fx;
        private TMP_Text _title;
        private readonly List<Row> _rows = new List<Row>();
        private readonly List<Texture2D> _thumbs = new List<Texture2D>();
        private GameObject _confirm;
        private TMP_Text _confirmText;
        private System.Action _confirmAction;

        public bool IsOpen => _open;

        private void Awake()
        {
            _group = GetComponent<CanvasGroup>();
            if (_group == null)
                _group = gameObject.AddComponent<CanvasGroup>();
            SetVisible(false);
            _group.alpha = 0f;
        }

        private void OnDestroy() => ClearThumbs();

        public void OpenSave() => Open(Mode.Save);
        public void OpenLoad() => Open(Mode.Load);

        private void Open(Mode mode)
        {
            if (!_built)
                Build();
            _mode = mode;
            _open = true;
            transform.SetAsLastSibling();
            SetVisible(true);
            HideConfirm();
            Refresh();
            _tween.Play();
            _fx?.Replay();
            AudioService.TryPlay(l => l.UiOpen);
        }

        public void Close()
        {
            if (!_open)
                return;
            _open = false;
            SetVisible(false);
            HideConfirm();
            _tween.Hide();
            AudioService.TryPlay(l => l.UiClose);
        }

        private void Update()
        {
            if (!_built)
                return;
            _tween.Update();
            var keyboard = Keyboard.current;
            if (!_open || keyboard == null || !keyboard.escapeKey.wasPressedThisFrame)
                return;
            InputGate.ConsumeEscape();
            if (_confirm.activeSelf)
                HideConfirm();
            else
                Close();
        }

        private void SetVisible(bool on)
        {
            _group.blocksRaycasts = on;
            _group.interactable = on;
        }

        // ---------------- 구성 ----------------

        private void Build()
        {
            _built = true;
            _ui = HoloUi.For(_holoArt, _font, _fillSprite, _frameSprite, _buttonSprite);
            var root = (RectTransform)transform;
            var dim = GetComponent<Image>();
            if (dim == null)
                dim = gameObject.AddComponent<Image>();
            dim.color = new Color(0f, 0.01f, 0.03f, 0.72f);

            float height = 150f + 4 * (RowHeight + 12f) + 70f;
            _window = HoloUi.Rect("Window", root);
            _window.anchorMin = _window.anchorMax = _window.pivot = new Vector2(0.5f, 0.5f);
            _window.sizeDelta = new Vector2(WindowWidth, height);
            _fx = _ui.Window(_window.gameObject, new Color(0.03f, 0.07f, 0.11f, 0.97f), "ARCHIVE");

            _title = _ui.Label(_window, "", 34f, TextAlignmentOptions.TopLeft);
            _title.fontStyle = FontStyles.Bold;
            HoloUi.Place(_title.rectTransform, new Vector2(44f, -28f), new Vector2(600f, 48f));
            var hint = _ui.Label(_window, "<color=#AFC4D8>자동 저장은 설정의 간격마다, 메인 메뉴로 나갈 때, 게임을 끌 때 저장됩니다 · 게임 오버가 되면 자동 저장은 지워집니다</color>",
                15f, TextAlignmentOptions.TopLeft);
            HoloUi.Place(hint.rectTransform, new Vector2(46f, -82f), new Vector2(WindowWidth - 90f, 26f));

            for (int i = 0; i < 4; i++)
                _rows.Add(BuildRow(new Vector2(44f, -122f - i * (RowHeight + 12f))));

            var close = _ui.Button(_window, "닫기 (ESC)", 19f, Close, clickSound: false);
            var crt = (RectTransform)close.transform;
            crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(1f, 0f);
            crt.anchoredPosition = new Vector2(-44f, 26f);
            crt.sizeDelta = new Vector2(200f, 48f);

            BuildConfirm();
            _tween = new UiTween(_window, _group, new Vector2(0f, -24f), 0.2f, 0.15f, glitch: true);
        }

        private Row BuildRow(Vector2 topLeft)
        {
            var row = new Row();
            var rt = HoloUi.Rect("Slot", _window);
            HoloUi.Place(rt, topLeft, new Vector2(WindowWidth - 88f, RowHeight));
            _ui.Panel(rt.gameObject, new Color(0.05f, 0.11f, 0.16f, 0.9f), HudTheme.AccentDim);

            var thumbRt = HoloUi.Rect("Thumb", rt);
            HoloUi.Place(thumbRt, new Vector2(12f, -12f), new Vector2(192f, 108f));
            row.Thumb = thumbRt.gameObject.AddComponent<RawImage>();
            row.Thumb.raycastTarget = false;

            row.Title = _ui.Label(rt, "", 21f, TextAlignmentOptions.TopLeft);
            HoloUi.Place(row.Title.rectTransform, new Vector2(224f, -14f), new Vector2(420f, 30f));
            row.Detail = _ui.Label(rt, "", 15f, TextAlignmentOptions.TopLeft, wrap: true);
            HoloUi.Place(row.Detail.rectTransform, new Vector2(224f, -50f), new Vector2(430f, 72f));

            row.Primary = _ui.Button(rt, "", 18f, () => HandlePrimary(row), clickSound: false);
            HoloUi.Place((RectTransform)row.Primary.transform, new Vector2(WindowWidth - 88f - 206f, -18f), new Vector2(190f, 46f));
            row.PrimaryLabel = row.Primary.GetComponentInChildren<TMP_Text>();
            row.Delete = _ui.Button(rt, "삭제", 16f, () => HandleDelete(row));
            HoloUi.Place((RectTransform)row.Delete.transform, new Vector2(WindowWidth - 88f - 206f, -72f), new Vector2(190f, 40f));
            return row;
        }

        private void BuildConfirm()
        {
            var overlay = HoloUi.Rect("Confirm", _window);
            HoloUi.Stretch(overlay);
            var shade = overlay.gameObject.AddComponent<Image>();
            shade.color = new Color(0f, 0.01f, 0.03f, 0.75f);
            var box = HoloUi.Rect("Box", overlay);
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(620f, 230f);
            _ui.Panel(box.gameObject, new Color(0.04f, 0.09f, 0.14f, 0.98f), HudTheme.Accent);
            _confirmText = _ui.Label(box, "", 20f, TextAlignmentOptions.Center, wrap: true);
            HoloUi.Place(_confirmText.rectTransform, new Vector2(30f, -26f), new Vector2(560f, 100f));
            var yes = _ui.Button(box, "확인", 19f, () =>
            {
                var action = _confirmAction;
                HideConfirm();
                action?.Invoke();
            }, clickSound: false);
            HoloUi.Place((RectTransform)yes.transform, new Vector2(110f, -150f), new Vector2(180f, 50f));
            var no = _ui.Button(box, "취소 (ESC)", 19f, HideConfirm);
            HoloUi.Place((RectTransform)no.transform, new Vector2(330f, -150f), new Vector2(180f, 50f));
            _confirm = overlay.gameObject;
            _confirm.SetActive(false);
        }

        private void ShowConfirm(string message, System.Action onYes)
        {
            _confirmText.SetText(message);
            _confirmAction = onYes;
            _confirm.SetActive(true);
            _confirm.transform.SetAsLastSibling();
            AudioService.TryPlay(l => l.UiOpen);
        }

        private void HideConfirm()
        {
            _confirmAction = null;
            if (_confirm != null)
                _confirm.SetActive(false);
        }

        // ---------------- 표시 ----------------

        private void Refresh()
        {
            _title.SetText(_mode == Mode.Save ? "저장" : "불러오기");
            ClearThumbs();
            var slots = SaveService.ListSlots(includeAuto: _mode == Mode.Load);
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                bool used = i < slots.Count;
                row.Primary.transform.parent.gameObject.SetActive(used);
                if (!used)
                    continue;
                var info = slots[i];
                row.Slot = info.Id;
                row.Info = info;
                row.Title.SetText($"<b>{info.Title}</b>");
                row.Detail.SetText(Describe(info));
                row.Thumb.texture = LoadThumb(info.ThumbnailPath);
                row.Thumb.color = row.Thumb.texture != null ? Color.white : new Color(0.08f, 0.14f, 0.2f, 1f);

                bool ok = info.Status == SaveSlotStatus.Ok;
                if (_mode == Mode.Save)
                {
                    bool canSave = SaveManager.Instance != null && SaveManager.Instance.CanSave;
                    row.PrimaryLabel.SetText(info.Status == SaveSlotStatus.Empty ? "여기에 저장" : "덮어쓰기");
                    row.Primary.interactable = canSave;
                }
                else
                {
                    row.PrimaryLabel.SetText("불러오기");
                    row.Primary.interactable = ok;
                }
                row.Delete.gameObject.SetActive(info.Status != SaveSlotStatus.Empty);
            }
        }

        private static string Describe(SaveSlotInfo info)
        {
            switch (info.Status)
            {
                case SaveSlotStatus.Empty:
                    return "<color=#AFC4D8>비어 있음</color>";
                case SaveSlotStatus.Incompatible:
                    return $"<color={HudText.Orange}>호환되지 않는 저장입니다 (더 새로운 버전이거나 파일이 손상됨)</color>";
            }
            var m = info.Meta;
            int t = Mathf.FloorToInt(m.PlaySeconds);
            string difficulty = string.IsNullOrEmpty(m.DifficultyName) ? "" : $" · 난이도 {m.DifficultyName}";
            string top = m.ReachedTopGrade ? $"  <color=#FFD36A>★ {m.TopGradeName} 달성</color>" : ""; // 8-6
            return $"{m.SavedAt:yyyy-MM-dd HH:mm}  ·  {m.GradeName}{top}\n" +
                   $"<color=#AFC4D8>인구 {m.Population} · 모듈 {m.Modules} · 플레이 {t / 60}:{t % 60:00}{difficulty}</color>";
        }

        private Texture2D LoadThumb(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (!tex.LoadImage(File.ReadAllBytes(path)))
            {
                Destroy(tex);
                return null;
            }
            _thumbs.Add(tex);
            return tex;
        }

        private void ClearThumbs()
        {
            foreach (var t in _thumbs)
                if (t != null)
                    Destroy(t);
            _thumbs.Clear();
        }

        // ---------------- 명령 ----------------

        private void HandlePrimary(Row row)
        {
            if (_mode == Mode.Save)
            {
                if (row.Info.Status == SaveSlotStatus.Empty)
                    SaveTo(row.Slot);
                else
                    ShowConfirm($"{row.Info.Title}에 덮어쓸까요?\n<size=75%><color=#AFC4D8>이전 저장은 사라집니다</color></size>", () => SaveTo(row.Slot));
                return;
            }
            if (_inGame)
                ShowConfirm($"이 저장을 불러올까요? ({row.Info.Title})\n<size=75%><color=#AFC4D8>저장하지 않은 지금 진행은 사라집니다</color></size>", () => Load(row.Slot));
            else
                Load(row.Slot);
        }

        private void SaveTo(string slot)
        {
            bool ok = SaveManager.Instance != null && SaveManager.Instance.SaveTo(slot, true);
            AudioService.TryPlay(l => ok ? l.BuildSelect : l.UiError);
            Refresh();
        }

        private void Load(string slot)
        {
            if (SaveManager.LoadAndPlay(slot))
            {
                AudioService.TryPlay(l => l.BuildSelect);
                SetVisible(false);
            }
            else
            {
                AudioService.TryPlay(l => l.UiError);
                Refresh();
            }
        }

        private void HandleDelete(Row row)
        {
            ShowConfirm($"이 저장을 삭제할까요? ({row.Info.Title})\n<size=75%><color=#AFC4D8>되돌릴 수 없습니다</color></size>", () =>
            {
                SaveService.Delete(row.Slot);
                AudioService.TryPlay(l => l.UiClose);
                Refresh();
            });
        }
    }
}
