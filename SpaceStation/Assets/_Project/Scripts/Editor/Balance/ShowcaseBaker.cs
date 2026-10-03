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

        // ---------------- Phase 8 이후: ISS형 전시 정거장 ----------------

        [MenuItem("SpaceStation/Menu/Generate Showcase Layout (ISS)")]
        public static void GenerateIssMenu() => GenerateIss(3);

        private enum Role { Ring, Spine, Industry, Dock, Solar, TopDefense, Turret, Armor, Under }

        /// <summary>
        /// ISS형 구성 (배치 순서 = 표 순서): 회전 링을 먼저 축 위에 놓고, 생활 모듈로 X축 중앙 축을 양쪽으로 늘린 뒤,
        /// +Z 쪽 산업동 가지, 양 끝 태양광 날개, 축 위 실드·포탑, 아래 통제실, 바깥 끝 장갑 격벽 순.
        /// </summary>
        private static readonly (string file, int count, Role role)[] IssMix =
        {
            ("MD_RotatingRing", 1, Role.Ring),
            ("MD_Habitat", 10, Role.Spine), ("MD_Medical", 3, Role.Spine), ("MD_Recreation", 3, Role.Spine),
            ("MD_ResearchLab", 2, Role.Spine), ("MD_Oxygen", 4, Role.Spine), ("MD_WaterRecycler", 4, Role.Spine),
            ("MD_Farm", 4, Role.Spine), ("MD_Storage", 3, Role.Spine), ("MD_Battery", 4, Role.Spine),
            ("MD_MaintenanceBay", 2, Role.Spine), ("MD_FuelCell", 2, Role.Spine),
            ("MD_FusionReactor", 2, Role.Industry), ("MD_Refinery", 1, Role.Industry),
            ("MD_MiningDock", 4, Role.Dock), ("MD_CargoTerminal", 1, Role.Dock),
            ("MD_Solar", 24, Role.Solar),
            ("MD_Shield", 2, Role.TopDefense), ("MD_Turret", 4, Role.Turret),
            ("MD_DamageControl", 2, Role.Under), ("MD_ArmorBulkhead", 4, Role.Armor),
        };

        private const int SpineHalfLength = 9;   // 축은 x -9..10 (코어 x 0..1)
        private const int RingCenterX = -5;      // 링은 왼쪽 축 위
        private const int BranchX = 5;           // 산업동 가지 시작 (오른쪽)

        public static string GenerateIss(int seed)
        {
            var core = AssetDatabase.LoadAssetAtPath<ModuleData>($"{DataRoot}/Modules/MD_Core.asset");
            var grid = new SpaceStation.Core.StationGrid();
            grid.TryPlace(core, Vector3Int.zero, 0, out _);
            var rng = new Random(seed);
            var entries = new List<ShowcaseLayout.Entry>();
            var candidates = new HashSet<Vector3Int>();
            int failed = 0;
            foreach (var (file, n, role) in IssMix)
            {
                var data = AssetDatabase.LoadAssetAtPath<ModuleData>($"{DataRoot}/Modules/{file}.asset");
                if (data == null)
                {
                    failed += n;
                    continue;
                }
                for (int k = 0; k < n; k++)
                {
                    candidates.Clear();
                    foreach (var m in grid.Modules)
                        foreach (var c in m.Cells)
                            foreach (var d in SpaceStation.Core.GridDirections.Faces)
                            {
                                var nb = c + d;
                                if (!grid.IsOccupied(nb) && nb.y >= -1 && nb.y <= 2)
                                    candidates.Add(nb);
                            }
                    float bestScore = float.MinValue;
                    Vector3Int bestOrigin = default;
                    int bestRotation = 0;
                    foreach (var cell in candidates)
                        for (int rot = 0; rot < 4; rot++)
                            foreach (var offset in data.CellOffsets)
                            {
                                var origin = cell - SpaceStation.Core.GridDirections.Rotate(offset, rot);
                                if (SpaceStation.Core.PlacementRules.Evaluate(grid, data, origin, rot) != SpaceStation.Core.PlacementResult.Valid)
                                    continue;
                                var cells = SpaceStation.Core.StationGrid.ResolveCells(data.CellOffsets, origin, rot);
                                float score = IssScore(cells, role, k) + (float)rng.NextDouble() * 0.3f;
                                if (score > bestScore)
                                {
                                    bestScore = score;
                                    bestOrigin = origin;
                                    bestRotation = rot;
                                }
                            }
                    if (bestScore == float.MinValue || !grid.TryPlace(data, bestOrigin, bestRotation, out _))
                    {
                        failed++;
                        continue;
                    }
                    entries.Add(new ShowcaseLayout.Entry { Module = data, Origin = bestOrigin, Rotation = bestRotation });
                }
            }

            var layout = AssetDatabase.LoadAssetAtPath<ShowcaseLayout>(LayoutPath);
            if (layout == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LayoutPath));
                layout = ScriptableObject.CreateInstance<ShowcaseLayout>();
                AssetDatabase.CreateAsset(layout, LayoutPath);
            }
            layout.SetEntries(entries, $"ISS형 전시 생성기 seed {seed}, 모듈 {entries.Count} (+코어)");
            EditorUtility.SetDirty(layout);
            AssetDatabase.SaveAssets();
            string summary = $"[ShowcaseBaker] {layout.Source}, 자리 없음 {failed}";
            Debug.Log(summary);
            return summary;
        }

        /// <summary>
        /// 역할별 목표 위치 점수 (칸 평균). 축 = z 0..1, y 0..1 띠를 따라 X로 뻗는 형태.
        /// k = 같은 종류 중 몇 번째인지: 축·태양광은 k로 왼쪽/오른쪽(태양광은 위/아래 날개도)을 번갈아 대칭으로 키운다.
        /// </summary>
        private static float IssScore(Vector3Int[] cells, Role role, int k)
        {
            int side = k % 2 == 0 ? -1 : 1;                 // -1 왼쪽(-X) / +1 오른쪽(+X)
            int wing = (k / 2) % 2 == 0 ? 1 : -1;           // 태양광 날개 +Z / -Z
            float score = 0f;
            foreach (var c in cells)
            {
                int cellSide = c.x < 0 ? -1 : c.x > 1 ? 1 : 0;
                float ax = Mathf.Abs(c.x - 0.5f);                       // 코어 중심에서 X 거리
                float offZ = Mathf.Max(0f, Mathf.Max(-c.z, c.z - 1));    // 축 띠(z 0..1)에서 벗어난 정도
                float offY = Mathf.Max(0f, Mathf.Max(-c.y, c.y - 1));    // 축 층(y 0..1)에서 벗어난 정도
                switch (role)
                {
                    case Role.Ring:
                        score -= Mathf.Abs(c.x - RingCenterX) * 2f + Mathf.Abs(c.z - 0.5f) * 1.5f + Mathf.Abs(c.y) * 3f;
                        break;
                    case Role.Spine:
                        score -= offZ * 4f + offY * 4f;
                        score -= ax * 0.25f;                              // 안쪽부터 채움
                        if (ax > SpineHalfLength) score -= (ax - SpineHalfLength) * 5f;
                        if (c.x >= BranchX - 1 && c.x <= BranchX + 2 && c.z > 1) score -= 6f; // 산업동 자리 비워 둠
                        if (cellSide != 0 && cellSide != side) score -= 2f; // 양쪽 번갈아 (대칭)
                        break;
                    case Role.Industry:
                        // +Z 가지: x BranchX..BranchX+1, z 2..6
                        score -= Mathf.Abs(c.x - (BranchX + 0.5f)) * 2f + Mathf.Abs(c.y) * 3f;
                        score -= Mathf.Max(0f, 2 - c.z) * 4f + Mathf.Max(0f, c.z - 6) * 4f;
                        score += c.z * 0.4f;
                        break;
                    case Role.Dock:
                        // 산업동 가지 끝과 옆 (바깥으로 갈수록)
                        score -= Mathf.Abs(c.x - (BranchX + 0.5f)) * 1.2f + Mathf.Abs(c.y) * 3f;
                        score += c.z * 0.8f;
                        break;
                    case Role.Solar:
                        // 양 끝 날개: |x| 11..14, z -4..5 로 넓게
                        score -= Mathf.Max(0f, SpineHalfLength + 2 - ax) * 3f + Mathf.Max(0f, ax - (SpineHalfLength + 5)) * 3f;
                        score -= Mathf.Abs(c.y) * 3f;
                        if (cellSide != side) score -= 20f;                                  // 왼쪽·오른쪽 끝 번갈아
                        score += Mathf.Clamp(wing * (c.z - 0.5f), -1f, 4.5f) * 0.8f;          // +Z·-Z 날개 번갈아
                        break;
                    case Role.TopDefense:
                        score -= Mathf.Abs(c.y - 2) * 4f + offZ * 3f + Mathf.Abs(ax - 3f) * 0.6f;
                        break;
                    case Role.Turret:
                        score -= offZ * 2f + Mathf.Abs(ax - 7f) * 0.5f;
                        score -= Mathf.Min(Mathf.Abs(c.y - 2), Mathf.Abs(c.y + 1)) * 3f; // 축 위나 아래
                        break;
                    case Role.Under:
                        score -= Mathf.Abs(c.y + 1) * 4f + offZ * 3f + Mathf.Abs(ax - 2f) * 0.5f;
                        break;
                    case Role.Armor:
                        // 산업동 가지 바깥 옆면 (운석 미끼로 산업동을 감쌈)
                        score -= Mathf.Abs(Mathf.Abs(c.x - (BranchX + 0.5f)) - 1.5f) * 2f + Mathf.Abs(c.y) * 2f;
                        score -= Mathf.Abs(c.z - 4f) * 0.5f;
                        break;
                }
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
