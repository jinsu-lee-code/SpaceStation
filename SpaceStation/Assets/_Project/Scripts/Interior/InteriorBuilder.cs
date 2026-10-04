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
    /// Phase 11-1 그레이박스 내부 생성: <see cref="InteriorLayout"/>을 기본 큐브(바닥·벽·천장·문 구멍·해치 뚜껑)로 만든다.
    /// 칸 하나 = <see cref="RoomSize"/>m 정육면체. 모든 조각은 공용 재질 하나 + MaterialPropertyBlock 색.
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

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private static readonly Color CoreTint = new Color(0.62f, 0.70f, 0.80f);
        private static readonly Color FloorShade = new Color(0.55f, 0.55f, 0.57f);
        private static readonly Color DoorFrame = new Color(0.30f, 0.75f, 0.85f);
        private static readonly Color HatchColor = new Color(0.95f, 0.75f, 0.20f);
        private static readonly Color DarkMultiplier = new Color(0.35f, 0.35f, 0.38f);

        private readonly Transform _root;
        private readonly Material _material;
        private readonly Material _lightMaterial;
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        private readonly Dictionary<ModuleInstance, List<Renderer>> _roomRenderers = new Dictionary<ModuleInstance, List<Renderer>>();
        private readonly Dictionary<ModuleInstance, Light> _roomLights = new Dictionary<ModuleInstance, Light>();
        private readonly Dictionary<ModuleInstance, Color> _roomColors = new Dictionary<ModuleInstance, Color>();

        public InteriorBuilder(Transform root, Material material, Material lightMaterial)
        {
            _root = root;
            _material = material;
            _lightMaterial = lightMaterial;
        }

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
            _roomRenderers.Clear();
            _roomLights.Clear();
            _roomColors.Clear();
        }

        public void Build(InteriorLayout layout)
        {
            Clear();
            var parents = new Dictionary<ModuleInstance, Transform>();
            foreach (var room in layout.Rooms)
            {
                var go = new GameObject(room.Module.ToString());
                go.transform.SetParent(_root, false);
                parents.Add(room.Module, go.transform);
                _roomRenderers.Add(room.Module, new List<Renderer>());
                _roomColors.Add(room.Module, RoomColor(room.Module.Data));
                BuildRoomLight(room, go.transform);
            }
            foreach (var face in layout.Faces)
            {
                if (face.Kind == InteriorFaceKind.Open)
                    continue;
                layout.TryGetRoom(face.Cell, out var room);
                BuildFace(face, room.Module, parents[room.Module]);
            }
        }

        /// <summary>비활성·파손 방은 어둡게 (색 곱 + 조명 약하게).</summary>
        public void SetRoomDim(ModuleInstance module, bool dim)
        {
            if (!_roomRenderers.TryGetValue(module, out var renderers))
                return;
            foreach (var r in renderers)
            {
                r.GetPropertyBlock(_block);
                var baseColor = r.GetComponent<InteriorPiece>().Color;
                _block.SetColor(BaseColorId, dim ? baseColor * DarkMultiplier : baseColor);
                r.SetPropertyBlock(_block);
            }
            if (_roomLights.TryGetValue(module, out var light))
                light.intensity = dim ? LightIntensity * 0.25f : LightIntensity;
        }

        // ---------------- 조각 ----------------

        private void BuildFace(InteriorFace face, ModuleInstance module, Transform parent)
        {
            var color = _roomColors[module];
            var center = CellCenter(face.Cell);
            float half = RoomSize * 0.5f;
            var dir = (Vector3)face.Direction;
            var rotation = Quaternion.LookRotation(dir, dir.y != 0 ? Vector3.forward : Vector3.up);
            // 면 로컬 축: right = u(가로), up = v(세로), forward = 면 법선 (바깥 방향)
            var right = rotation * Vector3.right;
            var up = rotation * Vector3.up;
            var panelCenter = center + dir * (half - Thickness * 0.5f);

            if (face.Direction.y != 0)
            {
                // 바닥·천장: 통째 판 + 해치면 뚜껑
                var shade = face.Direction.y < 0 ? Multiply(color, FloorShade) : color;
                Piece(parent, module, panelCenter, rotation, new Vector3(RoomSize, RoomSize, Thickness), shade);
                if (face.Direction.y > 0 && face.Kind != InteriorFaceKind.Hatch)
                    CeilingLight(parent, center + Vector3.up * (half - Thickness - 0.03f));
                if (face.Kind == InteriorFaceKind.Hatch)
                {
                    var lidCenter = center + dir * (half - Thickness - 0.04f);
                    var lid = Piece(parent, module, lidCenter, rotation, new Vector3(HatchSize, HatchSize, 0.08f), HatchColor);
                    var hatch = lid.gameObject.AddComponent<InteriorHatch>();
                    hatch.FromCell = face.Cell;
                    hatch.ToCell = face.OtherCell;
                }
                return;
            }

            if (face.Kind == InteriorFaceKind.Wall)
            {
                Piece(parent, module, panelCenter, rotation, new Vector3(RoomSize, RoomSize, Thickness), color);
                return;
            }

            // 문: 왼쪽·오른쪽 기둥, 위 인방, 아래 문턱(바닥 높이까지)
            float side = (RoomSize - DoorWidth) * 0.5f;
            float floorTop = -half + Thickness;
            float lintel = half - (floorTop + DoorHeight);
            Piece(parent, module, panelCenter - right * (DoorWidth * 0.5f + side * 0.5f), rotation, new Vector3(side, RoomSize, Thickness), color);
            Piece(parent, module, panelCenter + right * (DoorWidth * 0.5f + side * 0.5f), rotation, new Vector3(side, RoomSize, Thickness), color);
            Piece(parent, module, panelCenter + up * (half - lintel * 0.5f), rotation, new Vector3(DoorWidth, lintel, Thickness), color);
            Piece(parent, module, panelCenter + up * (-half + Thickness * 0.5f), rotation, new Vector3(DoorWidth, Thickness, Thickness), Multiply(color, FloorShade));
            // 문틀 강조 (얇은 띠, 충돌 없음)
            var frameCenter = panelCenter - dir * (Thickness * 0.5f + 0.02f);
            float frame = 0.08f;
            Piece(parent, module, frameCenter - right * (DoorWidth * 0.5f + frame * 0.5f) + up * (floorTop + DoorHeight * 0.5f), rotation, new Vector3(frame, DoorHeight, 0.04f), DoorFrame, false);
            Piece(parent, module, frameCenter + right * (DoorWidth * 0.5f + frame * 0.5f) + up * (floorTop + DoorHeight * 0.5f), rotation, new Vector3(frame, DoorHeight, 0.04f), DoorFrame, false);
            Piece(parent, module, frameCenter + up * (floorTop + DoorHeight + frame * 0.5f), rotation, new Vector3(DoorWidth + frame * 2f, frame, 0.04f), DoorFrame, false);
        }

        private Renderer Piece(Transform parent, ModuleInstance module, Vector3 position, Quaternion rotation, Vector3 size, Color color, bool collider = true)
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
            go.AddComponent<InteriorPiece>().Color = color;
            r.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, color);
            r.SetPropertyBlock(_block);
            _roomRenderers[module].Add(r);
            return r;
        }

        private void CeilingLight(Transform parent, Vector3 position)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "CeilingLight";
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.localScale = new Vector3(1.6f, 0.04f, 0.5f);
            Object.Destroy(go.GetComponent<Collider>());
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = _lightMaterial != null ? _lightMaterial : _material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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

    /// <summary>조각의 원래 색 (어둡게 했다 되돌릴 때).</summary>
    public sealed class InteriorPiece : MonoBehaviour
    {
        public Color Color;
    }
}
