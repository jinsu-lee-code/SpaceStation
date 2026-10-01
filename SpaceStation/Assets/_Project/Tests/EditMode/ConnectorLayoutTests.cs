using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>5-6 연결 통로 배치: 맞닿은 칸 쌍마다 하나, 길이 = 1 - 양쪽 표면 깊이, 연결점 없는 면은 제외.</summary>
    public class ConnectorLayoutTests
    {
        private readonly List<Object> _created = new List<Object>();
        private readonly List<ConnectorSpec> _specs = new List<ConnectorSpec>();
        private StationGrid _grid;

        [SetUp]
        public void SetUp() => _grid = new StationGrid();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void TwoBlocks_DefaultDepth_OneConnectorBetweenSurfaces()
        {
            var block = Module(new[] { Vector3Int.zero });
            _grid.TryPlace(block, Vector3Int.zero, 0, out var a);
            _grid.TryPlace(block, Vector3Int.right, 0, out var b);

            ConnectorLayout.ForModule(_grid, b, _specs);
            Assert.AreEqual(1, _specs.Count);
            var s = _specs[0];
            Assert.AreEqual(Vector3Int.zero, s.CellA, "작은 칸이 A");
            Assert.AreEqual(Vector3Int.right, s.Direction);
            Assert.AreEqual(1f - 2f * ModuleData.DefaultFaceDepth, s.Length, 1e-4f);
            Assert.IsFalse(s.IsVertical);

            ConnectorLayout.ForModule(_grid, a, _specs);
            Assert.AreEqual(1, _specs.Count);
            Assert.AreEqual(s.Key, _specs[0].Key, "양쪽에서 계산해도 같은 쌍");
        }

        [Test]
        public void MeasuredDepth_FollowsModuleRotation()
        {
            // 얇은 모듈: +X 면 표면이 중심에서 0.2 (회전 0 기준)
            var thin = Module(new[] { Vector3Int.zero }, (Vector3Int.zero, Vector3Int.right, 0.2f));
            var block = Module(new[] { Vector3Int.zero });
            _grid.TryPlace(thin, Vector3Int.zero, 1, out _);       // 90도 회전 → 로컬 +X가 월드 -Z
            _grid.TryPlace(block, new Vector3Int(0, 0, -1), 0, out var b);

            ConnectorLayout.ForModule(_grid, b, _specs);
            Assert.AreEqual(1, _specs.Count);
            var s = _specs[0];
            Assert.AreEqual(new Vector3Int(0, 0, -1), s.CellA);
            Assert.AreEqual(ModuleData.DefaultFaceDepth, s.DepthA, 1e-4f);
            Assert.AreEqual(0.2f, s.DepthB, 1e-4f, "회전된 얇은 모듈 쪽 깊이");
            Assert.AreEqual(1f - 0.46f - 0.2f, s.Length, 1e-4f);
        }

        [Test]
        public void FaceWithoutConnectionPoint_HasNoConnector()
        {
            var hollow = Module(new[] { Vector3Int.zero }, (Vector3Int.zero, Vector3Int.right, -1f));
            var block = Module(new[] { Vector3Int.zero });
            _grid.TryPlace(hollow, Vector3Int.zero, 0, out _);
            _grid.TryPlace(block, Vector3Int.right, 0, out var b);
            ConnectorLayout.ForModule(_grid, b, _specs);
            Assert.AreEqual(0, _specs.Count);
        }

        [Test]
        public void Stacked_IsVertical_AndMultiCellGetsOnePerFace()
        {
            var block = Module(new[] { Vector3Int.zero });
            var bar = Module(new[] { Vector3Int.zero, Vector3Int.right });
            _grid.TryPlace(bar, Vector3Int.zero, 0, out _);
            _grid.TryPlace(block, Vector3Int.up, 0, out var top);
            ConnectorLayout.ForModule(_grid, top, _specs);
            Assert.AreEqual(1, _specs.Count);
            Assert.IsTrue(_specs[0].IsVertical);

            ConnectorLayout.ForPlacement(_grid, bar, new Vector3Int(0, 0, 1), 0, _specs);
            Assert.AreEqual(2, _specs.Count, "2칸이 2칸에 나란히 = 통로 2개");
        }

        [Test]
        public void Solar_ConnectsOnlyAlongPanelTiltAxis_RegardlessOfModuleRotation()
        {
            var solar = Module(new[] { Vector3Int.zero });
            var so = new SerializedObject(solar);
            so.FindProperty("_solarPowered").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            var block = Module(new[] { Vector3Int.zero });
            ConnectorLayout.SolarSideAxis = Vector3Int.right;
            _grid.TryPlace(solar, Vector3Int.zero, 1, out _); // 모듈 회전과 무관 (패널은 월드 기준)

            ConnectorLayout.ForPlacement(_grid, block, Vector3Int.right, 0, _specs);
            Assert.AreEqual(1, _specs.Count, "옆면(회전축 방향) = 통로");
            ConnectorLayout.ForPlacement(_grid, block, new Vector3Int(0, 0, 1), 0, _specs);
            Assert.AreEqual(0, _specs.Count, "앞뒤(패널이 기우는 쪽) = 통로 없음");
            ConnectorLayout.ForPlacement(_grid, block, Vector3Int.down, 0, _specs);
            Assert.AreEqual(1, _specs.Count, "아래 = 기둥 조인트");
        }

        private ModuleData Module(Vector3Int[] offsets, params (Vector3Int cell, Vector3Int dir, float depth)[] depths)
        {
            var data = ScriptableObject.CreateInstance<ModuleData>();
            _created.Add(data);
            var so = new SerializedObject(data);
            var list = so.FindProperty("_cellOffsets");
            list.arraySize = offsets.Length;
            for (int i = 0; i < offsets.Length; i++)
                list.GetArrayElementAtIndex(i).vector3IntValue = offsets[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            var faces = new List<FaceDepth>();
            foreach (var d in depths)
                faces.Add(new FaceDepth(d.cell, d.dir, d.depth));
            data.SetFaceDepths(faces);
            return data;
        }
    }
}
