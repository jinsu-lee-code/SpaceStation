using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// 파손 모듈 위에 경고 아이콘 + 남은 시간(파괴까지 / 수리 완료까지)을 띄운다 (GDD 12-2).
    /// 12-0 (U-5) 가시성: 모듈이 많아지면 라벨이 겹치고 어느 모듈 것인지 헷갈렸음 →
    /// - 핀: 라벨을 모듈 윗면보다 <see cref="_liftPixels"/>만큼 위로 띄우고, 모듈까지 짧은 연결선
    /// - 겹치면 비켜 쌓기: 화면에서 라벨끼리 겹치면 위로 밀어 올림 (연결선이 늘어나 원래 모듈을 계속 가리킴)
    /// - 화면 밖 파손: 화면 가장자리에 방향 화살표 + 남은 시간
    /// - 어두운 배경 + 큰 글자 (붉은 배경에 붉은 글자라 대비가 낮았음)
    /// 화면 좌표 추적은 파손 모듈 수만큼만 매 프레임 계산한다. 라벨은 템플릿을 복제해 풀링한다.
    /// </summary>
    public sealed class DamageMarkers : MonoBehaviour
    {
        private sealed class Marker
        {
            public RectTransform Rect;
            public TMP_Text Text;
            public Image Background;
            public Image Edge;
            public RectTransform Line;
            public Vector3 World;
            public int ShownKey = int.MinValue;
            public bool Seen;
            // 이번 프레임 배치
            public bool OnScreen;
            public Vector2 Anchor;   // 모듈 윗면 (화면 픽셀)
            public Vector2 Position; // 라벨 아래 가운데 (화면 픽셀)
            public Vector2 Size;     // 라벨 크기 (화면 픽셀)
            public string Arrow = "";
        }

        [SerializeField] private ResourceController _resources;
        private const int SpreadWarnSeconds = 15; // 이 이하면 확산 경고 강조
        [SerializeField] private Camera _camera;
        [Tooltip("비활성 상태의 라벨 템플릿 (자식에 TMP_Text)")]
        [SerializeField] private RectTransform _template;
        [SerializeField] private RectTransform _container;
        [Tooltip("모듈 윗면 기준 높이 (칸 크기 배수)")]
        [SerializeField] private float _topOffset = 0.5f; // 12-0: 옛 _heightOffset(칸 중심 기준 0.9) 대신 윗면 기준

        [Header("12-0 가시성")]
        [Tooltip("라벨을 모듈 윗면보다 띄우는 높이 (기준 해상도 픽셀)")]
        [SerializeField] private float _liftPixels = 44f;
        [SerializeField] private float _fontSize = 20f;
        [SerializeField] private Color _background = new Color(0.05f, 0.06f, 0.08f, 0.92f);
        [SerializeField] private Color _lineColor = new Color(1f, 0.35f, 0.25f, 0.85f);
        [SerializeField] private float _lineWidth = 2f;
        [Tooltip("라벨끼리 띄우는 간격 (기준 해상도 픽셀)")]
        [SerializeField] private float _gap = 4f;
        [Tooltip("화면 밖 표시의 가장자리 여백 (기준 해상도 픽셀)")]
        [SerializeField] private float _edgeMargin = 24f;

        private readonly Dictionary<ModuleInstance, Marker> _markers = new Dictionary<ModuleInstance, Marker>();
        private readonly Stack<Marker> _pool = new Stack<Marker>();
        private readonly List<ModuleInstance> _toRelease = new List<ModuleInstance>();
        private readonly List<Marker> _placed = new List<Marker>();
        private readonly List<Marker> _visible = new List<Marker>();
        private Canvas _canvas;

        private float Scale => _canvas != null ? _canvas.scaleFactor : 1f;

        private void Awake()
        {
            if (_camera == null)
                _camera = Camera.main;
            if (_container == null)
                _container = (RectTransform)transform;
            _canvas = GetComponentInParent<Canvas>();
            _template.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            var damage = _resources.Damage;
            if (damage == null)
                return;

            foreach (var m in _markers.Values)
                m.Seen = false;

            _visible.Clear();
            foreach (var info in damage.DamagedModules)
            {
                if (!_markers.TryGetValue(info.Module, out var marker))
                {
                    marker = Acquire();
                    marker.World = WorldAnchor(info.Module);
                    _markers.Add(info.Module, marker);
                }
                marker.Seen = true;
                Project(marker);
                UpdateText(marker, info);
                _visible.Add(marker);
            }
            Layout();

            _toRelease.Clear();
            foreach (var pair in _markers)
            {
                if (!pair.Value.Seen)
                    _toRelease.Add(pair.Key);
            }
            foreach (var module in _toRelease)
            {
                var marker = _markers[module];
                _markers.Remove(module);
                marker.Rect.gameObject.SetActive(false);
                marker.Line.gameObject.SetActive(false);
                marker.ShownKey = int.MinValue;
                _pool.Push(marker);
            }
        }

        /// <summary>모듈 윗면을 화면에 투영. 화면 밖(뒤쪽 포함)이면 가장자리로 붙이고 방향 화살표.</summary>
        private void Project(Marker marker)
        {
            Vector3 screen = _camera.WorldToScreenPoint(marker.World);
            float w = Screen.width, h = Screen.height;
            bool behind = screen.z < 0f;
            marker.OnScreen = !behind && screen.x >= 0f && screen.x <= w && screen.y >= 0f && screen.y <= h;
            if (marker.OnScreen)
            {
                marker.Anchor = screen;
                marker.Arrow = "";
                return;
            }
            // 화면 가운데에서 대상 쪽으로 (뒤쪽이면 뒤집어서) 가장자리까지
            var center = new Vector2(w * 0.5f, h * 0.5f);
            var dir = (Vector2)screen - center;
            if (behind)
                dir = -dir;
            if (dir.sqrMagnitude < 1e-4f)
                dir = Vector2.down;
            float margin = _edgeMargin * Scale;
            float halfW = w * 0.5f - margin, halfH = h * 0.5f - margin;
            float t = Mathf.Min(Mathf.Abs(dir.x) > 1e-4f ? halfW / Mathf.Abs(dir.x) : float.MaxValue,
                                Mathf.Abs(dir.y) > 1e-4f ? halfH / Mathf.Abs(dir.y) : float.MaxValue);
            marker.Anchor = center + dir * t;
            marker.Arrow = Mathf.Abs(dir.x) * h > Mathf.Abs(dir.y) * w
                ? (dir.x > 0f ? "→ " : "← ")
                : (dir.y > 0f ? "↑ " : "↓ ");
        }

        /// <summary>라벨 위치를 정하고 겹치면 위로 비켜 쌓는다. 연결선은 모듈 윗면 → 라벨 아래.</summary>
        private void Layout()
        {
            float lift = _liftPixels * Scale;
            float gap = _gap * Scale;
            float w = Screen.width, h = Screen.height;
            foreach (var m in _visible)
            {
                m.Size = m.Rect.sizeDelta * Scale;
                m.Position = m.OnScreen ? m.Anchor + Vector2.up * lift : EdgePosition(m, w, h);
            }
            // 아래쪽 라벨부터 놓고, 이미 놓인 라벨과 겹치면 그 위로 (화면 밖 표시는 가장자리에 그대로)
            _visible.Sort((a, b) => a.Position.y.CompareTo(b.Position.y));
            _placed.Clear();
            foreach (var m in _visible)
            {
                if (m.OnScreen)
                {
                    bool moved = true;
                    for (int guard = 0; moved && guard < 32; guard++)
                    {
                        moved = false;
                        foreach (var other in _placed)
                        {
                            if (!Overlaps(m, other, gap))
                                continue;
                            m.Position.y = other.Position.y + other.Size.y + gap;
                            moved = true;
                        }
                    }
                }
                _placed.Add(m);
                Apply(m);
            }
        }

        /// <summary>화면 밖 표시: 라벨이 화면 안에 들어오도록 가장자리 점에서 안쪽으로.</summary>
        private static Vector2 EdgePosition(Marker m, float w, float h)
        {
            var p = m.Anchor - new Vector2(0f, m.Size.y * 0.5f);
            p.x = Mathf.Clamp(p.x, m.Size.x * 0.5f, w - m.Size.x * 0.5f);
            p.y = Mathf.Clamp(p.y, 0f, h - m.Size.y);
            return p;
        }

        private static bool Overlaps(Marker a, Marker b, float gap)
        {
            return Mathf.Abs(a.Position.x - b.Position.x) < (a.Size.x + b.Size.x) * 0.5f + gap
                   && a.Position.y < b.Position.y + b.Size.y + gap
                   && b.Position.y < a.Position.y + a.Size.y + gap;
        }

        private void Apply(Marker m)
        {
            if (!m.Rect.gameObject.activeSelf)
                m.Rect.gameObject.SetActive(true);
            m.Rect.position = new Vector3(m.Position.x, m.Position.y, 0f);

            bool line = m.OnScreen;
            if (m.Line.gameObject.activeSelf != line)
                m.Line.gameObject.SetActive(line);
            if (!line)
                return;
            var delta = m.Position - m.Anchor;
            float length = delta.magnitude;
            m.Line.position = new Vector3(m.Anchor.x, m.Anchor.y, 0f);
            m.Line.sizeDelta = new Vector2(_lineWidth, length / Scale);
            m.Line.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg);
        }

        private void UpdateText(Marker marker, DamageInfo info)
        {
            // 초 단위가 바뀔 때만 문자열 갱신 (수리 중은 음수 키로 구분)
            // 8-5 장갑: 파괴 시간 없음 → 0초로 두고 아래에서 "장갑" 표시
            int seconds = info.IsRepairing ? Mathf.CeilToInt(info.RepairRemaining) : info.NeverDestroyed ? 0 : Mathf.CeilToInt(info.TimeUntilDestroyed);
            int position = info.IsQueued ? _resources.Damage.GetQueuePosition(info.Module) : 0; // 4-6 대기 순번
            int spread = info.SpreadPending ? Mathf.CeilToInt(info.TimeUntilSpread) : 0; // 4-7 확산까지
            int key = info.IsRepairing ? -1 - seconds : seconds + position * 100000 + spread * 1000;
            key = key * 8 + ArrowCode(marker.Arrow);
            if (key == marker.ShownKey)
                return;
            marker.ShownKey = key;
            string spreadText = spread <= 0 ? ""
                : spread <= SpreadWarnSeconds ? $"\n<color={HudText.Orange}><b>확산 {spread}초</b></color>"
                : $"\n<size=85%><color={HudText.Muted}>확산 {spread}초</color></size>";
            string arrow = marker.Arrow.Length > 0 ? $"<color={HudText.Red}>{marker.Arrow}</color>" : "";
            marker.Text.SetText(arrow + (info.IsRepairing
                ? $"<color=#7FD8FF>수리 {seconds}초</color>"
                : info.NeverDestroyed
                    ? (position > 0 ? $"<color={HudText.Yellow}>대기 {position}</color> " : "") + $"<color={HudText.Muted}>장갑 파손</color>"
                : position > 0
                    ? $"<color={HudText.Yellow}>대기 {position}</color> <color={HudText.Red}>{seconds}초</color>{spreadText}"
                    : $"<color={HudText.Red}><b>!</b> {seconds}초</color>{spreadText}"));
            // 글자에 맞춰 라벨 크기 (두 줄이면 높아짐)
            var preferred = marker.Text.GetPreferredValues();
            marker.Rect.sizeDelta = new Vector2(Mathf.Max(64f, preferred.x + 20f), preferred.y + 10f);
            // 수리 중은 하늘색, 그 외는 붉은 연결선 · 강조 띠
            var accent = info.IsRepairing ? new Color(0.5f, 0.85f, 1f, 0.85f) : _lineColor;
            marker.Line.GetComponent<Image>().color = accent;
            if (marker.Edge != null)
                marker.Edge.color = accent;
        }

        private static int ArrowCode(string arrow)
        {
            switch (arrow)
            {
                case "→ ": return 1;
                case "← ": return 2;
                case "↑ ": return 3;
                case "↓ ": return 4;
                default: return 0;
            }
        }

        private Marker Acquire()
        {
            if (_pool.Count > 0)
            {
                var pooled = _pool.Pop();
                pooled.Rect.gameObject.SetActive(true);
                return pooled;
            }
            // 연결선이 라벨 뒤에 그려지도록 먼저 만든다
            var lineGo = new GameObject("DamageLine", typeof(RectTransform), typeof(Image));
            var line = (RectTransform)lineGo.transform;
            line.SetParent(_container, false);
            line.anchorMin = line.anchorMax = Vector2.zero;
            line.pivot = new Vector2(0.5f, 0f);
            var lineImage = lineGo.GetComponent<Image>();
            lineImage.color = _lineColor;
            lineImage.raycastTarget = false;
            lineGo.SetActive(false);

            var rect = Instantiate(_template, _container);
            rect.gameObject.SetActive(true);
            var marker = new Marker
            {
                Rect = rect,
                Text = rect.GetComponentInChildren<TMP_Text>(true),
                Background = rect.GetComponent<Image>(),
                Line = line,
            };
            if (marker.Background != null)
            {
                // 홀로 버튼 스프라이트는 가운데가 비어 밝은 모듈이 비쳐 보였음 → 꽉 찬 사각형
                marker.Background.sprite = null;
                marker.Background.type = Image.Type.Simple;
                marker.Background.color = _background;
                marker.Background.raycastTarget = false;
                // 왼쪽 강조 띠 (연결선과 같은 색)
                var edgeGo = new GameObject("Edge", typeof(RectTransform), typeof(Image));
                var edge = (RectTransform)edgeGo.transform;
                edge.SetParent(rect, false);
                edge.SetAsFirstSibling();
                edge.anchorMin = Vector2.zero;
                edge.anchorMax = new Vector2(0f, 1f);
                edge.pivot = new Vector2(0f, 0.5f);
                edge.sizeDelta = new Vector2(3f, 0f);
                edge.anchoredPosition = Vector2.zero;
                marker.Edge = edgeGo.GetComponent<Image>();
                marker.Edge.color = _lineColor;
                marker.Edge.raycastTarget = false;
            }
            marker.Text.fontSize = _fontSize;
            marker.Text.raycastTarget = false;
            return marker;
        }

        /// <summary>모듈 칸들의 가운데, 가장 높은 칸 윗면 근처 (월드).</summary>
        private Vector3 WorldAnchor(ModuleInstance module)
        {
            Vector3 sum = Vector3.zero;
            float top = float.MinValue;
            foreach (var cell in module.Cells)
            {
                var c = GridConfig.CellToWorld(cell);
                sum += c;
                top = Mathf.Max(top, c.y);
            }
            var center = sum / module.Cells.Count;
            center.y = top + _topOffset * GridConfig.CellSize;
            return center;
        }
    }
}
