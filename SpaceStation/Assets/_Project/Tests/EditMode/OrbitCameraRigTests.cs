using NUnit.Framework;
using SpaceStation.Core;
using UnityEngine;

namespace SpaceStation.Tests
{
    public class OrbitCameraRigTests
    {
        [Test]
        public void Position_IsDistanceFromFocus_AndLooksAtFocus()
        {
            var rig = new OrbitCameraRig(new Vector3(1, 2, 3), 30f, 20f, 10f);
            Assert.AreEqual(10f, Vector3.Distance(rig.Position, rig.Focus), 1e-4f);
            var toFocus = (rig.Focus - rig.Position).normalized;
            Assert.AreEqual(1f, Vector3.Dot(toFocus, rig.Rotation * Vector3.forward), 1e-4f);
        }

        [Test]
        public void Orbit_ClampsPitch()
        {
            var rig = new OrbitCameraRig(Vector3.zero, 0f, 0f, 10f);
            rig.Orbit(0f, 500f);
            Assert.AreEqual(rig.MaxPitch, rig.Pitch);
            rig.Orbit(0f, -1000f);
            Assert.AreEqual(rig.MinPitch, rig.Pitch);
        }

        [Test]
        public void Zoom_ClampsDistance()
        {
            var rig = new OrbitCameraRig(Vector3.zero, 0f, 0f, 10f);
            rig.Zoom(1f);
            Assert.Less(rig.Distance, 10f);
            rig.Zoom(1000f);
            Assert.AreEqual(rig.MinDistance, rig.Distance);
            rig.Zoom(-1000f);
            Assert.AreEqual(rig.MaxDistance, rig.Distance);
        }

        [Test]
        public void PanHorizontal_KeepsHeight()
        {
            var rig = new OrbitCameraRig(Vector3.zero, 90f, 45f, 10f);
            rig.PanHorizontal(0f, 1f);
            Assert.AreEqual(0f, rig.Focus.y, 1e-5f);
            Assert.AreEqual(1f, rig.Focus.x, 1e-4f); // yaw 90 => 전방 = +X
        }
    }
}
