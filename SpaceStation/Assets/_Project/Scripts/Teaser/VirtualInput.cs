#if UNITY_EDITOR
// 11-19 플레이 영상 녹화 전용 — 사람 대신 가상 마우스 · 키보드로 실제 게임을 조작한다 (빌드에서는 빠짐).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace SpaceStation.Teaser
{
    /// <summary>
    /// 녹화 동안 진짜 마우스 · 키보드를 끄고 가상 장치를 "현재 장치"로 둔다 (게임 코드는 Mouse.current · Keyboard.current를 읽음).
    /// 게임 뷰에 포커스가 없어도 입력이 가도록 Input System 설정을 잠시 바꾸고, <see cref="End"/>에서 모두 되돌린다.
    /// 상태는 매 프레임 <see cref="Flush"/>로 보낸다 (마우스 이동량은 그 프레임 것만).
    /// </summary>
    public sealed class VirtualInput
    {
        private Mouse _mouse;
        private Keyboard _keyboard;
        private readonly List<InputDevice> _disabled = new List<InputDevice>();
        private readonly HashSet<Key> _keys = new HashSet<Key>();
        private InputSettings.EditorInputBehaviorInPlayMode _editorBehavior;
        private InputSettings.BackgroundBehavior _background;
        private Vector2 _delta;

        public Vector2 Position { get; set; }
        public bool LeftButton { get; set; }
        public bool Active => _mouse != null;

        public void Begin()
        {
            if (Active)
                return;
            var settings = InputSystem.settings;
            _editorBehavior = settings.editorInputBehaviorInPlayMode;
            _background = settings.backgroundBehavior;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            foreach (var device in InputSystem.devices)
            {
                if ((device is Mouse || device is Keyboard) && device.enabled)
                {
                    InputSystem.DisableDevice(device);
                    _disabled.Add(device);
                }
            }
            _mouse = InputSystem.AddDevice<Mouse>("TourMouse");
            _keyboard = InputSystem.AddDevice<Keyboard>("TourKeyboard");
            _mouse.MakeCurrent();
            _keyboard.MakeCurrent();
            Position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        }

        public void End()
        {
            if (!Active)
                return;
            InputSystem.RemoveDevice(_mouse);
            InputSystem.RemoveDevice(_keyboard);
            _mouse = null;
            _keyboard = null;
            foreach (var device in _disabled)
            {
                if (device != null && device.added)
                    InputSystem.EnableDevice(device);
            }
            _disabled.Clear();
            _keys.Clear();
            var settings = InputSystem.settings;
            settings.editorInputBehaviorInPlayMode = _editorBehavior;
            settings.backgroundBehavior = _background;
        }

        public void Press(Key key) => _keys.Add(key);
        public void Release(Key key) => _keys.Remove(key);
        public bool IsDown(Key key) => _keys.Contains(key);
        public void ReleaseAll() => _keys.Clear();

        /// <summary>이번 프레임 마우스 이동량 (1인칭 시점 회전).</summary>
        public void AddDelta(Vector2 pixels) => _delta += pixels;

        /// <summary>이번 프레임 상태를 보냄 (다음 입력 갱신 때 게임이 읽음).</summary>
        public void Flush()
        {
            if (!Active)
                return;
            var mouseState = new MouseState { position = Position, delta = _delta };
            mouseState = mouseState.WithButton(MouseButton.Left, LeftButton);
            InputSystem.QueueStateEvent(_mouse, mouseState);
            var keys = new Key[_keys.Count];
            _keys.CopyTo(keys);
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys));
            _delta = Vector2.zero;
        }
    }
}
#endif
