using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 11-6 내부 장식 회전: 부모(템플릿 프리팹) 기준 한 점·축을 중심으로 계속 돈다 (연구소 홀로그램 위성의 궤도, 핵 자전).
    /// 연출 전용이라 게임 시간과 무관하게(일시정지 중 방문해도) 실제 시간으로 돈다.
    /// 매 프레임 RotateAround로 누적하면 내부 공간(y −5000)의 큰 월드 좌표 오차가 쌓여 궤도를 벗어났음 →
    /// 처음 로컬 위치·회전에 누적 각도를 한 번에 적용 (부모 로컬 좌표의 작은 값으로 계산).
    /// </summary>
    public class InteriorOrbit : MonoBehaviour
    {
        [SerializeField] private Vector3 _localCenter;
        [SerializeField] private Vector3 _localAxis = Vector3.up;
        [SerializeField] private float _degreesPerSecond = 30f;

        private Vector3 _startPosition;
        private Quaternion _startRotation;
        private float _angle;

        /// <summary>지금까지 돈 각 (0~360, 도). 12-5 센서 레이더가 빛줄 자리를 읽음.</summary>
        public float Angle => _angle;
        /// <summary>도는 빠르기 배율 (1 = 처음 값, 0 = 멈춤) — 바이오스피어 물레방아는 물이 지나갈 때만 돎.</summary>
        public float SpeedScale { get; set; } = 1f;

        public void Configure(Vector3 localCenter, Vector3 localAxis, float degreesPerSecond)
        {
            _localCenter = localCenter;
            _localAxis = localAxis.normalized;
            _degreesPerSecond = degreesPerSecond;
        }

        private void Awake()
        {
            _startPosition = transform.localPosition;
            _startRotation = transform.localRotation;
        }

        private void Update()
        {
            _angle = Mathf.Repeat(_angle + _degreesPerSecond * SpeedScale * Time.unscaledDeltaTime, 360f);
            var turn = Quaternion.AngleAxis(_angle, _localAxis);
            transform.localPosition = _localCenter + turn * (_startPosition - _localCenter);
            transform.localRotation = turn * _startRotation;
            UpdateSound();
        }

        // 사운드 개편 4-4 · D-8 (2026-11-26): 부품 이름으로 고른 물체 소리 (InteriorObjectSound.ForOrbitPart — 템플릿을 다시 지어도 따라감) ·
        // 지금 있는 방일 때만 · 멈추면 (SpeedScale 0) 끔. 날아다니는 드론 = 날개 웅 (침입 드론과 같은 녹음, 작게) · 홀로그램 · 플라즈마 = 작은 전자 웅 · 롤러 = 벨트 · 회전 선반 = 모터 낮게.
        private bool _soundChecked;
        private InteriorObjectSound.OrbitSound _sound;
        private SpaceStation.Audio.ObjectLoop _loop;
        private float _loopGain;
        private SpaceStation.Core.ModuleInstance _room;
        private float _roomAt = -99f, _hereAt = -99f;
        private bool _here;

        private void UpdateSound()
        {
            if (!_soundChecked)
            {
                _sound = InteriorObjectSound.ForOrbitPart(name);
                _soundChecked = true;
            }
            if (_sound == InteriorObjectSound.OrbitSound.None)
                return;
            if (_loop == null)
            {
                if (SpaceStation.Audio.AudioService.Instance == null)
                    return;
                var at = InteriorMotionSound.LocalCenter(transform);
                switch (_sound)
                {
                    case InteriorObjectSound.OrbitSound.Drone:
                        _loop = InteriorMotionSound.LoopAt(transform, at, l => l.DroneHoverLoop, 1.5f, 10f);
                        _loopGain = 0.45f;
                        break;
                    case InteriorObjectSound.OrbitSound.Hologram:
                        _loop = InteriorMotionSound.LoopAt(transform, at, l => l.ObjScreenHumLoop, 1f, 5f);
                        _loopGain = 1f;
                        break;
                    case InteriorObjectSound.OrbitSound.Roller:
                        _loop = InteriorMotionSound.LoopAt(transform, at, l => l.ObjConveyorLoop, 1.5f, 10f);
                        _loopGain = 0.6f;
                        break;
                    default:
                        _loop = InteriorMotionSound.LoopAt(transform, at, l => l.RefineryCraneMoveLoop, 2f, 12f);
                        _loopGain = 0.35f;
                        if (_loop != null)
                            _loop.Pitch = 0.8f;
                        break;
                }
                if (_loop == null)
                {
                    _sound = InteriorObjectSound.OrbitSound.None;   // 클립이 없음 — 다시 찾지 않음
                    return;
                }
            }
            bool here = InteriorMotionSound.PlayerInRoomOf(transform, ref _room, ref _roomAt, ref _hereAt, ref _here);
            _loop.Level = here && SpeedScale > 0.05f ? _loopGain : 0f;
        }
    }
}
