using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 3단계 내부 물 = KWS Water System (사용자가 멘토링으로 받은 실사 물 패키지 Assets/KriptoFX — git 제외, 셰이더 수정 = 저장소 루트 KWS_PATCH.md).
    /// 템플릿에는 KWS 물 물체를 꺼 둔 채 저장 (KWS는 편집 중에도 도는 [ExecuteAlways]라 템플릿을 만들 때 씬에 숨은 관리 물체가 생김) → 내부가 생길 때 켬.
    /// 켜기 전에 내부 하늘 면만 찍은 큐브맵을 물 반사 전용 하늘로 꽂고 반사 세기를 올림 (사용자 2026-11-11 "우주의 하늘이 물 표면에 반사되면 좋겠는데"),
    /// 내부를 나가면 되돌림. 패키지가 없는 PC면 KWS 컴포넌트가 '없는 스크립트'라 → 대신 Blender 수면 (<see cref="_fallback"/>)을 켬.
    /// </summary>
    public sealed class InteriorKwsWater : MonoBehaviour
    {
        [SerializeField] private GameObject[] _water;
        [SerializeField] private Renderer[] _fallback;
        [Tooltip("물에 비칠 하늘 면 (내부 하늘 — Unity 스카이박스는 내부에서 보이는 성운과 다름)")]
        [SerializeField] private Renderer _sky;
        [SerializeField] private Vector3 _skyCenter;
        [Tooltip("0 = 원래 물리 반사 (위에서 보면 3 %), 1 = 위에서도 하늘이 비침")]
        [SerializeField, Range(0f, 1f)] private float _reflectionBoost = 0.85f;
        [SerializeField, Min(0f)] private float _skyIntensity = 1.6f;

        private const int CaptureLayer = 31;   // 빈 레이어 (하늘 면만 찍을 때 잠깐)
        private static readonly int SkyCubeId = Shader.PropertyToID("KWS_SS_SkyCube");
        private static readonly int SkyOnId = Shader.PropertyToID("KWS_SS_SkyCubeOn");
        private static readonly int IntensityId = Shader.PropertyToID("KWS_SS_SkyCubeIntensity");
        private static readonly int FlattenId = Shader.PropertyToID("KWS_SS_SkyCubeFlatten");
        private static readonly int BoostId = Shader.PropertyToID("KWS_SS_ReflectionBoost");
        private RenderTexture _cube;

        public void Configure(GameObject[] water, Renderer[] fallback, Renderer sky, Vector3 skyCenter)
        {
            _water = water;
            _fallback = fallback;
            _sky = sky;
            _skyCenter = skyCenter;
        }

        private void Start()
        {
            bool kws = System.Type.GetType("KWS.WaterSystem, KWS") != null;
            if (kws)
                CaptureSky();
            foreach (var w in _water)
            {
                if (w != null)
                    w.SetActive(kws);
            }
            foreach (var r in _fallback)
            {
                if (r != null)
                    r.enabled = !kws;
            }
        }

        private void CaptureSky()
        {
            if (_sky == null)
                return;
            int old = _sky.gameObject.layer;
            _sky.gameObject.layer = CaptureLayer;
            var go = new GameObject("KwsSkyCapture");
            go.transform.position = transform.TransformPoint(_skyCenter);
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 1 << CaptureLayer;
            cam.allowHDR = true;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 500f;
            _cube = new RenderTexture(512, 512, 16, RenderTextureFormat.DefaultHDR)
            {
                dimension = UnityEngine.Rendering.TextureDimension.Cube,
                useMipMap = true,
                autoGenerateMips = true,
            };
            cam.RenderToCubemap(_cube);
            Destroy(go);
            _sky.gameObject.layer = old;
            Shader.SetGlobalTexture(SkyCubeId, _cube);
            Shader.SetGlobalFloat(SkyOnId, 1f);
            Shader.SetGlobalFloat(IntensityId, _skyIntensity);
            Shader.SetGlobalFloat(FlattenId, 0f);
            Shader.SetGlobalFloat(BoostId, _reflectionBoost);
        }

        private void OnDestroy()
        {
            Shader.SetGlobalFloat(SkyOnId, 0f);
            Shader.SetGlobalFloat(BoostId, 0f);
            if (_cube != null)
            {
                _cube.Release();
                Destroy(_cube);
            }
        }
    }
}
