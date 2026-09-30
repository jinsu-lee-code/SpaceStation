using NUnit.Framework;
using SpaceStation.Building;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>5-3 피드백: 태양광 패널이 모듈 회전과 무관하게 태양 쪽으로 기울어야 한다.</summary>
    public class SunFacingPanelTests
    {
        private GameObject _light, _module;

        [SetUp]
        public void SetUp()
        {
            _light = new GameObject("TestSun");
            var l = _light.AddComponent<Light>();
            l.type = LightType.Directional;
            _light.transform.rotation = Quaternion.Euler(32f, 180f, 0f); // 씬 태양과 같은 방향 (+Z 위에서 비춤)
            RenderSettings.sun = l;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_module);
            Object.DestroyImmediate(_light);
        }

        [TestCase(0f)]
        [TestCase(90f)]
        [TestCase(180f)]
        [TestCase(270f)]
        public void Panel_TiltsTowardSun_RegardlessOfModuleRotation(float yaw)
        {
            _module = new GameObject("Solar");
            _module.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var mount = new GameObject("PanelMount");
            mount.transform.SetParent(_module.transform, false);
            var panel = mount.AddComponent<SunFacingPanel>();

            panel.Face(); // 첫 적용이 건너뛰어지면 안 됨 (회귀: 영벡터 비교로 한 번도 회전하지 않던 버그)

            Vector3 up = mount.transform.up;
            float tilt = Vector3.Angle(Vector3.up, up);
            Assert.Greater(tilt, 30f, "기울어져야 함");
            Assert.LessOrEqual(tilt, 38.5f, "최대 38°");
            Assert.Greater(up.z, 0.3f, "태양(+Z) 쪽으로 기울어야 함");
            Assert.AreEqual(0f, up.x, 1e-3f);
        }
    }
}
