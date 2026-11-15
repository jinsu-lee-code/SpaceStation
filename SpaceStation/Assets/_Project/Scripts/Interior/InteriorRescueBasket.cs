using SpaceStation.Audio;
using SpaceStation.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 수리 사령부 체험 ① "구조 바구니" (TASKS 2026-11-13, 사용자 2026-11-15 "체험으로 넘어가자", 높이 규칙 = <see cref="RescueBasketRide"/>, 값 = <see cref="RepairCommand"/>):
    /// 땅 정류장의 엮은 바구니에 들어가 F를 누를 때마다 도르래 줄을 한 번 당겨 오름 (꾹 = 이어서 당김) → 나무집 마루 바구니 문에 닿음.
    /// 공중에서 손을 떼면 멈춤쇠가 붙잡고, S 꾹 = 천천히 내려감. 탄 사람은 바구니에 실려 감 (<see cref="FirstPersonController.Ride"/> — 공중에서는 걸어 나가지 못함).
    /// 빈 바구니는 정류장 · 마루 문 앞에 다가가면 혼자 내려오고 올라옴. 바구니가 마루에 없으면 마루 난간 문을 금빛 막대 · 보이지 않는 벽으로 막음.
    /// 걸이 줄 (도르래 → 바구니 고리)은 높이에 맞춰 줄어듦. 실제 시간.
    /// </summary>
    public sealed class InteriorRescueBasket : MonoBehaviour
    {
        private const float GateOpenAngle = -82f;   // 금빛 막대가 경첩에서 들리는 각 (도)
        private const float GateSpeed = 2.5f;

        [Tooltip("바구니 (원점 = 바닥 윗면 가운데, 땅 정류장)")]
        [SerializeField] private Transform _basket;
        [Tooltip("걸이 줄 (원점 = 도르래 바퀴, 아래로 바구니 고리까지)")]
        [SerializeField] private Transform _rope;
        [Tooltip("마루 바구니 문을 막는 보이지 않는 벽")]
        [SerializeField] private Collider _gate;
        [Tooltip("마루 바구니 문 금빛 막대 (원점 = 경첩, 바구니가 마루에 서면 들림)")]
        [SerializeField] private Transform _gateBar;
        [SerializeField] private TMP_FontAsset _font;

        private RescueBasketRide _ride;
        private Vector3 _basketBase;
        private Quaternion _gateRest;
        private float _gateOpen;
        private float _ropeLength;
        private FirstPersonController _player;
        private bool _riding;
        private bool _inside;
        private float _sway;
        private string _notice;
        private float _noticeTimer;
        private Canvas _canvas;
        private TextMeshProUGUI _hint;
        private string _hintShown;

        public RescueBasketRide Ride => _ride;
        public bool Riding => _riding;

        public void Configure(Transform basket, Transform rope, Collider gate, Transform gateBar, TMP_FontAsset font)
        {
            _basket = basket;
            _rope = rope;
            _gate = gate;
            _gateBar = gateBar;
            _font = font;
        }

        private void Awake()
        {
            _ride = new RescueBasketRide(RepairCommand.BasketTop);
            _basketBase = _basket != null ? _basket.localPosition : RepairCommand.Basket;
            _ropeLength = RepairCommand.Pulley.y - (RepairCommand.Basket.y + RepairCommand.BasketHook);
            if (_gateBar != null)
                _gateRest = _gateBar.localRotation;
        }

        private void OnDisable()
        {
            // 방을 나가면 가까운 정류장에 세우고 내려놓음 (다음에 들어오면 공중에 매달린 바구니가 없게)
            if (_ride != null && !_ride.AtTop && !_ride.AtBottom)
                _ride.Park(_ride.Height > _ride.Top * 0.5f ? _ride.Top : 0f);
            Release();
            Apply(0f);
            _noticeTimer = 0f;
            ShowHint(null);
        }

        private void OnDestroy()
        {
            if (_canvas != null)
                Destroy(_canvas.gameObject);
        }

        /// <summary>발이 바구니 안 (엮은 벽 안쪽 · 바닥 판 높이 근처)인지.</summary>
        public bool Contains(Vector3 worldFeet)
        {
            var p = transform.InverseTransformPoint(worldFeet) - Floor();
            return new Vector2(p.x, p.z).magnitude < RepairCommand.BasketInnerRadius && p.y > -0.35f && p.y < 0.9f;
        }

        private Vector3 Floor() => _basketBase + Vector3.up * (_ride.Height + RepairCommand.BasketFloorTop);

        private void Update()
        {
            if (_basket == null)
                return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (_player == null)
                _player = FindFirstObjectByType<FirstPersonController>();
            if (_noticeTimer > 0f)
                _noticeTimer -= dt;
            bool wasInside = _inside;
            _inside = _player != null && (_riding || (Contains(_player.transform.position) && !_player.Riding));
            if (_inside && !wasInside)
                _ride.Board();
            if (_inside)
                Control();
            else
            {
                _ride.Lower(false);
                CallIfWaiting();
            }
            var arrival = _ride.Step(dt);
            if (_ride.Moving && _inside && !_riding)
            {
                _player.Ride(_basket);
                _riding = true;
            }
            if (arrival == RescueBasketRide.Arrival.Top)
            {
                if (_riding)
                    Notice($"나무집 관제실에 닿았어요  ·  줄을 {_ride.Strokes}번 당겼어요", 3.5f);
                AudioService.TryPlay(l => l.SupplyArrive);
            }
            else if (arrival == RescueBasketRide.Arrival.Bottom)
            {
                if (_riding)
                    Notice("땅에 내려왔어요", 2.5f);
                AudioService.TryPlay(l => l.BuildPlace);
            }
            if (_riding && !_ride.Moving && (_ride.AtTop || _ride.AtBottom))
                Release();
            Apply(dt);
            UpdateHint();
        }

        /// <summary>탄 사람: F = 줄 한 번 당김 (꾹 = 한 번 끝날 때마다 이어서), S 꾹 = 멈춤쇠를 풀고 내려감.</summary>
        private void Control()
        {
            bool down = KeyBindings.IsPressed(GameAction.CameraBack);
            _ride.Lower(down && !_ride.Pulling);
            if (!down && (KeyBindings.WasPressed(GameAction.Interact) || (KeyBindings.IsPressed(GameAction.Interact) && !_ride.Pulling)))
            {
                if (_ride.Pull())
                    AudioService.TryPlay(l => l.BuildRotate);   // 도르래 삐걱
            }
        }

        /// <summary>빈 바구니: 정류장 · 마루 문 앞에 다가가면 그쪽으로 옴.</summary>
        private void CallIfWaiting()
        {
            if (_player == null || _ride.Moving || _player.Riding)
                return;
            var p = transform.InverseTransformPoint(_player.transform.position);
            var station = RepairCommand.Basket;
            var gate = RepairCommand.BasketGate;
            if (!_ride.AtBottom && Mathf.Abs(p.y - station.y) < 1.2f && Flat(p - station) < RepairCommand.BasketCallReach)
            {
                _ride.Call(false);
                Notice("구조 바구니가 내려와요", 2.5f);
                AudioService.TryPlay(l => l.UiOpen);
            }
            else if (!_ride.AtTop && Mathf.Abs(p.y - gate.y) < 1.2f && Flat(p - gate) < RepairCommand.BasketCallReach)
            {
                _ride.Call(true);
                Notice("구조 바구니가 올라와요", 2.5f);
                AudioService.TryPlay(l => l.UiOpen);
            }
        }

        private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        private void Release()
        {
            if (!_riding)
                return;
            _riding = false;
            if (_player != null)
                _player.Dismount();
        }

        private void Apply(float dt)
        {
            if (_ride == null)
                return;
            // 바구니: 높이 + 오를 때 살짝 흔들림 (줄 끝 진자 — 당길 때마다 툭)
            _sway = Mathf.Lerp(_sway, Mathf.Clamp(Mathf.Abs(_ride.Speed) * 0.6f, 0f, 1.2f), dt > 0f ? 1f - Mathf.Exp(-dt * 3f) : 1f);   // 도 (멀미 나지 않게 작게)
            if (_basket != null)
            {
                _basket.localPosition = _basketBase + Vector3.up * _ride.Height;
                _basket.localRotation = Quaternion.Euler(0f, 0f, _ride.Moving ? _sway * Mathf.Sin(Time.unscaledTime * 2.4f) : 0f);
            }
            if (_rope != null && _ropeLength > 0.01f)
                _rope.localScale = new Vector3(1f, Mathf.Max(0.02f, (_ropeLength - _ride.Height) / _ropeLength), 1f);
            bool parkedTop = _ride.AtTop && !_ride.Moving;
            if (_gate != null)
                _gate.enabled = !parkedTop;
            if (_gateBar != null)
            {
                _gateOpen = dt > 0f ? Mathf.MoveTowards(_gateOpen, parkedTop ? 1f : 0f, dt * GateSpeed) : (parkedTop ? 1f : 0f);
                _gateBar.localRotation = _gateRest * Quaternion.Euler(GateOpenAngle * Mathf.SmoothStep(0f, 1f, _gateOpen), 0f, 0f);
            }
        }

        // ---------------- 안내 ----------------

        private void UpdateHint()
        {
            if (_noticeTimer > 0f)
            {
                ShowHint(_notice);
                return;
            }
            if (!_inside)
            {
                ShowHint(null);
                return;
            }
            string f = KeyBindings.Label(GameAction.Interact), s = KeyBindings.Label(GameAction.CameraBack);
            if (_ride.AtTop && !_ride.Moving)
                ShowHint($"나무집 마루로 걸어 나가요  ·  <b>{s}</b> 꾹 = 내려가기");
            else if (_ride.AtBottom && !_ride.Moving)
                ShowHint($"<b>{f}</b> = 도르래 줄 당기기 (나무집 관제실까지 {_ride.StrokesLeft}번)");
            else
                ShowHint($"<b>{f}</b> 줄 당기기 · 남은 {Mathf.Max(0, _ride.StrokesLeft)}번  ·  <b>{s}</b> 꾹 = 내려가기  ·  {Mathf.RoundToInt(_ride.Height / _ride.Top * 100f)}%");
        }

        private void Notice(string text, float seconds)
        {
            _notice = text;
            _noticeTimer = seconds;
        }

        private void ShowHint(string text)
        {
            if (text == _hintShown)
                return;
            _hintShown = text;
            if (text == null)
            {
                if (_canvas != null)
                    _canvas.gameObject.SetActive(false);
                return;
            }
            EnsureCanvas();
            _canvas.gameObject.SetActive(true);
            _hint.SetText(text);
        }

        private void EnsureCanvas()
        {
            if (_canvas != null)
                return;
            var go = new GameObject("RescueBasketHint", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 300;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            var back = new GameObject("Back", typeof(RectTransform)).AddComponent<Image>();
            back.transform.SetParent(go.transform, false);
            Place(back.rectTransform, new Vector2(900f, 56f));
            back.color = new Color(0.03f, 0.05f, 0.1f, 0.62f);
            back.raycastTarget = false;
            _hint = new GameObject("Hint", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            _hint.transform.SetParent(go.transform, false);
            Place(_hint.rectTransform, new Vector2(880f, 48f));
            if (_font != null)
                _hint.font = _font;
            _hint.fontSize = 26f;
            _hint.enableAutoSizing = true;
            _hint.fontSizeMin = 15f;
            _hint.fontSizeMax = 26f;
            _hint.color = new Color(0.92f, 0.96f, 1f);
            _hint.alignment = TextAlignmentOptions.Center;
            _hint.raycastTarget = false;
        }

        private static void Place(RectTransform rt, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, 150f);
            rt.sizeDelta = size;
        }
    }
}
