using NUnit.Framework;
using SpaceStation.Core;
using UnityEngine.InputSystem;

namespace SpaceStation.Tests
{
    /// <summary>7-5 조작키 변경 규칙 (저장 없이 키 배열로만 검증 — 실제 PlayerPrefs는 건드리지 않음).</summary>
    public class KeyBindingsTests
    {
        [Test]
        public void Defaults_MatchPreviousHardcodedKeys()
        {
            var keys = KeyBindings.DefaultKeys();
            Assert.AreEqual(Key.W, keys[(int)GameAction.CameraForward]);
            Assert.AreEqual(Key.LeftCtrl, keys[(int)GameAction.CameraDown]);
            Assert.AreEqual(Key.R, keys[(int)GameAction.Rotate]);
            Assert.AreEqual(Key.R, keys[(int)GameAction.Repair], "회전(건설 중)과 수리(선택 중)는 같은 R");
            Assert.AreEqual(Key.Delete, keys[(int)GameAction.Demolish]);
            Assert.AreEqual(Key.X, keys[(int)GameAction.DemolishAlt]);
            Assert.AreEqual(Key.F3, keys[(int)GameAction.Speed3]);
            Assert.AreEqual(Key.H, keys[(int)GameAction.ToggleHelp]);
        }

        [Test]
        public void Assign_SameContextConflict_SwapsKeys()
        {
            var keys = KeyBindings.DefaultKeys();
            // 정비(M)를 재건축 키(B)로 → 재건축은 M으로
            Assert.IsTrue(KeyBindings.TryAssign(keys, GameAction.Maintain, Key.B, out var swapped));
            Assert.AreEqual(Key.B, keys[(int)GameAction.Maintain]);
            Assert.AreEqual(Key.M, keys[(int)GameAction.Rebuild]);
            Assert.AreEqual(GameAction.Rebuild, swapped);
        }

        [Test]
        public void Assign_BuildAndSelection_MayShareKey()
        {
            var keys = KeyBindings.DefaultKeys();
            // 회전(건설 중)을 정비 키 M으로 → 선택 중 동작이라 겹쳐도 그대로
            Assert.IsTrue(KeyBindings.TryAssign(keys, GameAction.Rotate, Key.M, out var swapped));
            Assert.IsNull(swapped);
            Assert.AreEqual(Key.M, keys[(int)GameAction.Rotate]);
            Assert.AreEqual(Key.M, keys[(int)GameAction.Maintain]);
        }

        [Test]
        public void Assign_AlwaysContext_ConflictsWithEverything()
        {
            var keys = KeyBindings.DefaultKeys();
            // 카메라 앞으로(항상)를 R로 → 회전(건설)·수리(선택) 둘 다 겹침, 둘 다 W를 넘겨받음
            Assert.IsTrue(KeyBindings.TryAssign(keys, GameAction.CameraForward, Key.R, out var swapped));
            Assert.IsTrue(swapped.HasValue);
            Assert.AreEqual(Key.R, keys[(int)GameAction.CameraForward]);
            Assert.AreEqual(Key.W, keys[(int)GameAction.Rotate]);
            Assert.AreEqual(Key.W, keys[(int)GameAction.Repair]);
        }

        [Test]
        public void Assign_ReservedKeys_Rejected()
        {
            var keys = KeyBindings.DefaultKeys();
            foreach (var key in new[] { Key.Escape, Key.Digit1, Key.Digit9, Key.LeftShift, Key.F5 })
            {
                Assert.IsFalse(KeyBindings.TryAssign(keys, GameAction.Pause, key, out _), key.ToString());
                Assert.AreEqual(Key.P, keys[(int)GameAction.Pause]);
            }
        }

        [Test]
        public void Assign_RightCtrl_NormalizedToCtrl()
        {
            var keys = KeyBindings.DefaultKeys();
            Assert.IsTrue(KeyBindings.TryAssign(keys, GameAction.Pause, Key.RightCtrl, out var swapped));
            Assert.AreEqual(Key.LeftCtrl, keys[(int)GameAction.Pause]);
            Assert.AreEqual(GameAction.CameraDown, swapped, "Ctrl을 쓰던 카메라 아래로와 맞바뀜");
            Assert.AreEqual(Key.P, keys[(int)GameAction.CameraDown]);
            Assert.AreEqual("Ctrl", KeyBindings.KeyName(Key.RightCtrl));
        }
    }
}
