using System;
using UnityEngine;

namespace SpaceStation.Settings
{
    public enum WindowMode { Fullscreen, Borderless, Windowed }
    public enum GraphicsPreset { Low, Medium, High, Custom }
    public enum ShadowLevel { Off, Low, Medium, High }
    public enum AntiAliasingMode { Off, Fxaa, Smaa, Msaa4 }
    public enum HintMode { Always, FirstMinute, Off }
    /// <summary>7-2 노후 모듈 표시: 기존(갈색 틴트) / 밝은 테두리.</summary>
    public enum WornDisplay { Tint, Rim }

    /// <summary>
    /// 5-10 설정값 (화면·게임). PlayerPrefs에 즉시 저장되고, 바뀌면 Changed로 알린다.
    /// 소리 볼륨은 <see cref="SpaceStation.Audio.AudioVolumes"/>가 따로 가진다.
    /// 실제 적용(그래픽·창·프레임)은 <see cref="SettingsApplier"/>, 게임 쪽 값은 각 사용처가 읽는다.
    /// </summary>
    public static class GameSettings
    {
        public static readonly int[] FrameLimits = { 30, 60, 120, 144, 0 }; // 0 = 무제한
        public static readonly float[] SpeedChoices = { 1f, 2f, 4f };
        /// <summary>자동 저장 간격(게임 시간 분), 0 = 끔.</summary>
        public static readonly int[] AutosaveChoices = { 0, 1, 3, 5 };
        private const int DefaultAutosaveIndex = 2;

        private const string Prefix = "settings.";
        private static bool _loaded;

        // 화면
        private static GraphicsPreset _preset = GraphicsPreset.High;
        private static ShadowLevel _shadows = ShadowLevel.High;
        private static AntiAliasingMode _antiAliasing = AntiAliasingMode.Smaa;
        private static float _renderScale = 1f;
        private static bool _bloom = true;
        private static bool _vignette = true;
        private static int _frameLimitIndex = 4;
        private static bool _vSync = true;
        private static float _uiScale = 1f;
        // 게임
        private static HintMode _hints = HintMode.FirstMinute;
        private static float _orbitSensitivity = 1f;
        private static float _zoomSensitivity = 1f;
        private static int _defaultSpeedIndex;
        private static bool _pauseWhenUnfocused;
        private static int _autosaveIndex = DefaultAutosaveIndex;
        private static WornDisplay _wornDisplay = WornDisplay.Rim;
        private static bool _tutorialPending = true;
        private static bool _interiorPause = true;
        private static bool _headBob = true;
        private static bool _padDirectZoom;

        public static event Action Changed;

        public static GraphicsPreset Preset { get { Load(); return _preset; } }
        public static ShadowLevel Shadows { get { Load(); return _shadows; } set => SetDetail(ref _shadows, value, "shadows"); }
        public static AntiAliasingMode AntiAliasing { get { Load(); return _antiAliasing; } set => SetDetail(ref _antiAliasing, value, "aa"); }
        public static float RenderScale { get { Load(); return _renderScale; } set => SetDetail(ref _renderScale, Mathf.Clamp(value, 0.5f, 1f), "renderScale"); }
        public static bool Bloom { get { Load(); return _bloom; } set => SetDetail(ref _bloom, value, "bloom"); }
        public static bool Vignette { get { Load(); return _vignette; } set => Set(ref _vignette, value, "vignette"); }
        public static int FrameLimitIndex { get { Load(); return _frameLimitIndex; } set => Set(ref _frameLimitIndex, Mathf.Clamp(value, 0, FrameLimits.Length - 1), "frameLimit"); }
        public static bool VSync { get { Load(); return _vSync; } set => Set(ref _vSync, value, "vsync"); }
        public static float UiScale { get { Load(); return _uiScale; } set => Set(ref _uiScale, Mathf.Clamp(value, 0.8f, 1.3f), "uiScale"); }
        public static HintMode Hints { get { Load(); return _hints; } set => Set(ref _hints, value, "hints"); }
        public static float OrbitSensitivity { get { Load(); return _orbitSensitivity; } set => Set(ref _orbitSensitivity, Mathf.Clamp(value, 0.5f, 2f), "orbit"); }
        public static float ZoomSensitivity { get { Load(); return _zoomSensitivity; } set => Set(ref _zoomSensitivity, Mathf.Clamp(value, 0.5f, 2f), "zoom"); }
        public static int DefaultSpeedIndex { get { Load(); return _defaultSpeedIndex; } set => Set(ref _defaultSpeedIndex, Mathf.Clamp(value, 0, SpeedChoices.Length - 1), "speed"); }
        public static bool PauseWhenUnfocused { get { Load(); return _pauseWhenUnfocused; } set => Set(ref _pauseWhenUnfocused, value, "pauseUnfocused"); }

