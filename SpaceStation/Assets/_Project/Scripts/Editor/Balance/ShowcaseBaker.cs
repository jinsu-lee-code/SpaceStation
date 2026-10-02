using System.Collections.Generic;
using System.IO;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEditor;
using UnityEngine;
using Random = System.Random;

namespace SpaceStation.Editor.Balance
{
    /// <summary>
    /// 5-9 메인 메뉴 전시 정거장: 밸런스 봇을 여러 시드로 돌려 목표 모듈 수에 먼저 도달한 정거장을
    /// <see cref="ShowcaseLayout"/> 에셋으로 저장한다. 실제 게임 규칙으로 지은 배치라 받침·연결이 모두 유효하다.
    /// </summary>
    public static class ShowcaseBaker
    {
        private const string DataRoot = "Assets/_Project/Data";
        private const string LayoutPath = DataRoot + "/Showcase/ShowcaseLayout.asset";

        [MenuItem("SpaceStation/Menu/Bake Showcase Layout (Bot)")]
        public static void BakeMenu() => Bake(100, 1, 12, 120f);

        [MenuItem("SpaceStation/Menu/Generate Showcase Layout")]
        public static void GenerateMenu() => Generate(100, 7);

        /// <summary>전시용 구성 (모듈 이름 → 개수). 모든 종류를 고르게 보여 준다.</summary>
        private static readonly (string file, int count)[] Mix =
        {
            ("MD_Habitat", 16), ("MD_Solar", 16), ("MD_Battery", 6), ("MD_Oxygen", 7), ("MD_WaterRecycler", 6),
            ("MD_Farm", 8), ("MD_Storage", 6), ("MD_Medical", 5), ("MD_Recreation", 5), ("MD_MaintenanceBay", 3),
            ("MD_MiningDock", 6), ("MD_Shield", 5), ("MD_Turret", 7), ("MD_ResearchLab", 4), // Phase 6 연구소

        };

        /// <summary>
        /// 전시용 배치 생성: 실제 배치 규칙(인접·받침·방향)으로 유효한 자리만 쓰되, 비용·해금은 무시하고
        /// 모양을 위해 점수로 고른다 — 십자 모양 팔로 넓게 퍼지고(층은 -1~2), 태양광·도킹·포탑은 바깥쪽, 실드는 위층.
        /// </summary>
        public static string Generate(int count, int seed)
        {
            var core = AssetDatabase.LoadAssetAtPath<ModuleData>($"{DataRoot}/Modules/MD_Core.asset");
            var grid = new SpaceStation.Core.StationGrid();
            grid.TryPlace(core, Vector3Int.zero, 0, out var coreInstance);

            var bag = new List<ModuleData>();
            foreach (var (file, n) in Mix)
            {
                var data = AssetDatabase.LoadAssetAtPath<ModuleData>($"{DataRoot}/Modules/{file}.asset");
                for (int i = 0; i < n && data != null; i++)
                    bag.Add(data);
            }
            var rng = new Random(seed);
            for (int i = bag.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (bag[i], bag[j]) = (bag[j], bag[i]);
            }

            var center = new Vector2(0.5f, 0.5f);
            var entries = new List<ShowcaseLayout.Entry>();
            var candidates = new HashSet<Vector3Int>();
            int failed = 0;
            foreach (var data in bag)
            {
                if (entries.Count >= count)
                    break;
                // 빈 이웃 셀 모으기
                candidates.Clear();
                foreach (var m in grid.Modules)
                    foreach (var c in m.Cells)
                        foreach (var d in SpaceStation.Core.GridDirections.Faces)
                        {
                            var n = c + d;
                            if (!grid.IsOccupied(n) && n.y >= -1 && n.y <= 2)
                                candidates.Add(n);
                        }

                string role = data.name;
                bool outer = role == "MD_Solar" || role == "MD_MiningDock" || role == "MD_Turret";
                float bestScore = float.MinValue;
                Vector3Int bestOrigin = default;
                int bestRotation = 0;
                foreach (var cell in candidates)
                {
                    for (int rot = 0; rot < 4; rot++)
                    {
                        foreach (var offset in data.CellOffsets)
                        {
                            var origin = cell - SpaceStation.Core.GridDirections.Rotate(offset, rot);
                            if (SpaceStation.Core.PlacementRules.Evaluate(grid, data, origin, rot) != SpaceStation.Core.PlacementResult.Valid)
                                continue;
                            var cells = SpaceStation.Core.StationGrid.ResolveCells(data.CellOffsets, origin, rot);
                            float score = Score(cells, center, role, outer) + (float)rng.NextDouble() * 0.9f;
                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestOrigin = origin;
                                bestRotation = rot;
                            }
                        }
                    }
                }
                if (bestScore == float.MinValue || !grid.TryPlace(data, bestOrigin, bestRotation, out _))
                {
                    failed++;
                    continue;
                }
                entries.Add(new ShowcaseLayout.Entry { Module = data, Origin = bestOrigin, Rotation = bestRotation });
            }

