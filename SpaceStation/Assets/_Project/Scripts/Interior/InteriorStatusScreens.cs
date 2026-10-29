using System;
using System.Collections.Generic;
using System.Text;
using SpaceStation.Data;
using TMPro;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 모듈 내부 화면 공용 표시 (사용자 2026-10-30 "주민들이 보고 있는 화면에 아무것도 나오지 않아 아쉽다" → 공용 상태 화면 + 다른 진화 모듈에도).
    /// 템플릿 빌더가 껍데기 모델의 Screen 재질 면(앞면 = 뒤판이 붙지 않은 쪽)을 찾아 칸마다 붙인다. 칸마다: 어두운 판(가장자리는 빛나는 화면이 테처럼 남음) 위에
    /// 모듈 이름 · 상태 (가동 중 / 멈춤 · 파손은 깜빡임 — 색이 아니라 글자 · 깜빡임 · 밝기, 색약) · 주요 값 (생산 · 소비 · 모듈 특성) · 아래 막대 파형
    /// (가동 중 = 실제 시간으로 오르내림, 멈춤 = 낮게 깔려 어둡게). 값은 <see cref="InteriorRoomDisplay.Poll"/> (0.25초마다)로 갱신.
    /// </summary>
    public sealed class InteriorStatusScreens : InteriorRoomDisplay
    {
        [Serializable]
        public struct Panel
        {
            public Vector3 Center;
            public Vector3 Normal;
            public Vector3 Up;
            public Vector2 Size;
        }

        [SerializeField] private Panel[] _panels = new Panel[0];
        [SerializeField] private Material _backMaterial;
        [SerializeField] private Material _barMaterial;

        private sealed class View
        {
            public Panel P;
            public TextMeshPro Title, Status, Body;
            public Transform[] Bars;
            public Vector3 BarBase;
            public Vector3 Right;
            public float BarHeight;
            public float Seed;
        }

        private readonly List<View> _views = new List<View>();
        private bool _built;
        private bool _warn;
        private string _statusText = "";

        public void Configure(Panel[] panels, Material back, Material bar)
        {
            _panels = panels;
            _backMaterial = back;
            _barMaterial = bar;
        }

        private void Build()
        {
            if (_built)
                return;
            _built = true;
            for (int i = 0; i < _panels.Length; i++)
            {
                var p = _panels[i];
                var n = p.Normal.normalized;
                var up = p.Up.normalized;
                var right = Vector3.Cross(n, up).normalized;   // 보는 사람 기준 오른쪽 (n이 보는 사람 쪽, 왼손 좌표)
                float w = p.Size.x, h = p.Size.y;
                float m = Mathf.Min(w, h) * 0.06f;
                CreateQuad("StatusBack", p.Center, n, up, new Vector2(w - m * 2f, h - m * 2f), _backMaterial, 0.002f);
                float iw = w - m * 4f, ih = h - m * 4f;
                var top = p.Center + up * (ih * 0.5f);
                var v = new View { P = p, Right = right, Seed = i * 1.37f };
                // 낮은 화면 (세로 0.16m 미만, 연구 단지 책상 화면 등) = 값 줄 없이 이름 · 상태 + 막대만 크게
                bool compact = ih < 0.16f;
                float head = compact ? 0.45f : 0.22f;
                // 위 띠: 모듈 이름 (왼쪽) · 상태 (오른쪽)
                v.Title = CreateText("StatusTitle", top - up * (ih * head * 0.5f) - right * (iw * 0.2f), n, up, iw * 0.6f, ih * head, 0.9f, TextAlignmentOptions.Left);
                v.Status = CreateText("StatusState", top - up * (ih * head * 0.5f) + right * (iw * 0.3f), n, up, iw * 0.4f, ih * head, 0.9f, TextAlignmentOptions.Right);
                // 가운데: 값 두세 줄
                if (!compact)
                    v.Body = CreateText("StatusBody", top - up * (ih * 0.46f), n, up, iw, ih * 0.44f, 0.6f, TextAlignmentOptions.Left);
                // 아래: 막대 파형
                int count = Mathf.Clamp(Mathf.RoundToInt(iw / 0.07f), 6, 22);
                v.Bars = new Transform[count];
                v.BarHeight = ih * (compact ? 0.45f : 0.24f);
                v.BarBase = p.Center - up * (ih * 0.5f);
                float step = iw / count;
                for (int k = 0; k < count; k++)
                {
                    var c = v.BarBase - right * (iw * 0.5f - step * (k + 0.5f)) + up * (v.BarHeight * 0.5f);
                    v.Bars[k] = CreateQuad("StatusBar", c, n, up, new Vector2(step * 0.6f, v.BarHeight), _barMaterial, 0.004f);
                }
                _views.Add(v);
            }
        }

        protected override void Refresh()
        {
            Build();
            var data = Module != null ? Module.Data : null;
            bool damaged = Station != null && Station.Simulation != null && Station.Simulation.Damage != null && Module != null
                           && Station.Simulation.Damage.TryGetInfo(Module, out _);
            _warn = damaged || !Active;
            _statusText = damaged ? "파손 · 수리 필요" : Active ? "가동 중" : "멈춤";
            string title = data != null ? $"<b>{data.DisplayName}</b>" : "";
            string body = data != null ? Lines(data) : "";
            var color = Active && !damaged ? ScreenText : ScreenDim;
            foreach (var v in _views)
            {
                SetText(v.Title, title, ScreenText);
                if (v.Body != null)
                    SetText(v.Body, body, color);
            }
        }

        /// <summary>주요 값 세 줄까지: 생산 · 소비 · 모듈 특성.</summary>
        private static string Lines(ModuleData d)
        {
            var lines = new List<string>(3);
            if (d.Production != null && d.Production.Count > 0)
                lines.Add("생산  " + Flow(d.Production, "+"));
            if (d.Consumption != null && d.Consumption.Count > 0)
                lines.Add("소비  " + Flow(d.Consumption, ""));
            if (d.IsShield)
                lines.Add($"막 반경 {d.ShieldRadius}칸 · 피해 {d.ShieldReduction * 100f:0}% 감소");   // "−"(U+2212)는 글꼴에 없어 네모로 나옴
            if (d.IsTurret)
                lines.Add($"요격 반경 {d.TurretRadius}칸");
            if (d.HousingCapacity > 0)
                lines.Add($"거주 {d.HousingCapacity}명");
            if (d.BatteryCapacity > 0f)
                lines.Add($"축전 {d.BatteryCapacity:0}");
            if (d.StorageBonus > 0f)
                lines.Add($"저장 +{d.StorageBonus:0}");
            if (d.ResearchSlots > 0)
                lines.Add($"연구 자리 {d.ResearchSlots}");
            if (d.RepairSlots > 0)
                lines.Add($"수리 자리 {d.RepairSlots}");
            if (lines.Count > 3)
                lines.RemoveRange(3, lines.Count - 3);
            return string.Join("\n", lines);
        }

        private static string Flow(IReadOnlyList<ResourceAmount> list, string sign)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < list.Count && i < 3; i++)
            {
                if (i > 0)
                    sb.Append(" · ");
                sb.Append(list[i].Type.DisplayName()).Append(' ').Append(sign).Append(Number(list[i].Amount));
            }
            sb.Append("/초");
            return sb.ToString();
        }

        private static string Number(float v) => Mathf.Abs(v - Mathf.Round(v)) < 0.05f ? Mathf.Round(v).ToString("0") : v.ToString("0.#");

        private void Update()
        {
            if (!_built)
                return;
            float t = Time.unscaledTime;
            // 경고(멈춤 · 파손)는 상태 글이 0.5초마다 깜빡임
            bool show = !_warn || Mathf.Repeat(t, 1f) < 0.6f;
            foreach (var v in _views)
            {
                SetText(v.Status, show ? _statusText : "", ScreenText);
                for (int k = 0; k < v.Bars.Length; k++)
                {
                    float level = Active && !_warn
                        ? 0.25f + 0.75f * Mathf.Abs(Mathf.Sin(t * 1.7f + k * 0.55f + v.Seed) * 0.6f + Mathf.Sin(t * 3.1f + k * 1.3f + v.Seed) * 0.4f)
                        : 0.08f;
                    float hgt = Mathf.Max(0.01f, v.BarHeight * level);
                    var bar = v.Bars[k];
                    var s = bar.localScale;
                    if (Mathf.Abs(s.y - hgt) > 0.0005f)
                    {
                        bar.localScale = new Vector3(s.x, hgt, 1f);
                        var up = v.P.Up.normalized;
                        // 아래 끝을 바닥에 붙인 채 높이만 (위치 = 바닥 + 높이 절반)
                        var basePos = bar.localPosition - up * Vector3.Dot(bar.localPosition - v.BarBase, up);
                        bar.localPosition = basePos + up * (hgt * 0.5f);
                    }
                }
            }
        }
    }
}
