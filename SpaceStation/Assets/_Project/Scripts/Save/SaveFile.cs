using System;
using SpaceStation.Simulation;
using UnityEngine;

namespace SpaceStation.Save
{
    /// <summary>세이브 파일 한 개 (JSON). 형식이 바뀌면 <see cref="SaveService.CurrentVersion"/>을 올린다.</summary>
    [Serializable]
    public sealed class SaveFile
    {
        public int Version = SaveService.CurrentVersion;
        public SaveMeta Meta = new SaveMeta();
        public CameraState Camera = new CameraState();
        public StationState Station = new StationState();
    }

    /// <summary>목록에 보여줄 요약.</summary>
    [Serializable]
    public sealed class SaveMeta
    {
        /// <summary>DateTime.Ticks (현지 시각).</summary>
        public long SavedAtTicks;
        /// <summary>난이도 에셋 이름 (DIFF_Normal 등). 비면 게임 씬 기본값.</summary>
        public string Difficulty;
        public string DifficultyName;
        public string GradeName;
        public int Population;
        public int Modules;
        public float PlaySeconds;
        /// <summary>8-6: 최고 등급(초대형)에 한 번이라도 도달 → 세이브 목록에 '달성' 표시.</summary>
        public bool ReachedTopGrade;
        public string TopGradeName;

        public DateTime SavedAt => new DateTime(SavedAtTicks, DateTimeKind.Local);
    }

    [Serializable]
    public sealed class CameraState
    {
        public bool Valid;
        public Vector3 Focus;
        public float Yaw;
        public float Pitch;
        public float Distance;
    }
}
