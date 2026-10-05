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
    /// <see cref="InteriorMode"/>가 들어갈 때 만들고 나올 때 없앤다.
    /// </summary>
    [DefaultExecutionOrder(1000)] // 시점 이동(FirstPersonController) 뒤
    public sealed class InteriorExteriorView : MonoBehaviour
    {
        public const string WindowShaderName = "SpaceStation/InteriorWindow";

        private static readonly int TexId = Shader.PropertyToID("_InteriorExteriorTex");
        private static readonly int OnId = Shader.PropertyToID("_InteriorExteriorOn");

        /// <summary>같은 방(모듈)의 바깥 창들 + 그 방 전용 화면.</summary>
        private sealed class Group
        {
            public ModuleInstance Module;
            public readonly List<Renderer> Windows = new List<Renderer>();
            public RenderTexture Texture;
        }

        private Camera _source;
        private Camera _camera;
        private Transform _interiorRoot;
        private StationController _station;
        private StationConnectors _connectors;
        private Light _sun;
        private Func<Vector3, ModuleInstance> _roomAt;
        private Action<ModuleInstance, HashSet<ModuleInstance>> _linkedRooms;
        private readonly HashSet<ModuleInstance> _linked = new HashSet<ModuleInstance>();
        private ModuleInstance _linkedFrom;
        private readonly List<Group> _groups = new List<Group>();
        private readonly List<Renderer> _hidden = new List<Renderer>();
        private readonly Dictionary<ModuleView, Renderer[]> _moduleRenderers = new Dictionary<ModuleView, Renderer[]>();
        private readonly Plane[] _planes = new Plane[6];
        private readonly HashSet<ModuleInstance> _nearModules = new HashSet<ModuleInstance>();
        private MaterialPropertyBlock _block;
        private ModuleInstance _hideModule;
        private bool _sunWasEnabled;

        /// <param name="roomAt">내부 월드 위치 → 그 칸의 방 모듈 (없으면 null)</param>
        /// <param name="linkedRooms">방 → 그 방 + 열린 통로로 바로 이어진 방들 (창을 그릴 방 범위)</param>
        public static InteriorExteriorView Create(Camera source, Transform interiorRoot, StationController station, Light sun, float farClip,
            Func<Vector3, ModuleInstance> roomAt, Action<ModuleInstance, HashSet<ModuleInstance>> linkedRooms)
        {
            var go = new GameObject("InteriorExteriorCamera");
            var view = go.AddComponent<InteriorExteriorView>();
            view._source = source;
            view._interiorRoot = interiorRoot;
            view._station = station;
            view._roomAt = roomAt;
            view._linkedRooms = linkedRooms;
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
            Shader.SetGlobalFloat(OnId, 1f);
            return view;
        }

        /// <summary>내부를 새로 만든 뒤: 바깥 창 렌더러를 방별로 다시 모음.</summary>
        public void CollectWindows()
        {
            ReleaseTextures();
            _groups.Clear();
            _moduleRenderers.Clear();
            _linked.Clear();
            _linkedFrom = null; // 통로가 바뀌었을 수 있음 → 다음 프레임에 다시 계산
            foreach (var r in _interiorRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (!IsWindow(r))
                    continue;
                var module = _roomAt != null ? _roomAt(r.bounds.center) : null;
                var group = _groups.Find(g => g.Module == module);
                if (group == null)
                {
                    group = new Group { Module = module };
                    _groups.Add(group);
                }
                group.Windows.Add(r);
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
            if (_source == null || _groups.Count == 0)
                return;
            // 지금 방 + 문·해치로 바로 이어진 방의 창만 (큰 정거장에서 그리는 횟수 제한)
            var here = _roomAt != null ? _roomAt(_source.transform.position) : null;
            if (here != _linkedFrom)
            {
                _linkedFrom = here;
                _linkedRooms?.Invoke(here, _linked);
            }
            GeometryUtility.CalculateFrustumPlanes(_source, _planes);
            foreach (var group in _groups)
            {
                if (!_linked.Contains(group.Module) || !InView(group))
                    continue;
                EnsureTexture(group);
                _hideModule = group.Module;
                _camera.targetTexture = group.Texture;
                _camera.Render();
            }
            _camera.targetTexture = null;
            _hideModule = null;
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

        /// <summary>내부 카메라 자리·방향 → 바깥 축척.</summary>
        private void SyncPose()
        {
            var src = _source.transform;
            float scale = GridConfig.CellSize / InteriorGeometry.CellSize;
            _camera.transform.SetPositionAndRotation((src.position - _interiorRoot.position) * scale, src.rotation);
            _camera.fieldOfView = _source.fieldOfView;
            _camera.aspect = _source.aspect;
        }

        private void EnsureTexture(Group group)
        {
            int w = Mathf.Max(16, _source.pixelWidth);
            int h = Mathf.Max(16, _source.pixelHeight);
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

        /// <summary>창이 속한 방 모듈 + 그 모듈에 닿은 연결 통로 + 카메라를 품은 모듈·연결 통로를 숨김.</summary>
        private void Hide(ModuleInstance module, Vector3 point)
        {
            _hidden.Clear();
            if (module != null)
                HideModule(module);
            // 카메라를 품은 이웃 모듈: 바깥 모델(칸의 약 92%)이 내부 방(80%)보다 커서, 튜브 끝·문가에서는 바깥 카메라가
            // 옆 모듈 바깥 모델 속에 들어가 안쪽 면이 창을 덮음 (장갑 격벽 → 채굴 도킹 튜브에서 검은 창)
            if (_station != null && _station.Grid != null)
            {
                _nearModules.Clear();
                var center = GridConfig.WorldToCell(point);
                for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                for (int z = -1; z <= 1; z++)
                {
                    if (_station.Grid.TryGetModule(center + new Vector3Int(x, y, z), out var near) && near != module)
                        _nearModules.Add(near);
                }
                foreach (var near in _nearModules)
                {
                    var rs = RenderersOf(near);
                    if (rs == null)
                        continue;
                    foreach (var r in rs)
                    {
                        if (r == null)
                            continue;
                        var b = r.bounds;
                        b.Expand(0.04f);
                        if (b.Contains(point))
                        {
                            HideModule(near);
                            break;
                        }
                    }
                }
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

        private Renderer[] RenderersOf(ModuleInstance module)
        {
            if (_station == null || !_station.TryGetView(module, out var view) || view == null)
                return null;
            if (!_moduleRenderers.TryGetValue(view, out var rs))
            {
                rs = view.GetComponentsInChildren<Renderer>(true);
                _moduleRenderers[view] = rs;
            }
            return rs;
        }

        private void HideModule(ModuleInstance module)
        {
            var rs = RenderersOf(module);
            if (rs == null)
                return;
            foreach (var r in rs)
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
