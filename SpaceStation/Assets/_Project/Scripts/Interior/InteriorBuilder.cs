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
    }

    /// <summary>
    /// Phase 11 내부 생성: <see cref="InteriorLayout"/>을 벽 키트(<see cref="InteriorKit"/>, 11-2a)로 만든다.
    /// 칸 하나 = <see cref="RoomSize"/>m 정육면체. 충돌은 보이지 않는 상자 콜라이더(벽·바닥·천장·문 구멍)로 따로 둔다.
    /// 키트의 강조 슬롯은 그 모듈의 강조색 재질로 바꾸고, 문 상태등은 너머 방 상태(정상 청록 / 비활성·파손 빨강).
    /// 키트가 없으면 11-1 그레이박스 큐브(분류별 색)로 대신한다.
    /// 비활성·파손 방은 슬롯별 MaterialPropertyBlock으로 어둡게 (정상 방은 블록을 비워 SRP Batcher 유지).
    /// </summary>
    public sealed class InteriorBuilder
    {
        /// <summary>내부 1칸의 크기 (m). 외부 1칸(1유닛)을 사람 크기로 키운 값.</summary>
        public const float RoomSize = 4f;
        public const float Thickness = 0.2f;
        public const float DoorWidth = 1.4f;
        public const float DoorHeight = 2.4f;
        public const float HatchSize = 1.4f;
        private const float LightIntensity = 5f;
        private const float Dark = 0.35f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private static readonly Color CoreTint = new Color(0.62f, 0.70f, 0.80f);
        private static readonly Color FloorShade = new Color(0.55f, 0.55f, 0.57f);
        private static readonly Color DoorFrame = new Color(0.30f, 0.75f, 0.85f);
        private static readonly Color HatchColor = new Color(0.95f, 0.75f, 0.20f);
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
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        private readonly MaterialPropertyBlock _empty = new MaterialPropertyBlock();
        private readonly Dictionary<ModuleInstance, List<Piece>> _roomPieces = new Dictionary<ModuleInstance, List<Piece>>();
        private readonly Dictionary<ModuleInstance, Light> _roomLights = new Dictionary<ModuleInstance, Light>();
        private readonly Dictionary<ModuleInstance, Color> _roomColors = new Dictionary<ModuleInstance, Color>();
        /// <summary>문 상태등: (렌더러 조각, 너머 방).</summary>
        private readonly List<(Piece Piece, ModuleInstance Other)> _statusLights = new List<(Piece, ModuleInstance)>();
        private readonly HashSet<ModuleInstance> _dimmed = new HashSet<ModuleInstance>();
        private readonly List<InteriorDoor> _doors = new List<InteriorDoor>();
        private Transform _colliders;

        public InteriorBuilder(Transform root, InteriorKit kit, Material fallbackMaterial)
        {
            _root = root;
            _kit = kit != null && kit.IsComplete ? kit : null;
            _material = fallbackMaterial;
        }

        public bool UsesKit => _kit != null;

        /// <summary>칸 중심의 월드 위치.</summary>
        public Vector3 CellCenter(Vector3Int cell) => _root.position + new Vector3(cell.x, cell.y, cell.z) * RoomSize;

        /// <summary>칸 바닥 윗면 높이의 중심점.</summary>
        public Vector3 FloorPoint(Vector3Int cell) => CellCenter(cell) + Vector3.up * (-RoomSize * 0.5f + Thickness);

        public Vector3Int WorldToCell(Vector3 world)
        {
            var local = (world - _root.position) / RoomSize;
            return new Vector3Int(Mathf.RoundToInt(local.x), Mathf.RoundToInt(local.y), Mathf.RoundToInt(local.z));
        }

        public void Clear()
        {
            for (int i = _root.childCount - 1; i >= 0; i--)
                Object.Destroy(_root.GetChild(i).gameObject);
            _roomPieces.Clear();
            _roomLights.Clear();
            _roomColors.Clear();
            _statusLights.Clear();
            _dimmed.Clear();
            _doors.Clear();
        }

        public void Build(InteriorLayout layout, Transform player)
        {
            Clear();
            _colliders = new GameObject("Colliders").transform;
            _colliders.SetParent(_root, false);
            var parents = new Dictionary<ModuleInstance, Transform>();
            foreach (var room in layout.Rooms)
            {
                var go = new GameObject(room.Module.ToString());
                go.transform.SetParent(_root, false);
                parents.Add(room.Module, go.transform);
                _roomPieces.Add(room.Module, new List<Piece>());
                _roomColors.Add(room.Module, RoomColor(room.Module.Data));
                BuildRoomLight(room, go.transform);
            }
            foreach (var face in layout.Faces)
            {
                if (face.Kind == InteriorFaceKind.Open)
                    continue;
                layout.TryGetRoom(face.Cell, out var room);
                layout.TryGetRoom(face.OtherCell, out var other);
                BuildFace(face, room.Module, other?.Module, parents[room.Module]);
            }
            foreach (var door in _doors)
                door.SetPlayer(player);
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
            if (_roomLights.TryGetValue(module, out var light))
                light.intensity = dim ? LightIntensity * 0.25f : LightIntensity;
            foreach (var (piece, other) in _statusLights)
            {
                if (other == module)
                    ApplyStatus(piece);
            }
        }

        private void ApplyColors(Piece piece, bool dim)
        {
            var r = piece.Renderer;
            for (int i = 0; i < piece.BaseColors.Length; i++)
            {
                if (i == piece.StatusSlot)
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

        // ---------------- 면 ----------------

        private void BuildFace(InteriorFace face, ModuleInstance module, ModuleInstance other, Transform parent)
        {
            var center = CellCenter(face.Cell);
            float half = RoomSize * 0.5f;
            var dir = (Vector3)face.Direction;

            if (face.Direction.y != 0)
            {
                BuildFloorOrCeiling(face, module, parent, center, half);
                return;
            }

            // 벽: 면 로컬 right = 가로, up = 위, forward = 바깥 (키트 벽은 -Z 쪽 방 안으로 두께)
            var rotation = Quaternion.LookRotation(dir, Vector3.up);
            var right = rotation * Vector3.right;
            var facePoint = center + dir * half;
            var panelCenter = center + dir * (half - Thickness * 0.5f);
            var color = _roomColors[module];

            if (face.Kind == InteriorFaceKind.Wall)
            {
                Solid(panelCenter, rotation, new Vector3(RoomSize, RoomSize, Thickness));
                if (_kit != null)
                    KitPiece(_kit.Wall, module, parent, facePoint, rotation);
                else
                    Cube(parent, module, panelCenter, rotation, new Vector3(RoomSize, RoomSize, Thickness), color);
                return;
            }

            // 문: 충돌 = 왼쪽·오른쪽 기둥, 위 인방 (문턱은 바닥 판이 막음)
            float side = (RoomSize - DoorWidth) * 0.5f;
            float floorTop = -half + Thickness;
            float lintel = half - (floorTop + DoorHeight);
            var up = Vector3.up;
            Solid(panelCenter - right * (DoorWidth * 0.5f + side * 0.5f), rotation, new Vector3(side, RoomSize, Thickness));
            Solid(panelCenter + right * (DoorWidth * 0.5f + side * 0.5f), rotation, new Vector3(side, RoomSize, Thickness));
            Solid(panelCenter + up * (half - lintel * 0.5f), rotation, new Vector3(DoorWidth, lintel, Thickness));

            if (_kit == null)
            {
                Cube(parent, module, panelCenter - right * (DoorWidth * 0.5f + side * 0.5f), rotation, new Vector3(side, RoomSize, Thickness), color);
                Cube(parent, module, panelCenter + right * (DoorWidth * 0.5f + side * 0.5f), rotation, new Vector3(side, RoomSize, Thickness), color);
                Cube(parent, module, panelCenter + up * (half - lintel * 0.5f), rotation, new Vector3(DoorWidth, lintel, Thickness), color);
                var frameCenter = panelCenter - dir * (Thickness * 0.5f + 0.02f) + up * (floorTop + DoorHeight * 0.5f);
                Cube(parent, module, frameCenter - right * (DoorWidth * 0.5f + 0.04f), rotation, new Vector3(0.08f, DoorHeight, 0.04f), DoorFrame);
                Cube(parent, module, frameCenter + right * (DoorWidth * 0.5f + 0.04f), rotation, new Vector3(0.08f, DoorHeight, 0.04f), DoorFrame);
                return;
            }

            var wall = KitPiece(_kit.WallDoor, module, parent, facePoint, rotation);
            if (wall.StatusSlot >= 0 && other != null)
            {
                _statusLights.Add((wall, other));
                ApplyStatus(wall);
            }
            // 문짝은 통로 하나에 한 쌍 (키상 작은 칸 쪽에서), 두 벽 경계면에 놓여 열리면 벽 속으로 숨는다
            if (ConnectorLayout.MakeKey(face.Cell, face.OtherCell).Item1 == face.Cell)
                BuildDoor(module, parent, facePoint + up * (floorTop + DoorHeight * 0.5f), rotation);
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
            door.Initialize(left, right, leafWidth, new Vector3(DoorWidth, DoorHeight, 0.1f));
            _doors.Add(door);
        }

        private void BuildFloorOrCeiling(InteriorFace face, ModuleInstance module, Transform parent, Vector3 center, float half)
        {
            bool floor = face.Direction.y < 0;
            var dir = (Vector3)face.Direction;
            var surface = center + dir * (half - Thickness); // 바닥 윗면 / 천장 아랫면
            Solid(center + dir * (half - Thickness * 0.5f), Quaternion.identity, new Vector3(RoomSize, Thickness, RoomSize));
            bool hatch = face.Kind == InteriorFaceKind.Hatch;
            var flip = floor ? Quaternion.identity : Quaternion.Euler(180f, 0f, 0f);

            if (_kit != null)
            {
                KitPiece(floor ? _kit.Floor : _kit.Ceiling, module, parent, surface, Quaternion.identity);
                if (hatch)
                    KitPiece(_kit.HatchFrame, module, parent, surface, flip);
            }
            else
            {
                var color = _roomColors[module];
                Cube(parent, module, center + dir * (half - Thickness * 0.5f), Quaternion.identity, new Vector3(RoomSize, Thickness, RoomSize),
                    floor ? Multiply(color, FloorShade) : color);
            }

            if (!hatch)
                return;
            var lidCenter = surface - dir * 0.04f;
            GameObject lid;
            if (_kit != null)
            {
                lid = KitPiece(_kit.HatchLid, module, parent, lidCenter, flip).Renderer.gameObject;
                var box = lid.AddComponent<BoxCollider>();
                box.center = Vector3.zero;
                box.size = new Vector3(HatchSize, 0.08f, HatchSize);
            }
            else
            {
                lid = Cube(parent, module, lidCenter, Quaternion.identity, new Vector3(HatchSize, 0.08f, HatchSize), HatchColor, true).Renderer.gameObject;
            }
            var h = lid.AddComponent<InteriorHatch>();
            h.FromCell = face.Cell;
            h.ToCell = face.OtherCell;
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

        private Piece KitPiece(InteriorKitPiece kitPiece, ModuleInstance module, Transform parent, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject(kitPiece.Mesh.name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
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
                var accent = _kit.AccentFor(module.Data);
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

        /// <summary>방마다 점광원 하나 (그림자 없음). 칸들의 가로 가운데, 맨 아래층 천장 아래 (걷는 높이를 밝힘).</summary>
        private void BuildRoomLight(InteriorRoom room, Transform parent)
        {
            var min = room.Cells[0];
            var max = room.Cells[0];
            foreach (var c in room.Cells)
            {
                min = Vector3Int.Min(min, c);
                max = Vector3Int.Max(max, c);
            }
            var center = (CellCenter(min) + CellCenter(max)) * 0.5f;
            center.y = CellCenter(min).y + RoomSize * 0.5f - 0.6f;
            var extent = (Vector3)(max - min) * RoomSize;
            var go = new GameObject("Light");
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.shadows = LightShadows.None;
            light.color = new Color(1f, 0.96f, 0.9f);
            light.range = RoomSize * 2f + extent.magnitude * 0.6f;
            light.intensity = LightIntensity;
            _roomLights.Add(room.Module, light);
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
