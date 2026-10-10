using System;
using System.Collections.Generic;
using SpaceStation.Audio;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using SpaceStation.UI;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 11-17 ② 보급품 줍기: 시뮬레이션의 보급 상자(<see cref="SupplyCrateSystem"/>)를 내부 방 바닥에 놓고, F 길게(짧게)로 줍는다.
    /// 자원 상자 = 띠 · 라벨이 자원 색, 특별 상자 = 주황 하드 케이스 + 보상 색 봉인과 은은한 불빛.
    /// 자리는 상자 번호 · 모듈 원점 씨앗으로 고른 빈 바닥(방 칸 가운데에서 1~2.6m, 바닥이 평평하고 위가 빈 곳)이라 다시 만들어도 같은 자리.
    /// </summary>
    public sealed class InteriorSupply : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("자원 상자 (BlenderWork/InteriorProps/supply_builder.py)")]
            public GameObject Crate;
            [Tooltip("특별 상자")]
            public GameObject Case;
            [Tooltip("줍는 데 누르는 시간(초)")]
            public float HoldSeconds = 0.6f;
        }

        private StationController _station;
        private InteriorBuilder _builder;
        private Settings _s;
        private InteriorLayout _layout;
        private bool _dirty;
        private readonly Dictionary<int, GameObject> _spawned = new Dictionary<int, GameObject>();
        private readonly List<Vector3> _used = new List<Vector3>();
        private readonly List<int> _gone = new List<int>();
        private MaterialPropertyBlock _block;

        /// <summary>알림 (글, 실패 여부).</summary>
        public event Action<string, bool> Message;

        private StationSimulation Sim => _station.Simulation;

        public static InteriorSupply Create(Transform parent, StationController station, InteriorBuilder builder, Settings settings)
        {
            var go = new GameObject("InteriorSupply");
            go.transform.SetParent(parent, false);
            var s = go.AddComponent<InteriorSupply>();
            s._station = station;
            s._builder = builder;
            s._s = settings ?? new Settings();
            s._block = new MaterialPropertyBlock();
            station.Simulation.Supply.Changed += s.MarkDirty;
            return s;
        }

        private void OnDestroy()
        {
            if (_station != null && _station.Simulation != null)
                Sim.Supply.Changed -= MarkDirty;
        }

        private void MarkDirty() => _dirty = true;

        /// <summary>내부를 (다시) 만든 직후 — 상자는 방 오브젝트 아래라 이미 지워졌음.</summary>
        public void Rebuild(InteriorLayout layout)
        {
            _layout = layout;
            _spawned.Clear();
            _used.Clear();
            Sync();
        }

        private void Update()
        {
            if (_dirty)
                Sync();
        }

        /// <summary>새 상자는 놓고, 주웠거나 옮겨진 상자는 지운다.</summary>
        private void Sync()
        {
            _dirty = false;
            if (_layout == null)
                return;
            _gone.Clear();
            foreach (var pair in _spawned)
            {
                var crate = Sim.Supply.Find(pair.Key);
                if (crate == null || !_layout.Contains(crate.Module) || pair.Value == null)
                    _gone.Add(pair.Key);
            }
            foreach (int id in _gone)
            {
                if (_spawned[id] != null)
                    Destroy(_spawned[id]);
                _spawned.Remove(id);
            }
            foreach (var crate in Sim.Supply.Crates)
            {
                if (!_spawned.ContainsKey(crate.Id) && _layout.Contains(crate.Module))
                    Spawn(crate);
            }
        }

        private void Spawn(SupplyCrate crate)
        {
            var prefab = crate.IsSpecial ? _s.Case : _s.Crate;
            var parent = _builder.RoomParent(crate.Module);
            if (prefab == null || parent == null)
                return;
            int seed = crate.Module.Origin.x * 73856093 ^ crate.Module.Origin.z * 83492791 ^ (crate.Slot + 1) * 19349663;
            var rng = new System.Random(seed);
            if (!FindSpot(crate.Module, rng, out var spot))
                return;
            _used.Add(spot);
            var go = Instantiate(prefab, parent);
            go.name = (crate.IsSpecial ? "SupplyCase" : "SupplyCrate") + crate.Id;
            go.transform.SetPositionAndRotation(spot, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
            Tint(go, crate);

            var mf = go.GetComponentInChildren<MeshFilter>();
            var col = go.AddComponent<BoxCollider>();
            if (mf != null && mf.sharedMesh != null)
            {
                col.center = mf.sharedMesh.bounds.center;
                col.size = mf.sharedMesh.bounds.size;
            }
            int id = crate.Id;
            var it = go.AddComponent<InteriorInteractable>();
            it.HoldSeconds = _s.HoldSeconds;
            it.Prompt = () => PromptFor(id);
            it.Completed = () => Pickup(id);
            _spawned[id] = go;
        }

        /// <summary>방 칸 가운데에서 1~2.6m 떨어진 바닥 — 평평하고(아래 광선이 같은 높이 바닥) 상자 크기만큼 비었고, 다른 상자와 0.9m 이상.</summary>
        private bool FindSpot(ModuleInstance module, System.Random rng, out Vector3 spot)
        {
            spot = default;
            var cells = module.Cells;
            for (int attempt = 0; attempt < 60; attempt++)
            {
                var cell = cells[rng.Next(cells.Count)];
                var floor = _builder.FloorPoint(cell);
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = 1f + (float)rng.NextDouble() * 1.6f;
                var p = floor + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                if (!Physics.Raycast(p + Vector3.up * 1.2f, Vector3.down, out var hit, 1.6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    continue;
                if (Mathf.Abs(hit.point.y - floor.y) > 0.06f || hit.normal.y < 0.95f)
                    continue;
                p = hit.point;
                if (Physics.CheckBox(p + Vector3.up * 0.3f, new Vector3(0.4f, 0.26f, 0.4f), Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    continue;
                bool far = true;
                foreach (var u in _used)
                {
                    if ((u - p).sqrMagnitude < 0.9f * 0.9f)
                    {
                        far = false;
                        break;
                    }
                }
                if (!far)
                    continue;
                spot = p + Vector3.up * 0.002f;
                return true;
            }
            return false;
        }

        /// <summary>자원 상자 = 띠 · 라벨(M_CrateBand)을 자원 색, 특별 상자 = 봉인(M_CaseSeal)을 보상 색 + 같은 색 은은한 불빛.</summary>
        private void Tint(GameObject go, SupplyCrate crate)
        {
            var color = crate.IsSpecial ? BonusColor(crate.Bonus) : ResourceColor(crate.Resource);
            string slot = crate.IsSpecial ? "M_CaseSeal" : "M_CrateBand";
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || !mats[i].name.StartsWith(slot))
                        continue;
                    _block.Clear();
                    _block.SetColor("_BaseColor", color);
                    r.SetPropertyBlock(_block, i);
                }
            }
            if (!crate.IsSpecial)
                return;
            var glowGo = new GameObject("Glow");
            glowGo.transform.SetParent(go.transform, false);
            glowGo.transform.localPosition = new Vector3(0f, 0.25f, 0.45f);
            var glow = glowGo.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.shadows = LightShadows.None;
            glow.color = color;
            glow.intensity = 0.25f; // 11-17 ① 피드백: 지점 불빛은 은은하게
            glow.range = 1.6f;
        }

        public static Color ResourceColor(ResourceType type)
        {
            switch (type)
            {
                case ResourceType.Food: return new Color(0.5f, 0.82f, 0.36f);
                case ResourceType.Water: return new Color(0.32f, 0.6f, 0.95f);
                case ResourceType.Oxygen: return new Color(0.55f, 0.92f, 0.95f);
                default: return new Color(0.78f, 0.8f, 0.84f); // 금속
            }
        }

        public static Color BonusColor(CrateBonus bonus)
        {
            switch (bonus)
            {
                case CrateBonus.ResearchPoints: return new Color(0.68f, 0.5f, 1f);
                case CrateBonus.FreeRepair: return new Color(0.45f, 0.95f, 0.55f);
                case CrateBonus.Satisfaction: return new Color(1f, 0.55f, 0.75f);
                default: return Color.white;
            }
        }

        public static string BonusName(CrateBonus bonus)
        {
            switch (bonus)
            {
                case CrateBonus.ResearchPoints: return "연구 포인트";
                case CrateBonus.FreeRepair: return "무료 수리권";
                case CrateBonus.Satisfaction: return "간식 · 생필품 (만족도)";
                default: return "";
            }
        }

        private string PromptFor(int id)
        {
            var crate = Sim.Supply.Find(id);
            if (crate == null)
                return "";
            if (crate.IsSpecial)
                return $"특별 보급 상자 열기  <color=#{ColorUtility.ToHtmlStringRGB(BonusColor(crate.Bonus))}>{BonusName(crate.Bonus)}</color>";
            return $"보급 상자 줍기  <color=#{ColorUtility.ToHtmlStringRGB(ResourceColor(crate.Resource))}>{crate.Resource.DisplayName()} {crate.Amount:0}</color>";
        }

        private void Pickup(int id)
        {
            if (!Sim.TryPickupCrate(id, out var text))
                return;
            AudioService.TryPlay(l => l.SupplyArrive);
            Message?.Invoke(text, false);
        }

        /// <summary>11-17 ② 패드 홀로그램 표시용: 이 방에 아직 줍지 않은 상자 수.</summary>
        public static int CountIn(StationSimulation sim, ModuleInstance module)
        {
            int n = 0;
            foreach (var c in sim.Supply.Crates)
            {
                if (c.Module == module)
                    n++;
            }
            return n;
        }
    }
}
