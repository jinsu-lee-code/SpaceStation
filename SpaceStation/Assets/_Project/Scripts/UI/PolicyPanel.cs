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
        private const float WindowWidth = 940f;
        private const float WindowHeight = 700f;

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
                case PolicySystem.AssignResult.NoSlot: _notice = $"<color={HudText.Red}>열리지 않은 슬롯</color>"; break;
                case PolicySystem.AssignResult.Cooldown: _notice = $"<color={HudText.Red}>이 슬롯은 아직 교체 대기 중</color>"; break;
                case PolicySystem.AssignResult.Duplicate: _notice = $"<color={HudText.Red}>이미 다른 슬롯에 있는 정책</color>"; break;
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
            _window.anchoredPosition = new Vector2(-150f, 30f);
            _window.sizeDelta = new Vector2(WindowWidth, WindowHeight);
            _group = _window.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _fx = _ui.Window(_window.gameObject, new Color(0.03f, 0.07f, 0.11f, 0.96f), "POLICY");

            var title = _ui.Label(_window, "정책", 32f, TextAlignmentOptions.TopLeft);
            title.fontStyle = FontStyles.Bold;
            HoloUi.Place(title.rectTransform, new Vector2(40f, -30f), new Vector2(300f, 46f));
            HoloUi.Glow(title, 0.5f);
            _summary = _ui.Label(_window, "", 15f, TextAlignmentOptions.TopLeft, wrap: true);
            HoloUi.Place(_summary.rectTransform, new Vector2(200f, -36f), new Vector2(WindowWidth - 240f, 50f));

            // 슬롯 카드 (최대 4)
            const float cardW = 205f, cardH = 104f, gap = 12f;
            for (int i = 0; i < 4; i++)
            {
                int slot = i;
                var b = _ui.TechButton(_window, "", 15f, () => SelectSlot(slot));
                HoloUi.Place((RectTransform)b.transform, new Vector2(40f + i * (cardW + gap), -96f), new Vector2(cardW, cardH));
                var label = b.GetComponentInChildren<TMP_Text>();
                label.alignment = TextAlignmentOptions.TopLeft;
                label.margin = new Vector4(12f, 10f, 8f, 6f);
                label.enableWordWrapping = true;
                _slotCards.Add((b, label));
            }

            // 정책 목록 (2열)
            const float rowW = 420f, rowH = 86f;
            float top = -96f - cardH - 24f;
            for (int i = 0; i < _policies.Policies.Count; i++)
            {
                var p = _policies.Policies[i];
                if (p == null)
                    continue;
                var b = _ui.TechButton(_window, "", 15f, () => AssignToSelected(p));
                HoloUi.Place((RectTransform)b.transform, new Vector2(40f + (i % 2) * (rowW + 20f), top - (i / 2) * (rowH + 8f)), new Vector2(rowW, rowH));
                var label = b.GetComponentInChildren<TMP_Text>();
                label.alignment = TextAlignmentOptions.TopLeft;
                label.margin = new Vector4(14f, 8f, 10f, 6f);
                label.enableWordWrapping = true;
                _rows.Add((p, b, label));
            }

            _clear = _ui.Button(_window, "고른 슬롯 비우기", 17f, () => AssignToSelected(null));
            var clr = (RectTransform)_clear.transform;
            clr.anchorMin = clr.anchorMax = clr.pivot = new Vector2(0f, 0f);
            clr.anchoredPosition = new Vector2(40f, 26f);
            clr.sizeDelta = new Vector2(220f, 48f);

            _footer = _ui.Label(_window, "", 15f, TextAlignmentOptions.MidlineLeft, wrap: true);
            _footer.rectTransform.anchorMin = _footer.rectTransform.anchorMax = _footer.rectTransform.pivot = new Vector2(0f, 0f);
            _footer.rectTransform.anchoredPosition = new Vector2(280f, 26f);
            _footer.rectTransform.sizeDelta = new Vector2(420f, 48f);

            var close = _ui.Button(_window, "닫기 (ESC)", 19f, Close);
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
            _crisisBox.sizeDelta = new Vector2(760f, 150f);
            _ui.Panel(_crisisBox.gameObject, new Color(0.16f, 0.07f, 0.02f, 0.92f), HudTheme.Negative);
            _crisisGroup = _crisisBox.gameObject.AddComponent<CanvasGroup>();
            _crisisTitle = _ui.Label(_crisisBox, "", 24f, TextAlignmentOptions.Center);
            HoloUi.Place(_crisisTitle.rectTransform, new Vector2(0f, -8f), new Vector2(760f, 32f));
            _crisisDetail = _ui.Label(_crisisBox, "", 14f, TextAlignmentOptions.Center, wrap: true);
            HoloUi.Place(_crisisDetail.rectTransform, new Vector2(20f, -40f), new Vector2(720f, 40f));
            var choices = new[] { SocialCrisisSystem.Choice.Bribe, SocialCrisisSystem.Choice.Crackdown, SocialCrisisSystem.Choice.Wage, SocialCrisisSystem.Choice.Ignore };
            for (int i = 0; i < choices.Length; i++)
            {
                var c = choices[i];
                var b = _ui.TechButton(_crisisBox, "", 14f, () => Choose(c));
                HoloUi.Place((RectTransform)b.transform, new Vector2(16f + i * 184f, -88f), new Vector2(176f, 52f));
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
            _crisisBox.sizeDelta = new Vector2(760f, buttons ? 150f : 84f);
        }

        // ---------------- 표시 ----------------

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
                sb.Append($"<color={(good ? HudTheme.GreenHex : HudText.Red)}>{line}</color>");
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
            _summary.SetText(slots > 0
                ? $"<color={HudText.Muted}>슬롯 {slots}개 (국가급 2 · 대륙급 3 · 지구급 4) · 바꾸면 {_policies.Config.ApplyDelay:0}초 뒤 시행 · 그 슬롯은 {_policies.Config.SwapCooldown / 60f:0}분 교체 대기</color>"
                  + (temps.Length > 0 ? $"\n<color={HudText.Orange}>일시 효과: {temps}</color>" : "")
                : $"<color={HudText.Orange}>국가급부터 정책 슬롯이 열립니다</color>");

            for (int i = 0; i < _slotCards.Count; i++)
            {
                var (button, label) = _slotCards[i];
                bool open = i < slots;
                button.interactable = open;
                if (!open)
                {
                    string grade = i < 2 ? "국가급" : i == 2 ? "대륙급" : "지구급";
                    label.SetText($"<color={HudText.Muted}>슬롯 {i + 1}\n{grade}부터</color>");
                    continue;
                }
                var s = list[i];
                var sb = new StringBuilder();
                sb.Append(i == _selectedSlot ? $"<color=#7FD8FF><b>[선택] 슬롯 {i + 1}</b></color>\n" : $"<color={HudText.Muted}>슬롯 {i + 1}</color>\n");
                sb.Append(s.Active != null ? $"<b>{s.Active.DisplayName}</b>\n" : $"<color={HudText.Muted}>(비어 있음)</color>\n");
                if (s.HasPending)
                    sb.Append($"<color={HudText.Orange}>다음: {(s.Pending != null ? s.Pending.DisplayName : "비우기")} {Clock(s.PendingLeft)}</color>\n");
                if (s.Cooldown > 0f)
                    sb.Append($"<size=85%><color={HudText.Muted}>교체 대기 {Clock(s.Cooldown)}</color></size>");
                label.SetText(sb.ToString());
            }

            foreach (var (policy, button, label) in _rows)
            {
                bool active = _policies.IsActive(policy);
                button.interactable = slots > 0;
                string name = active ? $"<color=#7FD8FF><b>{policy.DisplayName}</b> (시행 중)</color>" : $"<b>{policy.DisplayName}</b>";
                label.SetText($"{name}\n<size=90%>{Effects(policy.Modifiers)}</size>"
                              + (string.IsNullOrEmpty(policy.Description) ? "" : $"\n<size=80%><color={HudText.Muted}>{policy.Description}</color></size>"));
            }
            _clear.interactable = slots > 0;
            _footer.SetText(_notice);
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
                    _crisisDetail.SetText($"<color=#E8C8C0>만족도 {_sim.Population.Satisfaction:0} (기준 {_crisis.Threshold:0}) · {_crisis.Threshold + cfg.StrikeRecoverMargin:0} 이상으로 오르면 저절로 풀림 · 시간이 다 되면 파업(생산 -{(1f - cfg.StrikeProduction) * 100f:0}%, {cfg.StrikeDuration:0}초)</color>");
                    foreach (var (choice, button, label) in _choices)
                    {
                        button.interactable = _crisis.CanChoose(choice);
                        label.SetText(choice switch
                        {
                            SocialCrisisSystem.Choice.Bribe => $"<b>무마</b>\n<size=85%>{HudText.Cost(cfg.BribeCost)}</size>",
                            SocialCrisisSystem.Choice.Crackdown => $"<b>강경 대응</b>\n<size=85%><color={HudText.Red}>만족도 {cfg.CrackdownSatisfaction:0} · 주민 -{cfg.CrackdownLeave}</color></size>",
                            SocialCrisisSystem.Choice.Wage => $"<b>임금 인상</b>\n<size=85%>{cfg.WageSeconds / 60f:0}분 상한 +{cfg.WageSatisfactionCap:0} · 생산 -{(1f - cfg.WageProduction) * 100f:0}%</size>",
                            _ => $"<b>방치</b>\n<size=85%><color={HudText.Red}>바로 파업</color></size>",
                        });
                    }
                    break;
                case SocialCrisisSystem.CrisisPhase.Striking:
                    SetCrisisVisible(true, false);
                    _crisisTitle.SetText($"{HudTheme.Icon("warning")} <b>파업 중</b>  <color={HudText.Orange}>{Clock(_crisis.TimeLeft)}</color>");
                    _crisisDetail.SetText($"<color=#E8C8C0>생산 -{(1f - cfg.StrikeProduction) * 100f:0}%</color>");
                    break;
                default:
                    SetCrisisVisible(false, false);
                    break;
            }
        }
    }
}
