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
    /// - 바깥 카메라가 그리는 동안만 태양을 켠다 (내부는 태양 없이 방 조명만).
    /// - 지금 있는 칸의 모듈과 카메라를 품은 연결 통로는 숨긴다 (바깥 모델 속에서 자기 껍데기가 비치지 않게).
    /// - 바깥 창이 하나도 보이지 않는 프레임에는 그리지 않는다.
    /// <see cref="InteriorMode"/>가 들어갈 때 만들고 나올 때 없앤다.
    /// </summary>
    public sealed class InteriorExteriorView : MonoBehaviour
    {
        public const string WindowShaderName = "SpaceStation/InteriorWindow";

        private static readonly int TexId = Shader.PropertyToID("_InteriorExteriorTex");
        private static readonly int OnId = Shader.PropertyToID("_InteriorExteriorOn");

        private Camera _source;
        private Camera _camera;
        private Transform _interiorRoot;
        private StationController _station;
        private StationConnectors _connectors;
        private Light _sun;
        private RenderTexture _texture;
        private readonly List<Renderer> _windows = new List<Renderer>();
        private readonly List<Renderer> _hidden = new List<Renderer>();
        private readonly Dictionary<ModuleView, Renderer[]> _moduleRenderers = new Dictionary<ModuleView, Renderer[]>();
        private bool _sunWasEnabled;

        public static InteriorExteriorView Create(Camera source, Transform interiorRoot, StationController station, Light sun, float farClip)
        {
            var go = new GameObject("InteriorExteriorCamera");
            var view = go.AddComponent<InteriorExteriorView>();
            view._source = source;
            view._interiorRoot = interiorRoot;
            view._station = station;
            view._connectors = station != null ? station.GetComponent<StationConnectors>() : null;
            if (view._connectors == null)
                view._connectors = FindFirstObjectByType<StationConnectors>();
            view._sun = sun;
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = farClip;
            cam.cullingMask = source.cullingMask;
            cam.depth = source.depth - 1f;
            cam.allowMSAA = false;
            cam.allowHDR = true;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false; // 블룸 등은 내부 카메라가 창까지 한 번에
            data.renderShadows = true;
            cam.enabled = false;
            view._camera = cam;
            Shader.SetGlobalFloat(OnId, 0f);
            return view;
        }

        /// <summary>내부를 새로 만든 뒤: 바깥 창 렌더러 목록 갱신.</summary>
        public void CollectWindows()
        {
            _windows.Clear();
            foreach (var r in _interiorRoot.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m != null && m.shader != null && m.shader.name == WindowShaderName)
                    {
                        _windows.Add(r);
                        break;
                    }
                }
            }
            _moduleRenderers.Clear();
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
            if (_sun != null && _camera != null && _sun.enabled && !_sunWasEnabled)
                _sun.enabled = false;
        }

        private void OnDestroy()
        {
            Shader.SetGlobalFloat(OnId, 0f);
            if (_texture != null)
            {
                _texture.Release();
                Destroy(_texture);
            }
        }

        private void LateUpdate()
        {
            if (_source == null)
                return;
            bool any = false;
            foreach (var w in _windows)
            {
                if (w != null && w.isVisible)
                {
                    any = true;
                    break;
                }
            }
            // 처음 한 번(isVisible이 아직 갱신 전)도 그려 둠
            _camera.enabled = any || _texture == null;
            if (!_camera.enabled)
                return;

            EnsureTexture();
            SyncPose();
            Shader.SetGlobalTexture(TexId, _texture);
            Shader.SetGlobalFloat(OnId, 1f);
        }

        /// <summary>내부 카메라 자리·방향 → 바깥 축척. 렌더 직전에도 다시 맞춰 시점 이동보다 한 프레임 늦지 않게.</summary>
        private void SyncPose()
        {
            var src = _source.transform;
            float scale = GridConfig.CellSize / InteriorGeometry.CellSize;
            _camera.transform.SetPositionAndRotation((src.position - _interiorRoot.position) * scale, src.rotation);
            _camera.fieldOfView = _source.fieldOfView;
            _camera.aspect = _source.aspect;
        }

        private void EnsureTexture()
        {
            int w = Mathf.Max(16, _source.pixelWidth);
            int h = Mathf.Max(16, _source.pixelHeight);
            if (_texture != null && _texture.width == w && _texture.height == h)
                return;
            if (_texture != null)
            {
                _texture.Release();
                Destroy(_texture);
            }
            _texture = new RenderTexture(w, h, 24, RenderTextureFormat.DefaultHDR) { name = "InteriorExterior" };
            _texture.Create();
            _camera.targetTexture = _texture;
        }

        private void HandleBegin(ScriptableRenderContext context, Camera cam)
        {
            if (cam != _camera)
                return;
            if (_source != null)
                SyncPose();
            if (_sun != null)
            {
                _sunWasEnabled = _sun.enabled;
                _sun.enabled = true;
            }
            HideNear(cam.transform.position);
        }

        private void HandleEnd(ScriptableRenderContext context, Camera cam)
        {
            if (cam != _camera)
                return;
            if (_sun != null)
                _sun.enabled = _sunWasEnabled;
            RestoreHidden();
        }

        /// <summary>바깥 카메라 자리의 칸을 차지한 모듈 + 카메라를 품은 연결 통로를 숨김.</summary>
        private void HideNear(Vector3 point)
        {
            _hidden.Clear();
            if (_station != null && _station.Grid != null)
            {
                var cell = GridConfig.WorldToCell(point);
                if (_station.Grid.TryGetModule(cell, out var module) && _station.TryGetView(module, out var view) && view != null)
                {
                    if (!_moduleRenderers.TryGetValue(view, out var rs))
                    {
                        rs = view.GetComponentsInChildren<Renderer>(true);
                        _moduleRenderers[view] = rs;
                    }
                    foreach (var r in rs)
                        Hide(r);
                }
            }
            if (_connectors != null)
            {
                int before = _hidden.Count;
                _connectors.CollectRenderersContaining(point, 0.02f, _hidden);
                for (int i = before; i < _hidden.Count; i++)
                {
                    var r = _hidden[i];
                    if (r.forceRenderingOff)
                        _hidden[i] = null; // 원래 꺼져 있던 것은 되돌리지 않음
                    else
                        r.forceRenderingOff = true;
                }
            }
        }

        private void Hide(Renderer r)
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
