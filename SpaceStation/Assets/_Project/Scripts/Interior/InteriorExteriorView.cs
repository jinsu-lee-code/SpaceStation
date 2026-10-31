using System;
using System.Collections.Generic;
using SpaceStation.Building;
using SpaceStation.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 11-8 바깥 창: 내부 카메라와 같은 자리·방향을 바깥 정거장 축척(내부 1칸 8m → 바깥 1칸 1m)으로 옮긴 카메라가
    /// 실제 정거장·우주를 화면 크기 텍스처에 그리고, 바깥 창 재질(<c>SpaceStation/InteriorWindow</c>)이 그 화면을 화면 좌표 그대로 보여준다.
    /// - 창이 있는 방(모듈)마다 따로 그린다: 그 방 모듈의 바깥 모델을 숨겨야 창이 자기 껍데기를 비추지 않는데,
    ///   문 너머 옆방의 창은 플레이어가 선 방과 숨길 모듈이 다르기 때문 (텍스처는 창 렌더러마다 MaterialPropertyBlock).
    /// - 창이 보이는지는 그 프레임의 내부 카메라 시야(경계 상자)로 미리 판정 → 닫힌 문 너머 창도 시야 안이면 미리 그려 둬서 문이 열리는 순간 맞는 화면.
    /// - 그리는 동안만 태양을 켠다 (내부는 태양 없이 방 조명만). 카메라를 품은 연결 통로도 숨김.
    /// - 지붕 위 전망실 창(<see cref="InteriorLookoutWindow"/>)은 따로 묶어 자기 모듈도 보이게 + 카메라를 들어 올려 그림, 망원경은 <see cref="RenderLookout"/>.
    /// <see cref="InteriorMode"/>가 들어갈 때 만들고 나올 때 없앤다.
    /// </summary>
    [DefaultExecutionOrder(1000)] // 시점 이동(FirstPersonController) 뒤
    public sealed class InteriorExteriorView : MonoBehaviour
    {
        public const string WindowShaderName = "SpaceStation/InteriorWindow";

        private static readonly int TexId = Shader.PropertyToID("_InteriorExteriorTex");
        private static readonly int OnId = Shader.PropertyToID("_InteriorExteriorOn");

        /// <summary>같은 방(모듈)의 바깥 창들 + 그 방 전용 화면. 지붕 위 전망실 창(<see cref="InteriorLookoutWindow"/>)은 같은 방이라도 따로.</summary>
        private sealed class Group
        {
            public ModuleInstance Module;
            public InteriorLookoutWindow Lookout;
            public Bounds Bounds;
            public readonly List<Renderer> Windows = new List<Renderer>();
            public RenderTexture Texture;
        }

        private Camera _source;
        private Camera _camera;
        private UniversalAdditionalCameraData _cameraData;
        private Transform _interiorRoot;
        private StationController _station;
        private StationConnectors _connectors;
        private Light _sun;
        private Func<Vector3, ModuleInstance> _roomAt;
        private Func<Transform, ModuleInstance> _roomOf;
        private Action<ModuleInstance, HashSet<ModuleInstance>> _linkedRooms;
        private Func<ModuleInstance, bool> _showOwnExterior;
        private readonly HashSet<ModuleInstance> _linked = new HashSet<ModuleInstance>();
        private ModuleInstance _linkedFrom;
        private readonly List<Group> _groups = new List<Group>();
        private readonly List<Renderer> _hidden = new List<Renderer>();
        private readonly List<Renderer> _moduleRenderers = new List<Renderer>();
        private readonly Plane[] _planes = new Plane[6];
        private MaterialPropertyBlock _block;
        private ModuleInstance _hideModule;
        private float _lift;              // 지금 그리는 화면의 바깥 카메라 들어 올림 (내부 m) — 전망실 창 · 망원경
        private bool _poseOverride;       // 망원경: 내부 카메라 대신 주어진 자리 · 방향 · 시야각
        private Vector3 _overridePosition;
        private Quaternion _overrideRotation;
        private float _overrideFov;
        private bool _sunWasEnabled;

        /// <summary>망원경을 보는 동안: 창 화면을 새로 그리지 않음 (화면을 망원경이 덮음).</summary>
        public bool Suspended { get; set; }

        /// <param name="roomAt">내부 월드 위치 → 그 칸의 방 모듈 (없으면 null)</param>
        /// <param name="linkedRooms">방 → 그 방 + 열린 통로로 바로 이어진 방들 (창을 그릴 방 범위)</param>
        /// <param name="showOwnExterior">12-5 방 → 그 방 창에 자기 바깥 모델도 비출지 (템플릿 설정, 없으면 숨김)</param>
        /// <param name="roomOf">창 렌더러 → 그 렌더러가 속한 방 모듈 (방 오브젝트 아래가 아니면 null → 경계 상자 가운데 칸으로)</param>
        public static InteriorExteriorView Create(Camera source, Transform interiorRoot, StationController station, Light sun, float farClip,
            Func<Vector3, ModuleInstance> roomAt, Action<ModuleInstance, HashSet<ModuleInstance>> linkedRooms,
            Func<ModuleInstance, bool> showOwnExterior = null, Func<Transform, ModuleInstance> roomOf = null)
        {
            var go = new GameObject("InteriorExteriorCamera");
            var view = go.AddComponent<InteriorExteriorView>();
            view._source = source;
            view._interiorRoot = interiorRoot;
            view._station = station;
            view._roomAt = roomAt;
            view._roomOf = roomOf;
            view._linkedRooms = linkedRooms;
            view._showOwnExterior = showOwnExterior;
            view._connectors = station != null ? station.GetComponent<StationConnectors>() : null;
            if (view._connectors == null)
                view._connectors = FindFirstObjectByType<StationConnectors>();
            view._sun = sun;
            view._block = new MaterialPropertyBlock();
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = farClip;
            cam.cullingMask = source.cullingMask;
            cam.allowMSAA = false;
            cam.allowHDR = true;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false; // 블룸 등은 내부 카메라가 창까지 한 번에
            data.renderShadows = true;
            cam.enabled = false; // 방마다 직접 Render()
            view._camera = cam;
            view._cameraData = data;
            Shader.SetGlobalFloat(OnId, 1f);
            return view;
        }

        /// <summary>내부를 새로 만든 뒤: 바깥 창 렌더러를 방별로 다시 모음.</summary>
        public void CollectWindows()
        {
            ReleaseTextures();
            _groups.Clear();
            _linked.Clear();
            _linkedFrom = null; // 통로가 바뀌었을 수 있음 → 다음 프레임에 다시 계산
            AddWindows(_interiorRoot);
        }

        /// <summary>12-3c-opt: 방을 나중에 지으면 그 방의 바깥 창만 더함.</summary>
        public void AddWindows(Transform root)
        {
            if (root == null)
                return;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!IsWindow(r))
                    continue;
                // 창이 속한 방 = 렌더러가 든 방 오브젝트. 경계 상자 가운데 칸으로만 정하면 ㄱ자 모듈(비상 대응 센터)은 창 한 덩어리의
                // 가운데가 빈 모서리 칸(옆 모듈 자리)에 떨어져, 옆 모듈을 숨기고 자기 바깥 모델 안쪽을 비춰 창이 하얗게 보였음 (사용자 2026-10-30)
                var module = _roomOf?.Invoke(r.transform) ?? (_roomAt != null ? _roomAt(r.bounds.center) : null);
                var lookout = r.GetComponent<InteriorLookoutWindow>();
                var group = _groups.Find(g => g.Module == module && g.Lookout == lookout);
                if (group == null)
                {
                    group = new Group { Module = module, Lookout = lookout, Bounds = r.bounds };
                    _groups.Add(group);
                }
                group.Windows.Add(r);
                group.Bounds.Encapsulate(r.bounds);
            }
        }

        private static bool IsWindow(Renderer r)
        {
            foreach (var m in r.sharedMaterials)
            {
                if (m != null && m.shader != null && m.shader.name == WindowShaderName)
                    return true;
            }
            return false;
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += HandleBegin;
            RenderPipelineManager.endCameraRendering += HandleEnd;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= HandleBegin;
            RenderPipelineManager.endCameraRendering -= HandleEnd;
            RestoreHidden();
        }

        private void OnDestroy()
        {
            Shader.SetGlobalFloat(OnId, 0f);
            ReleaseTextures();
        }

        private void LateUpdate()
        {
            if (_source == null || _groups.Count == 0 || Suspended)
                return;
            // 지금 방 + 문·해치로 바로 이어진 방의 창만 (큰 정거장에서 그리는 횟수 제한)
            var eye = _source.transform.position;
            var here = _roomAt != null ? _roomAt(eye) : null;
            if (here != _linkedFrom)
            {
                _linkedFrom = here;
                _linkedRooms?.Invoke(here, _linked);
            }
            // 지붕 위 전망실 안 (창 경계 + 아래 1m — 바닥에 선 눈높이 포함): 전망실 창만, 같은 방의 다른 창은 안 보이므로 건너뜀
            Group lookout = null;
            foreach (var group in _groups)
            {
                if (group.Lookout == null || group.Module != here)
                    continue;
                var b = group.Bounds;
                b.min -= new Vector3(0f, 1f, 0f);
                if (b.Contains(eye))
                    lookout = group;
            }
            GeometryUtility.CalculateFrustumPlanes(_source, _planes);
            // 12-3c-opt 3차 (사용자 피드백 2026-10-14: 내부를 걸을 때 프레임이 살짝씩 떨어짐 — 창 묶음마다 정거장 전체를 매 프레임 다시 그려 약 7ms):
            // 지금 방의 창 = 매 프레임 · 원래 해상도 · 그림자, 이웃 방의 창(문 너머로 작게 보임) = 프레임마다 한 묶음씩 번갈아 · 절반 해상도 · 그림자 없음
            _others.Clear();
            foreach (var group in _groups)
            {
                if (!_linked.Contains(group.Module) || !InView(group))
                    continue;
                if (group.Lookout != null ? group != lookout : lookout != null && group.Module == lookout.Module)
                    continue; // 전망실 창은 그 안에서만, 전망실 안에서는 같은 방의 다른 창은 안 보임
                if (group.Module == here)
                    RenderGroup(group, main: true);
                else
                    _others.Add(group);
            }
            if (_others.Count > 0)
                RenderGroup(_others[(_turn++ & int.MaxValue) % _others.Count], main: false);
            _camera.targetTexture = null;
            _hideModule = null;
        }

        private readonly List<Group> _others = new List<Group>();
        private int _turn;

        private void RenderGroup(Group group, bool main)
        {
            EnsureTexture(group, main ? 1 : 2);
            // 전망실 창 = 자기 모듈도 보임 (탑 꼭대기 위에서 내려다보는 링 · 꼬리) + 바깥 카메라 들어 올림
            _hideModule = group.Lookout != null ? null : group.Module;
            _lift = group.Lookout != null ? group.Lookout.Lift : 0f;
            _camera.targetTexture = group.Texture;
            _cameraData.renderShadows = main;
            _camera.Render();
            _lift = 0f;
        }

        /// <summary>
        /// 망원경 화면: 내부 월드 자리 · 방향 · 세로 시야각 → 바깥 축척 (+ lift 내부 m 들어 올림)으로 target에 바로 그림.
        /// 자기 모듈도 보임 (전망실 창과 같음), 카메라를 품은 다른 모듈 · 연결 통로는 숨김.
        /// </summary>
        public void RenderLookout(RenderTexture target, Vector3 position, Quaternion rotation, float fieldOfView, float lift)
        {
            if (target == null || _camera == null)
                return;
            _poseOverride = true;
            _overridePosition = position;
            _overrideRotation = rotation;
            _overrideFov = fieldOfView;
            _hideModule = null;
            _lift = lift;
            _camera.targetTexture = target;
            _cameraData.renderShadows = true;
            _camera.Render();
            _camera.targetTexture = null;
            _poseOverride = false;
            _lift = 0f;
        }

        private bool InView(Group group)
        {
            foreach (var w in group.Windows)
            {
                if (w != null && w.enabled && w.gameObject.activeInHierarchy && GeometryUtility.TestPlanesAABB(_planes, w.bounds))
                    return true;
            }
            return false;
        }

        /// <summary>내부 카메라 자리·방향 → 바깥 축척 (전망실 창 · 망원경은 위로 _lift만큼 더).</summary>
        private void SyncPose()
        {
            float scale = GridConfig.CellSize / InteriorGeometry.CellSize;
            var position = _poseOverride ? _overridePosition : _source.transform.position;
            var rotation = _poseOverride ? _overrideRotation : _source.transform.rotation;
            _camera.transform.SetPositionAndRotation((position - _interiorRoot.position + Vector3.up * _lift) * scale, rotation);
            _camera.fieldOfView = _poseOverride ? _overrideFov : _source.fieldOfView;
            var target = _camera.targetTexture;
            _camera.aspect = _poseOverride && target != null ? (float)target.width / target.height : _source.aspect;
        }

        /// <param name="divisor">해상도 나눔 (창 재질은 화면 좌표로 읽으므로 절반 해상도도 같은 자리 — 조금 흐릴 뿐)</param>
        private void EnsureTexture(Group group, int divisor)
        {
            int w = Mathf.Max(16, _source.pixelWidth / divisor);
            int h = Mathf.Max(16, _source.pixelHeight / divisor);
            if (group.Texture != null && group.Texture.width == w && group.Texture.height == h)
                return;
            if (group.Texture != null)
            {
                group.Texture.Release();
                Destroy(group.Texture);
            }
            group.Texture = new RenderTexture(w, h, 24, RenderTextureFormat.DefaultHDR) { name = "InteriorExterior" };
            group.Texture.Create();
            _block.Clear();
            _block.SetTexture(TexId, group.Texture);
            foreach (var r in group.Windows)
            {
                if (r == null)
                    continue;
                var mats = r.sharedMaterials; // 창 칸에만 (방 어둡게 하기는 칸별 블록을 쓰므로 칸 단위로 맞춤)
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != null && mats[i].shader.name == WindowShaderName)
                        r.SetPropertyBlock(_block, i);
                }
            }
        }

        private void ReleaseTextures()
        {
            foreach (var g in _groups)
            {
                if (g.Texture == null)
                    continue;
                g.Texture.Release();
                Destroy(g.Texture);
                g.Texture = null;
            }
        }

        private void HandleBegin(ScriptableRenderContext context, Camera cam)
        {
            if (cam != _camera)
                return;
            SyncPose();
            if (_sun != null)
            {
                _sunWasEnabled = _sun.enabled;
                _sun.enabled = true;
            }
            Hide(_hideModule, cam.transform.position);
        }

        private void HandleEnd(ScriptableRenderContext context, Camera cam)
        {
            if (cam != _camera)
                return;
            if (_sun != null)
                _sun.enabled = _sunWasEnabled;
            RestoreHidden();
        }

        /// <summary>창이 속한 방 모듈 + 그 모듈에 닿은 연결 통로 + 카메라를 품은 모듈·연결 통로를 숨김.
        /// 12-5: 템플릿이 자기 외형 보이기면(구조 프레임 · 센서 어레이) 방 모듈은 숨기지 않음 — 창 너머로 자기 트러스 · 접시가 보이게.</summary>
        private void Hide(ModuleInstance module, Vector3 point)
        {
            _hidden.Clear();
            if (module != null && (_showOwnExterior == null || !_showOwnExterior(module)))
                HideModule(module);
            // 카메라를 품은 이웃 모듈: 바깥 모델(칸의 약 92%)이 내부 방(80%)보다 커서, 튜브 끝·문가에서는 바깥 카메라가
            // 옆 모듈 바깥 모델 속에 들어가 안쪽 면이 창을 덮음 (장갑 격벽 → 채굴 도킹 튜브에서 검은 창).
            // 카메라 칸을 실제로 차지한 모듈만 본다 — 렌더러 경계 상자만 보면 ㄱ자 모듈(생태 모듈)의 상자가 빈 모서리 칸까지 덮어,
            // 그 칸에 놓인 옆 모듈(융합 발전소) 방 안에서도 "품었다"로 잡혀 창 밖 생태 모듈이 통째로 사라졌음 (사용자 2026-10-29)
            if (_station != null && _station.Grid != null
                && _station.Grid.TryGetModule(GridConfig.WorldToCell(point), out var near) && near != module)
            {
                bool contains = false;
                foreach (var r in RenderersOf(near))
                {
                    if (r == null)
                        continue;
                    var b = r.bounds;
                    b.Expand(0.04f);
                    if (b.Contains(point))
                    {
                        contains = true;
                        break;
                    }
                }
                if (contains)
                    HideModule(near);
            }
            if (_connectors != null)
            {
                int before = _hidden.Count;
                if (module != null)
                    _connectors.CollectRenderersTouching(module.Cells, _hidden); // 창 바로 앞 통로 끝(팔각 칼라)이 가리지 않게
                _connectors.CollectRenderersContaining(point, 0.02f, _hidden);
                for (int i = before; i < _hidden.Count; i++)
                {
                    var r = _hidden[i];
                    if (r == null || r.forceRenderingOff)
                        _hidden[i] = null; // 원래 꺼져 있던 것은 되돌리지 않음
                    else
                        r.forceRenderingOff = true;
                }
            }
        }

        /// <summary>
        /// 모듈 바깥 모델의 렌더러 — 그릴 때마다 다시 모음 (목록 재사용, 쓰레기 없음). 처음 한 번만 모아 두면 나중에 생긴 자식이 빠짐:
        /// 패드를 들면 지금 방이 선택되며 생기는 선택 테두리 복제본(<see cref="ModuleView"/> <c>_Rim</c>)이 숨겨지지 않아,
        /// 자기 모듈 바깥 모델 속의 바깥 카메라가 청록 테두리 빛을 창 가득 비췄음 (사용자 2026-10-31 보안청 "패드를 들어올리니까 창문 쪽에 빛줄기").
        /// </summary>
        private List<Renderer> RenderersOf(ModuleInstance module)
        {
            _moduleRenderers.Clear();
            if (_station != null && _station.TryGetView(module, out var view) && view != null)
                view.GetComponentsInChildren(true, _moduleRenderers);
            return _moduleRenderers;
        }

        private void HideModule(ModuleInstance module)
        {
            foreach (var r in RenderersOf(module))
                HideOne(r);
        }

        private void HideOne(Renderer r)
        {
            if (r == null || r.forceRenderingOff)
                return;
            r.forceRenderingOff = true;
            _hidden.Add(r);
        }

        private void RestoreHidden()
        {
            foreach (var r in _hidden)
            {
                if (r != null)
                    r.forceRenderingOff = false;
            }
            _hidden.Clear();
        }
    }
}
