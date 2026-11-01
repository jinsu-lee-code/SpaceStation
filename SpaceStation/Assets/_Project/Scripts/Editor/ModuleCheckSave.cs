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
    /// 한 개씩만으로는 전력 · 산소 · 물 · 식량이 모자라므로 모자란 자원을 내는 기본 모듈을 바깥에 더 붙여 시간이 흘러도 버티게 한다 (보조 생산).
    /// 배치 설정 (사용자 2026-10-26, 메뉴 SpaceStation/Module Check 배치, 에디터에 기억):
    /// - 단계별 구획 (기본값): 같은 단계 모듈끼리 모여 구획을 이룸 — 기본 = 코어 둘레, 1단계 = 화면 왼쪽, 2단계 = 오른쪽,
    ///   3단계 = 안쪽, 4단계 = 3단계 너머, 보조 생산 = 기본 구획 앞 (기본 게임 카메라 = 앞 왼쪽 위에서 봄). 구획끼리 면이 닿아 모두 코어에 연결.
    /// - 한데 모음: 예전 방식 (기본 모듈 먼저 코어 가까이, 진화 모듈은 바깥으로 섞여서).
    /// </summary>
    public static class ModuleCheckSave
    {
        public const string Slot = "slot3";
        private const string DataRoot = "Assets/_Project/Data";
        private const int SearchRadius = 18;
        private const int MaxSupport = 60;
        private const string MenuRoot = "SpaceStation/Module Check 배치/";
        private const string ByStageMenu = MenuRoot + "단계별 구획 (기본 · 1 · 2 · 3 · 4단계)";
        private const string CompactMenu = MenuRoot + "한데 모음 (예전 방식)";
        private const string LayoutPref = "SpaceStation.ModuleCheck.Layout";

        public enum Layout
        {
            ByStage = 0,
            Compact = 1,
        }

        /// <summary>지금 배치 설정 (EditorPrefs — 이 PC의 에디터에 기억).</summary>
        public static Layout CurrentLayout
        {
            get => (Layout)EditorPrefs.GetInt(LayoutPref, (int)Layout.ByStage);
            set => EditorPrefs.SetInt(LayoutPref, (int)value);
        }

        [MenuItem("SpaceStation/Module Check (전체 모듈 1개씩 → 수동 저장 3)")]
        public static void WriteToSlot3() => Write(Slot, confirm: true);

        [MenuItem(ByStageMenu)]
        private static void UseByStage() => CurrentLayout = Layout.ByStage;

        [MenuItem(ByStageMenu, true)]
        private static bool UseByStageCheck()
        {
            Menu.SetChecked(ByStageMenu, CurrentLayout == Layout.ByStage);
            return true;
        }

        [MenuItem(CompactMenu)]
        private static void UseCompact() => CurrentLayout = Layout.Compact;

        [MenuItem(CompactMenu, true)]
        private static bool UseCompactCheck()
        {
            Menu.SetChecked(CompactMenu, CurrentLayout == Layout.Compact);
            return true;
        }

        /// <summary>세이브를 만들어 슬롯에 씀 (지금 배치 설정으로). 성공하면 요약, 실패 · 취소면 null.</summary>
        public static string Write(string slot, bool confirm) => Write(slot, confirm, CurrentLayout);

        /// <summary>세이브를 만들어 슬롯에 씀. 성공하면 요약, 실패 · 취소면 null.</summary>
        public static string Write(string slot, bool confirm, Layout layout)
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
            var evolved = new HashSet<ModuleData>();
            foreach (var guid in AssetDatabase.FindAssets("t:ModuleData", new[] { $"{DataRoot}/Modules" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var m = AssetDatabase.LoadAssetAtPath<ModuleData>(path);
                if (m == null || m == core || m.CellOffsets == null || m.CellOffsets.Count == 0)
                    continue;
                modules.Add(m);
                if (path.Contains("/Evolved/"))
                    evolved.Add(m);
            }
            var stages = StagesOf(modules, evolved);
            // 기본 모듈 먼저 (코어 가까이, 큰 것부터) → 진화 모듈은 바깥 (큰 덩어리가 기본 모듈을 가리지 않게) · 같은 크기는 이름순 (매번 같은 배치)
            // 단계별 구획이면 단계 순 (구획마다 큰 것부터 구획 가운데에)
            modules.Sort((a, b) =>
            {
                int sa = layout == Layout.ByStage ? stages[a] : evolved.Contains(a) ? 1 : 0;
                int sb = layout == Layout.ByStage ? stages[b] : evolved.Contains(b) ? 1 : 0;
                if (sa != sb)
                    return sa.CompareTo(sb);
                return a.CellOffsets.Count != b.CellOffsets.Count ? b.CellOffsets.Count.CompareTo(a.CellOffsets.Count) : string.CompareOrdinal(a.name, b.name);
            });

            // 모듈마다 놓을 자리 후보 = 그 구획 중심에서 가까운 순 (한데 모음은 모두 코어 중심)
            var anchors = layout == Layout.ByStage ? DistrictAnchors(modules, stages) : new Dictionary<int, Vector2> { { 0, new Vector2(0.5f, 0.5f) } };
            int radius = SearchRadius;
            foreach (var a in anchors.Values)
                radius = Mathf.Max(radius, Mathf.CeilToInt(a.magnitude) + SearchRadius / 2);
            var originsByDistrict = new Dictionary<int, List<Vector3Int>>();
            foreach (var kv in anchors)
                originsByDistrict[kv.Key] = OriginsNear(kv.Value, radius);

            var failed = new List<string>();
            foreach (var data in modules)
            {
                int district = layout == Layout.ByStage ? stages[data] : 0;
                if (!PlaceTouching(sim.Grid, data, originsByDistrict[district]))
                    failed.Add(data.name);
            }
            var supportOrigins = originsByDistrict.TryGetValue(SupportDistrict, out var so) ? so : originsByDistrict[0];

            // 자원 한도 · 수용 인구까지 (틱으로 저장 한도 · 수용 인구 · 명단을 갱신)
            sim.Tick(1f);
            sim.Resources.SetPopulation(sim.Resources.HousingCapacity);
            Refill(sim);
            sim.Tick(1f);

            // 보조 생산: 한 개씩만으로는 전력 · 산소 · 물 · 식량이 모자라 시간을 흐르게 두면 금방 무너짐
            // → 모자란 자원(전력 먼저)을 가장 많이 내는 기본 모듈을 바깥에 더 붙인다 (확인 중 시간이 흘러도 버티게)
            var support = new Dictionary<string, int>();
            var order = new[] { ResourceType.Power, ResourceType.Oxygen, ResourceType.Water, ResourceType.Food };
            for (int step = 0; step < MaxSupport; step++)
            {
                Refill(sim);
                sim.Tick(1f);
                ResourceType? lacking = null;
                foreach (var t in order)
                {
                    // 전력은 밤에 태양광이 멈춰도 남게 (태양광 몫만큼 여유)
                    float need = t == ResourceType.Power ? SolarPower(sim.Grid) : 0f;
                    if (sim.Resources.GetNetRate(t) < need - 0.01f)
                    {
                        lacking = t;
                        break;
                    }
                }
                if (lacking == null)
                    break;
                var producer = BestProducer(modules, evolved, lacking.Value);
                if (producer == null || !PlaceTouching(sim.Grid, producer, supportOrigins))
                    break;
                support.TryGetValue(producer.DisplayName, out int n);
                support[producer.DisplayName] = n + 1;
            }
            Refill(sim);
            sim.Population.SetSatisfaction(100f);

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
            var supportText = new List<string>();
            foreach (var kv in support)
                supportText.Add($"{kv.Key} {kv.Value}");
            string summary = $"{SaveService.TitleOf(slot)} ← 모듈 확인 정거장 ({(layout == Layout.ByStage ? "단계별 구획" : "한데 모음")} · 모듈 {sim.Grid.ModuleCount}개 = 코어 + {modules.Count - failed.Count}/{modules.Count}"
                             + (support.Count > 0 ? $" + 보조 생산 {string.Join(" · ", supportText)}" : "") + $", 인구 {sim.Resources.Population})"
                             + (failed.Count > 0 ? $" · 자리 못 찾음: {string.Join(", ", failed)}" : "");
            Debug.Log("[ModuleCheck] " + summary);
            return summary;
        }

        /// <summary>보조 생산(기본 모듈을 더 붙임) 구획 번호 — 단계별 구획에서는 기본 구획 앞.</summary>
        private const int SupportDistrict = -1;

        /// <summary>모듈 단계: 기본 모듈 0, 진화 모듈 = 결과로 만드는 레시피의 단계 (레시피가 없으면 맨 위 단계).</summary>
        private static Dictionary<ModuleData, int> StagesOf(List<ModuleData> modules, HashSet<ModuleData> evolved)
        {
            var recipeStage = new Dictionary<ModuleData, int>();
            int top = 1;
            foreach (var guid in AssetDatabase.FindAssets("t:RecipeData", new[] { $"{DataRoot}/Recipes" }))
            {
                var r = AssetDatabase.LoadAssetAtPath<RecipeData>(AssetDatabase.GUIDToAssetPath(guid));
                if (r == null || r.Result == null)
                    continue;
                recipeStage[r.Result] = r.Stage;
                top = Mathf.Max(top, r.Stage);
            }
            var stages = new Dictionary<ModuleData, int>();
            foreach (var m in modules)
                stages[m] = !evolved.Contains(m) ? 0 : recipeStage.TryGetValue(m, out int s) ? s : top;
            return stages;
        }

        /// <summary>
        /// 구획 중심 (칸 좌표 x, z). 기본 게임 카메라가 앞 왼쪽 위(−x, −z)에서 보므로 화면 왼쪽 = (−x, +z), 오른쪽 = (+x, −z), 안쪽 = (+x, +z), 앞 = (−x, −z).
        /// 기본 = 코어 둘레, 1단계 = 왼쪽, 2단계 = 오른쪽, 3단계 = 안쪽, 4단계 = 3단계 너머, 보조 생산 = 기본 구획 앞.
        /// 구획 반지름은 바닥 면적(층을 뺀 칸)으로 어림 — 중심을 기본 구획 반지름 + 그 구획 반지름만큼 떨어뜨려 두 덩어리가 맞닿게.
        /// </summary>
        private static Dictionary<int, Vector2> DistrictAnchors(List<ModuleData> modules, Dictionary<ModuleData, int> stages)
        {
            var area = new Dictionary<int, int>();
            foreach (var m in modules)
            {
                area.TryGetValue(stages[m], out int a);
                area[stages[m]] = a + Footprint(m);
            }
            float Radius(int stage) => area.TryGetValue(stage, out int a) ? Mathf.Sqrt(a / Mathf.PI) * 1.25f : 0f;
            var center = new Vector2(0.5f, 0.5f);
            float r0 = Radius(0) + 1f; // 코어 칸 포함
            var left = new Vector2(-1f, 1f).normalized;
            var right = new Vector2(1f, -1f).normalized;
            var far = new Vector2(1f, 1f).normalized;
            var near = new Vector2(-1f, -1f).normalized;
            var anchors = new Dictionary<int, Vector2>
            {
                { 0, center },
                { SupportDistrict, center + near * (r0 + 2f) },
            };
            foreach (int stage in area.Keys)
            {
                if (stage == 0)
                    continue;
                if (stage == 1)
                    anchors[stage] = center + left * (r0 + Radius(1) + 1f);
                else if (stage == 2)
                    anchors[stage] = center + right * (r0 + Radius(2) + 1f);
                else
                {
                    // 3단계부터 안쪽으로 차례로 (3단계 너머에 4단계)
                    float d = r0 + 1f;
                    for (int s = 3; s < stage; s++)
                        d += 2f * Radius(s) + 1f;
                    anchors[stage] = center + far * (d + Radius(stage));
                }
            }
            return anchors;
        }

        /// <summary>바닥에서 차지하는 칸 수 (층은 한 번만 셈).</summary>
        private static int Footprint(ModuleData m)
        {
            var cells = new HashSet<Vector2Int>();
            foreach (var c in m.CellOffsets)
                cells.Add(new Vector2Int(c.x, c.z));
            return cells.Count;
        }

        /// <summary>y = 0 자리 후보를 anchor에서 가까운 순으로 (같은 거리는 x, z 순 — 매번 같은 배치).</summary>
        private static List<Vector3Int> OriginsNear(Vector2 anchor, int radius)
        {
            var origins = new List<Vector3Int>();
            for (int x = -radius; x <= radius; x++)
                for (int z = -radius; z <= radius; z++)
                    origins.Add(new Vector3Int(x, 0, z));
            origins.Sort((a, b) =>
            {
                float da = (a.x - anchor.x) * (a.x - anchor.x) + (a.z - anchor.y) * (a.z - anchor.y);
                float db = (b.x - anchor.x) * (b.x - anchor.x) + (b.z - anchor.y) * (b.z - anchor.y);
                return da != db ? da.CompareTo(db) : a.x != b.x ? a.x.CompareTo(b.x) : a.z.CompareTo(b.z);
            });
            return origins;
        }

        private static float SolarPower(StationGrid grid)
        {
            float sum = 0f;
            foreach (var m in grid.Modules)
            {
                if (m.Data == null || !m.Data.SolarPowered)
                    continue;
                foreach (var a in m.Data.Production)
                    if (a.Type == ResourceType.Power)
                        sum += a.Amount;
            }
            return sum;
        }

        private static void Refill(StationSimulation sim)
        {
            foreach (ResourceType t in Enum.GetValues(typeof(ResourceType)))
                sim.Resources.SetStock(t, sim.Resources.GetCapacity(t));
        }

        /// <summary>그 자원을 칸당 가장 많이 (생산 − 소비) 내는 기본 모듈 (진화 모듈 · 밤에 멈추는 태양광 제외). 없으면 null.</summary>
        private static ModuleData BestProducer(List<ModuleData> modules, HashSet<ModuleData> evolved, ResourceType type)
        {
            ModuleData best = null;
            float bestRate = 0f;
            foreach (var m in modules)
            {
                if (evolved.Contains(m) || m.SolarPowered)
                    continue;
                float net = 0f;
                foreach (var a in m.Production)
                    if (a.Type == type)
                        net += a.Amount;
                foreach (var a in m.Consumption)
                    if (a.Type == type)
                        net -= a.Amount;
                float rate = net / m.CellOffsets.Count;
                if (rate > bestRate)
                {
                    best = m;
                    bestRate = rate;
                }
            }
            return best;
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
