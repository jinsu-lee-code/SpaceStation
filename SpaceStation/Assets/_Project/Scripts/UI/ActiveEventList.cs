using System.Text;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;

namespace SpaceStation.UI
{
    /// <summary>자원 패널 하단의 진행 중 이벤트 목록 (이름 + 남은 시간). 비어 있으면 숨긴다.</summary>
    public sealed class ActiveEventList : MonoBehaviour
    {
        [SerializeField] private EventController _events;
        [SerializeField] private TMP_Text _text;

        private readonly StringBuilder _sb = new StringBuilder(256);
        private bool _dirty = true;

        private void Start()
        {
            _events.Scheduler.Changed += MarkDirty;
            Refresh();
        }

        private void OnDestroy()
        {
            if (_events != null && _events.Scheduler != null)
                _events.Scheduler.Changed -= MarkDirty;
        }

        private void LateUpdate()
        {
            if (_dirty)
                Refresh();
        }

        private void MarkDirty()
        {
            _dirty = true;
        }

        private void Refresh()
        {
            _dirty = false;
            var active = _events.Scheduler.ActiveEvents;
            bool any = active.Count > 0;
            if (_text.gameObject.activeSelf != any)
                _text.gameObject.SetActive(any);
            if (!any)
                return;

            _sb.Clear();
            _sb.Append("<b>진행 중 이벤트</b>");
            foreach (var a in active)
            {
                string color = a.Data.IsPositive ? "#7CFF9A" : HudText.Orange;
                _sb.Append("\n<color=").Append(color).Append('>').Append(a.Data.DisplayName).Append("</color>")
                   .Append("<pos=62%>남은 ").Append(Mathf.CeilToInt(a.Remaining)).Append("초");
            }
            _text.SetText(_sb);
        }
    }
}
