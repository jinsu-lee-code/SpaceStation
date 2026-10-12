using System;
using SpaceStation.Data;
using SpaceStation.Save;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Editor.Balance
{
    /// <summary>
    /// 12-3 시험 경로 (사용자 결정 2026-10-12): 12-2에서 만든 구간 시작 상태(봇이 처음부터 플레이해 그 등급에 도달한 정거장)를
    /// 세이브 슬롯(수동 저장 3)에 넣는다 → 게임에서 [불러오기]로 바로 도시급부터 합성을 시험할 수 있다.
    /// 슬롯에 이미 저장이 있으면 덮어쓸지 묻는다. 난이도는 비워 둠 (게임 씬 기본 난이도).
    /// </summary>
    public static class SegmentStartSaveSlot
    {
        public const string Slot = "slot3";
        private const int CityGrade = 5;

        [MenuItem("SpaceStation/Recipes/Test Save: City Start → Slot 3")]
        public static void CityToSlot3() => Write(CityGrade, 1, Slot, confirm: true);

        /// <summary>그 등급 · 시드의 시작 상태를 슬롯에 씀. 성공하면 true.</summary>
        public static bool Write(int grade, int seed, string slot, bool confirm)
        {
            BalanceStartState start = null;
            foreach (var s in BalanceStartLibrary.Load(grade))
                if (s.Seed == seed)
                    start = s;
            if (start == null)
            {
                Debug.LogError($"[Recipes] 등급 {grade} 시드 {seed} 구간 시작 상태가 없음 (Balance Simulator '구간 시작 만들기')");
                return false;
            }
            if (confirm && SaveService.TryRead(slot, out _) != SaveSlotStatus.Empty
                && !EditorUtility.DisplayDialog("시험 저장", $"{SaveService.TitleOf(slot)}에 이미 저장이 있습니다. 덮어쓸까요?", "덮어쓰기", "취소"))
                return false;

            var grades = AssetDatabase.LoadAssetAtPath<StationGradeConfig>("Assets/_Project/Data/StationGrades.asset");
            int highest = Math.Max(start.Grade, start.Station.HighestGrade);
            var file = new SaveFile { Station = start.Station };
            file.Meta = new SaveMeta
            {
                SavedAtTicks = DateTime.Now.Ticks,
                GradeName = grades.Grades[start.Grade].DisplayName,
                Population = start.Population,
                Modules = start.Modules,
                PlaySeconds = start.ReachedAt,
                ReachedTopGrade = highest > grades.VictoryGradeIndex,
                TopGradeName = grades.Grades[Math.Min(highest, grades.Grades.Count - 1)].DisplayName,
            };
            bool ok = SaveService.Write(slot, file, null);
            if (ok)
                Debug.Log($"[Recipes] {SaveService.TitleOf(slot)} ← {start.GradeName} 시작 상태 (시드 {seed}, 인구 {start.Population}, 모듈 {start.Modules})");
            return ok;
        }
    }
}
