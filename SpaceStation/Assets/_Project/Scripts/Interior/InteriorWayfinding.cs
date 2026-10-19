using System;
using System.Collections.Generic;
using System.Text;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 12-5 연결 허브(환승 로비) 안내 — 사용자 결정 2026-10-19: 표지판 · 키오스크 = 실제 연결, 노선 색 · 무늬는 고정.
    /// - 문 위 표지판 4: 그 문 너머 모듈 이름 / 없으면 흐리게 "연결 없음" + 위쪽 화살표(INT_Hub_Arrow*) 숨김
    /// - 키오스크: 문마다 · 위아래 이웃 목록 + 허브가 이어 주는 인접 효과(허브 건너편끼리 맞닿은 것처럼 계산되는 규칙, <see cref="AdjacencySystem"/>)
    /// </summary>
    public sealed class InteriorWayfinding : InteriorRoomDisplay
    {
        [Serializable]
        public struct Sign
        {
            public Vector3Int Direction;   // 모듈 로컬 문 방향
            public Vector3 Center;
            public Vector3 Normal;
            public float Width;
            public float Height;
            public Renderer[] Arrow;
        }

        [SerializeField] private Sign[] _signs = Array.Empty<Sign>();
        [Header("키오스크 (로컬 화면 좌표 u = 오른쪽, v = 위)")]
        [SerializeField] private Vector3 _kioskCenter;
        [SerializeField] private Vector3 _kioskNormal;
        [SerializeField] private Vector3 _kioskUp;
        [SerializeField] private Vector3 _kioskRight;
        [Tooltip("목록 줄: 문 방향(로컬) · 줄 높이 v (위 = (0,1,0), 아래 = (0,−1,0))")]
        [SerializeField] private Vector3Int[] _rowDirections = Array.Empty<Vector3Int>();
        [SerializeField] private float[] _rowV = Array.Empty<float>();
        [SerializeField] private Vector2 _listU = new Vector2(-0.28f, -0.02f);
        [SerializeField] private Vector2 _relayU = new Vector2(0.02f, 0.39f);
        [SerializeField] private float _relayHeadV = 0.11f;
        [SerializeField] private Vector2 _relayLinesV = new Vector2(0.045f, -0.23f);
        [SerializeField] private float _titleV = 0.205f;

        private TextMeshPro[] _signTexts;
        private TextMeshPro[] _rowTexts;
        private TextMeshPro _relayHead, _relayLines, _title;
        private readonly List<ModuleInstance> _neighbors = new List<ModuleInstance>();
        private readonly List<AppliedAdjacency> _applied = new List<AppliedAdjacency>();
        private readonly List<ModuleData> _one = new List<ModuleData>(1);
        private readonly StringBuilder _sb = new StringBuilder();

        public void Configure(Sign[] signs, Vector3 kioskCenter, Vector3 kioskNormal, Vector3 kioskUp, Vector3 kioskRight,
            Vector3Int[] rowDirections, float[] rowV)
        {
            _signs = signs;
            _kioskCenter = kioskCenter;
            _kioskNormal = kioskNormal;
            _kioskUp = kioskUp;
            _kioskRight = kioskRight;
            _rowDirections = rowDirections;
            _rowV = rowV;
        }

        private Vector3 Kiosk(float u, float v) => _kioskCenter + _kioskRight * u + _kioskUp * v;

        private void EnsureTexts()
        {
            if (_signTexts != null)
                return;
            _signTexts = new TextMeshPro[_signs.Length];
            for (int i = 0; i < _signs.Length; i++)
                _signTexts[i] = CreateText("SignText", _signs[i].Center, _signs[i].Normal, Vector3.up, _signs[i].Width, _signs[i].Height, 2.4f,
                    TextAlignmentOptions.Left);
            _rowTexts = new TextMeshPro[_rowDirections.Length];
            float listW = _listU.y - _listU.x;
            for (int i = 0; i < _rowDirections.Length; i++)
                _rowTexts[i] = CreateText("KioskRow", Kiosk((_listU.x + _listU.y) / 2f, _rowV[i]), _kioskNormal, _kioskUp, listW, 0.05f, 1.2f,
                    TextAlignmentOptions.Left);
            float relayW = _relayU.y - _relayU.x;
            _relayHead = CreateText("KioskRelayHead", Kiosk((_relayU.x + _relayU.y) / 2f, _relayHeadV), _kioskNormal, _kioskUp, relayW, 0.045f, 1.0f,
                TextAlignmentOptions.Left);
            _relayLines = CreateText("KioskRelay", Kiosk((_relayU.x + _relayU.y) / 2f, (_relayLinesV.x + _relayLinesV.y) / 2f), _kioskNormal, _kioskUp,
                relayW, _relayLinesV.x - _relayLinesV.y, 0.9f, TextAlignmentOptions.TopLeft);
            _title = CreateText("KioskTitle", Kiosk(0f, _titleV), _kioskNormal, _kioskUp, 0.6f, 0.06f, 1.4f, TextAlignmentOptions.Center);
            SetText(_title, "환승 안내", ScreenText);
            SetText(_relayHead, "허브가 이어 주는 인접 효과", ScreenText);
        }

        protected override void Refresh()
        {
            EnsureTexts();
            var grid = Station != null && Station.Simulation != null ? Station.Grid : null;
            for (int i = 0; i < _signs.Length; i++)
            {
                var other = Neighbor(grid, _signs[i].Direction);
                SetText(_signTexts[i], other != null ? other.Data.DisplayName : "연결 없음", other != null ? ScreenText : ScreenDim);
                if (_signs[i].Arrow != null)
                {
                    foreach (var r in _signs[i].Arrow)
                    {
                        if (r != null && r.enabled != (other != null))
                            r.enabled = other != null;
                    }
                }
            }
            for (int i = 0; i < _rowDirections.Length; i++)
            {
                var d = _rowDirections[i];
                var other = Neighbor(grid, d);
                string head = d.y > 0 ? "위층" : d.y < 0 ? "아래층" : "";
                string name = other != null ? other.Data.DisplayName : "연결 없음";
                SetText(_rowTexts[i], head.Length > 0 ? head + " · " + name : name, other != null ? ScreenText : ScreenDim);
            }
            SetText(_relayLines, RelayText(grid), ScreenText);
        }

        private ModuleInstance Neighbor(StationGrid grid, Vector3Int localDir)
        {
            if (grid == null || Module == null)
                return null;
            var cell = Module.Origin + LocalToWorldDir(Module, localDir);
            return grid.TryGetModule(cell, out var m) && m != Module && m.Data != null ? m : null;
        }

        /// <summary>허브 이웃끼리 (서로 직접 맞닿지 않은 쌍) 실제로 걸리는 인접 규칙. 없으면 안내 문구.</summary>
        private string RelayText(StationGrid grid)
        {
            var adjacency = Station != null && Station.Simulation != null ? Station.Simulation.Adjacency : null;
            if (grid == null || Module == null || adjacency == null || !adjacency.HasRules)
                return "<alpha=#60>이어 주는 효과 없음";
            grid.GetNeighborModules(Module, _neighbors);
            _sb.Clear();
            int lines = 0;
            foreach (var target in _neighbors)
            {
                if (target.Data == null || target.Data.AdjacencyRelay)
                    continue;
                foreach (var source in _neighbors)
                {
                    if (source == target || source.Data == null || source.Data.AdjacencyRelay || Touching(grid, target, source))
                        continue;
                    _one.Clear();
                    _one.Add(source.Data);
                    adjacency.Evaluate(target.Data, _one, _applied);
                    foreach (var a in _applied)
                    {
                        if (lines >= 5)
                            break;
                        if (lines > 0)
                            _sb.Append('\n');
                        _sb.Append(source.Data.DisplayName).Append(" → ").Append(target.Data.DisplayName).Append("  ")
                           .Append(AdjacencySystem.ShortValue(a.Rule, a.Total));
                        lines++;
                    }
                }
            }
            return lines > 0 ? _sb.ToString() : "<alpha=#60>이어 주는 효과 없음";
        }

        private static bool Touching(StationGrid grid, ModuleInstance a, ModuleInstance b)
        {
            foreach (var cell in a.Cells)
            {
                foreach (var d in GridDirections.Faces)
                {
                    if (grid.TryGetModule(cell + d, out var m) && m == b)
                        return true;
                }
            }
            return false;
        }
    }
}
