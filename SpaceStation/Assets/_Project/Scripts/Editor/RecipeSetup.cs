using System.Collections.Generic;
using System.IO;
using SpaceStation.Building;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 12-3 합성 시험 레시피 5종 (사용자 결정 2026-10-12, 관문 C-4) — 그레이박스 진화 모듈 · 레시피 에셋을 만들고 게임 씬에 연결한다.
    /// 수치는 BALANCE.md 32번. 진화 모듈 건설비 = 재료 건설비 합 + 합성 비용, 등급 모듈 수 = 재료 수 (자동 계산).
    /// 모델은 칸마다 상자를 이웃 칸 쪽으로 이어 붙인 한 덩어리 (나중에 고유 모델 · 내부 템플릿으로 교체, C-3).
    /// 다시 실행해도 같은 에셋을 갱신한다.
    /// </summary>
    public static class RecipeSetup
    {
        private const string DataRoot = "Assets/_Project/Data";
        private const string ModuleDir = DataRoot + "/Modules/Evolved";
        private const string RecipeDir = DataRoot + "/Recipes";
        private const string PrefabDir = "Assets/_Project/Prefabs/Modules/Evolved";
        private const string MaterialDir = "Assets/_Project/Art/Materials/Evolved";
        private const string GameScene = "Assets/_Project/Scenes/Main.unity";
        private const int CityGrade = 4; // 도시급 (13-0 7단계) = 1단계 진화

        private sealed class Def
        {
            public string Id;
            public string Name;
            public string RecipeName;
            public ModuleCategory Category;
            public string Color;
            public Vector3Int[] Cells;
            public (string module, int count)[] Ingredients;
            public float SynthesisMetal;
            public float Duration;
            public string Rationale;
            public System.Action<SerializedObject> Stats;
            /// <summary>해금 등급 (0이면 도시급).</summary>
            public int UnlockGrade;
            /// <summary>12-4 담당 연구 카테고리 (이 카테고리 Lv.(4 + 단계)에서 그림자 공개).</summary>
            public ResearchCategory Research = ResearchCategory.Construction;
            /// <summary>연구 없이 해금 (연구소 계통 1단계만, E2-3-2).</summary>
            public bool AutoUnlock;
            /// <summary>진화 단계 (1 = 도시급 … 4 = 지구급).</summary>
            public int Stage = 1;
            /// <summary>남은 요구 식단: 식량 모듈의 식품 종류.</summary>
            public FoodKind Food;
        }

        private static Vector3Int V(int x, int y, int z) => new Vector3Int(x, y, z);

        private static readonly Def[] Defs =
        {
            new Def
            {
                Id = "ResearchComplex", Name = "연구 단지", RecipeName = "연구 단지", Category = ModuleCategory.Industry, Color = "#9E80FF",
                Cells = new[] { V(0, 0, 0), V(1, 0, 0) },
                Ingredients = new[] { ("MD_ResearchLab", 2) }, SynthesisMetal = 60f, Duration = 45f, AutoUnlock = true,
                Rationale = "관측 장비와 계산 설비를 한 건물에 모아 연구를 더 많이 동시에 돌린다 (연구소 계통 1단계, 도시급에서 자동 해금)",
                Stats = so =>
                {
                    // 12-4 (사용자 결정 2026-10-14): 연구소 계통 = 단계마다 레시피 연구 슬롯 1 → 연구 단지 = 레시피 1 + 일반 2
                    so.FindProperty("_researchSlots").intValue = 2;
                    so.FindProperty("_recipeSlots").intValue = 1;
                    Amounts(so.FindProperty("_consumption"), (ResourceType.Power, 3f));
                },
            },
            new Def
            {
                Id = "HabitatTower", Name = "거주 타워", RecipeName = "거주 타워", Category = ModuleCategory.Life, Color = "#7FD8FF",
                Cells = new[] { V(0, 0, 0), V(1, 0, 0), V(0, 0, 1), V(1, 0, 1), V(0, 1, 0), V(1, 1, 0), V(0, 1, 1), V(1, 1, 1) },
                Ingredients = new[] { ("MD_Habitat", 4) }, SynthesisMetal = 120f, Duration = 60f, Research = ResearchCategory.Habitation,
                Rationale = "같은 기능을 쌓아 올려 수용 인원을 늘리고, 공용 설비를 나눠 써서 전력은 덜 든다",
                Stats = so =>
                {
                    so.FindProperty("_housingCapacity").intValue = 30;
                    Amounts(so.FindProperty("_consumption"), (ResourceType.Power, 6f));
                },
            },
            new Def
            {
                Id = "EcoModule", Name = "생태 모듈", RecipeName = "생태 모듈", Category = ModuleCategory.Power, // 생산 탭 (재료 = 농장 · 산소 · 물) Color = "#7CFF9A",
                Cells = new[] { V(0, 0, 0), V(1, 0, 0), V(2, 0, 0), V(0, 0, 1) },
                Ingredients = new[] { ("MD_Farm", 1), ("MD_Oxygen", 1), ("MD_WaterRecycler", 1) }, SynthesisMetal = 90f, Duration = 60f, Research = ResearchCategory.Production,
                Food = FoodKind.Vegetable,
                Rationale = "식물 · 산소 · 물 순환이 한 곳에서 닫혀, 따로 둘 때보다 덜 쓰고 더 만든다",
                Stats = so =>
                {
                    Amounts(so.FindProperty("_production"), (ResourceType.Food, 2.5f), (ResourceType.Oxygen, 3.5f), (ResourceType.Water, 2.5f));
                    Amounts(so.FindProperty("_consumption"), (ResourceType.Power, 10f), (ResourceType.Water, 1.5f));
                },
            },
            new Def
            {
                Id = "PowerBank", Name = "축전 뱅크", RecipeName = "축전 뱅크", Category = ModuleCategory.Power, Color = "#FFD24A",
                Cells = new[] { V(0, 0, 0), V(1, 0, 0), V(2, 0, 0) },
                Ingredients = new[] { ("MD_Battery", 3) }, SynthesisMetal = 55f, Duration = 40f, Research = ResearchCategory.Energy,
                Rationale = "셀을 한 묶음으로 관리해 저장량과 충 · 방전 속도가 함께 오른다",
                Stats = so =>
                {
                    so.FindProperty("_batteryCapacity").floatValue = 2200f;
                    so.FindProperty("_batteryRate").floatValue = 40f;
                },
            },
            new Def
            {
                Id = "WelfareCenter", Name = "복지 센터", RecipeName = "복지 센터", Category = ModuleCategory.Life, Color = "#FF9ED8",
                Cells = new[] { V(0, 0, 0), V(1, 0, 0), V(0, 0, 1) },
                Ingredients = new[] { ("MD_Medical", 1), ("MD_Recreation", 1) }, SynthesisMetal = 110f, Duration = 50f, Research = ResearchCategory.Habitation,
                Rationale = "의료와 여가를 한 곳에서 넓은 반경으로, 주민이 몸과 마음을 함께 돌본다",
                Stats = so =>
                {
                    SetEnum(so.FindProperty("_serviceNeed"), nameof(ResidentNeed.Medical));
                    var extra = so.FindProperty("_extraServiceNeeds");
                    extra.arraySize = 1;
                    SetEnum(extra.GetArrayElementAtIndex(0), nameof(ResidentNeed.Recreation));
                    so.FindProperty("_serviceRadius").intValue = 4;
                    so.FindProperty("_serviceCapacity").intValue = 16;
                    Amounts(so.FindProperty("_consumption"), (ResourceType.Power, 6f), (ResourceType.Water, 0.2f));
                },
            },
            // 13-4 (사용자 결정 2026-10-15, BALANCE 44번): 계통표에서 지금 있는 1단계로 이어지는 것만 — 1단계 문화 센터 + 2단계 5종 (그레이박스)
            new Def
            {
                Id = "CultureCenter", Name = "문화 센터", RecipeName = "문화 센터", Category = ModuleCategory.Life, Color = "#FFC86B",
                Cells = new[] { V(0, 0, 0), V(1, 0, 0), V(0, 0, 1), V(1, 0, 1) }, // 학교 2칸 + 휴게실 2칸
                Ingredients = new[] { ("MD_School", 1), ("MD_Recreation", 1) }, SynthesisMetal = 120f, Duration = 50f, Research = ResearchCategory.Habitation,
                Rationale = "배우는 곳과 쉬는 곳을 붙여, 수업 뒤 바로 여가로 이어지는 하루를 만든다 (교육 · 여가를 넓게)",
                Stats = so =>
                {
                    SetEnum(so.FindProperty("_serviceNeed"), nameof(ResidentNeed.Education));
                    var extra = so.FindProperty("_extraServiceNeeds");
                    extra.arraySize = 1;
                    SetEnum(extra.GetArrayElementAtIndex(0), nameof(ResidentNeed.Recreation));
                    so.FindProperty("_serviceRadius").intValue = 5;
                    so.FindProperty("_serviceCapacity").intValue = 36;
                    Amounts(so.FindProperty("_consumption"), (ResourceType.Power, 5f));
                },
            },
            new Def
            {
                Id = "GridHub", Name = "그리드 허브", RecipeName = "그리드 허브", Category = ModuleCategory.Power, Color = "#FFE07A", Stage = 2, UnlockGrade = 5,
                Cells = new[] { V(0, 0, 0), V(1, 0, 0), V(2, 0, 0), V(0, 0, 1), V(1, 0, 1), V(2, 0, 1) },
                Ingredients = new[] { ("Evolved/MD_Evo_PowerBank", 2) }, SynthesisMetal = 160f, Duration = 70f, Research = ResearchCategory.Energy,
                Rationale = "저장 묶음 둘을 한 배전망으로 이어 정거장 전체에 전력을 나눠 준다 · 저장량과 충 · 방전 속도가 크게",
                Stats = so =>
                {
                    so.FindProperty("_batteryCapacity").floatValue = 6000f;
                    so.FindProperty("_batteryRate").floatValue = 110f;
                },
            },
            new Def
            {
                Id = "Arcology", Name = "아콜로지", RecipeName = "아콜로지", Category = ModuleCategory.Life, Color = "#8FD0FF", Stage = 2, UnlockGrade = 5,
                Cells = new[]
                {
                    V(0, 0, 0), V(1, 0, 0), V(0, 0, 1), V(1, 0, 1), V(0, 1, 0), V(1, 1, 0), V(0, 1, 1), V(1, 1, 1),
                    V(0, 2, 0), V(1, 2, 0), V(0, 2, 1), V(1, 2, 1), V(0, 3, 0), V(1, 3, 0), V(0, 3, 1), V(1, 3, 1),
                },
                Ingredients = new[] { ("Evolved/MD_Evo_HabitatTower", 2) }, SynthesisMetal = 330f, Duration = 80f, Research = ResearchCategory.Habitation,
                Rationale = "타워 둘을 하나로 올려 사는 곳 안에 생활 기반까지 넣는다 · 수용이 크게, 공용 설비로 전력은 덜",
                Stats = so =>
                {
                    so.FindProperty("_housingCapacity").intValue = 80;
                    Amounts(so.FindProperty("_consumption"), (ResourceType.Power, 10f));
                },
            },
            new Def
            {
                Id = "Biodome", Name = "바이오돔", RecipeName = "바이오돔", Category = ModuleCategory.Power, Color = "#5FE08A", Stage = 2, UnlockGrade = 5, // 생산 탭
                Cells = new[] { V(0, 0, 0), V(1, 0, 0), V(2, 0, 0), V(3, 0, 0), V(0, 0, 1), V(1, 0, 1), V(2, 0, 1), V(3, 0, 1) },
                Ingredients = new[] { ("Evolved/MD_Evo_EcoModule", 2) }, SynthesisMetal = 240f, Duration = 80f, Research = ResearchCategory.Production,
                Food = FoodKind.Vegetable,
                Rationale = "작은 순환 둘을 큰 생태계 하나로 · 순환이 커질수록 덜 쓰고 더 만든다",
                Stats = so =>
                {
                    Amounts(so.FindProperty("_production"), (ResourceType.Food, 6f), (ResourceType.Oxygen, 8f), (ResourceType.Water, 6f));
                    Amounts(so.FindProperty("_consumption"), (ResourceType.Power, 18f), (ResourceType.Water, 2.5f));
                },
            },
            new Def
            {
                Id = "CivicHall", Name = "시민 회관", RecipeName = "시민 회관", Category = ModuleCategory.Life, Color = "#FFB4E0", Stage = 2, UnlockGrade = 5,
                Cells = new[] { V(0, 0, 0), V(1, 0, 0), V(2, 0, 0), V(3, 0, 0), V(0, 0, 1), V(1, 0, 1), V(2, 0, 1) }, // 문화 센터 2×2 + 복지 센터 L 3칸
                Ingredients = new[] { ("Evolved/MD_Evo_WelfareCenter", 1), ("Evolved/MD_Evo_CultureCenter", 1) }, SynthesisMetal = 320f, Duration = 70f, Research = ResearchCategory.Habitation,
                Rationale = "복지와 문화를 한 회관으로 합쳐 의료 · 여가 · 교육을 한곳에서 넓게 맡는다 (합류)",
                Stats = so =>
                {
                    SetEnum(so.FindProperty("_serviceNeed"), nameof(ResidentNeed.Medical));
                    var extra = so.FindProperty("_extraServiceNeeds");
                    extra.arraySize = 2;
                    SetEnum(extra.GetArrayElementAtIndex(0), nameof(ResidentNeed.Recreation));
                    SetEnum(extra.GetArrayElementAtIndex(1), nameof(ResidentNeed.Education));
                    so.FindProperty("_serviceRadius").intValue = 5;
                    so.FindProperty("_serviceCapacity").intValue = 30;
                    Amounts(so.FindProperty("_consumption"), (ResourceType.Power, 9f), (ResourceType.Water, 0.3f));
                },
            },
            new Def
            {
                Id = "ResearchInstitute", Name = "연구원", RecipeName = "연구원", Category = ModuleCategory.Industry, Color = "#B79CFF", Stage = 2, UnlockGrade = 5,
                Cells = new[] { V(0, 0, 0), V(1, 0, 0), V(0, 0, 1), V(1, 0, 1) },
                Ingredients = new[] { ("Evolved/MD_Evo_ResearchComplex", 1), ("MD_DataCenter", 1) }, SynthesisMetal = 180f, Duration = 70f,
                Rationale = "연구 단지에 계산 설비를 붙여 데이터와 연구를 한 흐름으로 · 슬롯이 늘고 연구가 빨라진다",
                Stats = so =>
                {
                    // 연구소 계통 2단계 = 레시피 연구 슬롯 2 (단계마다 1, 12-4)
                    so.FindProperty("_researchSlots").intValue = 3;
                    so.FindProperty("_recipeSlots").intValue = 2;
                    so.FindProperty("_researchSpeedBonus").floatValue = 0.15f;
                    Amounts(so.FindProperty("_consumption"), (ResourceType.Power, 10f));
                },
            },
            // 남은 요구 치안 (사용자 결정 2026-10-22): 치안 계통 = 치안 + 행정을 함께 맡고 포탑 기능 유지 (재료 행정 관제소를 합성해도 행정이 비지 않게). 대륙급
            new Def
            {
                Id = "SecurityBureau", Name = "보안청", RecipeName = "보안청", Category = ModuleCategory.Life, Color = "#7FA8FF", UnlockGrade = 6,
                Cells = new[] { V(0, 0, 0), V(1, 0, 0), V(0, 0, 1) }, // 행정 관제소 2칸 + 포탑 1칸
                Ingredients = new[] { ("MD_Administration", 1), ("MD_Turret", 1) }, SynthesisMetal = 160f, Duration = 60f, Research = ResearchCategory.Habitation,
                Rationale = "행정과 무력을 한 청사에 묶어 치안을 맡는다 · 행정 창구도 그대로, 주변으로 오는 운석도 막는다",
                Stats = so =>
                {
                    SetEnum(so.FindProperty("_serviceNeed"), nameof(ResidentNeed.Security));
                    var extra = so.FindProperty("_extraServiceNeeds");
                    extra.arraySize = 1;
                    SetEnum(extra.GetArrayElementAtIndex(0), nameof(ResidentNeed.Administration));
                    so.FindProperty("_serviceRadius").intValue = 5;
                    so.FindProperty("_serviceCapacity").intValue = 45;
                    so.FindProperty("_turretRadius").intValue = 2;
                    so.FindProperty("_turretInterceptChance").floatValue = 0.25f;
                    Amounts(so.FindProperty("_consumption"), (ResourceType.Power, 10f));
                },
            },
            new Def
            {
                Id = "SecurityHQ", Name = "치안 본부", RecipeName = "치안 본부", Category = ModuleCategory.Life, Color = "#A0B8FF", Stage = 2, UnlockGrade = 6,
                Cells = new[] { V(0, 0, 0), V(1, 0, 0), V(0, 0, 1), V(1, 0, 1) },
                Ingredients = new[] { ("Evolved/MD_Evo_SecurityBureau", 1), ("MD_SensorArray", 1) }, SynthesisMetal = 290f, Duration = 70f, Research = ResearchCategory.Habitation,
                Rationale = "보안청에 감시망(센서)을 이어 치안 반경을 넓히고 위험을 미리 알린다 · 행정 · 방어도 더 넓게",
                Stats = so =>
                {
                    SetEnum(so.FindProperty("_serviceNeed"), nameof(ResidentNeed.Security));
                    var extra = so.FindProperty("_extraServiceNeeds");
                    extra.arraySize = 1;
                    SetEnum(extra.GetArrayElementAtIndex(0), nameof(ResidentNeed.Administration));
                    so.FindProperty("_serviceRadius").intValue = 7;
                    so.FindProperty("_serviceCapacity").intValue = 80;
                    so.FindProperty("_turretRadius").intValue = 3;
                    so.FindProperty("_turretInterceptChance").floatValue = 0.3f;
                    so.FindProperty("_warningBonus").floatValue = 20f;
                    Amounts(so.FindProperty("_consumption"), (ResourceType.Power, 14f));
                },
            },
            // 12-3c 지구급 (사용자 결정 2026-10-14: 통신 중계소 상위 모듈 추가, 계통표 47 → 48). 재료 MD_CommRelay는 Grade Rules/Setup Contact가 만듦
            new Def
            {
                Id = "DeepSpaceComm", Name = "심우주 통신기지", RecipeName = "심우주 통신기지", Category = ModuleCategory.Defense, Color = "#8FE3FF",
                Cells = new[] { V(0, 0, 0), V(1, 0, 0), V(2, 0, 0) },
                Ingredients = new[] { ("MD_CommRelay", 3) }, SynthesisMetal = 150f, Duration = 60f, UnlockGrade = 7, Research = ResearchCategory.Defense, Stage = 4,
                Rationale = "안테나를 한 배열로 묶어 깊은 우주의 약한 신호까지 잡는다 · 중계소 셋보다 높은 계급까지 해독",
                Stats = so =>
                {
                    so.FindProperty("_relayPower").intValue = 4;
                    Amounts(so.FindProperty("_consumption"), (ResourceType.Power, 5f));
                },
            },
        };

        [MenuItem("SpaceStation/Recipes/Setup")]
        public static void Setup()
        {
            Directory.CreateDirectory(ModuleDir);
            Directory.CreateDirectory(RecipeDir);
            Directory.CreateDirectory(PrefabDir);
            Directory.CreateDirectory(MaterialDir);
            AssetDatabase.Refresh();

            var recipePaths = new List<string>();
            foreach (var def in Defs)
            {
                var ingredients = new List<(ModuleData module, int count)>();
                float materialCost = 0f;
                int gradeCount = 0;
                foreach (var (name, count) in def.Ingredients)
                {
                    var m = AssetDatabase.LoadAssetAtPath<ModuleData>($"{DataRoot}/Modules/{name}.asset");
                    if (m == null)
                    {
                        Debug.LogError($"[Recipes] 재료 모듈 없음: {name}");
                        return;
                    }
                    ingredients.Add((m, count));
                    materialCost += Metal(m) * count;
                    gradeCount += (m.CountsTowardGrade ? m.GradeModuleCount : 0) * count;
                }

                var material = MakeMaterial(def);
                var prefab = MakePrefab(def, material);
                var module = MakeModule(def, prefab, materialCost + def.SynthesisMetal, Mathf.Max(1, gradeCount));
                recipePaths.Add(AssetDatabase.GetAssetPath(MakeRecipe(def, module, ingredients)));
            }
            AssetDatabase.SaveAssets();
            WireScene(recipePaths);
            Debug.Log($"[Recipes] 레시피 {recipePaths.Count}개 · 진화 모듈 생성, 게임 씬 연결 완료");
        }

        private static float Metal(ModuleData m)
        {
            float metal = 0f;
            foreach (var a in m.BuildCost)
                if (a.Type == ResourceType.Metal)
                    metal += a.Amount;
            return metal;
        }

        // ---------------- 에셋 ----------------

        private static Material MakeMaterial(Def def)
        {
            string path = $"{MaterialDir}/M_Evo_{def.Id}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            ColorUtility.TryParseHtmlString(def.Color, out var color);
            mat.SetColor("_BaseColor", Color.Lerp(new Color(0.78f, 0.8f, 0.84f), color, 0.55f));
            mat.SetFloat("_Metallic", 0.35f);
            mat.SetFloat("_Smoothness", 0.55f);
            // Unity 6: 문자열 EnableKeyword는 저장되지 않는 경우가 있어 LocalKeyword로 켠다 (StationArtBuilder와 같음)
            mat.SetKeyword(new UnityEngine.Rendering.LocalKeyword(mat.shader, "_EMISSION"), true);
            mat.SetColor("_EmissionColor", color * 0.35f);
            // None이면 URP 재질 검사가 _EMISSION을 꺼 버림 (MODELING.md 6-1 — 거주 타워 발광이 저장 때마다 꺼지던 원인)
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>칸마다 상자: 이웃 칸이 있는 쪽은 칸 경계까지 이어 붙여 한 덩어리로 보이게 (바깥 면은 0.46 — 기본 면 깊이와 같음).</summary>
        private static GameObject MakePrefab(Def def, Material material)
        {
            string path = $"{PrefabDir}/PF_Evo_{def.Id}.prefab";
            var root = new GameObject("PF_Evo_" + def.Id);
            try
            {
                root.AddComponent<ModuleView>();
                var visual = new GameObject("Visual");
                visual.transform.SetParent(root.transform, false);
                var model = new GameObject("Model");
                model.transform.SetParent(visual.transform, false);
                var pick = new GameObject(ModulePick.ChildName);
                pick.transform.SetParent(root.transform, false);
                int pickLayer = ModulePick.Layer;
                if (pickLayer >= 0)
                    pick.layer = pickLayer;

                var set = new HashSet<Vector3Int>(def.Cells);
                foreach (var c in def.Cells)
                {
                    var center = (Vector3)c;
                    var box = visual.AddComponent<BoxCollider>(); // 배치 판정용 칸 상자
                    box.center = center;
                    box.size = Vector3.one;
                    var pb = pick.AddComponent<BoxCollider>(); // 선택 판정 (모델 모양 = 칸 상자)
                    pb.center = center;
                    pb.size = Vector3.one * 0.92f;

                    Vector3 min = center - Vector3.one * 0.46f, max = center + Vector3.one * 0.46f;
                    if (set.Contains(c + Vector3Int.left)) min.x = center.x - 0.5f;
                    if (set.Contains(c + Vector3Int.right)) max.x = center.x + 0.5f;
                    if (set.Contains(c + Vector3Int.down)) min.y = center.y - 0.5f;
                    if (set.Contains(c + Vector3Int.up)) max.y = center.y + 0.5f;
                    if (set.Contains(c + new Vector3Int(0, 0, -1))) min.z = center.z - 0.5f;
                    if (set.Contains(c + new Vector3Int(0, 0, 1))) max.z = center.z + 0.5f;
                    var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Object.DestroyImmediate(cube.GetComponent<Collider>());
                    cube.name = $"Cell_{c.x}_{c.y}_{c.z}";
                    cube.transform.SetParent(model.transform, false);
                    cube.transform.localPosition = (min + max) * 0.5f;
                    cube.transform.localScale = max - min;
                    cube.GetComponent<MeshRenderer>().sharedMaterial = material;
                }
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static ModuleData MakeModule(Def def, GameObject prefab, float buildMetal, int gradeCount)
        {
            string path = $"{ModuleDir}/MD_Evo_{def.Id}.asset";
            var data = AssetDatabase.LoadAssetAtPath<ModuleData>(path);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<ModuleData>();
                AssetDatabase.CreateAsset(data, path);
            }
            var so = new SerializedObject(data);
            // 다시 실행할 때 이전 값이 남지 않게 효과 항목은 비우고 채움
            so.FindProperty("_production").arraySize = 0;
            so.FindProperty("_consumption").arraySize = 0;
            so.FindProperty("_housingCapacity").intValue = 0;
            so.FindProperty("_researchSlots").intValue = 0;
            so.FindProperty("_recipeSlots").intValue = 0;
            so.FindProperty("_batteryCapacity").floatValue = 0f;
            so.FindProperty("_batteryRate").floatValue = 0f;
            so.FindProperty("_serviceCapacity").intValue = 0;
            so.FindProperty("_extraServiceNeeds").arraySize = 0;
            so.FindProperty("_researchSpeedBonus").floatValue = 0f;
            SetEnum(so.FindProperty("_serviceNeed"), nameof(ResidentNeed.None));
            so.FindProperty("_serviceRadius").intValue = 0;
            so.FindProperty("_turretRadius").intValue = 0;
            so.FindProperty("_turretInterceptChance").floatValue = 0f;
            so.FindProperty("_warningBonus").floatValue = 0f;
            so.FindProperty("_foodKinds").enumValueFlag = (int)def.Food;

            so.FindProperty("_displayName").stringValue = def.Name;
            SetEnum(so.FindProperty("_category"), def.Category.ToString());
            var cells = so.FindProperty("_cellOffsets");
            cells.arraySize = def.Cells.Length;
            for (int i = 0; i < def.Cells.Length; i++)
                cells.GetArrayElementAtIndex(i).vector3IntValue = def.Cells[i];
            so.FindProperty("_prefab").objectReferenceValue = prefab;
            so.FindProperty("_removable").boolValue = true;
            Amounts(so.FindProperty("_buildCost"), (ResourceType.Metal, buildMetal));
            so.FindProperty("_gradeModuleCount").intValue = gradeCount;
            def.Stats(so);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);
            return data;
        }

        private static RecipeData MakeRecipe(Def def, ModuleData result, List<(ModuleData module, int count)> ingredients)
        {
            string path = $"{RecipeDir}/RC_{def.Id}.asset";
            var recipe = AssetDatabase.LoadAssetAtPath<RecipeData>(path);
            if (recipe == null)
            {
                recipe = ScriptableObject.CreateInstance<RecipeData>();
                AssetDatabase.CreateAsset(recipe, path);
            }
            var so = new SerializedObject(recipe);
            so.FindProperty("_displayName").stringValue = def.RecipeName;
            so.FindProperty("_result").objectReferenceValue = result;
            var list = so.FindProperty("_ingredients");
            list.arraySize = ingredients.Count;
            for (int i = 0; i < ingredients.Count; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("Module").objectReferenceValue = ingredients[i].module;
                e.FindPropertyRelative("Count").intValue = ingredients[i].count;
            }
            Amounts(so.FindProperty("_cost"), (ResourceType.Metal, def.SynthesisMetal));
            so.FindProperty("_duration").floatValue = def.Duration;
            so.FindProperty("_stage").intValue = def.Stage;
            so.FindProperty("_unlockGrade").intValue = def.UnlockGrade > 0 ? def.UnlockGrade : CityGrade;
            so.FindProperty("_unlockedByDefault").boolValue = def.AutoUnlock; // 12-4: 연구소 계통 1단계만 자동, 나머지는 해금 연구
            SetEnum(so.FindProperty("_researchCategory"), def.Research.ToString());
            so.FindProperty("_rationale").stringValue = def.Rationale;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(recipe);
            return recipe;
        }

        // ---------------- 씬 ----------------

        private static void WireScene(List<string> recipePaths)
        {
            var scene = EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
            var recipes = new List<RecipeData>();
            foreach (var p in recipePaths)
                recipes.Add(AssetDatabase.LoadAssetAtPath<RecipeData>(p));

            var host = Object.FindFirstObjectByType<SimulationHost>();
            var hso = new SerializedObject(host);
            var list = hso.FindProperty("_recipes");
            list.arraySize = recipes.Count;
            for (int i = 0; i < recipes.Count; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = recipes[i];
            hso.ApplyModifiedPropertiesWithoutUndo();

            var station = Object.FindFirstObjectByType<StationController>();
            var build = Object.FindFirstObjectByType<BuildController>();
            var synthesis = build.GetComponent<SynthesisController>();
            if (synthesis == null)
                synthesis = build.gameObject.AddComponent<SynthesisController>();
            var bso = new SerializedObject(build);
            var sso = new SerializedObject(synthesis);
            sso.FindProperty("_station").objectReferenceValue = station;
            sso.FindProperty("_build").objectReferenceValue = build;
            sso.FindProperty("_camera").objectReferenceValue = bso.FindProperty("_camera").objectReferenceValue;
            sso.FindProperty("_ghostMaterial").objectReferenceValue = bso.FindProperty("_ghostMaterial").objectReferenceValue;
            sso.FindProperty("_moduleMask").intValue = bso.FindProperty("_moduleMask").intValue;
            sso.ApplyModifiedPropertiesWithoutUndo();

            Link<ModuleSelectionController>("_synthesis", synthesis);
            Link<SpaceStation.UI.StatusBar>("_synthesis", synthesis);
            Link<SpaceStation.UI.PauseMenu>("_synthesis", synthesis);

            BuildRecipePanel(station, synthesis);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void Link<T>(string field, Object value) where T : Object
        {
            var target = Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
            if (target == null)
            {
                Debug.LogWarning($"[Recipes] 씬에 {typeof(T).Name} 없음");
                return;
            }
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>HUD에 레시피 창 자리 (연구 창 바로 뒤 순서, 글꼴 · 그림은 연구 창 것을 그대로).</summary>
        private static void BuildRecipePanel(StationController station, SynthesisController synthesis)
        {
            var research = Object.FindFirstObjectByType<SpaceStation.UI.ResearchPanel>(FindObjectsInactive.Include);
            var hud = GameObject.Find("HUD");
            if (hud == null || research == null)
            {
                Debug.LogWarning("[Recipes] HUD 또는 연구 창이 없어 레시피 창을 만들지 않음");
                return;
            }
            var old = hud.transform.Find("RecipePanel");
            if (old != null)
                Object.DestroyImmediate(old.gameObject);
            var go = new GameObject("RecipePanel", typeof(RectTransform));
            go.transform.SetParent(research.transform.parent, false);
            go.transform.SetSiblingIndex(research.transform.GetSiblingIndex() + 1);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var panel = go.AddComponent<SpaceStation.UI.RecipePanel>();
            var rso = new SerializedObject(research);
            var so = new SerializedObject(panel);
            so.FindProperty("_station").objectReferenceValue = station;
            so.FindProperty("_synthesis").objectReferenceValue = synthesis;
            so.FindProperty("_research").objectReferenceValue = research;
            foreach (var f in new[] { "_font", "_fillSprite", "_frameSprite", "_buttonSprite", "_holoArt" })
                so.FindProperty(f).objectReferenceValue = rso.FindProperty(f).objectReferenceValue;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------- 도우미 ----------------

        private static void SetEnum(SerializedProperty p, string name)
        {
            int i = System.Array.IndexOf(p.enumNames, name);
            if (i >= 0)
                p.enumValueIndex = i;
        }

        private static void Amounts(SerializedProperty list, params (ResourceType type, float amount)[] values)
        {
            list.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                SetEnum(e.FindPropertyRelative("Type"), values[i].type.ToString());
                e.FindPropertyRelative("Amount").floatValue = values[i].amount;
            }
        }
    }
}
