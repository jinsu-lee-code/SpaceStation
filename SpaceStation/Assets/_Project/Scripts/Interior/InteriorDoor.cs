using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// Phase 11-2a 자동 미닫이 문: 플레이어가 가까이 오면 문짝 두 장이 양옆 벽 속으로 밀려 들어가고, 멀어지면 닫힌다.
    /// 닫혀 있는 동안만 막는 콜라이더를 켠다. 시뮬레이션 일시정지와 무관하게 실제 시간으로 움직인다.
    /// </summary>
    public sealed class InteriorDoor : MonoBehaviour
    {
        private const float OpenDistance = 2.2f;
        private const float Speed = 4f; // 초당 열림 비율

        private Transform _left;
        private Transform _right;
        private BoxCollider _blocker;
        private Transform _player;
        private float _slide;
        private float _open;

        /// <summary>문짝(오른쪽 기준 메시)과 막는 콜라이더를 만든다. 문 로컬 X = 가로, Y = 위 (피벗 = 문 구멍 가운데).</summary>
        public void Initialize(Transform left, Transform right, float slide, Vector3 blockerSize)
        {
            _left = left;
            _right = right;
            _slide = slide;
            _blocker = gameObject.AddComponent<BoxCollider>();
            _blocker.size = blockerSize;
            Apply();
        }

        public void SetPlayer(Transform player) => _player = player;

        private void Update()
        {
            bool near = false;
            if (_player != null)
            {
                var d = _player.position - transform.position;
                d.y = 0f;
                near = d.sqrMagnitude < OpenDistance * OpenDistance;
            }
            float target = near ? 1f : 0f;
            if (Mathf.Approximately(_open, target))
                return;
            _open = Mathf.MoveTowards(_open, target, Speed * Time.unscaledDeltaTime);
            Apply();
        }

        private void Apply()
        {
            float eased = _open * _open * (3f - 2f * _open);
            float closedX = _slide * 0.5f;
            _right.localPosition = new Vector3(closedX + eased * _slide, 0f, 0f);
            _left.localPosition = new Vector3(-closedX - eased * _slide, 0f, 0f);
            _blocker.enabled = _open < 0.9f;
        }
    }
}
