using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpaceStation.Tests
{
    /// <summary>
    /// 12-3 합성: 규격 판정(빈 칸 · 튀어나옴 · 섞임 · 구성 · 파손 · 등급 · 비용 · 회전), 시작 → 가동 정지 · 철거/재건축 불가,
    /// 완료 → 같은 칸에 결과(내구도 100, 모듈 수 = 재료 수), 파손 재료 = 일시 정지, 재료 파괴 = 취소, 세이브.
    /// </summary>
    public class SynthesisTests
    {
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private ModuleData _core, _a, _b, _other, _result;
        private RecipeData _recipe;

        [SetUp]
        public void SetUp()
        {
            _config = Create<BalanceConfig>();
            var b = new SerializedObject(_config);
            b.FindProperty("_startingPopulation").intValue = 0;
            Fill(b.FindProperty("_startingResources"), R(ResourceType.Oxygen, 150), R(ResourceType.Metal, 200));
            Fill(b.FindProperty("_baseStorageCapacity"), R(ResourceType.Oxygen, 1000), R(ResourceType.Metal, 1000));
            b.FindProperty("_eventGracePeriod").floatValue = 100000f;
            b.FindProperty("_eventIntervalMin").floatValue = 100000f;
            b.FindProperty("_eventIntervalMax").floatValue = 100000f;
            b.FindProperty("_destroyAfterSeconds").floatValue = 1000f; // 파손 재료가 시험 중 파괴되지 않게
            b.ApplyModifiedPropertiesWithoutUndo();

            _core = Module("MD_TestCore", 0f, new[] { Vector3Int.zero }, removable: false);
            _a = Module("MD_TestA", 10f, new[] { Vector3Int.zero }, oxygen: 1f);       // 1칸, 산소 생산
            _b = Module("MD_TestB", 20f, new[] { Vector3Int.zero, Vector3Int.right }); // 2칸 (x 방향)
            _other = Module("MD_TestOther", 5f, new[] { Vector3Int.zero });
            // 결과: 1×3 일자, 건설비 = 10 + 20 + 30(합성), 등급 모듈 수 = 2
            _result = Module("MD_TestEvolved", 60f, new[] { Vector3Int.zero, Vector3Int.right, new Vector3Int(2, 0, 0) }, gradeCount: 2);

            _grades = Grades(0);
            _recipe = Recipe(unlockGrade: 0);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void Evaluate_ReportsEachReason_AndRotationWorks()
        {
            var sim = Sim();
            var syn = sim.Synthesis;
            Assert.IsNotNull(syn);
            var origin = new Vector3Int(1, 0, 0);

            Assert.AreEqual(SynthesisResult.EmptyCell, syn.Evaluate(_recipe, origin, 0));
            Place(sim, _a, origin);
            Assert.AreEqual(SynthesisResult.EmptyCell, syn.Evaluate(_recipe, origin, 0), "B 자리가 빔");

            // B가 규격 밖으로 튀어나옴 (3,0,0)~(4,0,0)
            var stray = Place(sim, _b, new Vector3Int(3, 0, 0));
            Place(sim, _other, new Vector3Int(2, 0, 0));
            Assert.AreEqual(SynthesisResult.Overhang, syn.Evaluate(_recipe, origin, 0));
            Assert.IsTrue(sim.TryRemove(stray));

            // 레시피에 없는 모듈이 섞임
            Place(sim, _other, new Vector3Int(3, 0, 0));
            var statuses = new List<IngredientStatus>();
            Assert.AreEqual(SynthesisResult.Mixed, syn.Evaluate(_recipe, origin, 0, null, statuses));
            Assert.IsTrue(statuses.Exists(s => s.Module == _other && s.Needed == 0 && s.Present == 2));
            Assert.IsTrue(statuses.Exists(s => s.Module == _b && s.Needed == 1 && s.Present == 0));

            // A 3개로 채움: 칸은 맞지만 구성이 다름
            var sim2 = Sim();
            Place(sim2, _a, origin);
            Place(sim2, _a, new Vector3Int(2, 0, 0));
            Place(sim2, _a, new Vector3Int(3, 0, 0));
            Assert.AreEqual(SynthesisResult.Missing, sim2.Synthesis.Evaluate(_recipe, origin, 0));

            // 맞는 구성 (A 위치는 자유 — 끝에 둠)
            var sim3 = Sim();
            Place(sim3, _b, origin);
            Place(sim3, _a, new Vector3Int(3, 0, 0));
            var materials = new List<ModuleInstance>();
            Assert.AreEqual(SynthesisResult.Valid, sim3.Synthesis.Evaluate(_recipe, origin, 0, materials));
            Assert.AreEqual(2, materials.Count);

            // 90도 회전: z 방향 일자 (B도 회전해서 놓음)
            var sim4 = Sim();
            var zOrigin = new Vector3Int(0, 0, 1);
            Place(sim4, _a, zOrigin);
            int bRot = -1;
            for (int r = 0; r < 4 && bRot < 0; r++)
            {
                var c = StationGrid.ResolveCells(_b.CellOffsets, new Vector3Int(0, 0, 2), r);
                if (c[1] == new Vector3Int(0, 0, 3))
                    bRot = r;
            }
            Assert.GreaterOrEqual(bRot, 0);
            Assert.IsTrue(sim4.TryPlace(_b, new Vector3Int(0, 0, 2), bRot, out _), "B를 z 방향으로");
            int rot = -1;
            for (int r = 0; r < 4 && rot < 0; r++)
                if (sim4.Synthesis.Evaluate(_recipe, zOrigin, r) == SynthesisResult.Valid)
                    rot = r;
            Assert.GreaterOrEqual(rot, 0, "어느 한 회전에서 규격이 맞음");
            Assert.AreEqual(SynthesisResult.EmptyCell, sim4.Synthesis.Evaluate(_recipe, zOrigin, 0), "회전 0은 x 방향이라 빈 칸");
        }

        [Test]
        public void Evaluate_DamagedMaterial_LockedGrade_AndCost()
        {
            var sim = Sim();
            var origin = new Vector3Int(1, 0, 0);
            var a = Place(sim, _a, origin);
            Place(sim, _b, new Vector3Int(2, 0, 0));
            Assert.IsTrue(sim.Damage.Damage(a));
            Assert.AreEqual(SynthesisResult.Damaged, sim.Synthesis.Evaluate(_recipe, origin, 0), "A8: 파손 재료 불가");
            sim.Damage.CompleteNow(a);
            Assert.AreEqual(SynthesisResult.Valid, sim.Synthesis.Evaluate(_recipe, origin, 0));

            sim.Resources.SetStock(ResourceType.Metal, 10f);
            Assert.AreEqual(SynthesisResult.InsufficientResources, sim.Synthesis.Evaluate(_recipe, origin, 0));

            // 등급 미달 (도시급 = 1단계 해금처럼 등급 1 필요)
            _grades = Grades(999);
            var locked = Recipe(unlockGrade: 1);
            var sim2 = Sim(locked);
            Place(sim2, _a, origin);
            Place(sim2, _b, new Vector3Int(2, 0, 0));
            Assert.AreEqual(SynthesisResult.Locked, sim2.Synthesis.Evaluate(locked, origin, 0));
            Assert.IsFalse(sim2.Synthesis.IsUnlocked(locked));
        }

        [Test]
        public void Synthesis_HaltsMaterials_ThenReplacesThemWithResult()
        {
            var sim = Sim();
            var origin = new Vector3Int(1, 0, 0);
            var a = Place(sim, _a, origin);
            var b = Place(sim, _b, new Vector3Int(2, 0, 0));
            sim.Tick(1f);
            Assert.Greater(sim.GetModuleEfficiency(a), 0f);
            int gradeModules = StationProgression.CountGradeModules(sim.Grid);
            int moduleRecord = StationProgression.CountModules(sim.Grid);
            float metal = sim.Resources.GetStock(ResourceType.Metal);

            Assert.AreEqual(SynthesisResult.Valid, sim.Synthesis.TryStart(_recipe, origin, 0, out var job));
            Assert.AreEqual(metal - 30f, sim.Resources.GetStock(ResourceType.Metal), 1e-3f, "합성 비용");
            Assert.IsTrue(sim.IsHalted(a) && sim.IsHalted(b));
            Assert.AreEqual(0f, sim.GetModuleEfficiency(a), "가동 정지");
            Assert.IsFalse(sim.CanRemove(a), "합성 중 철거 불가");
            Assert.AreEqual(RebuildResult.NotAllowed, sim.TryRebuild(a, out _), "합성 중 재건축 불가");
            Assert.AreEqual(SynthesisResult.Busy, sim.Synthesis.Evaluate(_recipe, origin, 0), "이미 합성 중");

            float oxygenBefore = sim.Resources.GetStock(ResourceType.Oxygen);
            sim.Tick(1f);
            Assert.AreEqual(oxygenBefore, sim.Resources.GetStock(ResourceType.Oxygen), 1e-4f, "정지한 A는 산소를 만들지 않음");

            for (int i = 0; i < 20 && sim.Synthesis.Jobs.Count > 0; i++)
                sim.Tick(1f);
            Assert.AreEqual(0, sim.Synthesis.Jobs.Count);
            Assert.IsFalse(sim.Grid.Modules.Contains(a) || sim.Grid.Modules.Contains(b), "재료가 사라짐");
            Assert.IsTrue(sim.Grid.TryGetModule(origin, out var evolved));
            Assert.AreSame(_result, evolved.Data);
            CollectionAssert.AreEquivalent(new[] { origin, new Vector3Int(2, 0, 0), new Vector3Int(3, 0, 0) }, evolved.Cells);
            Assert.IsTrue(sim.Durability.TryGetInfo(evolved, out var d));
            Assert.AreEqual(d.Max, d.Current, 1e-4f, "내구도 100%로 새로 시작");
            Assert.AreEqual(gradeModules, StationProgression.CountGradeModules(sim.Grid), "A4: 모듈 수 = 재료 수 (등급 유지)");
            Assert.AreEqual(moduleRecord, StationProgression.CountModules(sim.Grid));
            Assert.AreEqual(1, sim.Session.Syntheses);
            Assert.IsFalse(sim.IsHalted(evolved));

            // 진화 모듈 재건축 = 레시피가 열려 있으면 가능 (건설 메뉴 해금 목록에 없어도)
            Assert.IsTrue(sim.CanRebuildKind(evolved));
        }

        [Test]
        public void Stage2_UsesEvolvedModulesAsIngredients()
        {
            // 13-4: 2단계 = 1단계 진화 모듈 둘 → 3×2
            var big = Module("MD_TestEvolved2", 150f, new[]
            {
                Vector3Int.zero, Vector3Int.right, new Vector3Int(2, 0, 0),
                new Vector3Int(0, 0, 1), new Vector3Int(1, 0, 1), new Vector3Int(2, 0, 1),
            }, gradeCount: 4);
            var stage2 = Create<RecipeData>();
            stage2.name = "RC_TestStage2";
            var so = new SerializedObject(stage2);
            so.FindProperty("_result").objectReferenceValue = big;
            var ing = so.FindProperty("_ingredients");
            ing.arraySize = 1;
            ing.GetArrayElementAtIndex(0).FindPropertyRelative("Module").objectReferenceValue = _result;
            ing.GetArrayElementAtIndex(0).FindPropertyRelative("Count").intValue = 2;
            Fill(so.FindProperty("_cost"), R(ResourceType.Metal, 30f));
            so.FindProperty("_duration").floatValue = 10f;
            so.FindProperty("_stage").intValue = 2;
            so.FindProperty("_unlockGrade").intValue = 0;
            so.FindProperty("_unlockedByDefault").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();

            var sim = new StationSimulation(new StationSimulationSettings
            {
                Balance = _config, Grades = _grades, CoreModule = _core, Events = new List<GameEventData>(), Random01 = () => 0.5f,
                Recipes = new[] { _recipe, stage2 },
            });
            foreach (var z in new[] { 0, 1 })
            {
                var origin = new Vector3Int(1, 0, z);
                Place(sim, _a, origin);
                Place(sim, _b, new Vector3Int(2, 0, z));
                Assert.AreEqual(SynthesisResult.Valid, sim.Synthesis.TryStart(_recipe, origin, 0, out _));
                for (int i = 0; i < 20 && sim.Synthesis.Jobs.Count > 0; i++)
                    sim.Tick(1f);
            }
            int gradeModules = StationProgression.CountGradeModules(sim.Grid);
            Assert.AreEqual(SynthesisResult.Valid, sim.Synthesis.TryStart(stage2, new Vector3Int(1, 0, 0), 0, out _), "진화 모듈 둘이 2단계 재료");
            for (int i = 0; i < 20 && sim.Synthesis.Jobs.Count > 0; i++)
                sim.Tick(1f);
            Assert.IsTrue(sim.Grid.TryGetModule(new Vector3Int(3, 0, 1), out var result));
            Assert.AreSame(big, result.Data);
            Assert.AreEqual(gradeModules, StationProgression.CountGradeModules(sim.Grid), "모듈 수 유지 (재료 4개)");
        }

        [Test]
        public void DamagedMaterial_PausesSynthesis_DestroyedMaterial_Cancels()
        {
            var sim = Sim();
            var origin = new Vector3Int(1, 0, 0);
            var a = Place(sim, _a, origin);
            Place(sim, _b, new Vector3Int(2, 0, 0));
            Assert.AreEqual(SynthesisResult.Valid, sim.Synthesis.TryStart(_recipe, origin, 0, out var job));
            sim.Tick(1f);
            float remaining = job.Remaining;

            Assert.IsTrue(sim.Damage.Damage(a));
            sim.Tick(1f);
            sim.Tick(1f);
            Assert.IsTrue(job.Paused, "파손 재료 → 일시 정지");
            Assert.AreEqual(remaining, job.Remaining, 1e-4f);
            sim.Damage.CompleteNow(a);
            sim.Tick(1f);
            Assert.IsFalse(job.Paused, "수리되면 이어감");
            Assert.Less(job.Remaining, remaining);

            // 재료가 파괴되면 취소 (비용은 돌려받지 못함)
            bool cancelled = false;
            sim.Synthesis.Cancelled += _ => cancelled = true;
            float metal = sim.Resources.GetStock(ResourceType.Metal);
            Assert.IsTrue(sim.DestroyModule(a));
            Assert.IsTrue(cancelled);
            Assert.AreEqual(0, sim.Synthesis.Jobs.Count);
            Assert.AreEqual(metal, sim.Resources.GetStock(ResourceType.Metal), 1e-4f);
        }

        [Test]
        public void SaveRestore_KeepsJobAndEvolvedModule()
        {
            var sim = Sim();
            var origin = new Vector3Int(1, 0, 0);
            Place(sim, _a, origin);
            Place(sim, _b, new Vector3Int(2, 0, 0));
            Assert.AreEqual(SynthesisResult.Valid, sim.Synthesis.TryStart(_recipe, origin, 0, out var job));
            sim.Tick(3f);
            float remaining = job.Remaining;

            var json = JsonUtility.ToJson(StationStateSerializer.Capture(sim));
            var copy = Sim();
            Assert.AreEqual(0, StationStateSerializer.Restore(copy, JsonUtility.FromJson<StationState>(json)));
            Assert.AreEqual(1, copy.Synthesis.Jobs.Count);
            var restored = copy.Synthesis.Jobs[0];
            Assert.AreEqual(remaining, restored.Remaining, 1e-4f);
            Assert.AreEqual(2, restored.Materials.Count);
            Assert.IsTrue(copy.IsHalted(restored.Materials[0]));

            for (int i = 0; i < 20 && copy.Synthesis.Jobs.Count > 0; i++)
                copy.Tick(1f);
            Assert.IsTrue(copy.Grid.TryGetModule(origin, out var evolved) && evolved.Data == _result);
            Assert.AreEqual(1, copy.Session.Syntheses);

            // 진화 모듈 자체도 저장 · 복원 (건설 메뉴 해금 목록에 없는 모듈)
            var json2 = JsonUtility.ToJson(StationStateSerializer.Capture(copy));
            var copy2 = Sim();
            Assert.AreEqual(0, StationStateSerializer.Restore(copy2, JsonUtility.FromJson<StationState>(json2)));
            Assert.IsTrue(copy2.Grid.TryGetModule(new Vector3Int(3, 0, 0), out var again) && again.Data == _result);
            Assert.AreEqual(1, copy2.Session.Syntheses);
        }

        [Test]
        public void RecipeAssets_AreConsistent()
        {
            var grades = AssetDatabase.LoadAssetAtPath<StationGradeConfig>("Assets/_Project/Data/StationGrades.asset");
            var guids = AssetDatabase.FindAssets("t:RecipeData", new[] { "Assets/_Project/Data" });
            Assert.GreaterOrEqual(guids.Length, 5, "12-3 시험 레시피 5종");
            foreach (var guid in guids)
            {
                var r = AssetDatabase.LoadAssetAtPath<RecipeData>(AssetDatabase.GUIDToAssetPath(guid));
                Assert.IsNotNull(r.Result, r.name);
                Assert.IsNotNull(r.Result.Prefab, r.name + " 결과 프리팹");
                Assert.AreEqual(r.Result.CellOffsets.Count, r.IngredientCells, r.name + ": 재료 칸 수 = 규격 칸 수");
                int resultGrade = r.Result.CountsTowardGrade ? r.Result.GradeModuleCount : 0;   // 장갑 외벽처럼 재료가 모두 등급에 안 들면 결과도 안 듦
                Assert.AreEqual(r.IngredientGradeModules, resultGrade, r.name + ": A4 모듈 수 = 재료 수");
                float materials = 0f, synth = 0f, build = 0f;
                foreach (var i in r.Ingredients)
                    foreach (var a in i.Module.BuildCost)
                        materials += a.Type == ResourceType.Metal ? a.Amount * i.Count : 0f;
                foreach (var a in r.Cost)
                    synth += a.Type == ResourceType.Metal ? a.Amount : 0f;
                foreach (var a in r.Result.BuildCost)
                    build += a.Type == ResourceType.Metal ? a.Amount : 0f;
                Assert.AreEqual(materials + synth, build, 1e-3f, r.name + ": 건설비 = 재료 건설비 합 + 합성 비용");
                Assert.GreaterOrEqual(synth, materials * 0.5f, r.name + ": 합성 비용이 재건축 순비용 수준 이상 (A8)");
                foreach (var g in grades.Grades)
                    CollectionAssert.DoesNotContain(g.Unlocks, r.Result, r.name + ": 진화 모듈은 건설 메뉴에 없음");
                Assert.IsFalse(string.IsNullOrEmpty(r.Rationale), r.name + ": 당위성 한 줄 (A2)");
            }
        }

        [Test]
        public void NoRecipes_NoSynthesisSystem()
        {
            var sim = new StationSimulation(new StationSimulationSettings
            {
                Balance = _config, Grades = _grades, CoreModule = _core, Events = new List<GameEventData>(), Random01 = () => 0.5f,
            });
            Assert.IsNull(sim.Synthesis);
            Assert.IsNull(sim.GetSystem(SynthesisSystem.SystemKey));
        }

        // ---------------- 도우미 ----------------

        private StationSimulation Sim(RecipeData recipe = null) => new StationSimulation(new StationSimulationSettings
        {
            Balance = _config,
            Grades = _grades,
            CoreModule = _core,
            Events = new List<GameEventData>(),
            Random01 = () => 0.5f,
            Recipes = new[] { recipe ?? _recipe },
        });

        private ModuleInstance Place(StationSimulation sim, ModuleData data, Vector3Int origin)
        {
            Assert.IsTrue(sim.TryPlace(data, origin, 0, out var m), $"{data.name} @ {origin}");
            return m;
        }

        private StationGradeConfig Grades(int secondGradePopulation)
        {
            var grades = Create<StationGradeConfig>();
            var g = new SerializedObject(grades);
            var list = g.FindProperty("_grades");
            list.arraySize = 2;
            for (int i = 0; i < 2; i++)
            {
                var grade = list.GetArrayElementAtIndex(i);
                grade.FindPropertyRelative("_minPopulation").intValue = i == 0 ? 0 : secondGradePopulation;
                grade.FindPropertyRelative("_minModules").intValue = 0;
                var unlocks = grade.FindPropertyRelative("_unlocks");
                unlocks.arraySize = i == 0 ? 3 : 0;
                if (i == 0)
                {
                    unlocks.GetArrayElementAtIndex(0).objectReferenceValue = _a;
                    unlocks.GetArrayElementAtIndex(1).objectReferenceValue = _b;
                    unlocks.GetArrayElementAtIndex(2).objectReferenceValue = _other;
                }
            }
            g.ApplyModifiedPropertiesWithoutUndo();
            return grades;
        }

        private RecipeData Recipe(int unlockGrade)
        {
            var r = Create<RecipeData>();
            r.name = "RC_Test" + unlockGrade;
            var so = new SerializedObject(r);
            so.FindProperty("_displayName").stringValue = "시험 합성";
            so.FindProperty("_result").objectReferenceValue = _result;
            var ing = so.FindProperty("_ingredients");
            ing.arraySize = 2;
            ing.GetArrayElementAtIndex(0).FindPropertyRelative("Module").objectReferenceValue = _a;
            ing.GetArrayElementAtIndex(0).FindPropertyRelative("Count").intValue = 1;
            ing.GetArrayElementAtIndex(1).FindPropertyRelative("Module").objectReferenceValue = _b;
            ing.GetArrayElementAtIndex(1).FindPropertyRelative("Count").intValue = 1;
            Fill(so.FindProperty("_cost"), R(ResourceType.Metal, 30f));
            so.FindProperty("_duration").floatValue = 10f;
            so.FindProperty("_unlockGrade").intValue = unlockGrade;
            so.FindProperty("_unlockedByDefault").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            return r;
        }

        private ModuleData Module(string name, float metal, Vector3Int[] cells, bool removable = true, float oxygen = 0f, int gradeCount = 1)
        {
            var m = Create<ModuleData>();
            m.name = name;
            var so = new SerializedObject(m);
            so.FindProperty("_removable").boolValue = removable;
            var offsets = so.FindProperty("_cellOffsets");
            offsets.arraySize = cells.Length;
            for (int i = 0; i < cells.Length; i++)
                offsets.GetArrayElementAtIndex(i).vector3IntValue = cells[i];
            Fill(so.FindProperty("_buildCost"), R(ResourceType.Metal, metal));
            if (oxygen > 0f)
                Fill(so.FindProperty("_production"), R(ResourceType.Oxygen, oxygen));
            so.FindProperty("_gradeModuleCount").intValue = gradeCount;
            so.ApplyModifiedPropertiesWithoutUndo();
            return m;
        }

        private static ResourceAmount R(ResourceType t, float a) => new ResourceAmount(t, a);

        private static void Fill(SerializedProperty list, params ResourceAmount[] values)
        {
            list.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("Type").enumValueIndex = (int)values[i].Type;
                e.FindPropertyRelative("Amount").floatValue = values[i].Amount;
            }
        }

        private T Create<T>() where T : ScriptableObject
        {
            var o = ScriptableObject.CreateInstance<T>();
            _created.Add(o);
            return o;
        }
    }
}
