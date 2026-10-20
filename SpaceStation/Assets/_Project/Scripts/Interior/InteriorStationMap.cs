using System.Collections.Generic;
using SpaceStation.Core;
using TMPro;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 12-5 행정 관제소 관제실 모듈 상태 지도 벽 — 사용자 결정 2026-10-20: 실제 정거장.
    /// 이 모듈과 같은 층의 모듈 칸을 위에서 내려다본 모양으로 그린다 (지도 위 = 화면을 보는 쪽 앞, 지도 오른쪽 = 보는 사람 오른쪽 → 모듈과 같이 돎).
    /// 한 모듈의 칸끼리는 붙이고 모듈 사이만 틈. 상태는 색이 아니라 밝기 · 움직임으로도 구분 (색약):
    ///   여기 = 강조색 · 천천히 숨 쉬듯 / 정상 = 밝음 / 꺼짐 = 어두움 / 고장 = 밝음 ↔ 어두움 깜빡임.
    /// 범례 견본도 같은 메시에 넣어 똑같이 움직임. 모듈 · 상태가 바뀔 때만 메시를 다시 만든다. 이 모듈이 꺼지면 "전원 꺼짐".
    /// </summary>
    public sealed class InteriorStationMap : InteriorRoomDisplay
    {
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private const float Gap = 0.09f;        // 모듈 사이 틈 (칸 크기 비율)
        private const float MaxCell = 0.28f;    // 칸 하나 최대 크기 (m) — 모듈이 몇 개 없을 때
        private const float Lift = 0.006f;      // 화면 면에서 (바탕 격자 윗면 +0.0025보다 3.5mm 위 — 가까우면 겹친 자리가 깜빡임)
        private const float TitleBand = 0.26f;  // 위 제목 줄 높이
        private const float LegendBand = 0.22f; // 아래 범례 줄 높이
        private const float Margin = 0.1f;

        private enum State { Here, On, Off, Broken }
        private const int StateCount = 4;

        [SerializeField] private Vector3 _center;
        [SerializeField] private Vector3 _normal = Vector3.right;
        [SerializeField] private Vector3 _up = Vector3.up;
        [SerializeField] private Vector2 _size = new Vector2(4.8f, 1.8f);
        [SerializeField] private Material _on;
        [SerializeField] private Material _off;
        [SerializeField] private Material _here;

        private readonly Mesh[] _meshes = new Mesh[StateCount];
        private readonly MeshRenderer[] _renderers = new MeshRenderer[StateCount];
        private readonly List<Vector3> _verts = new List<Vector3>();
        private readonly List<int> _tris = new List<int>();
        private readonly List<Vector3> _norms = new List<Vector3>();
        private readonly Dictionary<Vector2Int, ModuleInstance> _cells = new Dictionary<Vector2Int, ModuleInstance>();
        private readonly Dictionary<ModuleInstance, State> _states = new Dictionary<ModuleInstance, State>();
        private readonly List<State> _signature = new List<State>();
        private readonly List<State> _scratch = new List<State>();
        private TextMeshPro _title, _summary;
        private readonly TextMeshPro[] _legend = new TextMeshPro[StateCount];
        private MaterialPropertyBlock _block;
        private Color _hereEmission;
        private Vector3 _right;
        private int _version = -1;
        private bool _shown;
        private bool _built;

        public void Configure(Vector3 center, Vector3 normal, Vector3 up, Vector2 size, Material on, Material off, Material here)
        {
            _center = center;
            _normal = normal;
            _up = up;
            _size = size;
            _on = on;
            _off = off;
            _here = here;
        }

        private void Setup()
        {
            if (_block != null)
                return;
            _block = new MaterialPropertyBlock();
            _normal = _normal.normalized;
            _up = _up.normalized;
            _right = Vector3.Cross(_normal, _up);   // 보는 사람 오른쪽 (CreateText 글자 방향과 같음)
            _hereEmission = _here != null && _here.HasProperty(EmissionColorId) ? _here.GetColor(EmissionColorId) : Color.white;
            string[] names = { "MapHere", "MapOn", "MapOff", "MapBroken" };
            Material[] mats = { _here, _on, _off, _on };
            for (int i = 0; i < StateCount; i++)
            {
                var go = new GameObject(names[i]);
                go.transform.SetParent(transform, false);
                _meshes[i] = new Mesh { name = names[i] };
                go.AddComponent<MeshFilter>().sharedMesh = _meshes[i];
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mats[i];
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                _renderers[i] = r;
            }
            float w = _size.x, h = _size.y;
            float ty = h / 2f - TitleBand / 2f;
            _title = CreateText("MapTitle", At(-w / 4f, ty), _normal, _up, w / 2f - Margin * 2f, TitleBand - 0.08f, 1.6f, TextAlignmentOptions.Left);
            _summary = CreateText("MapSummary", At(w / 4f, ty), _normal, _up, w / 2f - Margin * 2f, TitleBand - 0.1f, 1.2f, TextAlignmentOptions.Right);
            string[] labels = { "여기", "정상", "꺼짐", "고장 (깜빡임)" };
            for (int i = 0; i < StateCount; i++)
            {
                float u = LegendU(i);
                _legend[i] = CreateText("MapLegend", At(u + 0.08f + 0.3f, LegendV()), _normal, _up, 0.6f, LegendBand - 0.1f, 1.0f, TextAlignmentOptions.Left);
                _legend[i].SetText(labels[i]);
            }
        }

        private Vector3 At(float u, float v) => _center + _right * u + _up * v;
        private float LegendU(int i) => -_size.x / 2f + Margin + 0.06f + i * 0.85f;
        private float LegendV() => -_size.y / 2f + LegendBand / 2f;

        protected override void Refresh()
        {
            Setup();
            var grid = Station != null ? Station.Grid : null;
            if (!Active || grid == null || Module == null)
            {
                Show(false);
                SetText(_title, Active ? "<b>정거장 모듈 지도</b>" : "<b>전원 꺼짐</b>", ScreenDim);
                SetText(_summary, "", ScreenDim);
                _version = -1;   // 다시 켜지면 제목 · 요약까지 새로
                return;
            }
            // 상태 모으기 (모듈 순서대로) → 지난번과 같으면 메시 그대로
            var damage = Station.Simulation != null ? Station.Simulation.Damage : null;
            var connectivity = Station.Connectivity;
            _scratch.Clear();
            foreach (var m in grid.Modules)
            {
                State s = m == Module ? State.Here
                    : damage != null && damage.TryGetInfo(m, out _) ? State.Broken
                    : connectivity != null && !connectivity.IsActive(m) ? State.Off
                    : State.On;
                _scratch.Add(s);
            }
            bool same = _built && _version == grid.Version && _scratch.Count == _signature.Count;
            for (int i = 0; same && i < _scratch.Count; i++)
                same = _scratch[i] == _signature[i];
            if (!same)
            {
                _signature.Clear();
                _signature.AddRange(_scratch);
                _version = grid.Version;
                Rebuild(grid);
            }
            Show(true);
        }

        private void Show(bool on)
        {
            if (_shown == on && _built)
                return;
            _shown = on;
            foreach (var r in _renderers)
            {
                if (r != null)
                    r.enabled = on;
            }
            foreach (var t in _legend)
            {
                if (t != null)
                    t.gameObject.SetActive(on);
            }
        }

        private void Rebuild(StationGrid grid)
        {
            _built = true;
            _cells.Clear();
            _states.Clear();
            int floor = Module.Origin.y;
            int otherFloors = 0, off = 0, broken = 0;
            int i = 0;
            foreach (var m in grid.Modules)
            {
                var s = _signature[i++];
                bool onFloor = false;
                foreach (var c in m.Cells)
                {
                    if (c.y != floor)
                        continue;
                    onFloor = true;
                    // 이 모듈 로컬 칸 → 지도 (오른쪽 = 로컬 +z, 위 = 로컬 −x: 관제실에서 지도 벽을 보는 쪽)
                    var local = GridDirections.Rotate(c - Module.Origin, -Module.Rotation);
                    _cells[new Vector2Int(local.z, -local.x)] = m;
                }
                if (!onFloor)
                {
                    otherFloors++;
                    continue;
                }
                _states[m] = s;
                if (s == State.Off)
                    off++;
                else if (s == State.Broken)
                    broken++;
            }
            // 칸 범위 → 지도 칸 크기 · 가운데
            var min = new Vector2Int(int.MaxValue, int.MaxValue);
            var max = new Vector2Int(int.MinValue, int.MinValue);
            foreach (var p in _cells.Keys)
            {
                min = Vector2Int.Min(min, p);
                max = Vector2Int.Max(max, p);
            }
            float areaW = _size.x - Margin * 2f;
            float areaH = _size.y - TitleBand - LegendBand;
            float areaV = (LegendBand - TitleBand) / 2f;   // 지도 칸 가운데 높이
            float cs = MaxCell;
            if (_cells.Count > 0)
                cs = Mathf.Min(MaxCell, areaW / (max.x - min.x + 1), areaH / (max.y - min.y + 1));
            var mid = _cells.Count > 0 ? new Vector2((min.x + max.x) / 2f, (min.y + max.y) / 2f) : Vector2.zero;
            for (int k = 0; k < StateCount; k++)
            {
                _verts.Clear();
                _tris.Clear();
                foreach (var pair in _cells)
                {
                    if (!_states.TryGetValue(pair.Value, out var s) || (int)s != k)
                        continue;
                    var p = pair.Key;
                    // 같은 모듈 이웃 쪽은 틈 없이 붙임
                    float l = Joined(p, Vector2Int.left, pair.Value) ? 0f : Gap;
                    float r = Joined(p, Vector2Int.right, pair.Value) ? 0f : Gap;
                    float b = Joined(p, Vector2Int.down, pair.Value) ? 0f : Gap;
                    float t = Joined(p, Vector2Int.up, pair.Value) ? 0f : Gap;
                    float u0 = (p.x - mid.x - 0.5f + l) * cs, u1 = (p.x - mid.x + 0.5f - r) * cs;
                    float v0 = areaV + (p.y - mid.y - 0.5f + b) * cs, v1 = areaV + (p.y - mid.y + 0.5f - t) * cs;
                    Quad(u0, v0, u1, v1);
                }
                // 범례 견본
                float lu = LegendU(k), lv = LegendV();
                Quad(lu - 0.06f, lv - 0.05f, lu + 0.06f, lv + 0.05f);
                var mesh = _meshes[k];
                mesh.Clear();
                mesh.SetVertices(_verts);
                _norms.Clear();
                for (int n = 0; n < _verts.Count; n++)
                    _norms.Add(_normal);
                mesh.SetNormals(_norms);
                mesh.SetTriangles(_tris, 0);
                mesh.RecalculateBounds();
            }
            SetText(_title, "<b>정거장 모듈 지도</b>", ScreenText);
            string summary = $"모듈 {_states.Count} · 꺼짐 {off} · 고장 {broken}";
            if (otherFloors > 0)
                summary += $" · 다른 층 {otherFloors}";
            SetText(_summary, summary, ScreenText);
        }

        private bool Joined(Vector2Int p, Vector2Int dir, ModuleInstance m) => _cells.TryGetValue(p + dir, out var other) && other == m;

        private void Quad(float u0, float v0, float u1, float v1)
        {
            int i = _verts.Count;
            var lift = _normal * Lift;
            _verts.Add(At(u0, v0) + lift);
            _verts.Add(At(u1, v0) + lift);
            _verts.Add(At(u1, v1) + lift);
            _verts.Add(At(u0, v1) + lift);
            // 앞면 = 보는 사람 쪽 (_normal)
            _tris.Add(i); _tris.Add(i + 2); _tris.Add(i + 1);
            _tris.Add(i); _tris.Add(i + 3); _tris.Add(i + 2);
        }

        private void Update()
        {
            if (!_built || !_shown)
                return;
            float t = Time.unscaledTime;
            // 여기 = 천천히 숨 쉬듯 (2.4초)
            var here = _renderers[(int)State.Here];
            _block.Clear();
            _block.SetColor(EmissionColorId, _hereEmission * (1.05f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f / 2.4f)));
            here.SetPropertyBlock(_block);
            // 고장 = 밝음 0.35초 ↔ 어두움 0.25초
            var broken = _renderers[(int)State.Broken];
            var mat = Mathf.Repeat(t, 0.6f) < 0.35f ? _on : _off;
            if (broken.sharedMaterial != mat)
                broken.sharedMaterial = mat;
        }

        private void OnDestroy()
        {
            foreach (var m in _meshes)
            {
                if (m != null)
                    Destroy(m);
            }
        }
    }
}
