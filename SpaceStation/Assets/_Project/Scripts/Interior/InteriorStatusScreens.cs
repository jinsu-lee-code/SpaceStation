using System;
using System.Collections.Generic;
using System.Text;
using SpaceStation.Data;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 내부 화면 공용 표시 (사용자 2026-10-30 "주민들이 보고 있는 화면에 아무것도 나오지 않아 아쉽다" → 공용 상태 화면 + 다른 진화 모듈에도,
    /// 같은 날 "다른 단계의 모듈들도 미리 세팅" · "기본 모듈도" → 기본 · 모든 진화 단계 템플릿 공통).
    /// 템플릿 빌더가 모델의 화면 재질(M_InteriorScreen*) 면(앞면 = 뒤판이 붙지 않은 쪽)을 찾아 칸마다 붙인다. 칸마다: 어두운 판(가장자리는 빛나는 화면이
    /// 테처럼 남음) 위에 모듈 이름 · 상태 (가동 중 / 멈춤 · 파손은 깜빡임 — 색이 아니라 글자 · 깜빡임 · 밝기, 색약) · 모듈 정보창 효과를 짧게 (생산 · 소비 ·
    /// 모듈 특성, 연구 반영 값 — 세 줄씩 5초마다 넘김) · 아래 막대 파형 (가동 중 = 실제 시간으로 오르내림, 멈춤 = 낮게 깔려 어둡게).
    /// 값은 <see cref="InteriorRoomDisplay.Poll"/> (0.25초마다)로 갱신.
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
            [Tooltip("막대 재질 = 그 화면 면의 재질 (단계 키트 화면 색을 따라감)")]
            public Material Bar;
            [Tooltip("어두운 모니터 면 (빛 조각 무늬가 붙은 면) — 테 없이 면 전체를 덮음")]
            public bool Dark;
            [Tooltip("면에서 띄우는 거리 (어두운 면 위 빛 조각보다 앞)")]
            public float Lift;
        }

        private const int LinesPerPage = 3;
        private const float PageSeconds = 5f;

        [SerializeField] private Panel[] _panels = new Panel[0];
        [SerializeField] private Material _backMaterial;

        private sealed class View
        {
            public Panel P;
            public TextMeshPro Title, Status, Body;
            public Bars Batch;
            public int First;              // 이 화면 첫 막대의 첫 꼭짓점 (Batch 안)
            public Vector3[] Bottoms;      // 막대 아래 끝 가운데 (화면에서 띄움)
            public float[] Heights;
            public Vector3 Up, Right;
            public float HalfWidth;
            public float BarHeight;
            public float Seed;
        }

        /// <summary>
        /// ⑤-3 막대 묶음 (2026-11-16): 같은 재질 막대를 메시 하나로 — 예전엔 막대마다 사각형 렌더러 (화면마다 6 ~ 22개, 실드 방 44개 = 그리기 44번),
        /// 이제 재질마다 한 번. 높이는 꼭짓점 위 두 점만 옮김 (아래 끝은 그대로). 바운드는 가장 높을 때로 한 번만.
        /// </summary>
        private sealed class Bars
        {
            public Mesh Mesh;
            public readonly List<Vector3> Verts = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector4> Tangents = new List<Vector4>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int> Tris = new List<int>();
            public bool Dirty;
        }

        private readonly List<View> _views = new List<View>();
        private readonly Dictionary<Material, Bars> _bars = new Dictionary<Material, Bars>();
        private readonly List<string> _lines = new List<string>();
        private string[] _pages = { "" };   // 세 줄씩 묶은 장 (Refresh에서 만들고 Update는 고르기만)
        private bool _built;
        private bool _warn;
        private string _statusText = "";
        private Color _bodyColor = ScreenText;

        public void Configure(Panel[] panels, Material back)
        {
            _panels = panels;
            _backMaterial = back;
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
                var center = p.Center + n * p.Lift;
                // 빛나는 화면 = 가장자리를 테로 남김 · 어두운 모니터 = 면 전체를 덮어 빛 조각 무늬를 가림
                float edge = p.Dark ? 0f : m * 2f;
                CreateQuad("StatusBack", center, n, up, new Vector2(w - edge, h - edge), _backMaterial, 0.002f);
                float iw = w - m * 4f, ih = h - m * 4f;
                var top = center + up * (ih * 0.5f);
                var v = new View { P = p, Seed = i * 1.37f };
                // 낮은 화면 (세로 0.16m 미만, 연구 단지 책상 화면 등) = 값 줄 없이 이름 · 상태 + 막대만 크게
                bool compact = ih < 0.16f;
                // 세로로 긴 화면 (구조 프레임 기둥 판 등) = 이름 · 상태를 위아래 두 줄로 (한 줄이면 이름이 접힘)
                bool narrow = !compact && iw < ih * 1.3f;
                if (narrow)
                {
                    v.Title = CreateText("StatusTitle", top - up * (ih * 0.08f), n, up, iw, ih * 0.16f, 0.9f, TextAlignmentOptions.Left);
                    v.Status = CreateText("StatusState", top - up * (ih * 0.22f), n, up, iw, ih * 0.12f, 0.9f, TextAlignmentOptions.Left);
                    v.Body = CreateText("StatusBody", top - up * (ih * 0.52f), n, up, iw, ih * 0.4f, 0.6f, TextAlignmentOptions.Left);
                }
                else
                {
                    float head = compact ? 0.45f : 0.22f;
                    // 위 띠: 모듈 이름 (왼쪽) · 상태 (오른쪽)
                    v.Title = CreateText("StatusTitle", top - up * (ih * head * 0.5f) - right * (iw * 0.2f), n, up, iw * 0.6f, ih * head, 0.9f, TextAlignmentOptions.Left);
                    v.Status = CreateText("StatusState", top - up * (ih * head * 0.5f) + right * (iw * 0.3f), n, up, iw * 0.4f, ih * head, 0.9f, TextAlignmentOptions.Right);
                    // 가운데: 값 세 줄 (넘치면 장을 넘김)
                    if (!compact)
                        v.Body = CreateText("StatusBody", top - up * (ih * 0.46f), n, up, iw, ih * 0.44f, 0.6f, TextAlignmentOptions.Left);
                }
                // 한국어는 아무 글자에서나 접혀 "빗 / 겨냄"처럼 끊김 → 줄은 그대로 두고 칸에 맞게 글자를 줄임
                foreach (var t in new[] { v.Title, v.Status, v.Body })
                {
                    if (t != null)
                        t.textWrappingMode = TextWrappingModes.NoWrap;
                }
                // 아래: 막대 파형
                int count = Mathf.Clamp(Mathf.RoundToInt(iw / 0.07f), 6, 22);
                v.BarHeight = ih * (compact ? 0.45f : narrow ? 0.18f : 0.24f);
                var barBase = center - up * (ih * 0.5f) + n * 0.004f;   // 막대는 화면에서 4 mm 띄움 (글자 · 바탕보다 앞)
                float step = iw / count;
                v.Up = up;
                v.Right = right;
                v.HalfWidth = step * 0.3f;
                var barMaterial = p.Bar != null ? p.Bar : _backMaterial;
                if (barMaterial == null)
                    count = 0;
                v.Bottoms = new Vector3[count];
                v.Heights = new float[count];
                v.Batch = count > 0 ? BarsFor(barMaterial) : new Bars();
                v.First = v.Batch.Verts.Count;
                for (int k = 0; k < count; k++)
                {
                    v.Bottoms[k] = barBase - right * (iw * 0.5f - step * (k + 0.5f));
                    v.Heights[k] = v.BarHeight;
                    AddBar(v.Batch, v.Bottoms[k], right * v.HalfWidth, up * v.BarHeight, n);
                }
                _views.Add(v);
            }
            foreach (var b in _bars.Values)
            {
                b.Mesh.SetVertices(b.Verts);
                b.Mesh.SetNormals(b.Normals);
                b.Mesh.SetTangents(b.Tangents);
                b.Mesh.SetUVs(0, b.Uvs);
                b.Mesh.SetTriangles(b.Tris, 0);
                b.Mesh.RecalculateBounds();   // 가장 높을 때 — 낮아져도 그대로 (매 프레임 다시 재지 않음)
            }
        }

        /// <summary>이 재질 막대 묶음 (없으면 만듦 — 프리팹 로컬, 그림자 없음).</summary>
        private Bars BarsFor(Material material)
        {
            if (_bars.TryGetValue(material, out var b))
                return b;
            var go = new GameObject("StatusBars");
            go.transform.SetParent(transform, false);
            b = new Bars { Mesh = new Mesh { name = "StatusBars" } };
            b.Mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = b.Mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _bars[material] = b;
            return b;
        }

        /// <summary>막대 사각형 하나 (Unity 사각형과 같은 꼭짓점 순서 · 감김: 아래 왼 · 아래 오른 · 위 왼 · 위 오른).</summary>
        private static void AddBar(Bars b, Vector3 bottom, Vector3 halfRight, Vector3 height, Vector3 normal)
        {
            int i = b.Verts.Count;
            b.Verts.Add(bottom - halfRight);
            b.Verts.Add(bottom + halfRight);
            b.Verts.Add(bottom - halfRight + height);
            b.Verts.Add(bottom + halfRight + height);
            var t = new Vector4(halfRight.normalized.x, halfRight.normalized.y, halfRight.normalized.z, -1f);
            for (int k = 0; k < 4; k++)
            {
                b.Normals.Add(normal);
                b.Tangents.Add(t);
            }
            b.Uvs.Add(new Vector2(0f, 0f));
            b.Uvs.Add(new Vector2(1f, 0f));
            b.Uvs.Add(new Vector2(0f, 1f));
            b.Uvs.Add(new Vector2(1f, 1f));
            b.Tris.Add(i);
            b.Tris.Add(i + 3);
            b.Tris.Add(i + 1);
            b.Tris.Add(i + 3);
            b.Tris.Add(i);
            b.Tris.Add(i + 2);
        }

        private void OnDestroy()
        {
            foreach (var b in _bars.Values)
            {
                if (b.Mesh != null)
                    Destroy(b.Mesh);
            }
        }

        private SpaceStation.Audio.ObjectLoop[] _hums;

        /// <summary>
        /// 사운드 개편 4-4 · D-8 (2026-11-26): 화면마다 아주 작은 전자 "웅" (화면 가운데 3D 루프 — 4 m 안에서만 들림, 이 방에 있을 때) · 멈춤 · 파손 = 작게.
        /// 화면이 많은 방 (실드 방 등)은 앞 8개만 (같은 소리가 겹쳐 커지지 않게 — 화면끼리 4 m 넘게 떨어진 경우가 드묾).
        /// </summary>
        private void UpdateHum()
        {
            if (_hums == null)
            {
                if (SpaceStation.Audio.AudioService.Instance == null)
                    return;
                _hums = new SpaceStation.Audio.ObjectLoop[Mathf.Min(_panels.Length, 8)];
                for (int i = 0; i < _hums.Length; i++)
                    _hums[i] = InteriorMotionSound.LoopAt(transform, _panels[i].Center, l => l.ObjScreenHumLoop, 0.8f, 4f);
            }
            float level = !PlayerInRoom ? 0f : _warn ? 0.4f : 1f;
            foreach (var h in _hums)
            {
                if (h != null)
                    h.Level = level;
            }
        }

        protected override void Refresh()
        {
            Build();
            var data = Module != null ? Module.Data : null;
            var sim = Station != null ? Station.Simulation : null;
            bool damaged = sim != null && sim.Damage != null && Module != null && sim.Damage.TryGetInfo(Module, out _);
            _warn = damaged || !Active;
            _statusText = damaged ? "파손 · 수리 필요" : Active ? "가동 중" : "멈춤";
            _bodyColor = Active && !damaged ? ScreenText : ScreenDim;
            UpdateHum();
            _lines.Clear();
            if (data != null)
                AppendLines(_lines, data, sim != null ? sim.Effects : null);
            if (sim != null && Module != null)
                AppendNeighborLines(_lines, sim, Module); // CONNECT A5 이웃 효과 · 위험 요인 상시 표시 (3번 ④ V4)
            int pages = Mathf.Max(1, (_lines.Count + LinesPerPage - 1) / LinesPerPage);
            if (_pages.Length != pages)
                _pages = new string[pages];
            for (int i = 0; i < pages; i++)
            {
                int start = i * LinesPerPage;
                _pages[i] = start < _lines.Count ? string.Join("\n", _lines.GetRange(start, Mathf.Min(LinesPerPage, _lines.Count - start))) : "";
            }
            string title = data != null ? $"<b>{data.DisplayName}</b>" : "";
            foreach (var v in _views)
                SetText(v.Title, title, ScreenText);
        }

        private static readonly List<SpaceStation.Simulation.InnerHit> NeighborHits = new List<SpaceStation.Simulation.InnerHit>();
        private const int MaxNeighborLines = 6;

        /// <summary>
        /// CONNECT A5 (3번 ④ V4): 방 안 상태 화면에 이웃 효과(바깥 수치)와 안쪽 얼굴 — "이웃 · 물 소비 -25% (수로 공유)" · "도움 · 옆 정비 베이: 현장 수리 빨리" ·
        /// "위험 · 옆 연료전지: 화재 때 불길 두 곳" (나쁜 이웃은 "위험"으로 — 색이 아니라 글로).
        /// </summary>
        public static void AppendNeighborLines(List<string> lines, SpaceStation.Simulation.StationSimulation sim, SpaceStation.Core.ModuleInstance module)
        {
            if (sim.Adjacency == null)
                return;
            int before = lines.Count;
            sim.Heat?.AppendRoomLines(lines, module); // CONNECT ⑤ 따뜻한 방 · 과열 · 서늘한 방 · 배전 범위 밖
            int added = lines.Count - before;
            foreach (var a in sim.Adjacency.GetApplied(module))
            {
                if (added++ >= MaxNeighborLines)
                    return;
                lines.Add("이웃 · " + SpaceStation.Simulation.AdjacencySystem.Describe(a));
            }
            sim.Adjacency.InnerFor(sim.Grid, module, NeighborHits);
            foreach (var h in NeighborHits)
            {
                if (h.Face == SpaceStation.Data.InnerFace.None || h.Neighbor?.Data == null)
                    continue;
                if (added++ >= MaxNeighborLines)
                    return;
                bool bad = (h.Face & SpaceStation.Data.AdjacencyEffects.BadInner) != 0;
                lines.Add($"{(bad ? "위험" : "도움")} · 옆 {h.Neighbor.Data.DisplayName}: {SpaceStation.Data.AdjacencyEffects.InnerText(h.Face)}");
            }
        }

        /// <summary>
        /// 모듈 정보창 (<c>HudText.ModuleEffects</c>) 효과를 화면용으로 짧게 — 같은 순서 · 같은 값 (연구 반영), 설명 괄호는 뺌.
        /// 생산 · 발전 → 모듈 특성 → 소비 · 전력 수요 (첫 장에 그 모듈다운 줄이 오게).
        /// 기호 "−"(U+2212) · "×"는 글꼴에 없을 수 있어 "-" · "배"로 씀.
        /// </summary>
        public static void AppendLines(List<string> lines, ModuleData d, ResearchEffects fx)
        {
            Flow(lines, "생산", d.Production, "+", false);
            foreach (var a in d.Production)
            {
                if (a.Type == ResourceType.Power)
                    lines.Add((d.OnDemandPower ? "전력 공급 최대 +" : "전력 공급 +") + Number(a.Amount));
            }
            if (d.SolarPowered)
                lines.Add("햇빛 따라 발전 (밤에는 쉼)");
            float battery = fx != null ? fx.BatteryCapacity(d) : d.BatteryCapacity;
            if (battery > 0f)
                lines.Add($"배터리 {battery:0} · 충방전 {Number(d.BatteryRate)}/초");
            int housing = fx != null ? fx.Housing(d) : d.HousingCapacity;
            if (housing > 0)
                lines.Add($"수용 인구 +{housing}");
            float storage = fx != null ? fx.StorageBonus(d) : d.StorageBonus;
            if (storage > 0f)
                lines.Add($"저장 한도 +{storage:0}");
            if (d.ResearchSlots > 0)
                lines.Add($"동시 연구 +{d.ResearchSlots}");
            if (d.IsCargoTerminal)
                lines.Add($"화물선 {d.CargoInterval:0}초마다 · 한도의 {d.CargoFraction * 100f:0}%");
            if (d.IsShield)
                lines.Add($"실드 반경 {(fx != null ? fx.ShieldRadius(d) : d.ShieldRadius)}칸 · 운석 {d.ShieldReduction * 100f:0}% 빗겨냄");
            if (d.IsTurret)
                lines.Add($"포탑 반경 {(fx != null ? fx.TurretRadius(d) : d.TurretRadius)}칸 · 격추 {d.TurretInterceptChance * (fx != null ? fx.TurretInterceptMultiplier : 1f) * 100f:0}%");
            if (d.IsDamageControl)
                lines.Add($"손상 통제 반경 {(fx != null ? fx.ControlRadius(d) : d.ControlRadius)}칸");
            if (d.IsService)
                lines.Add($"{d.ServiceNeedsText()} 반경 {(fx != null ? fx.ServiceRadius(d) : d.ServiceRadius)}칸 · {d.ServiceCapacity}명");
            if (d.GrowthIntervalMultiplier < 0.999f)
                lines.Add($"인구 증가 간격 -{(1f - d.GrowthIntervalMultiplier) * 100f:0}%");
            if (d.IsDecoy)
                lines.Add($"미끼 반경 {(fx != null ? fx.DecoyRadius(d) : d.DecoyRadius)}칸 · 대신 맞음 {d.DecoyChance * 100f:0}%");
            if (d.MeteorWeightMultiplier > 1.001f)
                lines.Add($"운석을 {Number(d.MeteorWeightMultiplier)}배 끌어당김");
            if (d.Armored)
                lines.Add("장갑 · 파손돼도 누출 · 확산 없음");
            if (d.RepairTimeMultiplier < 0.999f)
                lines.Add($"수리 시간 {d.RepairTimeMultiplier:0.##}배");
            if (d.SpreadTimeMultiplier < 0.999f)
                lines.Add($"파손 번짐 {Number(1f / d.SpreadTimeMultiplier)}배 빠름");
            if (d.RepairSlots > 0)
                lines.Add($"동시 수리 +{d.RepairSlots}");
            if (d.HeatOutput > 0f)
                lines.Add($"발열 {Number(d.HeatOutput)}");
            if (d.HeatRemoval > 0f)
                lines.Add($"방열 -{Number(d.HeatRemoval)}");
            if (d.AdjacencyRelay)
                lines.Add("인접 효과 중계");
            if (d.WarningBonus > 0f)
                lines.Add($"유성우 · 포격 예보 +{d.WarningBonus:0}초");
            if (d.ResearchSpeedBonus > 0f)
                lines.Add($"연구 속도 +{d.ResearchSpeedBonus * 100f:0}%");
            if (d.ImmigrationPort)
                lines.Add("이민선 도착");
            if (d.RelayPower > 0)
                lines.Add($"통신 세기 +{d.RelayPower}");
            if (d.FoodKinds != FoodKind.None)
                lines.Add($"식단 {d.FoodKinds.Text()}");
            Flow(lines, "소비", d.Consumption, "", false);
            foreach (var a in d.Consumption)
            {
                if (a.Type == ResourceType.Power)
                    lines.Add("전력 수요 " + Number(a.Amount));
            }
        }

        /// <summary>전력 말고 자원 흐름 한 줄 ("생산  금속 +2 · 식량 +1/초"). 전력은 공급 · 수요라 따로.</summary>
        private static void Flow(List<string> lines, string label, IReadOnlyList<ResourceAmount> list, string sign, bool power)
        {
            if (list == null)
                return;
            var sb = new StringBuilder();
            int shown = 0;
            foreach (var a in list)
            {
                if ((a.Type == ResourceType.Power) != power || shown >= 3)
                    continue;
                sb.Append(shown++ > 0 ? " · " : label + "  ").Append(a.Type.DisplayName()).Append(' ').Append(sign).Append(Number(a.Amount));
            }
            if (shown > 0)
                lines.Add(sb.Append("/초").ToString());
        }

        private static string Number(float v) => Mathf.Abs(v - Mathf.Round(v)) < 0.05f ? Mathf.Round(v).ToString("0") : v.ToString("0.#");

        private void Update()
        {
            if (!_built)
                return;
            float t = Time.unscaledTime;
            // 경고(멈춤 · 파손)는 상태 글이 깜빡임
            bool show = !_warn || Mathf.Repeat(t, 1f) < 0.6f;
            // 값이 세 줄을 넘으면 5초마다 다음 장
            string body = _pages[Mathf.FloorToInt(t / PageSeconds) % _pages.Length];
            foreach (var v in _views)
            {
                SetText(v.Status, show ? _statusText : "", ScreenText);
                if (v.Body != null)
                    SetText(v.Body, body, _bodyColor);
                var verts = v.Batch.Verts;
                for (int k = 0; k < v.Bottoms.Length; k++)
                {
                    float level = Active && !_warn
                        ? 0.25f + 0.75f * Mathf.Abs(Mathf.Sin(t * 1.7f + k * 0.55f + v.Seed) * 0.6f + Mathf.Sin(t * 3.1f + k * 1.3f + v.Seed) * 0.4f)
                        : 0.08f;
                    float hgt = Mathf.Max(0.01f, v.BarHeight * level);
                    if (Mathf.Abs(v.Heights[k] - hgt) <= 0.0005f)
                        continue;
                    // 아래 끝은 그대로, 위 두 점만
                    v.Heights[k] = hgt;
                    int i = v.First + k * 4;
                    var top = v.Bottoms[k] + v.Up * hgt;
                    verts[i + 2] = top - v.Right * v.HalfWidth;
                    verts[i + 3] = top + v.Right * v.HalfWidth;
                    v.Batch.Dirty = true;
                }
            }
            foreach (var b in _bars.Values)
            {
                if (!b.Dirty)
                    continue;
                b.Dirty = false;
                b.Mesh.SetVertices(b.Verts, 0, b.Verts.Count, UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds);
            }
        }
    }
}
