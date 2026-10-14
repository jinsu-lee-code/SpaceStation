using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using SpaceStation.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceStation.Building
{
    /// <summary>
    /// 12-3c 대륙급 열 관리 표시 (그레이박스). 사용자 피드백 2026-10-14 (색약 · 붉은 태양 폭풍 화면 · 앞뒤 구분):
    /// - 색은 보조로만: 상태는 라벨 글자("과열 중" · "과열 위험") + 밝기 + 깜빡임. 상자 색은 노랑(과열) · 흰 깜빡임(위험) · 하늘색(방열판 · 추천 자리)
    /// - 라벨은 파손 표기와 같은 <see cref="WorldMarkerLayer"/> (창과 같은 테두리, 라벨 끝에서 모듈까지 연결선, 겹치면 비켜 쌓기)
    /// - 카메라에서 보이는 모듈만 진한 라벨 + 상자, 정거장 안쪽에 가려진 과열 모듈은 흐린 작은 라벨 "안쪽 과열"(상자 없음) — 앞뒤 구분 + 숨은 과열도 놓치지 않음
    /// - 늘: 과열 상자, 위험 라벨. 열 지도(J, 또는 건설 메뉴에서 방열판을 고르면 저절로): 과열 라벨, 방열판 추천 자리(깜빡이는 하늘색 칸), 막히거나 멈춘 방열판
    /// - 화면 왼쪽 아래 상태 줄: 과열 · 위험 수와 안내 (대륙급부터)
    /// </summary>
    public sealed class HeatMapView : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private StationController _station;
        [Tooltip("반투명 재질 (M_DefenseRange)")]
        [SerializeField] private Material _material;
        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private Camera _camera;
        [Header("창 스타일 (연구 창 것)")]
        [SerializeField] private Sprite _fillSprite;
        [SerializeField] private Sprite _frameSprite;
        [SerializeField] private HoloArt _holoArt;

        private enum Kind
        {
            Over,
            Danger,
            Radiator,
            Spot,
        }

        private sealed class Box
        {
            public MeshRenderer Renderer;
            public Kind Kind;
            public Color Color;
            public bool Blink;
            /// <summary>가리키는 모듈 (추천 빈 칸이면 null → <see cref="Cell"/>).</summary>
            public ModuleInstance Module;
            public Vector3Int Cell;
            /// <summary>라벨 글자 (null이면 라벨 없음).</summary>
            public string Label;
            /// <summary>카메라에서 보이는지 (가려지면 상자를 숨기고 라벨은 흐리게 · 또는 없음).</summary>
            public bool Visible = true;
            public Vector3 Anchor;
        }

        private static readonly Color OverColor = new Color(1f, 0.85f, 0.1f, 0.42f);
        private static readonly Color DangerColor = new Color(1f, 1f, 1f, 0.6f);
        private static readonly Color SpotColor = new Color(0.2f, 0.8f, 1f, 0.45f);
        private static readonly Color RadiatorColor = new Color(0.2f, 0.8f, 1f, 0.22f);
        private static readonly Color DeadRadiatorColor = new Color(0.55f, 0.55f, 0.6f, 0.3f);
        private static readonly Color OverAccent = new Color(1f, 0.85f, 0.2f, 0.9f);
        private static readonly Color DangerAccent = new Color(1f, 1f, 1f, 0.95f);
        private static readonly Color RadiatorAccent = new Color(0.65f, 0.7f, 0.75f, 0.9f);
        private const int MaxSpots = 16; // 가려진 칸은 숨기므로 넉넉히
        private const float VisibilityInterval = 0.2f;

        private HeatSystem _heat;
        private MaterialPropertyBlock _block;
        private readonly List<Box> _boxes = new List<Box>();
        private int _used;
        private bool _mapOn;
        private bool _forced;
        private bool _dirty = true;
        private float _nextVisibility;
        private float _pulse;
        private TextMeshProUGUI _hud;
        private WorldMarkerLayer _layer;
        private Mesh _cube;
        private BuildController _build;
        private ModuleData _radiator;
        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private readonly List<(Vector3Int cell, int helps, float relief)> _spots = new List<(Vector3Int, int, float)>();
        private readonly Dictionary<Vector3Int, (int helps, float relief)> _spotScore = new Dictionary<Vector3Int, (int, float)>();

        /// <summary>열 지도 켜짐 = J로 켰거나 건설 메뉴에서 방열판을 고름.</summary>
        private bool MapOn => _mapOn || _forced;

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
            var labels = (RectTransform)new GameObject("Labels", typeof(RectTransform)).transform;
            labels.SetParent(canvasGo.transform, false);
            HoloUi.Stretch(labels);
            var ui = _holoArt != null || _frameSprite != null ? HoloUi.For(_holoArt, _font, _fillSprite, _frameSprite, null) : null;
            _layer = new WorldMarkerLayer(labels, _camera, ui, _font) { FontSize = 20f, LiftPixels = 40f };

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
                    _used = 0;
                    HideFrom(0);
                    _layer.Clear();
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
                if (!b.Renderer.gameObject.activeSelf)
                    continue;
                var c = b.Color;
                if (b.Blink)
                    c.a *= _pulse;
                _block.SetColor(BaseColorId, c);
                b.Renderer.SetPropertyBlock(_block);
            }
            DrawLabels();
        }

        // ---------------- 라벨 ----------------

        private void DrawLabels()
        {
            _layer.Begin();
            for (int i = 0; i < _used; i++)
            {
                var b = _boxes[i];
                if (b.Label == null)
                    continue;
                bool hidden = !b.Visible;
                if (hidden && b.Kind != Kind.Over && b.Kind != Kind.Danger)
                    continue; // 가려진 방열판 · 빈 칸은 표시 안 함
                // 가려진 과열 모듈: 흐린 작은 라벨, 화면 밖이면 생략 (보이는 것만 가장자리 화살표)
                var m = _layer.Show(b, b.Anchor, faint: hidden, edgeArrow: !hidden && b.Kind == Kind.Danger);
                string text = hidden ? (b.Kind == Kind.Danger ? "안쪽 과열 위험" : "안쪽 과열") : b.Label;
                string arrow = m.Arrow.Length > 0 ? m.Arrow : "";
                _layer.Set(m, (int)b.Kind * 2 + (hidden ? 1 : 0), arrow + text, b.Kind == Kind.Danger ? DangerAccent : b.Kind == Kind.Over ? OverAccent : RadiatorAccent);
            }
            _layer.End();
        }

        // ---------------- 보임 판정 ----------------

        /// <summary>
        /// 카메라 → 모듈 각 칸(추천 자리는 그 빈 칸) 중심으로 Pick 콜라이더 레이: 처음 맞는 것이 그 모듈이거나(빈 칸이면) 막힌 게 없으면 보임.
        /// 보이면 상자 + 진한 라벨(보이는 칸 위), 가려지면 상자 숨김 + 흐린 라벨(모듈 윗면 가운데).
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
                    if (!visible)
                        b.Anchor = TopCenter(b.Module);
                }
                else
                {
                    visible = SeesCell(eye, b.Cell, mask, null);
                    b.Anchor = GridConfig.CellToWorld(b.Cell);
                }
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
            if (dist < 1e-3f)
                return true;
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

        private static Vector3 TopCenter(ModuleInstance m)
        {
            Vector3 sum = Vector3.zero;
            float top = float.MinValue;
            foreach (var c in m.Cells)
            {
                var w = GridConfig.CellToWorld(c);
                sum += w;
                top = Mathf.Max(top, w.y);
            }
            var center = sum / m.Cells.Count;
            center.y = top + GridConfig.CellSize * 0.5f;
            return center;
        }

        // ---------------- 상자 구성 ----------------

        private void Rebuild()
        {
            _used = 0;
            int over = 0, danger = 0;
            float limit = _heat.Limit;
            foreach (var kv in _heat.HeatMap)
            {
                var m = kv.Key;
                bool isOver = kv.Value > limit + 1e-4f;
                if (!isOver)
                    continue; // 한도 이하는 칠하지 않음 (정거장 전체가 물들어 어디가 문제인지 안 보였음)
                bool isDanger = _heat.IsDanger(m);
                over++;
                if (isDanger)
                    danger++;
                // 문구는 상태로 (사용자 피드백: "방열판 여기" 같은 지시문보다 "과열 중"). 지도를 끄면 위험 라벨만
                string label = isDanger ? "<b>과열 위험</b>" : MapOn ? "과열 중" : null;
                PlaceModule(m, isDanger ? Kind.Danger : Kind.Over, isDanger ? DangerColor : OverColor, isDanger, label);
            }
            int spots = 0;
            if (MapOn)
            {
                foreach (var m in _station.Grid.Modules)
                {
                    if (m.Data == null || m.Data.HeatRemoval <= 0f)
                        continue;
                    bool working = _heat.RadiatorWorking(m);
                    PlaceModule(m, Kind.Radiator, working ? RadiatorColor : DeadRadiatorColor, false,
                        working ? null : DamageSystem.IsExterior(_station.Grid, m) ? "방열 정지" : "방열 막힘");
                }
                spots = PlaceSpots();
            }
            HideFrom(_used);
            string key = KeyBindings.Label(GameAction.HeatMap);
            string tail = MapOn
                ? (spots > 0 ? "\n<color=#7FE0FF>깜빡이는 하늘색 칸 = 방열판 추천 자리</color>  <color=#C8D2DE>보이는 면만 표시 · 흐린 라벨 = 안쪽에 가려진 과열</color>" : "")
                : $"  <color=#C8D2DE>{key}: 열 지도 · 방열판 추천 자리 보기</color>";
            _hud.SetText($"<b>열 관리</b>  과열 {over} · 위험 {danger}  <color=#C8D2DE>(한도 {limit:0} · 위험 {_heat.DangerLevel:0.#})</color>{tail}");
        }

        /// <summary>
        /// 방열판 추천 자리: 과열 모듈과 면이 맞닿은 빈 칸 중, 그 자리에 지을 수 있고 바깥으로 열린 면이 남는 곳.
        /// 점수 = 열을 내려 줄 과열 모듈 수 → 줄여 주는 열 합, 상위 몇 곳.
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
            {
                var b = NextBox(Kind.Spot, SpotColor, true, null);
                b.Module = null;
                b.Cell = _spots[i].cell;
                var t = b.Renderer.transform;
                t.position = GridConfig.CellToWorld(b.Cell);
                t.localScale = Vector3.one * (GridConfig.CellSize * 0.7f);
            }
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

        private void PlaceModule(ModuleInstance m, Kind kind, Color color, bool blink, string label)
        {
            var b = NextBox(kind, color, blink, label);
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
            b.Anchor = TopCenter(m);
        }

        private Box NextBox(Kind kind, Color color, bool blink, string label)
        {
            if (_used >= _boxes.Count)
                _boxes.Add(CreateBox());
            var b = _boxes[_used++];
            b.Kind = kind;
            b.Color = color;
            b.Blink = blink;
            b.Label = label;
            b.Visible = true;
            if (!b.Renderer.gameObject.activeSelf)
                b.Renderer.gameObject.SetActive(true);
            return b;
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
            return new Box { Renderer = r };
        }

        private void HideFrom(int index)
        {
            for (int i = index; i < _boxes.Count; i++)
            {
                if (_boxes[i].Renderer.gameObject.activeSelf)
                    _boxes[i].Renderer.gameObject.SetActive(false);
                _boxes[i].Label = null;
            }
        }
    }
}
