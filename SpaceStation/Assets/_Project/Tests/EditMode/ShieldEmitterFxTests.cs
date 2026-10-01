using NUnit.Framework;
using SpaceStation.Building;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>5-5 실드 연출: 프레임 띠 두 개가 서로 다른 방향·속도로 세로축 회전한다 (고스트/초기화 전 상태).</summary>
    public class ShieldEmitterFxTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        [Test]
        public void Rings_RotateAroundVerticalAxis_InOppositeDirections()
        {
            _root = new GameObject("Emitter");
            var a = new GameObject("RingA").transform;
            var b = new GameObject("RingB").transform;
            a.SetParent(_root.transform, false);
            b.SetParent(_root.transform, false);
            var fx = _root.AddComponent<ShieldEmitterFx>();
            var so = new SerializedObject(fx);
            so.FindProperty("_ringA").objectReferenceValue = a;
            so.FindProperty("_ringB").objectReferenceValue = b;
            so.ApplyModifiedPropertiesWithoutUndo();

            fx.Step(0.5f, 0f);

            Assert.AreEqual(60f, a.localEulerAngles.y, 0.01f, "A: 120도/초");
            Assert.AreEqual(360f - 40f, b.localEulerAngles.y, 0.01f, "B: -80도/초");
            Assert.AreEqual(0f, a.localEulerAngles.x, 0.01f, "세로축만 회전");
            Assert.AreEqual(0f, a.localEulerAngles.z, 0.01f);
        }
    }
}
