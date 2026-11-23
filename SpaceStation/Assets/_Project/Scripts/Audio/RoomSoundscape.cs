using System;
using System.Collections.Generic;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Audio
{
    /// <summary>
    /// 사운드 개편 D-7 · D-7b (2026-11-23): 모듈 하나의 소리 풍경 — 지금 있는 방에서만 울림 (D-4).
    /// 바탕(2D 루프) + 구역 물체 소리(그 자리 3D 루프) + 가끔 소리(구역 안 아무 데서나, 음이 있으면 음계 높이 · 다음 박) + 대표 선율(들어설 때).
    /// 자리 = 내부 템플릿 프리팹 기준 좌표. 음이 있는 소리의 음계 = 내부 음악 4곡이 겹쳐도 안 부딪히는 음 (A · C · D · E · G).
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/Audio/Room Soundscape", fileName = "SS_")]
    public sealed class RoomSoundscape : ScriptableObject
    {
        public enum DayPart { Always, Day, Night }

        [Serializable]
        public sealed class Emitter
        {
            public string Name;
            public SoundCue Cue = new SoundCue { Bus = AudioBus.Ambient, Pitch = Vector2.one };
            [Tooltip("템플릿 프리팹 기준 자리")]
            public Vector3 Position;
            [Min(0.1f)] public float MinDistance = 1.5f;
            [Min(0.5f)] public float MaxDistance = 10f;
            [Tooltip("밤 음량 배율")]
            [Range(0f, 1f)] public float Night = 1f;
            [Tooltip("음악 박에 맞춰 시작 (맥동 루프 — 길이가 박 길이의 배수)")]
            public bool OnBeat;
        }

        [Serializable]
        public sealed class Scatter
        {
            public string Name;
            public SoundCue Cue = new SoundCue { Bus = AudioBus.Ambient };
            [Tooltip("구역 가운데 · 크기 (템플릿 프리팹 기준) — 이 상자 안 아무 데서나 남")]
            public Vector3 Center;
            public Vector3 Size;
            [Tooltip("다음 소리까지 (초, 무작위)")]
            public Vector2 Interval = new Vector2(6f, 14f);
            public DayPart When = DayPart.Always;
            [Min(0.1f)] public float MinDistance = 2f;
            [Min(0.5f)] public float MaxDistance = 15f;
            [Tooltip("음이 있는 소리: 녹음된 음 (MIDI, 60 = 가운데 도) → Notes 중 하나로 높이를 바꿔 음악의 다음 박에")]
            public bool Tonal;
            public int BaseNote = 72;
            public int[] Notes = Array.Empty<int>();
        }

        public ModuleData Module;

        [Header("바탕 (방 전체, 2D 루프)")]
        public SoundCue Bed = new SoundCue { Bus = AudioBus.Ambient, Pitch = Vector2.one };
        [Range(0f, 1f)] public float BedNight = 0.6f;

        [Header("구역 물체 소리 (그 자리 3D 루프)")]
        public List<Emitter> Emitters = new List<Emitter>();

        [Header("가끔 소리")]
        public List<Scatter> Scatters = new List<Scatter>();

        [Header("대표 선율 (들어설 때 한 번, 다음 박에)")]
        public SoundCue Motif = new SoundCue { Bus = AudioBus.Music, Pitch = Vector2.one };
        [Tooltip("같은 방에 다시 들어와도 이 시간(초) 안이면 선율을 다시 내지 않음")]
        [Min(0f)] public float MotifCooldown = 30f;
    }
}
