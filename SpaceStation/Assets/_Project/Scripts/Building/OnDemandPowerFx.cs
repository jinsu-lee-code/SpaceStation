using SpaceStation.Simulation;
using UnityEngine;

namespace SpaceStation.Building
{
    /// <summary>
    /// 8-5 연료전지 표시등: 보조 발전이 돌 때만 "Lamp" 부품을 켠다 (StationArtBuilder가 자동 배선).
    /// 재질 색은 ModuleView가 상태별로 다루므로 여기서는 켜고 끄기만 한다. 배치 고스트에서는 항상 켜 둔다.
    /// </summary>
    public sealed class OnDemandPowerFx : MonoBehaviour
    {
        [SerializeField] private GameObject _lamp;
        [Tooltip("이 가동 비율을 넘으면 켜짐")]
        [SerializeField, Range(0f, 1f)] private float _threshold = 0.005f;

        private ModuleView _view;
        private ResourceController _resources;
        private bool _live;

        private void Start()
        {
            _view = GetComponentInParent<ModuleView>();
            _live = _view != null && _view.Module != null;
            if (_live)
                _resources = FindFirstObjectByType<ResourceController>(); // 한 번만
        }

        private void Update()
        {
            if (_lamp == null)
                return;
            bool on = !_live || (_resources != null && _view.Operational
                                 && _resources.Simulation.OnDemandLoad > _threshold
                                 && !_resources.Simulation.IsHeldByReserve(_view.Module.Data));
            if (_lamp.activeSelf != on)
                _lamp.SetActive(on);
        }
    }
}
