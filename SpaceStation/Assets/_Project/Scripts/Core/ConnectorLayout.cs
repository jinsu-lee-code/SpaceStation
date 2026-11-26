using System.Collections.Generic;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Core
{
    /// <summary>모듈 두 개가 맞닿은 칸 한 쌍 사이의 연결 통로 (5-6).</summary>
    public readonly struct ConnectorSpec
    {
        /// <summary>A 쪽 칸 (정렬 키상 작은 쪽).</summary>
        public readonly Vector3Int CellA;
        /// <summary>A → B 방향 (면 법선, 단위 벡터).</summary>
        public readonly Vector3Int Direction;
        /// <summary>A 칸 중심에서 A 모델 표면까지.</summary>
        public readonly float DepthA;
        /// <summary>B 칸 중심에서 B 모델 표면까지.</summary>
        public readonly float DepthB;
        /// <summary>⑪ 다리 데크 높이에서 잰 A 쪽 깊이 (섬 · 받침 가장자리, 3 · 4단계 데크일 때만 씀).</summary>
        public readonly float DeckDepthA;
        public readonly float DeckDepthB;
        /// <summary>⑪ 옆 통로 (데크가 아닐 때) A 쪽 끝 높이 — 칸 가운데 기준, 받침 위에 내려앉은 끝은 음수 (0 = 예전처럼 가운데 높이).</summary>
        public readonly float HeightA;
        public readonly float HeightB;
        /// <summary>⑪ 옆 통로 A 쪽 끝 깊이 (내려앉은 끝은 그 높이 · 경사로로 밀어 넣은 깊이, 아니면 DepthA).</summary>
        public readonly float EndDepthA;
        public readonly float EndDepthB;

        public ConnectorSpec(Vector3Int cellA, Vector3Int direction, float depthA, float depthB)
            : this(cellA, direction, depthA, depthB, depthA, depthB)
        {
        }

        public ConnectorSpec(Vector3Int cellA, Vector3Int direction, float depthA, float depthB, float deckDepthA, float deckDepthB)
            : this(cellA, direction, depthA, depthB, deckDepthA, deckDepthB, 0f, depthA, 0f, depthB)
        {
        }

        public ConnectorSpec(Vector3Int cellA, Vector3Int direction, float depthA, float depthB, float deckDepthA, float deckDepthB,
            float heightA, float endDepthA, float heightB, float endDepthB)
        {
            CellA = cellA;
            Direction = direction;
            DepthA = depthA;
            DepthB = depthB;
            DeckDepthA = deckDepthA;
            DeckDepthB = deckDepthB;
            HeightA = heightA;
            HeightB = heightB;
            EndDepthA = endDepthA;
            EndDepthB = endDepthB;
        }

        /// <summary>옆 통로가 칸 가운데 높이가 아닌 끝을 가짐 (받침 위에 내려앉아 비스듬히 이어짐).</summary>
        public bool IsSloped => HeightA != 0f || HeightB != 0f;

        public Vector3Int CellB => CellA + Direction;
        public bool IsVertical => Direction.y != 0;
        /// <summary>두 표면 사이 길이 (칸 크기 1 기준). 위아래 기둥은 받침까지 내려가 1보다 길 수 있음.</summary>
        public float Length => Mathf.Max(0f, 1f - DepthA - DepthB);
        /// <summary>A 칸 중심 기준 통로 중앙까지의 거리.</summary>
        public float CenterOffset => DepthA + Length * 0.5f;
        /// <summary>다리 데크 길이 (데크 높이의 섬 · 받침 가장자리끼리).</summary>
        public float DeckLength => Mathf.Max(0f, 1f - DeckDepthA - DeckDepthB);
        public float DeckCenterOffset => DeckDepthA + DeckLength * 0.5f;
        public float LengthFor(bool deck) => deck ? DeckLength : Length;
        public float CenterOffsetFor(bool deck) => deck ? DeckCenterOffset : CenterOffset;
        /// <summary>쌍을 구분하는 키 (A·B 순서와 무관).</summary>
        public (Vector3Int, Vector3Int) Key => ConnectorLayout.MakeKey(CellA, CellB);
    }

    /// <summary>
    /// 5-6 연결 통로 배치 계산 (순수 로직). 서로 다른 모듈의 칸이 면으로 맞닿으면 통로 하나.
    /// 양쪽 모델의 표면 깊이(ModuleData.FaceDepths)로 길이를 정해 표면끼리 정확히 잇는다.
    /// 어느 한쪽 면에 연결점이 없으면(빈 칸, 예: 코어 위층) 통로를 만들지 않는다.
    /// </summary>
    public static class ConnectorLayout
    {
        /// <summary>
        /// 태양광 패널이 기우는 회전축 (월드, 수평). 태양광 모듈은 이 축과 나란한 옆면에만 수평 통로를 만든다
        /// (앞뒤 면은 패널 가장자리가 위아래로 움직여 통로 끝이 허공에 뜸). StationConnectors가 태양 방향으로 설정.
        /// </summary>
        public static Vector3Int SolarSideAxis { get; set; } = Vector3Int.right;

        /// <summary>모듈에 닿은 모든 통로.</summary>
        public static void ForModule(StationGrid grid, ModuleInstance module, List<ConnectorSpec> results)
        {
            results.Clear();
            if (module == null)
                return;
            foreach (var cell in module.Cells)
            {
                foreach (var dir in GridDirections.Faces)
                {
                    var neighborCell = cell + dir;
                    if (!grid.TryGetModule(neighborCell, out var other) || other == module)
                        continue;
                    if (TryMake(module.Data, module.Origin, module.Rotation, cell,
                            other.Data, other.Origin, other.Rotation, neighborCell, dir, out var spec))
                        results.Add(spec);
                }
            }
        }

        /// <summary>배치 미리보기: 아직 놓이지 않은 모듈이 이웃과 만들 통로.</summary>
        public static void ForPlacement(StationGrid grid, ModuleData data, Vector3Int origin, int rotation, List<ConnectorSpec> results)
            => ForPlacement(grid, data, origin, rotation, results, null);

        /// <summary>12-3b: ignore = 옮기는 모듈 (옛 자리와는 통로를 잇지 않음).</summary>
        public static void ForPlacement(StationGrid grid, ModuleData data, Vector3Int origin, int rotation, List<ConnectorSpec> results, ModuleInstance ignore)
        {
            results.Clear();
            if (data == null)
                return;
            var cells = StationGrid.ResolveCells(data.CellOffsets, origin, rotation);
            foreach (var cell in cells)
            {
                foreach (var dir in GridDirections.Faces)
                {
                    var neighborCell = cell + dir;
                    if (System.Array.IndexOf(cells, neighborCell) >= 0 || !grid.TryGetModule(neighborCell, out var other) || other == ignore)
                        continue;
                    if (TryMake(data, origin, rotation, cell, other.Data, other.Origin, other.Rotation, neighborCell, dir, out var spec))
                        results.Add(spec);
                }
            }
        }

        /// <summary>월드 칸·방향의 표면 깊이 (모듈 회전을 되돌려 데이터에서 찾음).</summary>
        public static bool TryGetDepth(ModuleData data, Vector3Int origin, int rotation, Vector3Int worldCell, Vector3Int worldDir, out float depth)
        {
            if (data == null)
            {
                depth = ModuleData.DefaultFaceDepth;
                return true;
            }
            var localCell = GridDirections.Rotate(worldCell - origin, -rotation);
            var localDir = GridDirections.Rotate(worldDir, -rotation);
            return data.TryGetFaceDepth(localCell, localDir, out depth);
        }

        /// <summary>⑪ 다리 데크 높이 깊이 (따로 잰 값이 없으면 fallback).</summary>
        public static float DeckDepth(ModuleData data, Vector3Int origin, int rotation, Vector3Int worldCell, Vector3Int worldDir, float fallback)
        {
            if (data == null)
                return fallback;
            var localCell = GridDirections.Rotate(worldCell - origin, -rotation);
            var localDir = GridDirections.Rotate(worldDir, -rotation);
            return data.DeckDepth(localCell, localDir, fallback);
        }

        public static (Vector3Int, Vector3Int) MakeKey(Vector3Int a, Vector3Int b)
        {
            return Less(a, b) ? (a, b) : (b, a);
        }

        private static bool TryMake(ModuleData dataA, Vector3Int originA, int rotA, Vector3Int cellA,
            ModuleData dataB, Vector3Int originB, int rotB, Vector3Int cellB, Vector3Int dir, out ConnectorSpec spec)
        {
            spec = default;
            if (!SolarAllows(dataA, dir) || !SolarAllows(dataB, -dir))
                return false;
            if (!TryGetDepth(dataA, originA, rotA, cellA, dir, out float depthA)
                || !TryGetDepth(dataB, originB, rotB, cellB, -dir, out float depthB))
                return false;
            float deckA = DeckDepth(dataA, originA, rotA, cellA, dir, depthA);
            float deckB = DeckDepth(dataB, originB, rotB, cellB, -dir, depthB);
            var endA = new TubeEnd(depthA);
            var endB = new TubeEnd(depthB);
            if (dir.y == 0)
            {
                endA = Landing(dataA, originA, rotA, cellA, dir, depthA);
                endB = Landing(dataB, originB, rotB, cellB, -dir, depthB);
                ResolveTube(ref endA, ref endB, depthA, depthB);
            }
            // 항상 키상 작은 칸을 A로 (같은 쌍이 양쪽에서 계산돼도 동일)
            spec = Less(cellA, cellB)
                ? new ConnectorSpec(cellA, dir, depthA, depthB, deckA, deckB, endA.Height, endA.Depth, endB.Height, endB.Depth)
                : new ConnectorSpec(cellB, -dir, depthB, depthA, deckB, deckA, endB.Height, endB.Depth, endA.Height, endA.Depth);
            return true;
        }

        // ---------------- ⑪ 옆 통로 끝을 받침 위에 내려앉히기 (2026-11-26) ----------------

        /// <summary>경사가 이보다 가파르면 받침 위 끝을 안쪽으로 밀어 경사로처럼 (사용자 2026-11-26).</summary>
        public const float RampMaxDegrees = 35f;
        /// <summary>밀어도 이보다 가파르면 내려앉히지 않음 (예전처럼 칸 가운데 높이 수평).</summary>
        public const float SlopeLimitDegrees = 50f;

        /// <summary>옆 통로 한쪽 끝: 높이 (칸 가운데 기준) · 깊이 · 받침 위로 밀어 넣을 수 있는 가장 안쪽 깊이.</summary>
        public struct TubeEnd
        {
            public float Height;
            public float Depth;
            public float MinDepth;
            public bool Lands;

            public TubeEnd(float depth)
            {
                Height = 0f;
                Depth = depth;
                MinDepth = depth;
                Lands = false;
            }

            public TubeEnd(float height, float depth, float minDepth)
            {
                Height = height;
                Depth = depth;
                MinDepth = Mathf.Min(minDepth, depth);
                Lands = true;
            }
        }

        private static TubeEnd Landing(ModuleData data, Vector3Int origin, int rotation, Vector3Int worldCell, Vector3Int worldDir, float depth)
        {
            if (data == null)
                return new TubeEnd(depth);
            var localCell = GridDirections.Rotate(worldCell - origin, -rotation);
            var localDir = GridDirections.Rotate(worldDir, -rotation);
            return data.TryGetLanding(localCell, localDir, out float h, out float d, out float min)
                ? new TubeEnd(h, d, min)
                : new TubeEnd(depth);
        }

        /// <summary>
        /// 두 끝을 잇는 비스듬한 옆 통로 (칼라는 벽에 맞춰 세움):
        /// 받침 위에 내려앉는 끝은 그 높이 · 깊이, 다른 끝은 칸 가운데 높이. 기울기가 <see cref="RampMaxDegrees"/>보다 가파르면
        /// 낮은 (내려앉은) 끝을 받침 위로 밀어 넣어 경사로처럼 (MinDepth까지). 그래도 <see cref="SlopeLimitDegrees"/>를 넘으면
        /// 둘 다 예전처럼 칸 가운데 높이 수평 (depthA · depthB).
        /// </summary>
        public static void ResolveTube(ref TubeEnd a, ref TubeEnd b, float depthA, float depthB)
        {
            if (!a.Lands && !b.Lands)
                return;
            float dh = Mathf.Abs(a.Height - b.Height);
            float span = 1f - a.Depth - b.Depth;
            float rampTan = Mathf.Tan(RampMaxDegrees * Mathf.Deg2Rad);
            if (dh > rampTan * span)
            {
                float need = dh / rampTan - Mathf.Max(0f, span);
                if (a.Height < b.Height && a.Lands)
                    a.Depth = Mathf.Max(a.MinDepth, a.Depth - need);
                else if (b.Lands)
                    b.Depth = Mathf.Max(b.MinDepth, b.Depth - need);
                span = 1f - a.Depth - b.Depth;
            }
            if (span <= 0.01f || Mathf.Atan2(dh, span) * Mathf.Rad2Deg > SlopeLimitDegrees)
            {
                a = new TubeEnd(depthA);
                b = new TubeEnd(depthB);
            }
        }

        /// <summary>패널이 기우는 태양광 모듈 (<see cref="ModuleData.TiltingPanels"/>): 수평 통로는 패널 회전축과 나란한 면(옆면)만.</summary>
        private static bool SolarAllows(ModuleData data, Vector3Int worldDir)
        {
            if (data == null || !data.TiltingPanels || worldDir.y != 0)
                return true;
            return worldDir == SolarSideAxis || worldDir == -SolarSideAxis;
        }

        private static bool Less(Vector3Int a, Vector3Int b)
        {
            if (a.x != b.x) return a.x < b.x;
            if (a.y != b.y) return a.y < b.y;
            return a.z < b.z;
        }
    }
}
