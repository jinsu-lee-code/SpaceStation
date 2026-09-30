using UnityEngine;

namespace SpaceStation.Building
{
    /// <summary>
    /// 태양광 패널 기울기 (5-3 피드백): 모듈을 어느 방향으로 돌려 놓아도 패널이 월드 기준으로 태양(Directional Light)을 향한다.
    /// 수직(위)에서 태양 쪽으로 최대 각도까지만 기울인다. 낮/밤으로 태양 고도가 바뀌면 따라간다.
    /// 이 컴포넌트는 패널 부품(Panel)을 담은 받침 오브젝트에 붙인다. 모델 교체 후에도 재사용.
    /// </summary>
    public sealed class SunFacingPanel : MonoBehaviour
    {
        [Tooltip("위쪽에서 태양 쪽으로 기울이는 최대 각도")]
        [SerializeField, Range(0f, 80f)] private float _maxTilt = 38f;
        [Tooltip("각도 변화가 이보다 작으면 갱신하지 않음")]
        [SerializeField] private float _epsilonDegrees = 0.2f;

        private static Light _sun;
        private Vector3 _appliedNormal;
        private bool _applied; // 첫 적용 여부 (영벡터와의 Vector3.Angle은 0이라 비교로 판단할 수 없음)

        private void OnEnable()
        {
            _applied = false;
            Face();
        }

        private void LateUpdate() => Face();

        /// <summary>즉시 태양 쪽으로 맞춘다 (에디터 미리보기·테스트용).</summary>
        public void Face()
        {
            var sun = FindSun();
            if (sun == null)
                return;
            Vector3 toSun = -sun.transform.forward;
            // 위쪽 기준으로 태양 방향까지의 각도를 최대 기울기로 제한
            Vector3 normal = Vector3.RotateTowards(Vector3.up, toSun, _maxTilt * Mathf.Deg2Rad, 0f);
            if (_applied && Vector3.Angle(normal, _appliedNormal) < _epsilonDegrees)
                return;
            _applied = true;
            _appliedNormal = normal;
            // 패널 방위는 태양 쪽 수평 방향에 맞춰 긴 변이 태양을 가로지르도록
            Vector3 flat = Vector3.ProjectOnPlane(toSun, Vector3.up);
            Vector3 forward = flat.sqrMagnitude > 1e-4f ? Vector3.ProjectOnPlane(flat, normal).normalized : Vector3.forward;
            transform.rotation = Quaternion.LookRotation(forward, normal);
        }

        private static Light FindSun()
        {
            if (_sun != null)
                return _sun;
            _sun = RenderSettings.sun;
            if (_sun == null)
            {
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (l.type == LightType.Directional)
                    {
                        _sun = l;
                        break;
                    }
                }
            }
            return _sun;
        }
    }
}