        public static int AutosaveIndex { get { Load(); return _autosaveIndex; } set => Set(ref _autosaveIndex, Mathf.Clamp(value, 0, AutosaveChoices.Length - 1), "autosave"); }

        public static WornDisplay WornDisplay { get { Load(); return _wornDisplay; } set => Set(ref _wornDisplay, value, "wornDisplay"); }

        /// <summary>Phase 9: 다음 새 게임에서 튜토리얼 시작 (처음엔 켜짐, 끝내거나 건너뛰면 꺼짐).</summary>
        public static bool TutorialPending { get { Load(); return _tutorialPending; } set => Set(ref _tutorialPending, value, "tutorial"); }

        /// <summary>Phase 11: 내부 방문 중 시뮬레이션 일시정지 (끄면 시간이 계속 흐름).</summary>
        public static bool InteriorPause { get { Load(); return _interiorPause; } set => Set(ref _interiorPause, value, "interiorPause"); }

        /// <summary>11-9: 내부에서 걸을 때 시점 흔들림 (멀미가 나면 끔, 발소리는 그대로).</summary>
        public static bool HeadBob { get { Load(); return _headBob; } set => Set(ref _headBob, value, "headBob"); }

        /// <summary>11-13: 휴대 패드를 들면 바로 확대(조작 화면) — 끄면 들기 → 배속 순환 키로 확대 (단계별, 기본).</summary>
        public static bool PadDirectZoom { get { Load(); return _padDirectZoom; } set => Set(ref _padDirectZoom, value, "padDirectZoom"); }

        public static int FrameLimit => FrameLimits[FrameLimitIndex];
        public static int AutosaveMinutes => AutosaveChoices[AutosaveIndex];

        /// <summary>그래픽 프리셋: 아래 세부 항목을 한꺼번에 바꾼다 (세부 항목을 따로 바꾸면 "사용자 지정").</summary>
        public static void ApplyPreset(GraphicsPreset preset)
        {
            Load();
            switch (preset)
            {
                case GraphicsPreset.Low:
                    _shadows = ShadowLevel.Low; _antiAliasing = AntiAliasingMode.Off; _renderScale = 0.75f; _bloom = false;
                    break;
                case GraphicsPreset.Medium:
                    _shadows = ShadowLevel.Medium; _antiAliasing = AntiAliasingMode.Fxaa; _renderScale = 0.9f; _bloom = true;
                    break;
                case GraphicsPreset.High:
                    _shadows = ShadowLevel.High; _antiAliasing = AntiAliasingMode.Smaa; _renderScale = 1f; _bloom = true;
                    break;
                default:
                    return;
            }
            _preset = preset;
            SaveAll();
            Changed?.Invoke();
        }

        public static void ResetDisplay()
        {
            Load();
            _vignette = true;
            _frameLimitIndex = 4;
            _vSync = true;
            _uiScale = 1f;
            ApplyPreset(GraphicsPreset.High); // 저장·알림 포함
        }

        public static void ResetGame()
        {
            Load();
            _hints = HintMode.FirstMinute;
            _orbitSensitivity = 1f;
            _zoomSensitivity = 1f;
            _defaultSpeedIndex = 0;
            _pauseWhenUnfocused = false;
            _autosaveIndex = DefaultAutosaveIndex;
            _wornDisplay = WornDisplay.Rim;
            _interiorPause = true;
            _headBob = true;
            _padDirectZoom = false;
            SaveAll();
            Changed?.Invoke();
        }

        // ---------------- 저장 ----------------

