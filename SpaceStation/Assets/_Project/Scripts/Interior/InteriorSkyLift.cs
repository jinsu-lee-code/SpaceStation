using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 빛 기둥 승강판 (진화 단계별 통로 ③, 사용자 2026-11-17 "A로 하는데, 색깔을 모듈의 색 테마에 맞춰서 진행해줘. 그리고 바로 이동되는게 아니라,
    /// 모듈 자체의 높이가 각자 있잖아? 그거에 맞게 공중으로 떠오르다가 이동이 되는 방식이면 좋겠어"): 하늘이 트인 2단계+ 방의 위 연결점 (해치 대신).
    /// 바닥 판 · 하늘 고리 = 단계 키트 조각 (빛 테 = 방 강조색), 그 사이 부드러운 빛 기둥 = SpaceStation/LightShaft (방 강조색, 판 쪽 환하게 · 위로 흐르는 빛 결 · 고리 앞에서 스며듦).
    /// 판 위에 서거나 판을 보고 상호작용하면 기둥을 따라 고리 밑까지 떠오른 뒤 위 모듈로 (<see cref="InteriorMode"/>), 위에서 내려오면 고리 밑에서 판까지 내려앉음.
    /// 타는 동안 기둥이 밝아짐 (색이 아닌 밝기로 — 색약 고려). 자리는 템플릿 문 자리 (<see cref="Data.InteriorSocket.SkyLift"/>, 에디터 InteriorSkyLiftSpots가 찾음).
    /// </summary>
    public sealed class InteriorSkyLift : MonoBehaviour
    {
        private const int Around = 28;
        private const int Along = 8;
        /// <summary>빛 기둥 몸통 반지름 (판 빛 고리 0.46 ~ 0.56 안쪽에서 솟음).</summary>
        public const float ColumnRadius = 0.5f;
        /// <summary>판 반지름 (위에 서면 탈 수 있음).</summary>
        public const float PadRadius = 0.75f;
        private const float Idle = 0.6f;
        private const float Riding = 1.8f;
        private static readonly int LevelId = Shader.PropertyToID("_Level");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private MeshRenderer _renderer;
        private Mesh _mesh;
        private MaterialPropertyBlock _block;
        private Color _color;
        private float _level = Idle;
        private float _target = Idle;

        /// <summary>판 윗면 가운데 (월드, 발 자리).</summary>
        public Vector3 PadTop { get; private set; }

        /// <summary>하늘 고리 높이 (월드).</summary>
        public float RingY { get; private set; }

        /// <summary>판에서 고리까지 (모듈마다 다름 — 떠오르는 높이).</summary>
        public float Rise => RingY - PadTop.y;

        /// <summary>판 윗면 · 고리 높이 · 빛 기둥 재질 (없으면 기둥 없이 판 · 고리만) · 방 강조색.</summary>
        public void Initialize(Vector3 padTop, float ringY, Material shaft, Color color)
        {
            PadTop = padTop;
            RingY = ringY;
            _color = color;
            if (shaft == null)
                return;
            var go = new GameObject("LightColumn");
            go.transform.SetParent(transform, false);
            go.transform.position = padTop + Vector3.up * 0.02f;
            go.transform.rotation = Quaternion.identity;
            _mesh = ColumnMesh(Mathf.Max(0.5f, ringY - padTop.y - 0.06f));
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = go.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = shaft;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            _block = new MaterialPropertyBlock();
            Apply();
        }

        /// <summary>발이 판 위 (판 반지름 안 · 판 높이 근처).</summary>
        public bool Contains(Vector3 feet)
        {
            var d = feet - PadTop;
            return new Vector2(d.x, d.z).sqrMagnitude < PadRadius * PadRadius && d.y > -0.3f && d.y < 0.5f;
        }

        /// <summary>타는 동안 밝게 (내리면 원래대로 — 서서히).</summary>
        public void SetRiding(bool riding) => _target = riding ? Riding : Idle;

        private void Update()
        {
            if (_renderer == null || Mathf.Approximately(_level, _target))
                return;
            _level = Mathf.MoveTowards(_level, _target, Time.unscaledDeltaTime * 3f);
            Apply();
        }

        private void OnDestroy()
        {
            if (_mesh == null)
                return;
            if (Application.isPlaying)
                Destroy(_mesh);
            else
                DestroyImmediate(_mesh); // 에디터 미리보기 (승강판 자리 시트)
        }

        private void Apply()
        {
            if (_renderer == null)
                return;
            _block.Clear();
            _block.SetColor(BaseColorId, _color);
            _block.SetFloat(LevelId, _level);
            _renderer.SetPropertyBlock(_block);
        }

        /// <summary>원기둥 (뚜껑 없음): uv.x = 높이 (0 판 ~ 1 고리), uv.y = 둘레 — LightShaft 셰이더 규칙.</summary>
        private static Mesh ColumnMesh(float height)
        {
            int count = (Along + 1) * (Around + 1);
            var vertices = new Vector3[count];
            var normals = new Vector3[count];
            var uvs = new Vector2[count];
            for (int i = 0; i <= Along; i++)
            {
                float t = (float)i / Along;
                for (int j = 0; j <= Around; j++)
                {
                    float a = j * Mathf.PI * 2f / Around;
                    var n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    int k = i * (Around + 1) + j;
                    vertices[k] = n * ColumnRadius + Vector3.up * (height * t);
                    normals[k] = n;
                    uvs[k] = new Vector2(t, (float)j / Around);
                }
            }
            var triangles = new int[Along * Around * 6];
            int q = 0;
            for (int i = 0; i < Along; i++)
            {
                for (int j = 0; j < Around; j++)
                {
                    int a = i * (Around + 1) + j, b = a + Around + 1;
                    triangles[q++] = a;
                    triangles[q++] = b;
                    triangles[q++] = a + 1;
                    triangles[q++] = a + 1;
                    triangles[q++] = b;
                    triangles[q++] = b + 1;
                }
            }
            var mesh = new Mesh { name = "SkyLiftColumn", vertices = vertices, normals = normals, uv = uvs, triangles = triangles };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
