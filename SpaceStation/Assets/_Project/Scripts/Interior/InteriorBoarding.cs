using System.Collections.Generic;
using SpaceStation.Audio;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 12-3c 지구급 습격 '침입 드론' 내부 표시 (그레이박스, 사용자 결정 2026-10-14).
    /// 드론이 붙은 방(<see cref="ContactSystem.BoardingTarget"/>)을 지으면 남은 드론 수만큼 떠다니는 드론을 놓고,
    /// 바라보고 상호작용 키를 길게 누르면 하나씩 꺼짐(<see cref="ContactSystem.DisableDrone"/>).
    /// 드론은 둥근 몸통 + 밝게 깜빡이는 눈 (색만이 아니라 밝기 깜빡임으로 눈에 띄게). 방 오브젝트 아래라 내부를 다시 만들면 함께 지워짐.
    /// </summary>
    public sealed class InteriorBoarding : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly Color EyeColor = new Color(1f, 0.32f, 0.2f);

        private sealed class Drone
        {
            public Transform Root;
            public Renderer Eye;
            public Vector3 Home;
            public float Phase;
        }

        private StationController _station;
        private InteriorBuilder _builder;
        private Material _material;
        private float _holdSeconds;
        private readonly List<Drone> _drones = new List<Drone>();
        private readonly List<Vector3> _used = new List<Vector3>();
        private MaterialPropertyBlock _block;
        private ModuleInstance _room;
        private int _shown;

        private ContactSystem Contact => _station != null && _station.Simulation != null ? _station.Simulation.Contact : null;

        public static InteriorBoarding Create(Transform parent, StationController station, InteriorBuilder builder, Material material)
        {
            var go = new GameObject("InteriorBoarding");
            go.transform.SetParent(parent, false);
            var b = go.AddComponent<InteriorBoarding>();
            b._station = station;
            b._builder = builder;
            b._material = material;
            b._block = new MaterialPropertyBlock();
            var contact = b.Contact;
            b._holdSeconds = contact != null ? contact.Config.DroneHoldSeconds : 1.5f;
            builder.RoomBuilt += b.HandleRoomBuilt;
            return b;
        }

        private void OnDestroy()
        {
            if (_builder != null)
                _builder.RoomBuilt -= HandleRoomBuilt;
        }

        /// <summary>내부를 (다시) 만든 직후 — 드론은 방 오브젝트 아래라 이미 지워졌음.</summary>
        public void Rebuild()
        {
            _drones.Clear();
            _room = null;
            _shown = 0;
            Sync();
        }

        private void HandleRoomBuilt(ModuleInstance module)
        {
            var c = Contact;
            if (c != null && c.Boarding && module == c.BoardingTarget)
                Sync();
        }

        private void Update()
        {
            Sync();
            float t = Time.unscaledTime;
            foreach (var d in _drones)
            {
                if (d.Root == null)
                    continue;
                // 둥실둥실 + 천천히 돌기, 눈은 밝기로 깜빡임
                d.Root.position = d.Home + new Vector3(Mathf.Sin(t * 0.7f + d.Phase) * 0.25f, Mathf.Sin(t * 1.6f + d.Phase) * 0.12f, Mathf.Cos(t * 0.5f + d.Phase) * 0.25f);
                d.Root.Rotate(0f, 40f * Time.unscaledDeltaTime, 0f, Space.World);
                if (d.Eye != null)
                {
                    float pulse = 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Sin(t * 6f + d.Phase));
                    _block.Clear();
                    _block.SetColor(BaseColorId, EyeColor);
                    _block.SetColor(EmissionColorId, EyeColor * (1f + 5f * pulse));
                    d.Eye.SetPropertyBlock(_block);
                }
            }
        }

        /// <summary>남은 드론 수 · 대상 방에 맞춤 (대상 방이 지어져 있을 때만 놓음).</summary>
        private void Sync()
        {
            var c = Contact;
            var target = c != null && c.Boarding ? c.BoardingTarget : null;
            if (target != _room)
            {
                Clear();
                _room = target;
            }
            if (_room == null || !_builder.IsBuilt(_room))
                return;
            int want = c.DronesLeft;
            // 끈 드론은 Completed에서 지움 → 남은 수보다 많으면 정리, 적으면 더 놓음 (방을 처음 지었을 때)
            _drones.RemoveAll(d => d.Root == null);
            while (_drones.Count > want)
            {
                Destroy(_drones[_drones.Count - 1].Root.gameObject);
                _drones.RemoveAt(_drones.Count - 1);
            }
            if (_drones.Count < want && _shown < want)
                Spawn(want - _drones.Count);
        }

        private void Clear()
        {
            foreach (var d in _drones)
            {
                if (d.Root != null)
                    Destroy(d.Root.gameObject);
            }
            _drones.Clear();
            _used.Clear();
            _shown = 0;
        }

        private void Spawn(int count)
        {
            var parent = _builder.RoomParent(_room);
            if (parent == null)
                return;
            var rng = new System.Random(_room.Origin.x * 73856093 ^ _room.Origin.z * 83492791 ^ 0xD20);
            for (int i = 0; i < count; i++)
            {
                if (!InteriorSpots.Floor(_builder, _room, rng, new Vector3(0.3f, 0.6f, 0.3f), _used, 1.4f, out var spot, 0.8f, 2.8f))
                    spot = _builder.FloorPoint(_room.Cells[i % _room.Cells.Count]) + new Vector3(0.6f * i, 0f, 0f);
                _used.Add(spot);
                _drones.Add(Build(parent, spot + Vector3.up * 1.3f, i * 1.7f));
                _shown++;
            }
        }

        private Drone Build(Transform parent, Vector3 position, float phase)
        {
            var root = new GameObject("BoardingDrone").transform;
            root.SetParent(parent, false);
            root.position = position;
            Part(PrimitiveType.Sphere, root, Vector3.zero, new Vector3(0.46f, 0.36f, 0.46f), new Color(0.16f, 0.17f, 0.19f), Color.black);
            Part(PrimitiveType.Cylinder, root, Vector3.zero, new Vector3(0.7f, 0.02f, 0.7f), new Color(0.3f, 0.3f, 0.32f), Color.black); // 테 (회전익 자리)
            var eye = Part(PrimitiveType.Sphere, root, new Vector3(0f, 0f, 0.2f), new Vector3(0.16f, 0.16f, 0.1f), EyeColor, EyeColor * 4f);
            var col = root.gameObject.AddComponent<SphereCollider>();
            col.radius = 0.38f;
            var drone = new Drone { Root = root, Eye = eye, Home = position, Phase = phase };
            var use = root.gameObject.AddComponent<InteriorInteractable>();
            use.HoldSeconds = _holdSeconds;
            use.Prompt = () =>
            {
                var c = Contact;
                return c != null ? $"침입 드론 끄기  <color=#AFC4D8>남은 {c.DronesLeft}기 · {Mathf.CeilToInt(c.BoardingTimeLeft)}초</color>" : "침입 드론 끄기";
            };
            use.Completed = () =>
            {
                _drones.Remove(drone);
                Destroy(root.gameObject);
                AudioService.TryPlay(l => l.StormSpark);
                Contact?.DisableDrone();
            };
            return drone;
        }

        private Renderer Part(PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 scale, Color color, Color emission)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            if (_material != null)
                r.sharedMaterial = _material;
            _block.Clear();
            _block.SetColor(BaseColorId, color);
            _block.SetColor(EmissionColorId, emission);
            r.SetPropertyBlock(_block);
            return r;
        }
    }
}
