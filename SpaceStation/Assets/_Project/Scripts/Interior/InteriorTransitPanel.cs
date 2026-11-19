using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 위아래 이동 장치의 목적지 판 (사용자 2026-11-19 "1안이 가능하면서도 깔끔하게 정리된 UI"):
    /// 층마다 한 줄 (높은 층이 위) · 왼쪽 세로선 = 이어진 기둥 · 지금 층은 밝은 점 + "지금 여기"만 · 이름만 (단계 · 설명 없음) ·
    /// 같은 층 여러 모듈은 한 줄에 나란히 (셋 넘으면 고른 칸 둘레 셋 + "+n") · 빈 층은 줄 없음 · 7줄 넘으면 고른 줄이 보이게 넘김.
    /// 처음 고른 칸 = 바로 위층 (없으면 바로 아래층). 입력은 <see cref="InteriorMode"/>가 읽어 <see cref="Move"/>로.
    /// </summary>
    public sealed class InteriorTransitPanel
    {
        private const float Width = 420f, TitleHeight = 52f, RowHeight = 44f, FooterHeight = 40f, Pad = 16f;
        private const float LineX = 30f, FloorX = 50f, ChipX = 100f, ChipGap = 8f;
        private const int MaxRows = 7, MaxChips = 3;
        /// <summary>판 바탕 (뒤 방이 비쳐 글자가 묻히지 않게 짙게).</summary>
        private static readonly Color Back = new Color(0.012f, 0.045f, 0.075f, 0.94f);

        private sealed class Row
        {
            public int Floor;
            public bool Current;
            public readonly List<TransitStop> Stops = new List<TransitStop>();
        }

        private readonly HoloUi _ui;
        private readonly Sprite _fill;
        private readonly RectTransform _root;
        private readonly List<Row> _rows = new List<Row>();
        private string _here;
        private string _footer;
        private int _row, _col;

        public bool IsOpen => _root.gameObject.activeSelf;

        public InteriorTransitPanel(HoloUi ui, Sprite fill, Transform parent)
        {
            _ui = ui;
            _fill = fill;
            // 받침 (InteriorMode가 들어가고 나올 때 오버레이 자식을 통째로 켜고 끔) 안에 판 — 열림은 판 자체로만
            var holder = HoloUi.Rect("TransitPanel", parent);
            HoloUi.Stretch(holder);
            _root = HoloUi.Rect("Panel", holder);
            _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(0.5f, 0.5f);
            _root.anchoredPosition = new Vector2(0f, 40f);
            _root.gameObject.SetActive(false);
        }

        /// <summary>목적지 판을 엶 — stops = <see cref="InteriorTransitRoutes.From"/>, footer = 키 안내 줄.</summary>
        public void Open(string currentName, int currentFloor, List<TransitStop> stops, string footer)
        {
            _rows.Clear();
            _here = currentName;
            _footer = footer;
            AddRows(stops, true);
            int currentRow = _rows.Count;
            _rows.Add(new Row { Floor = currentFloor, Current = true });
            AddRows(stops, false);
            // 처음 = 바로 위층 (지금 줄 바로 위), 없으면 바로 아래층
            _row = currentRow > 0 ? currentRow - 1 : currentRow + 1;
            _col = 0;
            _root.gameObject.SetActive(true);
            Draw();
        }

        private void AddRows(List<TransitStop> stops, bool up)
        {
            foreach (var s in stops)
            {
                if (s.Up != up)
                    continue;
                var last = _rows.Count > 0 ? _rows[_rows.Count - 1] : null;
                if (last == null || last.Current || last.Floor != s.Floor)
                {
                    last = new Row { Floor = s.Floor };
                    _rows.Add(last);
                }
                last.Stops.Add(s);
            }
        }

        public void Close() => _root.gameObject.SetActive(false);

        /// <summary>고른 목적지 (열려 있고 고를 칸이 있을 때).</summary>
        public bool TryGetSelected(out TransitStop stop)
        {
            stop = default;
            if (!IsOpen || _row < 0 || _row >= _rows.Count || _rows[_row].Current)
                return false;
            var stops = _rows[_row].Stops;
            if (stops.Count == 0)
                return false;
            stop = stops[Mathf.Clamp(_col, 0, stops.Count - 1)];
            return true;
        }

        /// <summary>dRow = 위 -1 · 아래 +1 (지금 줄은 건너뜀), dCol = 같은 층 왼쪽 -1 · 오른쪽 +1. 바뀌면 true.</summary>
        public bool Move(int dRow, int dCol)
        {
            int row = _row, col = _col;
            if (dRow != 0)
            {
                int r = _row + dRow;
                if (r >= 0 && r < _rows.Count && _rows[r].Current)
                    r += dRow;
                if (r >= 0 && r < _rows.Count)
                {
                    row = r;
                    col = Mathf.Min(_col, _rows[r].Stops.Count - 1);
                }
            }
            if (dCol != 0)
                col = Mathf.Clamp(col + dCol, 0, _rows[row].Stops.Count - 1);
            if (row == _row && col == _col)
                return false;
            _row = row;
            _col = col;
            Draw();
            return true;
        }

        // ---------------- 그리기 ----------------

        private void Draw()
        {
            for (int i = _root.childCount - 1; i >= 0; i--)
                Object.Destroy(_root.GetChild(i).gameObject);

            // 보이는 줄: 7줄 넘으면 고른 줄이 들어가게 (지금 줄도 되도록)
            int count = Mathf.Min(_rows.Count, MaxRows);
            int first = 0;
            if (_rows.Count > MaxRows)
            {
                int current = _rows.FindIndex(r => r.Current);
                first = Mathf.Clamp(Mathf.Min(_row, current) - 1, 0, _rows.Count - count);
                if (_row >= first + count)
                    first = _row - count + 1;
                if (_row < first)
                    first = _row;
            }
            float height = TitleHeight + count * RowHeight + FooterHeight;
            _root.sizeDelta = new Vector2(Width, height);

            Box("Back", _root, Vector2.zero, new Vector2(Width, height), Back, HudTheme.HoloLow);
            var title = _ui.Label(_root, "이동할 곳", 24f, TextAlignmentOptions.Left);
            HoloUi.Place(title.rectTransform, new Vector2(Pad, -12f), new Vector2(Width - Pad * 2f, 32f));
            HoloUi.Glow(title, 0.4f);

            // 이어진 기둥: 점과 점 사이 토막 (넘긴 줄이 있으면 판 끝 쪽으로도)
            for (int i = -1; i < count; i++)
            {
                bool above = i == -1 && first > 0, below = i == count - 1 && first + count < _rows.Count;
                if (i == -1 && !above || i == count - 1 && !below)
                    continue;
                float from = i == -1 ? -TitleHeight + 6f : -(TitleHeight + (i + 0.5f) * RowHeight) - 8f;
                float to = i == count - 1 ? -(TitleHeight + count * RowHeight) : -(TitleHeight + (i + 1.5f) * RowHeight) + 8f;
                Fill("Line", _root, new Vector2(LineX - 1f, from), new Vector2(2f, from - to), HudTheme.HoloMid);
            }

            for (int i = 0; i < count; i++)
                DrawRow(_rows[first + i], first + i, -(TitleHeight + i * RowHeight));

            var footer = _ui.Label(_root, _footer, 16f, TextAlignmentOptions.Left);
            footer.color = HoloUi.MutedColor;
            HoloUi.Place(footer.rectTransform, new Vector2(Pad, -(height - FooterHeight + 8f)), new Vector2(Width - Pad * 2f, 26f));
        }

        private void DrawRow(Row row, int index, float y)
        {
            float mid = y - RowHeight * 0.5f;
            if (row.Current)
                Fill("Here", _root, new Vector2(LineX - 7f, mid + 7f), new Vector2(14f, 14f), HudTheme.HoloBright);
            else
                Box("Stop", _root, new Vector2(LineX - 5f, mid + 5f), new Vector2(10f, 10f), HudTheme.HoloDeep, HudTheme.HoloBright);

            var floor = _ui.Label(_root, InteriorTransitRoutes.FloorLabel(row.Floor), 17f, TextAlignmentOptions.Left);
            floor.color = HoloUi.MutedColor;
            HoloUi.Place(floor.rectTransform, new Vector2(FloorX, mid + 13f), new Vector2(46f, 26f));

            if (row.Current)
            {
                var here = _ui.Label(_root, $"지금 여기 · {_here}", 18f, TextAlignmentOptions.Left);
                here.color = HoloUi.MutedColor;
                HoloUi.Place(here.rectTransform, new Vector2(ChipX, mid + 13f), new Vector2(Width - ChipX - Pad, 26f));
                return;
            }

            // 같은 층이 셋 넘으면 고른 칸 둘레 셋만 + "+n"
            int n = row.Stops.Count;
            int from = 0, to = n;
            bool selectedRow = index == _row;
            if (n > MaxChips)
            {
                int focus = selectedRow ? _col : 0;
                from = Mathf.Clamp(focus - 1, 0, n - MaxChips);
                to = from + MaxChips;
            }
            float x = ChipX;
            float maxX = Width - Pad;
            for (int i = from; i < to; i++)
            {
                bool on = selectedRow && i == _col;
                var stop = row.Stops[i];
                string name = stop.Module.Data != null ? stop.Module.Data.DisplayName : stop.Module.ToString();
                var label = _ui.Label(_root, name, 18f, TextAlignmentOptions.Center);
                float w = Mathf.Clamp(label.GetPreferredValues(name).x + 22f, 64f, maxX - x);
                Box("Chip", _root, new Vector2(x, mid + 15f), new Vector2(w, 30f), on ? HudTheme.HoloMid * new Color(1f, 1f, 1f, 0.55f) : new Color(0f, 0f, 0f, 0f),
                    on ? HudTheme.HoloBright : HudTheme.HoloLow);
                label.transform.SetAsLastSibling();
                label.color = on ? Color.white : HoloUi.TextColor;
                HoloUi.Place(label.rectTransform, new Vector2(x, mid + 15f), new Vector2(w, 30f));
                x += w + ChipGap;
                if (x > maxX - 40f && i < to - 1)
                    break;
            }
            int hidden = n - (to - from);
            if (hidden > 0)
            {
                var more = _ui.Label(_root, $"+{hidden}", 16f, TextAlignmentOptions.Left);
                more.color = HoloUi.MutedColor;
                HoloUi.Place(more.rectTransform, new Vector2(Mathf.Min(x, maxX - 36f), mid + 13f), new Vector2(36f, 26f));
            }
        }

        private Image Fill(string name, Transform parent, Vector2 topLeft, Vector2 size, Color color)
        {
            var rt = HoloUi.Rect(name, parent);
            HoloUi.Place(rt, topLeft, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = _fill;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>바탕 (투명이면 없음) + 테두리 1 px (네 변).</summary>
        private void Box(string name, Transform parent, Vector2 topLeft, Vector2 size, Color fill, Color frame)
        {
            if (fill.a > 0f)
                Fill(name, parent, topLeft, size, fill);
            Fill(name + "Top", parent, topLeft, new Vector2(size.x, 1f), frame);
            Fill(name + "Bottom", parent, topLeft + new Vector2(0f, 1f - size.y), new Vector2(size.x, 1f), frame);
            Fill(name + "Left", parent, topLeft, new Vector2(1f, size.y), frame);
            Fill(name + "Right", parent, topLeft + new Vector2(size.x - 1f, 0f), new Vector2(1f, size.y), frame);
        }
    }
}
