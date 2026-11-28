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
    /// CONNECT_1121 7번 ② 타이쿤판 비리 경고 창 (화면 위쪽 가운데, 역병 경고 창과 같은 모양 — 파업 · 방문 · 역병 창 아래로 쌓임).
    /// 경고 중: 남은 시간 · 행정 부족 + 감사 · 강경 숙청 · 투명 행정 · 방치. 횡령 중: 남은 시간 · 잃은 금속 · 금속 생산 감소 · 다음 파업 기준 ↑.
    /// 상태는 색만이 아니라 글자로 (사용자 색약). 게임 시간은 멈추지 않는다.
    /// </summary>
    [DefaultExecutionOrder(-250)]
    public sealed class CorruptionPanel : MonoBehaviour
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
        private CorruptionSystem _corruption;
        private PlagueSystem _plague;
        private RectTransform _box;
        private CanvasGroup _group;
        private TMP_Text _title, _detail;
        private readonly (CorruptionSystem.Choice Choice, Button Button, TMP_Text Label)[] _choices = new (CorruptionSystem.Choice, Button, TMP_Text)[4];
        private float _nextRefresh;

        private void Start()
        {
            _sim = _station != null ? _station.Simulation : null;
            _corruption = _sim?.GetSystem<CorruptionSystem>();
            if (_corruption == null)
            {
                gameObject.SetActive(false);
                return;
            }
            _plague = _sim.GetSystem<PlagueSystem>();
            _ui = HoloUi.For(_holoArt, _font, _fillSprite, _frameSprite, _buttonSprite);
            Build();
            _corruption.Changed += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            if (_corruption != null)
                _corruption.Changed -= Refresh;
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
            _box = HoloUi.Rect("CorruptionChoices", (RectTransform)transform);
            _box.anchorMin = _box.anchorMax = _box.pivot = new Vector2(0.5f, 1f);
            _box.sizeDelta = new Vector2(BoxWidth, 186f);
            _ui.Panel(_box.gameObject, new Color(0.06f, 0.05f, 0.02f, 0.98f), HudTheme.Negative);
            _box.SetAsFirstSibling();
            var back = HoloUi.Rect("Backing", _box);
            HoloUi.Stretch(back, new Vector2(4f, 4f), new Vector2(-4f, -4f));
            back.SetAsFirstSibling();
            var bi = back.gameObject.AddComponent<Image>();
            bi.color = new Color(0.045f, 0.04f, 0.03f, 0.97f);
            bi.raycastTarget = false;
            _group = _box.gameObject.AddComponent<CanvasGroup>();
            _title = _ui.Label(_box, "", 28f, TextAlignmentOptions.Center);
            HoloUi.Place(_title.rectTransform, new Vector2(0f, -8f), new Vector2(BoxWidth, 38f));
            _detail = _ui.Label(_box, "", 18f, TextAlignmentOptions.Center, wrap: true);
            HoloUi.Place(_detail.rectTransform, new Vector2(20f, -48f), new Vector2(BoxWidth - 40f, 48f));
            var kinds = new[] { CorruptionSystem.Choice.Audit, CorruptionSystem.Choice.Purge, CorruptionSystem.Choice.Transparency, CorruptionSystem.Choice.Ignore };
            for (int i = 0; i < kinds.Length; i++)
            {
                var c = kinds[i];
                var b = _ui.TechButton(_box, "", 20f, () => Choose(c), clickSound: false); // 결과 소리 (막음 = 회복음 · 방치 = 경보) 하나만
                HoloUi.Place((RectTransform)b.transform, new Vector2(18f + i * 226f, -102f), new Vector2(214f, 72f));
                var label = b.GetComponentInChildren<TMP_Text>();
                label.enableWordWrapping = true;
                _choices[i] = (c, b, label);
            }
            SetVisible(false, false);
        }

        private void Choose(CorruptionSystem.Choice choice)
        {
            if (_corruption.Choose(choice))
            {
                // 결과 소리 (AlertWatch — 막음 = 확인음 · 방치 = 위험 경보)가 울렸으면 그것 하나만 (전용 소리는 예정 자리만 — CONNECT_1121 결정)
                if (!AlertSound.PlayedThisFrame(l => l.ContactResult) && !AlertSound.PlayedThisFrame(l => l.AlertDanger))
                    AudioService.TryPlay(l => l.UiClick);
            }
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

        /// <summary>파업 경고 · 방문 대응 · 역병 창이 떠 있으면 그 아래로.</summary>
        private float BoxY()
        {
            float y = -314f;
            var crisis = _sim.Crisis;
            if (crisis != null && crisis.Phase != SocialCrisisSystem.CrisisPhase.Calm)
                y -= crisis.Phase == SocialCrisisSystem.CrisisPhase.Warning ? 200f : 114f;
            if (_sim.Contact != null && _sim.Contact.Visiting)
                y -= 206f;
            if (_plague != null && _plague.Phase != PlagueSystem.PlaguePhase.Calm)
                y -= _plague.Phase == PlagueSystem.PlaguePhase.Warning ? 200f : 114f;
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
            if (_sim.Session.IsGameOver || _corruption.Phase == CorruptionSystem.CorruptionPhase.Calm)
            {
                SetVisible(false, false);
                return;
            }
            _box.anchoredPosition = new Vector2(0f, BoxY());
            var cfg = _corruption.Config;
            if (_corruption.Phase == CorruptionSystem.CorruptionPhase.Warning)
            {
                SetVisible(true, true);
                _title.SetText($"{HudTheme.Icon("warning")} <b>비리 징후</b>  <color={HudText.Orange}>{Clock(_corruption.TimeLeft)}</color>");
                _detail.SetText($"<color=#F4ECE8>행정 부족 {_corruption.Shortage * 100f:0}% · 시간이 다 되면 횡령({cfg.EmbezzleSeconds:0}초 금속 생산 -{(1f - cfg.EmbezzleMetal) * 100f:0}% · 저장 금속 -{cfg.EmbezzleStoredLoss * 100f:0}% · 다음 파업 기준 +{cfg.IgnoredStrikeBonus:0})</color>");
                foreach (var (choice, button, label) in _choices)
                {
                    button.interactable = _corruption.CanChoose(choice);
                    label.SetText(choice switch
                    {
                        CorruptionSystem.Choice.Audit => $"<b>감사</b>\n<size=88%>{HudText.Cost(cfg.AuditCost)}</size>",
                        CorruptionSystem.Choice.Purge => $"<b>강경 숙청</b>\n<size=88%><color={Bad}>만족도 {cfg.PurgeSatisfaction:0} · 주민 -{cfg.PurgeLeave}</color></size>",
                        CorruptionSystem.Choice.Transparency => $"<b>투명 행정</b>\n<size=88%>{cfg.TransparencySeconds / 60f:0}분 연구 -{(1f - cfg.TransparencyResearch) * 100f:0}%</size>",
                        _ => $"<b>방치</b>\n<size=88%><color={Bad}>바로 횡령</color></size>",
                    });
                }
            }
            else
            {
                SetVisible(true, false);
                _title.SetText($"{HudTheme.Icon("warning")} <b>횡령 중</b>  <color={HudText.Orange}>{Clock(_corruption.TimeLeft)}</color>");
                _detail.SetText($"<color=#F4ECE8>잃은 금속 {_corruption.Lost:0} · 금속 생산 -{(1f - cfg.EmbezzleMetal) * 100f:0}% · 다음 파업 기준 +{cfg.IgnoredStrikeBonus:0} (행정 구역을 늘리면 덜 일어남)</color>");
            }
        }
    }
}
