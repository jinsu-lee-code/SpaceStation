using SpaceStation.Core;
using UnityEngine;

namespace SpaceStation.Building
{
    /// <summary>
    /// 5-9 메인 메뉴 카메라: 정거장 둘레를 천천히 돈다. 정거장 크기에 맞춰 거리를 정하고,
    /// 화면 왼쪽 메뉴를 피해 정거장이 오른쪽에 보이도록 카메라를 옆으로 민다.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class MenuCameraOrbit : MonoBehaviour
    {
        [SerializeField] private StationController _station;
        [Tooltip("초당 회전 각도")]
        [SerializeField] private float _yawSpeed = 2.5f;
        [SerializeField] private float _pitch = 24f;
        [Tooltip("정거장이 화면에 꽉 차지 않도록 거리 배율")]
        [SerializeField] private float _distanceScale = 0.95f;
        [Tooltip("정거장을 화면 오른쪽으로 보내는 정도 (거리 대비)")]
        [SerializeField] private float _sideShift = 0.3f;
        [Tooltip("위아래로 천천히 흔들리는 폭(도)")]
        [SerializeField] private float _bob = 4f;

        private Camera _camera;
        private int _measuredCount = -1;
        private Vector3 _center;
        private float _radius = 4f;
        private float _yaw = 35f;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        private void LateUpdate()
        {
            if (_station != null && _station.Grid != null && _station.Grid.ModuleCount != _measuredCount)
                Measure();

            _yaw += _yawSpeed * Time.unscaledDeltaTime;
            float pitch = _pitch + Mathf.Sin(Time.unscaledTime * 0.1f) * _bob;
            var rotation = Quaternion.Euler(pitch, _yaw, 0f);
            float halfFov = _camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float distance = _radius / Mathf.Sin(halfFov) * _distanceScale;
            transform.rotation = rotation;
            transform.position = _center - rotation * Vector3.forward * distance - rotation * Vector3.right * distance * _sideShift;
        }

        private void Measure()
        {
            var grid = _station.Grid;
            _measuredCount = grid.ModuleCount;
            if (_measuredCount == 0)
                return;
            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (var m in grid.Modules)
            {
                foreach (var c in m.Cells)
                {
                    sum += GridConfig.CellToWorld(c);
                    n++;
                }
            }
            _center = sum / n;
            float r = 2f;
            foreach (var m in grid.Modules)
            {
                foreach (var c in m.Cells)
                    r = Mathf.Max(r, Vector3.Distance(_center, GridConfig.CellToWorld(c)) + 0.7f);
            }
            _radius = r;
        }
    }
}
