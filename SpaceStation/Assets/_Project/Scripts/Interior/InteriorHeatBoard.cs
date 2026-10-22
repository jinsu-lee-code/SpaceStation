using System.Collections.Generic;
using SpaceStation.Core;
using TMPro;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 12-5 방열판 열 감시판 (동쪽 벽) — 사용자 결정 2026-10-22 "열 감시판".
    /// 맨 위 = 이 방열판 상태 ("방열 중 · 이웃 n곳 -4" / "막힘 · 바깥에 드러난 면 없음" / "멈춤" / "전원 꺼짐" / 대륙급 전 "열 관리 전"),
    /// 아래 = 면이 맞닿은 모듈마다 이름 · 열 막대 · "열 / 한도" · 상태 글 (정상 · 과열 · 위험은 깜빡임) — 색이 아니라 글자 · 막대 길이 · 깜빡임으로 (색약).
    /// 값은 <see cref="SpaceStation.Simulation.HeatSystem"/>과 같은 계산 (이웃 = 격자에서 면이 맞닿은 모듈).
    /// </summary>
    public sealed class InteriorHeatBoard : InteriorRoomDisplay
    {
        private const int Rows = 6;

        [SerializeField] private Vector3 _center;
        [SerializeField] private Vector3 _normal = Vector3.left;
        [SerializeField] private Vector3 _up = Vector3.up;
        [SerializeField] private Vector2 _size = new Vector2(1.2f, 1.1f);
        [SerializeField] private Material _barMaterial;
        [SerializeField] private Material _trackMaterial;

        private TextMeshPro _title;
        private readonly TextMeshPro[] _names = new TextMeshPro[Rows];
        private readonly TextMeshPro[] _values = new TextMeshPro[Rows];
        private readonly Transform[] _bars = new Transform[Rows];
        private readonly GameObject[] _tracks = new GameObject[Rows];
        private readonly Vector3[] _rows = new Vector3[Rows];
        private readonly List<ModuleInstance> _neighbors = new List<ModuleInstance>();
        private Vector3 _right;
        private float _barX, _barWidth;

        public void Configure(Vector3 center, Vector3 normal, Vector3 up, Vector2 size, Material bar, Material track)
        {
            _center = center;
            _normal = normal;
            _up = up;
            _size = size;
            _barMaterial = bar;
            _trackMaterial = track;
        }

        private void Setup()
        {
            if (_title != null)
                return;
            var up = _up.normalized;
            _right = Vector3.Cross(up, -_normal.normalized);   // 화면을 보는 사람의 오른쪽
            float w = _size.x, h = _size.y;
            float rowH = (h - 0.22f) / Rows;
            _title = CreateText("HeatTitle", _center + up * (h * 0.5f - 0.1f), _normal, up, w - 0.08f, 0.15f, 1.3f, TextAlignmentOptions.Center);
            _barX = -w * 0.5f + w * 0.36f;
            _barWidth = w * 0.3f;
            for (int i = 0; i < Rows; i++)
            {
                var c = _center + up * (h * 0.5f - 0.22f - rowH * (i + 0.5f));
                _rows[i] = c;
                _names[i] = CreateText("HeatName", c + _right * (-w * 0.5f + w * 0.18f), _normal, up, w * 0.34f, rowH * 0.8f, 0.6f, TextAlignmentOptions.Left);
                _values[i] = CreateText("HeatValue", c + _right * (w * 0.5f - w * 0.155f), _normal, up, w * 0.3f, rowH * 0.8f, 0.6f, TextAlignmentOptions.Right);
                _tracks[i] = Quad("HeatTrack", c + _right * (_barX + _barWidth * 0.5f), new Vector2(_barWidth, rowH * 0.36f), _trackMaterial, 0.002f).gameObject;
                _bars[i] = Quad("HeatBar", c + _right * (_barX + _barWidth * 0.5f), new Vector2(_barWidth, rowH * 0.36f), _barMaterial, 0.003f);
            }
        }

        private Transform Quad(string name, Vector3 center, Vector2 size, Material material, float lift)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = center + _normal.normalized * lift;
            go.transform.localRotation = Quaternion.LookRotation(-_normal.normalized, _up.normalized);
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        protected override void Refresh()
        {
            Setup();
            var heat = Station != null && Station.Simulation != null ? Station.Simulation.Heat : null;
            if (!Active)
            {
                SetText(_title, "<b>방열판 · 전원 꺼짐</b>", ScreenDim);
                ShowRows(0, null);
                return;
            }
            _neighbors.Clear();
            Station.Grid.GetNeighborModules(Module, _neighbors);
            _neighbors.RemoveAll(n => n == Module);
            if (heat == null || !heat.Active)
            {
                // 대륙급 전: 맞닿은 모듈 이름만 + "대기" (빈 화면처럼 보이지 않게)
                SetText(_title, "<b>열 관리 전</b>  대륙급부터 방열", ScreenText);
                ShowRows(Mathf.Min(Rows, _neighbors.Count), null);
                return;
            }
            _neighbors.Sort((a, b) => heat.HeatOf(b).CompareTo(heat.HeatOf(a)));
            float removal = Module.Data != null ? Module.Data.HeatRemoval : 0f;
            string title;
            if (heat.RadiatorWorking(Module))
                title = $"<b>방열 중</b>  이웃 {_neighbors.Count}곳 -{removal:0.#}";
            else if (!SpaceStation.Simulation.DamageSystem.IsExterior(Station.Grid, Module))
                title = "<b>막힘</b>  바깥에 드러난 면 없음";
            else
                title = "<b>멈춤</b>";
            SetText(_title, title, ScreenText);
            ShowRows(Mathf.Min(Rows, _neighbors.Count), heat);
        }

        private void ShowRows(int count, SpaceStation.Simulation.HeatSystem heat)
        {
            bool blink = Mathf.Repeat(Time.unscaledTime, 1f) < 0.6f;
            for (int i = 0; i < Rows; i++)
            {
                bool on = i < count;
                _tracks[i].SetActive(on && heat != null);
                _bars[i].gameObject.SetActive(on && heat != null);
                if (!on)
                {
                    SetText(_names[i], i == 0 && heat != null ? "맞닿은 모듈 없음" : "", ScreenDim);
                    SetText(_values[i], "", ScreenDim);
                    continue;
                }
                var m = _neighbors[i];
                if (heat == null)
                {
                    SetText(_names[i], m.Data != null ? m.Data.DisplayName : "모듈", ScreenText);
                    SetText(_values[i], "대기", ScreenDim);
                    continue;
                }
                float h = heat.HeatOf(m);
                float limit = Mathf.Max(0.01f, heat.Limit);
                bool danger = heat.IsDanger(m);
                bool over = heat.IsOverheated(m);
                string state = danger ? "<b>위험</b>" : over ? "<b>과열</b>" : "정상";
                SetText(_names[i], m.Data != null ? m.Data.DisplayName : "모듈", ScreenText);
                SetText(_values[i], $"{h:0.0} / {limit:0}  {state}", danger && !blink ? ScreenDim : ScreenText);
                float t = Mathf.Clamp01(h / limit);
                _bars[i].localScale = new Vector3(Mathf.Max(0.001f, _barWidth * t), _bars[i].localScale.y, 1f);
                _bars[i].localPosition = _rows[i] + _right * (_barX + _barWidth * t * 0.5f) + _normal.normalized * 0.003f;
            }
        }
    }
}
