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
    /// 메시·재질은 StationArtBuilder(Connectors)가 배선한다.
    /// </summary>
    public sealed class StationConnectors : MonoBehaviour
    {
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

        [Header("Look")]
        [Tooltip("불빛 띠 위치 (통로 중심에서 위쪽으로)")]
        [SerializeField] private float _stripOffset = 0.088f;
        [SerializeField, Range(0f, 1f)] private float _inactiveBrightness = 0.35f;

        private sealed class Connector
        {
            public GameObject Root;
            public ConnectorSpec Spec;
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
            _station.Connectivity.ActiveStateChanged += HandleActiveChanged;
            _stateDirty = true;
        }

        private void OnDestroy()
        {
            if (_station == null || _station.Simulation == null)
                return;
            _station.Grid.ModulePlaced -= AddFor;
            _station.Grid.ModuleRemoved -= RemoveFor;
            _station.Connectivity.ActiveStateChanged -= HandleActiveChanged;
        }

        private void HandleActiveChanged(ModuleInstance module, bool active) => _stateDirty = true;

        /// <summary>11-8 바깥 창 카메라: 모듈(칸 목록)에 닿은 통로 렌더러를 모음 (창 바로 앞을 통로 끝이 가리지 않게).</summary>
        public void CollectRenderersTouching(IEnumerable<Vector3Int> cells, List<Renderer> into)
        {
            foreach (var pair in _connectors)
            {
                bool touches = false;
                foreach (var cell in cells)
                {
                    if (pair.Key.Item1 == cell || pair.Key.Item2 == cell)
                    {
                        touches = true;
                        break;
                    }
                }
                if (!touches || pair.Value.Root == null)
                    continue;
                into.AddRange(pair.Value.Hull);
                into.AddRange(pair.Value.Strips);
            }
        }

        /// <summary>11-8 바깥 창 카메라: 경계 상자(여유 margin)가 점을 품은 통로 렌더러를 모음 (통로 속에서 자기 껍데기를 숨기려고).</summary>
        public void CollectRenderersContaining(Vector3 point, float margin, List<Renderer> into)
        {
            foreach (var c in _connectors.Values)
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

        private void RemoveFor(ModuleInstance module)
        {
            _removeKeys.Clear();
            var cells = new HashSet<Vector3Int>(module.Cells);
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

        private Connector Build(ConnectorSpec spec, Transform parent, bool ghost)
        {
            var c = new Connector { Spec = spec };
            c.Root = new GameObject(ghost ? "GhostConnector" : $"Connector {spec.CellA}->{spec.CellB}");
            c.Root.transform.SetParent(parent, false);
            var hull = new List<Renderer>();
            var strips = new List<Renderer>();
            Material hullMat = ghost ? _ghostMaterial : _hullMaterial;
            Material stripMat = ghost ? _ghostMaterial : _flowMaterial;
            Place(c.Root.transform, spec);

            float length = spec.Length;
            float half = length * 0.5f;
            if (!spec.IsVertical)
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

        /// <summary>통로 루트: A·B 표면 사이 중앙, 로컬 Z = A→B 방향.</summary>
        private static void Place(Transform t, ConnectorSpec spec)
        {
            Vector3 dir = spec.Direction;
            Vector3 position = GridConfig.CellToWorld(spec.CellA) + dir * spec.CenterOffset;
            Vector3 up = spec.IsVertical ? Vector3.forward : Vector3.up;
            t.SetPositionAndRotation(position, Quaternion.LookRotation(dir, up));
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
                _block.SetFloat(LengthId, Mathf.Max(0.01f, c.Spec.Length));
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
        public void ShowPreview(ModuleData data, Vector3Int origin, int rotation, Color color)
        {
            if (_station == null || _ghostRoot == null)
                return;
            ConnectorLayout.ForPlacement(_station.Grid, data, origin, rotation, _specs);
            // 개수가 바뀌면 다시 만들고, 같으면 위치만 갱신
            if (_ghosts.Count != _specs.Count || NeedsRebuild())
            {
                HidePreview();
                foreach (var spec in _specs)
                    _ghosts.Add(Build(spec, _ghostRoot, true));
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
                if (a.CellA != b.CellA || a.Direction != b.Direction || !Mathf.Approximately(a.Length, b.Length))
                    return true;
            }
            return false;
        }

        public void HidePreview()
        {
            foreach (var g in _ghosts)
                Destroy(g.Root);
            _ghosts.Clear();
            if (_ghostRoot != null)
                _ghostRoot.gameObject.SetActive(false);
        }
    }
}
