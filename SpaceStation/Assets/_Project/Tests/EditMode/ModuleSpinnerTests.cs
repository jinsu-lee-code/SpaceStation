using NUnit.Framework;
using SpaceStation.Building;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>회전 부품: 기본은 위쪽 축, 진화 연구 단지 궤도 고리는 모듈 루트 기준 기울어진 축 (모듈 회전을 따라감).</summary>
    public class ModuleSpinnerTests
    {
        private GameObject _module;

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_module);

        private ModuleSpinner Part(float yaw, Vector3 axis)
        {
            _module = new GameObject("Module");
            _module.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var part = new GameObject("SpinOrbit_Test");
            part.transform.SetParent(_module.transform, false);
            var spinner = part.AddComponent<ModuleSpinner>();
            var so = new SerializedObject(spinner);
            so.FindProperty("_speed").floatValue = 90f;
            so.FindProperty("_axis").vector3Value = axis;
            so.ApplyModifiedPropertiesWithoutUndo();
            return spinner;
        }

        [Test]
        public void NoAxis_RotatesAroundWorldUp()
        {
            var spinner = Part(0f, Vector3.zero);
            var start = spinner.transform.rotation;
            spinner.Step(1f, 0f);
            Assert.Less(Quaternion.Angle(spinner.transform.rotation, Quaternion.AngleAxis(90f, Vector3.up) * start), 0.01f);
        }

        [TestCase(0f)]
        [TestCase(90f)]
        [TestCase(270f)]
        public void TiltedAxis_FollowsModuleRotation(float yaw)
        {
            var axis = new Vector3(0f, 0.94f, 0.34f).normalized;   // 20° 기운 궤도 고리
            var spinner = Part(yaw, axis);
            var start = spinner.transform.rotation;
            spinner.Step(1f, 0f);
            var world = _module.transform.TransformDirection(axis);
            Assert.Less(Quaternion.Angle(spinner.transform.rotation, Quaternion.AngleAxis(90f, world) * start), 0.01f);
        }
    }
}
