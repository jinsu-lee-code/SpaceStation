using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Building
{
    /// <summary>
    /// 궤도 미러 빛기둥 (사용자 2026-10-27 "중앙탑 빛줄기가 그냥 선처럼 보임 → 광원이 비추는 듯한 진짜 빛줄기",
    /// 2026-11-02 "그냥 선같아보여서 반사광 느낌이 안나" · "빛줄기 시작점 해바라기 표면에서 명확하게 시작하도록"): 거울마다 집광 수신기까지 3D 빛기둥 하나 (SpaceStation/LightShaft).
    /// 아래 테두리 = 거울 테두리 그대로 (거울 면에 기운 원 — 카메라를 향한 띠로는 거울 모양에서 시작할 수 없었음), 위 끝 = 수신기 크기로 모임, 거울 면 자체도 환하게 빛남.
    /// 끝점 · 지름은 모델 기준 — 바깥 모델은 StationArtBuilder가 모델 빛줄기 메시에서 찾아 넣고, 내부 템플릿은 InteriorTemplateBuilder가 넣는다. 비활성이면 서서히 옅어짐, 파손이면 깜빡임.
    /// 광량 · 감쇠 · 빛 결은 재질에서 조절 (바깥 M_LightShaft, 내부 M_InteriorLightShaft — 사용자 2026-11-02 "감쇄 효과를 조절할 수 있게").
    /// </summary>
    public sealed class LightBeamFx : MonoBehaviour
    {
        private const int Around = 28;
        private const int Along = 6;
        private static readonly int LevelId = Shader.PropertyToID("_Level");

        [SerializeField] private Material _material;
        [Tooltip("거울 면 가운데 (모델 기준)")]
        [SerializeField] private Vector3[] _from;
        [Tooltip("수신기 쪽 끝 (모델 기준)")]
        [SerializeField] private Vector3[] _to;
        [Tooltip("거울 지름 (모델 기준, 빛줄기마다)")]
        [SerializeField] private float[] _mirrorDiameters;
        [Tooltip("거울 면 방향. 비우면 해가 바로 위일 때 (수신기 방향과 위의 가운데 — 외형 해바라기와 같은 규칙)")]
        [SerializeField] private Vector3[] _mirrorNormals;
        [Tooltip("수신기 쪽 지름 (빛이 모이는 곳)")]
        [SerializeField] private float _receiverDiameter = 0.08f;

        private Mesh _mesh;
        private Vector3[] _vertices;
        private Vector3[] _normals;
        private MeshRenderer _renderer;
        private MaterialPropertyBlock _block;
        private ModuleView _view;
        private bool _live;
        private bool _dirty;
        private float _level = 1f;
        private float _shown = -1f;
        private float _seed;

        private static int TubeVertices => (Along + 1) * (Around + 1);
        private static int BeamVertices => TubeVertices + Around + 2;   // 기둥 + 거울 면 (가운데 하나 + 테두리)

        public int BeamCount => _mesh != null ? _vertices.Length / BeamVertices : 0;

        private void Start()
        {
            _view = GetComponentInParent<ModuleView>();
            _live = _view != null && _view.Module != null;
            _seed = Random.value * 100f;
            if (_material == null || _from == null || _to == null)
                return;
            Build(Mathf.Min(_from.Length, _to.Length));
            Apply(1f);
        }

        /// <summary>해를 따라 도는 거울 (내부 템플릿 <c>InteriorHeliostat</c>): 이 빛줄기의 시작 면을 옮김.</summary>
        public void SetMirror(int index, Vector3 from, Vector3 normal)
        {
            if (_mesh == null || _from == null || index < 0 || index >= BeamCount)
                return;
            _from[index] = from;
            if (_mirrorNormals == null || _mirrorNormals.Length != _from.Length)
                _mirrorNormals = new Vector3[_from.Length];
            _mirrorNormals[index] = normal;
            FillBeam(index);
            _dirty = true;
        }

        /// <summary>도는 등대 빛 (내부 템플릿 <c>InteriorLighthouse</c>): 이 빛줄기의 끝을 옮김 (방 하늘 면 앞에서 끝나게). 아직 메시가 없으면 false.</summary>
        public bool SetEnd(int index, Vector3 to)
        {
            if (_mesh == null || _to == null || index < 0 || index >= BeamCount)
                return false;
            _to[index] = to;
            FillBeam(index);
            _dirty = true;
            return true;
        }

        private void Build(int count)
        {
            int total = count * BeamVertices;
            _vertices = new Vector3[total];
            _normals = new Vector3[total];
            var uv = new Vector2[total];
            var tris = new int[count * (Along * Around * 6 + Around * 3)];
            int t = 0;
            for (int i = 0; i < count; i++)
            {
                int start = i * BeamVertices;
                for (int k = 0; k <= Along; k++)
                {
                    float along = k / (float)Along;
                    for (int a = 0; a <= Around; a++)
                    {
                        int v = start + k * (Around + 1) + a;
                        uv[v] = new Vector2(along, a / (float)Around);
                        if (k < Along && a < Around)
                        {
                            tris[t++] = v;
                            tris[t++] = v + Around + 1;
                            tris[t++] = v + 1;
                            tris[t++] = v + 1;
                            tris[t++] = v + Around + 1;
                            tris[t++] = v + Around + 2;
                        }
                    }
                }
                // 거울 면: uv.x < 0 = 셰이더가 면으로 그림, uv.y = 가운데 0 ~ 테두리 1
                int center = start + TubeVertices;
                uv[center] = new Vector2(-1f, 0f);
                for (int a = 0; a <= Around; a++)
                {
                    int v = center + 1 + a;
                    uv[v] = new Vector2(-1f, 1f);
                    if (a < Around)
                    {
                        tris[t++] = center;
                        tris[t++] = v + 1;
                        tris[t++] = v;
                    }
                }
                FillBeam(i);
            }
            _mesh = new Mesh { name = "LightShafts" };
            _mesh.indexFormat = total > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            _mesh.vertices = _vertices;
            _mesh.normals = _normals;
            _mesh.uv = uv;
            _mesh.triangles = tris;
            _mesh.RecalculateBounds();
            var go = new GameObject("LightShafts");
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = go.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _block = new MaterialPropertyBlock();
        }

        /// <summary>한 빛줄기의 꼭짓점: 거울 면에 놓인 원 → 수신기 쪽 원 (법선 = 축에서 바깥, 셰이더가 시선과 견줘 가장자리를 흐리게 지움).</summary>
        private void FillBeam(int i)
        {
            Vector3 from = _from[i], to = _to[i];
            Vector3 axis = (to - from).normalized;
            Vector3 normal = _mirrorNormals != null && i < _mirrorNormals.Length && _mirrorNormals[i].sqrMagnitude > 0.5f
                ? _mirrorNormals[i]
                : (axis + Vector3.up).normalized;
            float mirror = (_mirrorDiameters != null && i < _mirrorDiameters.Length ? _mirrorDiameters[i] : _receiverDiameter * 2f) * 0.5f;
            float receiver = _receiverDiameter * 0.5f;
            Vector3 x = Vector3.ProjectOnPlane(axis, normal);
            x = x.sqrMagnitude > 1e-5f ? x.normalized : Vector3.Cross(normal, Mathf.Abs(normal.x) < 0.9f ? Vector3.right : Vector3.forward).normalized;
            Vector3 y = Vector3.Cross(normal, x);
            Vector3 x2 = Vector3.ProjectOnPlane(x, axis);
            x2 = x2.sqrMagnitude > 1e-5f ? x2.normalized : Vector3.Cross(axis, y).normalized;
            Vector3 y2 = Vector3.Cross(axis, x2);
            int start = i * BeamVertices;
            for (int k = 0; k <= Along; k++)
            {
                float along = k / (float)Along;
                Vector3 mid = Vector3.Lerp(from, to, along);
                for (int a = 0; a <= Around; a++)
                {
                    float angle = 2f * Mathf.PI * a / Around;
                    float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
                    Vector3 p = Vector3.Lerp(from + (x * cos + y * sin) * mirror, to + (x2 * cos + y2 * sin) * receiver, along);
                    int v = start + k * (Around + 1) + a;
                    _vertices[v] = p;
                    _normals[v] = Vector3.ProjectOnPlane(p - mid, axis).normalized;
                }
            }
            int center = start + TubeVertices;
            _vertices[center] = from;
            _normals[center] = normal;
            for (int a = 0; a <= Around; a++)
            {
                float angle = 2f * Mathf.PI * a / Around;
                _vertices[center + 1 + a] = from + (x * Mathf.Cos(angle) + y * Mathf.Sin(angle)) * mirror;
                _normals[center + 1 + a] = normal;
            }
        }

        private void Update()
        {
            if (_dirty && _mesh != null)
            {
                _dirty = false;
                _mesh.vertices = _vertices;
                _mesh.normals = _normals;
                _mesh.RecalculateBounds();
            }
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
