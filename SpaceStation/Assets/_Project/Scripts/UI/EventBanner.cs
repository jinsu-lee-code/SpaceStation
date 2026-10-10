using SpaceStation.Data;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// 화면 상단 중앙 이벤트 배너. 발생 시 이름·설명을 잠시 보여주고, 지속형 이벤트 종료도 짧게 알린다.
    /// 5-8: 홀로그램 패널(어두운 바탕 + 위험도 색 테두리), 아이콘, 설명은 첫 문장만, 위에서 내려오는 등장 애니메이션.
    /// CanvasGroup 알파로 숨긴다 (오브젝트를 끄지 않음).
    /// </summary>
    public sealed class EventBanner : MonoBehaviour
    {
        [SerializeField] private EventController _events;
        [SerializeField] private CanvasGroup _group;
        [SerializeField] private Image _background;
        [Tooltip("위험도 색 테두리 (HUD 스타일 적용 시 생기는 Frame)")]
        [SerializeField] private Image _frame;
        [SerializeField] private TMP_Text _title;
        [SerializeField] private TMP_Text _description;
        [SerializeField] private float _showSeconds = 5f;
        [SerializeField] private float _endedSeconds = 3f;

        [Tooltip("8-5 화물선 도착 알림 표시 시간")]
        [SerializeField] private float _cargoSeconds = 3f;

        private float _hideAt;
        private UiTween _tween;
        private CargoSystem _cargo;
        private HoloSkin _skin;

        private void Start()
        {
            _events.Scheduler.EventStarted += HandleStarted;
            _events.Scheduler.EventEnded += HandleEnded;
            // 8-5 화물 터미널 화물선 (씬 연결 없이 한 번만 찾음)
            var station = FindFirstObjectByType<SpaceStation.Building.StationController>();
            _cargo = station != null && station.Simulation != null ? station.Simulation.Cargo : null;
            if (_cargo != null)
                _cargo.Delivered += HandleCargo;
            _skin = GetComponent<HoloSkin>(); // 11-15 테크 테두리 (있으면 테두리 · 빛 번짐 · 줄무늬를 함께 물들임)
            if (_frame == null && _skin == null)
            {
                var f = transform.Find("Frame");
                if (f != null)
                    _frame = f.GetComponent<Image>();
            }
            _group.blocksRaycasts = false;
            _group.interactable = false;
            if (_skin != null)
                HoloGlitch.Add((RectTransform)transform, _skin.Art);
            _tween = new UiTween((RectTransform)transform, _group, new Vector2(0f, 36f), glitch: true);
        }

        private void OnDestroy()
        {
            if (_cargo != null)
                _cargo.Delivered -= HandleCargo;
            if (_events == null || _events.Scheduler == null)
                return;
            _events.Scheduler.EventStarted -= HandleStarted;
            _events.Scheduler.EventEnded -= HandleEnded;
        }

        private void Update()
        {
            _tween?.Update();
            if (_hideAt > 0f && Time.unscaledTime >= _hideAt)
            {
                _hideAt = 0f;
                _tween?.Hide();
            }
        }

        private void HandleStarted(GameEventData data)
        {
            Color tone = data.IsPositive ? HudTheme.Positive : HudTheme.Negative;
            SetTone(tone, 0.28f);
            string icon = HudTheme.Icon(data.IsPositive ? "event" : "warning");
            string title = data.IsTimed ? $"{data.DisplayName}  <size=70%><color={HudText.Muted}>{ActiveDuration(data):0}초</color></size>" : data.DisplayName;
            _title.SetText($"{icon} {title}");
            string description = FirstSentence(data.Description);
            _description.SetText(description);
            _description.gameObject.SetActive(!string.IsNullOrEmpty(description));
            Show(_showSeconds);
        }

        /// <summary>등급 강도 배율이 반영된 실제 지속시간.</summary>
        private float ActiveDuration(GameEventData data)
        {
            foreach (var a in _events.Scheduler.ActiveEvents)
            {
                if (a.Data == data)
                    return a.Duration;
            }
            return data.Duration;
        }

        private void HandleEnded(ActiveEvent active)
        {
            SetTone(HudTheme.Neutral, 0.15f);
            _title.SetText($"<size=80%>{HudTheme.Icon("info")} {active.Data.DisplayName} 종료</size>");
            _description.gameObject.SetActive(false);
            Show(_endedSeconds);
        }

        /// <summary>8-5: 화물선 도착 — 짧고 작게 (가장 부족한 자원).</summary>
        private void HandleCargo(SpaceStation.Core.ModuleInstance terminal, ResourceType type, float amount)
        {
            SetTone(HudTheme.Positive, 0.15f);
            string extra = amount > 0.5f ? $"{HudTheme.Icon(type)} {HudText.ResourceName(type)} +{amount:0}" : $"{HudText.ResourceName(type)} (저장 한도 가득)";
            _title.SetText($"<size=80%>{HudTheme.Icon("event")} 화물선 도착 · {extra}</size>");
            _description.gameObject.SetActive(false);
            Show(_cargoSeconds);
        }

        /// <summary>바탕은 어두운 패널에 위험도 색을 살짝 섞고, 테두리는 위험도 색.</summary>
        private void SetTone(Color tone, float tint)
        {
            var fill = Color.Lerp(HudTheme.PanelFill, tone * 0.5f, tint);
            fill.a = 0.9f;
            _background.color = fill;
            if (_skin != null)
                _skin.SetLine(new Color(tone.r, tone.g, tone.b, 0.95f));
            else if (_frame != null)
                _frame.color = new Color(tone.r, tone.g, tone.b, 0.95f);
        }

        private static string FirstSentence(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;
            int end = text.IndexOf(". ", System.StringComparison.Ordinal);
            return end > 0 ? text.Substring(0, end + 1) : text;
        }

        private void Show(float seconds)
        {
            _tween?.Play(restart: !_tween.Visible);
            _hideAt = Time.unscaledTime + seconds;
        }
    }
}
