using System.Collections.Generic;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.UI
{
    /// <summary>
    /// 14-1d 4장 위기 '파편 띠' 화면 (사용자 결정 2026-10-26: 도는 파편 고리 + 막힌 칸 사유). 띠가 정거장을 감싸는 동안
    /// (위기 = 파편 띠 + 칸 제한이 걸려 있는 동안) 정거장 둘레를 천천히 도는 바위 · 선체 조각 고리 — 감쌀 때 바깥에서 모여들고,
    /// 띠가 지나가면 바깥으로 흩어진다. 1인칭 내부에서는 숨김. 내용은 코드로 만든다 (탐사 지점의 바위 메시 · 재질).
    /// 막힌 칸 사유는 상태 줄(공통 M-53)이 보여 준다.
    /// </summary>
    public sealed class DebrisBandView : MonoBehaviour
    {
        [SerializeField] private StationController _station;
        [Tooltip("바위 재질 (배경 소행성 재질)")]
        [SerializeField] private Material _rockMaterial;
        [Tooltip("선체 조각 재질 (M_ExploreHull)")]
        [SerializeField] private Material _hullMaterial;
        [Tooltip("조각 수")]
        [SerializeField, Min(8)] private int _pieces = 160;
        [Tooltip("정거장 끝에서 고리 가운데까지 (칸)")]
        [SerializeField, Min(0f)] private float _margin = 4f;
        [Tooltip("고리 두께 (칸, 안팎으로 흩어진 폭)")]
        [SerializeField, Min(0f)] private float _thickness = 3f;
        [Tooltip("고리 높이 폭 (칸)")]
        [SerializeField, Min(0f)] private float _height = 2.5f;
        [Tooltip("공전 속도 (도/초)")]
        [SerializeField] private float _orbitSpeed = 6f;
        [Tooltip("모여들기 · 흩어지기 (초)")]
        [SerializeField, Min(0.1f)] private float _gatherSeconds = 2.5f;
        [Tooltip("흩어질 때 바깥으로 더 나가는 거리 (칸)")]
        [SerializeField, Min(0f)] private float _scatterDistance = 12f;

        private StationSimulation _sim;
        private Interior.InteriorMode _interior;
        private Transform _ring;
        private readonly List<Piece> _list = new List<Piece>();
        private readonly List<Mesh> _meshes = new List<Mesh>();
        private float _shown; // 0 = 없음 · 1 = 감쌈
        private bool _active;

        private sealed class Piece
        {
            public Transform T;
            public Vector3 Home;
            public Vector3 Axis;
            public float Spin;
            public Vector3 Size;
        }

        /// <summary>지금 띠가 정거장을 감싸고 있는지.</summary>
        public bool BandActive => _sim != null && _sim.Chapters != null && _sim.Chapters.Phase == ChapterPhase.Climax
            && _sim.Chapters.Current.Climax.Kind == ChapterClimaxKind.DebrisBand && _sim.BuildZone != null;

        private void Start()
        {
            _sim = _station != null ? _station.Simulation : null;
            if (_sim == null || _sim.Chapters == null || _rockMaterial == null)
            {
                enabled = false;
                return;
            }
            _interior = FindFirstObjectByType<Interior.InteriorMode>(FindObjectsInactive.Include);
            Build();
            _ring.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            foreach (var m in _meshes)
                Destroy(m);
            _meshes.Clear();
        }

        private void Build()
        {
            _ring = new GameObject("DebrisRing").transform;
            _ring.SetParent(transform, false);
            for (int i = 0; i < 6; i++)
                _meshes.Add(RockMesh.Create(9173 + i * 31, 0.4f, 1));
            var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var rng = new System.Random(4021);
            float R() => (float)rng.NextDouble();
            for (int i = 0; i < _pieces; i++)
            {
                bool metal = _hullMaterial != null && i % 4 == 0;
                var go = new GameObject(metal ? "Shard" : "Rock");
                go.transform.SetParent(_ring, false);
                go.AddComponent<MeshFilter>().sharedMesh = metal ? cube : _meshes[i % _meshes.Count];
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = metal ? _hullMaterial : _rockMaterial;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                var p = new Piece
                {
                    T = go.transform,
                    Axis = new Vector3(R() - 0.5f, R() - 0.5f, R() - 0.5f).normalized,
                    Spin = 15f + R() * 45f,
                    Size = metal ? new Vector3(0.35f + R() * 0.35f, 0.05f, 0.12f + R() * 0.18f) : Vector3.one * (0.2f + R() * 0.45f),
                };
                go.transform.localRotation = Quaternion.Euler(R() * 360f, R() * 360f, R() * 360f);
                _list.Add(p);
            }
        }

        /// <summary>띠가 감쌀 때: 지금 정거장 크기에 맞춰 고리 자리를 정함 (조각마다 각도 · 반지름 · 높이는 고정 순서).</summary>
        private void PlaceAround()
        {
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach (var m in _sim.Grid.Modules)
                foreach (var c in m.Cells)
                {
                    var w = GridConfig.CellToWorld(c);
                    min = Vector3.Min(min, w);
                    max = Vector3.Max(max, w);
                }
            if (min.x > max.x)
                min = max = Vector3.zero;
            var center = (min + max) * 0.5f;
            float half = Mathf.Max(max.x - min.x, max.z - min.z) * 0.5f;
            float radius = half + _margin * GridConfig.CellSize;
            _ring.position = center;
            var rng = new System.Random(7351);
            float R() => (float)rng.NextDouble();
            foreach (var p in _list)
            {
                float a = R() * Mathf.PI * 2f;
                float r = radius + (R() - 0.5f) * _thickness * GridConfig.CellSize;
                float y = (R() - 0.5f) * _height * GridConfig.CellSize;
                p.Home = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
            }
        }

        private void Update()
        {
            if (_ring == null)
                return;
            bool band = BandActive;
            if (band && !_active)
            {
                _active = true;
                if (_shown <= 0f)
                    PlaceAround();
            }
            else if (!band && _active)
                _active = false;

            float dt = Time.unscaledDeltaTime;
            _shown = Mathf.MoveTowards(_shown, _active ? 1f : 0f, dt / _gatherSeconds);
            bool inside = _interior != null && _interior.IsInside;
            bool visible = _shown > 0f && !inside;
            if (_ring.gameObject.activeSelf != visible)
                _ring.gameObject.SetActive(visible);
            if (!visible)
                return;

            _ring.Rotate(Vector3.up, _orbitSpeed * dt, Space.World);
            float e = Mathf.SmoothStep(0f, 1f, _shown);
            float scatter = (1f - e) * _scatterDistance * GridConfig.CellSize;
            foreach (var p in _list)
            {
                var flat = new Vector3(p.Home.x, 0f, p.Home.z);
                p.T.localPosition = p.Home + (flat.sqrMagnitude > 1e-4f ? flat.normalized * scatter : Vector3.zero);
                p.T.localScale = p.Size * e;
                p.T.Rotate(p.Axis, p.Spin * dt, Space.Self);
            }
        }
    }
}
