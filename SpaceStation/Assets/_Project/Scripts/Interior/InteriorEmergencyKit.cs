using System;
using System.Collections.Generic;
using SpaceStation.Audio;
using SpaceStation.Building;
using SpaceStation.Core;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// CONNECT_1121 3번 ④ V4 (사용자 결정 2026-11-28 — "들면 다음 일 하나 빨리"): 구조 프레임 통로(완충 — 사이에 끼우면 양쪽 관계가 끊김)의 비상 공구함.
    /// 프레임 방을 지을 때 바닥 벽가에 하나 (요청 공구함 모델을 빨간 띠로). F로 들면, 다음 사고 하나 또는 현장 수리 지점 하나의 F 시간이 절반
    /// (<see cref="InteriorInteractable.Work"/>, 쓰면 빈 손). 쓴 공구함은 빈 채로 두고, 내부에 다시 들어가면 채워짐. 나가면 든 것은 내려놓음.
    /// </summary>
    public sealed class InteriorEmergencyKit : MonoBehaviour
    {
        /// <summary>비상 공구함을 들었을 때 일의 F 시간 배율.</summary>
        public const float HoldScale = 0.5f;
        private const string BufferTag = "AT_Buffer";
        private static readonly Color KitBand = new Color(0.92f, 0.3f, 0.22f);

        /// <summary>비상 공구함을 들고 있음 (다음 일 하나를 빨리).</summary>
        public static bool Carrying { get; private set; }
        private static readonly Dictionary<ModuleInstance, GameObject> Boxes = new Dictionary<ModuleInstance, GameObject>();

        private InteriorBuilder _builder;
        private StationController _station;
        private GameObject _prefab;
        private InteriorLayout _layout;
        private readonly System.Random _rng = new System.Random();
        private readonly List<Vector3> _used = new List<Vector3>();
        private MaterialPropertyBlock _block;

        /// <summary>알림 (글, 실패 여부).</summary>
        public event Action<string, bool> Message;

        public static InteriorEmergencyKit Create(Transform parent, StationController station, InteriorBuilder builder, GameObject prefab)
        {
            var go = new GameObject("InteriorEmergencyKit");
            go.transform.SetParent(parent, false);
            var k = go.AddComponent<InteriorEmergencyKit>();
            k._station = station;
            k._builder = builder;
            k._prefab = prefab;
            builder.RoomBuilt += k.HandleRoomBuilt;
            return k;
        }

        private void OnDestroy()
        {
            if (_builder != null)
                _builder.RoomBuilt -= HandleRoomBuilt;
        }

        /// <summary>내부를 (다시) 만든 직후 — 상자는 방 오브젝트 아래라 이미 지워짐. 들고 있던 것은 내려놓음 (나갔다 오면 다시 채워짐).</summary>
        public void Rebuild(InteriorLayout layout)
        {
            _layout = layout;
            Boxes.Clear();
            _used.Clear();
            Carrying = false;
        }

        /// <summary>구조 프레임(완충) 모듈인지.</summary>
        public static bool IsFrame(ModuleInstance m)
        {
            if (m?.Data == null)
                return false;
            foreach (var t in m.Data.AdjacencyTags)
            {
                if (t != null && t.name == BufferTag)
                    return true;
            }
            return false;
        }

        /// <summary>그 방이나 맞닿은 방에 아직 안 쓴 비상 공구함이 있는지 (사고 알림 한 줄).</summary>
        public static bool AvailableNear(StationGrid grid, ModuleInstance room, List<ModuleInstance> scratch)
        {
            if (Carrying || room == null || grid == null)
                return false;
            if (Boxes.TryGetValue(room, out var box) && box != null && box.GetComponent<InteriorInteractable>() != null)
                return true;
            grid.GetNeighborModules(room, scratch);
            foreach (var n in scratch)
            {
                if (Boxes.TryGetValue(n, out var b) && b != null && b.GetComponent<InteriorInteractable>() != null)
                    return true;
            }
            return false;
        }

        /// <summary>일 하나를 끝냈을 때 (InteriorMode) — 들고 있었으면 씀.</summary>
        public static bool Use()
        {
            if (!Carrying)
                return false;
            Carrying = false;
            return true;
        }

        private void HandleRoomBuilt(ModuleInstance module)
        {
            if (_layout == null || _prefab == null || Boxes.ContainsKey(module) || !IsFrame(module))
                return;
            var adjacency = _station != null && _station.Simulation != null ? _station.Simulation.Adjacency : null;
            if (adjacency == null || !adjacency.HasInnerFaces)
                return; // 안쪽 얼굴이 있는 관계 표(엔드리스)에서만 — 캠페인은 통합 밸런싱 때 (CONNECT A4)
            var parent = _builder.RoomParent(module);
            if (parent == null)
                return;
            if (!InteriorSpots.Floor(_builder, module, _rng, new Vector3(0.28f, 0.18f, 0.28f), _used, 0.9f, out var spot, 0.8f, 2.4f))
                return;
            _used.Add(spot);
            var go = Instantiate(_prefab, parent);
            go.name = "EmergencyKit";
            go.transform.SetPositionAndRotation(spot, Quaternion.Euler(0f, (float)_rng.NextDouble() * 360f, 0f));
            Tint(go);
            var col = go.AddComponent<BoxCollider>();
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                var b = renderers[0].bounds;
                foreach (var r in renderers)
                    b.Encapsulate(r.bounds);
                col.center = go.transform.InverseTransformPoint(b.center);
                col.size = Vector3.Max(b.size, new Vector3(0.25f, 0.15f, 0.25f));
            }
            var use = go.AddComponent<InteriorInteractable>();
            use.HoldSeconds = 0.4f;
            use.Prompt = () => Carrying ? "비상 공구함  <color=#AFC4D8>이미 들고 있어요</color>" : "비상 공구함 들기  <color=#AFC4D8>다음 사고 · 수리 하나 빨리</color>";
            use.Blocked = () => Carrying ? "이미 들고 있어요" : null;
            use.Completed = () =>
            {
                if (Carrying)
                    return;
                Carrying = true;
                Destroy(use);
                Destroy(col);
                AudioService.TryPlayAt(l => l.ExtinguisherPickup, go.transform.position);
                go.transform.localScale *= 0.001f; // 든 상자는 감춤 (방을 다시 지으면 새로)
                Message?.Invoke("비상 공구함을 들었어요 · 다음 사고나 현장 수리 하나를 절반 시간에", false);
            };
            Boxes[module] = go;
        }

        private void Tint(GameObject go)
        {
            if (_block == null)
                _block = new MaterialPropertyBlock();
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                for (int i = 0; i < r.sharedMaterials.Length; i++)
                {
                    _block.Clear();
                    _block.SetColor("_BaseColor", KitBand);
                    r.SetPropertyBlock(_block, i);
                }
            }
        }
    }
}
