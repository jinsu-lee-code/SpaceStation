using System;
using System.Collections;
using System.Collections.Generic;
using SpaceStation.Core;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// 장면 무대 (13-6b 이민선 연출에서 꺼냄 → 13-7b 장 전환 연출도 씀): 레터박스 · 자막(말하는 쪽 + 줄, 타자) · ESC 건너뛰기 안내 ·
    /// 검은 막(제목 카드) · 다른 HUD 감추기 · 시간 · 입력 멈춤 · 카메라 저장 / 복귀. 내용은 코드로 만든다.
    /// 쓰는 쪽(HUD 아래 화면 전체 RectTransform을 가진 MonoBehaviour)이 코루틴을 돌리고, LateUpdate에서 <see cref="PollSkip"/>을 부른다.
    /// </summary>
    public sealed class CinematicStage
    {
        public const string Soft = "#C8D2DE";
        public const float LineHold = 2.4f; // 한 줄이 다 나온 뒤 기다리는 시간

        private readonly RectTransform _root;
        private readonly HoloUi _ui;
        private readonly SimulationClock _clock;
        private readonly OrbitCameraController _orbit;
        private readonly CanvasGroup _group;
        private readonly RectTransform _barTop, _barBottom;
        private readonly TMP_Text _speaker, _line, _hint;
        private readonly Image _curtain;
        private readonly TMP_Text _title, _subtitle;
        private readonly List<(CanvasGroup group, float alpha, bool blocks)> _hidden = new List<(CanvasGroup, float, bool)>();
        private bool _wasPaused, _wasLocked, _wasBlocked;
        private Vector3 _savedFocus;
        private float _savedYaw, _savedPitch, _savedDistance;

        public bool Active { get; private set; }
        public bool Skip { get; set; }
        public OrbitCameraController Orbit => _orbit;
        public Transform Camera => _orbit != null ? _orbit.transform : null;

        /// <summary>
        /// 연출 한 프레임 (실제 초, 최대 0.05) — 끊김(로딩 · 에디터 멈춤)이 있어도 장면이 건너뛰지 않게.
        /// 녹화로 프레임 시간을 고정했으면(<see cref="Time.captureDeltaTime"/>) 그 값 — 영상 속도가 실제와 같게.
        /// </summary>
        public static float Dt => Time.captureDeltaTime > 0f ? Time.captureDeltaTime : Mathf.Min(Time.unscaledDeltaTime, 0.05f);

        public static float Smooth(float t) => t * t * (3f - 2f * t);

        public CinematicStage(RectTransform root, HoloUi ui, SimulationClock clock, OrbitCameraController orbit)
        {
            _root = root;
            _ui = ui;
            _clock = clock;
            _orbit = orbit;
            _group = root.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            // 제목 카드 막 (레터박스보다 아래, 처음엔 투명)
            var curtainRt = HoloUi.Rect("Curtain", root);
            curtainRt.anchorMin = Vector2.zero;
            curtainRt.anchorMax = Vector2.one;
            curtainRt.offsetMin = curtainRt.offsetMax = Vector2.zero;
            _curtain = curtainRt.gameObject.AddComponent<Image>();
            _curtain.color = new Color(0f, 0f, 0f, 0f);
            _curtain.raycastTarget = false;
            TitleLabels(ui, curtainRt, out _title, out _subtitle);
            SetTitleAlpha(0f);

            _barTop = Bar(root, "BarTop", top: true);
            _barBottom = Bar(root, "BarBottom", top: false);
            _speaker = ui.Label(_barBottom, "", 18f, TextAlignmentOptions.Center);
            _speaker.rectTransform.anchorMin = new Vector2(0f, 1f);
            _speaker.rectTransform.anchorMax = new Vector2(1f, 1f);
            _speaker.rectTransform.pivot = new Vector2(0.5f, 1f);
            _speaker.rectTransform.anchoredPosition = new Vector2(0f, -10f);
            _speaker.rectTransform.sizeDelta = new Vector2(0f, 24f);
            _line = ui.Label(_barBottom, "", 26f, TextAlignmentOptions.Center, wrap: true);
            _line.rectTransform.anchorMin = new Vector2(0.15f, 0f);
            _line.rectTransform.anchorMax = new Vector2(0.85f, 1f);
            _line.rectTransform.offsetMin = new Vector2(0f, 30f);
            _line.rectTransform.offsetMax = new Vector2(0f, -34f);
            _hint = ui.Label(_barBottom, $"<color={Soft}>ESC 건너뛰기</color>", 14f, TextAlignmentOptions.BottomRight);
            _hint.rectTransform.anchorMin = new Vector2(1f, 0f);
            _hint.rectTransform.anchorMax = new Vector2(1f, 0f);
            _hint.rectTransform.pivot = new Vector2(1f, 0f);
            _hint.rectTransform.anchoredPosition = new Vector2(-28f, 12f);
            _hint.rectTransform.sizeDelta = new Vector2(260f, 24f);
            SetBars(0f);
        }

        /// <summary>제목 카드 글씨 (장 번호 크게 + 부제) — 1장 컷신 끝 · 장 전환 연출이 같은 모양을 쓰게.</summary>
        public static void TitleLabels(HoloUi ui, RectTransform parent, out TMP_Text title, out TMP_Text subtitle)
        {
            var column = HoloUi.Rect("TitleCard", parent);
            column.anchorMin = column.anchorMax = column.pivot = new Vector2(0.5f, 0.5f);
            column.sizeDelta = new Vector2(1000f, 220f);
            title = ui.Label(column, "", 72f, TextAlignmentOptions.Center);
            HoloUi.Place(title.rectTransform, new Vector2(0f, 0f), new Vector2(1000f, 110f));
            HoloUi.Glow(title, 0.4f);
            subtitle = ui.Label(column, "", 30f, TextAlignmentOptions.Center);
            HoloUi.Place(subtitle.rectTransform, new Vector2(0f, -118f), new Vector2(1000f, 60f));
        }

        // ---------------- 시작 · 끝 ----------------

        /// <summary>시간 · 입력 멈춤, 카메라 저장(궤도 카메라 끔), 다른 HUD 감춤, 무대를 맨 앞으로, 레터박스가 들어옴.</summary>
        public void Begin(MonoBehaviour host)
        {
            Active = true;
            Skip = false;
            _wasPaused = _clock.Clock.IsPaused;
            _wasLocked = _clock.InputLocked;
            _wasBlocked = InputGate.Blocked;
            _clock.Clock.SetPaused(true);
            _clock.InputLocked = true;
            InputGate.Blocked = true;
            var rig = _orbit.Rig;
            _savedFocus = rig.Focus;
            _savedYaw = rig.Yaw;
            _savedPitch = rig.Pitch;
            _savedDistance = rig.Distance;
            _orbit.enabled = false;
            HideHud(true);
            _root.SetAsLastSibling();
            _group.alpha = 1f;
            _hint.gameObject.SetActive(true);
            ClearText();
            host.StartCoroutine(BarsIn());
        }

        /// <summary>카메라 · HUD를 되돌리고 레터박스를 거둠 (시간 · 입력은 그대로 — <see cref="Release"/>).</summary>
        public void RestoreView()
        {
            _orbit.Restore(_savedFocus, _savedYaw, _savedPitch, _savedDistance);
            _orbit.enabled = true;
            HideHud(false);
            SetBars(0f);
            ClearText();
            _hint.gameObject.SetActive(false);
        }

        /// <summary>시간 · 입력을 연출 전 상태로, 무대를 투명하게.</summary>
        public void Release()
        {
            _group.alpha = 0f;
            _curtain.color = new Color(0f, 0f, 0f, 0f);
            SetTitleAlpha(0f);
            _clock.InputLocked = _wasLocked;
            _clock.Clock.SetPaused(_wasPaused);
            InputGate.Blocked = _wasBlocked;
            Active = false;
        }

        public void End()
        {
            RestoreView();
            Release();
        }

        /// <summary>쓰는 쪽 LateUpdate에서: ESC = 건너뛰기.</summary>
        public void PollSkip()
        {
            if (!Active)
                return;
            var k = Keyboard.current;
            if (k != null && k.escapeKey.wasPressedThisFrame)
            {
                InputGate.ConsumeEscape();
                Skip = true;
            }
        }

        public static bool NextPressed()
        {
            var k = Keyboard.current;
            var m = Mouse.current;
            return (k != null && (k.spaceKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame)) || (m != null && m.leftButton.wasPressedThisFrame);
        }

        // ---------------- 자막 ----------------

        public void SetSpeaker(string rich) => _speaker.SetText(rich ?? "");

        public void SetLine(string rich) => _line.SetText(rich ?? "");

        public void ClearText()
        {
            _speaker.SetText("");
            _line.SetText("");
        }

        /// <summary>넘기지 않고 다 볼 때 걸리는 시간.</summary>
        public static float SaySeconds(IList<string> lines, float charsPerSecond)
        {
            float s = 0f;
            foreach (var text in lines)
                s += text.Length / Mathf.Max(1f, charsPerSecond) + LineHold;
            return Mathf.Max(0.5f, s);
        }

        /// <summary>줄마다 타자 → 다 나오면 2.4초 또는 클릭 · Space · Enter로 다음 (건너뛰면 멈춤). onLine = 줄이 시작될 때 (코어 기록 등).</summary>
        public IEnumerator Say(string speaker, string color, IList<string> lines, float charsPerSecond, Action<string> onLine = null)
        {
            SetSpeaker(string.IsNullOrEmpty(speaker) ? "" : $"<color={color}>{speaker}</color>");
            foreach (var text in lines)
            {
                if (Skip)
                    yield break;
                onLine?.Invoke(text);
                float typed = 0f;
                while (!Skip && typed < text.Length)
                {
                    typed += Dt * charsPerSecond;
                    if (NextPressed())
                        typed = text.Length;
                    SetLine($"<color={color}>{text.Substring(0, Mathf.Min(text.Length, Mathf.FloorToInt(typed)))}</color>");
                    yield return null;
                }
                SetLine($"<color={color}>{text}</color>");
                yield return null;
                for (float w = 0f; w < LineHold && !Skip && !NextPressed(); w += Dt)
                    yield return null;
            }
        }

        // ---------------- 카메라 · 레터박스 ----------------

        /// <summary>저장한 시점으로 부드럽게 돌아오며 레터박스가 나감 (건너뛰면 0.25초).</summary>
        public IEnumerator ReturnCamera(float seconds)
        {
            var cam = _orbit.transform;
            var rig = _orbit.Rig;
            rig.Set(_savedFocus, _savedYaw, _savedPitch, _savedDistance);
            Vector3 fromPos = cam.position;
            Quaternion fromRot = cam.rotation;
            float s = Skip ? 0.25f : seconds;
            for (float k = 0f; k < 1f; k += Dt / s)
            {
                float b = Smooth(Mathf.Clamp01(k));
                cam.SetPositionAndRotation(Vector3.Lerp(fromPos, rig.Position, b), Quaternion.Slerp(fromRot, rig.Rotation, b));
                SetBars(1f - b);
                yield return null;
            }
            SetBars(0f);
        }

        private IEnumerator BarsIn()
        {
            for (float k = 0f; k < 1f && Active; k += Dt / 0.4f)
            {
                SetBars(Smooth(Mathf.Clamp01(k)));
                yield return null;
            }
            if (Active)
                SetBars(1f);
        }

        /// <summary>레터박스 0 ~ 1 (화면 위아래 검은 띠가 들어오고 나감).</summary>
        public void SetBars(float k)
        {
            _barTop.anchoredPosition = new Vector2(0f, (1f - k) * 150f);
            _barBottom.anchoredPosition = new Vector2(0f, -(1f - k) * 150f);
        }

        // ---------------- 제목 카드 ----------------

        /// <summary>검은 막 0 ~ 1.</summary>
        public void SetCurtain(float a) => _curtain.color = new Color(0f, 0f, 0f, Mathf.Clamp01(a));

        public void SetTitle(string title, string subtitle)
        {
            _title.SetText(title ?? "");
            _subtitle.SetText(string.IsNullOrEmpty(subtitle) ? "" : $"<color={Soft}>{subtitle}</color>");
        }

        public void SetTitleAlpha(float a)
        {
            _title.alpha = a;
            _subtitle.alpha = a;
        }

        public IEnumerator Fade(Action<float> apply, float from, float to, float seconds)
        {
            for (float k = 0f; k < 1f; k += Dt / Mathf.Max(0.01f, seconds))
            {
                apply(Mathf.Lerp(from, to, Smooth(Mathf.Clamp01(k))));
                yield return null;
            }
            apply(to);
        }

        // ---------------- HUD ----------------

        /// <summary>장면 동안 다른 HUD를 감춤 (CanvasGroup 투명도, 끝나면 되돌림).</summary>
        private void HideHud(bool hide)
        {
            if (!hide)
            {
                foreach (var (group, alpha, blocks) in _hidden)
                {
                    if (group == null)
                        continue;
                    group.alpha = alpha;
                    group.blocksRaycasts = blocks;
                }
                _hidden.Clear();
                return;
            }
            var parent = _root.parent;
            if (parent == null)
                return;
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child == _root || !child.gameObject.activeSelf)
                    continue;
                var group = child.GetComponent<CanvasGroup>();
                if (group == null)
                    group = child.gameObject.AddComponent<CanvasGroup>();
                _hidden.Add((group, group.alpha, group.blocksRaycasts));
                group.alpha = 0f;
                group.blocksRaycasts = false;
            }
        }

        private static RectTransform Bar(RectTransform parent, string name, bool top)
        {
            var rt = HoloUi.Rect(name, parent);
            rt.anchorMin = new Vector2(0f, top ? 1f : 0f);
            rt.anchorMax = new Vector2(1f, top ? 1f : 0f);
            rt.pivot = new Vector2(0.5f, top ? 1f : 0f);
            rt.sizeDelta = new Vector2(0f, 150f);
            var image = rt.gameObject.AddComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = false;
            return rt;
        }
    }
}
