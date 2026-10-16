using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>4-5 건설 메뉴 탭.</summary>
    public class BuildCategoriesTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        private ModuleData Module(string name, ModuleCategory category)
        {
            var m = ScriptableObject.CreateInstance<ModuleData>();
            m.name = name;
            var so = new SerializedObject(m);
            so.FindProperty("_category").enumValueIndex = (int)category;
            so.ApplyModifiedPropertiesWithoutUndo();
            _created.Add(m);
            return m;
        }

        [Test]
        public void Available_SkipsEmptyCategories_InEnumOrder()
        {
            var list = new[] { Module("Habitat", ModuleCategory.Life), Module("Solar", ModuleCategory.Power), Module("Dock", ModuleCategory.Industry) };
            var result = new List<ModuleCategory>();
            BuildCategories.GetAvailable(list, result);
            CollectionAssert.AreEqual(new[] { ModuleCategory.Power, ModuleCategory.Life, ModuleCategory.Industry }, result, "방어는 모듈이 없어 숨김");
        }

        [Test]
        public void Filter_KeepsBuildListOrder()
        {
            var solar = Module("Solar", ModuleCategory.Power);
            var habitat = Module("Habitat", ModuleCategory.Life);
            var battery = Module("Battery", ModuleCategory.Power);
            var result = new List<ModuleData>();
            BuildCategories.Filter(new[] { solar, habitat, battery }, ModuleCategory.Power, result);
            CollectionAssert.AreEqual(new[] { solar, battery }, result, "숫자키 1=태양광, 2=배터리");
        }

        private void Cost(ModuleData m, float metal)
        {
            var so = new SerializedObject(m);
            var list = so.FindProperty("_buildCost");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).FindPropertyRelative("Type").enumValueIndex = (int)ResourceType.Metal;
            list.GetArrayElementAtIndex(0).FindPropertyRelative("Amount").floatValue = metal;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void Filter_VisibleOnly_SortedByGradeThenMetal()
        {
            // 13-6a: 열린 것만 · 열리는 등급 → 금속 비용 → 목록 순서, 해금 조건 없음(-1)은 맨 뒤
            var a = Module("A", ModuleCategory.Power);
            var b = Module("B", ModuleCategory.Power);
            var c = Module("C", ModuleCategory.Power);
            var d = Module("D", ModuleCategory.Power);
            var e = Module("E", ModuleCategory.Power);
            var hidden = Module("Hidden", ModuleCategory.Power);
            Cost(a, 80f); Cost(b, 40f); Cost(c, 10f); Cost(d, 40f); Cost(e, 1f);
            var grade = new Dictionary<ModuleData, int> { { a, 1 }, { b, 1 }, { c, 2 }, { d, 1 }, { e, -1 }, { hidden, 0 } };
            var result = new List<ModuleData>();
            BuildCategories.Filter(new[] { a, b, c, d, e, hidden }, ModuleCategory.Power, result, m => m != hidden, m => grade[m]);
            CollectionAssert.AreEqual(new[] { b, d, a, c, e }, result, "등급 1(40 · 40 · 80) → 등급 2 → 조건 없음, 숨김 제외");

            var tabs = new List<ModuleCategory>();
            BuildCategories.GetAvailable(new[] { hidden, Module("H", ModuleCategory.Life) }, tabs, m => m != hidden);
            CollectionAssert.AreEqual(new[] { ModuleCategory.Life }, tabs, "열린 모듈이 없는 탭은 숨김");
        }

        [Test]
        public void Cycle_WrapsBothDirections()
        {
            var tabs = new[] { ModuleCategory.Power, ModuleCategory.Life, ModuleCategory.Industry };
            Assert.AreEqual(ModuleCategory.Life, BuildCategories.Cycle(tabs, ModuleCategory.Power, 1));
            Assert.AreEqual(ModuleCategory.Power, BuildCategories.Cycle(tabs, ModuleCategory.Industry, 1));
            Assert.AreEqual(ModuleCategory.Industry, BuildCategories.Cycle(tabs, ModuleCategory.Power, -1));
            Assert.AreEqual(ModuleCategory.Power, BuildCategories.Cycle(tabs, ModuleCategory.Defense, 1), "없는 탭이면 첫 탭");
        }
    }
}
