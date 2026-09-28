using NUnit.Framework;
using SpaceStation.Core;
using UnityEngine;

namespace SpaceStation.Tests
{
    public class GridPickingTests
    {
        [Test]
        public void AdjacentCell_FromEachFaceOfOriginCube()
        {
            float h = GridConfig.CellSize * 0.5f;
            foreach (var dir in GridDirections.Faces)
            {
                // 면 위의 한 점 (중심에서 약간 비껴서)
                Vector3 point = (Vector3)dir * h + new Vector3(0.1f, 0.2f, 0.3f) - Vector3.Scale(new Vector3(0.1f, 0.2f, 0.3f), Abs(dir));
                Assert.AreEqual(Vector3Int.zero, GridConfig.GetHitCell(point, dir), $"hit {dir}");
                Assert.AreEqual(dir, GridConfig.GetAdjacentCell(point, dir), $"adjacent {dir}");
            }
        }

        [Test]
        public void AdjacentCell_OnMultiCellModuleFarHalf()
        {
            // 2x1x1 모듈의 두 번째 셀(1,0,0) 윗면
            var point = new Vector3(1.2f, 0.5f, -0.1f);
            Assert.AreEqual(new Vector3Int(1, 1, 0), GridConfig.GetAdjacentCell(point, Vector3.up));
        }

        [Test]
        public void DominantAxis_RoundsNoisyNormal()
        {
            Assert.AreEqual(Vector3Int.up, GridConfig.DominantAxis(new Vector3(0.01f, 0.99f, -0.02f)));
            Assert.AreEqual(new Vector3Int(0, 0, -1), GridConfig.DominantAxis(new Vector3(0.1f, 0f, -0.9f)));
        }

        [Test]
        public void ToQuaternion_MatchesGridRotate()
        {
            var offset = new Vector3Int(2, 1, 1);
            for (int r = 0; r < 4; r++)
            {
                Vector3 viaQuat = GridDirections.ToQuaternion(r) * (Vector3)offset;
                Vector3 viaGrid = GridDirections.Rotate(offset, r);
                Assert.Less(Vector3.Distance(viaQuat, viaGrid), 1e-4f, $"rotation {r}");
            }
        }

        private static Vector3 Abs(Vector3Int v)
        {
            return new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        }
    }
}
