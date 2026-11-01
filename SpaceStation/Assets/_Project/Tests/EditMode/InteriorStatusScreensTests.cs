using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Data;
using SpaceStation.Interior;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>
    /// 공용 상태 화면 값 줄 (사용자 2026-10-30 "다른 단계의 모듈들도 미리 세팅" · "기본 모듈도"): 모듈 정보창 효과를 짧게 — 순서 (생산 → 특성 → 소비 · 전력 수요),
    /// 글꼴에 없는 기호 금지, 게임의 모든 모듈 데이터가 줄을 만든다.
    /// </summary>
    public class InteriorStatusScreensTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void Lines_ShieldFirstThenPowerDemand()
        {
            var data = ScriptableObject.CreateInstance<ModuleData>();
            _created.Add(data);
            var so = new SerializedObject(data);
            so.FindProperty("_shieldRadius").intValue = 3;
            so.FindProperty("_shieldReduction").floatValue = 0.7f;
            var consumption = so.FindProperty("_consumption");
            consumption.arraySize = 1;
            consumption.GetArrayElementAtIndex(0).FindPropertyRelative("Type").enumValueIndex = (int)ResourceType.Power;
            consumption.GetArrayElementAtIndex(0).FindPropertyRelative("Amount").floatValue = 15f;
            so.ApplyModifiedPropertiesWithoutUndo();

            var lines = new List<string>();
            InteriorStatusScreens.AppendLines(lines, data, null);

            Assert.AreEqual(new[] { "실드 반경 3칸 · 운석 70% 빗겨냄", "전력 수요 15" }, lines.ToArray());
        }

        [Test]
        public void Lines_ProductionBeforeConsumption_PowerSeparate()
        {
            var data = ScriptableObject.CreateInstance<ModuleData>();
            _created.Add(data);
            var so = new SerializedObject(data);
            var production = so.FindProperty("_production");
            production.arraySize = 1;
            production.GetArrayElementAtIndex(0).FindPropertyRelative("Type").enumValueIndex = (int)ResourceType.Metal;
            production.GetArrayElementAtIndex(0).FindPropertyRelative("Amount").floatValue = 2f;
            var consumption = so.FindProperty("_consumption");
            consumption.arraySize = 2;
            consumption.GetArrayElementAtIndex(0).FindPropertyRelative("Type").enumValueIndex = (int)ResourceType.Power;
            consumption.GetArrayElementAtIndex(0).FindPropertyRelative("Amount").floatValue = 7f;
            consumption.GetArrayElementAtIndex(1).FindPropertyRelative("Type").enumValueIndex = (int)ResourceType.Water;
            consumption.GetArrayElementAtIndex(1).FindPropertyRelative("Amount").floatValue = 0.3f;
            so.ApplyModifiedPropertiesWithoutUndo();

            var lines = new List<string>();
            InteriorStatusScreens.AppendLines(lines, data, null);

            Assert.AreEqual(3, lines.Count);
            StringAssert.StartsWith("생산  ", lines[0]);
            StringAssert.Contains("+2/초", lines[0]);
            StringAssert.StartsWith("소비  ", lines[1]);
            StringAssert.Contains("0.3/초", lines[1]);
            Assert.AreEqual("전력 수요 7", lines[2]);
        }

        /// <summary>게임의 모든 모듈 (기본 · 진화 단계 전부): 화면 글꼴에 없는 "−"(U+2212) · "×"가 없고, 효과가 있는 모듈은 한 줄 이상.</summary>
        [Test]
        public void Lines_AllModules_NoMissingGlyphs()
        {
            int checkedCount = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:ModuleData", new[] { "Assets/_Project/Data/Modules" }))
            {
                var data = AssetDatabase.LoadAssetAtPath<ModuleData>(AssetDatabase.GUIDToAssetPath(guid));
                var lines = new List<string>();
                InteriorStatusScreens.AppendLines(lines, data, null);
                foreach (var line in lines)
                {
                    Assert.IsFalse(line.Contains("−") || line.Contains("×"), $"{data.name}: '{line}'");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(line), data.name);
                }
                if (data.Production.Count > 0 || data.Consumption.Count > 0)
                    Assert.Greater(lines.Count, 0, data.name);
                checkedCount++;
            }
            Assert.Greater(checkedCount, 0);
        }
    }
}
