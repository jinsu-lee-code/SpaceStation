using System;
using System.Collections.Generic;
using System.Text;
using SpaceStation.Audio;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using SpaceStation.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 11-14 내부 건설 (휴대 패드 건설 탭): 바깥 건설 메뉴와 같은 모듈 목록 · 분류 · 비용 · 해금을 패드 화면에 두고,
    /// 지을 자리는 홀로그램 모형 위에서 고른다 — 지금 층의 후보 칸(빈 · 닿은 칸, 고른 모듈 · 회전으로 지을 수 있으면 초록),
    /// 마우스가 가리킨 칸에 반투명 미리보기, R 회전, 클릭 건설, 우클릭 · ESC 고르기 취소, Q/E 층(맨 위 위 · 맨 아래 아래 빈 층 포함).
    /// 판정 · 건설은 바깥과 같은 <see cref="StationController.EvaluatePlacement"/> · <see cref="StationController.TryPlace"/>,
    /// 원점은 바깥처럼 붙는 면 바깥쪽으로 민다(<see cref="PlacementRules.AnchorOnFace"/>), 도킹은 입구 방향을 자동으로 맞춘다.
    /// 인접 효과 미리보기 = 패드 글 + 효과가 생기는 이웃을 모형에서 초록(좋음) · 주황(나쁨)으로.
    /// </summary>
    public sealed class PadBuild
    {
        private static readonly Color Good = new Color(0.45f, 1f, 0.6f);
        private static readonly Color Bad = new Color(1f, 0.6f, 0.25f);

        private readonly StationController _station;
        private readonly IReadOnlyList<ModuleData> _modules;
        private readonly PadHologram _holo;
        private readonly Camera _camera;
        private readonly Action<string, bool> _message;

        private readonly RectTransform _root;
        private readonly List<ModuleCategory> _categories = new List<ModuleCategory>();
        private readonly List<ModuleData> _categoryModules = new List<ModuleData>();
        private readonly List<(ModuleCategory Category, Button Button)> _tabs = new List<(ModuleCategory, Button)>();
        private readonly List<Tile> _tiles = new List<Tile>();
        private readonly TMP_Text _detail;

        private ModuleCategory _category;
        private ModuleData _selected;
        private int _rotation;
        private readonly List<Vector3Int> _candidates = new List<Vector3Int>();
        private readonly List<bool> _candidateValid = new List<bool>();
        private readonly HashSet<Vector3Int> _candidateSet = new HashSet<Vector3Int>();
        private bool _hasTarget;
        private Vector3Int _target;
        private PlacementResult _targetResult;
        private readonly List<AppliedAdjacency> _self = new List<AppliedAdjacency>();
        private readonly List<NeighborAdjacencyPreview> _neighbors = new List<NeighborAdjacencyPreview>();
        private string _adjacencyText = string.Empty;
        private int _shownFloor = int.MinValue;
        private int _shownModules = -1;
        private bool _dirty = true;

        private sealed class Tile
        {
            public ModuleData Data;
            public Button Button;
            public Image Icon;
            public TMP_Text Name;
            public TMP_Text Cost;
        }

        public bool Active { get; private set; }
        public ModuleData Selected => _selected;

        /// <param name="root">건설 탭 내용 영역 (패드 화면 안, 왼쪽 위 기준 좌표).</param>
        /// <param name="message">알림 (글, 실패 여부).</param>
        public PadBuild(HoloUi ui, RectTransform root, float width, StationController station, IReadOnlyList<ModuleData> modules,
            PadHologram holo, Camera camera, Action<string, bool> message)
        {
            _root = root;
            _station = station;
            _modules = modules ?? Array.Empty<ModuleData>();
            _holo = holo;
            _camera = camera;
            _message = message;

            BuildCategories.GetAvailable(_modules, _categories);
            _category = _categories.Count > 0 ? _categories[0] : default;

            // 왼쪽: 분류 탭 + 모듈 타일 4 × 2
            const float x0 = 16f, listW = 520f;
            float tabW = (listW - 6f * (_categories.Count - 1)) / Mathf.Max(1, _categories.Count);
            for (int i = 0; i < _categories.Count; i++)
            {
                var category = _categories[i];
                var b = ui.TechButton(root, category.DisplayName(), 15f, () => SetCategory(category));
                HoloUi.Place((RectTransform)b.transform, new Vector2(x0 + i * (tabW + 6f), -76f), new Vector2(tabW, 32f));
                _tabs.Add((category, b));
            }
            float tileW = (listW - 18f) / 4f, tileH = 106f;
            for (int i = 0; i < 8; i++)
            {
                int col = i % 4, row = i / 4;
                var tile = new Tile();
                int index = i;
                tile.Button = ui.TechButton(root, "", 12f, () => ToggleTile(index));
                var rt = (RectTransform)tile.Button.transform;
                HoloUi.Place(rt, new Vector2(x0 + col * (tileW + 6f), -116f - row * (tileH + 6f)), new Vector2(tileW, tileH));
                var iconRt = HoloUi.Rect("Icon", rt);
                HoloUi.Place(iconRt, new Vector2((tileW - 52f) * 0.5f, -6f), new Vector2(52f, 52f));
                tile.Icon = iconRt.gameObject.AddComponent<Image>();
                tile.Icon.preserveAspect = true;
                tile.Icon.raycastTarget = false;
                tile.Name = ui.Label(rt, "", 14f, TextAlignmentOptions.Center);
                HoloUi.Place(tile.Name.rectTransform, new Vector2(2f, -58f), new Vector2(tileW - 4f, 20f));
                tile.Name.overflowMode = TextOverflowModes.Ellipsis;
                tile.Cost = ui.Label(rt, "", 12f, TextAlignmentOptions.Center);
                HoloUi.Place(tile.Cost.rectTransform, new Vector2(2f, -79f), new Vector2(tileW - 4f, 20f));
                _tiles.Add(tile);
            }

            // 오른쪽: 고른 모듈 · 자리 판정 · 인접 효과 · 조작 안내
            float px = x0 + listW + 16f, pw = width - px - 16f;
            var panel = HoloUi.Rect("BuildPanel", root);
            HoloUi.Place(panel, new Vector2(px, -76f), new Vector2(pw, 254f));
            ui.TechPanel(panel.gameObject, new Color(0.03f, 0.1f, 0.15f, 0.88f), new Color(HudTheme.Accent.r, HudTheme.Accent.g, HudTheme.Accent.b, 0.7f), "BUILD", 0.2f);
            _detail = ui.Label(panel, "", 14f, TextAlignmentOptions.TopLeft, wrap: true);
            HoloUi.Stretch(_detail.rectTransform, new Vector2(14f, 8f), new Vector2(-14f, -24f));
            _detail.overflowMode = TextOverflowModes.Ellipsis;

            RefreshCategory();
            root.gameObject.SetActive(false);
        }

        // ---------------- 탭 · 목록 ----------------

        public void SetActive(bool on)
        {
            if (Active == on)
                return;
            Active = on;
            _root.gameObject.SetActive(on);
            if (!on)
            {
                Select(null);
                _holo?.ClearBuild();
            }
            _dirty = true;
        }

        private void SetCategory(ModuleCategory category)
        {
            if (category == _category)
                return;
            _category = category;
            AudioService.TryPlay(l => l.UiTab);
            RefreshCategory();
        }

        private void RefreshCategory()
        {
            BuildCategories.Filter(_modules, _category, _categoryModules);
            foreach (var (category, button) in _tabs)
                ((Image)button.targetGraphic).color = category == _category ? HudTheme.ButtonSelected : HudTheme.ButtonNormal;
            for (int i = 0; i < _tiles.Count; i++)
            {
                var tile = _tiles[i];
                tile.Data = i < _categoryModules.Count ? _categoryModules[i] : null;
                tile.Button.gameObject.SetActive(tile.Data != null);
                if (tile.Data == null)
                    continue;
                tile.Icon.sprite = tile.Data.Icon;
                tile.Icon.enabled = tile.Data.Icon != null;
                tile.Name.SetText(tile.Data.DisplayName);
            }
            RefreshTiles();
        }

        /// <summary>타일 상태 (해금 · 최대 수 · 비용 · 고름) — 자원이 바뀌면 다시.</summary>
        private void RefreshTiles()
        {
            var sim = _station.Simulation;
            foreach (var tile in _tiles)
            {
                if (tile.Data == null)
                    continue;
                var buildable = _station.CheckBuildable(tile.Data);
                bool affordable = sim.CanAfford(tile.Data);
                tile.Button.interactable = buildable == PlacementResult.Valid;
                tile.Cost.SetText(buildable == PlacementResult.ModuleLocked ? $"<color={HudText.Muted}>잠김</color>"
                    : buildable == PlacementResult.LimitReached ? $"<color={HudText.Yellow}>최대 수</color>"
                    : affordable ? HudText.Cost(sim.GetBuildCost(tile.Data))
                    : $"<color={HudText.Red}>{HudText.Cost(sim.GetBuildCost(tile.Data))}</color>");
                ((Image)tile.Button.targetGraphic).color = tile.Data == _selected ? HudTheme.ButtonSelected : HudTheme.ButtonNormal;
                var fx = tile.Button.GetComponent<HoloButtonFx>();
                if (fx != null)
                    fx.Status = buildable == PlacementResult.Valid && !affordable ? EfficiencyBands.WarningTint : (Color?)null; // 자원 부족 = 노랑 막대
            }
        }

        private void ToggleTile(int index)
        {
            var data = _tiles[index].Data;
            Select(data == _selected ? null : data);
        }

        private void Select(ModuleData data)
        {
            if (data == _selected)
                return;
            _selected = data;
            _rotation = 0;
            _hasTarget = false;
            AudioService.TryPlay(l => data != null ? l.BuildSelect : l.UiClose, data != null ? 1f : 0.6f);
            _dirty = true;
            if (_holo != null)
            {
                _holo.SetGhost(data, default, 0, false);
                _holo.HideGhost();
                _holo.Highlight.Clear();
            }
            RefreshTiles();
        }

        // ---------------- 매 프레임 ----------------

        /// <summary>고른 모듈 취소 (ESC — 패드 축소보다 먼저). 취소했으면 true.</summary>
        public bool Cancel()
        {
            if (!Active || _selected == null)
                return false;
            Select(null);
            return true;
        }

        /// <summary>건설 탭이 열린 확대 상태에서 매 프레임.</summary>
        public void Tick(bool pointerOverUi, bool refresh)
        {
            if (!Active || _holo == null)
                return;
            var mouse = Mouse.current;
            if (_selected != null)
            {
                if (mouse != null && mouse.rightButton.wasPressedThisFrame)
                    Select(null);
                else if (KeyBindings.WasPressed(GameAction.Rotate))
                {
                    _rotation = GridDirections.NormalizeRotation(_rotation + 1);
                    AudioService.TryPlay(l => l.BuildRotate);
                    _dirty = true;
                }
            }

            int floor = _holo.Floor;
            if (refresh || _dirty || floor != _shownFloor || _station.Grid.ModuleCount != _shownModules)
            {
                _shownFloor = floor;
                _shownModules = _station.Grid.ModuleCount;
                _dirty = false;
                RefreshCandidates(floor);
                RefreshTiles();
                _hasTarget = false; // 판정 다시
            }

            UpdateTarget(pointerOverUi ? (Vector2?)null : mouse?.position.ReadValue());
            if (_selected != null && _hasTarget && mouse != null && mouse.leftButton.wasPressedThisFrame && !pointerOverUi)
                Place();
            RefreshDetail();
        }

        private void RefreshCandidates(int floor)
        {
            PadMap.BuildCandidates(_station.Grid, floor, _candidates);
            _candidateSet.Clear();
            _candidateValid.Clear();
            foreach (var cell in _candidates)
            {
                _candidateSet.Add(cell);
                bool ok = false;
                if (_selected != null)
                {
                    int rotation = _rotation;
                    var origin = Origin(cell, ref rotation);
                    ok = _station.EvaluatePlacement(_selected, origin, rotation) == PlacementResult.Valid;
                }
                _candidateValid.Add(ok);
            }
            _holo.SetBuildTiles(_selected != null ? _candidates : null, _candidateValid);
        }

        /// <summary>칸 → 원점 (바깥 건설처럼 붙는 면 바깥쪽으로 밀기, 도킹은 입구 방향 자동).</summary>
        private Vector3Int Origin(Vector3Int cell, ref int rotation)
        {
            var outward = PadMap.Outward(_station.Grid, cell);
            if (_selected.TerminalOnly && outward.y == 0 && outward != Vector3Int.zero)
            {
                for (int r = 0; r < 4; r++)
                {
                    if (PlacementRules.DockFrontWorld(_selected, r) == outward)
                    {
                        rotation = r;
                        break;
                    }
                }
            }
            return PlacementRules.AnchorOnFace(_selected, cell, outward, rotation);
        }

        private void UpdateTarget(Vector2? screen)
        {
            if (_selected == null)
                return;
            Vector3Int cell = default;
            bool hit = screen.HasValue && _holo.PickCell(_camera.ScreenPointToRay(screen.Value), out cell) && _candidateSet.Contains(cell);
            if (!hit)
            {
                _hasTarget = false;
                _holo.HideGhost();
                if (_holo.Highlight.Count > 0)
                    _holo.Highlight.Clear();
                _adjacencyText = string.Empty;
                return;
            }
            int rotation = _rotation;
            var origin = Origin(cell, ref rotation);
            if (_hasTarget && origin == _target && rotation == _rotation)
                return;
            if (_selected.TerminalOnly)
                _rotation = rotation; // 도킹은 입구 방향 자동
            _hasTarget = true;
            _target = origin;
            _targetResult = _station.EvaluatePlacement(_selected, origin, _rotation);
            _holo.SetGhost(_selected, origin, _rotation, _targetResult == PlacementResult.Valid);
            _holo.ShowGhost();
            PreviewAdjacency();
        }

        private void PreviewAdjacency()
        {
            _holo.Highlight.Clear();
            _adjacencyText = string.Empty;
            if (_targetResult != PlacementResult.Valid)
                return;
            _station.Simulation.PreviewAdjacency(_selected, _target, _rotation, _self, _neighbors);
            var sb = new StringBuilder();
            if (_self.Count > 0)
            {
                sb.Append($"<color={HudText.Muted}>이 모듈</color>  ");
                foreach (var a in _self)
                    Effect(sb, a.Rule, a.Total);
                sb.Append('\n');
            }
            if (_neighbors.Count > 0)
            {
                sb.Append($"<color={HudText.Muted}>이웃</color>  ");
                foreach (var n in _neighbors)
                {
                    sb.Append(n.Module.Data != null ? n.Module.Data.DisplayName : "").Append(' ');
                    Effect(sb, n.Rule, n.Total);
                    bool good = AdjacencySystem.IsBeneficial(n.Rule, n.Total);
                    // 좋은 효과와 나쁜 효과가 겹치면 나쁜 쪽(주황)을 보임
                    if (!_holo.Highlight.TryGetValue(n.Module, out var shown) || shown != Bad)
                        _holo.Highlight[n.Module] = good ? Good : Bad;
                }
            }
            _adjacencyText = sb.ToString();
        }

        private static void Effect(StringBuilder sb, AdjacencyRule rule, float total)
        {
            var resource = AdjacencySystem.EffectResource(rule);
            bool good = AdjacencySystem.IsBeneficial(rule, total);
            sb.Append("<color=").Append(good ? HudTheme.GreenHex : HudText.Orange).Append('>')
              .Append(resource.HasValue ? resource.Value.DisplayName() : "주민").Append(' ')
              .Append(AdjacencySystem.ShortValue(rule, total)).Append("</color>  ");
        }

        private void Place()
        {
            if (_targetResult != PlacementResult.Valid)
            {
                _message(HudText.PlacementReason(_targetResult), true);
                AudioService.TryPlay(l => l.UiError);
                return;
            }
            if (_station.TryPlace(_selected, _target, _rotation, out var placed))
            {
                AudioService.TryPlay(l => l.BuildPlace);
                AudioService.TryPlay(l => l.BuildConnect, 1f);
                _message($"{_selected.DisplayName} 건설 · {HudText.Cost(_station.Simulation.GetBuildCost(_selected))}", false); // "—"은 글꼴에 없음
                _dirty = true;
                _hasTarget = false;
                _holo.HideGhost();
                _holo.Highlight.Clear();
            }
            else
            {
                _message("건설하지 못했습니다", true);
                AudioService.TryPlay(l => l.UiError);
            }
        }

        private void RefreshDetail()
        {
            if (_selected == null)
            {
                _detail.SetText($"<color={HudText.Muted}>왼쪽에서 지을 모듈을 고르세요.\n\n홀로그램 모형에 지을 수 있는 칸이 표시돼요.\nQ · E로 층을 바꾸면 위 · 아래 빈 층에도 지을 수 있어요. 휠로 확대 · 축소.</color>");
                return;
            }
            var sim = _station.Simulation;
            string state = !_hasTarget ? $"<color={HudText.Muted}>모형에서 초록 칸을 가리키세요</color>"
                : _targetResult == PlacementResult.Valid ? $"<color={HudTheme.GreenHex}>여기에 지을 수 있음</color>"
                : $"<color={HudText.Red}>{HudText.PlacementReason(_targetResult)}</color>";
            string adjacency = _adjacencyText.Length > 0 ? "\n" + _adjacencyText : string.Empty;
            _detail.SetText($"<b><size=118%>{_selected.DisplayName}</size></b>  <color={HudText.Muted}>{_selected.CellOffsets.Count}칸</color>\n" +
                            $"비용 {HudText.Cost(sim.GetBuildCost(_selected))}\n{state}{adjacency}\n" +
                            $"<color={HudText.Muted}>클릭 건설 · {KeyBindings.Label(GameAction.Rotate)} 회전 · 우클릭/ESC 취소</color>");
        }
    }
}
