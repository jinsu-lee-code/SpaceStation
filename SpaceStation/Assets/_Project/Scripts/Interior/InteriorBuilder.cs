using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>해치 뚜껑: 바라보고 상호작용 키를 누르면 너머 칸으로 이동한다.</summary>
    public sealed class InteriorHatch : MonoBehaviour
    {
        public Vector3Int FromCell { get; set; }
        public Vector3Int ToCell { get; set; }
        public bool Up => ToCell.y > FromCell.y;
        /// <summary>너머 방의 맞은편 해치 아래 바닥점 (월드). 없으면 너머 칸 바닥 가운데.</summary>
        public Vector3? Arrival { get; set; }
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
        }

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
        /// <summary>문 상태등: (렌더러 조각, 너머 방).</summary>
        private readonly List<(Piece Piece, ModuleInstance Other)> _statusLights = new List<(Piece, ModuleInstance)>();
        private readonly HashSet<ModuleInstance> _dimmed = new HashSet<ModuleInstance>();
        private readonly List<InteriorDoor> _doors = new List<InteriorDoor>();
        private readonly List<InteriorHatch> _hatches = new List<InteriorHatch>();
        /// <summary>열린 통로 면 (칸, 방향) — 양쪽 모두 들어 있다.</summary>
        private readonly HashSet<(Vector3Int, Vector3Int)> _open = new HashSet<(Vector3Int, Vector3Int)>();
        private readonly List<WallPanel> _wallBuffer = new List<WallPanel>();
        private readonly List<FloorRect> _floorBuffer = new List<FloorRect>();
        private Transform _colliders;
        private InteriorLayout _layout;

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

        /// <summary>그 방 + 열린 통로(문·해치, 6방향)로 바로 이어진 방들의 모듈.</summary>
        public void CollectLinkedRooms(ModuleInstance module, HashSet<ModuleInstance> into)
        {
            into.Clear();
            if (module == null || _layout == null)
                return;
            into.Add(module);
            foreach (var (cell, dir) in _open)
            {
                if (_layout.TryGetRoom(cell, out var room) && room.Module == module && _layout.TryGetRoom(cell + dir, out var other))
                    into.Add(other.Module);
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
            _statusLights.Clear();
            _dimmed.Clear();
            _doors.Clear();
            _hatches.Clear();
            _open.Clear();
            _layout = null;
        }

        public void Build(InteriorLayout layout, Transform player)
        {
            Clear();
            _layout = layout;
            _colliders = new GameObject("Colliders").transform;
            _colliders.SetParent(_root, false);

            // 1) 열린 통로: 양쪽 방이 모두 그 면에 문을 낼 수 있어야
            foreach (var face in layout.Faces)
            {
                if (face.Kind != InteriorFaceKind.Door && face.Kind != InteriorFaceKind.Hatch)
                    continue;
                layout.TryGetRoom(face.Cell, out var a);
                layout.TryGetRoom(face.OtherCell, out var b);
                if (Accepts(a.Module, face.Cell, face.Direction) && Accepts(b.Module, face.OtherCell, -face.Direction))
                    _open.Add((face.Cell, face.Direction));
            }

            var parents = new Dictionary<ModuleInstance, Transform>();
            foreach (var room in layout.Rooms)
            {
                var go = new GameObject(room.Module.ToString());
                go.transform.SetParent(_root, false);
                parents.Add(room.Module, go.transform);
                _roomPieces.Add(room.Module, new List<Piece>());
                _roomColors.Add(room.Module, RoomColor(room.Module.Data));
            }
            foreach (var room in layout.Rooms)
            {
                var parent = parents[room.Module];
                if (room.Module.Data != null && _templates.TryGetValue(room.Module.Data, out var template))
                {
                    BuildTemplateRoom(room, template, parent); // 조명은 템플릿 프리팹 것
                }
                else
                {
                    BuildFallbackRoom(room, parent);
                    BuildRoomLights(room, parent);
                }
            }

            // 2) 수평 통로마다 튜브 하나 (키상 작은 칸 쪽에서)
            var tubes = new GameObject("Tubes").transform;
            tubes.SetParent(_root, false);
            foreach (var (cell, dir) in _open)
            {
                if (dir.y != 0 || ConnectorLayout.MakeKey(cell, cell + dir).Item1 != cell)
                    continue;
                layout.TryGetRoom(cell, out var a);
                layout.TryGetRoom(cell + dir, out var b);
                var span = InteriorGeometry.Tube(cell, dir, DoorDepth(a.Module, cell, dir), DoorDepth(b.Module, cell + dir, -dir));
                BuildTube(span, tubes);
            }

            // 해치 도착점 = 맞은편 해치 자리 (템플릿은 칸 중심에서 옮겨 둘 수 있음)
            foreach (var hatch in _hatches)
            {
                var key = (hatch.ToCell, hatch.FromCell - hatch.ToCell);
                foreach (var other in _hatches)
                {
                    if ((other.FromCell, other.ToCell - other.FromCell) != key)
                        continue;
                    var p = other.transform.position;
                    p.y = _root.position.y + InteriorGeometry.FloorY(hatch.ToCell);
                    hatch.Arrival = p;
                }
            }

            foreach (var door in _doors)
                door.SetPlayer(player);
        }

        private bool Accepts(ModuleInstance module, Vector3Int cell, Vector3Int dir)
        {
            if (module.Data == null || !_templates.TryGetValue(module.Data, out var t))
                return true;
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

        /// <summary>비활성·파손 방은 어둡게 (색·발광 낮춤 + 조명 약하게). 그 방으로 가는 문 상태등은 빨강.</summary>
        public void SetRoomDim(ModuleInstance module, bool dim)
        {
            if (!_roomPieces.TryGetValue(module, out var pieces))
                return;
            if (dim)
                _dimmed.Add(module);
            else
                _dimmed.Remove(module);
            foreach (var piece in pieces)
                ApplyColors(piece, dim);
            if (_roomLights.TryGetValue(module, out var lights))
            {
                foreach (var light in lights)
                {
                    float full = _lightBase.TryGetValue(light, out float b) ? b : LightIntensity;
                    light.intensity = dim ? full * 0.25f : full;
                }
            }
            foreach (var (piece, other) in _statusLights)
            {
                if (other == module)
                    ApplyStatus(piece);
            }
        }

        private void ApplyColors(Piece piece, bool dim)
        {
            var r = piece.Renderer;
            var mats = r.sharedMaterials;
            for (int i = 0; i < piece.BaseColors.Length; i++)
            {
                if (i == piece.StatusSlot)
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

        private void ApplyStatus(Piece piece)
        {
            ModuleInstance other = null;
            foreach (var (p, o) in _statusLights)
            {
                if (p == piece)
                    other = o;
            }
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
            foreach (var r in instance.GetComponentsInChildren<Renderer>())
                _roomPieces[module].Add(new Piece { Renderer = r, BaseColors = BaseColors(r.sharedMaterials) });
            // 템플릿 조명도 어둡게 할 수 있게 (세기 비율 유지)
            var lights = new List<Light>(instance.GetComponentsInChildren<Light>());
            _roomLights[module] = lights;
            foreach (var l in lights)
                _lightBase[l] = l.intensity;

            foreach (var socket in template.Sockets)
            {
                var cell = module.Origin + GridDirections.Rotate(socket.Cell, module.Rotation);
                var dir = GridDirections.Rotate(socket.Direction, module.Rotation);
                bool open = _open.Contains((cell, dir));
                if (dir.y != 0)
                {
                    if (open)
                        BuildHatch(module, parent, CellCenter(cell) + (Vector3)dir * socket.Depth + rotation * socket.Offset, dir.y < 0, cell, dir);
                    continue;
                }
                var center = CellCenter(cell) + (Vector3)dir * socket.Depth;
                center.y = _root.position.y + InteriorGeometry.FloorY(cell) + InteriorGeometry.PanelCenterAboveFloor;
                Panel(new WallPanel(center - _root.position, dir, InteriorGeometry.PanelWidth, open), module, parent, open ? OtherRoom(cell, dir) : null);
            }
        }

        // ---------------- 대체 방 (벽 키트) ----------------

        private void BuildFallbackRoom(InteriorRoom room, Transform parent)
        {
            var module = room.Module;
            var doors = new HashSet<(Vector3Int, Vector3Int)>();
            foreach (var (cell, dir) in _open)
            {
                if (_layout.TryGetRoom(cell, out var r) && r.Module == module)
                    doors.Add((cell, dir));
            }
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

        private void Panel(WallPanel panel, ModuleInstance module, Transform parent, ModuleInstance other)
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
                Solid(solidCenter, rotation, new Vector3(panel.Width, h, t));
                if (_kit != null)
                    KitPiece(_kit.Wall, module, parent, center, rotation, new Vector3(panel.Width / InteriorGeometry.PanelWidth, 1f, 1f));
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
            var wall = KitPiece(_kit.WallDoor, module, parent, center, rotation);
            if (wall.StatusSlot >= 0 && other != null)
            {
                _statusLights.Add((wall, other));
                ApplyStatus(wall);
            }
            // 방마다 문 한 쌍 (튜브 양 끝 = 에어록). 벽 두께 안에서 미끄러져 숨는다
            BuildDoor(module, parent, center + up * (doorBottom + InteriorGeometry.DoorHeight * 0.5f) - panel.Normal * (t * 0.5f), rotation);
        }

        private void BuildDoor(ModuleInstance module, Transform parent, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject("Door");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
            var right = KitPiece(_kit.DoorLeaf, module, go.transform, position, rotation).Renderer.transform;
            var left = KitPiece(_kit.DoorLeaf, module, go.transform, position, rotation * Quaternion.Euler(0f, 180f, 0f)).Renderer.transform;
            right.name = "LeafRight";
            left.name = "LeafLeft";
            var door = go.AddComponent<InteriorDoor>();
            float leafWidth = _kit.DoorLeaf.Mesh.bounds.size.x;
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
                KitPiece(rect.Ceiling ? _kit.Ceiling : _kit.Floor, module, parent, surface, Quaternion.identity, new Vector3(rect.Size.x / 4f, 1f, rect.Size.y / 4f));
            else
                Cube(parent, module, slab, Quaternion.identity, size, rect.Ceiling ? _roomColors[module] : Multiply(_roomColors[module], FloorShade));
            if (rect.Hatch)
            {
                var cellCenter = CellCenter(rect.Cell);
                var hatchPoint = new Vector3(cellCenter.x, surface.y, cellCenter.z);
                BuildHatch(module, parent, hatchPoint, !rect.Ceiling, rect.Cell, rect.Ceiling ? Vector3Int.up : Vector3Int.down);
            }
        }

        /// <summary>해치 틀 + 뚜껑 (뚜껑에 상호작용 콜라이더). surface = 바닥 윗면 / 천장 아랫면.</summary>
        private void BuildHatch(ModuleInstance module, Transform parent, Vector3 surface, bool floor, Vector3Int cell, Vector3Int dir)
        {
            var flip = floor ? Quaternion.identity : Quaternion.Euler(180f, 0f, 0f);
            var lidCenter = surface + (floor ? Vector3.up : Vector3.down) * 0.04f;
            GameObject lid;
            if (_kit != null)
            {
                KitPiece(_kit.HatchFrame, module, parent, surface, flip);
                lid = KitPiece(_kit.HatchLid, module, parent, lidCenter, flip).Renderer.gameObject;
                var box = lid.AddComponent<BoxCollider>();
                box.center = Vector3.zero;
                box.size = new Vector3(InteriorGeometry.HatchSize, 0.08f, InteriorGeometry.HatchSize);
            }
            else
            {
                lid = Cube(parent, module, lidCenter, Quaternion.identity, new Vector3(InteriorGeometry.HatchSize, 0.08f, InteriorGeometry.HatchSize), HatchColor, true)
                    .Renderer.gameObject;
            }
            var h = lid.AddComponent<InteriorHatch>();
            h.FromCell = cell;
            h.ToCell = cell + dir;
            _hatches.Add(h);
        }

        // ---------------- 연결 튜브 ----------------

        private void BuildTube(TubeSpan span, Transform parent)
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
            if (_kit != null && _kit.HasTube)
            {
                Tube(_kit.Tube, go.transform, mid, rotation, new Vector3(1f, 1f, len));
                // 고리(두께 ±0.15, 바깥 반지름 1.87)를 벽면 중심에 두면 절반이 방 안으로 나와 문 패널(폭 ±1.6) 옆에 보였음 →
                // 통로 쪽으로 반 두께 + 0.003 밀어 벽 속에 묻음 (방 쪽 면이 벽면과 같은 평면이 되지 않게)
                var sink = span.Direction.normalized * CollarSink;
                Tube(_kit.TubeCollar, go.transform, start + sink, rotation, Vector3.one);
                Tube(_kit.TubeCollar, go.transform, end - sink, rotation, Vector3.one);
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
        }

        /// <summary>튜브 조각 (방 소속 아님: 어둡게 하지 않음, 강조색은 기본색).</summary>
        private void Tube(InteriorKitPiece kitPiece, Transform parent, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            var go = new GameObject(kitPiece.Mesh.name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = kitPiece.Mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.sharedMaterials = MaterialsFor(kitPiece, null);
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
            var piece = new Piece { Renderer = r, StatusSlot = kitPiece.StatusSlot, BaseColors = BaseColors(r.sharedMaterials) };
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
