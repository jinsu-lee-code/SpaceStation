using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 유리 엘리베이터 버튼 (<see cref="InteriorLift"/>): 캡슐 안 층 버튼 = 타고 그 층으로 · 층마다 문 옆 호출 버튼 = 캡슐을 이 층으로.
    /// InteriorMode가 시선이 닿은 버튼의 안내 글을 띄우고 상호작용 키(한 번)로 누름.
    /// </summary>
    public sealed class InteriorLiftButton : MonoBehaviour
    {
        [SerializeField] private InteriorLift _lift;
        [Tooltip("층 (0 = 1층)")]
        [SerializeField] private int _level;
        [Tooltip("층마다 문 옆 호출 버튼 (아니면 캡슐 안 층 버튼)")]
        [SerializeField] private bool _call;

        public void Configure(InteriorLift lift, int level, bool call)
        {
            _lift = lift;
            _level = level;
            _call = call;
        }

        /// <summary>안내 글 (빈 글 = 지금 누를 일 없음).</summary>
        public string Prompt(FirstPersonController player)
        {
            if (_lift == null || _lift.Moving)
                return string.Empty;
            if (_call)
                return _lift.Level == _level ? string.Empty : "엘리베이터 부르기";
            if (_lift.Level == _level)
                return string.Empty;
            return player != null && _lift.Contains(player.transform.position) ? $"{_level + 1}층으로" : string.Empty;
        }

        public void Press(FirstPersonController player)
        {
            if (_lift == null)
                return;
            if (_call)
                _lift.Call(_level);
            else
                _lift.Ride(_level, player);
        }
    }
}
