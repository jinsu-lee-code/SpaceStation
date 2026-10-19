using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 12-5 템플릿 안 미닫이 문 (검역소 격리실 — 2026-10-19 피드백: 문이 안 열려 안에 있는 주민과 대화할 수 없었음).
    /// 템플릿 조각(INT_*_Door, MeshCollider 포함)을 플레이어가 가까이 오면 <see cref="_openOffset"/>만큼 미끄러뜨리고, 멀어지면 닫는다.
    /// 콜라이더가 조각과 같이 움직여 열린 동안 지나갈 수 있다. 실제 시간(unscaled)으로 움직임 (내부 일시정지 중에도 열림).
    /// 좌표는 프리팹 로컬 (= 템플릿 로컬, 회전 전).
    /// </summary>
    public sealed class InteriorSlidingDoor : MonoBehaviour
    {
        private const float Speed = 2.5f;     // 초당 열림 비율
        private const float FindInterval = 0.5f;

        [SerializeField] private Transform _door;
        [SerializeField] private Vector3 _openOffset;
        [Tooltip("열림 판정 가운데 (문 앞). 수평 거리만 봄")]
        [SerializeField] private Vector3 _trigger;
        [SerializeField] private float _radius = 1.8f;

        private Vector3 _closed;
        private float _open;
        private Transform _player;
        private float _nextFind;

        public void Configure(Transform door, Vector3 openOffset, Vector3 trigger, float radius)
        {
            _door = door;
            _openOffset = openOffset;
            _trigger = trigger;
            _radius = radius;
        }

        private void Awake()
        {
            if (_door != null)
                _closed = _door.localPosition;
        }

        private void Update()
        {
            if (_door == null)
                return;
            if (_player == null && Time.unscaledTime >= _nextFind)
            {
                _nextFind = Time.unscaledTime + FindInterval; // 내부에 들어간 뒤 플레이어가 생김 (없을 때만 가끔 찾음)
                var fp = FindFirstObjectByType<FirstPersonController>();
                _player = fp != null ? fp.transform : null;
            }
            bool near = false;
            if (_player != null)
            {
                var d = transform.InverseTransformPoint(_player.position) - _trigger;
                d.y = 0f;
                near = d.sqrMagnitude < _radius * _radius;
            }
            float target = near ? 1f : 0f;
            if (Mathf.Approximately(_open, target))
                return;
            _open = Mathf.MoveTowards(_open, target, Speed * Time.unscaledDeltaTime);
            float eased = _open * _open * (3f - 2f * _open);
            _door.localPosition = _closed + _openOffset * eased;
        }
    }
}
