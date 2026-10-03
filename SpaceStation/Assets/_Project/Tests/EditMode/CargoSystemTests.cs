using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>8-5 화물 터미널 (BALANCE 23번).</summary>
    public class CargoSystemTests
    {
        private const float Eps = 1e-3f;
        private readonly List<Object> _created = new List<Object>();
        private ResourceSimulation _resources;
        private StationGrid _grid;
        private ModuleData _terminal;

        [SetUp]
        public void SetUp()
        {
            var config = ScriptableObject.CreateInstance<BalanceConfig>();
            _created.Add(config);
            var so = new SerializedObject(config);
            Fill(so.FindProperty("_startingResources"),
                (ResourceType.Oxygen, 150f), (ResourceType.Water, 40f), (ResourceType.Food, 100f), (ResourceType.Metal, 120f));
            Fill(so.FindProperty("_baseStorageCapacity"),
                (ResourceType.Oxygen, 200f), (ResourceType.Water, 200f), (ResourceType.Food, 200f), (ResourceType.Metal, 200f));
            so.ApplyModifiedPropertiesWithoutUndo();
            _resources = new ResourceSimulation(config);

            _terminal = ScriptableObject.CreateInstance<ModuleData>();
            _created.Add(_terminal);
            var t = new SerializedObject(_terminal);
            t.FindProperty("_cargoInterval").floatValue = 180f;
            t.FindProperty("_cargoFraction").floatValue = 0.15f;
            t.ApplyModifiedPropertiesWithoutUndo();
            _grid = new StationGrid();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void PickNeediest_LowestStockRatio()
        {
            Assert.AreEqual(ResourceType.Water, CargoSystem.PickNeediest(_resources), "물 40/200 = 20%");
        }

        [Test]
        public void Delivers_NeediestResource_EveryInterval_PerTerminal()
        {
            Assert.IsTrue(_terminal.IsCargoTerminal);
            _grid.TryPlace(_terminal, Vector3Int.zero, 0, out var a);
            _grid.TryPlace(_terminal, Vector3Int.right, 0, out var b);
            var cargo = new CargoSystem();
            var got = new List<(ModuleInstance, ResourceType, float)>();
            cargo.Delivered += (m, type, amount) => got.Add((m, type, amount));

            for (int i = 0; i < 179; i++)
                cargo.Tick(_grid, _resources, _ => 1f, 1f);
            Assert.AreEqual(0, got.Count);
            cargo.Tick(_grid, _resources, _ => 1f, 1f);
            Assert.AreEqual(2, got.Count, "터미널마다 따로");
            Assert.AreEqual(ResourceType.Water, got[0].Item2);
            Assert.AreEqual(30f, got[0].Item3, Eps, "한도 200의 15%");
            Assert.AreEqual(100f, _resources.GetStock(ResourceType.Water), Eps, "40 + 30 + 30");
        }

        [Test]
        public void Strength_SlowsTimer_ZeroStops()
        {
            _grid.TryPlace(_terminal, Vector3Int.zero, 0, out var a);
            var cargo = new CargoSystem();
            int count = 0;
            cargo.Delivered += (_, __, ___) => count++;
            for (int i = 0; i < 180; i++)
                cargo.Tick(_grid, _resources, _ => 0.5f, 1f);
            Assert.AreEqual(0, count, "가동률 50% → 360초");
            Assert.AreEqual(0.5f, cargo.GetProgress(a), Eps);
            for (int i = 0; i < 100; i++)
                cargo.Tick(_grid, _resources, _ => 0f, 1f);
            Assert.AreEqual(0.5f, cargo.GetProgress(a), Eps, "파손·비활성이면 멈춤");
        }

        private static void Fill(SerializedProperty list, params (ResourceType type, float amount)[] values)
        {
            list.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("Type").enumValueIndex = (int)values[i].type;
                e.FindPropertyRelative("Amount").floatValue = values[i].amount;
            }
        }
    }
}
