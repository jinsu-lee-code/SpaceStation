using System;
using UnityEngine;

namespace SpaceStation.Audio
{
    /// <summary>
    /// 볼륨 설정 (마스터·음악·효과음·환경음). PlayerPrefs에 저장되며 5-10 설정창이 이 값을 바꾼다.
    /// 효과음 값은 UI·효과음 묶음에 함께 적용된다.
    /// </summary>
    public static class AudioVolumes
    {
        private const string MasterKey = "audio.master";
        private const string MusicKey = "audio.music";
        private const string EffectsKey = "audio.effects";
        private const string AmbientKey = "audio.ambient";

        private static bool _loaded;
        private static float _master = 0.8f;
        private static float _music = 0.7f;
        private static float _effects = 1f;
        private static float _ambient = 1f;

        public static event Action Changed;

        public static float Master { get { Load(); return _master; } set => Set(ref _master, MasterKey, value); }
        public static float Music { get { Load(); return _music; } set => Set(ref _music, MusicKey, value); }
        public static float Effects { get { Load(); return _effects; } set => Set(ref _effects, EffectsKey, value); }
        public static float Ambient { get { Load(); return _ambient; } set => Set(ref _ambient, AmbientKey, value); }

        /// <summary>묶음별 배율 (마스터는 AudioListener.volume으로 따로 적용).</summary>
        public static float For(AudioBus bus)
        {
            switch (bus)
            {
                case AudioBus.Music: return Music;
                case AudioBus.Ambient: return Ambient;
                default: return Effects;
            }
        }

        private static void Load()
        {
            if (_loaded)
                return;
            _loaded = true;
            _master = PlayerPrefs.GetFloat(MasterKey, _master);
            _music = PlayerPrefs.GetFloat(MusicKey, _music);
            _effects = PlayerPrefs.GetFloat(EffectsKey, _effects);
            _ambient = PlayerPrefs.GetFloat(AmbientKey, _ambient);
        }

        private static void Set(ref float field, string key, float value)
        {
            Load();
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(field, value))
                return;
            field = value;
            PlayerPrefs.SetFloat(key, value);
            Changed?.Invoke();
        }
    }
}
