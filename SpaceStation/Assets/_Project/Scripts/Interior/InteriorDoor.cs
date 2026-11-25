using SpaceStation.Audio;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>문 소리 종류 (사운드 개편 D-6): 일반 문은 방 단계로 톤만 다름, 에어록은 따로. 트인 문간은 문이 없어 소리도 없음.
    /// 2026-11-26 금고문 = 방사선 차폐실 쪽 문짝 (고르는 규칙 = <see cref="InteriorDeviceSound.DoorFor"/>).</summary>
    public enum DoorSound
    {
        Basic,
        Stage1,
        Airlock,
        Vault,
    }

    /// <summary>
    /// Phase 11-2a 자동 미닫이 문: 플레이어가 가까이 오면 문짝 두 장이 양옆 벽 속으로 밀려 들어가고, 멀어지면 닫힌다.
    /// 닫혀 있는 동안만 막는 콜라이더를 켠다. 시뮬레이션 일시정지와 무관하게 실제 시간으로 움직인다.
    /// 2026-11-20 소리: 열리기 · 닫히기 시작할 때 문 자리에서 3D로. 경계에 서 있으면 열렸다 닫혔다 반복하지 않게
    /// 닫히는 거리를 조금 더 길게 + 같은 문의 소리 사이 최소 간격 (SOUND_LIST_NEW D-2 ⑥).
    /// </summary>
    public sealed class InteriorDoor : MonoBehaviour
    {
        private const float OpenDistance = 2.2f;
        private const float CloseDistance = 2.7f;
        private const float Speed = 4f; // 초당 열림 비율
        private const float SoundGap = 0.4f;

        private Transform _left;
        private Transform _right;
        private BoxCollider _blocker;
        private Transform _player;
        private float _leafWidth;
        private float _travel;
        private float _open;
        private bool _wantOpen;
        private DoorSound _sound;
        private float _lastSound = -999f;

        /// <summary>
        /// 문짝(오른쪽 기준 메시)과 막는 콜라이더를 만든다. 문 로컬 X = 가로, Y = 위 (피벗 = 문 구멍 가운데).
        /// travel = 열릴 때 미끄러지는 거리 (문틀 뒤까지 완전히 숨도록 문짝 폭보다 조금 길게).
        /// </summary>
        public void Initialize(Transform left, Transform right, float leafWidth, float travel, Vector3 blockerSize)
        {
            _left = left;
            _right = right;
            _leafWidth = leafWidth;
            _travel = travel;
            _blocker = gameObject.AddComponent<BoxCollider>();
            _blocker.size = blockerSize;
            Apply();
        }

        public void SetPlayer(Transform player) => _player = player;

        public void SetSound(DoorSound sound) => _sound = sound;

        private void Update()
        {
            bool want = false;
            if (_player != null)
            {
                var d = _player.position - transform.position;
                d.y = 0f;
                float reach = _wantOpen ? CloseDistance : OpenDistance;
                want = d.sqrMagnitude < reach * reach;
            }
            if (want != _wantOpen)
            {
                _wantOpen = want;
                PlaySound(want);
            }
            float target = want ? 1f : 0f;
            if (Mathf.Approximately(_open, target))
                return;
            _open = Mathf.MoveTowards(_open, target, Speed * Time.unscaledDeltaTime);
            Apply();
        }

        private void PlaySound(bool opening)
        {
            if (Time.unscaledTime - _lastSound < SoundGap)
                return;
            _lastSound = Time.unscaledTime;
            var at = transform.position;
            switch (_sound)
            {
                case DoorSound.Stage1:
                    AudioService.TryPlayAt(l => opening ? l.Door1Open : l.Door1Close, at);
                    break;
                case DoorSound.Airlock:
                    AudioService.TryPlayAt(l => opening ? l.AirlockOpen : l.AirlockClose, at);
                    break;
                case DoorSound.Vault:
                    AudioService.TryPlayAt(l => opening ? l.DoorVaultOpen : l.DoorVaultClose, at);
                    break;
                default:
                    AudioService.TryPlayAt(l => opening ? l.DoorOpen : l.DoorClose, at);
                    break;
            }
        }

        private void Apply()
        {
            float eased = _open * _open * (3f - 2f * _open);
            float closedX = _leafWidth * 0.5f;
            _right.localPosition = new Vector3(closedX + eased * _travel, 0f, 0f);
            _left.localPosition = new Vector3(-closedX - eased * _travel, 0f, 0f);
            _blocker.enabled = _open < 0.9f;
        }
    }
}
