using SpaceStation.Audio;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// 13-3 타이쿤판 역병 경고 창 (화면 위쪽 가운데, 파업 경고 창과 같은 모양 — 그 아래로 쌓임).
    /// 경고 중: 남은 시간 · 검역 · 의료 충족 + 방역 물자 · 격리 봉쇄 · 위생 캠페인 · 방치. 역병 중: 남은 시간 · 잃은 주민 · 생산 감소.
    /// 상태는 색만이 아니라 글자로 (사용자 색약). 게임 시간은 멈추지 않는다.
    /// </summary>
    [DefaultExecutionOrder(-250)]
    public sealed class PlaguePanel : MonoBehaviour
    {
        private const float BoxWidth = 940f;
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
        private PlagueSystem _plague;
        private RectTransform _box;
        private CanvasGroup _group;
        private TMP_Text _title, _detail;
        private readonly (PlagueSystem.Choice Choice, Button Button, TMP_Text Label)[] _choices = new (PlagueSystem.Choice, Button, TMP_Text)[4];
        private float _nextRefresh;

        private void Start()
        {
            _sim = _station != null ? _station.Simulation : null;
            _plague = _sim?.GetSystem<PlagueSystem>();
            if (_plague == null)
            {
                gameObject.SetActive(false);
                return;
            }
            _ui = HoloUi.For(_holoArt, _font, _fillSprite, _frameSprite, _buttonSprite);
            Build();
            _plague.Changed += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            if (_plague != null)
                _plague.Changed -= Refresh;
        }

        private void Update()
        {
            if (_ui == null || Time.unscaledTime < _nextRefresh)
                return;
            _nextRefresh = Time.unscaledTime + _refreshSeconds;
            Refresh();
        }

        private void Build()
        {
            _box = HoloUi.Rect("PlagueChoices", (RectTransform)transform);
            _box.anchorMin = _box.anchorMax = _box.pivot = new Vector2(0.5f, 1f);
            _box.sizeDelta = new Vector2(BoxWidth, 186f);
            _ui.Panel(_box.gameObject, new Color(0.03f, 0.06f, 0.03f, 0.98f), HudTheme.Negative);
            _box.SetAsFirstSibling();
            var back = HoloUi.Rect("Backing", _box);
            HoloUi.Stretch(back, new Vector2(4f, 4f), new Vector2(-4f, -4f));
            back.SetAsFirstSibling();
            var bi = back.gameObject.AddComponent<Image>();
            bi.color = new Color(0.03f, 0.045f, 0.03f, 0.97f);
            bi.raycastTarget = false;
            _group = _box.gameObject.AddComponent<CanvasGroup>();
            _title = _ui.Label(_box, "", 28f, TextAlignmentOptions.Center);
            HoloUi.Place(_title.rectTransform, new Vector2(0f, -8f), new Vector2(BoxWidth, 38f));
            _detail = _ui.Label(_box, "", 18f, TextAlignmentOptions.Center, wrap: true);
            HoloUi.Place(_detail.rectTransform, new Vector2(20f, -48f), new Vector2(BoxWidth - 40f, 48f));
            var kinds = new[] { PlagueSystem.Choice.Supplies, PlagueSystem.Choice.Lockdown, PlagueSystem.Choice.Campaign, PlagueSystem.Choice.Ignore };
            for (int i = 0; i < kinds.Length; i++)
            {
                var c = kinds[i];
                var b = _ui.TechButton(_box, "", 20f, () => Choose(c));
                HoloUi.Place((RectTransform)b.transform, new Vector2(18f + i * 226f, -102f), new Vector2(214f, 72f));
                var label = b.GetComponentInChildren<TMP_Text>();
                label.enableWordWrapping = true;
                _choices[i] = (c, b, label);
            }
            SetVisible(false, false);
        }

        private void Choose(PlagueSystem.Choice choice)
        {
            if (_plague.Choose(choice))
                AudioService.TryPlay(l => l.UiClick);
            else
                AudioService.TryPlay(l => l.UiError);
            Refresh();
        }

        private void SetVisible(bool visible, bool buttons)
        {
            _group.alpha = visible ? 1f : 0f;
            _group.blocksRaycasts = visible && buttons;
            _group.interactable = visible && buttons;
            foreach (var (_, button, _) in _choices)
                if (button.gameObject.activeSelf != buttons)
                    button.gameObject.SetActive(buttons);
            _box.sizeDelta = new Vector2(BoxWidth, buttons ? 186f : 100f);
        }

        /// <summary>파업 경고 · 방문 대응 창이 떠 있으면 그 아래로.</summary>
        private float BoxY()
        {
            float y = -314f;
            var crisis = _sim.Crisis;
            if (crisis != null && crisis.Phase != SocialCrisisSystem.CrisisPhase.Calm)
                y -= crisis.Phase == SocialCrisisSystem.CrisisPhase.Warning ? 200f : 114f;
            if (_sim.Contact != null && _sim.Contact.Visiting)
                y -= 206f;
            return y;
        }

        private static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{s / 60}:{s % 60:00}";
        }

        private void Refresh()
        {
            if (_ui == null)
                return;
            if (_sim.Session.IsGameOver || _plague.Phase == PlagueSystem.PlaguePhase.Calm)
            {
                SetVisible(false, false);
                return;
            }
            _box.anchoredPosition = new Vector2(0f, BoxY());
            var cfg = _plague.Config;
            if (_plague.Phase == PlagueSystem.PlaguePhase.Warning)
            {
                SetVisible(true, true);
                _title.SetText($"{HudTheme.Icon("warning")} <b>역병 징후</b>  <color={HudText.Orange}>{Clock(_plague.TimeLeft)}</color>");
                _detail.SetText($"<color=#F4ECE8>검역 · 의료 부족 {_plague.Shortage * 100f:0}% · 시간이 다 되면 역병({cfg.OutbreakSeconds:0}초, {cfg.LossInterval:0}초마다 주민 약 {_plague.LossPerStep()}명 · 생산 -{(1f - cfg.OutbreakProduction) * 100f:0}%)</color>");
                foreach (var (choice, button, label) in _choices)
                {
                    button.interactable = _plague.CanChoose(choice);
                    label.SetText(choice switch
                    {
                        PlagueSystem.Choice.Supplies => $"<b>방역 물자</b>\n<size=88%>{HudText.Cost(cfg.SuppliesCost)}</size>",
                        PlagueSystem.Choice.Lockdown => $"<b>격리 봉쇄</b>\n<size=88%><color={Bad}>만족도 {cfg.LockdownSatisfaction:0} · 주민 -{cfg.LockdownLeave}</color></size>",
                        PlagueSystem.Choice.Campaign => $"<b>위생 캠페인</b>\n<size=88%>{cfg.CampaignSeconds / 60f:0}분 생산 -{(1f - cfg.CampaignProduction) * 100f:0}%</size>",
                        _ => $"<b>방치</b>\n<size=88%><color={Bad}>바로 역병</color></size>",
                    });
                }
            }
            else
            {
                SetVisible(true, false);
                _title.SetText($"{HudTheme.Icon("warning")} <b>역병 유행 중</b>  <color={HudText.Orange}>{Clock(_plague.TimeLeft)}</color>");
                _detail.SetText($"<color=#F4ECE8>잃은 주민 {_plague.Lost}명 · 생산 -{(1f - cfg.OutbreakProduction) * 100f:0}% · 검역 충족 {_plague.QuarantineRatio * 100f:0}% (높을수록 피해 적음)</color>");
            }
        }
    }
}
