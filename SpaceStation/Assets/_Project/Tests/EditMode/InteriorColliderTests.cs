using NUnit.Framework;
using UnityEditor;

namespace SpaceStation.Tests
{
    /// <summary>
    /// ⑤-5 충돌 메시 (2026-11-16): 내부 방 겉껍질은 그리는 메시를 그대로 MeshCollider로 씀 (줄이면 바닥 · 벽이 어긋남 — 01 머리말),
    /// 대신 빌드에서 미리 구움 — 꺼져 있으면 처음 들어가는 방마다 굽느라 멈칫 (궤도 도시 겉껍질 28.8만 삼각형 117 ms).
    /// </summary>
    public class InteriorColliderTests
    {
        [Test]
        public void CollisionMeshes_ArePrebakedInBuilds()
        {
            Assert.IsTrue(PlayerSettings.bakeCollisionMeshes, "Player Settings > Prebake Collision Meshes");
        }
    }
}
