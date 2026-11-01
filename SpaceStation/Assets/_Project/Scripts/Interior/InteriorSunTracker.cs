using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 태양광 어레이 내부 추적 가대 (사용자 2026-10-29 "실제 방향에 맞춰줘"): 바깥 패널(<see cref="SpaceStation.Building.SunFacingPanel"/>)과
    /// 같은 규칙으로 실제 태양(Directional Light) 쪽을 본다 — 기둥 · 톱니 · 머리(<see cref="_mast"/>)는 해의 수평 방위로 돌고,
    /// 머리 가로축에 걸린 패널 줄(<see cref="_panels"/>)은 위에서 해 쪽으로 최대 기울기까지 기운다.
    /// 바깥은 패널판이 통째로 기울지만, 안은 천장 우물(높이 1.2m)에 들어가게 축마다 기운다.
    /// 돌기 전 = 머리의 해 쪽이 템플릿 +z, 패널 축이 템플릿 x. 템플릿(= 모듈) 회전을 빼고 계산하므로 모듈을 어느 쪽으로 돌려 놓아도 맞다.
    /// 처음엔 바로 맞추고, 해 방향이 바뀌면 일정한 속도로 따라 돈다. 처음 로컬 위치 · 회전에 한 번에 곱해 누적 오차가 없다 (<see cref="InteriorOrbit"/>와 같음).
    /// </summary>
    public sealed class InteriorSunTracker : MonoBehaviour
    {
        [SerializeField] private Transform _mast;
        [SerializeField] private Transform[] _panels;
        [Tooltip("기둥 축 위 한 점 (이 오브젝트 로컬)")]
        [SerializeField] private Vector3 _center;
        [Tooltip("패널 줄마다 축 위 한 점 (돌기 전, 이 오브젝트 로컬). 축 방향 = 로컬 x")]
        [SerializeField] private Vector3[] _pivots;
        [Tooltip("위쪽에서 태양 쪽으로 기울이는 최대 각도 (바깥 SunFacingPanel과 같음)")]
        [SerializeField, Range(0f, 80f)] private float _maxTilt = 38f;
        [Tooltip("해 방향이 바뀔 때 따라 도는 속도 (도/초)")]
        [SerializeField] private float _degreesPerSecond = 15f;

        private static Light _sun;
        private Vector3 _mastPosition;
        private Quaternion _mastRotation;
        private Vector3[] _panelPositions;
        private Quaternion[] _panelRotations;
        private float _yaw;
        private float _tilt;
        private bool _aimed;

        /// <summary>지금 머리 방위 (템플릿 로컬, +z에서 +x 쪽으로 도).</summary>
        public float Yaw => _yaw;

        /// <summary>지금 패널 기울기 (도).</summary>
        public float Tilt => _tilt;

        public void Configure(Transform mast, Transform[] panels, Vector3 center, Vector3[] pivots)
        {
            _mast = mast;
            _panels = panels;
            _center = center;
            _pivots = pivots;
        }

        private void Awake()
        {
            _mastPosition = _mast.localPosition;
            _mastRotation = _mast.localRotation;
            _panelPositions = new Vector3[_panels.Length];
            _panelRotations = new Quaternion[_panels.Length];
            for (int i = 0; i < _panels.Length; i++)
            {
                _panelPositions[i] = _panels[i].localPosition;
                _panelRotations[i] = _panels[i].localRotation;
            }
        }

        private void LateUpdate()
        {
            var sun = FindSun();
            if (sun == null)
                return;
            Vector3 toSun = transform.InverseTransformDirection(-sun.transform.forward);
            Vector3 flat = Vector3.ProjectOnPlane(toSun, Vector3.up);
            float yaw = flat.sqrMagnitude > 1e-4f ? Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg : _yaw; // 해가 바로 위면 방위 그대로
            float tilt = Mathf.Min(Vector3.Angle(Vector3.up, toSun), _maxTilt);
            if (!_aimed)
            {
                _yaw = yaw;
                _tilt = tilt;
                _aimed = true;
            }
            else
            {
                if (Mathf.Abs(Mathf.DeltaAngle(_yaw, yaw)) < 0.05f && Mathf.Abs(_tilt - tilt) < 0.05f)
                    return;
                float step = _degreesPerSecond * Time.unscaledDeltaTime;
                _yaw = Mathf.MoveTowardsAngle(_yaw, yaw, step);
                _tilt = Mathf.MoveTowards(_tilt, tilt, step);
            }
            Apply();
        }

        private void Apply()
        {
            var turn = Quaternion.AngleAxis(_yaw, Vector3.up);
            _mast.localPosition = _center + turn * (_mastPosition - _center);
            _mast.localRotation = turn * _mastRotation;
            // 패널 줄: 머리와 함께 돈 축을 중심으로 해 쪽(+z)으로 기욺
            var lean = turn * Quaternion.AngleAxis(_tilt, Vector3.right);
            for (int i = 0; i < _panels.Length; i++)
            {
                Vector3 pivot = _pivots[i];
                _panels[i].localPosition = _center + turn * (pivot - _center) + lean * (_panelPositions[i] - pivot);
                _panels[i].localRotation = lean * _panelRotations[i];
            }
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
