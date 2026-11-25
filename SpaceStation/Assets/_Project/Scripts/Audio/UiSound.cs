using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpaceStation.Audio
{
    /// <summary>
    /// 버튼 소리 (2026-11-25 홀로그램 UI 한 벌 — SOUND_LIST_NEW D-6 동작 9): 마우스 올림 → 호버, 클릭 → 확인음, 취소 · 닫기 · 뒤로 버튼 → 취소음,
    /// 비활성 버튼 클릭 → 거절음. 토글(Toggle)은 클릭음 대신 켜짐 · 꺼짐 소리, 슬라이더(Slider)는 값이 8칸 중 한 칸 바뀔 때 "톡" (오음계로 오름)
    /// — 둘 다 마우스로 바꿨을 때만 (코드가 값을 넣을 땐 조용).
    /// 클릭 결과에 전용 소리가 있는 버튼(건설·탭·배속)은 Silent로 두어 소리가 겹치지 않게 한다.
    /// </summary>
    public sealed class UiSound : MonoBehaviour, IPointerEnterHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        public enum ClickMode
        {
            /// <summary>확인음. 버튼 이름 · 글이 취소 쪽이면 (<see cref="LooksLikeCancel"/>) 누를 때 저절로 취소음.</summary>
            Click,
            Silent,
            /// <summary>취소음 (만드는 쪽에서 직접 지정할 때).</summary>
            Cancel,
        }

        [SerializeField] private ClickMode _click = ClickMode.Click;

        /// <summary>슬라이더를 나누는 칸 수 — 한 칸 바뀔 때마다 톡 하나.</summary>
        private const int SliderCells = 8;
        /// <summary>
        /// 칸 0 = 음계 칸 2 (파5): 소리 파일은 도5 높이 (TryPlayNote 기준 음 = 도)라 칸 2부터 부르면
        /// 파 · 솔 · 라 · 도 · 레 (승인된 시안의 다섯 톡) · 파 · 솔 · 라로 오름.
        /// </summary>
        private const int SliderFirstStep = 2;

        // 취소 쪽 버튼 글 · 이름 (앞부분). 버튼 글은 누를 때마다 다시 읽음 (줄마다 글이 바뀌는 버튼 — 주민 이사 [취소] 등).
        private static readonly string[] CancelWords = { "닫기", "취소", "뒤로", "아니오", "아니요", "그만두기", "Close", "Cancel", "Back" };
        private const char Times = '×';

        private Selectable _selectable;
        private Toggle _toggle;
        private Slider _slider;
        private TMP_Text _label;
        private int _clickFrame = -1;
        private int _pendingFrame = -1;
        private bool _pendingOn;
        private int _pendingCell = -1;
        private int _lastCell = -1;
        private bool _held;
        private bool _pressInteractable = true;

        public ClickMode Mode
        {
            get => _click;
            set => _click = value;
        }

        private void Awake()
        {
            _selectable = GetComponent<Selectable>();
            _toggle = _selectable as Toggle;
            _slider = _selectable as Slider;
            _label = GetComponentInChildren<TMP_Text>(true);
            if (_toggle != null)
                _toggle.onValueChanged.AddListener(OnToggleChanged);
            if (_slider != null)
            {
                _lastCell = Cell(_slider.normalizedValue);
                _slider.onValueChanged.AddListener(OnSliderChanged);
            }
        }

        private void OnDisable() => _held = false;

        private bool Interactable => _selectable == null || _selectable.IsInteractable();

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (Interactable && !_held)
                AudioService.TryPlay(l => l.UiHover);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
                return;
            // 누른 순간의 상태로 거절음을 가림: 버튼이 창을 닫으면 (CanvasGroup 비활성) 클릭 처리 때 이미 비활성이라
            // 전엔 닫기 버튼마다 거절음이 함께 났음 (2026-11-25 실제 조작 시험에서 찾음)
            _pressInteractable = Interactable;
            if (_slider == null)
                return;
            _held = true;
            // 슬라이더가 먼저 값을 바꿨으면 (같은 오브젝트의 컴포넌트 순서) 그 칸 소리를 지금
            if (_pendingFrame == Time.frameCount)
                PlaySliderCell(_pendingCell);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
                _held = false;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
                return;
            if (!_pressInteractable)
            {
                AudioService.TryPlay(l => l.UiError);
                return;
            }
            if (_toggle != null)
            {
                _clickFrame = Time.frameCount;
                if (_pendingFrame == Time.frameCount)
                    PlayToggle(_pendingOn);
                return;
            }
            if (_slider != null)
                return; // 슬라이더는 칸 소리만
            var mode = _click == ClickMode.Click && LooksLikeCancel() ? ClickMode.Cancel : _click;
            if (mode == ClickMode.Click)
                AudioService.TryPlay(l => l.UiClick);
            else if (mode == ClickMode.Cancel)
                PlayCancel();
        }

        /// <summary>취소음. 같은 클릭으로 창이 닫혀 창 닫힘 소리가 막 울렸으면 그것을 멈추고 취소음 하나만 (두 내림 음이 겹쳐 흐려지지 않게).</summary>
        private static void PlayCancel()
        {
            var s = AudioService.Instance;
            if (s == null || s.Library == null)
                return;
            s.StopStartedThisFrame(s.Library.UiClose);
            s.Play(s.Library.UiCancel);
        }

        /// <summary>버튼 글 (태그 · 괄호 앞) 또는 오브젝트 이름이 닫기 · 취소 · 뒤로 · 아니오 · × · X · Close · Cancel · Back으로 시작하는지.</summary>
        private bool LooksLikeCancel()
        {
            if (_label != null)
            {
                string text = StripTags(_label.text).Trim();
                if (text.Length == 1 && (text[0] == Times || text[0] == 'X' || text[0] == 'x'))
                    return true;
                if (StartsWithWord(text))
                    return true;
            }
            return StartsWithWord(gameObject.name);
        }

        private static bool StartsWithWord(string s)
        {
            if (string.IsNullOrEmpty(s))
                return false;
            foreach (var w in CancelWords)
            {
                if (!s.StartsWith(w, StringComparison.OrdinalIgnoreCase))
                    continue;
                // "Back"은 "BackButton" · "Back (ESC)"는 되고 "Backing"은 안 됨 (영문 낱말 경계)
                if (s.Length == w.Length || !char.IsLower(s[w.Length]))
                    return true;
            }
            return false;
        }

        private static string StripTags(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('<') < 0)
                return s ?? string.Empty;
            var sb = new System.Text.StringBuilder(s.Length);
            bool tag = false;
            foreach (char c in s)
            {
                if (c == '<')
                    tag = true;
                else if (c == '>')
                    tag = false;
                else if (!tag)
                    sb.Append(c);
            }
            return sb.ToString();
        }

        // ---------------- 토글 ----------------

        private void OnToggleChanged(bool on)
        {
            if (_clickFrame == Time.frameCount)
            {
                PlayToggle(on);
                return;
            }
            // 클릭 처리 순서가 토글 먼저면 OnPointerClick에서 냄, 아니면 코드가 바꾼 것 (조용)
            _pendingFrame = Time.frameCount;
            _pendingOn = on;
        }

        private void PlayToggle(bool on)
        {
            _pendingFrame = -1;
            AudioService.TryPlay(l => on ? l.UiToggleOn : l.UiToggleOff);
        }

        // ---------------- 슬라이더 ----------------

        private static int Cell(float normalized) => Mathf.Clamp(Mathf.FloorToInt(normalized * SliderCells), 0, SliderCells - 1);

        private void OnSliderChanged(float _)
        {
            int cell = Cell(_slider.normalizedValue);
            if (cell == _lastCell)
                return;
            _lastCell = cell;
            if (_held)
            {
                PlaySliderCell(cell);
                return;
            }
            _pendingFrame = Time.frameCount;
            _pendingCell = cell;
        }

        private void PlaySliderCell(int cell)
        {
            _pendingFrame = -1;
            AudioService.TryPlayNote(l => l.UiSlider, SliderFirstStep + cell);
        }
    }
}
