using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>
    /// 랜덤 이벤트 정의. 발생 확률(가중치)과 지속시간만 공통으로 가진다.
    /// 이벤트별 효과 수치는 3-3에서 이 클래스를 상속한 SO에 추가한다.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/Game Event", fileName = "EV_NewEvent")]
    public class GameEventData : ScriptableObject
    {
        [SerializeField] private string _displayName = "Event";
        [TextArea(2, 4)]
        [SerializeField] private string _description;
        [Tooltip("긍정 이벤트(보급선 등) 여부. 알림 색에 사용")]
        [SerializeField] private bool _isPositive;

        [Tooltip("랜덤 선택 가중치. 0 이하면 랜덤으로는 발생하지 않음")]
        [SerializeField, Min(0f)] private float _weight = 1f;
        [Tooltip("지속시간(초, 시뮬레이션 시간). 0이면 즉발")]
        [SerializeField, Min(0f)] private float _duration;

        public string DisplayName => _displayName;
        public string Description => _description;
        public bool IsPositive => _isPositive;
        public float Weight => _weight;
        public float Duration => _duration;
        public bool IsTimed => _duration > 0f;
    }
}
