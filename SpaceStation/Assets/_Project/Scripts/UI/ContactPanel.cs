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
    /// 12-3c 지구급 핵심 규칙 그레이박스 화면 (방문 세력 + 평판):
    /// - 외부 접촉 창 (U · 왼쪽 위 [외부 접촉]): 통신 세기 · 해독 가능 계급, 세력 3개의 평판 카드(막대 + 단계 글자 · 직전 선택 · 교역 내용).
    /// - 방문 대응 창 (함선이 오면 화면 위쪽 가운데): 정체(해독 전 '정체 불명') · 남은 시간 · 해독 진행 + 교역 · 협상 · 경계 버튼.
    /// - 침입 드론 알림 (침입 중): 모듈 · 남은 드론 · 남은 시간 + [해당 모듈 보기].
    /// 상태는 색만이 아니라 글자로 (사용자 색약). 게임 시간은 멈추지 않는다.
    /// </summary>
    [DefaultExecutionOrder(-250)]
    public sealed class ContactPanel : MonoBehaviour
    {
        private const float WindowWidth = 1200f;
        private const float WindowHeight = 720f;
        private const float BoxWidth = 940f;
        private const string Soft = "#C8D2DE";
        private const string Bad = "#FFB0A0";

        [SerializeField] private StationController _station;
        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private Sprite _fillSprite;
        [SerializeField] private Sprite _frameSprite;
        [SerializeField] private Sprite _buttonSprite;
        [SerializeField] private HoloArt _holoArt;
        [SerializeField] private float _refreshSeconds = 0.25f;

        private HoloUi _ui;
        private StationSimulation _sim;
        private ContactSystem _contact;
        private CanvasGroup _group;
        private RectTransform _window;
        private UiTween _tween;
        private HoloFx _fx;
        private bool _open;
        private float _nextRefresh;
        private TMP_Text _summary, _status;
        private readonly TMP_Text[] _factionText = new TMP_Text[ContactSystem.FactionCount];
        private readonly RectTransform[] _repMarker = new RectTransform[ContactSystem.FactionCount];

        // 방문 대응
        private RectTransform _visitBox;
        private CanvasGroup _visitGroup;
        private TMP_Text _visitTitle, _visitDetail;
        private readonly (ContactChoice Choice, Button Button, TMP_Text Label)[] _choices = new (ContactChoice, Button, TMP_Text)[3];
        // 침입 드론
        private RectTransform _boardBox;
        private CanvasGroup _boardGroup;
        private TMP_Text _boardText;

        public bool IsOpen => _open;

        private void Start()
        {
            _sim = _station != null ? _station.Simulation : null;
            _contact = _sim?.Contact;
            if (_contact == null)
            {
                gameObject.SetActive(false);
                return;
            }
            _ui = HoloUi.For(_holoArt, _font, _fillSprite, _frameSprite, _buttonSprite);
            BuildLauncher();
            BuildWindow();
            BuildVisit();
            BuildBoarding();
            HudWindows.Opened += HandleWindowOpened;
            Refresh();
        }

        private void OnDestroy()
        {
            HudWindows.Opened -= HandleWindowOpened;
        }

        private void Update()
        {
            if (_ui == null)
                return;
            _tween.Update();
            var keyboard = Keyboard.current;
            if (keyboard != null && !InputGate.Blocked)
            {
                if (KeyBindings.WasPressed(GameAction.Contacts))
                    Toggle();
                else if (_open && keyboard.escapeKey.wasPressedThisFrame && !InputGate.EscapeConsumedThisFrame)
                {
                    InputGate.ConsumeEscape();
                    Close();
                }
            }
            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + _refreshSeconds;
                Refresh();
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

        /// <summary>13-6a: 왼쪽 위 [관리] 메뉴에 등록 — 외부 접촉이 열리면, 함선이 대응을 기다리면 '!'.</summary>
        private void BuildLauncher()
        {
            HudLauncher.For(_ui, transform).Add(new HudLauncher.Entry
            {
                Key = "contact", Order = HudLauncher.Order.Contact, Name = "외부 접촉",
                KeyLabel = () => KeyBindings.Label(GameAction.Contacts), IsOpen = () => _open, Click = Toggle,
                Visible = () => _contact.Unlocked, Attention = () => _contact.Unlocked && _contact.Phase == ContactSystem.ContactPhase.Waiting,
            });
        }

        private void BuildWindow()
        {
            var root = (RectTransform)transform;
            _window = HoloUi.Rect("ContactWindow", root);
            _window.anchorMin = _window.anchorMax = _window.pivot = new Vector2(0.5f, 0.5f);
            _window.anchoredPosition = new Vector2(-120f, 40f);
            _window.sizeDelta = new Vector2(WindowWidth, WindowHeight);
            _group = _window.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _fx = _ui.Window(_window.gameObject, new Color(0.02f, 0.045f, 0.07f, 0.99f), "CONTACT");
            var back = HoloUi.Rect("Backing", _window);
            HoloUi.Stretch(back, new Vector2(6f, 6f), new Vector2(-6f, -6f));
            back.SetAsFirstSibling();
            var backImage = back.gameObject.AddComponent<Image>();
            backImage.color = new Color(0.015f, 0.03f, 0.05f, 0.97f);
            backImage.raycastTarget = false;

            var title = _ui.Label(_window, "외부 접촉", 36f, TextAlignmentOptions.TopLeft);
            title.fontStyle = FontStyles.Bold;
            HoloUi.Place(title.rectTransform, new Vector2(40f, -28f), new Vector2(260f, 50f));
            HoloUi.Glow(title, 0.5f);
            _summary = _ui.Label(_window, "", 19f, TextAlignmentOptions.TopLeft, wrap: true);
            HoloUi.Place(_summary.rectTransform, new Vector2(260f, -30f), new Vector2(WindowWidth - 300f, 80f));

            // 세력 카드 3장
            const float cardW = 362f, cardH = 430f, gap = 17f;
            for (int i = 0; i < ContactSystem.FactionCount; i++)
            {
                var card = HoloUi.Rect("Faction" + i, _window);
                HoloUi.Place(card, new Vector2(40f + i * (cardW + gap), -122f), new Vector2(cardW, cardH));
                _ui.Panel(card.gameObject, new Color(0.03f, 0.06f, 0.09f, 0.98f), HudTheme.AccentDim);
                // 평판 막대: −100 ~ +100, 가운데 0, 단계 경계 눈금
                var bar = HoloUi.Rect("RepBar", card);
                HoloUi.Place(bar, new Vector2(18f, -70f), new Vector2(cardW - 36f, 10f));
                var barImg = bar.gameObject.AddComponent<Image>();
                barImg.sprite = _fillSprite;
                barImg.color = new Color(1f, 1f, 1f, 0.14f);
                barImg.raycastTarget = false;
                foreach (float tick in new[] { -60f, -20f, 0f, 20f, 60f })
                {
                    var t = HoloUi.Rect("Tick", bar);
                    t.anchorMin = t.anchorMax = new Vector2((tick + 100f) / 200f, 0.5f);
                    t.pivot = new Vector2(0.5f, 0.5f);
                    t.sizeDelta = new Vector2(tick == 0f ? 3f : 2f, tick == 0f ? 20f : 14f);
                    var ti = t.gameObject.AddComponent<Image>();
                    ti.color = new Color(1f, 1f, 1f, tick == 0f ? 0.6f : 0.35f);
                    ti.raycastTarget = false;
                }
                var marker = HoloUi.Rect("Marker", bar);
                marker.anchorMin = marker.anchorMax = new Vector2(0.5f, 0.5f);
                marker.pivot = new Vector2(0.5f, 0.5f);
                marker.sizeDelta = new Vector2(12f, 26f);
                var mi = marker.gameObject.AddComponent<Image>();
                mi.sprite = _fillSprite;
                mi.color = Color.white;
                mi.raycastTarget = false;
                _repMarker[i] = marker;
                var text = _ui.Label(card, "", 19f, TextAlignmentOptions.TopLeft, wrap: true);
                HoloUi.Stretch(text.rectTransform, new Vector2(18f, 14f), new Vector2(-14f, -14f));
                _factionText[i] = text;
            }

            _status = _ui.Label(_window, "", 19f, TextAlignmentOptions.TopLeft, wrap: true);
            _status.rectTransform.anchorMin = _status.rectTransform.anchorMax = _status.rectTransform.pivot = new Vector2(0f, 0f);
            _status.rectTransform.anchoredPosition = new Vector2(40f, 26f);
            _status.rectTransform.sizeDelta = new Vector2(880f, 110f);

            var close = _ui.Button(_window, "닫기 (ESC)", 20f, Close);
            var crt = (RectTransform)close.transform;
            crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(1f, 0f);
            crt.anchoredPosition = new Vector2(-40f, 26f);
            crt.sizeDelta = new Vector2(190f, 48f);

            HoloGlitch.Add(_window, _holoArt);
            _tween = new UiTween(_window, _group, new Vector2(0f, -24f), 0.2f, 0.15f, glitch: true);
        }

        private RectTransform Box(string name, out CanvasGroup group, Color fill, Color frame)
        {
            var box = HoloUi.Rect(name, (RectTransform)transform);
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 1f);
            box.sizeDelta = new Vector2(BoxWidth, 196f);
            _ui.Panel(box.gameObject, fill, frame);
            box.SetAsFirstSibling(); // 창이 열려 있으면 그 뒤로
            var back = HoloUi.Rect("Backing", box);
            HoloUi.Stretch(back, new Vector2(4f, 4f), new Vector2(-4f, -4f));
            back.SetAsFirstSibling();
            var bi = back.gameObject.AddComponent<Image>();
            bi.color = new Color(0.02f, 0.035f, 0.055f, 0.97f);
            bi.raycastTarget = false;
            group = box.gameObject.AddComponent<CanvasGroup>();
            return box;
        }

        private void BuildVisit()
        {
            _visitBox = Box("ContactVisit", out _visitGroup, new Color(0.03f, 0.05f, 0.08f, 0.98f), HudTheme.Accent);
            _visitTitle = _ui.Label(_visitBox, "", 28f, TextAlignmentOptions.Center);
            HoloUi.Place(_visitTitle.rectTransform, new Vector2(0f, -8f), new Vector2(BoxWidth, 38f));
            _visitDetail = _ui.Label(_visitBox, "", 18f, TextAlignmentOptions.Center, wrap: true);
            HoloUi.Place(_visitDetail.rectTransform, new Vector2(20f, -48f), new Vector2(BoxWidth - 40f, 50f));
            var kinds = new[] { ContactChoice.Trade, ContactChoice.Negotiate, ContactChoice.Guard };
            for (int i = 0; i < kinds.Length; i++)
            {
                var c = kinds[i];
                var b = _ui.TechButton(_visitBox, "", 20f, () => Choose(c));
                HoloUi.Place((RectTransform)b.transform, new Vector2(18f + i * 302f, -104f), new Vector2(290f, 80f));
                var label = b.GetComponentInChildren<TMP_Text>();
                label.enableWordWrapping = true;
                _choices[i] = (c, b, label);
            }
            SetVisible(_visitGroup, false, false);
        }

        private void BuildBoarding()
        {
            _boardBox = Box("ContactBoarding", out _boardGroup, new Color(0.07f, 0.03f, 0.015f, 0.98f), HudTheme.Negative);
            _boardBox.sizeDelta = new Vector2(BoxWidth, 92f);
            _boardText = _ui.Label(_boardBox, "", 21f, TextAlignmentOptions.MidlineLeft, wrap: true);
            HoloUi.Stretch(_boardText.rectTransform, new Vector2(22f, 8f), new Vector2(-250f, -8f));
            var focus = _ui.TechButton(_boardBox, "해당 모듈 보기", 20f, FocusBoarding);
            var frt = (RectTransform)focus.transform;
            frt.anchorMin = frt.anchorMax = frt.pivot = new Vector2(1f, 0.5f);
            frt.anchoredPosition = new Vector2(-18f, 0f);
            frt.sizeDelta = new Vector2(210f, 56f);
            SetVisible(_boardGroup, false, false);
        }

        private void FocusBoarding()
        {
            if (_contact.BoardingTarget != null)
                ModuleFocus.Focus(_contact.BoardingTarget);
        }

        private void Choose(ContactChoice choice)
        {
            if (_contact.Choose(choice))
                AudioService.TryPlay(l => l.UiClick);
            else
                AudioService.TryPlay(l => l.UiError);
            Refresh();
        }

        private static void SetVisible(CanvasGroup g, bool visible, bool interactive)
        {
            g.alpha = visible ? 1f : 0f;
            g.blocksRaycasts = visible && interactive;
            g.interactable = visible && interactive;
        }

        // ---------------- 표시 ----------------

        private static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{s / 60}:{s % 60:00}";
        }

        private void Refresh()
        {
            if (_open)
                RefreshWindow();
            RefreshVisit();
            RefreshBoarding();
        }

        private string DecodeRange(int power)
        {
            var cfg = _contact.Config;
            int top = -1;
            for (int r = 0; r < cfg.RankCount; r++)
                if (power >= cfg.RankPower(r))
                    top = r;
            return top < 0 ? "해독 불가" : top == 0 ? "말단까지" : $"말단 ~ {ContactConfig.RankName(top)}";
        }

        private void RefreshWindow()
        {
            var cfg = _contact.Config;
            int power = _contact.RelayPower;
            string next = !_contact.Unlocked ? $"<color={HudText.Orange}>지구급부터 함선이 찾아옵니다</color>"
                : _contact.Visiting ? $"<color={HudText.Orange}>지금 방문 중: {_contact.VisitorName}</color>"
                : $"다음 방문까지 약 {Clock(_contact.TimeLeft)}";
            _summary.SetText($"<color={Soft}>통신 세기 <b>{power}</b> → 해독 {DecodeRange(power)} (계급 기준 말단 1 · 중간 간부 2 · 간부 4 · 수뇌 8 · 통신 중계소 1, 심우주 통신기지 4)</color>\n"
                             + $"방문 {_contact.Visits}회 · 규모는 방문할수록 커짐 · {next}");
            for (int i = 0; i < ContactSystem.FactionCount; i++)
            {
                var f = (ContactFaction)i;
                float rep = _contact.Reputation(f);
                var tier = _contact.Tier(f);
                _repMarker[i].anchorMin = _repMarker[i].anchorMax = new Vector2((rep + 100f) / 200f, 0.5f);
                var sb = new StringBuilder();
                sb.Append($"<size=125%><b>{ContactConfig.FactionName(f)}</b></size>\n");
                sb.Append($"평판 <b>{ContactConfig.TierName(tier)}</b> ({rep:+0;-0;0})\n\n\n");
                var last = _contact.LastChoice(f);
                sb.Append($"<color={Soft}>직전 대응:</color> {(last == ContactChoice.None ? "없음" : ContactConfig.ChoiceName(last, f))}");
                if (last == ContactChoice.Guard)
                    sb.Append($" <color={Bad}>(다음 방문 제안 ↓{(f == ContactFaction.Outlaw ? " · 습격 위험 ↑" : "")})</color>");
                else if (last == ContactChoice.Negotiate)
                    sb.Append($" <color={HudTheme.GreenHex}>(다음 방문 제안 ↑)</color>");
                sb.Append("\n\n");
                sb.Append(f switch
                {
                    ContactFaction.Merchant => $"<b>교역</b>: 금속 → 가장 모자란 산소 · 물 · 식량\n<b>우호 이상</b>: 도착하면 금속 선물\n<color={Soft}>무법 선단과 경쟁</color>",
                    ContactFaction.Scholar => $"<b>교역</b>: 금속 → 진행 중인 연구 +{cfg.ScholarProgress * 100f:0}%\n<b>우호 이상</b>: 도착하면 연구 선물\n<color={Soft}>무법 선단과 경쟁</color>",
                    _ => $"<b>통행료</b>: 금속을 내면 습격 없이 지나감\n<b>경계</b>: {cfg.OutlawGuardRaidChance * 100f:0}% 확률로 습격 (포격 또는 침입 드론)\n<color={Soft}>가까워지면 상인 · 학술과 멀어짐</color>",
                });
                if (tier == ReputationTier.Hostile)
                    sb.Append($"\n<color={Bad}><b>적대</b>: 협상하지 않으면 습격</color>");
                _factionText[i].SetText(sb.ToString());
            }
            var st = new StringBuilder();
            if (_contact.Boarding)
                st.Append($"<color={Bad}><b>침입 드론</b> {ModuleName(_contact.BoardingTarget)} · 남은 드론 {_contact.DronesLeft}/{_contact.DronesTotal} · {Clock(_contact.BoardingTimeLeft)}</color>\n");
            if (_sim.MeteorShower != null && _sim.MeteorShower.IsRaid)
                st.Append($"<color={Bad}><b>포격</b> {MeteorShowerSystem.DirectionName(_sim.MeteorShower.Direction)} · {_sim.MeteorShower.WavesDone}/{_sim.MeteorShower.Waves}파</color>\n");
            if (_contact.PendingRaid != ContactSystem.RaidKind.None)
                st.Append($"<color={HudText.Orange}>습격 대기 중 (진행 중인 유성우 · 습격이 끝나면 시작)</color>\n");
            st.Append($"<color={Soft}>계급이 높을수록 요구 · 보상 · 습격이 큼 · 해독 못 한 함선은 '정체 불명' · 고른 대응을 못 하면 경계로 처리</color>");
            _status.SetText(st.ToString());
        }

        private float VisitBoxY()
        {
            // 파업 경고 창(같은 자리)이 떠 있으면 그 아래로
            bool crisis = _sim.Crisis != null && _sim.Crisis.Phase != SocialCrisisSystem.CrisisPhase.Calm;
            return crisis ? -514f : -314f;
        }

        private void RefreshVisit()
        {
            if (!_contact.Visiting || _sim.Session.IsGameOver)
            {
                SetVisible(_visitGroup, false, false);
                return;
            }
            SetVisible(_visitGroup, true, true);
            _visitBox.anchoredPosition = new Vector2(0f, VisitBoxY());
            var cfg = _contact.Config;
            bool arrived = _contact.Phase == ContactSystem.ContactPhase.Waiting;
            _visitTitle.SetText($"{HudTheme.Icon("warning")} <b>{(arrived ? "함선 도착" : "함선 접근")} · {_contact.VisitorName}</b>  <color={HudText.Orange}>{(arrived ? "응답 마감" : "도착")} {Clock(_contact.TimeLeft)}</color>");
            string detail;
            if (_contact.Decoded)
            {
                var f = _contact.Faction;
                var last = _contact.LastChoice(f);
                detail = $"평판 <b>{ContactConfig.TierName(_contact.Tier(f))}</b> ({_contact.Reputation(f):+0;-0;0}) · 직전 대응 {(last == ContactChoice.None ? "없음" : ContactConfig.ChoiceName(last, f))} · 규모 ×{_contact.Scale:0.0}";
            }
            else if (_contact.CanDecode)
                detail = $"신호 해독 중 <b>{_contact.DecodeProgress * 100f:0}%</b> · 통신 세기 {_contact.RelayPower} (이 함선 {cfg.RankPower(_contact.Rank)} 필요) · 해독 전에 고르면 정체를 모른 채 대응";
            else
                detail = $"<color={Bad}><b>해독 불가</b></color> · 통신 세기 {_contact.RelayPower} / 이 계급 {cfg.RankPower(_contact.Rank)} 필요 (중계소를 더 짓거나 심우주 통신기지로 합성) · 정체를 모른 채 대응";
            _visitDetail.SetText($"<color=#E8EEF4>{detail}</color>");
            foreach (var (choice, button, label) in _choices)
            {
                string reason = _contact.BlockReason(choice);
                button.interactable = reason == null;
                label.SetText(ChoiceText(choice, reason));
            }
        }

        private string ChoiceText(ContactChoice choice, string reason)
        {
            var cfg = _contact.Config;
            bool known = _contact.Decoded;
            var f = _contact.Faction;
            string name = known ? ContactConfig.ChoiceName(choice, f) : choice == ContactChoice.Trade ? "교역 / 통행료" : ContactConfig.ChoiceName(choice, f);
            string body;
            if (reason != null)
                body = $"<color={Bad}>{reason}</color>";
            else if (!known)
                body = choice == ContactChoice.Guard ? "비용 없음 · 평판 ↓ · 습격 위험" : $"<color={Soft}>비용 · 결과 모름 (정체 불명)</color>";
            else
            {
                float cost = _contact.CostOf(choice);
                body = choice switch
                {
                    ContactChoice.Trade => f switch
                    {
                        ContactFaction.Merchant => $"금속 -{cost:0} → {_contact.MerchantGood.DisplayName()} +{_contact.MerchantAmount:0}",
                        ContactFaction.Scholar => $"금속 -{cost:0} → 연구 +{_contact.ScholarAmount * 100f:0}%",
                        _ => $"금속 -{cost:0} · 습격 없음",
                    },
                    ContactChoice.Negotiate => $"금속 -{cost:0} · 평판 ↑↑",
                    _ => f == ContactFaction.Outlaw || _contact.Tier(f) == ReputationTier.Hostile
                        ? $"<color={Bad}>평판 ↓ · 습격 위험</color> (피해 ×{cfg.GuardDefense:0.#})"
                        : "비용 없음 · 평판 ↓",
                };
            }
            return $"<b>{name}</b>\n<size=86%>{body}</size>";
        }

        private void RefreshBoarding()
        {
            if (!_contact.Boarding || _sim.Session.IsGameOver)
            {
                SetVisible(_boardGroup, false, false);
                return;
            }
            SetVisible(_boardGroup, true, true);
            float y = _contact.Visiting ? VisitBoxY() - 206f : VisitBoxY();
            _boardBox.anchoredPosition = new Vector2(0f, y);
            _boardText.SetText($"{HudTheme.Icon("warning")} <b>침입 드론</b> · {ModuleName(_contact.BoardingTarget)} · 남은 드론 <b>{_contact.DronesLeft}/{_contact.DronesTotal}</b> · <color={HudText.Orange}>{Clock(_contact.BoardingTimeLeft)}</color>\n"
                               + $"<size=86%><color=#F4ECE8>해당 모듈을 선택하고 {KeyBindings.Label(GameAction.EnterInterior)}로 들어가 드론을 끄세요 · 방어 실패 시 해당 모듈 파손 + 금속 손실</color></size>");
        }

        private static string ModuleName(ModuleInstance m) => m != null && m.Data != null ? m.Data.DisplayName : "모듈";
    }
}
