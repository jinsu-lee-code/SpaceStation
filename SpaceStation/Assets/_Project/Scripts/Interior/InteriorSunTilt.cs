using SpaceStation.Core;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 11-7 태양광 정비 통로: 유리 천장 너머 패널을 바깥 패널(<see cref="SpaceStation.Building.SunFacingPanel"/>)처럼 태양 쪽으로 기울인다.
    /// 바깥은 태양 방위로 수평 회전도 하지만, 안쪽은 정사각 패널 3×3을 나란히 두므로 이웃과 겹치지 않게
    /// 바깥 통로 규칙과 같은 회전축(<see cref="ConnectorLayout.SolarSideAxis"/>, 월드 격자 축) 하나로만 기울인다. 기울기 한도는 바깥과 같음.
    /// 피벗 오브젝트(패널 중심)에 붙인다. 처음 월드 회전(모듈 회전)에 기울기를 한 번에 곱해 누적 오차가 없다.
    /// </summary>
    public sealed class InteriorSunTilt : MonoBehaviour
    {
        [SerializeField, Range(0f, 80f)] private float _maxTilt = 38f;
        [SerializeField] private float _epsilonDegrees = 0.2f;

        private static Light _sun;
        private Quaternion _baseRotation;
        private float _applied = float.NaN;

        private void Awake() => _baseRotation = transform.rotation;

        private void LateUpdate()
        {
            var sun = FindSun();
            if (sun == null)
                return;
            Vector3 axis = ConnectorLayout.SolarSideAxis;
            Vector3 toSun = Vector3.ProjectOnPlane(-sun.transform.forward, axis);
            if (toSun.sqrMagnitude < 1e-6f)
                return;
            Vector3 normal = Vector3.RotateTowards(Vector3.up, toSun.normalized, _maxTilt * Mathf.Deg2Rad, 0f);
            float angle = Vector3.SignedAngle(Vector3.up, normal, axis);
            if (!float.IsNaN(_applied) && Mathf.Abs(angle - _applied) < _epsilonDegrees)
                return;
            _applied = angle;
            transform.rotation = Quaternion.AngleAxis(angle, axis) * _baseRotation;
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
