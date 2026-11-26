using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Settings
{
    /// <summary>
    /// REVIEW_1115 1-8 조준점 저장 (PlayerPrefs). 지금 조준점은 바꿀 때마다 바로 저장 (프로필에 안 넣어도 다음 실행에 남음),
    /// 내 프로필 3칸은 [내 프로필 n에 저장]을 눌렀을 때만. 바뀌면 Changed — 게임 속 조준점과 설정 창 미리보기가 같이 다시 그림.
    /// 값 · 범위 · 기본 모양은 <see cref="CrosshairStyle"/> (순수 C#).
    /// </summary>
    public static class CrosshairSettings
    {
        private const string CurrentKey = "settings.crosshair";
        private const string SlotKey = "settings.crosshair.profile"; // + 1 ~ 3

        private static bool _loaded;
        private static CrosshairStyle _current = CrosshairStyle.Default;
        private static readonly CrosshairStyle?[] _slots = new CrosshairStyle?[CrosshairProfiles.UserSlots];

        public static event Action Changed;

        public static CrosshairStyle Current
        {
            get { Load(); return _current; }
            set
            {
                Load();
                var v = value.Clamped();
                if (v == _current)
                    return;
                _current = v;
                PlayerPrefs.SetString(CurrentKey, v.Serialize());
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        /// <summary>지금 조준점의 일부만 바꿈 — 예 <c>Modify((ref CrosshairStyle s) => s.DotSize = 3)</c>.</summary>
        public delegate void Edit(ref CrosshairStyle style);

        public static void Modify(Edit edit)
        {
            var s = Current;
            edit(ref s);
            Current = s;
        }

        /// <summary>내 프로필 3칸 (빈 칸 = null).</summary>
        public static IReadOnlyList<CrosshairStyle?> Slots { get { Load(); return _slots; } }

        /// <summary>프로필 고르기 줄 (기본 모양 4 + 내 프로필 3)에서 불러옴. 빈 칸이면 false (지금 조준점 그대로).</summary>
        public static bool LoadProfile(int index)
        {
            var s = CrosshairProfiles.Get(index, Slots);
            if (!s.HasValue)
                return false;
            var v = s.Value;
            v.Visible = Current.Visible; // 표시 켬 / 끔은 프로필과 따로 (꺼 둔 채로 고르다 켜지지 않게)
            Current = v;
            return true;
        }

        /// <summary>지금 조준점을 내 프로필 slot (0 ~ 2)에 저장.</summary>
        public static void SaveToSlot(int slot)
        {
            Load();
            if (slot < 0 || slot >= _slots.Length)
                return;
            _slots[slot] = _current;
            PlayerPrefs.SetString(SlotKey + (slot + 1), _current.Serialize());
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>설정 창 [기본값 복원]: 지금 조준점만 기본 모양(점)으로 — 내 프로필은 그대로.</summary>
        public static void ResetToDefault() => Current = CrosshairStyle.Default;

        private static void Load()
        {
            if (_loaded)
                return;
            _loaded = true;
            if (CrosshairStyle.TryParse(PlayerPrefs.GetString(CurrentKey, string.Empty), out var current))
                _current = current;
            for (int i = 0; i < _slots.Length; i++)
                _slots[i] = CrosshairStyle.TryParse(PlayerPrefs.GetString(SlotKey + (i + 1), string.Empty), out var s) ? s : (CrosshairStyle?)null;
        }
    }
}
