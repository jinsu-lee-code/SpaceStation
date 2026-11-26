using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Building
{
    /// <summary>
    /// 5-6 모듈 사이 연결 통로 자동 생성.
    /// - 옆(수평) 연결 = 팔각 여압 통로 + 양끝 칼라 + 불빛 띠 / 위아래 연결 = 기둥 조인트 + 받침판 + 불빛 띠
    /// - 길이는 양쪽 모델 표면 깊이(ModuleData.FaceDepths)로 정해 표면끼리 정확히 잇는다 (ConnectorLayout)
    /// - 상태: 코어와 끊긴(비활성) 모듈에 닿은 통로는 어둡게, 활성 통로는 코어에서 바깥쪽으로 빛이 흐른다
    /// - 배치 미리보기: BuildController가 ShowPreview/HidePreview로 고스트 통로를 홀로그램으로 표시
    /// - 진화 단계별 모양 (2026-11-17, <see cref="ConnectorStyle"/>): 두 모듈 중 높은 단계 — 1 세라믹 캡슐 · 2 에어록 현관 포드 · 3 · 4 받침을 잇는 다리 데크,
    ///   데크가 2단계 이하 모듈에 닿는 끝 = 에어록 문 고리. 고리 · 난간 기둥 · 정원등은 늘리지 않는 따로 조각을 정해진 간격으로 놓음.
    /// - ⑪ 바깥 통로 끝 맞닿음 (2026-11-26): 위아래 기둥은 기둥 바로 밑 표면 (받침 윗면 등)까지, 3 · 4단계 데크는 데크 높이의 섬 · 받침 가장자리까지,
    ///   1 · 2단계 얇은 받침 위에 뜬 옆 통로 끝은 받침 위에 내려앉혀 비스듬히 잇고 (가파르면 경사로처럼 받침 위로 밀어 넣음) 칼라는 벽에 맞춰 세움.
    /// 메시·재질은 StationArtBuilder(Connectors)가 배선한다 (진화 = ConnectorSetup, SM_ConnectorsEvo.fbx).
    /// </summary>
    public sealed class StationConnectors : MonoBehaviour
    {
        /// <summary>진화 단계 한 벌 (길이 방향 = Z, 통로 · 띠 · 기둥 = 단위 길이 1).</summary>
        [System.Serializable]
        private sealed class StageSet
        {
            public Mesh Tube;
            public Mesh Collar;
            [Tooltip("통로를 따라 정해진 간격마다 놓는 조각 (고리 · 둥근 창 · 난간 기둥 · 정원등)")]
            public Mesh Ring;
            public Mesh Strip;
            public Mesh Strut;
            public Mesh Plate;
            [Tooltip("위아래 기둥 가운데 한 벌")]
            public Mesh VRing;
            public float RingStep = 0.25f;
        }

        private const float RingClear = 0.07f; // 칼라 · 착지판 안쪽부터 고리를 놓음
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int LengthId = Shader.PropertyToID("_Length");
        private static readonly int FlowId = Shader.PropertyToID("_Flow");
        private static readonly int EnergyId = Shader.PropertyToID("_Energy");

        [SerializeField] private StationController _station;

        [Header("Meshes (길이 방향 = Z, 단위 길이 1)")]
        [SerializeField] private Mesh _tubeMesh;
        [SerializeField] private Mesh _collarMesh;
        [SerializeField] private Mesh _strutMesh;
        [SerializeField] private Mesh _plateMesh;
        [SerializeField] private Mesh _stripMesh;

        [Header("Materials")]
        [SerializeField] private Material _hullMaterial;
        [SerializeField] private Material _flowMaterial;
        [SerializeField] private Material _ghostMaterial;

        [Header("진화 단계 (1 ~ 4)")]
        [SerializeField] private StageSet[] _stages = new StageSet[0];
        [Tooltip("다리 데크가 2단계 이하 모듈에 닿는 면의 에어록 문 고리")]
        [SerializeField] private Mesh _lockMesh;
        [Tooltip("진화 통로 재질 (색표 텍스처 하나)")]
        [SerializeField] private Material _evoMaterial;

        [Header("Look")]
        [Tooltip("불빛 띠 위치 (통로 중심에서 위쪽으로)")]
        [SerializeField] private float _stripOffset = 0.088f;
        [SerializeField, Range(0f, 1f)] private float _inactiveBrightness = 0.35f;

        private sealed class Connector
        {
            public GameObject Root;
            public ConnectorSpec Spec;
            /// <summary>실제로 지은 길이 (다리 데크 = 데크 높이 깊이로 잰 길이).</summary>
            public float Length;
            public Renderer[] Hull;
            public Renderer[] Strips;
            public bool? Active;
            public float Flow;
        }

        private readonly Dictionary<(Vector3Int, Vector3Int), Connector> _connectors = new Dictionary<(Vector3Int, Vector3Int), Connector>();
        private readonly List<ConnectorSpec> _specs = new List<ConnectorSpec>();
        private readonly List<(Vector3Int, Vector3Int)> _removeKeys = new List<(Vector3Int, Vector3Int)>();
        private readonly Dictionary<ModuleInstance, int> _distance = new Dictionary<ModuleInstance, int>();
        private readonly Queue<ModuleInstance> _queue = new Queue<ModuleInstance>();
        private readonly List<Connector> _ghosts = new List<Connector>();
        private MaterialPropertyBlock _block;
        private Transform _root;
        private Transform _ghostRoot;
        private bool _stateDirty;

        public int Count => _connectors.Count;

        private void Start()
        {
            if (_station == null)
                _station = GetComponent<StationController>();
            _block = new MaterialPropertyBlock();
            _root = new GameObject("Connectors").transform;
            _root.SetParent(transform, false);
            _ghostRoot = new GameObject("ConnectorGhosts").transform;
            _ghostRoot.SetParent(transform, false);
            ConnectorLayout.SolarSideAxis = SolarSideAxisFromSun();

            var grid = _station.Grid;
            foreach (var module in grid.Modules)
                AddFor(module);
            grid.ModulePlaced += AddFor;
            grid.ModuleRemoved += RemoveFor;
            grid.ModuleMoved += MoveFor;
            _station.Connectivity.ActiveStateChanged += HandleActiveChanged;
            _stateDirty = true;
        }

        private void OnDestroy()
        {
            if (_station == null || _station.Simulation == null)
                return;
            _station.Grid.ModulePlaced -= AddFor;
            _station.Grid.ModuleRemoved -= RemoveFor;
            _station.Grid.ModuleMoved -= MoveFor;
            _station.Connectivity.ActiveStateChanged -= HandleActiveChanged;
        }

        private void HandleActiveChanged(ModuleInstance module, bool active) => _stateDirty = true;

        /// <summary>11-8 바깥 창 카메라: 모듈(칸 목록)에 닿은 통로 렌더러를 모음 (창 바로 앞을 통로 끝이 가리지 않게).</summary>
        public void CollectRenderersTouching(IEnumerable<Vector3Int> cells, List<Renderer> into)
        {
            // 12-3c-opt: 통로는 면이 맞닿은 두 칸 사이에만 있으므로 칸마다 6면만 찾아봄
            // (예전: 모든 통로를 훑음 — 대륙급 내부 바깥 창이 그릴 때마다 반복돼 프레임당 약 13ms)
            _found.Clear();
            foreach (var cell in cells)
            {
                foreach (var dir in GridDirections.Faces)
                {
                    if (_connectors.TryGetValue(ConnectorLayout.MakeKey(cell, cell + dir), out var c) && c.Root != null && _found.Add(c))
                    {
                        into.AddRange(c.Hull);
                        into.AddRange(c.Strips);
                    }
                }
            }
        }

        private readonly HashSet<Connector> _found = new HashSet<Connector>();
        private readonly HashSet<Connector> _near = new HashSet<Connector>();

        /// <summary>11-8 바깥 창 카메라: 경계 상자(여유 margin)가 점을 품은 통로 렌더러를 모음 (통로 속에서 자기 껍데기를 숨기려고).</summary>
        public void CollectRenderersContaining(Vector3 point, float margin, List<Renderer> into)
        {
            // 12-3c-opt: 점이 든 칸 둘레(3×3×3)의 통로만 확인 (예전: 모든 통로)
            _near.Clear();
            var center = GridConfig.WorldToCell(point);
            for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            for (int z = -1; z <= 1; z++)
            {
                var cell = center + new Vector3Int(x, y, z);
                foreach (var dir in GridDirections.Faces)
                {
                    if (_connectors.TryGetValue(ConnectorLayout.MakeKey(cell, cell + dir), out var near))
                        _near.Add(near);
                }
            }
            foreach (var c in _near)
            {
                if (c.Root == null)
                    continue;
                bool inside = false;
                foreach (var r in c.Hull)
                {
                    var b = r.bounds;
                    b.Expand(margin * 2f);
                    if (b.Contains(point))
                    {
                        inside = true;
                        break;
                    }
                }
                if (!inside)
                    continue;
                into.AddRange(c.Hull);
                into.AddRange(c.Strips);
            }
        }

        /// <summary>태양 수평 방향에 수직인 격자 축 = 패널 회전축 (SunFacingPanel과 같은 기준).</summary>
        private static Vector3Int SolarSideAxisFromSun()
        {
            var sun = RenderSettings.sun;
            if (sun == null)
                return Vector3Int.right;
            Vector3 toSun = -sun.transform.forward;
            Vector3 flat = Vector3.ProjectOnPlane(toSun, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f)
                return Vector3Int.right;
            Vector3 axis = Vector3.Cross(Vector3.up, flat);
            return Mathf.Abs(axis.x) >= Mathf.Abs(axis.z) ? Vector3Int.right : new Vector3Int(0, 0, 1);
        }

        private void LateUpdate()
        {
            if (!_stateDirty)
                return;
            _stateDirty = false;
            RefreshStates();
        }

        // ---------------- 생성/제거 ----------------

        private void AddFor(ModuleInstance module)
        {
            ConnectorLayout.ForModule(_station.Grid, module, _specs);
            foreach (var spec in _specs)
            {
                if (_connectors.ContainsKey(spec.Key))
                    continue;
                var c = Build(spec, _root, false);
                _connectors.Add(spec.Key, c);
            }
            _stateDirty = true;
        }

        private void RemoveFor(ModuleInstance module) => RemoveTouching(module.Cells);

        /// <summary>12-3b: 옛 칸에 닿던 통로를 걷고 새 자리에서 다시 잇는다.</summary>
        private void MoveFor(ModuleInstance module, IReadOnlyList<Vector3Int> oldCells)
        {
            RemoveTouching(oldCells);
            AddFor(module);
        }

        private void RemoveTouching(IReadOnlyList<Vector3Int> touched)
        {
            _removeKeys.Clear();
            var cells = new HashSet<Vector3Int>(touched);
            foreach (var pair in _connectors)
            {
                if (cells.Contains(pair.Key.Item1) || cells.Contains(pair.Key.Item2))
                    _removeKeys.Add(pair.Key);
            }
            foreach (var key in _removeKeys)
            {
                Destroy(_connectors[key].Root);
                _connectors.Remove(key);
            }
            _stateDirty = true;
        }

        // ---------------- 진화 단계 ----------------

        private Dictionary<ModuleData, int> _stageOf;

        /// <summary>모듈의 진화 단계 (그 모듈을 만드는 레시피 단계, 기본 모듈 = 0).</summary>
        private int StageOf(ModuleData data)
        {
            if (data == null)
                return 0;
            if (_stageOf == null)
            {
                _stageOf = new Dictionary<ModuleData, int>();
                var recipes = _station != null && _station.Simulation != null && _station.Simulation.Synthesis != null ? _station.Simulation.Synthesis.Recipes : null;
                if (recipes != null)
                {
                    foreach (var r in recipes)
                    {
                        if (r != null && r.Result != null && (!_stageOf.TryGetValue(r.Result, out int s) || r.Stage > s))
                            _stageOf[r.Result] = r.Stage;
                    }
                }
            }
            return _stageOf.TryGetValue(data, out int stage) ? stage : 0;
        }

        /// <summary>칸의 모듈 단계 (배치 미리보기 = 아직 그리드에 없는 칸은 놓을 모듈).</summary>
        private int StageAt(Vector3Int cell, ModuleData placing)
        {
            if (_station.Grid.TryGetModule(cell, out var m) && m != _ignorePreview)
                return StageOf(m.Data);
            return StageOf(placing);
        }

        private ModuleInstance _ignorePreview;

        private StageSet SetFor(int stage)
        {
            int i = stage - 1;
            if (i < 0 || i >= _stages.Length)
                return null;
            var s = _stages[i];
            return s != null && s.Tube != null && s.Strut != null ? s : null;
        }

        private Connector Build(ConnectorSpec spec, Transform parent, bool ghost, ModuleData placing = null)
        {
            var c = new Connector { Spec = spec };
            c.Root = new GameObject(ghost ? "GhostConnector" : $"Connector {spec.CellA}->{spec.CellB}");
            c.Root.transform.SetParent(parent, false);
            var hull = new List<Renderer>();
            var strips = new List<Renderer>();
            Material hullMat = ghost ? _ghostMaterial : _hullMaterial;
            Material stripMat = ghost ? _ghostMaterial : _flowMaterial;
            var style = ConnectorStyle.For(StageAt(spec.CellA, placing), StageAt(spec.CellB, placing), spec.IsVertical);
            var set = SetFor(style.Stage);
            // ⑪ 다리 데크는 데크 높이 (칸 가운데 −0.12)에서 잰 섬 · 받침 가장자리끼리 (칸 가운데 높이 건물 · 나무까지 잔디 위를 가로지르지 않게)
            bool deck = style.IsDeck && !spec.IsVertical && set != null && _evoMaterial != null;
            // ⑪ 1 · 2단계 받침 위에 내려앉은 끝이 있으면 두 끝을 비스듬히 (칼라는 벽에 맞춰 세움)
            bool sloped = !deck && !spec.IsVertical && spec.IsSloped;
            float length = Place(c.Root.transform, spec, deck, sloped);
            c.Length = length;
            float half = length * 0.5f;
            if (set != null && _evoMaterial != null)
            {
                BuildEvo(c, set, style, ghost ? _ghostMaterial : _evoMaterial, stripMat, length, hull, strips);
            }
            else if (!spec.IsVertical)
            {
                hull.Add(Part(c.Root.transform, "Tube", _tubeMesh, hullMat, Vector3.zero, new Vector3(1f, 1f, length)));
                hull.Add(Part(c.Root.transform, "CollarA", _collarMesh, hullMat, new Vector3(0f, 0f, -half), Vector3.one));
                hull.Add(Part(c.Root.transform, "CollarB", _collarMesh, hullMat, new Vector3(0f, 0f, half), Vector3.one, 180f));
                strips.Add(Part(c.Root.transform, "Strip", _stripMesh, stripMat, new Vector3(0f, _stripOffset, 0f), new Vector3(0.022f, 0.008f, length)));
            }
            else
            {
                hull.Add(Part(c.Root.transform, "Strut", _strutMesh, hullMat, Vector3.zero, new Vector3(1f, 1f, length)));
                hull.Add(Part(c.Root.transform, "PlateA", _plateMesh, hullMat, new Vector3(0f, 0f, -half), Vector3.one));
                hull.Add(Part(c.Root.transform, "PlateB", _plateMesh, hullMat, new Vector3(0f, 0f, half), Vector3.one, 180f));
                for (int s = -1; s <= 1; s += 2)
                    strips.Add(Part(c.Root.transform, "Strip", _stripMesh, stripMat, new Vector3(s * 0.081f, 0f, 0f), new Vector3(0.008f, 0.03f, length)));
            }
            if (sloped)
                KeepCollarsUpright(c.Root.transform, spec.Direction);
            c.Hull = hull.ToArray();
            c.Strips = strips.ToArray();
            foreach (var r in c.Strips)
            {
                _block.Clear();
                _block.SetFloat(LengthId, Mathf.Max(0.01f, length));
                if (!ghost)
                    r.SetPropertyBlock(_block);
            }
            return c;
        }

        /// <summary>
        /// 진화 단계 통로: 수평 = 통로 · 띠 (길이만큼 늘림) + 양끝 칼라 · 착지판 (B는 뒤집음) + 간격마다 고리 + 데크가 낮은 단계에 닿는 끝의 에어록 고리,
        /// 수직 = 기둥 (늘림) + 양끝 받침판 + 가운데 고리.
        /// </summary>
        private void BuildEvo(Connector c, StageSet set, ConnectorStyle style, Material material, Material stripMaterial, float length, List<Renderer> hull, List<Renderer> strips)
        {
            var t = c.Root.transform;
            float half = length * 0.5f;
            var stretch = new Vector3(1f, 1f, Mathf.Max(0.001f, length));
            if (!c.Spec.IsVertical)
            {
                hull.Add(Part(t, "Tube", set.Tube, material, Vector3.zero, stretch));
                if (set.Collar != null)
                {
                    hull.Add(Part(t, "CollarA", set.Collar, material, new Vector3(0f, 0f, -half), Vector3.one));
                    hull.Add(Part(t, "CollarB", set.Collar, material, new Vector3(0f, 0f, half), Vector3.one, 180f));
                }
                if (set.Strip != null)
                    strips.Add(Part(t, "Strip", set.Strip, stripMaterial, Vector3.zero, stretch));
                if (set.Ring != null && set.RingStep > 0.01f)
                {
                    float free = length - 2f * RingClear;
                    int n = Mathf.FloorToInt(free / set.RingStep);
                    for (int i = 0; i < n; i++)
                        hull.Add(Part(t, "Ring", set.Ring, material, new Vector3(0f, 0f, -free * 0.5f + (i + 0.5f) * free / n), Vector3.one));
                }
                if (_lockMesh != null && style.LockAtA)
                    hull.Add(Part(t, "LockA", _lockMesh, material, new Vector3(0f, 0f, -half), Vector3.one));
                if (_lockMesh != null && style.LockAtB)
                    hull.Add(Part(t, "LockB", _lockMesh, material, new Vector3(0f, 0f, half), Vector3.one, 180f));
            }
            else
            {
                hull.Add(Part(t, "Strut", set.Strut, material, Vector3.zero, stretch));
                if (set.Plate != null)
                {
                    hull.Add(Part(t, "PlateA", set.Plate, material, new Vector3(0f, 0f, -half), Vector3.one));
                    hull.Add(Part(t, "PlateB", set.Plate, material, new Vector3(0f, 0f, half), Vector3.one, 180f));
                }
                if (set.VRing != null)
                    hull.Add(Part(t, "VRing", set.VRing, material, Vector3.zero, Vector3.one));
            }
        }

        /// <summary>통로 루트: A·B 표면 사이 중앙, 로컬 Z = A→B 방향. 돌려주는 값 = 통로 길이.</summary>
        private static float Place(Transform t, ConnectorSpec spec, bool deck, bool sloped)
        {
            Vector3 dir = spec.Direction;
            if (sloped)
            {
                // ⑪ 끝마다 제 높이 · 깊이 (받침 위에 내려앉은 끝 = 음수 높이), 두 끝 사이를 비스듬히
                Vector3 a = GridConfig.CellToWorld(spec.CellA) + dir * spec.EndDepthA + Vector3.up * spec.HeightA;
                Vector3 b = GridConfig.CellToWorld(spec.CellB) - dir * spec.EndDepthB + Vector3.up * spec.HeightB;
                Vector3 axis = b - a;
                t.SetPositionAndRotation((a + b) * 0.5f, Quaternion.LookRotation(axis.normalized, Vector3.up));
                return axis.magnitude;
            }
            Vector3 position = GridConfig.CellToWorld(spec.CellA) + dir * spec.CenterOffsetFor(deck);
            Vector3 up = spec.IsVertical ? Vector3.forward : Vector3.up;
            t.SetPositionAndRotation(position, Quaternion.LookRotation(dir, up));
            return spec.LengthFor(deck);
        }

        /// <summary>⑪ 비스듬한 통로의 끝 칼라는 기울이지 않고 모듈 벽에 맞춰 세움 (A = 면 방향, B = 반대).</summary>
        private static void KeepCollarsUpright(Transform root, Vector3Int direction)
        {
            Vector3 dir = direction;
            foreach (Transform child in root)
            {
                if (child.name == "CollarA")
                    child.rotation = Quaternion.LookRotation(dir, Vector3.up);
                else if (child.name == "CollarB")
                    child.rotation = Quaternion.LookRotation(-dir, Vector3.up);
            }
        }

        private static Renderer Part(Transform parent, string name, Mesh mesh, Material material, Vector3 localPosition, Vector3 localScale, float yaw = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = localScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return r;
        }

        // ---------------- 상태 ----------------

        /// <summary>코어에서의 거리(통로 수)로 흐름 방향, 연결 여부로 밝기.</summary>
        private void RefreshStates()
        {
            ComputeDistances();
            var grid = _station.Grid;
            foreach (var c in _connectors.Values)
            {
                grid.TryGetModule(c.Spec.CellA, out var a);
                grid.TryGetModule(c.Spec.CellB, out var b);
                bool active = _station.Connectivity.IsActive(a) && _station.Connectivity.IsActive(b);
                float flow = 0f;
                if (active && _distance.TryGetValue(a, out int da) && _distance.TryGetValue(b, out int db))
                    flow = da == db ? 0f : da < db ? 1f : -1f; // A가 코어에 가까우면 A→B(+Z)로 흐름
                if (c.Active == active && Mathf.Approximately(c.Flow, flow))
                    continue;
                c.Active = active;
                c.Flow = flow;
                Apply(c);
            }
        }

        private void Apply(Connector c)
        {
            bool active = c.Active == true;
            foreach (var r in c.Strips)
            {
                r.GetPropertyBlock(_block);
                _block.SetFloat(LengthId, Mathf.Max(0.01f, c.Length));
                _block.SetFloat(FlowId, c.Flow);
                _block.SetFloat(EnergyId, active ? 1f : 0f);
                r.SetPropertyBlock(_block);
            }
            foreach (var r in c.Hull)
            {
                if (active)
                {
                    r.SetPropertyBlock(null);
                    continue;
                }
                _block.Clear();
                _block.SetColor(BaseColorId, Color.white * _inactiveBrightness);
                _block.SetColor(EmissionColorId, Color.black);
                r.SetPropertyBlock(_block);
            }
        }

        /// <summary>코어에서 면 인접으로 몇 단계인지 (흐름 방향용).</summary>
        private void ComputeDistances()
        {
            _distance.Clear();
            var core = _station.Core;
            if (core == null)
                return;
            _distance[core] = 0;
            _queue.Clear();
            _queue.Enqueue(core);
            var grid = _station.Grid;
            while (_queue.Count > 0)
            {
                var current = _queue.Dequeue();
                int next = _distance[current] + 1;
                foreach (var cell in current.Cells)
                {
                    foreach (var dir in GridDirections.Faces)
                    {
                        if (!grid.TryGetModule(cell + dir, out var other) || other == current || _distance.ContainsKey(other))
                            continue;
                        if (!_station.Connectivity.IsActive(other))
                            continue;
                        _distance[other] = next;
                        _queue.Enqueue(other);
                    }
                }
            }
        }

        // ---------------- 배치 미리보기 ----------------

        /// <summary>배치할 모듈이 이웃과 만들 통로를 홀로그램으로 표시 (색은 고스트와 같게).</summary>
        public void ShowPreview(ModuleData data, Vector3Int origin, int rotation, Color color, ModuleInstance ignore = null)
        {
            if (_station == null || _ghostRoot == null)
                return;
            ConnectorLayout.ForPlacement(_station.Grid, data, origin, rotation, _specs, ignore);
            // 개수가 바뀌면 다시 만들고, 같으면 위치만 갱신 (놓을 모듈이 바뀌면 단계 모양이 달라지므로 다시)
            if (_ghosts.Count != _specs.Count || data != _ghostData || NeedsRebuild())
            {
                HidePreview();
                _ghostData = data;
                _ignorePreview = ignore;
                foreach (var spec in _specs)
                    _ghosts.Add(Build(spec, _ghostRoot, true, data));
                _ignorePreview = null;
            }
            _block.Clear();
            _block.SetColor(BaseColorId, color);
            foreach (var g in _ghosts)
            {
                foreach (var r in g.Hull)
                    r.SetPropertyBlock(_block);
                foreach (var r in g.Strips)
                    r.SetPropertyBlock(_block);
            }
            _ghostRoot.gameObject.SetActive(true);
        }

        private bool NeedsRebuild()
        {
            for (int i = 0; i < _specs.Count; i++)
            {
                var a = _ghosts[i].Spec;
                var b = _specs[i];
                if (a.CellA != b.CellA || a.Direction != b.Direction || !Mathf.Approximately(a.Length, b.Length) || !Mathf.Approximately(a.DeckLength, b.DeckLength)
                    || !Mathf.Approximately(a.HeightA, b.HeightA) || !Mathf.Approximately(a.HeightB, b.HeightB)
                    || !Mathf.Approximately(a.EndDepthA, b.EndDepthA) || !Mathf.Approximately(a.EndDepthB, b.EndDepthB))
                    return true;
            }
            return false;
        }

        private ModuleData _ghostData;

        public void HidePreview()
        {
            foreach (var g in _ghosts)
                Destroy(g.Root);
            _ghosts.Clear();
            _ghostData = null;
            if (_ghostRoot != null)
                _ghostRoot.gameObject.SetActive(false);
        }
    }
}
