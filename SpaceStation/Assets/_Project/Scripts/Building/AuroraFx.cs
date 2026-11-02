using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Building
{
    /// <summary>오로라 띠 하나: 길을 따라 바닥선이 놓이고 위로 Height만큼 드리움 (모델 기준).</summary>
    [Serializable]
    public struct AuroraRibbon
    {
        public Vector3[] Path;
        public float Height;
        [Tooltip("위로 갈수록 띠 면 앞쪽으로 기우는 거리")]
        public float Tilt;

        public AuroraRibbon(Vector3[] path, float height, float tilt)
        {
            Path = path;
            Height = height;
            Tilt = tilt;
        }
    }

    /// <summary>
    /// 실드 돔 · 궤도 방어망 오로라 커튼 (사용자 2026-11-02 "칼로 자른 것처럼 보임 — 부드럽게, 자연의 오로라처럼, 고정된 위치가 아니라 주변 범위에서 일렁이게"):
    /// 띠마다 촘촘한 메시를 만들어 (모델에 구운 얇은 판은 가장자리가 그대로 보이고 UV도 없어 끝을 흐릴 수 없었음) SpaceStation/AuroraCurtain으로 그림 — 움직임 · 부드러움은 셰이더 · 재질 슬라이더.
    /// 띠 하나에 겹 둘 (뒤 겹은 조금 바깥 · 더 높게, 씨앗이 달라 따로 움직여 부피가 생김). 비활성이면 서서히 옅어짐, 파손이면 깜빡임. 고스트(배치 미리보기)에서도 보임.
    /// </summary>
    public sealed class AuroraFx : MonoBehaviour
    {
        private const int Rows = 10;
        private const float SegmentLength = 0.012f;
        private const int MaxSegments = 240;
        private const float BackOffset = 0.03f;     // 뒤 겹이 떨어진 거리
        private const float BackHeight = 1.2f;
        private static readonly int LevelId = Shader.PropertyToID("_Level");

        [SerializeField] private Material _material;
        [SerializeField] private AuroraRibbon[] _ribbons;

        private Mesh _mesh;
        private MeshRenderer _renderer;
        private MaterialPropertyBlock _block;
        private ModuleView _view;
        private bool _live;
        private float _level = 1f;
        private float _shown = -1f;
        private float _seed;

        public int RibbonCount => _ribbons != null ? _ribbons.Length : 0;

        public void Configure(Material material, AuroraRibbon[] ribbons)
        {
            _material = material;
            _ribbons = ribbons;
        }

        private void Start()
        {
            _view = GetComponentInParent<ModuleView>();
            _live = _view != null && _view.Module != null;
            _seed = UnityEngine.Random.value * 100f;
            if (_material == null || _ribbons == null || _ribbons.Length == 0)
                return;
            Build();
            Apply(1f);
        }

        private void Build()
        {
            var positions = new System.Collections.Generic.List<Vector3>();
            var normals = new System.Collections.Generic.List<Vector3>();
            var tangents = new System.Collections.Generic.List<Vector4>();
            var uv = new System.Collections.Generic.List<Vector2>();
            var uv2 = new System.Collections.Generic.List<Vector2>();
            var tris = new System.Collections.Generic.List<int>();
            for (int i = 0; i < _ribbons.Length; i++)
            {
                AddLayer(_ribbons[i], 0f, 1f, i * 0.37f + 0.11f, positions, normals, tangents, uv, uv2, tris);
                AddLayer(_ribbons[i], BackOffset, BackHeight, i * 0.37f + 0.63f, positions, normals, tangents, uv, uv2, tris);
            }
            _mesh = new Mesh { name = "AuroraCurtains" };
            _mesh.indexFormat = positions.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            _mesh.SetVertices(positions);
            _mesh.SetNormals(normals);
            _mesh.SetTangents(tangents);
            _mesh.SetUVs(0, uv);
            _mesh.SetUVs(1, uv2);
            _mesh.SetTriangles(tris, 0);
            _mesh.RecalculateBounds();
            // 꼭짓점이 셰이더에서 움직이므로 넉넉히 (셰이더 슬라이더 최대 움직임)
            var bounds = _mesh.bounds;
            bounds.Expand(0.8f);
            _mesh.bounds = bounds;
            var go = new GameObject("AuroraCurtains");
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = go.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _block = new MaterialPropertyBlock();
        }

        private static void AddLayer(AuroraRibbon ribbon, float offset, float heightScale, float seed,
            System.Collections.Generic.List<Vector3> positions, System.Collections.Generic.List<Vector3> normals, System.Collections.Generic.List<Vector4> tangents,
            System.Collections.Generic.List<Vector2> uv, System.Collections.Generic.List<Vector2> uv2, System.Collections.Generic.List<int> tris)
        {
            var path = ribbon.Path;
            if (path == null || path.Length < 2)
                return;
            float length = 0f;
            for (int i = 1; i < path.Length; i++)
                length += Vector3.Distance(path[i - 1], path[i]);
            int segments = Mathf.Clamp(Mathf.CeilToInt(length / SegmentLength), 8, MaxSegments);
            int start = positions.Count;
            for (int k = 0; k <= segments; k++)
            {
                float along = k / (float)segments;
                Vector3 p = Sample(path, along);
                Vector3 ahead = Sample(path, Mathf.Min(along + 0.004f, 1f)) - Sample(path, Mathf.Max(along - 0.004f, 0f));
                Vector3 tangent = Vector3.ProjectOnPlane(ahead, Vector3.up).normalized;
                Vector3 normal = new Vector3(tangent.z, 0f, -tangent.x);
                for (int r = 0; r <= Rows; r++)
                {
                    float v = r / (float)Rows;
                    positions.Add(p + normal * (offset + ribbon.Tilt * v) + Vector3.up * (ribbon.Height * heightScale * v));
                    normals.Add(normal);
                    tangents.Add(new Vector4(tangent.x, tangent.y, tangent.z, 1f));
                    uv.Add(new Vector2(along, v));
                    uv2.Add(new Vector2(along * length, seed));
                    if (k < segments && r < Rows)
                    {
                        int a = start + k * (Rows + 1) + r;
                        int b = a + Rows + 1;
                        tris.Add(a);
                        tris.Add(b);
                        tris.Add(a + 1);
                        tris.Add(a + 1);
                        tris.Add(b);
                        tris.Add(b + 1);
                    }
                }
            }
        }

        /// <summary>점 줄 위 자리 (Catmull-Rom — 길의 꺾임이 띠에 칼자국으로 남지 않게).</summary>
        private static Vector3 Sample(Vector3[] path, float along)
        {
            float f = Mathf.Clamp01(along) * (path.Length - 1);
            int i = Mathf.Min((int)f, path.Length - 2);
            float t = f - i;
            Vector3 p0 = path[Mathf.Max(i - 1, 0)], p1 = path[i], p2 = path[i + 1], p3 = path[Mathf.Min(i + 2, path.Length - 1)];
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (3f * p1 - p0 - 3f * p2 + p3) * t * t * t);
        }

        private void Update()
        {
            if (_mesh == null || !_live)
                return;
            float target = _view.Operational ? 1f : 0.1f;
            if (_view.DamageVisual == ModuleDamageVisual.Damaged)
                target *= Mathf.PerlinNoise(Time.time * 6f, _seed) > 0.5f ? 1f : 0.2f;
            _level = Mathf.MoveTowards(_level, target, Time.deltaTime * 2f);
            if (Mathf.Abs(_level - _shown) > 0.01f)
                Apply(_level);
        }

        private void Apply(float level)
        {
            _shown = level;
            _block.SetFloat(LevelId, level);
            _renderer.SetPropertyBlock(_block);
        }

        private void OnDestroy()
        {
            if (_mesh != null)
                Destroy(_mesh);
        }
    }
}
