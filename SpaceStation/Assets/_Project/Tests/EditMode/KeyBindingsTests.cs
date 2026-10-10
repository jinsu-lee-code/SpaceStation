using NUnit.Framework;
using SpaceStation.Core;
using UnityEngine.InputSystem;

namespace SpaceStation.Tests
{
    /// <summary>7-5 조작키 변경 규칙 (저장 없이 키 배열로만 검증 — 실제 PlayerPrefs는 건드리지 않음).</summary>
    public class KeyBindingsTests
    {
        [Test]
        public void Defaults_LeftHandLayout()
        {
            var keys = KeyBindings.DefaultKeys();
            Assert.AreEqual(Key.W, keys[(int)GameAction.CameraForward]);
            Assert.AreEqual(Key.LeftAlt, keys[(int)GameAction.CameraUp], "12-0: Space는 일시정지로");
            Assert.AreEqual(Key.LeftCtrl, keys[(int)GameAction.CameraDown]);
            Assert.AreEqual(Key.R, keys[(int)GameAction.Rotate]);
            Assert.AreEqual(Key.R, keys[(int)GameAction.Repair], "회전(건설 중)과 수리(선택 중)는 같은 R");
            Assert.AreEqual(Key.F, keys[(int)GameAction.Maintain], "정비(선택 중)와 해치 이용(내부)은 같은 F");
            Assert.AreEqual(Key.X, keys[(int)GameAction.Demolish]);
            Assert.AreEqual(Key.Delete, keys[(int)GameAction.DemolishAlt]);
            Assert.AreEqual(Key.V, keys[(int)GameAction.EnterInterior]);
            Assert.AreEqual(Key.Space, keys[(int)GameAction.Pause]);
            Assert.AreEqual(Key.Tab, keys[(int)GameAction.SpeedCycle]);
            Assert.AreEqual(Key.Backquote, keys[(int)GameAction.NextCategory]);
            Assert.AreEqual(Key.F3, keys[(int)GameAction.Speed3]);
            Assert.AreEqual(Key.Y, keys[(int)GameAction.Roster]);
            Assert.AreEqual(Key.H, keys[(int)GameAction.ToggleHelp]);
            Assert.IsFalse(KeyBindings.HasConflict(keys), "기본 배치에 겹침 없음");
        }

        [Test]
        public void Migrate_OldDefaultsMove_CustomKeysStay()
        {
            var pause = KeyBindings.InfoOf(GameAction.Pause);
            Assert.AreEqual(Key.Space, KeyBindings.Migrate(pause, Key.P), "옛 기본 P → 새 기본 Space");
            Assert.AreEqual(Key.K, KeyBindings.Migrate(pause, Key.K), "사용자가 바꾼 키는 유지");
            var maintain = KeyBindings.InfoOf(GameAction.Maintain);
            Assert.AreEqual(Key.F, KeyBindings.Migrate(maintain, Key.M));
            var rebuild = KeyBindings.InfoOf(GameAction.Rebuild);
            Assert.AreEqual(Key.B, KeyBindings.Migrate(rebuild, Key.B), "바뀌지 않은 동작은 그대로");
        }

        [Test]
        public void Migrate_FromVersion2_OnlyLaterChanges()
        {
            var pad = KeyBindings.InfoOf(GameAction.Pad);
            Assert.AreEqual(Key.G, KeyBindings.Migrate(pad, Key.M, 2), "버전 2의 패드 기본 M → G");
            Assert.AreEqual(Key.J, KeyBindings.Migrate(pad, Key.J, 2), "사용자가 바꾼 패드 키는 유지");
            var pause = KeyBindings.InfoOf(GameAction.Pause);
            Assert.AreEqual(Key.P, KeyBindings.Migrate(pause, Key.P, 2), "버전 2에서 이미 옮긴 동작은 다시 옮기지 않음 (사용자가 P로 바꾼 것)");
        }

        [Test]
        public void HasConflict_DetectsSameContextOnly()
        {
            var keys = KeyBindings.DefaultKeys();
            keys[(int)GameAction.Research] = Key.Space; // 항상 ↔ 항상(일시정지)
            Assert.IsTrue(KeyBindings.HasConflict(keys));
        }

        [Test]
        public void Assign_SameContextConflict_SwapsKeys()
        {
            var keys = KeyBindings.DefaultKeys();
            // 정비(F)를 재건축 키(B)로 → 재건축은 F로
            Assert.IsTrue(KeyBindings.TryAssign(keys, GameAction.Maintain, Key.B, out var swapped));
            Assert.AreEqual(Key.B, keys[(int)GameAction.Maintain]);
            Assert.AreEqual(Key.F, keys[(int)GameAction.Rebuild]);
            Assert.AreEqual(GameAction.Rebuild, swapped);
        }

        [Test]
        public void Assign_BuildAndSelection_MayShareKey()
        {
            var keys = KeyBindings.DefaultKeys();
            // 회전(건설 중)을 재건축 키 B로 → 선택 중 동작이라 겹쳐도 그대로
            Assert.IsTrue(KeyBindings.TryAssign(keys, GameAction.Rotate, Key.B, out var swapped));
            Assert.IsNull(swapped);
            Assert.AreEqual(Key.B, keys[(int)GameAction.Rotate]);
            Assert.AreEqual(Key.B, keys[(int)GameAction.Rebuild]);
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
                Assert.AreEqual(Key.Space, keys[(int)GameAction.Pause]);
            }
        }

        [Test]
        public void Assign_RightCtrl_NormalizedToCtrl()
        {
            var keys = KeyBindings.DefaultKeys();
            Assert.IsTrue(KeyBindings.TryAssign(keys, GameAction.Pause, Key.RightCtrl, out var swapped));
            Assert.AreEqual(Key.LeftCtrl, keys[(int)GameAction.Pause]);
            Assert.AreEqual(GameAction.CameraDown, swapped, "Ctrl을 쓰던 카메라 아래로와 맞바뀜");
            Assert.AreEqual(Key.Space, keys[(int)GameAction.CameraDown]);
            Assert.AreEqual("Ctrl", KeyBindings.KeyName(Key.RightCtrl));
        }
    }
}
