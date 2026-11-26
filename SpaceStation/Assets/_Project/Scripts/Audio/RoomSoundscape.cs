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
            [Tooltip("낮 · 밤 음량 배율 (밤에만 나는 풀벌레 = 낮 0)")]
            [Range(0f, 1f)] public float Day = 1f;
            [Range(0f, 1f)] public float Night = 1f;
            [Tooltip("음악 박에 맞춰 시작 (맥동 루프 — 길이가 박 길이의 배수)")]
            public bool OnBeat;
            [Tooltip("끄면 자리 없는 2D 층 (방 전체)")]
            public bool Spatial = true;
            [Tooltip("이 이름의 방 안 물체에 붙어 함께 움직임 (예: 도는 구름 고리) — 비우면 제자리")]
            public string Follow;
            // 2026-11-26 2단계 소리 풍경 (SOUND_LIST_NEW D-7, 작업자 P7): 쇼 · 탈것 · 움직임에 따라 크기
            [Tooltip("그 방 쇼 (시민 회관 상영 · 드론 쇼 · 폭죽 쇼)가 도는 동안 음량 배율 (홀 웅성 = 0, 2초에 걸쳐 넘어감)")]
            [Range(0f, 1f)] public float ShowGain = 1f;
            [Tooltip("플레이어가 탈것에 탄 동안 음량 배율 (탄 쪽 2D 루프가 따로 있는 물체 = 0)")]
            [Range(0f, 1f)] public float RideGain = 1f;
            [Tooltip("0보다 크면 소리 자리가 움직인 빠르기 (m/s)만큼 크기 — 이 빠르기에서 1, 서 있으면 0 (Follow로 붙인 열차 · 곤돌라)")]
            [Min(0f)] public float MotionMeters;
            [Tooltip("켜면 MotionMeters가 위아래로 움직인 빠르기만 셈 (2026-11-26 S-31 하역 체인 = 크레인 집게가 오르내릴 때만, 옆으로 갈 땐 모터 소리만)")]
            public bool MotionVertical;
        }

        public enum ShowPart { Any, During, NotDuring }

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
            [Tooltip("그 방 쇼가 도는 동안만 (관객 감탄 · 함성) / 쇼가 없을 때만 / 늘 (2026-11-26 P7)")]
            public ShowPart Show = ShowPart.Any;
        }

        public ModuleData Module;

        [Header("바탕 (방 전체, 2D 루프)")]
        public SoundCue Bed = new SoundCue { Bus = AudioBus.Ambient, Pitch = Vector2.one };
        [Range(0f, 1f)] public float BedNight = 0.6f;
        [Tooltip("그 방 쇼 (시민 회관 상영 등)가 도는 동안 바탕 음량 배율 (2026-11-26 P7, 1 = 그대로)")]
        [Range(0f, 1f)] public float BedShow = 1f;

        [Header("구역 물체 소리 (그 자리 3D 루프)")]
        public List<Emitter> Emitters = new List<Emitter>();

        [Header("가끔 소리")]
        public List<Scatter> Scatters = new List<Scatter>();

        /// <summary>가끔 소리가 지금 나도 되는지 — 낮 · 밤 (When) · 쇼 (Show) 둘 다 맞아야 (2026-11-26 P7, EditMode RoomSoundscapeTests).</summary>
        public static bool ScatterAllowed(DayPart when, ShowPart show, bool day, bool showing)
            => !(when == DayPart.Day && !day) && !(when == DayPart.Night && day)
               && !(show == ShowPart.During && !showing) && !(show == ShowPart.NotDuring && showing);

        /// <summary>쇼 · 탈것 음량 배율 — showLevel · rideLevel (0 ~ 1, 넘어가는 정도)만큼 ShowGain · RideGain 쪽으로, 둘이 겹치면 곱.</summary>
        public static float StateGain(float showGain, float rideGain, float showLevel, float rideLevel)
            => Mathf.Lerp(1f, showGain, showLevel) * Mathf.Lerp(1f, rideGain, rideLevel);

        /// <summary>소리 자리가 움직인 빠르기 (m/s) — vertical = 위아래만 (Emitter.MotionVertical, 2026-11-26 S-31). dt가 0이면 0.</summary>
        public static float MotionSpeed(Vector3 from, Vector3 to, float dt, bool vertical)
        {
            if (dt <= 1e-4f)
                return 0f;
            return (vertical ? Mathf.Abs(to.y - from.y) : Vector3.Distance(from, to)) / dt;
        }

        [Header("대표 선율 (들어설 때 한 번, 다음 박에)")]
        public SoundCue Motif = new SoundCue { Bus = AudioBus.Music, Pitch = Vector2.one };
        [Tooltip("같은 방에 다시 들어와도 이 시간(초) 안이면 선율을 다시 내지 않음")]
        [Min(0f)] public float MotifCooldown = 30f;
    }
}
