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
    /// 12-3c 국가급 핵심 규칙 그레이박스 화면: 정책 창(O 키 · 왼쪽 위 [정책]) + 사회 위기 선택지 창(경고 중 화면 위쪽 가운데).
    /// 정책 창: 위 = 슬롯 카드(시행 중 · 적용 대기 · 교체 대기, 눌러서 고름), 아래 = 정책 8개(효과 초록 · 부작용 빨강, 눌러서 고른 슬롯에 예약).
    /// 게임 시간은 멈추지 않는다.
    /// </summary>
    [DefaultExecutionOrder(-250)]
    public sealed class PolicyPanel : MonoBehaviour
    {
        // 사용자 피드백 2026-10-13: 글자가 잘 안 보임 → 창 · 글자 키우고 뒤 배경을 진하게 (붉은 태양 폭풍 배경에서도 읽히게)
        private const float WindowWidth = 1200f;
        private const float WindowHeight = 770f;
        private const float CrisisWidth = 940f;

        [SerializeField] private StationController _station;
        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private Sprite _fillSprite;
        [SerializeField] private Sprite _frameSprite;
        [SerializeField] private Sprite _buttonSprite;
        [SerializeField] private HoloArt _holoArt;
        [SerializeField] private float _refreshSeconds = 0.25f;

        private HoloUi _ui;
        private StationSimulation _sim;
        private PolicySystem _policies;
        private SocialCrisisSystem _crisis;
        private CanvasGroup _group;
        private RectTransform _window;
        private UiTween _tween;
        private HoloFx _fx;
        private bool _open;
        private float _nextRefresh;
        private int _selectedSlot;
        private string _notice = "";
        private TMP_Text _launcherLabel, _summary, _footer;
        private readonly List<(Button button, TMP_Text label)> _slotCards = new List<(Button, TMP_Text)>();
        private readonly List<(PolicyData policy, Button button, TMP_Text label)> _rows = new List<(PolicyData, Button, TMP_Text)>();
        private Button _clear;

        // 사회 위기 선택지
        private RectTransform _crisisBox;
        private CanvasGroup _crisisGroup;
        private TMP_Text _crisisTitle, _crisisDetail;
        private readonly List<(SocialCrisisSystem.Choice choice, Button button, TMP_Text label)> _choices = new List<(SocialCrisisSystem.Choice, Button, TMP_Text)>();

        public bool IsOpen => _open;

        private void Start()
        {
            _sim = _station != null ? _station.Simulation : null;
            _policies = _sim?.Policies;
            _crisis = _sim?.Crisis;
            if (_policies == null)
            {
                gameObject.SetActive(false);
                return;
            }
            _ui = HoloUi.For(_holoArt, _font, _fillSprite, _frameSprite, _buttonSprite);
            BuildLauncher();
            BuildWindow();
            BuildCrisis();
            HudWindows.Opened += HandleWindowOpened;
            Refresh();
        }

        private void OnDestroy()
        {
            KeyBindings.Changed -= RefreshLauncher;
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
                if (KeyBindings.WasPressed(GameAction.Policies))
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
            transform.SetAsLastSibling(); // 건설 안내 등 다른 HUD 글자가 창 위로 비치지 않게
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

        // ---------------- 조작 ----------------

        private void SelectSlot(int slot)
        {
            _selectedSlot = slot;
            _notice = "";
            Refresh();
        }

        private void AssignToSelected(PolicyData policy)
        {
            var result = _policies.Assign(_selectedSlot, policy);
            switch (result)
            {
                case PolicySystem.AssignResult.Ok:
                    _notice = policy != null ? $"'{policy.DisplayName}' 예약 · {_policies.Config.ApplyDelay:0}초 뒤 시행" : "슬롯 비우기 예약";
                    AudioService.TryPlay(l => l.UiClick);
                    break;
                case PolicySystem.AssignResult.NoSlot: _notice = $"<color={Bad}>열리지 않은 슬롯</color>"; break;
                case PolicySystem.AssignResult.Cooldown: _notice = $"<color={Bad}>이 슬롯은 아직 교체 대기 중</color>"; break;
                case PolicySystem.AssignResult.Duplicate: _notice = $"<color={Bad}>이미 다른 슬롯에 있는 정책</color>"; break;
                case PolicySystem.AssignResult.Same: _notice = $"<color={HudText.Muted}>이미 이 슬롯의 정책</color>"; break;
            }
            Refresh();
        }

        // ---------------- 구성 ----------------

        private static string LauncherText() => $"정책 ({KeyBindings.Label(GameAction.Policies)})";

        private void RefreshLauncher()
        {
            if (_launcherLabel != null)
                _launcherLabel.SetText(LauncherText());
        }

        private void BuildLauncher()
        {
            var root = (RectTransform)transform;
            var button = _ui.TechButton(root, LauncherText(), 19f, Toggle);
            HoloUi.Place((RectTransform)button.transform, new Vector2(500f, -24f), new Vector2(140f, 44f)); // 합성 버튼 오른쪽
            _launcherLabel = button.GetComponentInChildren<TMP_Text>();
            KeyBindings.Changed += RefreshLauncher;
        }

        private void BuildWindow()
        {
            var root = (RectTransform)transform;
            _window = HoloUi.Rect("PolicyWindow", root);
            _window.anchorMin = _window.anchorMax = _window.pivot = new Vector2(0.5f, 0.5f);
            _window.anchoredPosition = new Vector2(-120f, 40f);
            _window.sizeDelta = new Vector2(WindowWidth, WindowHeight);
            _group = _window.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _fx = _ui.Window(_window.gameObject, new Color(0.02f, 0.045f, 0.07f, 0.99f), "POLICY");
            // 반투명 버튼 뒤로 배경이 비치지 않게 진한 바탕
            var back = HoloUi.Rect("Backing", _window);
            HoloUi.Stretch(back, new Vector2(6f, 6f), new Vector2(-6f, -6f)); // 창 전체 (홀로그램 창 바탕이 반투명이라 HUD 글자가 비쳤음)
            back.SetAsFirstSibling();
            var backImage = back.gameObject.AddComponent<Image>();
            backImage.color = new Color(0.015f, 0.03f, 0.05f, 0.97f);
            backImage.raycastTarget = false;

            var title = _ui.Label(_window, "정책", 36f, TextAlignmentOptions.TopLeft);
            title.fontStyle = FontStyles.Bold;
            HoloUi.Place(title.rectTransform, new Vector2(40f, -28f), new Vector2(200f, 50f));
            HoloUi.Glow(title, 0.5f);
            _summary = _ui.Label(_window, "", 18f, TextAlignmentOptions.TopLeft, wrap: true);
            HoloUi.Place(_summary.rectTransform, new Vector2(180f, -30f), new Vector2(WindowWidth - 220f, 56f));

            // 슬롯 카드 (최대 4)
            const float cardW = 272f, cardH = 116f, gap = 14f;
            for (int i = 0; i < 4; i++)
            {
                int slot = i;
                var b = _ui.TechButton(_window, "", 19f, () => SelectSlot(slot));
                HoloUi.Place((RectTransform)b.transform, new Vector2(40f + i * (cardW + gap), -96f), new Vector2(cardW, cardH));
                var label = b.GetComponentInChildren<TMP_Text>();
                label.alignment = TextAlignmentOptions.TopLeft;
                label.margin = new Vector4(12f, 10f, 8f, 6f);
                label.enableWordWrapping = true;
                _slotCards.Add((b, label));
            }

            // 정책 목록 (2열)
            const float rowW = 550f, rowH = 104f;
            float top = -96f - cardH - 20f;
            for (int i = 0; i < _policies.Policies.Count; i++)
            {
                var p = _policies.Policies[i];
                if (p == null)
                    continue;
                var b = _ui.TechButton(_window, "", 19f, () => AssignToSelected(p));
                HoloUi.Place((RectTransform)b.transform, new Vector2(40f + (i % 2) * (rowW + 20f), top - (i / 2) * (rowH + 8f)), new Vector2(rowW, rowH));
                var label = b.GetComponentInChildren<TMP_Text>();
                label.alignment = TextAlignmentOptions.TopLeft;
                label.margin = new Vector4(14f, 8f, 10f, 6f);
                label.enableWordWrapping = true;
                _rows.Add((p, b, label));
            }

            _clear = _ui.Button(_window, "고른 슬롯 비우기", 20f, () => AssignToSelected(null));
            var clr = (RectTransform)_clear.transform;
            clr.anchorMin = clr.anchorMax = clr.pivot = new Vector2(0f, 0f);
            clr.anchoredPosition = new Vector2(40f, 26f);
            clr.sizeDelta = new Vector2(250f, 52f);

            _footer = _ui.Label(_window, "", 19f, TextAlignmentOptions.MidlineLeft, wrap: true);
            _footer.rectTransform.anchorMin = _footer.rectTransform.anchorMax = _footer.rectTransform.pivot = new Vector2(0f, 0f);
            _footer.rectTransform.anchoredPosition = new Vector2(310f, 26f);
            _footer.rectTransform.sizeDelta = new Vector2(600f, 52f);

            var close = _ui.Button(_window, "닫기 (ESC)", 20f, Close);
            var crt = (RectTransform)close.transform;
            crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(1f, 0f);
            crt.anchoredPosition = new Vector2(-40f, 26f);
            crt.sizeDelta = new Vector2(190f, 48f);

            HoloGlitch.Add(_window, _holoArt);
            _tween = new UiTween(_window, _group, new Vector2(0f, -24f), 0.2f, 0.15f, glitch: true);
        }

        private void BuildCrisis()
        {
            var root = (RectTransform)transform;
            _crisisBox = HoloUi.Rect("CrisisChoices", root);
            _crisisBox.anchorMin = _crisisBox.anchorMax = _crisisBox.pivot = new Vector2(0.5f, 1f);
            _crisisBox.anchoredPosition = new Vector2(0f, -314f);
            _crisisBox.sizeDelta = new Vector2(CrisisWidth, 186f);
            _ui.Panel(_crisisBox.gameObject, new Color(0.07f, 0.03f, 0.015f, 0.98f), HudTheme.Negative);
            _crisisBox.SetAsFirstSibling(); // 정책 창이 열려 있으면 그 뒤로
            var crisisBack = HoloUi.Rect("Backing", _crisisBox);
            HoloUi.Stretch(crisisBack, new Vector2(4f, 4f), new Vector2(-4f, -4f)); // 붉은 배경(태양 폭풍 등)이 비치지 않게
            crisisBack.SetAsFirstSibling();
            var crisisBackImage = crisisBack.gameObject.AddComponent<Image>();
            crisisBackImage.color = new Color(0.05f, 0.025f, 0.02f, 0.97f);
            crisisBackImage.raycastTarget = false;
            _crisisGroup = _crisisBox.gameObject.AddComponent<CanvasGroup>();
            _crisisTitle = _ui.Label(_crisisBox, "", 28f, TextAlignmentOptions.Center);
            HoloUi.Place(_crisisTitle.rectTransform, new Vector2(0f, -8f), new Vector2(CrisisWidth, 38f));
            _crisisDetail = _ui.Label(_crisisBox, "", 18f, TextAlignmentOptions.Center, wrap: true);
            HoloUi.Place(_crisisDetail.rectTransform, new Vector2(20f, -48f), new Vector2(CrisisWidth - 40f, 48f));
            var choices = new[] { SocialCrisisSystem.Choice.Bribe, SocialCrisisSystem.Choice.Crackdown, SocialCrisisSystem.Choice.Wage, SocialCrisisSystem.Choice.Ignore };
            for (int i = 0; i < choices.Length; i++)
            {
                var c = choices[i];
                var b = _ui.TechButton(_crisisBox, "", 20f, () => Choose(c));
                HoloUi.Place((RectTransform)b.transform, new Vector2(18f + i * 226f, -102f), new Vector2(214f, 72f));
                var label = b.GetComponentInChildren<TMP_Text>();
                label.enableWordWrapping = true;
                _choices.Add((c, b, label));
            }
            SetCrisisVisible(false, false);
        }

        private void Choose(SocialCrisisSystem.Choice choice)
        {
            if (_crisis != null && _crisis.Choose(choice))
                AudioService.TryPlay(l => l.UiClick);
            Refresh();
        }

        private void SetCrisisVisible(bool visible, bool buttons)
        {
            _crisisGroup.alpha = visible ? 1f : 0f;
            _crisisGroup.blocksRaycasts = visible && buttons;
            _crisisGroup.interactable = visible && buttons;
            foreach (var (_, button, _) in _choices)
                if (button.gameObject.activeSelf != buttons)
                    button.gameObject.SetActive(buttons);
            _crisisBox.sizeDelta = new Vector2(CrisisWidth, buttons ? 186f : 100f);
        }

        // ---------------- 표시 ----------------

        /// <summary>보조 글자색 (Muted보다 밝게 — 어두운 바탕 · 붉은 배경에서도 읽히게).</summary>
        private const string Soft = "#C8D2DE";
        /// <summary>나쁜 효과 글자색 (HudText.Red보다 밝게 — 붉은 경고 창 바탕에서도 읽히게, 사용자 피드백 2026-10-13).</summary>
        private const string Bad = "#FFB0A0";

        private static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{s / 60}:{s % 60:00}";
        }

        private static string Effects(IReadOnlyList<PolicyModifier> modifiers)
        {
            var sb = new StringBuilder();
            foreach (var m in modifiers)
            {
                string line = PolicyStats.Describe(m.Stat, m.Value, out bool good);
                if (sb.Length > 0)
                    sb.Append("  ");
                sb.Append($"<color={(good ? HudTheme.GreenHex : Bad)}>{line}</color>");
            }
            return sb.ToString();
        }

        private void Refresh()
        {
            if (_open)
                RefreshWindow();
            RefreshCrisis();
        }

        private void RefreshWindow()
        {
            int slots = _policies.SlotCount;
            var list = _policies.Slots;
            if (_selectedSlot >= Mathf.Max(1, slots))
                _selectedSlot = 0;
            var temps = new StringBuilder();
            foreach (var t in _policies.Temporaries)
                temps.Append(temps.Length > 0 ? " · " : "").Append($"{t.Name} {Clock(t.Remaining)}");
            var rules = _policies.Sim.Rules; // 13-1 타이쿤판 해금 선택이면 등급 대신 시스템 개방
            string slotRule = rules == null ? "국가급 2 · 대륙급 3 · 지구급 4"
                : rules.Allows(StationRule.PolicyExpansion) ? $"정책 슬롯 확장 +{_policies.Config.ExpansionSlots} 포함"
                : $"'정책 슬롯 확장'을 열면 +{_policies.Config.ExpansionSlots}";
            _summary.SetText(slots > 0
                ? $"<color={Soft}>슬롯 {slots}개 ({slotRule}) · 바꾸면 {_policies.Config.ApplyDelay:0}초 뒤 시행 · 그 슬롯은 {_policies.Config.SwapCooldown / 60f:0}분 교체 대기</color>"
                  + (temps.Length > 0 ? $"\n<color={HudText.Orange}>일시 효과: {temps}</color>" : "")
                : rules != null ? $"<color={HudText.Orange}>'정책' 시스템이 열리면 정책 슬롯이 생깁니다</color>"
                : $"<color={HudText.Orange}>국가급부터 정책 슬롯이 열립니다</color>");

            for (int i = 0; i < _slotCards.Count; i++)
            {
                var (button, label) = _slotCards[i];
                bool open = i < slots;
                button.interactable = open;
                if (!open)
                {
                    string grade = rules != null ? (slots > 0 ? "정책 슬롯 확장" : "'정책' 시스템") : i < 2 ? "국가급" : i == 2 ? "대륙급" : "지구급";
                    label.SetText($"<color={Soft}>슬롯 {i + 1}\n{grade}부터</color>");
                    continue;
                }
                var s = list[i];
                var sb = new StringBuilder();
                sb.Append(i == _selectedSlot ? $"<color=#7FD8FF><b>[선택] 슬롯 {i + 1}</b></color>\n" : $"<color={Soft}>슬롯 {i + 1}</color>\n");
                sb.Append(s.Active != null ? $"<b>{s.Active.DisplayName}</b>\n" : $"<color={Soft}>(비어 있음)</color>\n");
                if (s.HasPending)
                    sb.Append($"<color={HudText.Orange}>다음: {(s.Pending != null ? s.Pending.DisplayName : "비우기")} {Clock(s.PendingLeft)}</color>\n");
                if (s.Cooldown > 0f)
                    sb.Append($"<size=90%><color={Soft}>교체 대기 {Clock(s.Cooldown)}</color></size>");
                label.SetText(sb.ToString());
            }

            foreach (var (policy, button, label) in _rows)
            {
                bool active = _policies.IsActive(policy);
                button.interactable = slots > 0;
                string name = active ? $"<color=#7FD8FF><b>{policy.DisplayName}</b> (시행 중)</color>" : $"<b>{policy.DisplayName}</b>";
                label.SetText($"{name}\n{Effects(policy.Modifiers)}"
                              + (string.IsNullOrEmpty(policy.Description) ? "" : $"\n<size=88%><color={Soft}>{policy.Description}</color></size>"));
            }
            _clear.interactable = slots > 0;
            // 파업 경고 선택지 창은 정책 창 뒤에 있음 → 열려 있는 동안은 아래에 알림
            bool warning = _crisis != null && _crisis.Phase == SocialCrisisSystem.CrisisPhase.Warning;
            _footer.SetText(warning ? $"<color={Bad}><b>파업 조짐 {Clock(_crisis.TimeLeft)}</b> · 창을 닫고 선택지로 대응</color>" : _notice);
        }

        private void RefreshCrisis()
        {
            if (_crisis == null || _sim.Session.IsGameOver)
            {
                SetCrisisVisible(false, false);
                return;
            }
            var cfg = _crisis.Config;
            switch (_crisis.Phase)
            {
                case SocialCrisisSystem.CrisisPhase.Warning:
                    SetCrisisVisible(true, true);
                    _crisisTitle.SetText($"{HudTheme.Icon("warning")} <b>파업 조짐</b>  <color={HudText.Orange}>{Clock(_crisis.TimeLeft)}</color>");
                    var growth = _sim.GetSystem<GrowthThreatSystem>(); // 13-2 타이쿤판: 밀집도가 기준을 올림
                    string crowd = growth != null && growth.CrowdingThreshold >= 0.5f ? $" · 밀집도 {growth.Crowding * 100f:0}% +{growth.CrowdingThreshold:0}" : "";
                    _crisisDetail.SetText($"<color=#F4ECE8>만족도 {_sim.Population.Satisfaction:0} (기준 {_crisis.Threshold:0}{crowd}) · {_crisis.Threshold + cfg.StrikeRecoverMargin:0} 이상으로 오르면 저절로 풀림 · 시간이 다 되면 파업(생산 -{(1f - cfg.StrikeProduction) * 100f:0}%, {cfg.StrikeDuration:0}초)</color>");
                    foreach (var (choice, button, label) in _choices)
                    {
                        button.interactable = _crisis.CanChoose(choice);
                        label.SetText(choice switch
                        {
                            SocialCrisisSystem.Choice.Bribe => $"<b>무마</b>\n<size=88%>{HudText.Cost(cfg.BribeCost)}</size>",
                            SocialCrisisSystem.Choice.Crackdown => $"<b>강경 대응</b>\n<size=88%><color={Bad}>만족도 {cfg.CrackdownSatisfaction:0} · 주민 -{cfg.CrackdownLeave}</color></size>",
                            SocialCrisisSystem.Choice.Wage => $"<b>임금 인상</b>\n<size=88%>{cfg.WageSeconds / 60f:0}분 상한 +{cfg.WageSatisfactionCap:0} · 생산 -{(1f - cfg.WageProduction) * 100f:0}%</size>",
                            _ => $"<b>방치</b>\n<size=88%><color={Bad}>바로 파업</color></size>",
                        });
                    }
                    break;
                case SocialCrisisSystem.CrisisPhase.Striking:
                    SetCrisisVisible(true, false);
                    _crisisTitle.SetText($"{HudTheme.Icon("warning")} <b>파업 중</b>  <color={HudText.Orange}>{Clock(_crisis.TimeLeft)}</color>");
                    _crisisDetail.SetText($"<color=#F4ECE8>생산 -{(1f - cfg.StrikeProduction) * 100f:0}%</color>");
                    break;
                default:
                    SetCrisisVisible(false, false);
                    break;
            }
        }
    }
}