            var layout = AssetDatabase.LoadAssetAtPath<ShowcaseLayout>(LayoutPath);
            if (layout == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LayoutPath));
                layout = ScriptableObject.CreateInstance<ShowcaseLayout>();
                AssetDatabase.CreateAsset(layout, LayoutPath);
            }
            layout.SetEntries(entries, $"전시 생성기 seed {seed}, 모듈 {entries.Count} (+코어)");
            EditorUtility.SetDirty(layout);
            AssetDatabase.SaveAssets();
            string summary = $"[ShowcaseBaker] {layout.Source}, 자리 없음 {failed}";
            Debug.Log(summary);
            return summary;
        }

        private static float Score(Vector3Int[] cells, Vector2 center, string role, bool outer)
        {
            float score = 0f;
            foreach (var c in cells)
            {
                var flat = new Vector2(c.x, c.z) - center;
                float r = flat.magnitude;
                float angle = Mathf.Atan2(flat.y, flat.x);
                // 목표 모양: 가운데 허브 + 십자 팔 4개. 팔 방향일수록 더 멀리까지 허용
                float arm = Mathf.Max(0f, Mathf.Cos(angle * 4f));
                float reach = 2.6f + 4.2f * arm * arm;
                if (r > reach)
                    score -= (r - reach) * 3f;
                score += outer ? r * 0.45f : -r * 0.35f; // 안쪽부터 채우고, 바깥 역할은 가장자리로
                score -= Mathf.Abs(c.y - 0.5f) * (role == "MD_Shield" ? -0.6f : 0.7f); // 실드는 위아래로, 나머지는 가운데 층
                if (role == "MD_Shield" && c.y < 1)
                    score -= 1.5f;
            }
            return score / cells.Length;
        }

        /// <returns>요약</returns>
        public static string Bake(int targetModules, int firstSeed, int seeds, float maxMinutes)
        {
            var balance = AssetDatabase.LoadAssetAtPath<BalanceConfig>($"{DataRoot}/BalanceConfig.asset");
            var grades = AssetDatabase.LoadAssetAtPath<StationGradeConfig>($"{DataRoot}/StationGrades.asset");
            var easy = AssetDatabase.LoadAssetAtPath<DifficultyPreset>($"{DataRoot}/Difficulty/DIFF_Easy.asset");
            if (easy != null)
            {
                balance = easy.ApplyTo(balance); // 게임 오버 없이 크게 자라도록
                grades = easy.ApplyTo(grades);
            }
            var core = AssetDatabase.LoadAssetAtPath<ModuleData>($"{DataRoot}/Modules/MD_Core.asset");
            var adjacency = AssetDatabase.LoadAssetAtPath<AdjacencyRuleSet>($"{DataRoot}/AdjacencyRules.asset");
            var events = new List<GameEventData>();
            foreach (var guid in AssetDatabase.FindAssets("t:GameEventData", new[] { $"{DataRoot}/Events" }))
                events.Add(AssetDatabase.LoadAssetAtPath<GameEventData>(AssetDatabase.GUIDToAssetPath(guid)));
            var buildable = new List<ModuleData>();
            foreach (var guid in AssetDatabase.FindAssets("t:ModuleData", new[] { $"{DataRoot}/Modules" }))
            {
                var m = AssetDatabase.LoadAssetAtPath<ModuleData>(AssetDatabase.GUIDToAssetPath(guid));
                if (m != null && m != core)
                    buildable.Add(m);
            }

            StationSimulation best = null;
            int bestSeed = 0;
            float bestTime = 0f;
            var log = new System.Text.StringBuilder();
            for (int seed = firstSeed; seed < firstSeed + seeds; seed++)
            {
                var rng = new Random(seed);
                var sim = new StationSimulation(new StationSimulationSettings
                {
                    Balance = balance,
                    Grades = grades,
                    CoreModule = core,
                    Events = events,
                    AdjacencyRules = adjacency,
                    Random01 = () => (float)rng.NextDouble(),
                });
                var bot = new BalanceBot(sim, buildable);
                while (sim.ElapsedSeconds < maxMinutes * 60f && sim.Grid.ModuleCount < targetModules && !sim.Session.IsGameOver)
                {
                    bot.Decide();
                    sim.Tick(1f);
                }
                log.AppendLine($"seed {seed}: 모듈 {sim.Grid.ModuleCount}, {sim.ElapsedSeconds / 60f:0.0}분, 파손 {sim.Damage.DamagedCount}");
                if (best == null || sim.Grid.ModuleCount > best.Grid.ModuleCount
                    || (sim.Grid.ModuleCount == best.Grid.ModuleCount && sim.Damage.DamagedCount < best.Damage.DamagedCount))
                {
                    best = sim;
                    bestSeed = seed;
                    bestTime = sim.ElapsedSeconds;
                }
            }

            var entries = new List<ShowcaseLayout.Entry>();
            foreach (var m in best.Grid.Modules)
            {
                if (m == best.Core)
                    continue;
                entries.Add(new ShowcaseLayout.Entry { Module = m.Data, Origin = m.Origin, Rotation = m.Rotation });
            }

            var layout = AssetDatabase.LoadAssetAtPath<ShowcaseLayout>(LayoutPath);
            if (layout == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LayoutPath));
                layout = ScriptableObject.CreateInstance<ShowcaseLayout>();
                AssetDatabase.CreateAsset(layout, LayoutPath);
            }
            layout.SetEntries(entries, $"봇 seed {bestSeed}, 이지, {bestTime / 60f:0.0}분, 모듈 {best.Grid.ModuleCount}");
            EditorUtility.SetDirty(layout);
            AssetDatabase.SaveAssets();
            string summary = log + $"→ 선택: {layout.Source}";
            Debug.Log("[ShowcaseBaker]\n" + summary);
            return summary;
        }
    }
}
