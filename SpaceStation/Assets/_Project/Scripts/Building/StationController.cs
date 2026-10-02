using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Settings;
using SpaceStation.Simulation;
using UnityEngine;

namespace SpaceStation.Building
{
    /// <summary>
    /// 정거장의 씬 표현. <see cref="StationSimulation"/>의 그리드를 따라 모듈 프리팹을 생성/제거하고,
    /// 연결(활성/비활성)·파손 상태를 <see cref="ModuleView"/>에 반영한다.
    /// 배치·철거 명령은 시뮬레이션으로 그대로 전달한다 (규칙·비용 판정은 시뮬레이션이 한다).
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public sealed class StationController : MonoBehaviour
    {
        [SerializeField] private SimulationHost _host;
        [Tooltip("생성된 모듈 오브젝트의 부모. 비우면 이 오브젝트 아래에 둔다.")]
        [SerializeField] private Transform _moduleRoot;
        [Tooltip("선택 가장자리 빛 재질 (SpaceStation/SelectionRim, 5-4)")]
        [SerializeField] private Material _selectionRimMaterial;
        [Tooltip("7-2 노후 테두리 빛 색 (HDR). 선택 테두리 재질을 복제해 만든 공용 재질에 쓴다")]
        [SerializeField, ColorUsage(false, true)] private Color _wornRimColor = new Color(1.9f, 0.95f, 0.3f, 1f);
        [SerializeField] private float _wornRimPulseSpeed = 1.2f;
        [SerializeField, Range(0f, 1f)] private float _wornRimPulseAmount = 0.35f;

        private readonly Dictionary<ModuleInstance, ModuleView> _views = new Dictionary<ModuleInstance, ModuleView>();
        private StationSimulation _sim;
        private Material _wornRimMaterial;
        private bool _wornRimStyle;

        public StationSimulation Simulation => _sim;
        public StationGrid Grid => _sim.Grid;
        public StationConnectivity Connectivity => _sim.Connectivity;
        public ModuleInstance Core => _sim.Core;

        private void Awake()
        {
            if (_moduleRoot == null)
                _moduleRoot = transform;

            _sim = _host.Simulation; // SimulationHost.Awake(-100) 이후
            CreateWornRimMaterial();
            _wornRimStyle = GameSettings.WornDisplay == WornDisplay.Rim;
            GameSettings.Changed += HandleSettingsChanged;
            foreach (var module in _sim.Grid.Modules)
                CreateView(module); // 코어 등 이미 배치된 모듈

            _sim.Grid.ModulePlaced += CreateView;
            _sim.Grid.ModuleRemoved += HandleModuleRemoved;
            _sim.Connectivity.ActiveStateChanged += HandleActiveStateChanged;
            _sim.Damage.Damaged += HandleDamaged;
            _sim.Damage.RepairStarted += HandleRepairStarted;
            _sim.Damage.Repaired += HandleRepaired;
            _sim.Resources.Ticked += RefreshWornVisuals;
            _sim.Durability.Maintained += HandleMaintained;
            _sim.ShieldDeflected += HandleShieldDeflected;
        }

        private void OnDestroy()
        {
            GameSettings.Changed -= HandleSettingsChanged;
            if (_wornRimMaterial != null)
                Destroy(_wornRimMaterial);
            if (_sim == null)
                return;
            _sim.Grid.ModulePlaced -= CreateView;
            _sim.Grid.ModuleRemoved -= HandleModuleRemoved;
            _sim.Connectivity.ActiveStateChanged -= HandleActiveStateChanged;
            _sim.Damage.Damaged -= HandleDamaged;
            _sim.Damage.RepairStarted -= HandleRepairStarted;
            _sim.Damage.Repaired -= HandleRepaired;
            _sim.Resources.Ticked -= RefreshWornVisuals;
            _sim.Durability.Maintained -= HandleMaintained;
            _sim.ShieldDeflected -= HandleShieldDeflected;
        }

        /// <summary>
        /// 5-7 운석 연출(MeteorFx)이 있으면 true: 실드 번쩍임은 운석이 실드에 닿는 순간,
        /// 파손 표시는 운석이 모듈에 닿는 순간 연출 쪽에서 적용한다.
        /// </summary>
        public bool DeferMeteorVisuals { get; set; }

        private readonly Dictionary<ModuleInstance, float> _damageHold = new Dictionary<ModuleInstance, float>();
        private readonly List<ModuleInstance> _releaseBuffer = new List<ModuleInstance>();

        private void HandleShieldDeflected(ModuleInstance shield)
        {
            if (DeferMeteorVisuals)
                return;
            if (shield != null && _views.TryGetValue(shield, out var view))
                view.PlayImpulse();
        }

        /// <summary>운석이 도착할 때까지 이 모듈의 파손 표시를 미룬다 (최대 seconds 뒤에는 자동 적용).</summary>
        public void HoldDamageVisual(ModuleInstance module, float seconds)
        {
            if (module != null)
                _damageHold[module] = seconds;
        }

        /// <summary>운석 도착을 기다리며 파손 표시를 미뤄 둔 모듈인지 (소리도 충돌 순간에 낸다).</summary>
        public bool IsDamageHeld(ModuleInstance module) => module != null && _damageHold.ContainsKey(module);

        /// <summary>미뤄 둔 파손 표시를 지금 적용 (운석 충돌 순간).</summary>
        public void ReleaseDamageVisual(ModuleInstance module)
        {
            if (module == null || !_damageHold.Remove(module))
                return;
            if (_sim.Damage.TryGetInfo(module, out var info))
                SetDamageVisual(module, info.IsRepairing ? ModuleDamageVisual.Repairing : ModuleDamageVisual.Damaged);
        }

        private void Update()
        {
            if (_damageHold.Count == 0)
                return;
            _releaseBuffer.Clear();
            foreach (var module in new List<ModuleInstance>(_damageHold.Keys))
            {
                float left = _damageHold[module] - Time.deltaTime;
                if (left <= 0f)
                    _releaseBuffer.Add(module);
                else
                    _damageHold[module] = left;
            }
            foreach (var module in _releaseBuffer)
                ReleaseDamageVisual(module); // 연출이 놓쳐도 안전하게 적용
        }

        /// <summary>7-2: 노후 테두리 공용 재질 (선택 테두리 재질 복제 + 호박색·느린 맥동). 모든 노후 모듈이 같은 재질을 공유한다.</summary>
        private void CreateWornRimMaterial()
        {
            if (_selectionRimMaterial == null)
                return;
            _wornRimMaterial = new Material(_selectionRimMaterial) { name = "M_WornRim (Runtime)" };
            _wornRimMaterial.SetColor("_RimColor", _wornRimColor);
            _wornRimMaterial.SetFloat("_PulseSpeed", _wornRimPulseSpeed);
            _wornRimMaterial.SetFloat("_PulseAmount", _wornRimPulseAmount);
        }

        private void HandleSettingsChanged()
        {
            bool rim = GameSettings.WornDisplay == WornDisplay.Rim;
            if (rim == _wornRimStyle)
                return;
            _wornRimStyle = rim;
            foreach (var view in _views.Values)
            {
                if (view != null)
                    view.SetWornRimStyle(rim);
            }
        }

        /// <summary>틱마다 내구도 효율 저하 여부를 뷰에 반영 (상태가 바뀐 뷰만 MPB 갱신).</summary>
        private void RefreshWornVisuals()
        {
            foreach (var info in _sim.Durability.Modules)
            {
                if (_views.TryGetValue(info.Module, out var view))
                    view.SetWorn(_sim.Durability.EfficiencyFor(info.Current) < 1f);
            }
        }

        private void HandleMaintained(DurabilityInfo info)
        {
            if (_views.TryGetValue(info.Module, out var view))
                view.SetWorn(false);
        }

        public bool CanPlace(ModuleData data, Vector3Int origin, int rotation)
            => _sim.EvaluatePlacement(data, origin, rotation) == PlacementResult.Valid;

        public PlacementResult EvaluatePlacement(ModuleData data, Vector3Int origin, int rotation)
            => _sim.EvaluatePlacement(data, origin, rotation);

        public PlacementResult CheckBuildable(ModuleData data) => _sim.CheckBuildable(data);
        public bool CanAfford(ModuleData data) => _sim.CanAfford(data);

        public bool TryPlace(ModuleData data, Vector3Int origin, int rotation, out ModuleInstance module)
            => _sim.TryPlace(data, origin, rotation, out module);

        public bool CanRemove(ModuleInstance module) => _sim.CanRemove(module);
        public bool IsSupportingOthers(ModuleInstance module) => _sim.IsSupportingOthers(module);
        public bool TryRemove(ModuleInstance module) => _sim.TryRemove(module);
        public List<ResourceAmount> GetRefund(ModuleInstance module) => _sim.GetRefund(module);
        public bool DestroyModule(ModuleInstance module) => _sim.DestroyModule(module);

        public bool TryGetView(ModuleInstance module, out ModuleView view)
        {
            return _views.TryGetValue(module, out view);
        }

        private void CreateView(ModuleInstance module)
        {
            var prefab = module.Data != null ? module.Data.Prefab : null;
            if (prefab == null)
            {
                Debug.LogWarning($"{module}: 프리팹이 없어 표시하지 않음", this);
                return;
            }

            var go = Instantiate(prefab,
                GridConfig.CellToWorld(module.Origin),
                GridDirections.ToQuaternion(module.Rotation),
                _moduleRoot);
            go.name = module.ToString();
            if (!go.TryGetComponent<ModuleView>(out var view))
                view = go.AddComponent<ModuleView>();
            view.Initialize(module, _selectionRimMaterial, _wornRimMaterial, _wornRimStyle);
            // 연결 재계산은 시뮬레이션이 이미 끝냈으므로 현재 상태를 바로 반영
            view.SetOperational(_sim.Connectivity.IsActive(module));
            // 불러온 판: 이미 파손·노후된 모듈은 생성 즉시 표시 (평소 새 모듈은 둘 다 해당 없음)
            if (_sim.Damage.TryGetInfo(module, out var damage))
                view.SetDamageVisual(damage.IsRepairing ? ModuleDamageVisual.Repairing : ModuleDamageVisual.Damaged);
            if (_sim.Durability.TryGetInfo(module, out var durability))
                view.SetWorn(_sim.Durability.EfficiencyFor(durability.Current) < 1f);
            _views.Add(module, view);
        }

        private void HandleModuleRemoved(ModuleInstance module)
        {
            if (_views.TryGetValue(module, out var view))
            {
                _views.Remove(module);
                Destroy(view.gameObject);
            }
        }

        private void HandleActiveStateChanged(ModuleInstance module, bool active)
        {
            if (_views.TryGetValue(module, out var view))
                view.SetOperational(active);
        }

        private void HandleDamaged(DamageInfo info)
        {
            if (_damageHold.ContainsKey(info.Module))
                return; // 운석 도착 순간에 ReleaseDamageVisual로 적용
            SetDamageVisual(info.Module, ModuleDamageVisual.Damaged);
        }
        private void HandleRepairStarted(DamageInfo info)
        {
            _damageHold.Remove(info.Module);
            SetDamageVisual(info.Module, ModuleDamageVisual.Repairing);
        }
        private void HandleRepaired(ModuleInstance module)
        {
            _damageHold.Remove(module);
            SetDamageVisual(module, ModuleDamageVisual.None);
        }

        private void SetDamageVisual(ModuleInstance module, ModuleDamageVisual visual)
        {
            if (_views.TryGetValue(module, out var view))
                view.SetDamageVisual(visual);
        }
    }
}
