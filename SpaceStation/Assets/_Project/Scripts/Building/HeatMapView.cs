using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Building
{
    /// <summary>
    /// 12-3c 대륙급 열 관리 표시 (그레이박스).
    /// - 열 지도(J): 열이 있는 모듈을 반투명 상자로 (노랑 → 주황 → 빨강, 위험은 깜빡임) + 열 숫자, 방열판은 동작 중 하늘색 · 묻혀서 못 쓰면 회색
    /// - 지도를 끈 상태에서도 과열 모듈은 빨간 상자로 늘 보임
    /// - 화면 왼쪽 아래 한 줄: 과열 · 위험 수와 키 안내 (대륙급부터)
    /// </summary>
    public sealed class HeatMapView : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private StationController _station;
        [Tooltip("반투명 재질 (M_DefenseRange)")]
        [SerializeField] private Material _material;
        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private Camera _camera;

        private sealed class Box
        {
            public MeshRenderer Renderer;
            public TextMeshPro Label;
        }

        private HeatSystem _heat;
        private MaterialPropertyBlock _block;
        private readonly List<Box> _boxes = new List<Box>();
        private int _used;
        private bool _mapOn;
        private bool _dirty = true;
        private TextMeshProUGUI _hud;
        private Mesh _cube;

        private void Start()
        {
            _heat = _station != null && _station.Simulation != null ? _station.Simulation.Heat : null;
            if (_heat == null || _material == null)
            {
                enabled = false;
                return;
            }
            if (_camera == null)
                _camera = Camera.main;
            _block = new MaterialPropertyBlock();
            _cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            _heat.Changed += MarkDirty;
            BuildHud();
        }

        private void OnDestroy()
        {
            if (_heat != null)
                _heat.Changed -= MarkDirty;
        }

        private void MarkDirty() => _dirty = true;

        private void BuildHud()
        {
            var canvasGo = new GameObject("HeatHud", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5;
            var scaler = canvasGo.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            var go = new GameObject("HeatLine", typeof(RectTransform));
            go.transform.SetParent(canvasGo.transform, false);
            _hud = go.AddComponent<TextMeshProUGUI>();
            _hud.font = _font;
            _hud.fontSize = 20f;
            _hud.raycastTarget = false;
            _hud.alignment = TextAlignmentOptions.BottomLeft;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(24f, 300f); // 아래 조작 안내 줄과 겹치지 않게
            rt.sizeDelta = new Vector2(700f, 60f);
        }

        private void Update()
        {
            if (!InputGate.Blocked && KeyBindings.WasPressed(GameAction.HeatMap) && _heat.Active)
            {
                _mapOn = !_mapOn;
                _dirty = true;
            }
        }

        private void LateUpdate()
        {
            bool active = _heat.Active && !_station.Simulation.Session.IsGameOver;
            if (!active)
            {
                if (_used > 0 || !string.IsNullOrEmpty(_hud.text))
                {
                    HideFrom(0);
                    _hud.SetText("");
                }
                _mapOn = false;
                return;
            }
            if (_dirty)
            {
                _dirty = false;
                Rebuild();
            }
            float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4f));
            for (int i = 0; i < _used; i++)
            {
                var b = _boxes[i];
                if (b.Label.gameObject.activeSelf && _camera != null)
                    b.Label.transform.rotation = Quaternion.LookRotation(b.Label.transform.position - _camera.transform.position, _camera.transform.up);
            }
            _pulse = pulse;
            ApplyPulse();
        }

        private float _pulse;
        private readonly List<(Box box, Color color, bool blink)> _colors = new List<(Box, Color, bool)>();

        private void ApplyPulse()
        {
            foreach (var (box, color, blink) in _colors)
            {
                var c = color;
                if (blink)
                    c.a *= _pulse;
                _block.SetColor(BaseColorId, c);
                box.Renderer.SetPropertyBlock(_block);
            }
        }

        private void Rebuild()
        {
            _used = 0;
            _colors.Clear();
            int over = 0, danger = 0;
            float limit = _heat.Limit;
            foreach (var kv in _heat.HeatMap)
            {
                var m = kv.Key;
                float h = kv.Value;
                bool isOver = h > limit + 1e-4f;
                bool isDanger = _heat.IsDanger(m);
                if (isOver) over++;
                if (isDanger) danger++;
                // 지도: 한도 절반 이상만 (낮은 열까지 칠하면 정거장 전체가 물들어 읽히지 않았음). 숫자는 과열 모듈만
                if (!isOver && (!_mapOn || h < limit * 0.5f))
                    continue;
                Color c;
                if (isDanger) c = new Color(1f, 0.08f, 0.05f, 0.5f);
                else if (isOver) c = new Color(1f, 0.25f, 0.1f, 0.38f);
                else if (h > limit * 0.75f) c = new Color(1f, 0.55f, 0.15f, 0.24f);
                else c = new Color(1f, 0.9f, 0.3f, 0.14f);
                Place(m, c, isOver, isOver ? $"{h:0.0}" : null);
            }
            if (_mapOn)
            {
                foreach (var m in _station.Grid.Modules)
                {
                    if (m.Data == null || m.Data.HeatRemoval <= 0f)
                        continue;
                    bool working = _heat.RadiatorWorking(m);
                    Place(m, working ? new Color(0.3f, 0.85f, 1f, 0.3f) : new Color(0.6f, 0.6f, 0.65f, 0.3f), false,
                        working ? $"-{m.Data.HeatRemoval:0}" : DamageSystem.IsExterior(_station.Grid, m) ? "정지" : "묻힘");
                }
            }
            HideFrom(_used);
            string key = KeyBindings.Label(GameAction.HeatMap);
            _hud.SetText($"<color=#FFB070>열 관리</color>  과열 {over} · 위험 {danger}  <color=#C8D2DE>(한도 {limit:0} · 위험 {_heat.DangerLevel:0.#} · {key}: 열 지도 {(_mapOn ? "끄기" : "보기")})</color>");
        }

        private void Place(ModuleInstance m, Color color, bool blink, string label)
        {
            if (_used >= _boxes.Count)
                _boxes.Add(CreateBox());
            var b = _boxes[_used++];
            var min = m.Cells[0];
            var max = m.Cells[0];
            foreach (var c in m.Cells)
            {
                min = Vector3Int.Min(min, c);
                max = Vector3Int.Max(max, c);
            }
            var t = b.Renderer.transform;
            t.position = (GridConfig.CellToWorld(min) + GridConfig.CellToWorld(max)) * 0.5f;
            t.localScale = ((Vector3)(max - min) + Vector3.one * 1.04f) * GridConfig.CellSize;
            if (!b.Renderer.gameObject.activeSelf)
                b.Renderer.gameObject.SetActive(true);
            _colors.Add((b, color, blink));
            bool showLabel = label != null;
            if (b.Label.gameObject.activeSelf != showLabel)
                b.Label.gameObject.SetActive(showLabel);
            if (showLabel)
            {
                b.Label.transform.position = t.position + Vector3.up * (t.localScale.y * 0.5f + 0.25f);
                b.Label.SetText(label);
            }
        }

        private Box CreateBox()
        {
            var go = new GameObject("HeatBox");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = _cube;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            var lgo = new GameObject("HeatLabel");
            lgo.transform.SetParent(transform, false);
            var tmp = lgo.AddComponent<TextMeshPro>();
            tmp.font = _font;
            tmp.fontSize = 2f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.outlineWidth = 0.25f;
            tmp.outlineColor = new Color32(0, 0, 0, 255);
            tmp.rectTransform.sizeDelta = new Vector2(4f, 1f);
            return new Box { Renderer = r, Label = tmp };
        }

        private void HideFrom(int index)
        {
            for (int i = index; i < _boxes.Count; i++)
            {
                if (_boxes[i].Renderer.gameObject.activeSelf)
                    _boxes[i].Renderer.gameObject.SetActive(false);
                if (_boxes[i].Label.gameObject.activeSelf)
                    _boxes[i].Label.gameObject.SetActive(false);
            }
            if (index == 0)
                _used = 0;
        }
    }
}
