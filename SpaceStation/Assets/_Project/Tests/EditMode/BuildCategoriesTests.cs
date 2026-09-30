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
