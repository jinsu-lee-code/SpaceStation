using System.Collections.Generic;
using System.Text;
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
    /// 7-3: 배치 중 인접 효과를 모듈 위에 아이콘 + 수치로 띄운다.
    /// 고스트 위 = 새 모듈 자신이 받을 효과, 이웃 위 = 새 모듈 때문에 그 이웃에 새로 생길 효과. 좋은 효과 초록, 나쁜 효과 주황.
    /// 미리보기 계산은 배치 대상(모듈·칸·회전·판정)이 바뀔 때만, 화면 위치는 매 프레임(카메라 이동) 갱신한다.
    /// 라벨은 처음 필요할 때 코드로 만들어 풀링한다.
    /// </summary>
    public sealed class AdjacencyPreviewMarkers : MonoBehaviour
    {
        private sealed class Marker
        {
            public RectTransform Rect;
            public TMP_Text Text;
            public Vector3 World;
        }

        private const string GoodHex = "#7CFF9A";

        [SerializeField] private BuildController _build;
        [SerializeField] private StationController _station;
        [SerializeField] private Camera _camera;
        [Tooltip("라벨 바탕 (9-slice, 비우면 단색)")]
        [SerializeField] private Sprite _background;
        [SerializeField] private Color _backgroundColor = new Color(0.03f, 0.07f, 0.11f, 0.82f);
        [SerializeField] private float _fontSize = 18f;
        [Tooltip("모듈 중심에서 라벨까지 높이 (칸 단위)")]
        [SerializeField] private float _heightOffset = 0.75f;

        private readonly List<AppliedAdjacency> _self = new List<AppliedAdjacency>();
        private readonly List<NeighborAdjacencyPreview> _neighbors = new List<NeighborAdjacencyPreview>();
        private readonly Dictionary<ModuleInstance, StringBuilder> _byModule = new Dictionary<ModuleInstance, StringBuilder>();
        private readonly List<Marker> _active = new List<Marker>();
        private readonly Stack<Marker> _pool = new Stack<Marker>();

        private ModuleData _shownBuild;
        private Vector3Int _shownCell;
        private int _shownRotation;
        private bool _shownValid;
        private int _shownModuleCount = -1;

        private void Awake()
        {
            if (_camera == null)
                _camera = Camera.main;
        }

        private void LateUpdate()
        {
            if (_build == null || _station == null || _station.Simulation == null)
                return;
            var data = _build.Selected;
            bool valid = data != null && _build.HasTarget && _build.TargetResult == PlacementResult.Valid;
            var cell = valid ? _build.TargetCell : Vector3Int.zero;
            int rotation = valid ? _build.Rotation : 0;
            int moduleCount = _station.Grid.ModuleCount; // 배치·철거로 이웃이 바뀌면 다시 계산
            if (data != _shownBuild || valid != _shownValid || cell != _shownCell || rotation != _shownRotation || moduleCount != _shownModuleCount)
            {
                _shownBuild = data;
                _shownValid = valid;
                _shownCell = cell;
                _shownRotation = rotation;
                _shownModuleCount = moduleCount;
                Rebuild(valid ? data : null, cell, rotation);
            }
            foreach (var marker in _active)
                Place(marker);
        }

        private void Rebuild(ModuleData data, Vector3Int cell, int rotation)
        {
            foreach (var marker in _active)
            {
                marker.Rect.gameObject.SetActive(false);
                _pool.Push(marker);
            }
            _active.Clear();
            if (data == null)
                return;

            _station.Simulation.PreviewAdjacency(data, cell, rotation, _self, _neighbors);

            if (_self.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (var a in _self)
                    Append(sb, a.Rule, a.Total);
                Show(sb.ToString(), Anchor(StationGrid.ResolveCells(data.CellOffsets, cell, rotation)));
            }

            foreach (var n in _neighbors)
            {
                if (!_byModule.TryGetValue(n.Module, out var sb))
                    _byModule[n.Module] = sb = new StringBuilder();
                Append(sb, n.Rule, n.Total);
            }
            foreach (var pair in _byModule)
            {
                if (pair.Value.Length > 0)
                    Show(pair.Value.ToString(), Anchor(pair.Key.Cells));
            }
            _byModule.Clear(); // 철거된 모듈을 붙잡지 않게 매번 비움 (이웃은 최대 수십 개라 다시 만드는 비용이 작음)
        }

        private static void Append(StringBuilder sb, AdjacencyRule rule, float total)
        {
            if (sb.Length > 0)
                sb.Append("  ");
            var resource = AdjacencySystem.EffectResource(rule);
            sb.Append(resource.HasValue ? HudTheme.Icon(resource.Value) : HudTheme.Icon("population"));
            sb.Append(" <color=").Append(AdjacencySystem.IsBeneficial(rule, total) ? GoodHex : HudText.Orange).Append('>')
              .Append(AdjacencySystem.ShortValue(rule, total)).Append("</color>");
        }

        private void Show(string text, Vector3 world)
        {
            var marker = _pool.Count > 0 ? _pool.Pop() : CreateMarker();
            marker.Text.SetText(text);
            marker.World = world;
            marker.Rect.gameObject.SetActive(true);
            _active.Add(marker);
            Place(marker);
        }

        private void Place(Marker marker)
        {
            Vector3 screen = _camera.WorldToScreenPoint(marker.World);
            bool onScreen = screen.z > 0f; // 카메라 뒤면 숨김
            if (marker.Rect.gameObject.activeSelf != onScreen)
                marker.Rect.gameObject.SetActive(onScreen);
            if (onScreen)
                marker.Rect.position = new Vector3(screen.x, screen.y, 0f);
        }

        private Vector3 Anchor(IReadOnlyList<Vector3Int> cells)
        {
            Vector3 sum = Vector3.zero;
            foreach (var c in cells)
                sum += GridConfig.CellToWorld(c);
            return sum / Mathf.Max(1, cells.Count) + Vector3.up * (_heightOffset * GridConfig.CellSize);
        }

        private Marker CreateMarker()
        {
            var go = new GameObject("AdjacencyMarker", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(transform, false);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0f);
            var image = go.AddComponent<Image>();
            image.sprite = _background;
            image.type = _background != null ? Image.Type.Sliced : Image.Type.Simple;
            image.color = _backgroundColor;
            image.raycastTarget = false;
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 4, 4);
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var fitter = go.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(rect, false);
            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.fontSize = _fontSize;
            text.color = new Color(0.91f, 0.96f, 1f, 1f);
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            return new Marker { Rect = rect, Text = text };
        }
    }
}
