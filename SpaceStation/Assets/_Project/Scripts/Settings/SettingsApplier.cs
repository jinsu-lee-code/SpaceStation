using System.Collections.Generic;
using SpaceStation.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SpaceStation.Settings
{
    /// <summary>
    /// 5-10 설정 적용기. 게임 시작 때 스스로 생기고 씬이 바뀌어도 남는다 (씬 배선 불필요).
    /// - 그래픽: URP 에셋 복사본을 현재 파이프라인으로 써서 원본 에셋은 바꾸지 않음 (종료 시 원래대로)
    ///   그림자(방향광 + 해상도·단계·거리), 안티에일리어싱(카메라 FXAA/SMAA 또는 MSAA), 렌더 해상도, 블룸
    /// - 프레임 제한·수직 동기화, UI 크기(CanvasScaler 기준 해상도), 창 비활성 시 일시정지
    /// 씬이 바뀌면 새 카메라·조명·볼륨·캔버스에 다시 적용한다.
    /// </summary>
    public sealed class SettingsApplier : MonoBehaviour
    {
        private static SettingsApplier _instance;

        private RenderPipelineAsset _original;
        private UniversalRenderPipelineAsset _runtime;
        private readonly Dictionary<CanvasScaler, Vector2> _scalerBase = new Dictionary<CanvasScaler, Vector2>();
        private bool _pausedByFocus;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (_instance != null)
                return;
            var go = new GameObject("SettingsApplier");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<SettingsApplier>();
        }

        /// <summary>새로 만든 UI(설정창 등)에 UI 크기를 바로 반영하고 싶을 때.</summary>
        public static void Refresh()
        {
            if (_instance != null)
                _instance.ApplyAll();
        }

        private void Awake()
        {
            _original = QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline : GraphicsSettings.defaultRenderPipeline;
            if (_original is UniversalRenderPipelineAsset urp)
            {
                _runtime = Instantiate(urp);
                _runtime.name = urp.name + " (Runtime)";
                QualitySettings.renderPipeline = _runtime;
            }
            GameSettings.Changed += ApplyAll;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            ApplyAll();
        }

        private void OnDestroy()
        {
            GameSettings.Changed -= ApplyAll;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            if (_runtime != null && QualitySettings.renderPipeline == _runtime)
                QualitySettings.renderPipeline = _original; // 에디터에서 프로젝트 설정이 바뀌지 않도록
            if (_instance == this)
                _instance = null;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _pausedByFocus = false;
            var dead = new List<CanvasScaler>();
            foreach (var s in _scalerBase.Keys)
                if (s == null)
                    dead.Add(s);
            foreach (var s in dead)
                _scalerBase.Remove(s);
            ApplyAll();
        }

        private void ApplyAll()
        {
            // 프레임
            QualitySettings.vSyncCount = GameSettings.VSync ? 1 : 0;
            int limit = GameSettings.FrameLimit;
            Application.targetFrameRate = GameSettings.VSync || limit <= 0 ? -1 : limit;

            // 파이프라인
            var shadows = GameSettings.Shadows;
            if (_runtime != null)
            {
                _runtime.renderScale = GameSettings.RenderScale;
                _runtime.msaaSampleCount = GameSettings.AntiAliasing == AntiAliasingMode.Msaa4 ? 4 : 1;
                switch (shadows)
                {
                    case ShadowLevel.Low:
                        _runtime.mainLightShadowmapResolution = 1024; _runtime.shadowCascadeCount = 1; _runtime.shadowDistance = 30f;
                        break;
                    case ShadowLevel.Medium:
                        _runtime.mainLightShadowmapResolution = 2048; _runtime.shadowCascadeCount = 2; _runtime.shadowDistance = 40f;
                        break;
                    default:
                        _runtime.mainLightShadowmapResolution = 2048; _runtime.shadowCascadeCount = 4; _runtime.shadowDistance = 50f;
                        break;
                }
            }

            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type != LightType.Directional)
                    continue;
                light.shadows = shadows == ShadowLevel.Off ? LightShadows.None
                    : shadows == ShadowLevel.Low ? LightShadows.Hard : LightShadows.Soft;
            }

            var aa = GameSettings.AntiAliasing == AntiAliasingMode.Fxaa ? AntialiasingMode.FastApproximateAntialiasing
                : GameSettings.AntiAliasing == AntiAliasingMode.Smaa ? AntialiasingMode.SubpixelMorphologicalAntiAliasing
                : AntialiasingMode.None;
            foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (cam.TryGetComponent<UniversalAdditionalCameraData>(out var data))
                    data.antialiasing = aa;
            }

            // 블룸: 블룸이 들어 있는 볼륨만 (프로필 복사본을 수정해 에셋은 그대로)
            foreach (var volume in FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                if (volume.sharedProfile == null || !volume.sharedProfile.Has<Bloom>())
                    continue;
                if (volume.profile.TryGet<Bloom>(out var bloom))
                    bloom.active = GameSettings.Bloom;
            }

            // UI 크기
            foreach (var scaler in FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
                    continue;
                if (!_scalerBase.TryGetValue(scaler, out var reference))
                {
                    reference = scaler.referenceResolution;
                    _scalerBase[scaler] = reference;
                }
                scaler.referenceResolution = reference / GameSettings.UiScale;
            }
        }

        private void OnApplicationFocus(bool focus)
        {
            if (!GameSettings.PauseWhenUnfocused && !_pausedByFocus)
                return;
            var clock = FindFirstObjectByType<SimulationClock>();
            if (clock == null || clock.Clock == null || clock.InputLocked)
                return;
            if (!focus)
            {
                if (!clock.Clock.IsPaused)
                {
                    clock.Clock.SetPaused(true);
                    _pausedByFocus = true;
                }
            }
            else if (_pausedByFocus)
            {
                _pausedByFocus = false;
                clock.Clock.SetPaused(false);
            }
        }
    }
}
