using System.Collections.Generic;
using SpaceStation.Audio;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// Phase 6 조기 경보 (방어 연구 Lv.1~2).
    /// - 상단 배너: "운석 접근 · 0:12" / "태양 폭풍 접근 · 0:12" + 경보음, 남은 시간이 줄어들고 테두리가 깜빡임
    /// - 방어 Lv.2: 맞을 예정인 모듈 위에 "운석 예상" 표시 (예정 대상이 철거되면 다시 뽑힌 곳으로 옮겨감)
    /// 내용은 코드로 만든다 (씬에는 글꼴·스프라이트만 연결된 빈 오브젝트).
    /// </summary>
    public sealed class EarlyWarningView : MonoBehaviour
    {
        [SerializeField] private StationController _station;
        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private Sprite _fillSprite;
        [SerializeField] private Sprite _frameSprite;
        [SerializeField] private Camera _camera;
        [SerializeField] private float _markerHeight = 1.35f;

        private sealed class Marker
        {
            public RectTransform Rect;
            public ModuleInstance Module;
            public Vector3 World;
        }

        private HoloUi _ui;
        private StationSimulation _sim;
        private RectTransform _banner;
        private CanvasGroup _group;
        private Image _frame;
        private TMP_Text _title;
        private TMP_Text _detail;
        private UiTween _tween;
        private int _shownSeconds = -1;
        private readonly List<Marker> _markers = new List<Marker>();
        private readonly Stack<RectTransform> _pool = new Stack<RectTransform>();

        private void Start()
        {
            _sim = _station != null ? _station.Simulation : null;
            if (_sim == null)
            {
                enabled = false;
                return;
            }
            if (_camera == null)
                _camera = Camera.main;
            _ui = new HoloUi(_font, _fillSprite, _frameSprite, null);
            BuildBanner();
            _sim.Events.UpcomingChanged += HandleUpcomingChanged;
            _sim.MeteorPlanChanged += RebuildMarkers;
            if (_sim.Events.Upcoming != null)
                HandleUpcomingChanged(_sim.Events.Upcoming); // 불러온 판
            RebuildMarkers();
        }

        private void OnDestroy()
        {
            if (_sim == null)
                return;
            _sim.Events.UpcomingChanged -= HandleUpcomingChanged;
            _sim.MeteorPlanChanged -= RebuildMarkers;
        }

        private void BuildBanner()
        {
            _banner = HoloUi.Rect("WarningBanner", transform);
            _banner.anchorMin = _banner.anchorMax = _banner.pivot = new Vector2(0.5f, 1f);
            _banner.anchoredPosition = new Vector2(0f, -150f);
            _banner.sizeDelta = new Vector2(560f, 74f);
            _ui.Panel(_banner.gameObject, new Color(0.16f, 0.05f, 0.03f, 0.9f), HudTheme.Negative);
            _banner.GetComponent<Image>().raycastTarget = false;
            _frame = _banner.Find("Frame").GetComponent<Image>();
            _group = _banner.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _group.alpha = 0f;
            _title = _ui.Label(_banner, "", 26f, TextAlignmentOptions.Center);
            HoloUi.Place(_title.rectTransform, new Vector2(0f, -6f), new Vector2(560f, 36f));
            _detail = _ui.Label(_banner, "", 15f, TextAlignmentOptions.Center);
            HoloUi.Place(_detail.rectTransform, new Vector2(0f, -42f), new Vector2(560f, 24f));
            _tween = new UiTween(_banner, _group, new Vector2(0f, 24f), 0.2f, 0.25f);
        }

        private void HandleUpcomingChanged(GameEventData upcoming)
        {
            _shownSeconds = -1;
            if (upcoming == null)
            {
                _tween.Hide();
                return;
            }
            _tween.Play(restart: !_tween.Visible);
            AudioService.TryPlay(l => l.EarlyWarning);
            Refresh();
        }

        private void Update()
        {
            if (_tween == null)
                return;
            _tween.Update();
            if (_sim.Session.IsGameOver)
            {
                if (_tween.Visible)
                    _tween.Hide();
                if (_markers.Count > 0)
                    RebuildMarkersEmpty();
                return;
            }
            if (_sim.Events.Upcoming != null)
            {
                Refresh();
                float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3.2f));
                var c = HudTheme.Negative;
                _frame.color = new Color(c.r, c.g, c.b, pulse);
            }
        }

        private void LateUpdate()
        {
            if (_camera == null)
                return;
            foreach (var m in _markers)
            {
                Vector3 screen = _camera.WorldToScreenPoint(m.World);
                bool on = screen.z > 0f;
                if (m.Rect.gameObject.activeSelf != on)
                    m.Rect.gameObject.SetActive(on);
                if (on)
                    m.Rect.position = new Vector3(screen.x, screen.y, 0f);
            }
        }

        private void Refresh()
        {
            var upcoming = _sim.Events.Upcoming;
            int seconds = Mathf.Max(0, Mathf.CeilToInt(_sim.Events.TimeUntilNext));
            if (upcoming == null || seconds == _shownSeconds)
                return;
            _shownSeconds = seconds;
            bool meteor = upcoming is MeteorEventData;
            string name = meteor ? "운석 접근" : "태양 폭풍 접근";
            _title.SetText($"{HudTheme.Icon("warning")} <b>{name}</b>  <color={HudText.Orange}>{seconds / 60}:{seconds % 60:00}</color>");
            string detail;
            if (meteor)
                detail = _sim.PlannedMeteorTargets.Count > 0
                    ? $"운석 {_sim.PlannedMeteorTargets.Count}개 · 맞을 모듈을 표시했습니다 (실드·포탑으로 대비)"
                    : "실드·포탑을 점검하고 파손에 대비하세요";
            else
                detail = upcoming is SolarStormEventData storm
                    ? $"전력 생산 -{(1f - storm.PowerSupplyMultiplier) * 100f:0}% 예상 · 배터리를 채워 두세요"
                    : "";
            _detail.SetText($"<color=#E8C8C0>{detail}</color>");
        }

        // ---------------- 대상 표시 ----------------

        private void RebuildMarkersEmpty()
        {
            foreach (var m in _markers)
            {
                m.Rect.gameObject.SetActive(false);
                _pool.Push(m.Rect);
            }
            _markers.Clear();
        }

        private void RebuildMarkers()
        {
            RebuildMarkersEmpty();
            if (_sim.Session.IsGameOver)
                return;
            foreach (var module in _sim.PlannedMeteorTargets)
            {
                var rect = _pool.Count > 0 ? _pool.Pop() : CreateMarker();
                rect.gameObject.SetActive(true);
                _markers.Add(new Marker { Rect = rect, Module = module, World = Anchor(module) });
            }
            _shownSeconds = -1; // 개수 문구 갱신
        }

        private RectTransform CreateMarker()
        {
            var rt = HoloUi.Rect("MeteorTarget", transform);
            rt.sizeDelta = new Vector2(130f, 30f);
            rt.pivot = new Vector2(0.5f, 0f);
            _ui.Panel(rt.gameObject, new Color(0.22f, 0.05f, 0.02f, 0.85f), HudTheme.Negative);
            rt.GetComponent<Image>().raycastTarget = false;
            var label = _ui.Label(rt, $"{HudTheme.Icon("warning")} <color={HudText.Orange}><b>운석 예상</b></color>", 15f, TextAlignmentOptions.Center);
            HoloUi.Stretch(label.rectTransform);
            return rt;
        }

        private Vector3 Anchor(ModuleInstance module)
        {
            Vector3 sum = Vector3.zero;
            foreach (var cell in module.Cells)
                sum += GridConfig.CellToWorld(cell);
            return sum / module.Cells.Count + Vector3.up * (_markerHeight * GridConfig.CellSize);
        }
    }
}
