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
            so.FindProperty("_tiltingPanels").boolValue = true;
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

        /// <summary>
        /// 통로를 옆면으로 제한하는 표시 (<see cref="ModuleData.TiltingPanels"/>) = 기본 태양광만 (2026-11-20: 태양광으로 발전하는 항성 발전망 · 궤도 미러는
        /// 패널이 안 기우는데도 긴 면에 통로가 막혔음, 태양광 어레이는 사용자 "앞뒤로도 생기게"). 표시가 켜진 모듈은 프리팹에 기우는 패널이 있어야 함.
        /// </summary>
        [Test]
        public void TiltingPanels_OnlyBasicSolar()
        {
            var wrong = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:ModuleData", new[] { "Assets/_Project/Data" }))
            {
                var data = AssetDatabase.LoadAssetAtPath<ModuleData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null)
                    continue;
                bool tilts = data.Prefab != null && data.Prefab.GetComponentInChildren<SpaceStation.Building.SunFacingPanel>(true) != null;
                bool expected = data.name == "MD_Solar";
                if (data.TiltingPanels != expected || (data.TiltingPanels && !tilts))
                    wrong.Add($"{data.name} (패널 기욺 {tilts} · 표시 {data.TiltingPanels})");
            }
            Assert.IsEmpty(wrong, string.Join(", ", wrong));
        }

        [Test]
        public void LinkedNegativeDepth_StrutReachesLowPlatform()
        {
            // ⑪ 받침 모듈: 윗면 연결점은 있고 표면 (받침 윗면)이 칸 가운데보다 0.26 아래
            var platform = Module(new[] { Vector3Int.zero });
            platform.SetFaceDepths(new List<FaceDepth> { FaceDepth.Link(Vector3Int.zero, Vector3Int.up, -0.26f, 0f) });
            var block = Module(new[] { Vector3Int.zero });
            Assert.IsTrue(platform.TryGetFaceDepth(Vector3Int.zero, Vector3Int.up, out float d), "연결점 있음 (깊이가 음수여도)");
            Assert.AreEqual(-0.26f, d, 1e-4f);
            _grid.TryPlace(platform, Vector3Int.zero, 0, out _);
            _grid.TryPlace(block, Vector3Int.up, 0, out var top);

            ConnectorLayout.ForModule(_grid, top, _specs);
            Assert.AreEqual(1, _specs.Count);
            var s = _specs[0];
            Assert.IsTrue(s.IsVertical);
            Assert.AreEqual(1f + 0.26f - ModuleData.DefaultFaceDepth, s.Length, 1e-4f, "기둥이 받침 윗면까지 내려감");
            Assert.AreEqual(-0.26f + s.Length * 0.5f, s.CenterOffset, 1e-4f);
        }

        [Test]
        public void OldNegativeDepth_StillMeansNoConnectionPoint()
        {
            var hollow = Module(new[] { Vector3Int.zero }, (Vector3Int.zero, Vector3Int.up, -1f));
            Assert.IsFalse(hollow.TryGetFaceDepth(Vector3Int.zero, Vector3Int.up, out _));
        }

        [Test]
        public void DeckDepth_SeparateFromTubeDepth()
        {
            // ⑪ 3단계 섬: 칸 가운데 높이는 깊은 곳 건물 (0.05), 데크 높이는 섬 가장자리 (0.42)
            var island = Module(new[] { Vector3Int.zero });
            island.SetFaceDepths(new List<FaceDepth> { FaceDepth.Link(Vector3Int.zero, Vector3Int.right, 0.05f, 0.42f) });
            var block = Module(new[] { Vector3Int.zero });
            _grid.TryPlace(island, Vector3Int.zero, 0, out _);
            _grid.TryPlace(block, Vector3Int.right, 0, out var b);

            ConnectorLayout.ForModule(_grid, b, _specs);
            var s = _specs[0];
            Assert.AreEqual(0.05f, s.DepthA, 1e-4f);
            Assert.AreEqual(0.42f, s.DeckDepthA, 1e-4f);
            Assert.AreEqual(ModuleData.DefaultFaceDepth, s.DeckDepthB, 1e-4f, "데크 깊이를 따로 안 잰 쪽 = 통로 깊이");
            Assert.AreEqual(1f - 0.05f - 0.46f, s.LengthFor(false), 1e-4f);
            Assert.AreEqual(1f - 0.42f - 0.46f, s.LengthFor(true), 1e-4f);
            Assert.AreEqual(0.42f + s.DeckLength * 0.5f, s.CenterOffsetFor(true), 1e-4f);
        }

        [Test]
        public void Landing_SlopedTube_KeepsOtherEndAtCenterHeight()
        {
            // ⑪ 받침 모듈 +X 면: 끝을 받침 위 (−0.18)에 내려앉힘, 깊이 0.20 (받침 위 건물까지) → 이웃 기본 깊이 0.46과 길이 0.34 → 기울기 28° (35° 안)
            var platform = Module(new[] { Vector3Int.zero });
            platform.SetFaceDepths(new List<FaceDepth> { FaceDepth.Link(Vector3Int.zero, Vector3Int.right, 0.42f, 0f, -0.18f, 0.20f, 0.20f) });
            var block = Module(new[] { Vector3Int.zero });
            _grid.TryPlace(platform, Vector3Int.zero, 0, out _);
            _grid.TryPlace(block, Vector3Int.right, 0, out var b);

            ConnectorLayout.ForModule(_grid, b, _specs);
            var s = _specs[0];
            Assert.IsTrue(s.IsSloped);
            Assert.AreEqual(-0.18f, s.HeightA, 1e-4f);
            Assert.AreEqual(0.20f, s.EndDepthA, 1e-4f);
            Assert.AreEqual(0f, s.HeightB, 1e-4f, "이웃 끝은 칸 가운데 높이");
            Assert.AreEqual(ModuleData.DefaultFaceDepth, s.EndDepthB, 1e-4f);
            Assert.AreEqual(0.42f, s.DepthA, 1e-4f, "수평 통로 깊이 (데크 · 예전 값)는 그대로");
        }

        [Test]
        public void Landing_TooSteep_SlidesInwardAsRamp()
        {
            // 받침 가장자리 (0.42)에 내려앉으면 길이 0.12 · 높이 차 0.20 → 59° → 받침 위로 밀어 35°
            var a = new ConnectorLayout.TubeEnd(-0.20f, 0.42f, 0.05f);
            var b = new ConnectorLayout.TubeEnd(0.46f);
            ConnectorLayout.ResolveTube(ref a, ref b, 0.42f, 0.46f);
            float span = 1f - a.Depth - b.Depth;
            Assert.AreEqual(-0.20f, a.Height, 1e-4f);
            Assert.AreEqual(ConnectorLayout.RampMaxDegrees, Mathf.Atan2(0.20f, span) * Mathf.Rad2Deg, 0.1f);
            Assert.Less(a.Depth, 0.42f, "받침 안쪽으로 밀림");
            Assert.AreEqual(0.46f, b.Depth, 1e-4f, "칸 가운데 높이 끝은 그대로");
        }

        [Test]
        public void Landing_BlockedAndStillTooSteep_FallsBackToStraight()
        {
            // 받침 위 물건 때문에 0.40까지만 밀 수 있음 → 길이 0.14 · 높이 차 0.20 → 55° (50° 넘음) → 예전처럼 수평
            var a = new ConnectorLayout.TubeEnd(-0.20f, 0.42f, 0.40f);
            var b = new ConnectorLayout.TubeEnd(0.46f);
            ConnectorLayout.ResolveTube(ref a, ref b, 0.30f, 0.46f);
            Assert.IsFalse(a.Lands);
            Assert.AreEqual(0f, a.Height, 1e-4f);
            Assert.AreEqual(0.30f, a.Depth, 1e-4f, "수평 통로 깊이로 돌아감");
        }

        [Test]
        public void Landing_BothOnPlatforms_StaysLevel()
        {
            var a = new ConnectorLayout.TubeEnd(-0.18f, 0.42f, 0.05f);
            var b = new ConnectorLayout.TubeEnd(-0.18f, 0.41f, 0.05f);
            ConnectorLayout.ResolveTube(ref a, ref b, 0.42f, 0.42f);
            Assert.AreEqual(0.42f, a.Depth, 1e-4f, "높이가 같으면 밀지 않음");
            Assert.AreEqual(0.41f, b.Depth, 1e-4f);
            Assert.AreEqual(a.Height, b.Height, 1e-4f);
        }

        /// <summary>⑪ 모든 모듈 데이터가 새 측정으로 구워졌는지 · 기둥 · 데크 깊이가 범위 안인지.</summary>
        [Test]
        public void AllModules_FaceDepthsRebaked_InRange()
        {
            var wrong = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:ModuleData", new[] { "Assets/_Project/Data/Modules" }))
            {
                var data = AssetDatabase.LoadAssetAtPath<ModuleData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null || data.Prefab == null)
                    continue;
                foreach (var f in data.FaceDepths)
                {
                    bool vertical = f.Direction.y != 0;
                    if (!f.Linked && f.Depth >= 0f)
                        wrong.Add($"{data.name} {f.Cell}{f.Direction} 옛 측정");
                    else if (f.Linked && (f.Depth > 0.5f || f.Depth < (vertical ? -8f : 0.04f)))
                        wrong.Add($"{data.name} {f.Cell}{f.Direction} 깊이 {f.Depth}");
                    else if (f.Deck != 0f && (vertical || f.Deck < 0.04f || f.Deck > 0.48f))
                        wrong.Add($"{data.name} {f.Cell}{f.Direction} 데크 {f.Deck}");
                    else if (f.Lands && (vertical || f.Land < -0.45f || f.LandDepth < 0.04f || f.LandDepth > 0.48f || f.LandMin > f.LandDepth + 1e-4f))
                        wrong.Add($"{data.name} {f.Cell}{f.Direction} 받침 {f.Land}@{f.LandDepth}~{f.LandMin}");
                }
            }
            Assert.IsEmpty(wrong, string.Join(", ", wrong));
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
