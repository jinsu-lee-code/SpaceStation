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
            _angle = Mathf.Repeat(_angle + _degreesPerSecond * Time.unscaledDeltaTime, 360f);
            var turn = Quaternion.AngleAxis(_angle, _localAxis);
            transform.localPosition = _localCenter + turn * (_startPosition - _localCenter);
            transform.localRotation = turn * _startRotation;
        }
    }
}
