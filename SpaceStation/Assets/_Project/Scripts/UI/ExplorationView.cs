using System.Collections.Generic;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.UI
{
    /// <summary>
    /// 13-5a 탐사 지점 표시 (REDESIGN V5: 지도 화면 대신 정거장 시점의 방향 마커). 지점마다
    /// '닿는 범위' 반투명 상자(이 안에 모듈이 들어가면 닿음) + 빛기둥 + 화면 라벨(신호 힌트 / 종류 · 지금 거리).
    /// 색 = 신호(하늘 · 깜박임) · 탐사선 비행(노랑) · 밝힘(종류색) · 닿음(초록, 옅게). 1인칭 내부에서는 숨김. 내용은 코드로 만든다.
    /// </summary>
    public sealed class ExplorationView : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private const float EdgeWidth = 0.06f;

        [SerializeField] private StationController _station;
        [Tooltip("반투명 재질 (M_DefenseRange)")]
        [SerializeField] private Material _material;
        [SerializeField] private TMP_FontAsset _font;
        [Tooltip("라벨을 올릴 화면 (HUD)")]
        [SerializeField] private RectTransform _labelRoot;
        [SerializeField] private float _beamHeight = 7f;

        private ExplorationSystem _explore;
        private Interior.InteriorMode _interior;
        private Camera _camera;
        private MaterialPropertyBlock _block;
        private RectTransform _labels;
        private readonly List<Marker> _markers = new List<Marker>();
        private float _nextText;

        private sealed class Marker
        {
            public ExploreSite Site;
            public GameObject Root;
            public readonly List<MeshRenderer> Edges = new List<MeshRenderer>(12);
            public MeshRenderer Beam, Core;
            public TMP_Text Label;
        }

        private void Start()
        {
            var sim = _station != null ? _station.Simulation : null;
            _explore = sim?.GetSystem<ExplorationSystem>();
            if (_explore == null || _material == null)
            {
                enabled = false;
                return;
            }
            _block = new MaterialPropertyBlock();
            _camera = Camera.main;
            // 라벨 층은 HUD 맨 아래 (창 · 카드가 라벨을 덮게)
            _labels = new GameObject("ExploreLabels", typeof(RectTransform)).GetComponent<RectTransform>();
            _labels.SetParent(_labelRoot != null ? _labelRoot : transform, false);
            _labels.anchorMin = Vector2.zero;
            _labels.anchorMax = Vector2.one;
            _labels.offsetMin = _labels.offsetMax = Vector2.zero;
            _labels.SetAsFirstSibling();
            _interior = FindFirstObjectByType<Interior.InteriorMode>(FindObjectsInactive.Include);
            _explore.Changed += Rebuild;
            Rebuild();
        }

        private void OnDestroy()
        {
            if (_explore != null)
                _explore.Changed -= Rebuild;
        }

        private void Rebuild()
        {
            var sites = _explore.Sites;
            while (_markers.Count < sites.Count)
                _markers.Add(Create());
            for (int i = 0; i < _markers.Count; i++)
            {
                var m = _markers[i];
                bool show = i < sites.Count;
                m.Root.SetActive(show);
                m.Label.gameObject.SetActive(show);
                if (!show)
                {
                    m.Site = null;
                    continue;
                }
                m.Site = sites[i];
                var center = GridConfig.CellToWorld(m.Site.Cell);
                m.Root.transform.position = center;
                // 닿는 범위 = 모서리 선 12개 (꽉 찬 상자는 무겁고 뒤를 가렸음)
                float half = (_explore.Config.ReachRadius + 0.5f) * GridConfig.CellSize;
                for (int e = 0; e < 12; e++)
                {
                    int axis = e / 4; // 0 = x 방향 선, 1 = y, 2 = z
                    float u = (e & 1) == 0 ? -half : half, v = (e & 2) == 0 ? -half : half;
                    var pos = axis == 0 ? new Vector3(0f, u, v) : axis == 1 ? new Vector3(u, 0f, v) : new Vector3(u, v, 0f);
                    var size = axis == 0 ? new Vector3(half * 2f, EdgeWidth, EdgeWidth) : axis == 1 ? new Vector3(EdgeWidth, half * 2f, EdgeWidth) : new Vector3(EdgeWidth, EdgeWidth, half * 2f);
                    m.Edges[e].transform.localPosition = pos;
                    m.Edges[e].transform.localScale = size;
                }
                m.Beam.transform.localPosition = Vector3.up * (_beamHeight * 0.5f);
                m.Beam.transform.localScale = new Vector3(0.14f, _beamHeight, 0.14f);
                m.Core.transform.localScale = Vector3.one * 0.5f;
            }
            _nextText = 0f;
        }

        private Marker Create()
        {
            var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var root = new GameObject("ExploreSite");
            root.transform.SetParent(transform, false);
            MeshRenderer Part(string name)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = cube;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = _material;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                return r;
            }
            var marker = new Marker { Root = root, Beam = Part("Beam"), Core = Part("Core") };
            for (int e = 0; e < 12; e++)
                marker.Edges.Add(Part("Edge"));
            var label = new GameObject("ExploreLabel", typeof(RectTransform)).GetComponent<RectTransform>();
            label.SetParent(_labels, false);
            label.anchorMin = label.anchorMax = new Vector2(0f, 0f);
            label.pivot = new Vector2(0.5f, 0f);
            label.sizeDelta = new Vector2(320f, 60f);
            var text = label.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = _font;
            text.fontSize = 17f;
            text.alignment = TextAlignmentOptions.Bottom;
            text.raycastTarget = false;
            text.enableWordWrapping = true;
            marker.Label = text;
            return marker;
        }

        private static Color KindColor(SiteKind kind)
        {
            switch (kind)
            {
                case SiteKind.Asteroid: return new Color(1f, 0.62f, 0.25f);
                case SiteKind.IceRock: return new Color(0.55f, 0.85f, 1f);
                case SiteKind.Wreck: return new Color(0.75f, 0.55f, 1f);
                default: return new Color(1f, 0.3f, 0.25f);
            }
        }

        private void LateUpdate()
        {
            if (_explore == null)
                return;
            bool inside = _interior != null && _interior.IsInside;
            if (_camera == null)
                _camera = Camera.main;
            bool refreshText = Time.unscaledTime >= _nextText;
            if (refreshText)
                _nextText = Time.unscaledTime + 0.25f;
            float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2.4f));
            foreach (var m in _markers)
            {
                if (m.Site == null)
                    continue;
                if (m.Root.activeSelf == inside)
                    m.Root.SetActive(!inside);
                var s = m.Site;
                Color c;
                float a;
                if (s.Reached) { c = new Color(0.45f, 1f, 0.6f); a = 0.35f; }
                else if (s.Revealed) { c = KindColor(s.Kind); a = 0.8f; }
                else if (_explore.ProbeTarget == s) { c = new Color(1f, 0.85f, 0.3f); a = pulse; }
                else { c = new Color(0.5f, 0.9f, 1f); a = pulse * 0.8f; }
                foreach (var edge in m.Edges)
                    Tint(edge, c, a * 0.7f);
                Tint(m.Beam, c, a * 0.55f);
                Tint(m.Core, c, a);
                PlaceLabel(m, inside, refreshText);
            }
        }

        private void Tint(Renderer r, Color c, float alpha)
        {
            _block.SetColor(BaseColorId, new Color(c.r, c.g, c.b, alpha));
            r.SetPropertyBlock(_block);
        }

        private void PlaceLabel(Marker m, bool inside, bool refreshText)
        {
            var world = m.Root.transform.position + Vector3.up * (_beamHeight + 0.3f);
            var screen = _camera != null ? _camera.WorldToScreenPoint(world) : Vector3.back;
            bool show = !inside && screen.z > 0f;
            if (m.Label.gameObject.activeSelf != show)
                m.Label.gameObject.SetActive(show);
            if (!show)
                return;
            var parent = (RectTransform)m.Label.rectTransform.parent;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, null, out var local);
            m.Label.rectTransform.anchoredPosition = local + parent.rect.size * parent.pivot; // 왼쪽 아래 기준
            if (!refreshText)
                return;
            var s = m.Site;
            string distance = $"{_explore.Distance(s)}칸";
            m.Label.SetText(s.Reached ? $"<color=#7CFF9A>{ExplorationConfig.Name(s.Kind)} · 탐사 중</color>"
                : s.Revealed ? $"<b>{ExplorationConfig.Name(s.Kind)}</b>\n<size=85%>지금 {distance} · {_explore.Config.ReachRadius}칸 이내 도달 시 탐사 시작</size>"
                : _explore.ProbeTarget == s ? $"<color=#FFD36A>탐사선 비행 중</color>\n<size=85%>{distance}</size>"
                : $"<color=#9FE8FF>신호</color> <size=85%>{ExplorationConfig.Hint(s.Kind)}</size>\n<size=85%>{distance}</size>");
        }
    }
}
