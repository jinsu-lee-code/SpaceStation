using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Building
{
    /// <summary>
    /// 12-3c 유성우 방향 예보 표시 (그레이박스): 오는 방향에서 보이는 칸의 그 방향 면을 붉은 반투명 판으로 깜빡이고,
    /// 정거장 바깥 그 방향에서 붉은 줄기가 정거장 쪽으로 흘러옴. 경보 · 유성우 중에만 보임.
    /// </summary>
    public sealed class MeteorShowerZoneView : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private StationController _station;
        [Tooltip("반투명 재질 (M_DefenseRange)")]
        [SerializeField] private Material _material;
        [SerializeField] private Color _faceColor = new Color(1f, 0.18f, 0.08f, 0.32f);
        [SerializeField] private Color _streakColor = new Color(1f, 0.4f, 0.15f, 0.22f);
        [SerializeField] private int _streaks = 14;
        [SerializeField] private float _streakLength = 2.4f;

        private MeteorShowerSystem _shower;
        private MeshRenderer _faces;
        private Mesh _mesh;
        private MaterialPropertyBlock _block;
        private readonly List<Transform> _streakObjects = new List<Transform>();
        private readonly List<Vector3> _streakOffsets = new List<Vector3>();
        private readonly List<Vector3> _verts = new List<Vector3>();
        private readonly List<int> _tris = new List<int>();
        private bool _dirty = true;
        private Vector3 _center;
        private float _extent;

        private void Start()
        {
            _shower = _station != null && _station.Simulation != null ? _station.Simulation.MeteorShower : null;
            if (_shower == null || _material == null)
            {
                enabled = false;
                return;
            }
            _block = new MaterialPropertyBlock();
            _mesh = new Mesh { name = "ShowerFaces" };
            var go = new GameObject("Faces");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _faces = go.AddComponent<MeshRenderer>();
            _faces.sharedMaterial = _material;
            _faces.shadowCastingMode = ShadowCastingMode.Off;
            _faces.receiveShadows = false;
            var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            for (int i = 0; i < _streaks; i++)
            {
                var s = new GameObject("Streak");
                s.transform.SetParent(transform, false);
                s.AddComponent<MeshFilter>().sharedMesh = cube;
                var r = s.AddComponent<MeshRenderer>();
                r.sharedMaterial = _material;
                r.shadowCastingMode = ShadowCastingMode.Off;
                _block.SetColor(BaseColorId, _streakColor);
                r.SetPropertyBlock(_block);
                _streakObjects.Add(s.transform);
                _streakOffsets.Add(new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.value));
            }
            _shower.Changed += MarkDirty;
            SetVisible(false);
        }

        private void OnDestroy()
        {
            if (_shower != null)
                _shower.Changed -= MarkDirty;
        }

        private void MarkDirty() => _dirty = true;

        private void SetVisible(bool on)
        {
            if (_faces.gameObject.activeSelf != on)
                _faces.gameObject.SetActive(on);
            foreach (var s in _streakObjects)
                if (s.gameObject.activeSelf != on)
                    s.gameObject.SetActive(on);
        }

        private void LateUpdate()
        {
            bool on = _shower.Active && !_station.Simulation.Session.IsGameOver;
            SetVisible(on);
            if (!on)
                return;
            if (_dirty)
            {
                _dirty = false;
                Rebuild();
            }
            float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3f));
            var c = _faceColor;
            _block.SetColor(BaseColorId, new Color(c.r, c.g, c.b, c.a * pulse));
            _faces.SetPropertyBlock(_block);
            AnimateStreaks();
        }

        private void Rebuild()
        {
            Vector3 dir = _shower.Direction;
            float size = GridConfig.CellSize;
            Vector3 t1 = Vector3.Cross(dir, Mathf.Abs(dir.y) > 0.5f ? Vector3.right : Vector3.up).normalized;
            Vector3 t2 = Vector3.Cross(dir, t1).normalized;
            _verts.Clear();
            _tris.Clear();
            var cells = _shower.FacingCells;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            foreach (var cell in cells)
            {
                Vector3 p = GridConfig.CellToWorld(cell) + dir * (size * 0.52f);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
                float h = size * 0.48f;
                int b = _verts.Count;
                _verts.Add(p + (-t1 - t2) * h);
                _verts.Add(p + (t1 - t2) * h);
                _verts.Add(p + (t1 + t2) * h);
                _verts.Add(p + (-t1 + t2) * h);
                // 양면 (어느 쪽에서 봐도 보이게)
                _tris.Add(b); _tris.Add(b + 1); _tris.Add(b + 2);
                _tris.Add(b); _tris.Add(b + 2); _tris.Add(b + 3);
                _tris.Add(b); _tris.Add(b + 2); _tris.Add(b + 1);
                _tris.Add(b); _tris.Add(b + 3); _tris.Add(b + 2);
            }
            _mesh.Clear();
            _mesh.SetVertices(_verts);
            _mesh.SetTriangles(_tris, 0);
            _mesh.RecalculateBounds();
            if (cells.Count > 0)
            {
                _center = (min + max) * 0.5f;
                _extent = Mathf.Max(1.5f, (max - min).magnitude * 0.5f);
            }
        }

        private void AnimateStreaks()
        {
            Vector3 dir = _shower.Direction;
            Vector3 t1 = Vector3.Cross(dir, Mathf.Abs(dir.y) > 0.5f ? Vector3.right : Vector3.up).normalized;
            Vector3 t2 = Vector3.Cross(dir, t1).normalized;
            var rot = Quaternion.LookRotation(-dir, Mathf.Abs(dir.y) > 0.5f ? Vector3.forward : Vector3.up);
            float speed = _shower.Phase == MeteorShowerSystem.ShowerPhase.Falling ? 1.4f : 0.45f;
            for (int i = 0; i < _streakObjects.Count; i++)
            {
                var o = _streakOffsets[i];
                float k = Mathf.Repeat(o.z + Time.time * speed * 0.25f, 1f); // 0 = 먼 곳 → 1 = 정거장 앞
                Vector3 lateral = (t1 * o.x + t2 * o.y) * _extent;
                Vector3 pos = _center + lateral + dir * (_extent + 1f + (1f - k) * 8f);
                var s = _streakObjects[i];
                s.position = pos;
                s.rotation = rot;
                s.localScale = new Vector3(0.06f, 0.06f, _streakLength) * Mathf.Sin(k * Mathf.PI);
            }
        }
    }
}
