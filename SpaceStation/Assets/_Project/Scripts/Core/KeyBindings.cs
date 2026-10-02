using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace SpaceStation.Core
{
    /// <summary>변경 가능한 키보드 조작 (7-5). 순서 = 설정 '조작' 탭 표시 순서. 저장은 이름 기준이라 중간에 추가해도 된다.</summary>
    public enum GameAction
    {
        CameraForward,
        CameraBack,
        CameraLeft,
        CameraRight,
        CameraUp,
        CameraDown,
        CameraYawLeft,
        CameraYawRight,
        Rotate,
        NextCategory,
        Repair,
        Maintain,
        Rebuild,
        CancelRepair,
        Demolish,
        DemolishAlt,
        Pause,
        Speed1,
        Speed2,
        Speed3,
        Research,
        ToggleHelp,
    }

    /// <summary>키가 쓰이는 상황. 건설 중과 선택 중은 동시에 일어나지 않으므로 같은 키를 써도 된다.</summary>
    public enum ActionContext
    {
        /// <summary>항상 (카메라·시간·창)</summary>
        Always,
        /// <summary>모듈 배치 중</summary>
        Build,
        /// <summary>모듈 선택 중</summary>
        Selection,
    }

    /// <summary>
    /// 7-5 조작키 설정: 동작 → 키. PlayerPrefs(`keys.{동작}`)에 저장, 바뀌면 <see cref="Changed"/>.
    /// 고정 키(ESC, 숫자 1~9, F5 디버그)는 바꿀 수 없다. 같은 상황(또는 '항상')의 다른 동작과 겹치면 서로 맞바꾼다.
    /// Ctrl·Shift·Alt는 왼쪽/오른쪽을 같은 키로 본다.
    /// </summary>
    public static class KeyBindings
    {
        public struct Info
        {
            public GameAction Action;
            public string Group;
            public string Label;
            public Key Default;
            public ActionContext Context;
        }

        private const string Prefix = "keys.";

        public static readonly Info[] All =
        {
            new Info { Action = GameAction.CameraForward, Group = "카메라", Label = "앞으로 이동", Default = Key.W, Context = ActionContext.Always },
            new Info { Action = GameAction.CameraBack, Group = "카메라", Label = "뒤로 이동", Default = Key.S, Context = ActionContext.Always },
            new Info { Action = GameAction.CameraLeft, Group = "카메라", Label = "왼쪽 이동", Default = Key.A, Context = ActionContext.Always },
            new Info { Action = GameAction.CameraRight, Group = "카메라", Label = "오른쪽 이동", Default = Key.D, Context = ActionContext.Always },
            new Info { Action = GameAction.CameraUp, Group = "카메라", Label = "위로 이동", Default = Key.Space, Context = ActionContext.Always },
            new Info { Action = GameAction.CameraDown, Group = "카메라", Label = "아래로 이동", Default = Key.LeftCtrl, Context = ActionContext.Always },
            new Info { Action = GameAction.CameraYawLeft, Group = "카메라", Label = "왼쪽으로 회전", Default = Key.Q, Context = ActionContext.Always },
            new Info { Action = GameAction.CameraYawRight, Group = "카메라", Label = "오른쪽으로 회전", Default = Key.E, Context = ActionContext.Always },
            new Info { Action = GameAction.Rotate, Group = "건설", Label = "모듈 회전", Default = Key.R, Context = ActionContext.Build },
            new Info { Action = GameAction.NextCategory, Group = "건설", Label = "건설 탭 전환 (Shift: 반대로)", Default = Key.Tab, Context = ActionContext.Always },
            new Info { Action = GameAction.Repair, Group = "선택한 모듈", Label = "수리 / 우선 수리", Default = Key.R, Context = ActionContext.Selection },
            new Info { Action = GameAction.Maintain, Group = "선택한 모듈", Label = "정비", Default = Key.M, Context = ActionContext.Selection },
            new Info { Action = GameAction.Rebuild, Group = "선택한 모듈", Label = "재건축", Default = Key.B, Context = ActionContext.Selection },
            new Info { Action = GameAction.CancelRepair, Group = "선택한 모듈", Label = "수리 대기 취소", Default = Key.C, Context = ActionContext.Selection },
            new Info { Action = GameAction.Demolish, Group = "선택한 모듈", Label = "철거", Default = Key.Delete, Context = ActionContext.Selection },
            new Info { Action = GameAction.DemolishAlt, Group = "선택한 모듈", Label = "철거 (보조 키)", Default = Key.X, Context = ActionContext.Selection },
            new Info { Action = GameAction.Pause, Group = "시간", Label = "일시정지 / 재개", Default = Key.P, Context = ActionContext.Always },
            new Info { Action = GameAction.Speed1, Group = "시간", Label = "배속 1x", Default = Key.F1, Context = ActionContext.Always },
            new Info { Action = GameAction.Speed2, Group = "시간", Label = "배속 2x", Default = Key.F2, Context = ActionContext.Always },
            new Info { Action = GameAction.Speed3, Group = "시간", Label = "배속 4x", Default = Key.F3, Context = ActionContext.Always },
            new Info { Action = GameAction.Research, Group = "창", Label = "연구 창", Default = Key.T, Context = ActionContext.Always },
            new Info { Action = GameAction.ToggleHelp, Group = "창", Label = "조작 안내 고정 / 숨기기", Default = Key.H, Context = ActionContext.Always },
        };

        /// <summary>바꿀 수 없고 안내에만 보이는 조작 (설정 '조작' 탭 아래쪽).</summary>
        public static readonly (string Label, string Keys)[] Fixed =
        {
            ("카메라 회전", "휠 버튼 드래그"),
            ("카메라 화면 이동", "Shift + 휠 버튼 드래그"),
            ("확대 / 축소", "마우스 휠"),
            ("모듈 선택 · 배치", "왼쪽 클릭"),
            ("건설 메뉴 모듈 고르기", "숫자 1~9"),
            ("배치 취소", "오른쪽 클릭 / ESC"),
            ("선택 해제 · 창 닫기 · 메뉴", "ESC"),
        };

        private static Key[] _keys;

        public static event Action Changed;

        /// <summary>마지막 변경에서 맞바꾼 동작 (없으면 null). 설정 창 안내용.</summary>
        public static GameAction? LastSwapped { get; private set; }

        public static Key Get(GameAction action)
        {
            Load();
            return _keys[(int)action];
        }

        public static Info InfoOf(GameAction action) => All[IndexOf(action)];

        public static bool WasPressed(GameAction action)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return false;
            Key key = Get(action);
            if (key == Key.None)
                return false;
            Key twin = Twin(key);
            return keyboard[key].wasPressedThisFrame || (twin != Key.None && keyboard[twin].wasPressedThisFrame);
        }

        public static bool IsPressed(GameAction action)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return false;
            Key key = Get(action);
            if (key == Key.None)
                return false;
            Key twin = Twin(key);
            return keyboard[key].isPressed || (twin != Key.None && keyboard[twin].isPressed);
        }

        /// <summary>바꿀 수 없는 키 (ESC = 취소·메뉴, 숫자 1~9 = 모듈 고르기, F5 = 개발 빌드 디버그).</summary>
        public static bool IsReserved(Key key)
        {
            return key == Key.None || key == Key.Escape || key == Key.F5 || (key >= Key.Digit1 && key <= Key.Digit9)
                   || key == Key.LeftShift || key == Key.RightShift; // Shift는 Shift+Tab·Shift+드래그 조합용
        }

        /// <summary>두 상황에서 동시에 같은 키를 쓰면 문제가 되는지 (건설 중 ↔ 선택 중만 겹쳐도 됨).</summary>
        public static bool ContextsOverlap(ActionContext a, ActionContext b)
        {
            if (a == ActionContext.Always || b == ActionContext.Always)
                return true;
            return a == b;
        }

        /// <summary>
        /// 키를 바꾼다. 고정 키면 false. 같은 상황의 다른 동작이 이 키를 쓰고 있으면 그 동작에 원래 키를 넘긴다 (<see cref="LastSwapped"/>).
        /// </summary>
        public static bool Set(GameAction action, Key key)
        {
            Load();
            LastSwapped = null;
            if (!TryAssign(_keys, action, key, out var swapped))
                return false;
            LastSwapped = swapped;
            Save();
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// 키 배열(인덱스 = 동작)에 바꾸기 규칙을 적용한다 (저장 없음, 테스트 가능). 고정 키면 false.
        /// 같은 상황에서 이 키를 쓰던 동작에는 원래 키를 넘기고 swapped로 알려 준다.
        /// </summary>
        public static bool TryAssign(Key[] keys, GameAction action, Key key, out GameAction? swapped)
        {
            swapped = null;
            key = Normalize(key);
            if (IsReserved(key))
                return false;
            int index = (int)action;
            Key previous = keys[index];
            if (previous == key)
                return true;
            var context = InfoOf(action).Context;
            for (int i = 0; i < keys.Length; i++)
            {
                if (i == index || keys[i] != key || !ContextsOverlap(context, InfoOf((GameAction)i).Context))
                    continue;
                keys[i] = previous;
                swapped = (GameAction)i;
            }
            keys[index] = key;
            return true;
        }

        /// <summary>기본 키 배열 (인덱스 = 동작).</summary>
        public static Key[] DefaultKeys()
        {
            var keys = new Key[Enum.GetValues(typeof(GameAction)).Length];
            foreach (var info in All)
                keys[(int)info.Action] = info.Default;
            return keys;
        }

        public static void ResetAll()
        {
            Load();
            LastSwapped = null;
            foreach (var info in All)
                _keys[(int)info.Action] = info.Default;
            Save();
            Changed?.Invoke();
        }

        /// <summary>화면 표시용 짧은 이름 ("R", "Space", "Del", "F1").</summary>
        public static string Label(GameAction action) => KeyName(Get(action));

        public static string KeyName(Key key)
        {
            switch (key)
            {
                case Key.None: return "-";
                case Key.Space: return "Space";
                case Key.LeftCtrl: case Key.RightCtrl: return "Ctrl";
                case Key.LeftAlt: case Key.RightAlt: return "Alt";
                case Key.Delete: return "Del";
                case Key.Backspace: return "Backspace";
                case Key.Enter: return "Enter";
                case Key.NumpadEnter: return "Num Enter";
                case Key.Tab: return "Tab";
                case Key.Insert: return "Ins";
                case Key.Home: return "Home";
                case Key.End: return "End";
                case Key.PageUp: return "PgUp";
                case Key.PageDown: return "PgDn";
                case Key.UpArrow: return "↑";
                case Key.DownArrow: return "↓";
                case Key.LeftArrow: return "←";
                case Key.RightArrow: return "→";
                case Key.Digit0: return "0";
                case Key.Minus: return "-";
                case Key.Equals: return "=";
                case Key.LeftBracket: return "[";
                case Key.RightBracket: return "]";
                case Key.Semicolon: return ";";
                case Key.Quote: return "'";
                case Key.Comma: return ",";
                case Key.Period: return ".";
                case Key.Slash: return "/";
                case Key.Backslash: return "\\";
                case Key.Backquote: return "`";
                case Key.CapsLock: return "Caps";
            }
            if (key >= Key.A && key <= Key.Z)
                return ((char)('A' + (key - Key.A))).ToString();
            if (key >= Key.F1 && key <= Key.F12)
                return "F" + (key - Key.F1 + 1);
            if (key >= Key.Numpad0 && key <= Key.Numpad9)
                return "Num " + (key - Key.Numpad0);
            return key.ToString();
        }

        /// <summary>이번 프레임에 눌린 키 (키 변경 대기 중 사용). 없으면 Key.None.</summary>
        public static Key PressedThisFrame()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return Key.None;
            foreach (KeyControl control in keyboard.allKeys)
            {
                if (control != null && control.wasPressedThisFrame)
                    return control.keyCode;
            }
            return Key.None;
        }

        private static Key Normalize(Key key)
        {
            switch (key)
            {
                case Key.RightCtrl: return Key.LeftCtrl;
                case Key.RightAlt: return Key.LeftAlt;
                default: return key;
            }
        }

        private static Key Twin(Key key)
        {
            switch (key)
            {
                case Key.LeftCtrl: return Key.RightCtrl;
                case Key.LeftAlt: return Key.RightAlt;
                default: return Key.None;
            }
        }

        private static int IndexOf(GameAction action)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Action == action)
                    return i;
            }
            throw new ArgumentOutOfRangeException(nameof(action));
        }

        private static void Load()
        {
            if (_keys != null)
                return;
            _keys = new Key[Enum.GetValues(typeof(GameAction)).Length];
            foreach (var info in All)
            {
                int stored = PlayerPrefs.GetInt(Prefix + info.Action, (int)info.Default);
                var key = Enum.IsDefined(typeof(Key), stored) ? (Key)stored : info.Default;
                _keys[(int)info.Action] = IsReserved(key) ? info.Default : key;
            }
        }

        private static void Save()
        {
            foreach (var info in All)
                PlayerPrefs.SetInt(Prefix + info.Action, (int)_keys[(int)info.Action]);
            PlayerPrefs.Save();
        }
    }
}
