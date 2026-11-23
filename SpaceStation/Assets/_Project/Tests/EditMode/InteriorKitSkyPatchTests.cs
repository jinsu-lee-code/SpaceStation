using NUnit.Framework;
using SpaceStation.Data;
using UnityEditor;

namespace SpaceStation.Tests
{
    /// <summary>
    /// 닫힌 문 자리 패널 (사용자 2026-11-03 치안 본부 광장 "공간이 깨져 있음"): 패널 뒤에 붙은 하늘 조각이 문 깊이 3.79 · 3.95에서 칸 경계를 넘어 옆 방 갑판에 보였음.
    /// 패널 모델은 바깥(+Z)으로 나가지 않고, 하늘 조각은 따로 있어 빌더가 칸 경계에 놓음.
    /// </summary>
    public class InteriorKitSkyPatchTests
    {
        [Test]
        public void ClosedPanelsStayInsideAndSkyPatchIsSeparate()
        {
            var kit = AssetDatabase.LoadAssetAtPath<InteriorKit>("Assets/_Project/Data/Interior/InteriorKit.asset");
            var module = AssetDatabase.LoadAssetAtPath<ModuleData>("Assets/_Project/Data/Modules/Evolved/MD_Evo_SecurityHQ.asset");
            Assert.IsNotNull(kit);
            Assert.IsNotNull(module);
            Assert.Less(kit.WallFor(module).Mesh.bounds.max.z, 0.05f, "난간 패널은 패널 면 안쪽");
            Assert.Less(kit.SealedWallFor(module).Mesh.bounds.max.z, 0.05f, "막힌 창 패널은 패널 면 안쪽");
            var patch = kit.SkyPatchFor(module);
            Assert.IsNotNull(patch, "2단계 키트에 하늘 조각이 따로 있음");
            Assert.Less(patch.Mesh.bounds.size.z, 0.02f, "하늘 조각은 얇은 판 (피벗이 판 가운데)");
        }
    }
}
