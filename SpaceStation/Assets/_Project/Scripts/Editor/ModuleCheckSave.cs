using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Save;
using SpaceStation.Simulation;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 내부 확인용 세이브 (사용자 요청 2026-10-19): 모든 모듈(진화 모듈 포함, 코어 제외)을 한 개씩 코어에 이어 붙인 정거장을
    /// 수동 저장 3에 쓴다 → 게임에서 [불러오기]로 모듈마다 들어가 내부를 확인. 새 모듈 에셋을 만들면 자동으로 포함된다.
    /// 배치: 한 층(y = 0)에 큰 모듈부터 코어 가까운 자리로, 기존 덩어리에 면이 닿는 자리만 (= 전부 코어에 연결).
    /// 자원은 한도까지 채우고 인구는 수용 인구만큼 (주민이 방에 보이게). 비용 · 해금 · 배치 규칙은 무시 (확인 전용).
    /// </summary>
    public static class ModuleCheckSave
    {
        public const string Slot = "slot3";
        private const string DataRoot = "Assets/_Project/Data";
        private const int SearchRadius = 18;

        [MenuItem("SpaceStation/Module Check (전체 모듈 1개씩 → 수동 저장 3)")]
        public static void WriteToSlot3() => Write(Slot, confirm: true);

        /// <summary>세이브를 만들어 슬롯에 씀. 성공하면 요약, 실패 · 취소면 null.</summary>
        public static string Write(string slot, bool confirm)
        {
            if (confirm && SaveService.TryRead(slot, out _) != SaveSlotStatus.Empty
                && !EditorUtility.DisplayDialog("모듈 확인 세이브", $"{SaveService.TitleOf(slot)}에 이미 저장이 있습니다. 덮어쓸까요?", "덮어쓰기", "취소"))
                return null;

            var balance = AssetDatabase.LoadAssetAtPath<BalanceConfig>($"{DataRoot}/BalanceConfig.asset");
            var grades = AssetDatabase.LoadAssetAtPath<StationGradeConfig>($"{DataRoot}/StationGrades.asset");
            var core = AssetDatabase.LoadAssetAtPath<ModuleData>($"{DataRoot}/Modules/MD_Core.asset");
            var research = new List<ResearchCategoryData>();
            foreach (var guid in AssetDatabase.FindAssets("t:ResearchCategoryData", new[] { $"{DataRoot}/Research" }))
                research.Add(AssetDatabase.LoadAssetAtPath<ResearchCategoryData>(AssetDatabase.GUIDToAssetPath(guid)));
            var rng = new System.Random(1);
            var sim = new StationSimulation(new StationSimulationSettings
            {
                Balance = balance,
                Grades = grades,
                CoreModule = core,
                AdjacencyRules = AssetDatabase.LoadAssetAtPath<AdjacencyRuleSet>($"{DataRoot}/AdjacencyRules.asset"),
                Random01 = () => (float)rng.NextDouble(),
                ResearchCategories = research,
                ResearchCaps = AssetDatabase.LoadAssetAtPath<ResearchLevelCapConfig>($"{DataRoot}/Research/ResearchLevelCaps.asset"),
                Residents = AssetDatabase.LoadAssetAtPath<ResidentConfig>($"{DataRoot}/Residents/ResidentConfig.asset"),
            });

            var modules = new List<ModuleData>();
            foreach (var guid in AssetDatabase.FindAssets("t:ModuleData", new[] { $"{DataRoot}/Modules" }))
            {
                var m = AssetDatabase.LoadAssetAtPath<ModuleData>(AssetDatabase.GUIDToAssetPath(guid));
                if (m != null && m != core && m.CellOffsets != null && m.CellOffsets.Count > 0)
                    modules.Add(m);
            }
            // 큰 모듈부터 (코어 가까이) · 같은 크기는 이름순 (매번 같은 배치)
            modules.Sort((a, b) => a.CellOffsets.Count != b.CellOffsets.Count ? b.CellOffsets.Count.CompareTo(a.CellOffsets.Count)
                : string.CompareOrdinal(a.name, b.name));

            var origins = new List<Vector3Int>();
            for (int x = -SearchRadius; x <= SearchRadius; x++)
                for (int z = -SearchRadius; z <= SearchRadius; z++)
                    origins.Add(new Vector3Int(x, 0, z));
            origins.Sort((a, b) =>
            {
                float da = (a.x - 0.5f) * (a.x - 0.5f) + (a.z - 0.5f) * (a.z - 0.5f);
                float db = (b.x - 0.5f) * (b.x - 0.5f) + (b.z - 0.5f) * (b.z - 0.5f);
                return da != db ? da.CompareTo(db) : a.x != b.x ? a.x.CompareTo(b.x) : a.z.CompareTo(b.z);
            });

            var failed = new List<string>();
            foreach (var data in modules)
            {
                if (!PlaceTouching(sim.Grid, data, origins))
                    failed.Add(data.name);
            }

            // 자원 한도 · 수용 인구까지 (틱으로 저장 한도 · 수용 인구 · 명단을 갱신)
            sim.Tick(1f);
            sim.Resources.SetPopulation(sim.Resources.HousingCapacity);
            for (int i = 0; i < 2; i++)
            {
                foreach (ResourceType t in Enum.GetValues(typeof(ResourceType)))
                    sim.Resources.SetStock(t, sim.Resources.GetCapacity(t));
                sim.Tick(1f);
            }
            foreach (ResourceType t in Enum.GetValues(typeof(ResourceType)))
                sim.Resources.SetStock(t, sim.Resources.GetCapacity(t));

            var file = new SaveFile { Station = StationStateSerializer.Capture(sim) };
            int grade = Mathf.Clamp(sim.Progression.GradeIndex, 0, grades.Grades.Count - 1);
            file.Meta = new SaveMeta
            {
                SavedAtTicks = DateTime.Now.Ticks,
                GradeName = grades.Grades[grade].DisplayName,
                Population = sim.Resources.Population,
                Modules = sim.Grid.ModuleCount,
                PlaySeconds = sim.ElapsedSeconds,
                TopGradeName = grades.Grades[grade].DisplayName,
            };
            if (!SaveService.Write(slot, file, null))
                return null;
            string summary = $"{SaveService.TitleOf(slot)} ← 모듈 확인 정거장 (모듈 {sim.Grid.ModuleCount}개 = 코어 + {modules.Count - failed.Count}/{modules.Count}, 인구 {sim.Resources.Population})"
                             + (failed.Count > 0 ? $" · 자리 못 찾음: {string.Join(", ", failed)}" : "");
            Debug.Log("[ModuleCheck] " + summary);
            return summary;
        }

        /// <summary>코어에서 가까운 순으로, 기존 모듈에 면이 닿는 첫 자리(회전 0~3)에 놓는다.</summary>
        private static bool PlaceTouching(StationGrid grid, ModuleData data, List<Vector3Int> origins)
        {
            foreach (var origin in origins)
            {
                for (int rot = 0; rot < 4; rot++)
                {
                    if (!grid.CanPlace(data, origin, rot))
                        continue;
                    var cells = StationGrid.ResolveCells(data.CellOffsets, origin, rot);
                    if (!Touches(grid, cells))
                        continue;
                    if (grid.TryPlace(data, origin, rot, out _))
                        return true;
                }
            }
            return false;
        }

        private static bool Touches(StationGrid grid, Vector3Int[] cells)
        {
            foreach (var c in cells)
            {
                foreach (var d in GridDirections.Faces)
                {
                    if (grid.IsOccupied(c + d) && Array.IndexOf(cells, c + d) < 0)
                        return true;
                }
            }
            return false;
        }
    }
}
