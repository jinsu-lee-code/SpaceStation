using System.Collections.Generic;
using System.Text;
using SpaceStation.Audio;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// 13-1 타이쿤판 해금 선택 화면 (사용자 결정 2026-10-15: 알림 + 나중에 고름, 게임은 멈추지 않음).
    /// - 선택 대기 알림 (화면 위쪽 가운데): "새 시스템 선택 · 중형" + [고르기]. 고를 때까지 계속 보임.
    /// - 선택 창: 후보 카드 2~3장(이름 · 위기 + 대응 도구 설명 · 안 고르면 어떻게 되는지) + 열린 시스템 · 다음 등급 예정.
    /// - 왼쪽 위 [시스템] 버튼으로 언제든 열린 시스템 목록 확인.
    /// 상태는 색만이 아니라 글자로 (사용자 색약).
    /// </summary>
    [DefaultExecutionOrder(-250)]
    public sealed class UnlockChoicePanel : MonoBehaviour
    {
        private const float WindowWidth = 1200f;
        private const float WindowHeight = 700f;
        private const float NoticeWidth = 760f;
        private const int MaxCards = 3;
        private const float CardWidth = 362f;
        private const float CardGap = 17f;
        private const string Soft = "#C8D2DE";

        [SerializeField] private StationController _station;
        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private Sprite _fillSprite;
        [SerializeField] private Sprite _frameSprite;
        [SerializeField] private Sprite _buttonSprite;
        [SerializeField] private HoloArt _holoArt;

        private HoloUi _ui;
        private StationSimulation _sim;
        private UnlockChoiceSystem _choices;
        private CanvasGroup _group;
        private RectTransform _window;
        private UiTween _tween;
        private HoloFx _fx;
        private bool _open;
        private TMP_Text _launcherLabel, _title, _summary, _status;
        private readonly List<(RectTransform Card, TMP_Text Text, Button Pick)> _cards = new List<(RectTransform, TMP_Text, Button)>();

        private RectTransform _notice;
        private CanvasGroup _noticeGroup;
        private TMP_Text _noticeText;

        public bool IsOpen => _open;

        private void Start()
        {
            _sim = _station != null ? _station.Simulation : null;
            _choices = _sim?.GetSystem<UnlockChoiceSystem>();
            if (_choices == null)
            {
                gameObject.SetActive(false);
                return;
            }
            _ui = HoloUi.For(_holoArt, _font, _fillSprite, _frameSprite, _buttonSprite);
            BuildLauncher();
            BuildWindow();
            BuildNotice();
            HudWindows.Opened += HandleWindowOpened;
            _choices.OfferChanged += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            HudWindows.Opened -= HandleWindowOpened;
            if (_choices != null)
                _choices.OfferChanged -= Refresh;
        }

        private void Update()
        {
            if (_ui == null)
                return;
            _tween.Update();
            var keyboard = Keyboard.current;
            if (_open && keyboard != null && !InputGate.Blocked && keyboard.escapeKey.wasPressedThisFrame && !InputGate.EscapeConsumedThisFrame)
            {
                InputGate.ConsumeEscape();
                Close();
            }
        }

        public void Toggle()
        {
            if (_open)
                Close();
            else
                Open();
        }

        public void Open()
        {
            HudWindows.NotifyOpened(this);
            transform.SetAsLastSibling();
            _open = true;
            _group.blocksRaycasts = true;
            _group.interactable = true;
            _tween.Play();
            _fx?.Replay();
            Refresh();
            AudioService.TryPlay(l => l.UiOpen);
        }

        private void HandleWindowOpened(object window)
        {
            if (!ReferenceEquals(window, this))
                Close(sound: false);
        }

        public void Close() => Close(sound: true);

        private void Close(bool sound)
        {
            if (!_open)
                return;
            _open = false;
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _tween.Hide();
            if (sound)
                AudioService.TryPlay(l => l.UiClose);
        }

        // ---------------- 구성 ----------------

        private void BuildLauncher()
        {
            var button = _ui.TechButton((RectTransform)transform, "시스템", 19f, Toggle);
            HoloUi.Place((RectTransform)button.transform, new Vector2(840f, -24f), new Vector2(170f, 44f)); // 외부 접촉 버튼 오른쪽
            _launcherLabel = button.GetComponentInChildren<TMP_Text>();
        }

        private void BuildWindow()
        {
            var root = (RectTransform)transform;
            _window = HoloUi.Rect("UnlockChoiceWindow", root);
            _window.anchorMin = _window.anchorMax = _window.pivot = new Vector2(0.5f, 0.5f);
            _window.anchoredPosition = new Vector2(-120f, 40f);
            _window.sizeDelta = new Vector2(WindowWidth, WindowHeight);
            _group = _window.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _fx = _ui.Window(_window.gameObject, new Color(0.02f, 0.045f, 0.07f, 0.99f), "SYSTEMS");
            var back = HoloUi.Rect("Backing", _window);
            HoloUi.Stretch(back, new Vector2(6f, 6f), new Vector2(-6f, -6f));
            back.SetAsFirstSibling();
            var backImage = back.gameObject.AddComponent<Image>();
            backImage.color = new Color(0.015f, 0.03f, 0.05f, 0.97f);
            backImage.raycastTarget = false;

            _title = _ui.Label(_window, "", 34f, TextAlignmentOptions.TopLeft);
            _title.fontStyle = FontStyles.Bold;
            HoloUi.Place(_title.rectTransform, new Vector2(40f, -28f), new Vector2(WindowWidth - 80f, 50f));
            HoloUi.Glow(_title, 0.5f);
            _summary = _ui.Label(_window, "", 19f, TextAlignmentOptions.TopLeft, wrap: true);
            HoloUi.Place(_summary.rectTransform, new Vector2(40f, -80f), new Vector2(WindowWidth - 80f, 56f));

            const float cardW = CardWidth, cardH = 340f;
            for (int i = 0; i < MaxCards; i++)
            {
                int index = i;
                var card = HoloUi.Rect("Option" + i, _window);
                HoloUi.Place(card, new Vector2(40f + i * (cardW + CardGap), -146f), new Vector2(cardW, cardH));
                _ui.Panel(card.gameObject, new Color(0.03f, 0.06f, 0.09f, 0.98f), HudTheme.AccentDim);
                var text = _ui.Label(card, "", 19f, TextAlignmentOptions.TopLeft, wrap: true);
                HoloUi.Stretch(text.rectTransform, new Vector2(18f, 78f), new Vector2(-14f, -14f));
                var pick = _ui.TechButton(card, "이것 먼저 열기", 20f, () => Pick(index));
                var prt = (RectTransform)pick.transform;
                prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0f);
                prt.anchoredPosition = new Vector2(0f, 16f);
                prt.sizeDelta = new Vector2(cardW - 36f, 52f);
                _cards.Add((card, text, pick));
            }

            _status = _ui.Label(_window, "", 18f, TextAlignmentOptions.TopLeft, wrap: true);
            _status.rectTransform.anchorMin = _status.rectTransform.anchorMax = _status.rectTransform.pivot = new Vector2(0f, 0f);
            _status.rectTransform.anchoredPosition = new Vector2(40f, 26f);
            _status.rectTransform.sizeDelta = new Vector2(900f, 150f);

            var close = _ui.Button(_window, "닫기 (ESC)", 20f, Close);
            var crt = (RectTransform)close.transform;
            crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(1f, 0f);
            crt.anchoredPosition = new Vector2(-40f, 26f);
            crt.sizeDelta = new Vector2(190f, 48f);

            HoloGlitch.Add(_window, _holoArt);
            _tween = new UiTween(_window, _group, new Vector2(0f, -24f), 0.2f, 0.15f, glitch: true);
        }

        private void BuildNotice()
        {
            _notice = HoloUi.Rect("UnlockChoiceNotice", (RectTransform)transform);
            _notice.anchorMin = _notice.anchorMax = _notice.pivot = new Vector2(0.5f, 1f);
            _notice.anchoredPosition = new Vector2(0f, -150f);
            _notice.sizeDelta = new Vector2(NoticeWidth, 84f);
            _ui.Panel(_notice.gameObject, new Color(0.03f, 0.05f, 0.08f, 0.98f), HudTheme.Accent);
            _notice.SetAsFirstSibling(); // 창이 열려 있으면 그 뒤로
            var back = HoloUi.Rect("Backing", _notice);
            HoloUi.Stretch(back, new Vector2(4f, 4f), new Vector2(-4f, -4f));
            back.SetAsFirstSibling();
            var bi = back.gameObject.AddComponent<Image>();
            bi.color = new Color(0.02f, 0.035f, 0.055f, 0.97f);
            bi.raycastTarget = false;
            _noticeGroup = _notice.gameObject.AddComponent<CanvasGroup>();
            _noticeText = _ui.Label(_notice, "", 20f, TextAlignmentOptions.MidlineLeft, wrap: true);
            HoloUi.Stretch(_noticeText.rectTransform, new Vector2(22f, 8f), new Vector2(-200f, -8f));
            var go = _ui.TechButton(_notice, "고르기", 21f, Open);
            var grt = (RectTransform)go.transform;
            grt.anchorMin = grt.anchorMax = grt.pivot = new Vector2(1f, 0.5f);
            grt.anchoredPosition = new Vector2(-16f, 0f);
            grt.sizeDelta = new Vector2(166f, 56f);
        }

        private void Pick(int index)
        {
            if (index < _choices.Offer.Count && _choices.Choose(index))
                AudioService.TryPlay(l => l.UiClick);
            else
                AudioService.TryPlay(l => l.UiError);
            Refresh();
        }

        // ---------------- 표시 ----------------

        private string GradeName(int grade) =>
            grade >= 0 && grade < _sim.Progression.GradeCount ? _sim.Progression.GetGrade(grade).DisplayName : "?";

        private static string Names(IEnumerable<UnlockOption> options)
        {
            var sb = new StringBuilder();
            foreach (var o in options)
                sb.Append(sb.Length > 0 ? " · " : "").Append(o.DisplayName);
            return sb.Length > 0 ? sb.ToString() : "없음";
        }

        private void Refresh()
        {
            if (_choices == null || _ui == null)
                return;
            bool pending = _choices.Pending;
            _launcherLabel.SetText(pending ? "시스템 <b>(선택!)</b>" : "시스템");

            bool showNotice = pending && !_sim.Session.IsGameOver;
            _noticeGroup.alpha = showNotice ? 1f : 0f;
            _noticeGroup.blocksRaycasts = showNotice;
            _noticeGroup.interactable = showNotice;
            if (showNotice)
                _noticeText.SetText($"{HudTheme.Icon("info")} <b>새 시스템 선택 · {GradeName(_choices.OfferGrade)}</b>\n"
                                    + $"<size=86%><color={Soft}>후보 {_choices.Offer.Count}개 중 하나를 먼저 엽니다 · 고를 때까지 기다림 (게임은 계속 진행)</color></size>");

            if (pending)
            {
                _title.SetText($"새 시스템 선택 · {GradeName(_choices.OfferGrade)}");
                _summary.SetText($"<color={Soft}>하나를 먼저 엽니다. 위기와 대응 도구가 함께 열려요. <b>새 후보</b>는 안 고르면 다음 등급 후보로 넘어가고, <b>넘어온 후보</b>는 또 안 고르면 다음 등급에 자동으로 열립니다.</color>");
            }
            else
            {
                _title.SetText("시스템");
                _summary.SetText($"<color={Soft}>지금 고를 후보가 없습니다. 등급이 오르면 새 시스템 후보가 나와요.</color>");
            }

            int shown = pending ? Mathf.Min(_choices.Offer.Count, _cards.Count) : 0;
            float left = (WindowWidth - (shown * CardWidth + (shown - 1) * CardGap)) / 2f; // 후보 수만큼 가운데 정렬
            for (int i = 0; i < _cards.Count; i++)
            {
                var (card, text, pick) = _cards[i];
                bool on = i < shown;
                card.gameObject.SetActive(on);
                if (!on)
                    continue;
                card.anchoredPosition = new Vector2(left + i * (CardWidth + CardGap), card.anchoredPosition.y);
                var c = _choices.Offer[i];
                var sb = new StringBuilder();
                sb.Append($"<size=125%><b>{c.Option.DisplayName}</b></size>\n");
                sb.Append(c.Carried
                    ? $"<color={HudText.Yellow}><b>[넘어온 후보]</b></color> <size=88%>안 고르면 다음 등급에 자동</size>\n\n"
                    : $"<color={HudTheme.GreenHex}><b>[새 후보]</b></color> <size=88%>안 고르면 다음 등급 후보로</size>\n\n");
                sb.Append($"<size=92%>{c.Option.Description}</size>");
                text.SetText(sb.ToString());
                pick.interactable = true;
            }

            var opened = new List<string>();
            if (_choices.Allows(StationRule.Policies))
                opened.Add("정책");
            foreach (var key in _choices.OpenedKeys)
            {
                var o = _choices.Config.Find(key);
                if (o != null)
                    opened.Add(o.DisplayName);
            }
            var st = new StringBuilder();
            st.Append($"<b>열린 시스템</b>  {(opened.Count > 0 ? string.Join(" · ", opened) : "없음")}\n");
            st.Append($"<color={Soft}><b>다음 등급에 자동</b>  {Names(_choices.AutoNext)}</color>\n");
            st.Append($"<color={Soft}><b>다음 등급 후보로 넘어감</b>  {Names(_choices.Carry)}</color>");
            _status.SetText(st.ToString());
        }
    }
}
