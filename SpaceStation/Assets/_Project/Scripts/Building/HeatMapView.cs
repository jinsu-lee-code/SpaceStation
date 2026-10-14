using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Building
{
    /// <summary>
    /// 12-3c 대륙급 열 관리 표시 (그레이박스).
    /// 색약 · 붉은 태양 폭풍 화면에서도 읽히게 색은 보조로만 (사용자 피드백 2026-10-14): 상태는 화면 고정 크기 라벨("과열 중" · "과열 위험") + 밝기 + 깜빡임.
    /// - 늘: 과열 모듈 노란 상자, 위험 모듈 흰 깜빡임 상자 + "과열 위험"
    /// - 열 지도(J): 과열 모듈에 "과열 중", 방열판 추천 자리(과열 모듈 옆 · 지을 수 있고 바깥이 열린 빈 칸, 상위 8곳)를 깜빡이는 하늘색 칸으로, 막히거나 멈춘 방열판 표시
    /// - 화면 왼쪽 아래 한 줄: 과열 · 위험 수와 안내 (대륙급부터)
    /// - 카메라에서 실제로 보이는 모듈 · 빈 칸에만 상자 · 라벨 (가려진 뒤쪽은 숨김 — 앞뒤 구분), 건설 메뉴에서 방열판을 고르면 열 지도가 저절로 켜짐
    /// </summary>
    public sealed class HeatMapView : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private StationController _station;
        [Tooltip("반투명 재질 (M_DefenseRange)")]
        [SerializeField] private Material _material;
        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private Camera _camera;

        /// <summary>월드 상자 + 화면 고정 크기 라벨(진한 바탕 알약, 운석 예상 표시와 같은 방식 — 가까이 가도 커지지 않고 정거장에 가려지지 않음).</summary>
        private sealed class Box
        {
            public MeshRenderer Renderer;
            public RectTransform Label;
            public UnityEngine.UI.Image LabelBack;
            public TextMeshProUGUI LabelText;
            public Vector3 Anchor;
            public bool HasLabel;
            /// <summary>상자가 가리키는 모듈 (추천 빈 칸이면 null → <see cref="Cell"/>).</summary>
            public ModuleInstance Module;
            public Vector3Int Cell;
            /// <summary>카메라에서 실제로 보이는지 (앞의 모듈에 가려지면 라벨 · 상자를 숨김).</summary>
            public bool Visible = true;
        }

        // 사용자 피드백 2026-10-14: 라벨이 정거장을 뚫고 늘 보여 앞쪽 · 뒤쪽 융합로를 구분할 수 없었음 → 카메라에서 보이는 것만 표시
        private const float VisibilityInterval = 0.2f;
        private float _nextVisibility;
        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private BuildController _build;
        private bool _forced;
        /// <summary>열 지도 켜짐 = J로 켰거나 건설 메뉴에서 방열판을 고름.</summary>
        private bool MapOn => _mapOn || _forced;

        private RectTransform _labelRoot;

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
            _build = FindFirstObjectByType<BuildController>();
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
            canvas.sortingOrder = -10; // 라벨이 건설 메뉴 · 창 위로 올라오지 않게 HUD 아래
            var scaler = canvasGo.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _labelRoot = (RectTransform)new GameObject("Labels", typeof(RectTransform)).transform;
            _labelRoot.SetParent(canvasGo.transform, false);
            var go = new GameObject("HeatLine", typeof(RectTransform));
            go.transform.SetParent(canvasGo.transform, false);
            _hud = go.AddComponent<TextMeshProUGUI>();
            _hud.font = _font;
            _hud.fontSize = 22f;
            _hud.outlineWidth = 0.25f; // 붉은 화면 위에서도 읽히게
            _hud.outlineColor = new Color32(0, 0, 0, 255);
            _hud.raycastTarget = false;
            _hud.alignment = TextAlignmentOptions.BottomLeft;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(24f, 300f); // 아래 조작 안내 줄과 겹치지 않게
            rt.sizeDelta = new Vector2(1100f, 70f);
        }

        private void Update()
        {
            if (!InputGate.Blocked && KeyBindings.WasPressed(GameAction.HeatMap) && _heat.Active)
            {
                _mapOn = !_mapOn;
                _dirty = true;
            }
            // 방열판을 고르면 열 지도가 저절로 켜져 추천 칸이 바로 보임 (배치를 끝내거나 다른 모듈을 고르면 원래대로)
            bool forced = _heat.Active && _build != null && _build.Selected != null && _build.Selected.HeatRemoval > 0f;
            if (forced != _forced)
            {
                _forced = forced;
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
                _nextVisibility = 0f;
            }
            if (Time.unscaledTime >= _nextVisibility)
            {
                _nextVisibility = Time.unscaledTime + VisibilityInterval;
                UpdateVisibility();
            }
            _pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4f));
            for (int i = 0; i < _used; i++)
            {
                var b = _boxes[i];
                if (!b.HasLabel || _camera == null)
                    continue;
                Vector3 screen = _camera.WorldToScreenPoint(b.Anchor);
                bool on = screen.z > 0f && b.Visible;
                if (b.Label.gameObject.activeSelf != on)
                    b.Label.gameObject.SetActive(on);
                if (on)
                    b.Label.position = new Vector3(screen.x, screen.y, 0f);
            }
            ApplyPulse();
        }

        /// <summary>
        /// 카메라 → 모듈 각 칸(추천 자리는 그 빈 칸) 중심으로 Pick 콜라이더 레이: 처음 맞는 것이 그 모듈이거나(빈 칸이면) 아무것도 없으면 보임.
        /// 가려진 것은 상자 · 라벨 모두 숨김 → 지금 보이는 면에 있는 것만 표시.
        /// </summary>
        private void UpdateVisibility()
        {
            if (_camera == null)
                return;
            int mask = ModulePick.SelectionMask(~0);
            Vector3 eye = _camera.transform.position;
            for (int i = 0; i < _used; i++)
            {
                var b = _boxes[i];
                bool visible = false;
                if (b.Module != null)
                {
                    foreach (var c in b.Module.Cells)
                    {
                        if (SeesCell(eye, c, mask, b.Module))
                        {
                            visible = true;
                            b.Anchor = GridConfig.CellToWorld(c) + Vector3.up * (GridConfig.CellSize * 0.5f);
                            break;
                        }
                    }
                }
                else
                    visible = SeesCell(eye, b.Cell, mask, null);
                b.Visible = visible;
                if (b.Renderer.gameObject.activeSelf != visible)
                    b.Renderer.gameObject.SetActive(visible);
            }
        }

        private bool SeesCell(Vector3 eye, Vector3Int cell, int mask, ModuleInstance target)
        {
            Vector3 to = GridConfig.CellToWorld(cell);
            Vector3 dir = to - eye;
            float dist = dir.magnitude;
            int n = Physics.RaycastNonAlloc(eye, dir / dist, _hits, dist, mask, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            ModuleView first = null;
            for (int i = 0; i < n; i++)
            {
                if (_hits[i].distance >= nearest)
                    continue;
                var view = _hits[i].collider.GetComponentInParent<ModuleView>();
                if (view == null)
                    continue;
                nearest = _hits[i].distance;
                first = view;
            }
            if (first == null)
                return true;
            if (target != null)
                return first.Module == target;
            return nearest >= dist - GridConfig.CellSize * 0.55f; // 빈 칸: 그 칸 바로 앞까지 막힌 게 없음
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
                if (blink && box.HasLabel) // 위험 · 추천 자리 라벨 테두리도 깜빡임 (색이 아닌 움직임으로 구분)
                    box.LabelBack.color = new Color(0f, 0f, 0f, 0.6f + 0.3f * _pulse);
            }
        }

        // 사용자 피드백 2026-10-14 (색약 · 태양 폭풍 붉은 화면과 겹침): 빨강 · 주황 단계 대신 글자 + 밝기 + 깜빡임으로 구분하고,
        // 방열판을 놓을 자리를 직접 보여 줌. 색은 노랑(과열) · 흰색 깜빡임(위험) · 하늘색(방열판 · 추천 자리)만.
        private static readonly Color OverColor = new Color(1f, 0.85f, 0.1f, 0.42f);
        private static readonly Color DangerColor = new Color(1f, 1f, 1f, 0.6f);
        private static readonly Color SpotColor = new Color(0.2f, 0.8f, 1f, 0.45f);
        private static readonly Color RadiatorColor = new Color(0.2f, 0.8f, 1f, 0.22f);
        private static readonly Color DeadRadiatorColor = new Color(0.55f, 0.55f, 0.6f, 0.3f);
        private const int MaxSpots = 16; // 가려진 칸은 숨기므로 넉넉히

        private readonly List<(Vector3Int cell, int helps, float relief)> _spots = new List<(Vector3Int, int, float)>();
        private readonly Dictionary<Vector3Int, (int helps, float relief)> _spotScore = new Dictionary<Vector3Int, (int, float)>();
        private ModuleData _radiator;

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
                if (!isOver)
                    continue; // 한도 이하는 칠하지 않음 (정거장 전체가 물들어 어디가 문제인지 안 보였음)
                // 라벨: 지도를 끄면 위험만 (과열 상자는 늘 보임), 지도를 켜면 과열도
                // 문구는 상태로 (사용자 피드백 2026-10-14: "방열판 여기" 같은 지시문보다 "과열 중"이 보기 좋음)
                string label = isDanger ? "<b>과열 위험</b>" : MapOn ? "<color=#FFE14A>과열 중</color>" : null;
                Place(m, isDanger ? DangerColor : OverColor, isDanger, label);
            }
            int spots = 0;
            if (MapOn)
            {
                foreach (var m in _station.Grid.Modules)
                {
                    if (m.Data == null || m.Data.HeatRemoval <= 0f)
                        continue;
                    bool working = _heat.RadiatorWorking(m);
                    Place(m, working ? RadiatorColor : DeadRadiatorColor, false,
                        working ? null : DamageSystem.IsExterior(_station.Grid, m) ? "방열 정지" : "방열 막힘");
                }
                spots = PlaceSpots();
            }
            HideFrom(_used);
            string key = KeyBindings.Label(GameAction.HeatMap);
            string tail = MapOn
                ? (spots > 0 ? $"\n<color=#7FE0FF>깜빡이는 하늘색 칸 = 방열판 추천 자리</color>  <color=#C8D2DE>보이는 면만 표시 · 카메라를 돌려 다른 면 확인</color>" : "")
                : $"  <color=#C8D2DE>{key}: 열 지도 · 방열판 추천 자리 보기</color>";
            _hud.SetText($"<b>열 관리</b>  과열 {over} · 위험 {danger}  <color=#C8D2DE>(한도 {limit:0} · 위험 {_heat.DangerLevel:0.#})</color>{tail}");
        }

        /// <summary>
        /// 방열판 추천 자리: 과열 모듈과 면이 맞닿은 빈 칸 중, 그 자리에 지을 수 있고 바깥으로 열린 면이 남는 곳.
        /// 점수 = 열을 내려 줄 과열 모듈 수 → 줄여 주는 열 합. 상위 몇 곳만 (하늘색 상자 + "방열판 여기 · 과열 n개").
        /// </summary>
        private int PlaceSpots()
        {
            _radiator ??= FindRadiator();
            if (_radiator == null)
                return 0;
            var grid = _station.Grid;
            var sim = _station.Simulation;
            float limit = _heat.Limit;
            float removal = _radiator.HeatRemoval;
            _spotScore.Clear();
            foreach (var kv in _heat.HeatMap)
            {
                if (kv.Value <= limit + 1e-4f)
                    continue;
                float relief = Mathf.Min(removal, kv.Value);
                foreach (var c in kv.Key.Cells)
                {
                    foreach (var d in GridDirections.Faces)
                    {
                        var p = c + d;
                        if (grid.IsOccupied(p))
                            continue;
                        _spotScore.TryGetValue(p, out var s);
                        _spotScore[p] = (s.helps + 1, s.relief + relief);
                    }
                }
            }
            _spots.Clear();
            foreach (var kv in _spotScore)
            {
                var p = kv.Key;
                bool open = false;
                foreach (var d in GridDirections.Faces)
                    open |= !grid.IsOccupied(p + d);
                if (!open)
                    continue; // 지어도 묻힘
                var r = sim.EvaluatePlacement(_radiator, p, 0);
                if (r != PlacementResult.Valid && r != PlacementResult.InsufficientResources)
                    continue;
                _spots.Add((p, kv.Value.helps, kv.Value.relief));
            }
            _spots.Sort((a, b) => a.helps != b.helps ? b.helps.CompareTo(a.helps) : b.relief.CompareTo(a.relief));
            int n = Mathf.Min(MaxSpots, _spots.Count);
            for (int i = 0; i < n; i++)
                PlaceCell(_spots[i].cell, SpotColor, true, null); // 글자 없이 하늘색 상자만 (안내는 아래 상태 줄)
            return n;
        }

        private ModuleData FindRadiator()
        {
            var p = _station.Simulation.Progression;
            for (int g = 0; g < p.GradeCount; g++)
                foreach (var m in p.GetGrade(g).Unlocks)
                    if (m != null && m.HeatRemoval > 0f)
                        return m;
            return null;
        }

        private void PlaceCell(Vector3Int cell, Color color, bool blink, string label)
        {
            var b = NextBox();
            b.Module = null;
            b.Cell = cell;
            var t = b.Renderer.transform;
            t.position = GridConfig.CellToWorld(cell);
            t.localScale = Vector3.one * (GridConfig.CellSize * 0.7f);
            Finish(b, color, blink, label);
        }

        private Box NextBox()
        {
            if (_used >= _boxes.Count)
                _boxes.Add(CreateBox());
            return _boxes[_used++];
        }

        private void Finish(Box b, Color color, bool blink, string label)
        {
            var t = b.Renderer.transform;
            if (!b.Renderer.gameObject.activeSelf)
                b.Renderer.gameObject.SetActive(true);
            _colors.Add((b, color, blink));
            b.HasLabel = label != null;
            if (!b.HasLabel)
            {
                if (b.Label.gameObject.activeSelf)
                    b.Label.gameObject.SetActive(false);
                return;
            }
            b.Anchor = t.position + Vector3.up * (t.localScale.y * 0.5f);
            b.LabelText.SetText(label);
            var size = b.LabelText.GetPreferredValues(label);
            b.Label.sizeDelta = new Vector2(size.x + 22f, 34f);
            b.LabelBack.color = new Color(0f, 0f, 0f, 0.82f);
        }

        private void Place(ModuleInstance m, Color color, bool blink, string label)
        {
            var b = NextBox();
            b.Module = m;
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
            Finish(b, color, blink, label);
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
            // 화면 라벨: 진한 바탕(붉은 화면 · 밝은 모듈 위에서도 읽히게) + 흰 글자 (색은 보조)
            var lgo = new GameObject("HeatLabel", typeof(RectTransform));
            lgo.transform.SetParent(_labelRoot, false);
            var rt = (RectTransform)lgo.transform;
            rt.pivot = new Vector2(0.5f, 0f);
            var back = lgo.AddComponent<UnityEngine.UI.Image>();
            back.raycastTarget = false;
            back.color = new Color(0f, 0f, 0f, 0.82f);
            var tgo = new GameObject("Text", typeof(RectTransform));
            tgo.transform.SetParent(lgo.transform, false);
            var trt = (RectTransform)tgo.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            var tmp = tgo.AddComponent<TextMeshProUGUI>();
            tmp.font = _font;
            tmp.fontSize = 20f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
            lgo.SetActive(false);
            return new Box { Renderer = r, Label = rt, LabelBack = back, LabelText = tmp };
        }

        private void HideFrom(int index)
        {
            for (int i = index; i < _boxes.Count; i++)
            {
                if (_boxes[i].Renderer.gameObject.activeSelf)
                    _boxes[i].Renderer.gameObject.SetActive(false);
                if (_boxes[i].Label.gameObject.activeSelf)
                    _boxes[i].Label.gameObject.SetActive(false);
                _boxes[i].HasLabel = false;
            }
            if (index == 0)
                _used = 0;
        }
    }
}
