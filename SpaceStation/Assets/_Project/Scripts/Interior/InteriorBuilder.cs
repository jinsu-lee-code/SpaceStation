using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 문 승강판 · 빛 다리 (1층 옆문 — 화물 환적장 물길): 판에서 떠올라 문으로 나감. 위아래 이동은 방마다 이동 장치 하나 (<see cref="InteriorTransit"/>, 2026-11-19 — 해치 · 빛 기둥 승강판 대신).
    /// </summary>
    public sealed class InteriorHatch : MonoBehaviour
    {
        public Vector3Int FromCell { get; set; }
        public Vector3Int ToCell { get; set; }
        /// <summary>판 · 빛 기둥.</summary>
        public InteriorSkyLift SkyLift { get; set; }
        /// <summary>
        /// 문 승강판 (옆문 — 모든 면 연결 ②-4): 문 평면 가운데 문턱 (월드). 판에서 떠오르면 이 문으로 나가고, 문으로 들어오면 판까지 내려앉음.
        /// </summary>
        public Vector3? DoorPoint { get; set; }
        /// <summary>문 승강판의 문 바깥 방향 (월드).</summary>
        public Vector3 DoorDirection { get; set; }
        /// <summary>빛 다리 승강판 (문 승강판 중 판이 문턱 높이 — <see cref="InteriorGeometry.IsBridgeLift"/>).</summary>
        public bool Bridge { get; set; }
    }

    /// <summary>
    /// Phase 11 내부 생성 (11-3: 1칸 = 8m, <see cref="InteriorGeometry"/>).
    /// 모듈마다 템플릿(<see cref="InteriorTemplate"/>)이 있으면 그 프리팹 + 문 자리 채우기, 없으면 벽 키트 대체 방(+ 여러 층이면 11-2b 발코니·나선 계단).
    /// 통로(<see cref="InteriorLayout"/>의 문·해치 면)는 양쪽 방이 모두 문을 낼 수 있을 때만 열린다 (템플릿은 문 자리가 있어야 함).
    /// 수평 통로는 양쪽 벽 바깥면을 잇는 연결 튜브 + 방마다 미닫이 문(에어록), 수직 통로는 해치(F 이동).
    /// 충돌은 보이지 않는 상자(Colliders)로 따로 둔다 (템플릿 프리팹은 자기 콜라이더를 가짐).
    /// 키트 강조 슬롯 = 그 방 모듈의 강조색, 문 상태등 = 너머 방 상태(청록 / 빨강).
    /// 비활성·파손 방은 슬롯별 MaterialPropertyBlock으로 어둡게 (정상 방은 블록을 비워 SRP Batcher 유지). 키트가 없으면 큐브로 대신한다.
    /// </summary>
    public sealed class InteriorBuilder
    {
        private const float LightIntensity = 6f;
        private const float Dark = 0.35f;
        private const float RailPostSpacing = 1.6f;
        private const float CollarSink = 0.153f; // 튜브 끝 고리 반 두께(0.15) + 0.003: 고리를 벽 속으로
        private const float TubeWindowMinLength = 1.2f; // 현창 바깥 지름 1.09

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private static readonly Color CoreTint = new Color(0.62f, 0.70f, 0.80f);
        private static readonly Color FloorShade = new Color(0.55f, 0.55f, 0.57f);
        private static readonly Color DoorFrame = new Color(0.30f, 0.75f, 0.85f);
        private static readonly Color HatchColor = new Color(0.95f, 0.75f, 0.20f);
        private static readonly Color TubeColor = new Color(0.75f, 0.77f, 0.80f);
        private static readonly Color StatusOk = new Color(0.25f, 0.9f, 1f) * 3f;
        private static readonly Color StatusWarn = new Color(1f, 0.12f, 0.08f) * 4f;

        /// <summary>렌더러 하나의 원래 색 (어둡게 했다 되돌릴 때). Tinted = 그레이박스처럼 평소에도 블록으로 색을 칠함.</summary>
        private sealed class Piece
        {
            public Renderer Renderer;
            public Color[] BaseColors;
            public int StatusSlot = -1;
            public bool Tinted;
            /// <summary>11-10 천장 조명 판 재질 칸 (InteriorAtmosphere가 발광을 직접 조절 — 어둡게 하기에서 건너뜀).</summary>
            public List<int> LightSlots;
        }

        /// <summary>11-10 조명 판 재질 칸 하나 (발광 세기를 조명과 함께 조절).</summary>
        public struct LightSlot
        {
            public Renderer Renderer;
            public int Slot;
            public Color BaseColor;
            public Color Emission;
        }

        private const string LightMaterialName = "M_InteriorLight";

        private readonly Transform _root;
        private readonly InteriorKit _kit;
        private readonly Material _material;
        private readonly Dictionary<ModuleData, InteriorTemplate> _templates = new Dictionary<ModuleData, InteriorTemplate>();
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        private readonly MaterialPropertyBlock _empty = new MaterialPropertyBlock();
        private readonly Dictionary<ModuleInstance, List<Piece>> _roomPieces = new Dictionary<ModuleInstance, List<Piece>>();
        private readonly Dictionary<ModuleInstance, List<Light>> _roomLights = new Dictionary<ModuleInstance, List<Light>>();
        private readonly Dictionary<Light, float> _lightBase = new Dictionary<Light, float>();
        private readonly Dictionary<ModuleInstance, Color> _roomColors = new Dictionary<ModuleInstance, Color>();
        private readonly Dictionary<ModuleInstance, Transform> _roomParents = new Dictionary<ModuleInstance, Transform>();
        private readonly Dictionary<Transform, ModuleInstance> _parentRooms = new Dictionary<Transform, ModuleInstance>();
        /// <summary>11-11 템플릿 방: (템플릿, 놓인 프리팹) — 주민 자리를 월드로 옮길 때.</summary>
        private readonly Dictionary<ModuleInstance, (InteriorTemplate Template, Transform Instance)> _templateRooms = new Dictionary<ModuleInstance, (InteriorTemplate, Transform)>();
        /// <summary>문 상태등: 너머 방 → 그 방 상태를 보여 주는 렌더러 조각들.</summary>
        private readonly Dictionary<ModuleInstance, List<Piece>> _statusByOther = new Dictionary<ModuleInstance, List<Piece>>();
        private readonly HashSet<ModuleInstance> _dimmed = new HashSet<ModuleInstance>();
        private readonly List<InteriorDoor> _doors = new List<InteriorDoor>();
        /// <summary>열린 통로 면 (칸, 방향) — 양쪽 모두 들어 있다.</summary>
        private readonly HashSet<(Vector3Int, Vector3Int)> _open = new HashSet<(Vector3Int, Vector3Int)>();
        private readonly List<WallPanel> _wallBuffer = new List<WallPanel>();
        private readonly List<FloorRect> _floorBuffer = new List<FloorRect>();
        private Transform _colliders;
        private Transform _tubeRoot;
        private Transform _player;
        private InteriorLayout _layout;
        // 12-3c-opt 2차: 방은 처음 필요할 때 짓는다
        private readonly Dictionary<ModuleInstance, InteriorRoom> _rooms = new Dictionary<ModuleInstance, InteriorRoom>();
        private readonly HashSet<ModuleInstance> _built = new HashSet<ModuleInstance>();
        /// <summary>방마다 열린 통로 면 (칸, 방향) · 통로로 바로 이어진 방.</summary>
        private readonly Dictionary<ModuleInstance, List<(Vector3Int, Vector3Int)>> _openOf = new Dictionary<ModuleInstance, List<(Vector3Int, Vector3Int)>>();
        private readonly Dictionary<ModuleInstance, List<ModuleInstance>> _linksOf = new Dictionary<ModuleInstance, List<ModuleInstance>>();
        private readonly HashSet<(Vector3Int, Vector3Int)> _tubeKeys = new HashSet<(Vector3Int, Vector3Int)>();
        /// <summary>위아래 이동 장치 목적지 · 지은 장치 (방마다 하나, 2026-11-19).</summary>
        private InteriorTransitRoutes _routes;
        private readonly Dictionary<ModuleInstance, InteriorTransit> _transits = new Dictionary<ModuleInstance, InteriorTransit>();
        /// <summary>근처라 켜 둔 방 (마지막 ShowOnly) · 소품 자리 찾기로 잠시 켠 방.</summary>
        private readonly HashSet<ModuleInstance> _shown = new HashSet<ModuleInstance>();
        private readonly HashSet<ModuleInstance> _extraActive = new HashSet<ModuleInstance>();

        public InteriorBuilder(Transform root, InteriorKit kit, Material fallbackMaterial, IEnumerable<InteriorTemplate> templates)
        {
            _root = root;
            _kit = kit != null && kit.IsComplete ? kit : null;
            _material = fallbackMaterial;
            if (templates != null)
            {
                foreach (var t in templates)
                {
                    if (t != null && t.Module != null && t.Prefab != null)
                        _templates[t.Module] = t;
                }
            }
        }

        public bool UsesKit => _kit != null;

        /// <summary>칸 중심의 월드 위치.</summary>
        public Vector3 CellCenter(Vector3Int cell) => _root.position + InteriorGeometry.CellCenter(cell);

        /// <summary>칸 바닥 윗면 높이의 중심점.</summary>
        public Vector3 FloorPoint(Vector3Int cell) => CellCenter(cell) + Vector3.up * InteriorGeometry.FloorOffset;

        public Vector3Int WorldToCell(Vector3 world)
        {
            var local = (world - _root.position) / InteriorGeometry.CellSize;
            return new Vector3Int(Mathf.RoundToInt(local.x), Mathf.RoundToInt(local.y), Mathf.RoundToInt(local.z));
        }

        /// <summary>11-9 방 환경음 묶음 (템플릿 없는 대체 방은 넓은 공간).</summary>
        public InteriorAmbience AmbienceFor(ModuleInstance module)
        {
            return module != null && module.Data != null && _templates.TryGetValue(module.Data, out var t) ? t.Ambience : InteriorAmbience.Hall;
        }

        /// <summary>그 방 + 열린 통로(문·해치, 6방향)로 바로 이어진 방들의 모듈.</summary>
        public void CollectLinkedRooms(ModuleInstance module, HashSet<ModuleInstance> into)
        {
            into.Clear();
            if (module == null || _layout == null)
                return;
            into.Add(module);
            if (_linksOf.TryGetValue(module, out var links))
            {
                foreach (var other in links)
                    into.Add(other);
            }
        }

        /// <summary>들어갈 때 서는 곳 (템플릿 지정 또는 원점 칸 바닥 가운데).</summary>
        public Vector3 SpawnPoint(ModuleInstance module, out float yaw)
        {
            if (module.Data != null && _templates.TryGetValue(module.Data, out var t))
            {
                var rotation = GridDirections.ToQuaternion(module.Rotation);
                yaw = module.Rotation * 90f + t.SpawnYaw;
                return CellCenter(module.Origin) + rotation * t.Spawn;
            }
            yaw = module.Rotation * 90f;
            return FloorPoint(module.Origin);
        }

        public void Clear()
        {
            for (int i = _root.childCount - 1; i >= 0; i--)
                Object.Destroy(_root.GetChild(i).gameObject);
            _roomPieces.Clear();
            _roomLights.Clear();
            _lightBase.Clear();
            _roomColors.Clear();
            _roomParents.Clear();
            _parentRooms.Clear();
            _templateRooms.Clear();
            _dimmed.Clear();
            _doors.Clear();
            _skyLifts.Clear();
            _doorLifts.Clear();
            _open.Clear();
            _tubes.Clear();
            _rooms.Clear();
            _built.Clear();
            _openOf.Clear();
            _linksOf.Clear();
            _tubeKeys.Clear();
            _transits.Clear();
            _routes = null;
            _shown.Clear();
            _extraActive.Clear();
            _statusByOther.Clear();
            _layout = null;
        }

        /// <summary>
        /// 내부 틀만 만든다: 열린 통로 · 방 오브젝트(빈 것). 방 안(벽 · 템플릿 · 문 · 조명 · 통로 튜브)은 <see cref="EnsureBuilt"/>가 처음 필요할 때 짓는다.
        /// 12-3c-opt 2차 (사용자 피드백 2026-10-14): 대륙급(모듈 579)은 들어갈 때 모든 방을 지어 약 3초 걸렸음 → 가까운 방만 짓고 나머지는 다가갈 때.
        /// </summary>
        public void Build(InteriorLayout layout, Transform player)
        {
            Clear();
            _layout = layout;
            _routes = new InteriorTransitRoutes(layout);
            _player = player;
            _colliders = new GameObject("Colliders").transform;
            _colliders.SetParent(_root, false);
            _tubeRoot = new GameObject("Tubes").transform;
            _tubeRoot.SetParent(_root, false);

            foreach (var room in layout.Rooms)
            {
                var go = new GameObject(room.Module.ToString());
                go.transform.SetParent(_root, false);
                go.SetActive(false); // 근처 방만 켬 (InteriorMode → ShowOnly)
                _roomParents.Add(room.Module, go.transform);
                _parentRooms.Add(go.transform, room.Module);
                _roomPieces.Add(room.Module, new List<Piece>());
                _roomColors.Add(room.Module, RoomColor(room.Module.Data));
                _rooms.Add(room.Module, room);
                _openOf.Add(room.Module, new List<(Vector3Int, Vector3Int)>());
                _linksOf.Add(room.Module, new List<ModuleInstance>());
            }

            // 열린 통로: 양쪽 방이 모두 그 면에 문을 낼 수 있어야 (양쪽 방향 모두 기록)
            foreach (var face in layout.Faces)
            {
                if (face.Kind != InteriorFaceKind.Door && face.Kind != InteriorFaceKind.Hatch)
                    continue;
                layout.TryGetRoom(face.Cell, out var a);
                layout.TryGetRoom(face.OtherCell, out var b);
                if (!Accepts(a.Module, face.Cell, face.Direction) || !Accepts(b.Module, face.OtherCell, -face.Direction))
                    continue;
                if (_open.Add((face.Cell, face.Direction)))
                    AddOpen(a.Module, b.Module, face.Cell, face.Direction);
                if (_open.Add((face.OtherCell, -face.Direction)))
                    AddOpen(b.Module, a.Module, face.OtherCell, -face.Direction);
            }
        }

        private void AddOpen(ModuleInstance module, ModuleInstance other, Vector3Int cell, Vector3Int dir)
        {
            _openOf[module].Add((cell, dir));
            var links = _linksOf[module];
            if (other != module && !links.Contains(other))
                links.Add(other);
        }

        /// <summary>방 안을 다 지은 직후 (연출이 그 방을 채움). 방 오브젝트는 켜진 상태로 알린다.</summary>
        public event System.Action<ModuleInstance> RoomBuilt;

        public bool IsBuilt(ModuleInstance module) => module != null && _built.Contains(module);

        /// <summary>지금까지 지은 방 (연출을 다시 만들 때, 복사본).</summary>
        public List<ModuleInstance> BuiltRooms => new List<ModuleInstance>(_built);

        /// <summary>모듈의 내부 템플릿 (없으면 null — 벽 키트 대체 방). 방을 짓기 전에도 쓸 수 있다.</summary>
        public InteriorTemplate TemplateFor(ModuleInstance module)
        {
            return module != null && module.Data != null && _templates.TryGetValue(module.Data, out var t) ? t : null;
        }

        /// <summary>
        /// 방 안을 아직 안 지었으면 지금 짓는다 (+ 이 방에 닿은 통로 튜브, 맞은편이 지어져 있으면 해치 도착점).
        /// 지은 뒤 <see cref="RoomBuilt"/>로 알려 연출(주민 · 조명 · 식물 · 창 등)이 그 방을 채우게 한다. 방 오브젝트의 켜짐 상태는 그대로.
        /// </summary>
        public bool EnsureBuilt(ModuleInstance module)
        {
            if (module == null || !_rooms.TryGetValue(module, out var room))
                return false;
            if (!_built.Add(module))
                return true;
            var parent = _roomParents[module];
            bool wasActive = parent.gameObject.activeSelf;
            if (!wasActive)
                parent.gameObject.SetActive(true); // 켜진 채로 지음 (자식 찾기 · 주민 생성이 꺼진 오브젝트를 건너뛰지 않게)
            int doorStart = _doors.Count;
            if (module.Data != null && _templates.TryGetValue(module.Data, out var template))
            {
                BuildTemplateRoom(room, template, parent); // 조명은 템플릿 프리팹 것
            }
            else
            {
                BuildFallbackRoom(room, parent);
                BuildRoomLights(room, parent);
            }

            // 수평 통로마다 튜브 하나 (양쪽 방 중 먼저 지어지는 쪽에서)
            foreach (var (cell, dir) in _openOf[module])
            {
                if (dir.y != 0 || !_tubeKeys.Add(ConnectorLayout.MakeKey(cell, cell + dir)))
                    continue;
                var other = OtherRoom(cell, dir);
                var span = InteriorGeometry.Tube(cell, dir, DoorDepth(module, cell, dir), DoorDepth(other, cell + dir, -dir));
                var tube = BuildTube(span, _tubeRoot, module, other);
                tube.SetActive(_shown.Contains(module) || _shown.Contains(other));
                _tubes.Add((tube, module, other));
            }

            // 위아래 이동 장치 (위나 아래로 이어진 방만) — 옛 천장 문 자리 밑 바닥은 방이 켜진 지금 광선으로
            Physics.SyncTransforms();
            if (_routes != null && _routes.HasAny(module))
                BuildTransit(module, parent, TransitGround(module));

            for (int i = doorStart; i < _doors.Count; i++)
                _doors[i].SetPlayer(_player);
            if (_dimmed.Contains(module))
            {
                foreach (var piece in _roomPieces[module])
                    ApplyColors(piece, true);
            }
            RoomBuilt?.Invoke(module);
            if (!wasActive && !_shown.Contains(module) && !_extraActive.Contains(module))
                parent.gameObject.SetActive(false);
            return true;
        }

        /// <summary>
        /// 방을 짓고 켠다 — 광선으로 소품 자리를 고를 때 (꺼진 방의 가구 충돌체는 광선에 안 맞음, <see cref="InteriorSpots"/>).
        /// 근처가 아닌 방이면 InteriorMode가 다음 프레임에 근처 방만 다시 켠다 (<see cref="NeedsReshow"/>).
        /// </summary>
        public void EnsureActive(ModuleInstance module)
        {
            if (!EnsureBuilt(module))
                return;
            var parent = _roomParents[module];
            if (parent.gameObject.activeSelf)
                return;
            parent.gameObject.SetActive(true);
            _extraActive.Add(module);
            Physics.SyncTransforms();
        }

        /// <summary>근처가 아닌 방을 소품 자리 찾기로 켜 둠 → 근처 방만 다시 켜야 함.</summary>
        public bool NeedsReshow => _extraActive.Count > 0;

        private bool Accepts(ModuleInstance module, Vector3Int cell, Vector3Int dir)
        {
            if (dir.y != 0 || module.Data == null || !_templates.TryGetValue(module.Data, out var t))
                return true;   // 위아래 = 방마다 이동 장치 하나 (문 자리 없이 이어짐)
            return t.TryGetSocket(module.Origin, module.Rotation, cell, dir, out _);
        }

        /// <summary>칸 중심에서 그 면의 문 벽 바깥면까지 (템플릿 문 자리 깊이 / 대체 방 벽 깊이).</summary>
        private float DoorDepth(ModuleInstance module, Vector3Int cell, Vector3Int dir)
        {
            if (module.Data != null && _templates.TryGetValue(module.Data, out var t) && t.TryGetSocket(module.Origin, module.Rotation, cell, dir, out var s))
                return s.Depth;
            return InteriorGeometry.FallbackDepth;
        }

        private ModuleInstance OtherRoom(Vector3Int cell, Vector3Int dir)
        {
            return _layout.TryGetRoom(cell + dir, out var r) ? r.Module : null;
        }

        /// <summary>
        /// 비활성·파손 방은 어둡게 (색·발광 낮춤). 그 방으로 가는 문 상태등은 빨강.
        /// 방 조명·천장 조명 판의 세기는 11-10부터 <see cref="InteriorAtmosphere"/>가 상태별로 조절한다.
        /// </summary>
        public void SetRoomDim(ModuleInstance module, bool dim)
        {
            if (!_roomPieces.TryGetValue(module, out var pieces))
                return;
            // 바뀔 때만 (지을 때 조각은 그때의 상태로 칠해짐 — 들어갈 때 모든 방을 훑던 약 0.4초)
            if (dim ? !_dimmed.Add(module) : !_dimmed.Remove(module))
                return;
            foreach (var piece in pieces)
                ApplyColors(piece, dim);
            if (_statusByOther.TryGetValue(module, out var lights))
            {
                foreach (var piece in lights)
                    ApplyStatus(piece, module);
            }
        }

        // ---------------- 11-10 상태 연출용 조회 ----------------

        /// <summary>방 오브젝트 (연출 오브젝트를 여기 붙이면 내부를 다시 만들 때 함께 지워진다).</summary>
        public Transform RoomParent(ModuleInstance module) => _roomParents.TryGetValue(module, out var t) ? t : null;

        /// <summary>방 안 물체 → 그 방 모듈 (부모를 따라 올라가 방 오브젝트를 찾음). 방 밖(연결 통로 튜브 등)이면 null.</summary>
        public ModuleInstance RoomOf(Transform t)
        {
            for (; t != null && t != _root; t = t.parent)
            {
                if (_parentRooms.TryGetValue(t, out var module))
                    return module;
            }
            return null;
        }

        /// <summary>11-11 템플릿으로 만든 방이면 템플릿과 놓인 프리팹 (모듈 로컬 → 월드 변환).</summary>
        public bool TryGetTemplateRoom(ModuleInstance module, out InteriorTemplate template, out Transform instance)
        {
            var found = _templateRooms.TryGetValue(module, out var room);
            template = room.Template;
            instance = room.Instance;
            return found && instance != null;
        }

        /// <summary>방의 점광원(원래 세기)과 천장 조명 판 재질 칸.</summary>
        public void CollectRoomLights(ModuleInstance module, List<Light> lights, List<float> baseIntensity, List<LightSlot> slots)
        {
            lights.Clear();
            baseIntensity.Clear();
            slots.Clear();
            if (_roomLights.TryGetValue(module, out var roomLights))
            {
                foreach (var light in roomLights)
                {
                    if (light == null)
                        continue;
                    lights.Add(light);
                    baseIntensity.Add(_lightBase.TryGetValue(light, out float b) ? b : LightIntensity);
                }
            }
            if (!_roomPieces.TryGetValue(module, out var pieces))
                return;
            foreach (var piece in pieces)
            {
                if (piece.LightSlots == null || piece.Renderer == null)
                    continue;
                var mats = piece.Renderer.sharedMaterials;
                foreach (int slot in piece.LightSlots)
                {
                    var m = mats[slot];
                    slots.Add(new LightSlot
                    {
                        Renderer = piece.Renderer,
                        Slot = slot,
                        BaseColor = piece.BaseColors[slot],
                        Emission = m.IsKeywordEnabled("_EMISSION") ? m.GetColor(EmissionColorId) : Color.black,
                    });
                }
            }
        }

        /// <summary>방 칸들을 감싸는 상자 (월드, 바닥 윗면 ~ 맨 위층 천장 높이).</summary>
        public Bounds RoomBounds(ModuleInstance module)
        {
            var cells = module.Cells;
            var min = cells[0];
            var max = cells[0];
            foreach (var c in cells)
            {
                min = Vector3Int.Min(min, c);
                max = Vector3Int.Max(max, c);
            }
            float half = InteriorGeometry.CellSize * 0.5f;
            var lo = CellCenter(min) - new Vector3(half, 0f, half);
            var hi = CellCenter(max) + new Vector3(half, 0f, half);
            lo.y = _root.position.y + InteriorGeometry.FloorY(min);
            hi.y = _root.position.y + InteriorGeometry.FloorY(max) + InteriorGeometry.RoomHeight;
            var b = new Bounds();
            b.SetMinMax(lo, hi);
            return b;
        }

        private void ApplyColors(Piece piece, bool dim)
        {
            var r = piece.Renderer;
            var mats = r.sharedMaterials;
            for (int i = 0; i < piece.BaseColors.Length; i++)
            {
                if (i == piece.StatusSlot || (piece.LightSlots != null && piece.LightSlots.Contains(i)))
                    continue;
                // 11-8 바깥 창 칸은 InteriorExteriorView가 블록(바깥 화면 텍스처)을 씀
                if (i < mats.Length && mats[i] != null && mats[i].shader.name == InteriorExteriorView.WindowShaderName)
                    continue;
                if (!dim && !piece.Tinted)
                {
                    r.SetPropertyBlock(_empty, i);
                    continue;
                }
                _block.Clear();
                var c = piece.BaseColors[i];
                _block.SetColor(BaseColorId, dim ? new Color(c.r * Dark, c.g * Dark, c.b * Dark, c.a) : c);
                if (dim)
                    _block.SetColor(EmissionColorId, Color.black);
                r.SetPropertyBlock(_block, i);
            }
        }

        private void ApplyStatus(Piece piece, ModuleInstance other)
        {
            if (piece.Renderer == null)
                return;
            var color = other != null && _dimmed.Contains(other) ? StatusWarn : StatusOk;
            _block.Clear();
            _block.SetColor(BaseColorId, color / Mathf.Max(1f, color.maxColorComponent));
            _block.SetColor(EmissionColorId, color);
            piece.Renderer.SetPropertyBlock(_block, piece.StatusSlot);
        }

        // ---------------- 템플릿 방 ----------------

        private void BuildTemplateRoom(InteriorRoom room, InteriorTemplate template, Transform parent)
        {
            var module = room.Module;
            var rotation = GridDirections.ToQuaternion(module.Rotation);
            var instance = Object.Instantiate(template.Prefab, CellCenter(module.Origin), rotation, parent);
            instance.name = template.Prefab.name;
            _templateRooms[module] = (template, instance.transform);
            var gauge = instance.GetComponent<InteriorGauge>(); // 11-10 게이지 칸은 InteriorAtmosphere가 칠함
            foreach (var r in instance.GetComponentsInChildren<Renderer>())
            {
                if (gauge == null || !gauge.Owns(r))
                    _roomPieces[module].Add(MakePiece(r, -1));
            }
            // 템플릿 조명도 어둡게 할 수 있게 (세기 비율 유지)
            var lights = new List<Light>(instance.GetComponentsInChildren<Light>());
            _roomLights[module] = lights;
            foreach (var l in lights)
                _lightBase[l] = l.intensity;

            var lifts = new List<InteriorSkyLift>();   // 이 방 빛 다리 판 — 같은 자리 빛 다리끼리 판을 같이 씀
            var bridges = new List<(Vector3Int Cell, Vector3Int Dir, Vector3 Ground, Vector3 Door)>();
            foreach (var socket in template.Sockets)
            {
                var cell = module.Origin + GridDirections.Rotate(socket.Cell, module.Rotation);
                var dir = GridDirections.Rotate(socket.Direction, module.Rotation);
                bool open = _open.Contains((cell, dir));
                if (dir.y != 0)
                    continue;   // 위아래 = 방마다 이동 장치 하나 (BuildTransit)
                var center = CellCenter(cell) + (Vector3)dir * socket.Depth;
                center.y = _root.position.y + InteriorGeometry.FloorY(cell) + InteriorGeometry.PanelCenterAboveFloor;
                var skyPatch = CellCenter(cell) + (Vector3)dir * SkyPatchDepth;
                skyPatch.y = center.y;
                Panel(new WallPanel(center - _root.position, dir, InteriorGeometry.PanelWidth, open), module, parent, open ? OtherRoom(cell, dir) : null, socket.SealedWall, skyPatch,
                    skyOnly: (socket.DoorLift || socket.DoorSteps != 0) && !open);
                if (open && socket.DoorLift)
                {
                    var ground = CellCenter(cell) + rotation * socket.Offset;
                    var door = center + Vector3.down * InteriorGeometry.PanelCenterAboveFloor;
                    if (InteriorGeometry.IsBridgeLift(ground.y, door.y))
                        bridges.Add((cell, dir, ground, door));   // 빛 다리 = 위 승강판 판을 같이 쓸 수 있게 다 지은 뒤에
                    else
                        BuildDoorLift(module, parent, cell, dir, ground, door);
                }
                if (open && socket.DoorSteps != 0)
                    BuildDoorSteps(module, parent, dir, center + Vector3.down * InteriorGeometry.PanelCenterAboveFloor, socket.DoorSteps, (CellCenter(cell) + rotation * socket.Offset).y);
            }
            foreach (var b in bridges)
                BuildBridgeLift(module, parent, b.Cell, b.Dir, b.Ground, b.Door, lifts);
            // 하늘 껍질이 있는 방 = 개방형 (아콜로지 · 대형 토러스는 우주복 없이도 하늘이 있음)
            foreach (Transform part in instance.transform)
            {
                if (part.name.EndsWith("_Sky"))
                {
                    OpenAirBarrier(room);
                    break;
                }
            }
        }

        private static readonly Vector3Int[] HorizontalDirections = { Vector3Int.left, Vector3Int.right, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) };

        /// <summary>
        /// 개방형 방 둘레의 보이지 않는 벽 (칸 경계 전체, 문 자리만 뚫림): 난간은 허리 높이라 벤치 · 앉은 주민을 밟고 올라서면 넘어갈 수 있었음 (사용자 2026-11-05 "바깥으로 나가서 떨어질 수 있더라").
        /// 하늘 껍질 바로 뒤라 걷는 자리는 그대로이고, 안에서 쏘는 빛 (바라보기 · 상호작용 · 망원경)은 통과한다 (Ignore Raycast).
        /// </summary>
        private void OpenAirBarrier(InteriorRoom room)
        {
            const float Half = InteriorGeometry.CellSize * 0.5f;
            const float T = 0.04f;
            float doorHalf = InteriorGeometry.DoorWidth * 0.5f;
            float sideWidth = Half - doorHalf;
            float floor = InteriorGeometry.FloorY(Vector3Int.zero);
            float doorTop = floor + InteriorGeometry.DoorHeight;
            var cells = new HashSet<Vector3Int>(room.Cells);
            foreach (var cell in room.Cells)
            {
                foreach (var dir in HorizontalDirections)
                {
                    if (cells.Contains(cell + dir))
                        continue;
                    var rotation = Quaternion.LookRotation(dir, Vector3.up);
                    var center = CellCenter(cell) + (Vector3)dir * (Half - T * 0.5f);
                    var right = rotation * Vector3.right;
                    if (!_open.Contains((cell, dir)))
                    {
                        BarrierPiece(center, rotation, new Vector3(InteriorGeometry.CellSize, InteriorGeometry.CellSize, T));
                        continue;
                    }
                    BarrierPiece(center - right * (doorHalf + sideWidth * 0.5f), rotation, new Vector3(sideWidth, InteriorGeometry.CellSize, T));
                    BarrierPiece(center + right * (doorHalf + sideWidth * 0.5f), rotation, new Vector3(sideWidth, InteriorGeometry.CellSize, T));
                    BarrierPiece(center + Vector3.up * ((doorTop + Half) * 0.5f), rotation, new Vector3(InteriorGeometry.DoorWidth, Half - doorTop, T));
                    BarrierPiece(center + Vector3.up * ((floor - Half) * 0.5f), rotation, new Vector3(InteriorGeometry.DoorWidth, floor + Half, T));
                }
            }
        }

        private void BarrierPiece(Vector3 position, Quaternion rotation, Vector3 size)
        {
            var go = new GameObject("OpenAirBarrier") { layer = 2 };   // Ignore Raycast
            go.transform.SetParent(_colliders, false);
            go.transform.SetPositionAndRotation(position, rotation);
            go.AddComponent<BoxCollider>().size = size;
        }

        // ---------------- 대체 방 (벽 키트) ----------------

        private void BuildFallbackRoom(InteriorRoom room, Transform parent)
        {
            var module = room.Module;
            var doors = new HashSet<(Vector3Int, Vector3Int)>(_openOf[module]);
            InteriorGeometry.FallbackWalls(room.Cells, doors, _wallBuffer);
            foreach (var panel in _wallBuffer)
            {
                ModuleInstance other = null;
                if (panel.Door)
                {
                    var cell = WorldToCell(_root.position + panel.Center - panel.Normal * InteriorGeometry.FallbackDepth);
                    other = OtherRoom(cell, Vector3Int.RoundToInt(panel.Normal));
                }
                Panel(panel, module, parent, other);
            }
            InteriorGeometry.FallbackFloors(room.Cells, doors, _floorBuffer);
            foreach (var rect in _floorBuffer)
                FloorPanel(rect, module, parent);
            BuildBalcony(room, parent);
        }

        /// <summary>칸 가운데 → 닫힌 문 자리 하늘 조각 (하늘 껍질 3.95 바로 뒤 · 칸 경계 안). 옆 칸의 방은 4.05부터라 어느 방에서도 튀어나와 보이지 않음.</summary>
        private const float SkyPatchDepth = 3.98f;

        /// <summary>대체 방 (문 깊이가 템플릿과 다름): 패널 뒤 하늘 조각 거리 (표준 문 깊이 3.2 + 이 값 = 하늘 껍질).</summary>
        private const float SkyPatchBehindPanel = 0.7425f;

        private void Panel(WallPanel panel, ModuleInstance module, Transform parent, ModuleInstance other, bool sealedWall = false, Vector3? skyPatch = null, bool skyOnly = false)
        {
            var rotation = Quaternion.LookRotation(panel.Normal, Vector3.up);
            var center = _root.position + panel.Center;
            var right = rotation * Vector3.right;
            float t = InteriorGeometry.Thickness;
            float h = InteriorGeometry.PanelHeight;
            var solidCenter = center - panel.Normal * (t * 0.5f);
            var color = _roomColors[module];

            if (!panel.Door)
            {
                if (skyOnly)   // 닫힌 2층 높이 옆문 (문 승강판 자리) · 문 계단 자리 (이웃 없음): 공중에 난간 · 벽 조각을 두지 않고 뚫어 둔 하늘 구멍만 메움
                {
                    if (_kit != null && _kit.SkyPatchFor(module.Data) is { IsValid: true } sky)
                        KitPiece(sky, module, parent, skyPatch ?? center + panel.Normal * SkyPatchBehindPanel, rotation);
                    return;
                }
                Solid(solidCenter, rotation, new Vector3(panel.Width, h, t));
                if (_kit != null)
                    KitPiece(sealedWall || TemplateFor(module)?.SealedSockets == true ? _kit.SealedWallFor(module.Data) : _kit.WallFor(module.Data), module, parent, center, rotation, new Vector3(panel.Width / InteriorGeometry.PanelWidth, 1f, 1f)); // 진화 모듈 = 단계 조각 (InteriorKitStage), 실내형 방 = 막힌 창 패널
                if (_kit != null && _kit.SkyPatchFor(module.Data) is { IsValid: true } patch)
                    KitPiece(patch, module, parent, skyPatch ?? center + panel.Normal * SkyPatchBehindPanel, rotation);
                else
                    Cube(parent, module, solidCenter, rotation, new Vector3(panel.Width, h, t), color);
                return;
            }

            // 문 패널: 충돌 = 양옆 기둥 + 위 인방 (문턱은 바닥이 막음)
            float dw = InteriorGeometry.DoorWidth;
            float side = (panel.Width - dw) * 0.5f;
            float doorBottom = -InteriorGeometry.PanelCenterAboveFloor; // 패널 가운데 기준
            float doorTop = doorBottom + InteriorGeometry.DoorHeight;
            float lintel = h * 0.5f - doorTop;
            var up = Vector3.up;
            Solid(solidCenter - right * (dw * 0.5f + side * 0.5f), rotation, new Vector3(side, h, t));
            Solid(solidCenter + right * (dw * 0.5f + side * 0.5f), rotation, new Vector3(side, h, t));
            Solid(solidCenter + up * (doorTop + lintel * 0.5f), rotation, new Vector3(dw, lintel, t));
            if (_kit == null)
            {
                Cube(parent, module, solidCenter - right * (dw * 0.5f + side * 0.5f), rotation, new Vector3(side, h, t), color);
                Cube(parent, module, solidCenter + right * (dw * 0.5f + side * 0.5f), rotation, new Vector3(side, h, t), color);
                Cube(parent, module, solidCenter + up * (doorTop + lintel * 0.5f), rotation, new Vector3(dw, lintel, t), DoorFrame);
                return;
            }
            var wall = KitPiece(_kit.WallDoorFor(module.Data), module, parent, center, rotation);
            if (wall.StatusSlot >= 0 && other != null)
            {
                if (!_statusByOther.TryGetValue(other, out var lights))
                    _statusByOther[other] = lights = new List<Piece>();
                lights.Add(wall);
                ApplyStatus(wall, other);
            }
            // 방마다 문 한 쌍 (튜브 양 끝 = 에어록). 벽 두께 안에서 미끄러져 숨는다
            // 진화 단계별 통로 (2026-11-17): 3단계 이상 쪽 · 2 ↔ 2 = 문짝 없이 트임 (바닥이 이어짐), 3단계 이상과 붙은 낮은 쪽 · 현관의 정원 쪽 = 에어록 문짝
            var kind = other != null ? ConnectorStyle.DoorAt(StageOf(module), StageOf(other)) : DoorKind.Normal;
            if (kind == DoorKind.Open)
                return;
            BuildDoor(module, parent, center + up * (doorBottom + InteriorGeometry.DoorHeight * 0.5f) - panel.Normal * (t * 0.5f), rotation,
                kind == DoorKind.Airlock ? _kit.AirlockLeaf : null);
        }

        private void BuildDoor(ModuleInstance module, Transform parent, Vector3 position, Quaternion rotation, InteriorKitPiece leafOverride = null)
        {
            var go = new GameObject("Door");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
            var leaf = leafOverride ?? _kit.DoorLeafFor(module != null ? module.Data : null);
            var right = KitPiece(leaf, module, go.transform, position, rotation).Renderer.transform;
            var left = KitPiece(leaf, module, go.transform, position, rotation * Quaternion.Euler(0f, 180f, 0f)).Renderer.transform;
            right.name = "LeafRight";
            left.name = "LeafLeft";
            var door = go.AddComponent<InteriorDoor>();
            float leafWidth = leaf.Mesh.bounds.size.x;
            // 안쪽 끝은 문 구멍(±0.7) 밖, 바깥 끝은 문 패널 주머니(±1.6) 안에서 멈추게.
            // 처음 값(구멍 반 폭 + 0.3 = 1.0)은 바깥 끝이 1.72까지 나가 패널 옆으로 문짝 끝(강조색 줄)이 보였음 (튜브 고리에 가려 있다 고리를 벽 속에 묻으며 드러남)
            float travel = InteriorGeometry.PanelWidth * 0.5f - leafWidth - 0.04f;
            door.Initialize(left, right, leafWidth, travel,
                new Vector3(InteriorGeometry.DoorWidth, InteriorGeometry.DoorHeight, 0.1f));
            _doors.Add(door);
        }

        private void FloorPanel(FloorRect rect, ModuleInstance module, Transform parent)
        {
            var surface = _root.position + rect.Center;
            float t = InteriorGeometry.Thickness;
            var size = new Vector3(rect.Size.x, t, rect.Size.y);
            var slab = surface + Vector3.up * (rect.Ceiling ? t * 0.5f : -t * 0.5f);
            Solid(slab, Quaternion.identity, size);
            if (_kit != null)
                KitPiece(rect.Ceiling ? _kit.CeilingFor(module.Data) : _kit.FloorFor(module.Data), module, parent, surface, Quaternion.identity, new Vector3(rect.Size.x / 4f, 1f, rect.Size.y / 4f));
            else
                Cube(parent, module, slab, Quaternion.identity, size, rect.Ceiling ? _roomColors[module] : Multiply(_roomColors[module], FloorShade));
        }

        private readonly List<InteriorHatch> _skyLifts = new List<InteriorHatch>();
        private readonly List<InteriorHatch> _doorLifts = new List<InteriorHatch>();

        // ---------------- 위아래 이동 장치 ----------------

        /// <summary>위아래 목적지 (방 틀을 만든 뒤).</summary>
        public InteriorTransitRoutes Routes => _routes;

        /// <summary>그 방의 이동 장치 (지은 방만, 없으면 null).</summary>
        public InteriorTransit TransitOf(ModuleInstance module) => module != null && _transits.TryGetValue(module, out var t) ? t : null;

        /// <summary>플레이어 발이 올라선 이동 장치 발판 (켜진 방만) — 없으면 null.</summary>
        public InteriorTransit TransitUnder(Vector3 feet)
        {
            foreach (var t in _transits.Values)
            {
                if (t != null && t.isActiveAndEnabled && t.Contains(feet))
                    return t;
            }
            return null;
        }

        /// <summary>시선이 닿은 이동 장치 (켜진 방만, reach 안에서 가장 가까운 것) — 없으면 null.</summary>
        public InteriorTransit TransitLookedAt(Vector3 origin, Vector3 dir, float reach)
        {
            InteriorTransit best = null;
            float bestDistance = reach;
            foreach (var t in _transits.Values)
            {
                if (t == null || !t.isActiveAndEnabled)
                    continue;
                float d = t.LookDistance(origin, dir, bestDistance);
                if (d >= 0f && d <= bestDistance)
                {
                    best = t;
                    bestDistance = d;
                }
            }
            return best;
        }

        /// <summary>
        /// 장치 자리 (발판 밑 땅, 월드): 템플릿이 정한 자리 (도구가 고름) → 없으면 옛 위아래 문 자리 중 이웃이 있는 쪽 (빛 기둥 판 땅 · 바닥 문 면 · 천장 문 밑 바닥)
        /// → 들어오는 자리. 템플릿 없는 방 = 위아래 이웃이 있는 칸 바닥 가운데.
        /// </summary>
        private Vector3 TransitGround(ModuleInstance module)
        {
            if (module.Data != null && _templates.TryGetValue(module.Data, out var t))
            {
                var rotation = GridDirections.ToQuaternion(module.Rotation);
                if (t.HasTransitSpot)
                    return CellCenter(module.Origin) + rotation * t.TransitSpot;
                Vector3? down = null, up = null;
                foreach (var socket in t.Sockets)
                {
                    var cell = module.Origin + GridDirections.Rotate(socket.Cell, module.Rotation);
                    var dir = GridDirections.Rotate(socket.Direction, module.Rotation);
                    if (dir.y == 0 || !_layout.TryGetRoom(cell + dir, out var other) || other.Module == module)
                        continue;
                    if (socket.SkyLift)
                        return CellCenter(cell) + rotation * socket.Offset;
                    var surface = CellCenter(cell) + (Vector3)dir * socket.Depth + rotation * socket.Offset;
                    if (dir.y < 0)
                        down ??= surface;
                    else if (up == null && Physics.Raycast(surface + Vector3.down * 0.3f, Vector3.down, out var hit, 7f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && hit.normal.y > 0.7f)
                        up = hit.point;
                }
                if (down.HasValue)
                    return down.Value;
                if (up.HasValue)
                    return up.Value;
                return SpawnPoint(module, out _);
            }
            foreach (var cell in module.Cells)
            {
                foreach (var dir in new[] { Vector3Int.up, Vector3Int.down })
                {
                    if (_layout.TryGetRoom(cell + dir, out var other) && other.Module != module)
                        return FloorPoint(cell);
                }
            }
            return FloorPoint(module.Origin);
        }

        /// <summary>단계별 장치의 빛 기둥 꼭대기 (땅에서, 0 = 기둥 없음): 기본 화물 칸 없음 · 1 후광 고리 밑 · 2 혼천의 밑 · 3 수정 밑 · 4 초승달 고리 밑.</summary>
        private static readonly float[] TransitBeamTop = { 0f, 2.85f, 2.45f, 2.35f, 2.8f };

        /// <summary>
        /// 위아래 이동 장치 (2026-11-19): 단계 장치 조각 (기본 화물 승강기 칸 · 1 반중력 원판 · 2 혼천의 · 3 수정 꽃 · 4 성운, 앞 = 들어오는 자리 쪽) + 빛 기둥 (1 ~ 4단계, 방 강조색)
        /// + 발판 충돌 상자 (밟고 서는 면 + 바라보기 대상) · 기본 칸은 옆 · 뒤 벽 충돌. 조각이 없으면 승강판 판 · 고리 · 빛 기둥.
        /// </summary>
        private void BuildTransit(ModuleInstance module, Transform parent, Vector3 ground)
        {
            var tpl = TemplateFor(module);
            if (tpl != null && tpl.TransitHatch)
            {
                BuildTransitHatch(module, parent, tpl, ground);
                return;
            }
            bool chosen = tpl != null && tpl.HasTransitSpot;
            bool wallMounted = chosen && tpl.TransitWall;   // 길을 막는 방 = 벽 장치 (사용자 2026-11-19 "길을 막는다면 벽면에")
            var piece = _kit == null ? null : wallMounted ? _kit.TransitWallFor(module.Data) : _kit.TransitFor(module.Data);
            InteriorSkyLift lift;
            Vector3 stand = ground;
            if (piece == null)
            {
                float top = ground.y + InteriorTransit.Height;
                lift = LiftPad(module, parent, ground, top, top);
            }
            else
            {
                int stage = _kit.TransitStage(module.Data);
                // 앞 (로컬 −z, 안내 기둥 쪽 · 벽 장치는 방 안) = 도구가 고른 쪽 (템플릿) · 없으면 들어오는 자리 쪽, 90° 단위
                var moduleRotation = GridDirections.ToQuaternion(module.Rotation);
                var rotation = chosen ? moduleRotation * Quaternion.Euler(0f, tpl.TransitYaw, 0f)
                    : InteriorTransit.Facing(SpawnPoint(module, out _) - ground, moduleRotation);
                float s = chosen ? tpl.TransitScale : 1f;   // 붐비는 방 = 85% (충돌 상자 · 빛 기둥도 이 배율을 따름)
                var go = KitPiece(piece, module, parent, ground, rotation, Vector3.one * s).Renderer.gameObject;
                var pad = go.AddComponent<BoxCollider>();
                pad.center = new Vector3(0f, InteriorTransit.PadHeight - 0.1f, wallMounted ? -InteriorTransit.WallStand : 0f);
                pad.size = wallMounted ? new Vector3(1.3f, 0.2f, 0.9f) : new Vector3(1.3f, 0.2f, 1.3f);
                if (stage == 0 && !wallMounted)
                {
                    // 화물 칸 옆 철망 · 뒤판 (앞은 트임)
                    foreach (var (center, size) in new[] { (new Vector3(-0.9f, 1.45f, 0f), new Vector3(0.12f, 2.7f, 1.9f)), (new Vector3(0.9f, 1.45f, 0f), new Vector3(0.12f, 2.7f, 1.9f)),
                                 (new Vector3(0f, 1.45f, 0.92f), new Vector3(1.9f, 2.7f, 0.12f)) })
                    {
                        var wall = go.AddComponent<BoxCollider>();
                        wall.center = center;
                        wall.size = size;
                    }
                }
                float padTop = InteriorTransit.PadHeight * s;
                if (wallMounted)
                    stand = ground + rotation * Vector3.back * (InteriorTransit.WallStand * s);   // 서는 자리 = 벽에서 0.5 m
                lift = go.AddComponent<InteriorSkyLift>();
                float beam = wallMounted ? 0f : TransitBeamTop[Mathf.Clamp(stage, 0, TransitBeamTop.Length - 1)];   // 벽 장치 = 빛 기둥 없음
                // 빛 기둥은 오브젝트 배율로 줄어듦 → 고리 높이를 배율로 나눠 줌 (기둥 끝 = 장식 밑 beam × s)
                lift.Initialize(stand + Vector3.up * padTop, ground.y + padTop + Mathf.Max(beam * s - padTop, 0.5f) / s, beam > 0f ? _kit.SkyLiftShaft : null, ShaftColor(module));
            }
            var transit = lift.gameObject.AddComponent<InteriorTransit>();
            transit.Initialize(module, lift.PadTop, lift);
            _transits[module] = transit;
        }

        /// <summary>
        /// 해치 방 (사용자 2026-11-20 "기본 모듈 중 지금의 상하이동 모델을 쓰기 힘든 모듈은 기존 해치를 상호작용하면 층이동 창이 뜨는 방식 —
        /// 칸별로 해치 한 개이던 걸 방에 해치 하나, 천장과 바닥 두 곳에 하나씩"): 옛 아래 · 위 문 자리 (같은 칸, 원점 칸 먼저)에 키트 해치 틀 + 뚜껑.
        /// 바닥 해치 뚜껑 = 밟는 면 · 발판, 천장 해치까지가 바라보기 원기둥 — 둘 중 어느 쪽을 보거나 바닥 해치에 서서 F. 위 = 떠올라 천장 해치로, 아래 = 바닥 해치로 가라앉음.
        /// 문 자리가 하나뿐이면 다른 쪽은 그 바로 위 · 아래 면 (광선).
        /// </summary>
        private void BuildTransitHatch(ModuleInstance module, Transform parent, InteriorTemplate tpl, Vector3 fallback)
        {
            var rotation = GridDirections.ToQuaternion(module.Rotation);
            Vector3? floor = null, ceiling = null;
            foreach (bool originFirst in new[] { true, false })
            {
                foreach (var socket in tpl.Sockets)
                {
                    if (socket.Direction.y == 0 || (originFirst && socket.Cell != Vector3Int.zero))
                        continue;
                    var cell = module.Origin + GridDirections.Rotate(socket.Cell, module.Rotation);
                    var surface = CellCenter(cell) + Vector3.up * (socket.Direction.y * socket.Depth) + rotation * socket.Offset;
                    if (socket.Direction.y < 0)
                        floor ??= surface;
                    else
                        ceiling ??= surface;
                }
                if (floor.HasValue || ceiling.HasValue)
                    break;
            }
            if (!floor.HasValue && ceiling.HasValue && Physics.Raycast(ceiling.Value + Vector3.down * 0.3f, Vector3.down, out var down, 7f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                floor = down.point;
            var ground = floor ?? fallback;
            if (!ceiling.HasValue && Physics.Raycast(ground + Vector3.up * 0.3f, Vector3.up, out var up, 7f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                ceiling = up.point;
            var surfaces = ceiling.HasValue ? new[] { ground, ceiling.Value } : new[] { ground };
            var lids = new List<Renderer>();
            GameObject floorLid = null;
            for (int i = 0; i < surfaces.Length; i++)
            {
                bool isFloor = i == 0;
                var flip = isFloor ? Quaternion.identity : Quaternion.Euler(180f, 0f, 0f);
                var lidCenter = surfaces[i] + (isFloor ? Vector3.up : Vector3.down) * 0.04f;
                GameObject lid;
                if (_kit != null)
                {
                    KitPiece(_kit.HatchFrameFor(module.Data), module, parent, surfaces[i], flip);
                    lid = KitPiece(_kit.HatchLidFor(module.Data), module, parent, lidCenter, flip).Renderer.gameObject;
                }
                else
                    lid = Cube(parent, module, lidCenter, Quaternion.identity, new Vector3(InteriorGeometry.HatchSize, 0.08f, InteriorGeometry.HatchSize), HatchColor, true).Renderer.gameObject;
                if (isFloor)
                {
                    var box = lid.AddComponent<BoxCollider>();
                    box.center = Vector3.zero;
                    box.size = new Vector3(InteriorGeometry.HatchSize, 0.08f, InteriorGeometry.HatchSize);
                    floorLid = lid;
                }
                lids.Add(lid.GetComponent<Renderer>());
            }
            var padTop = ground + Vector3.up * 0.08f;
            var underCeiling = ceiling.HasValue && new Vector2(ceiling.Value.x - ground.x, ceiling.Value.z - ground.z).sqrMagnitude > 0.01f
                ? new Vector3(ceiling.Value.x, ground.y + 0.02f, ceiling.Value.z)   // 옛 설계에서 천장 해치가 다른 자리 (산소 · 연구실 · 물 재활용 · 대피소) — 같은 바닥 높이
                : padTop;
            var transit = floorLid.AddComponent<InteriorTransit>();
            transit.InitializeHatch(module, padTop, underCeiling, ceiling.HasValue ? ceiling.Value.y - padTop.y : InteriorTransit.Height, lids.ToArray());
            _transits[module] = transit;
        }

        /// <summary>승강판 판 · 고리 (ringPieceY) · 판 위 충돌 상자 · 빛 기둥 (판 ~ ringY) — 이동 장치 · 문 승강판 · 빛 다리가 같이 씀.</summary>
        private InteriorSkyLift LiftPad(ModuleInstance module, Transform parent, Vector3 ground, float ringY, float ringPieceY)
        {
            const float PadTop = 0.05f;
            var padPiece = _kit != null ? _kit.SkyLiftPadFor(module.Data) : null;
            var ringPiece = _kit != null ? _kit.SkyLiftRingFor(module.Data) : null;
            GameObject pad;
            if (padPiece != null)
                pad = KitPiece(padPiece, module, parent, ground, Quaternion.identity).Renderer.gameObject;
            else
                pad = Cube(parent, module, ground + Vector3.up * (PadTop * 0.5f), Quaternion.identity, new Vector3(1.5f, PadTop, 1.5f), HatchColor).Renderer.gameObject;
            if (ringPiece != null)
                KitPiece(ringPiece, module, parent, new Vector3(ground.x, ringPieceY, ground.z), Quaternion.identity);
            // 둥근 판 안에 드는 상자 (반 대각선 = 판 반지름) — 윗면 = 판 윗면
            var box = pad.AddComponent<BoxCollider>();
            float half = InteriorSkyLift.PadRadius * 0.7071f;
            box.size = new Vector3(half * 2f, 0.2f, half * 2f);
            box.center = new Vector3(0f, PadTop - 0.1f, 0f);
            if (padPiece == null)
            {
                box.size = new Vector3(box.size.x / 1.5f, 0.2f / PadTop, box.size.z / 1.5f);
                box.center = new Vector3(0f, 0.5f - 0.1f / PadTop, 0f);
            }
            var lift = pad.AddComponent<InteriorSkyLift>();
            lift.Initialize(ground + Vector3.up * PadTop, ringY, _kit != null ? _kit.SkyLiftShaft : null, ShaftColor(module));
            return lift;
        }

        /// <summary>
        /// 문 승강판 (2층 높이 옆문 — 문 높이에 바닥이 없는 위 칸 옆면, 모든 면 연결 ②-4): 아래 바닥 판 · 판에서 문턱까지 빛 기둥 · 문턱 높이 고리 (위 승강판과 같은 단계 조각),
        /// 떠오른 자리 ~ 문 사이 보이지 않는 받침 (문으로 들어온 사람이 내려앉기 시작하기 전에 떨어지지 않게). ground = 판 밑 땅, door = 문 평면 가운데 문턱 (월드).
        /// </summary>
        private void BuildDoorLift(ModuleInstance module, Transform parent, Vector3Int cell, Vector3Int dir, Vector3 ground, Vector3 door)
        {
            var lift = LiftPad(module, parent, ground, door.y, door.y - 0.04f);
            var pad = lift.gameObject;
            // 받침: 기둥 꼭대기 (판 반지름까지) ~ 문 평면 + 0.1, 문 폭 + 여유, 윗면 = 문턱 — 문 옆으로 비킨 판 (2026-11-19)은 기둥 꼭대기 → 문 대각선으로
            var d = (Vector3)dir;
            var top = new Vector3(ground.x, door.y, ground.z);
            var flat = door - top;
            flat.y = 0f;
            bool aside = Mathf.Abs(Vector3.Dot(flat, Vector3.Cross(Vector3.up, d))) > 0.6f;
            var axis = aside ? flat.normalized : d;
            float back = InteriorSkyLift.PadRadius, length = Vector3.Dot(flat, axis) + back + 0.1f;
            var landing = new GameObject("DoorLiftLanding") { layer = 2 };   // Ignore Raycast — 시선 · 상호작용 광선은 지나감
            landing.transform.SetParent(parent, false);
            landing.transform.SetPositionAndRotation(top + axis * (length * 0.5f - back) + Vector3.down * 0.05f, Quaternion.LookRotation(axis));
            landing.AddComponent<BoxCollider>().size = new Vector3(InteriorGeometry.DoorWidth, 0.1f, length);
            if (aside)   // 문 바로 안 (문 폭 전체 · 0.8 m) — 대각선 받침이 문 평면에서 비스듬히 끝나 문 양끝이 비지 않게
            {
                var mouth = new GameObject("DoorLiftLanding") { layer = 2 };
                mouth.transform.SetParent(parent, false);
                mouth.transform.SetPositionAndRotation(new Vector3(door.x, door.y - 0.05f, door.z) - d * 0.35f, Quaternion.LookRotation(d));
                mouth.AddComponent<BoxCollider>().size = new Vector3(InteriorGeometry.DoorWidth + 0.2f, 0.1f, 0.9f);
            }
            var h = pad.AddComponent<InteriorHatch>();
            h.FromCell = cell;
            h.ToCell = cell + dir;
            h.SkyLift = lift;
            h.DoorPoint = door;
            h.DoorDirection = d;
            _skyLifts.Add(h);
            _doorLifts.Add(h);
        }

        /// <summary>
        /// 빛 다리 승강판 (문 승강판 중 판이 문턱 높이 — 문 앞 좁은 턱 너머 물길, 화물 환적장 후룸, 모든 면 연결 2026-11-19): 판에서 문턱 + 2.5까지 떠올라 물길 위를 건넘.
        /// 같은 자리에 위 승강판 · 다른 빛 다리 판이 있으면 그 판을 같이 씀 (붐비는 갑판 — 판 위에서 바라보는 쪽으로 감, <see cref="Facing"/>),
        /// 없으면 판 · 기둥 · 문턱 + 2.5 고리를 새로. 받침 없음 (받침이 있으면 물길 위를 걸어감).
        /// </summary>
        private void BuildBridgeLift(ModuleInstance module, Transform parent, Vector3Int cell, Vector3Int dir, Vector3 ground, Vector3 door, List<InteriorSkyLift> lifts)
        {
            var lift = SharedLift(lifts, ground);
            if (lift == null)
            {
                float ringY = door.y + InteriorGeometry.BridgeLiftRise;
                lift = LiftPad(module, parent, ground, ringY, ringY - 0.04f);
                lifts.Add(lift);
            }
            var h = lift.gameObject.AddComponent<InteriorHatch>();
            h.FromCell = cell;
            h.ToCell = cell + dir;
            h.SkyLift = lift;
            h.DoorPoint = door;
            h.DoorDirection = dir;
            h.Bridge = true;
            _skyLifts.Add(h);
            _doorLifts.Add(h);
        }

        /// <summary>같은 자리 (5 cm 안 · 높이 0.3 안)에 이미 지은 승강판 판 — 없으면 null.</summary>
        private static InteriorSkyLift SharedLift(List<InteriorSkyLift> lifts, Vector3 ground)
        {
            foreach (var l in lifts)
            {
                if (new Vector2(l.PadTop.x - ground.x, l.PadTop.z - ground.z).magnitude < 0.05f && Mathf.Abs(l.PadTop.y - ground.y) < 0.3f)
                    return l;
            }
            return null;
        }

        /// <summary>
        /// 판 하나를 여럿이 같이 쓰면 (위 승강판 · 빛 다리 — 화물 환적장) 바라보는 쪽: 문 해치 = 수평 시선과 판 → 문 방향이 가까울수록, 위 승강판 = 위를 볼수록.
        /// </summary>
        public static InteriorHatch Facing(InteriorHatch hatch, Vector3 forward)
        {
            var all = hatch.GetComponents<InteriorHatch>();
            if (all.Length < 2)
                return hatch;
            var flat = new Vector3(forward.x, 0f, forward.z);
            InteriorHatch best = hatch;
            float bestScore = float.MinValue;
            foreach (var h in all)
            {
                float score = forward.y;
                if (h.DoorPoint.HasValue)
                {
                    var to = h.DoorPoint.Value - h.SkyLift.PadTop;
                    to.y = 0f;
                    score = Vector3.Dot(flat, to.normalized);
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    best = h;
                }
            }
            return best;
        }

        /// <summary>
        /// 문 계단 (모든 면 연결 ②, 2026-11-19): 문 안 바닥이 문턱보다 낮은 옆문이 열리면 문 앞에 문턱 높이 층계참 + side 쪽으로 세 단 (키트 조각, 다른 쪽은 x 뒤집음 · 높이는 세로로 늘림).
        /// 충돌 = 층계참 · 단마다 상자 (윗면 = 그 단, 땅 2 cm 밑부터). door = 문 평면 · 문턱 가운데, ground = 땅 높이.
        /// </summary>
        private void BuildDoorSteps(ModuleInstance module, Transform parent, Vector3Int dir, Vector3 door, int side, float ground)
        {
            float rise = door.y - ground;
            var rotation = Quaternion.LookRotation(dir, Vector3.up);
            var piece = _kit != null ? _kit.DoorStepsFor(module.Data) : null;
            if (piece != null)
                KitPiece(piece, module, parent, door, rotation, new Vector3(side, rise / InteriorGeometry.StepsKitRise, 1f));
            var right = rotation * Vector3.right * side;
            var inward = -(Vector3)dir;
            float step = rise / (InteriorGeometry.StepCount + 1);
            for (int k = 0; k <= InteriorGeometry.StepCount; k++)
            {
                float top = door.y - k * step;
                float x0 = k == 0 ? -InteriorGeometry.StepsHalfWidth : InteriorGeometry.StepsHalfWidth + (k - 1) * InteriorGeometry.StepRun;
                float x1 = k == 0 ? InteriorGeometry.StepsHalfWidth : InteriorGeometry.StepsHalfWidth + k * InteriorGeometry.StepRun;
                var c = door + right * ((x0 + x1) * 0.5f) + inward * (InteriorGeometry.StepsLandingDepth * 0.5f);
                c.y = (top + ground - 0.02f) * 0.5f;
                var go = new GameObject("DoorStep");
                go.transform.SetParent(parent, false);
                go.transform.SetPositionAndRotation(c, rotation);
                go.AddComponent<BoxCollider>().size = new Vector3(x1 - x0, top - ground + 0.02f, InteriorGeometry.StepsLandingDepth);
                if (piece == null)
                    Cube(parent, module, c, rotation, new Vector3(x1 - x0, top - ground + 0.02f, InteriorGeometry.StepsLandingDepth), DoorFrame);
            }
            // 난간 (층계참 방 쪽 · 끝): 문으로 들어와 곧장 걸으면 층계참 끝에서 0.78 m 떨어졌음 → 계단 쪽으로만 내려감
            float railTop = door.y + 1.05f * rise / InteriorGeometry.StepsKitRise;
            float half = InteriorGeometry.StepsHalfWidth, depth = InteriorGeometry.StepsLandingDepth;
            RailCollider(parent, door + inward * (depth - 0.05f), rotation, new Vector3(half * 2f - 0.1f, 0f, 0.08f), door.y, railTop);
            RailCollider(parent, door + right * (-half + 0.05f) + inward * (depth * 0.5f), rotation, new Vector3(0.08f, 0f, depth - 0.1f), door.y, railTop);
        }

        private static void RailCollider(Transform parent, Vector3 at, Quaternion rotation, Vector3 size, float bottom, float top)
        {
            var go = new GameObject("DoorStepRail");
            go.transform.SetParent(parent, false);
            at.y = (bottom + top) * 0.5f;
            go.transform.SetPositionAndRotation(at, rotation);
            size.y = top - bottom;
            go.AddComponent<BoxCollider>().size = size;
        }

        /// <summary>
        /// 문으로 들어와 문 승강판 떠오른 자리 (문 평면 안쪽 ~ 판 가운데 + 판 반지름, 문 폭, 문턱 높이)에 선 발 — 내려앉기 시작 (<see cref="InteriorMode"/>). 없으면 null.
        /// </summary>
        public InteriorHatch DoorLiftAt(Vector3 feet)
        {
            foreach (var h in _doorLifts)
            {
                if (h == null || !h.isActiveAndEnabled || !h.DoorPoint.HasValue)
                    continue;
                var door = h.DoorPoint.Value;
                var d = h.DoorDirection;
                var rel = feet - door;
                float inward = -Vector3.Dot(rel, d);
                float reach = h.Bridge ? 0.6f : Vector3.Dot(door - h.SkyLift.PadTop, d) + InteriorSkyLift.PadRadius;   // 빛 다리 = 문 앞 턱 (0.6 m)만 — 판은 같은 높이라 판에 선 사람이 내려앉기 시작하면 안 됨
                float lateral = Mathf.Abs(Vector3.Dot(rel, Vector3.Cross(Vector3.up, d)));
                if (inward > 0.05f && inward < reach && lateral < InteriorGeometry.DoorWidth * 0.5f + 0.1f && rel.y > -0.6f && rel.y < 0.9f)
                    return h;
            }
            return null;
        }

        /// <summary>빛 기둥 색 = 방 강조색 (강조 재질의 발광 색 — 없으면 바탕색).</summary>
        private Color ShaftColor(ModuleInstance module)
        {
            var accent = _kit != null ? _kit.AccentFor(module.Data) : null;
            if (accent != null && accent.HasProperty(EmissionColorId) && accent.GetColor(EmissionColorId).maxColorComponent > 0.05f)
            {
                var e = accent.GetColor(EmissionColorId);
                return e / Mathf.Max(1f, e.maxColorComponent) * 1.5f;
            }
            if (accent != null && accent.HasProperty(BaseColorId))
                return accent.GetColor(BaseColorId) * 1.5f;
            return new Color(1.5f, 1.3f, 0.85f);
        }

        /// <summary>플레이어 발이 올라선 승강판 (켜진 방만) — 없으면 null.</summary>
        public InteriorHatch SkyLiftUnder(Vector3 feet)
        {
            foreach (var h in _skyLifts)
            {
                if (h != null && h.isActiveAndEnabled && h.SkyLift.Contains(feet))
                    return h;
            }
            return null;
        }

        // ---------------- 연결 튜브 ----------------

        /// <summary>방의 진화 단계 (단계 키트 묶음, 기본 · 템플릿 없는 방 = 0).</summary>
        private int StageOf(ModuleInstance module) => _kit != null && module != null ? _kit.StageNumber(module.Data) : 0;

        /// <summary>
        /// 진화 단계별 통로 (사용자 2026-11-17, <see cref="ConnectorStyle"/>): 두 방 중 높은 단계 한 벌 — 1 세라믹 튜브 · 2 우주복 현관 (낮은 쪽이 1단계 이하면 보관함 · 벤치 · 거울,
        /// 2 ↔ 2는 트인 문간) · 3 · 4 다리 데크 + 하늘 껍질. 에어록 문인 끝에는 에어록 문틀. 충돌 (바닥 · 천장 · 양옆 벽)은 기본 튜브와 같음.
        /// </summary>
        private bool BuildStageTube(GameObject go, TubeSpan span, ModuleInstance a, ModuleInstance b, Vector3 start, Vector3 end, Vector3 mid, Quaternion rotation, float len)
        {
            int sA = StageOf(a), sB = StageOf(b);
            var style = ConnectorStyle.ForInterior(sA, sB);
            var set = _kit != null ? _kit.TubeStage(style.Stage) : null;
            if (set == null)
                return false;
            var accent = sA >= sB ? a : b;                  // 강조색 = 높은 단계 방
            var sink = span.Direction.normalized * CollarSink;
            var flip = rotation * Quaternion.Euler(0f, 180f, 0f);
            if (_kit.AirlockFrame != null && ConnectorStyle.DoorAt(sA, sB) == DoorKind.Airlock)
                Tube(_kit.AirlockFrame, go.transform, start + sink, rotation, Vector3.one, a);
            if (_kit.AirlockFrame != null && ConnectorStyle.DoorAt(sB, sA) == DoorKind.Airlock)
                Tube(_kit.AirlockFrame, go.transform, end - sink, flip, Vector3.one, b);
            // 트인 방 (문 깊이 3.9 ~ 3.95)끼리는 틈이 0.1 ~ 0.6 m — 끝 테 둘이 겹치므로 두 방 바닥을 잇는 문턱판만 (사용자 "짧은 문턱판으로")
            if (len < ShortTubeLength && set.TubeShort != null)
            {
                Tube(set.TubeShort, go.transform, mid, rotation, new Vector3(1f, 1f, len), accent);
                return true;
            }
            Tube(set.Tube, go.transform, mid, rotation, new Vector3(1f, 1f, len), accent);
            Tube(set.TubeCollar, go.transform, start + sink, rotation, Vector3.one, accent);
            Tube(set.TubeCollar, go.transform, end - sink, rotation, Vector3.one, accent);
            if (set.TubeRing != null && set.TubeRing.IsValid && set.TubeRingStep > 0.1f)
            {
                float clear = style.Stage == 1 ? TubeRingClear : ThinCollarClear;
                float free = len - 2f * clear;
                int n = Mathf.FloorToInt(free / set.TubeRingStep);
                for (int i = 0; i < n; i++)
                    Tube(set.TubeRing, go.transform, start + span.Direction.normalized * (TubeRingClear + (i + 0.5f) * free / n), rotation, Vector3.one, accent);
            }
            if (style.IsVestibule && set.Vestibule != null && set.Vestibule.IsValid)
            {
                // 양옆 보관함 (얇은 현관) — 2단계가 A 쪽이면 뒤집어 우주복이 늘 같은 쪽 순서로
                var turn = sA == 2 ? flip : rotation;
                if (len >= VestibuleTwinLength)
                {
                    Tube(set.Vestibule, go.transform, mid - span.Direction.normalized * (len * 0.25f), turn, Vector3.one, accent);
                    Tube(set.Vestibule, go.transform, mid + span.Direction.normalized * (len * 0.25f), turn, Vector3.one, accent);
                }
                else
                {
                    Tube(set.Vestibule, go.transform, mid, turn, Vector3.one, accent);
                }
            }
            return true;
        }

        private const float TubeRingClear = 0.3f;        // 끝 고리 (두께 0.3) 안쪽부터 간격 조각
        private const float ThinCollarClear = 0.12f;     // 2단계 이상 = 벽에 붙은 얇은 끝 테 (0.1)
        private const float ShortTubeLength = 0.7f;      // 이보다 짧은 틈 = 문턱판만
        private const float VestibuleTwinLength = 1.6f;  // 이보다 긴 현관 (2단계 문 깊이 2.9 방) = 보관함 두 벌

        private GameObject BuildTube(TubeSpan span, Transform parent, ModuleInstance a = null, ModuleInstance b = null)
        {
            var start = _root.position + span.Start;
            var end = _root.position + span.End;
            var mid = (start + end) * 0.5f;
            var rotation = Quaternion.LookRotation(span.Direction, Vector3.up);
            var right = rotation * Vector3.right;
            var up = Vector3.up;
            float len = span.Length;
            float hw = InteriorGeometry.TubeHalfWidth;
            float height = InteriorGeometry.TubeHeight;
            float t = InteriorGeometry.Thickness;

            Solid(mid - up * (t * 0.5f), rotation, new Vector3(hw * 2f + 0.4f, t, len));
            Solid(mid + up * (height + t * 0.5f), rotation, new Vector3(hw * 2f + 0.4f, t, len));
            Solid(mid - right * (hw + t * 0.5f) + up * (height * 0.5f), rotation, new Vector3(t, height, len));
            Solid(mid + right * (hw + t * 0.5f) + up * (height * 0.5f), rotation, new Vector3(t, height, len));

            var go = new GameObject("Tube");
            go.transform.SetParent(parent, false);
            if (BuildStageTube(go, span, a, b, start, end, mid, rotation, len))
            {
            }
            else if (_kit != null && _kit.HasTube)
            {
                Tube(_kit.Tube, go.transform, mid, rotation, new Vector3(1f, 1f, len));
                // 고리(두께 ±0.15, 바깥 반지름 1.87)를 벽면 중심에 두면 절반이 방 안으로 나와 문 패널(폭 ±1.6) 옆에 보였음 →
                // 통로 쪽으로 반 두께 + 0.003 밀어 벽 속에 묻음 (방 쪽 면이 벽면과 같은 평면이 되지 않게)
                var sink = span.Direction.normalized * CollarSink;
                Tube(_kit.TubeCollar, go.transform, start + sink, rotation, Vector3.one);
                Tube(_kit.TubeCollar, go.transform, end - sink, rotation, Vector3.one);
                // 11-8 현창 한 벌 (위쪽 45도 면 양쪽, 지름 약 1.1) — 가장 짧은 튜브(1.6)에도 들어감. 고리는 튜브 벽 바깥(아포템 1.55~)이라 닿지 않음
                if (_kit.TubeWindow != null && len >= TubeWindowMinLength)
                    Tube(_kit.TubeWindow, go.transform, mid, rotation, Vector3.one);
            }
            else
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(cube.GetComponent<Collider>());
                cube.transform.SetParent(go.transform, false);
                cube.transform.SetPositionAndRotation(mid - up * (t * 0.5f), rotation);
                cube.transform.localScale = new Vector3(hw * 2f, t, len);
                var r = cube.GetComponent<Renderer>();
                r.sharedMaterial = _material;
                _block.Clear();
                _block.SetColor(BaseColorId, TubeColor);
                r.SetPropertyBlock(_block);
            }
            var lightGo = new GameObject("Light");
            lightGo.transform.SetParent(go.transform, false);
            lightGo.transform.position = mid + up * (height - 0.4f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.shadows = LightShadows.None;
            light.color = new Color(0.85f, 0.95f, 1f);
            light.range = Mathf.Max(3f, len + 2f);
            light.intensity = 2f;
            return go;
        }

        /// <summary>
        /// 12-3c-opt 근접 활성화: rooms에 든 방(과 그 방에 닿은 통로 튜브)만 켜고 나머지는 끔.
        /// 방 오브젝트 아래의 조명 · 문 · 주민 · 연출도 같이 꺼진다.
        /// </summary>
        public void ShowOnly(HashSet<ModuleInstance> rooms)
        {
            _shown.Clear();
            _shown.UnionWith(rooms);
            foreach (var room in rooms)
                EnsureBuilt(room); // 처음 다가간 방은 여기서 지음 (방 하나 약 1~3ms)
            _extraActive.Clear();
            foreach (var pair in _roomParents)
            {
                if (pair.Value == null)
                    continue;
                bool on = rooms.Contains(pair.Key);
                if (pair.Value.gameObject.activeSelf != on)
                    pair.Value.gameObject.SetActive(on);
            }
            foreach (var (go, a, b) in _tubes)
            {
                if (go == null)
                    continue;
                bool on = rooms.Contains(a) || rooms.Contains(b);
                if (go.activeSelf != on)
                    go.SetActive(on);
            }
        }

        private readonly List<(GameObject Go, ModuleInstance A, ModuleInstance B)> _tubes = new List<(GameObject, ModuleInstance, ModuleInstance)>();

        /// <summary>튜브 조각 (방 소속 아님: 어둡게 하지 않음, 강조색 = accent 모듈 · 없으면 기본색).</summary>
        private void Tube(InteriorKitPiece kitPiece, Transform parent, Vector3 position, Quaternion rotation, Vector3 scale, ModuleInstance accent = null)
        {
            var go = new GameObject(kitPiece.Mesh.name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = kitPiece.Mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.sharedMaterials = MaterialsFor(kitPiece, accent);
        }

        // ---------------- 11-2b 발코니·나선 계단 (대체 방이 여러 층일 때) ----------------

        private void BuildBalcony(InteriorRoom room, Transform parent)
        {
            var plan = InteriorBalcony.Build(room.Cells, InteriorGeometry.CellSize, InteriorGeometry.FallbackDepth, InteriorGeometry.FloorOffset);
            if (plan.IsEmpty)
                return;
            var origin = _root.position;
            var module = room.Module;
            bool art = _kit != null && _kit.HasBalcony;
            var color = _roomColors[module];

            foreach (var deck in plan.Decks)
                Deck(deck, origin, module, parent, art, color);
            foreach (var rail in plan.Rails)
                Rail(rail, origin, module, parent, art, color);
            if (!plan.HasStair)
                return;

            Deck(plan.Bridge, origin, module, parent, art, color);
            var stair = new GameObject("SpiralStair").transform;
            stair.SetParent(parent, false);
            var basePoint = origin + plan.StairBase;
            float lastAngle = Mathf.Atan2(plan.BridgeDirection.z, plan.BridgeDirection.x) * Mathf.Rad2Deg;
            int steps = plan.StairSteps;
            float stepRise = plan.StairRise / steps;
            for (int i = 0; i < steps; i++)
            {
                float angle = lastAngle - (steps - 1 - i) * InteriorBalcony.StepAngle;
                var rotation = Quaternion.Euler(0f, -angle, 0f); // +X → (cos, 0, sin)
                var top = basePoint + Vector3.up * (stepRise * (i + 1));
                var dir = rotation * Vector3.right;
                Solid(top + dir * 0.8f - Vector3.up * 0.04f, rotation, new Vector3(1.25f, 0.08f, 0.36f));
                if (art)
                    KitPiece(_kit.StairStep, module, stair, top, rotation);
                else
                    Cube(stair, module, top + dir * 0.8f - Vector3.up * 0.04f, rotation, new Vector3(1.25f, 0.08f, 0.36f), color);
            }
            float poleHeight = plan.StairRise + 1.1f;
            Solid(basePoint + Vector3.up * (poleHeight * 0.5f), Quaternion.identity, new Vector3(0.34f, poleHeight, 0.34f));
            if (art)
                KitPiece(_kit.StairPole, module, stair, basePoint, Quaternion.identity, new Vector3(1f, poleHeight, 1f));
            else
                Cube(stair, module, basePoint + Vector3.up * (poleHeight * 0.5f), Quaternion.identity, new Vector3(0.34f, poleHeight, 0.34f), DoorFrame);
        }

        private void Deck(BalconyDeck deck, Vector3 origin, ModuleInstance module, Transform parent, bool art, Color color)
        {
            // 키트 띠는 로컬 Z = 길이, +X = 트인 쪽
            var rotation = Quaternion.LookRotation(deck.Along, Vector3.up);
            if (Vector3.Dot(rotation * Vector3.right, deck.Inward) < 0f)
                rotation = Quaternion.LookRotation(-deck.Along, Vector3.up);
            var top = origin + deck.Center;
            float t = InteriorGeometry.Thickness;
            var size = new Vector3(InteriorBalcony.DeckWidth, t, deck.Length);
            Solid(top - Vector3.up * (t * 0.5f), rotation, size);
            if (art)
                KitPiece(_kit.Deck, module, parent, top, rotation, new Vector3(1f, 1f, deck.Length));
            else
                Cube(parent, module, top - Vector3.up * (t * 0.5f), rotation, size, Multiply(color, FloorShade));
        }

        private void Rail(BalconyRail rail, Vector3 origin, ModuleInstance module, Transform parent, bool art, Color color)
        {
            var start = origin + rail.Start;
            var end = origin + rail.End;
            var dir = (end - start).normalized;
            float length = rail.Length;
            var rotation = Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up), Vector3.up); // 로컬 X = dir
            var mid = (start + end) * 0.5f;
            Solid(mid + Vector3.up * 0.55f, rotation, new Vector3(length, 1.1f, 0.1f));
            if (!art)
            {
                Cube(parent, module, mid + Vector3.up * 1.0f, rotation, new Vector3(length, 0.06f, 0.06f), DoorFrame);
                return;
            }
            KitPiece(_kit.RailBar, module, parent, mid, rotation, new Vector3(length, 1f, 1f));
            int posts = Mathf.Max(1, Mathf.CeilToInt(length / RailPostSpacing));
            for (int i = 0; i <= posts; i++)
                KitPiece(_kit.RailPost, module, parent, Vector3.Lerp(start, end, i / (float)posts), rotation);
        }

        // ---------------- 조각 ----------------

        /// <summary>보이지 않는 충돌 상자.</summary>
        private void Solid(Vector3 position, Quaternion rotation, Vector3 size)
        {
            var go = new GameObject("Solid");
            go.transform.SetParent(_colliders, false);
            go.transform.SetPositionAndRotation(position, rotation);
            go.AddComponent<BoxCollider>().size = size;
        }

        private Piece KitPiece(InteriorKitPiece kitPiece, ModuleInstance module, Transform parent, Vector3 position, Quaternion rotation, Vector3? scale = null)
        {
            var go = new GameObject(kitPiece.Mesh.name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
            if (scale.HasValue)
                go.transform.localScale = scale.Value;
            go.AddComponent<MeshFilter>().sharedMesh = kitPiece.Mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.sharedMaterials = MaterialsFor(kitPiece, module);
            var piece = MakePiece(r, kitPiece.StatusSlot);
            _roomPieces[module].Add(piece);
            return piece;
        }

        private Material[] MaterialsFor(InteriorKitPiece kitPiece, ModuleInstance module)
        {
            var source = kitPiece.Materials;
            var result = new Material[source.Count];
            for (int i = 0; i < source.Count; i++)
                result[i] = source[i];
            if (kitPiece.AccentSlot >= 0)
            {
                var accent = _kit.AccentFor(module != null ? module.Data : null);
                if (accent != null)
                    result[kitPiece.AccentSlot] = accent;
            }
            return result;
        }

        private static Piece MakePiece(Renderer r, int statusSlot)
        {
            var mats = r.sharedMaterials;
            var piece = new Piece { Renderer = r, StatusSlot = statusSlot, BaseColors = BaseColors(mats) };
            for (int i = 0; i < mats.Length; i++)
            {
                if (i == statusSlot || mats[i] == null || !mats[i].name.StartsWith(LightMaterialName))
                    continue;
                piece.LightSlots ??= new List<int>();
                piece.LightSlots.Add(i);
            }
            return piece;
        }

        private static Color[] BaseColors(Material[] materials)
        {
            var colors = new Color[materials.Length];
            for (int i = 0; i < materials.Length; i++)
                colors[i] = materials[i] != null && materials[i].HasProperty(BaseColorId) ? materials[i].GetColor(BaseColorId) : Color.white;
            return colors;
        }

        /// <summary>키트가 없을 때의 그레이박스 큐브 (콜라이더 없음, 해치 뚜껑만 있음).</summary>
        private Piece Cube(Transform parent, ModuleInstance module, Vector3 position, Quaternion rotation, Vector3 size, Color color, bool collider = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = size;
            if (!collider)
                Object.Destroy(go.GetComponent<Collider>());
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = _material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var piece = new Piece { Renderer = r, BaseColors = new[] { color }, Tinted = true };
            ApplyColors(piece, false);
            _roomPieces[module].Add(piece);
            return piece;
        }

        /// <summary>층마다 점광원 하나 (그림자 없음): 바닥 넓이 가운데, 천장 조금 아래.</summary>
        private void BuildRoomLights(InteriorRoom room, Transform parent)
        {
            var min = room.Cells[0];
            var max = room.Cells[0];
            foreach (var c in room.Cells)
            {
                min = Vector3Int.Min(min, c);
                max = Vector3Int.Max(max, c);
            }
            var extent = new Vector3(max.x - min.x + 1, 0f, max.z - min.z + 1) * InteriorGeometry.CellSize;
            var lights = new List<Light>();
            _roomLights.Add(room.Module, lights);
            for (int y = min.y; y <= max.y; y++)
            {
                var center = (CellCenter(min) + CellCenter(max)) * 0.5f;
                center.y = _root.position.y + InteriorGeometry.FloorY(new Vector3Int(0, y, 0)) + InteriorGeometry.RoomHeight - 0.5f;
                var go = new GameObject("Light");
                go.transform.SetParent(parent, false);
                go.transform.position = center;
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.shadows = LightShadows.None;
                light.color = new Color(1f, 0.9f, 0.78f); // 다듬기 B: 방은 따뜻하게 (튜브는 차갑게)
                light.range = extent.magnitude * 0.75f + 2f;
                light.intensity = LightIntensity;
                lights.Add(light);
                _lightBase[light] = LightIntensity;
            }
        }

        private static Color RoomColor(ModuleData data)
        {
            if (data == null)
                return CoreTint;
            switch (data.Category)
            {
                case ModuleCategory.Power: return new Color(0.82f, 0.80f, 0.62f);
                case ModuleCategory.Life: return new Color(0.68f, 0.80f, 0.70f);
                case ModuleCategory.Industry: return new Color(0.80f, 0.70f, 0.60f);
                case ModuleCategory.Defense: return new Color(0.80f, 0.64f, 0.64f);
                default: return CoreTint;
            }
        }

        private static Color Multiply(Color a, Color b) => new Color(a.r * b.r, a.g * b.g, a.b * b.b, 1f);
    }
}