        private static void Load()
        {
            if (_loaded)
                return;
            _loaded = true;
            _preset = (GraphicsPreset)PlayerPrefs.GetInt(Prefix + "preset", (int)_preset);
            _shadows = (ShadowLevel)PlayerPrefs.GetInt(Prefix + "shadows", (int)_shadows);
            _antiAliasing = (AntiAliasingMode)PlayerPrefs.GetInt(Prefix + "aa", (int)_antiAliasing);
            _renderScale = PlayerPrefs.GetFloat(Prefix + "renderScale", _renderScale);
            _bloom = PlayerPrefs.GetInt(Prefix + "bloom", _bloom ? 1 : 0) == 1;
            _vignette = PlayerPrefs.GetInt(Prefix + "vignette", _vignette ? 1 : 0) == 1;
            _frameLimitIndex = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "frameLimit", _frameLimitIndex), 0, FrameLimits.Length - 1);
            _vSync = PlayerPrefs.GetInt(Prefix + "vsync", _vSync ? 1 : 0) == 1;
            _uiScale = PlayerPrefs.GetFloat(Prefix + "uiScale", _uiScale);
            _hints = (HintMode)PlayerPrefs.GetInt(Prefix + "hints", (int)_hints);
            _orbitSensitivity = PlayerPrefs.GetFloat(Prefix + "orbit", _orbitSensitivity);
            _zoomSensitivity = PlayerPrefs.GetFloat(Prefix + "zoom", _zoomSensitivity);
            _defaultSpeedIndex = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "speed", _defaultSpeedIndex), 0, SpeedChoices.Length - 1);
            _pauseWhenUnfocused = PlayerPrefs.GetInt(Prefix + "pauseUnfocused", _pauseWhenUnfocused ? 1 : 0) == 1;
            _autosaveIndex = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "autosave", _autosaveIndex), 0, AutosaveChoices.Length - 1);
            _wornDisplay = (WornDisplay)Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "wornDisplay", (int)_wornDisplay), 0, 1);
            _tutorialPending = PlayerPrefs.GetInt(Prefix + "tutorial", _tutorialPending ? 1 : 0) == 1;
            _interiorPause = PlayerPrefs.GetInt(Prefix + "interiorPause", _interiorPause ? 1 : 0) == 1;
            _headBob = PlayerPrefs.GetInt(Prefix + "headBob", _headBob ? 1 : 0) == 1;
            _padDirectZoom = PlayerPrefs.GetInt(Prefix + "padDirectZoom", _padDirectZoom ? 1 : 0) == 1;
        }

        private static void SaveAll()
        {
            PlayerPrefs.SetInt(Prefix + "preset", (int)_preset);
            PlayerPrefs.SetInt(Prefix + "shadows", (int)_shadows);
            PlayerPrefs.SetInt(Prefix + "aa", (int)_antiAliasing);
            PlayerPrefs.SetFloat(Prefix + "renderScale", _renderScale);
            PlayerPrefs.SetInt(Prefix + "bloom", _bloom ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "vignette", _vignette ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "frameLimit", _frameLimitIndex);
            PlayerPrefs.SetInt(Prefix + "vsync", _vSync ? 1 : 0);
            PlayerPrefs.SetFloat(Prefix + "uiScale", _uiScale);
            PlayerPrefs.SetInt(Prefix + "hints", (int)_hints);
            PlayerPrefs.SetFloat(Prefix + "orbit", _orbitSensitivity);
            PlayerPrefs.SetFloat(Prefix + "zoom", _zoomSensitivity);
            PlayerPrefs.SetInt(Prefix + "speed", _defaultSpeedIndex);
            PlayerPrefs.SetInt(Prefix + "pauseUnfocused", _pauseWhenUnfocused ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "autosave", _autosaveIndex);
            PlayerPrefs.SetInt(Prefix + "wornDisplay", (int)_wornDisplay);
            PlayerPrefs.SetInt(Prefix + "tutorial", _tutorialPending ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "interiorPause", _interiorPause ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "headBob", _headBob ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "padDirectZoom", _padDirectZoom ? 1 : 0);
            PlayerPrefs.Save();
        }

        private static void Set<T>(ref T field, T value, string key)
        {
            Load();
            if (Equals(field, value))
                return;
            field = value;
            SaveAll();
            Changed?.Invoke();
        }

        /// <summary>프리셋에 속한 세부 항목: 바꾸면 프리셋이 "사용자 지정"이 된다.</summary>
        private static void SetDetail<T>(ref T field, T value, string key)
        {
            Load();
            if (Equals(field, value))
                return;
            field = value;
            _preset = GraphicsPreset.Custom;
            SaveAll();
            Changed?.Invoke();
        }
    }
}
