using System;
using System.Collections.Generic;
using System.IO;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Editor.Balance
{
    /// <summary>
    /// 12-2 구간 시작 상태: 봇이 처음부터 플레이하다 어떤 등급에 처음 도달한 순간의 정거장 (세이브와 같은 <see cref="StationState"/>).
    /// 구간 측정은 이 상태를 복원해 다음 등급까지 돌린다 (PHASESET D1).
    /// </summary>
    [Serializable]
    public sealed class BalanceStartState
    {
        public int Seed;
        public int Grade;
        public string GradeName;
        /// <summary>처음부터 이 등급까지 걸린 게임 시간(초).</summary>
        public float ReachedAt;
        public int Population;
        public int Modules;
        public string Created;
        public StationState Station;
    }

    /// <summary>
    /// 구간 시작 상태 파일 보관소: <c>Assets/_Project/Data/Balance/SegmentStarts/grade{등급}_seed{시드}.json</c> (저장소에 커밋 — 두 PC가 같은 출발점,
    /// 앞 구간 수치를 바꿔도 뒤 구간 측정 기준이 흔들리지 않음. 규칙 · 모듈이 바뀌면 Balance Simulator의 "구간 시작 만들기"로 다시 만든다).
    /// 봇이 그 등급에 실제로 도달한 시드만 파일이 생긴다 (사용자 결정 2026-10-12).
    /// </summary>
    public static class BalanceStartLibrary
    {
        public const string Folder = "Assets/_Project/Data/Balance/SegmentStarts";

        /// <summary>구간 시작으로 쓰는 등급: 승리 등급(대형) 다음부터 마지막 바로 앞까지 (초대형 · 도시급 · 국가급 · 대륙급).</summary>
        public static int[] SegmentGrades(StationGradeConfig grades)
        {
            var list = new List<int>();
            for (int g = grades.VictoryGradeIndex + 1; g <= grades.Grades.Count - 2; g++)
                list.Add(g);
            return list.ToArray();
        }

        public static string FileName(int grade, int seed) => $"grade{grade}_seed{seed:00}.json";

        public static string FullFolder => Path.Combine(Directory.GetParent(Application.dataPath).FullName, Folder);

        /// <summary>등급마다 이번에 만든 상태로 교체 (그 등급의 옛 파일은 지움 — 도달하지 못한 시드의 낡은 파일이 섞이지 않게).</summary>
        public static void Save(IReadOnlyList<int> grades, IEnumerable<BalanceStartState> states)
        {
            string folder = FullFolder;
            Directory.CreateDirectory(folder);
            foreach (int grade in grades)
            {
                foreach (var path in Directory.GetFiles(folder, $"grade{grade}_seed*.json"))
                {
                    File.Delete(path);
                    if (File.Exists(path + ".meta"))
                        File.Delete(path + ".meta");
                }
            }
            foreach (var s in states)
                File.WriteAllText(Path.Combine(folder, FileName(s.Grade, s.Seed)), JsonUtility.ToJson(s));
            AssetDatabase.Refresh();
        }

        /// <summary>그 등급의 시작 상태 전부 (시드 순).</summary>
        public static List<BalanceStartState> Load(int grade)
        {
            var list = new List<BalanceStartState>();
            string folder = FullFolder;
            if (!Directory.Exists(folder))
                return list;
            var files = Directory.GetFiles(folder, $"grade{grade}_seed*.json");
            Array.Sort(files, StringComparer.Ordinal);
            foreach (var path in files)
            {
                var s = JsonUtility.FromJson<BalanceStartState>(File.ReadAllText(path));
                if (s != null && s.Station != null)
                    list.Add(s);
            }
            return list;
        }

        public static int Count(int grade)
        {
            string folder = FullFolder;
            return Directory.Exists(folder) ? Directory.GetFiles(folder, $"grade{grade}_seed*.json").Length : 0;
        }
    }
}
