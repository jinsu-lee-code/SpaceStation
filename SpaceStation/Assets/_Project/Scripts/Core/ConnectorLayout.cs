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

        public ConnectorSpec(Vector3Int cellA, Vector3Int direction, float depthA, float depthB)
        {
            CellA = cellA;
            Direction = direction;
            DepthA = depthA;
            DepthB = depthB;
        }

        public Vector3Int CellB => CellA + Direction;
        public bool IsVertical => Direction.y != 0;
        /// <summary>두 표면 사이 길이 (칸 크기 1 기준).</summary>
        public float Length => Mathf.Max(0f, 1f - DepthA - DepthB);
        /// <summary>A 칸 중심 기준 통로 중앙까지의 거리.</summary>
        public float CenterOffset => DepthA + Length * 0.5f;
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
                    if (System.Array.IndexOf(cells, neighborCell) >= 0 || !grid.TryGetModule(neighborCell, out var other))
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
            // 항상 키상 작은 칸을 A로 (같은 쌍이 양쪽에서 계산돼도 동일)
            spec = Less(cellA, cellB)
                ? new ConnectorSpec(cellA, dir, depthA, depthB)
                : new ConnectorSpec(cellB, -dir, depthB, depthA);
            return true;
        }

        /// <summary>태양광 모듈: 수평 통로는 패널 회전축과 나란한 면(옆면)만.</summary>
        private static bool SolarAllows(ModuleData data, Vector3Int worldDir)
        {
            if (data == null || !data.SolarPowered || worldDir.y != 0)
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
