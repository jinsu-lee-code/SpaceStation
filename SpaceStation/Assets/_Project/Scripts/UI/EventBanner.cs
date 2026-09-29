using SpaceStation.Data;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// 화면 상단 중앙 이벤트 배너. 발생 시 이름·설명을 잠시 보여주고, 지속형 이벤트 종료도 짧게 알린다.
    /// CanvasGroup 알파로 숨긴다 (오브젝트를 끄지 않음).
    /// </summary>
    public sealed class EventBanner : MonoBehaviour
    {
        [SerializeField] private EventController _events;
        [SerializeField] private CanvasGroup _group;
        [SerializeField] private Image _background;
        [SerializeField] private TMP_Text _title;
        [SerializeField] private TMP_Text _description;
        [SerializeField] private float _showSeconds = 5f;
        [SerializeField] private float _endedSeconds = 3f;
        [SerializeField] private Color _negativeColor = new Color(0.45f, 0.12f, 0.08f, 0.92f);
        [SerializeField] private Color _positiveColor = new Color(0.1f, 0.35f, 0.18f, 0.92f);
        [SerializeField] private Color _endedColor = new Color(0.12f, 0.14f, 0.18f, 0.9f);

        private float _hideAt;

        private void Start()
        {
            _events.Scheduler.EventStarted += HandleStarted;
            _events.Scheduler.EventEnded += HandleEnded;
            SetVisible(false);
        }

        private void OnDestroy()
        {
            if (_events == null || _events.Scheduler == null)
                return;
            _events.Scheduler.EventStarted -= HandleStarted;
            _events.Scheduler.EventEnded -= HandleEnded;
        }

        private void Update()
        {
            if (_hideAt > 0f && Time.unscaledTime >= _hideAt)
            {
                _hideAt = 0f;
                SetVisible(false);
            }
        }

        private void HandleStarted(GameEventData data)
        {
            _background.color = data.IsPositive ? _positiveColor : _negativeColor;
            _title.SetText(data.IsTimed ? $"{data.DisplayName}  <size=70%>({ActiveDuration(data):0}초)</size>" : data.DisplayName);
            _description.SetText(data.Description);
            _description.gameObject.SetActive(!string.IsNullOrEmpty(data.Description));
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
            _background.color = _endedColor;
            _title.SetText($"<size=80%>{active.Data.DisplayName} 종료</size>");
            _description.gameObject.SetActive(false);
            Show(_endedSeconds);
        }

        private void Show(float seconds)
        {
            SetVisible(true);
            _hideAt = Time.unscaledTime + seconds;
        }

        private void SetVisible(bool visible)
        {
            _group.alpha = visible ? 1f : 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;
        }
    }
}
