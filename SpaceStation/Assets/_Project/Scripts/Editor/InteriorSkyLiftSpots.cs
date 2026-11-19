using System.Collections.Generic;
using SpaceStation.Data;
using SpaceStation.Interior;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 템플릿 걷기 격자 (진화 내부 시험의 걷기 검사와 같은 규칙): 0.25 m 기둥마다 아래로 쏜 모든 바닥 중 몸 (반지름 0.28 · 발 위 0.5 ~ 1.6 — 계단 한 칸은 넘음)이 들어가는 곳을 마디로,
    /// 이웃 마디 높이 차 0.36 이하면 이어 걸음. 유리 엘리베이터 (<see cref="InteriorLift"/>)는 층마다 승강장 마디를 서로 이음. 좌표는 월드 (템플릿을 놓은 자리).
    /// </summary>
    public sealed class InteriorWalkGraph
    {
        public const float Step = 0.25f;
        private const float BodyRadius = 0.28f;
        private const float StepUp = 0.36f;

        private readonly float _x0, _z0;
        private readonly int _nx, _nz;
        private readonly List<Vector3> _nodes = new List<Vector3>();
        private readonly List<int>[,] _cols;
        private readonly Dictionary<int, int> _portal = new Dictionary<int, int>();
        private readonly List<List<int>> _portalNodes = new List<List<int>>();
        private bool[] _seen;

        public int Count => _nodes.Count;
        public Vector3 this[int k] => _nodes[k];
        public bool Reached(int k) => _seen != null && k >= 0 && _seen[k];

        /// <summary>area 안 기둥마다 마디를 찾는다 (위에서 아래로, 한 충돌체 속 여러 층도).</summary>
        public InteriorWalkGraph(Bounds area)
        {
            _x0 = area.min.x;
            _z0 = area.min.z;
            _nx = Mathf.RoundToInt(area.size.x / Step) + 1;
            _nz = Mathf.RoundToInt(area.size.z / Step) + 1;
            _cols = new List<int>[_nx, _nz];
            float top = area.max.y, bottom = area.min.y;
            for (int i = 0; i < _nx; i++)
            {
                for (int j = 0; j < _nz; j++)
                {
                    var origin = new Vector3(_x0 + i * Step, top, _z0 + j * Step);
                    for (int guard = 0; guard < 16 && Physics.Raycast(origin, Vector3.down, out var hit, origin.y - bottom, ~0, QueryTriggerInteraction.Ignore); guard++)
                    {
                        origin = hit.point + Vector3.down * 0.01f;
                        if (hit.normal.y < 0.7f)
                            continue;
                        var p = hit.point;
                        if (Physics.CheckCapsule(p + Vector3.up * 0.5f, p + Vector3.up * 1.6f, BodyRadius, ~0, QueryTriggerInteraction.Ignore))
                            continue;
                        if (_cols[i, j] == null)
                            _cols[i, j] = new List<int>();
                        _cols[i, j].Add(_nodes.Count);
                        _nodes.Add(p);
                    }
                }
            }
        }

        /// <summary>엘리베이터 층마다 샤프트 둘레 (안쪽 반지름 + 1.4 m) 승강장 마디를 한 묶음으로 — 한 마디에 닿으면 모든 층 승강장에 닿음.</summary>
        public void LinkLift(Vector3 center, float innerRadius, IEnumerable<float> stopHeights)
        {
            int group = _portalNodes.Count;
            var members = new List<int>();
            float reach = innerRadius + 1.4f;
            foreach (float y in stopHeights)
            {
                for (int k = 0; k < _nodes.Count; k++)
                {
                    var p = _nodes[k];
                    if (Mathf.Abs(p.y - y) < 0.45f && new Vector2(p.x - center.x, p.z - center.z).magnitude < reach && !_portal.ContainsKey(k))
                    {
                        _portal[k] = group;
                        members.Add(k);
                    }
                }
            }
            _portalNodes.Add(members);
        }

        /// <summary>p에 가장 가까운 마디 (이웃 기둥 3 × 3, 높이 차를 크게 침). 없으면 −1.</summary>
        public int Node(Vector3 p, float maxDistance = 0.5f)
        {
            int i = Mathf.RoundToInt((p.x - _x0) / Step), j = Mathf.RoundToInt((p.z - _z0) / Step);
            int best = -1;
            float bd = maxDistance;
            for (int di = -1; di <= 1; di++)
            {
                for (int dj = -1; dj <= 1; dj++)
                {
                    int a = i + di, b = j + dj;
                    if (a < 0 || b < 0 || a >= _nx || b >= _nz || _cols[a, b] == null)
                        continue;
                    foreach (int k in _cols[a, b])
                    {
                        float d = Mathf.Abs(_nodes[k].y - p.y) + new Vector2(_nodes[k].x - p.x, _nodes[k].z - p.z).magnitude * 0.1f;
                        if (d < bd)
                        {
                            bd = d;
                            best = k;
                        }
                    }
                }
            }
            return best;
        }

        /// <summary>x, z 기둥의 마디들 (위에서 아래 순).</summary>
        public IReadOnlyList<int> Column(float x, float z)
        {
            int i = Mathf.RoundToInt((x - _x0) / Step), j = Mathf.RoundToInt((z - _z0) / Step);
            return i < 0 || j < 0 || i >= _nx || j >= _nz || _cols[i, j] == null ? System.Array.Empty<int>() : (IReadOnlyList<int>)_cols[i, j];
        }

        /// <summary>시작 마디들에서 걸어서 (+ 엘리베이터) 닿는 마디를 모두 표시.</summary>
        public void Flood(IEnumerable<int> starts)
        {
            _starts = new List<int>(starts);
            _seen = Spread(_starts, null);
        }

        private List<int> _starts;

        /// <summary>
        /// 지금 닿는 마디 중 평면 원 (center, radius, 높이 ±1.5)을 막고 다시 퍼뜨리면 못 닿게 되는 마디 수 — 위아래 이동 장치가 길을 끊는지 (사용자 2026-11-19 "길을 막지 않는지").
        /// </summary>
        public int LostWithout(Vector3 center, float radius)
        {
            if (_seen == null || _starts == null)
                return 0;
            bool Blocked(int k)
            {
                var p = _nodes[k];
                return Mathf.Abs(p.y - center.y) < 1.5f && new Vector2(p.x - center.x, p.z - center.z).sqrMagnitude < radius * radius;
            }
            var seen = Spread(_starts, Blocked);
            int lost = 0;
            for (int k = 0; k < _nodes.Count; k++)
            {
                if (_seen[k] && !seen[k] && !Blocked(k))
                    lost++;
            }
            return lost;
        }

        private bool[] Spread(List<int> starts, System.Func<int, bool> blocked)
        {
            var _seen = new bool[_nodes.Count];
            var q = new Queue<int>();
            foreach (int s in starts)
            {
                if (s >= 0 && !_seen[s] && (blocked == null || !blocked(s)))
                {
                    _seen[s] = true;
                    q.Enqueue(s);
                }
            }
            var dirs = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
            while (q.Count > 0)
            {
                int k = q.Dequeue();
                var p = _nodes[k];
                if (_portal.TryGetValue(k, out int g))
                {
                    foreach (int m in _portalNodes[g])
                    {
                        if (!_seen[m] && (blocked == null || !blocked(m)))
                        {
                            _seen[m] = true;
                            q.Enqueue(m);
                        }
                    }
                }
                int i = Mathf.RoundToInt((p.x - _x0) / Step), j = Mathf.RoundToInt((p.z - _z0) / Step);
                foreach (var (di, dj) in dirs)
                {
                    int a = i + di, b = j + dj;
                    if (a < 0 || b < 0 || a >= _nx || b >= _nz || _cols[a, b] == null)
                        continue;
                    foreach (int m in _cols[a, b])
                    {
                        if (_seen[m] || Mathf.Abs(_nodes[m].y - p.y) > StepUp || (blocked != null && blocked(m)))
                            continue;
                        _seen[m] = true;
                        q.Enqueue(m);
                    }
                }
            }
            return _seen;
        }

        /// <summary>
        /// 템플릿 하나의 걷기 격자: 모듈 칸 둘레 전체, 시작 = 스폰 · 문 자리 안쪽 · 아래 해치 (들어오는 곳 모두), 엘리베이터 층 이음.
        /// root = 템플릿 프리팹을 회전 없이 놓은 것 (원점 = 0번 칸 중심).
        /// </summary>
        public static InteriorWalkGraph ForTemplate(InteriorTemplate template, Transform root)
        {
            var o = root.position;
            var bounds = CellBounds(template.Module);
            bounds.center += o;
            var graph = new InteriorWalkGraph(bounds);
            foreach (var lift in root.GetComponentsInChildren<InteriorLift>(true))
            {
                var c = lift.transform.TransformPoint(lift.Center);
                var stops = new List<float>();
                for (int k = 0; k < lift.StopCount; k++)
                    stops.Add(c.y + lift.StopOffset(k));
                graph.LinkLift(c, lift.InnerRadius, stops);
            }
            var starts = new List<int> { graph.Node(o + template.Spawn) };
            foreach (var s in template.Sockets)
            {
                var center = o + (Vector3)s.Cell * InteriorGeometry.CellSize;
                if (s.Direction.y == 0 && s.DoorLift)
                {
                    starts.Add(graph.Node(center + s.Offset, 0.8f));   // 2층 높이 옆문 = 문 승강판 판 (모든 면 연결 ②-4)
                }
                else if (s.Direction.y == 0 && s.DoorSteps != 0)
                {
                    starts.Add(graph.Node(center + s.Offset, 0.35f));   // 문 계단 내려선 자리 (계단은 문이 열릴 때만 놓임 — 모든 면 연결 ②)
                }
                else if (s.Direction.y == 0)
                {
                    // 문 자리 안쪽 0.7 m — 그 기둥의 마디 중 칸 가운데보다 아래에서 가장 높은 것 (문 바닥)
                    var p = center + (Vector3)s.Direction * (s.Depth - 0.7f);
                    int best = -1;
                    foreach (int k in graph.Column(p.x, p.z))
                    {
                        if (graph[k].y < center.y && (best < 0 || graph[k].y > graph[best].y))
                            best = k;
                    }
                    starts.Add(best);
                }
                else if (s.Direction.y < 0)
                {
                    starts.Add(graph.Node(center + s.Offset + Vector3.down * s.Depth, 0.8f));
                }
            }
            graph.Flood(starts);
            return graph;
        }

        /// <summary>모듈 칸 둘레 (0번 칸 중심 기준, 칸 경계 그대로).</summary>
        public static Bounds CellBounds(ModuleData module)
        {
            var min = Vector3.one * float.MaxValue;
            var max = Vector3.one * float.MinValue;
            foreach (var c in module.CellOffsets)
            {
                min = Vector3.Min(min, (Vector3)c);
                max = Vector3.Max(max, (Vector3)c);
            }
            float h = InteriorGeometry.CellSize * 0.5f;
            var b = new Bounds();
            b.SetMinMax(min * InteriorGeometry.CellSize - Vector3.one * h, max * InteriorGeometry.CellSize + Vector3.one * h);
            return b;
        }
    }

    /// <summary>
    /// 진화 단계별 통로 ③ 빛 기둥 승강판 자리 (사용자 2026-11-17 "A로 하는데, 색깔을 모듈의 색 테마에 맞춰서 … 모듈 자체의 높이가 각자 있잖아? 그거에 맞게 공중으로 떠오르다가 이동"):
    /// 하늘이 트인 2단계+ 방 (프리팹에 _Sky)의 외형 위 연결점마다 승강판 자리를 찾아 템플릿 문 자리 (<see cref="InteriorSocket.SkyLift"/>)로 넣는다.
    /// 메뉴 SpaceStation/Interior/Set Sky Lift Spots, Build Templates 끝에도 실행.
    /// 하늘 높이에 매단 위 해치 (깊이 3.5 이상 — 사용자 2026-11-17 "천장이 비어있는 허공인데 해치가 있는것도 이상")는 승강판으로 바꾸고 원래 자리 가까이를 먼저 봄.
    /// 단 금빛 사다리 · 진주 고리를 모델에 함께 지은 위 해치 (<see cref="KeepHatch"/>)와 지붕 · 건물 속 낮은 천장 해치는 그대로.
    /// 승강판 자리 조건: 그 칸 안 (칸 가장자리에서 0.9 m 안쪽) · 걸어서 닿는 바닥 (<see cref="InteriorWalkGraph"/>) · 판 둘레가 평평 · 판에서 하늘 고리까지 기둥에 아무것도 없음
    /// (넓은 기둥 0.75 → 없으면 가는 기둥 0.6) · 문 자리 · 아래 해치 · 나무 · 화분 · 주민 자리 · 다른 승강판과 떨어짐. 그중 칸 가운데 (바꾼 해치는 원래 자리)에 가깝고 둘레가 넓은 곳.
    /// 칸 위가 지붕 · 건물 천장이라 기둥을 세울 곳이 없으면 그 천장에 보통 위 해치 (걸어서 닿는 바닥 위 2.2 ~ 3.4 m · 평평 · 그 위로 걸을 곳 없는 맨 위 천장).
    /// </summary>
    public static class InteriorSkyLiftSpots
    {
        public const float PadRadius = 0.75f;
        /// <summary>기둥 둘레에 비울 반지름 — 넓게 먼저, 없으면 가늘게 (빛 기둥 몸통 반지름 0.55).</summary>
        public static readonly float[] ColumnRadii = { 0.75f, 0.6f };
        /// <summary>이 깊이 이상인 위 해치 = 하늘 허공의 해치 (하늘 껍질 3.95 바로 아래 3.9) → 승강판으로.</summary>
        public const float SkyHatchDepth = 3.5f;
        /// <summary>칸 중심 → 기둥 꼭대기 고리 (하늘 껍질 3.95 바로 아래).</summary>
        public const float RingDepth = 3.8f;
        /// <summary>판에서 고리까지 최소 높이 (떠오르는 맛 — 이보다 낮은 마루 위는 고르지 않음).</summary>
        public const float MinRise = 2.6f;
        /// <summary>판에서 하늘 고리까지 최대 높이 — 한 칸 아래 바닥 (−9.3 → 고리 3.8 = 13.1 m)까지. 아콜로지 1층 광장 (2026-11-19)이 꼭대기 칸 기둥 밑 26 m 아래 문 앞 길에 판을 받았음.</summary>
        public const float MaxRise = 16f;
        /// <summary>
        /// 천장 해치 (기둥을 못 세우는 지붕 밑): 바닥에서 천장까지 (눈 1.6 + 해치 광선 4 m가 비스듬히 닿는 높이 — 의료동 · 청사 천장 3.85 · 감시탑 전망실 2.13 ·
        /// 1단계 공방 · 중력 거주구 4.0, 모든 면 연결 ③ 2026-11-19에 4.0 → 4.6).
        /// </summary>
        public const float CeilingMin = 2.0f, CeilingMax = 4.6f;
        /// <summary>천장 해치 가운데를 서는 자리에서 비키는 양 (걸음 격자 0.25 m 사이 · 칸 가장자리 띠까지).</summary>
        private static readonly Vector2[] HatchShifts =
        {
            Vector2.zero, new Vector2(0.25f, 0f), new Vector2(-0.25f, 0f), new Vector2(0f, 0.25f), new Vector2(0f, -0.25f),
            new Vector2(0.25f, 0.25f), new Vector2(-0.25f, 0.25f), new Vector2(0.25f, -0.25f), new Vector2(-0.25f, -0.25f),
        };
        /// <summary>천장 해치 틀 배율: 보통 → 작은 해치 (천장이 휘거나 좁은 복도 · 탑 꼭대기 방 — 중력 거주구 둥근 복도 · 대형 토러스 바큇살).</summary>
        private static readonly float[] HatchScales = { 1f, 0.65f };
        /// <summary>천장 밑 고리 (닫힌 방 · 유리 돔 밑 승강판): 고리 둘레 (반지름 1.1)에서 가장 낮은 천장보다 이만큼 아래.</summary>
        private const float RingGap = 0.25f;
        private const float EdgeMargin = 0.9f;
        /// <summary>
        /// 거리 규칙 (문 자리 · 아래 해치 · 주민 자리에서 판까지): 엄하게 → 느슨하게. Frame = 해치 틀 네모 가장자리에서 판 테까지 (가운데 거리만 보면 틀 옆면 쪽으로
        /// 판이 틀에 걸쳤음 — 정련 콤비나트 · 식량 공장 · 융합 플랜트 · 궤도 방어망 · 방사선 장벽, 2026-11-18 플레이).
        /// 셋째 줄 = 천장 · 바닥 해치와 마지막 승강판 찾기만 (머리 위 · 발밑 해치는 길을 막지 않음 — 닫힌 방 · 붐비는 갑판, 모든 면 연결 ③ 2026-11-19): 문 · 해치 1 m, 틀 겹침만 피함, 주민 0.2 m, 풀 0.8 m.
        /// </summary>
        private static readonly (float Door, float Hatch, float Frame, float Resident)[] Rules = { (1.8f, 1.6f, 0.25f, 0.6f), (1.5f, 1.4f, 0.1f, 0.25f), (1.0f, 1.0f, 0f, 0.2f) };
        /// <summary>
        /// 승강판만 (천장 해치 · 모델 착지 원판 말고): 판 테에서 몸 높이 (0.3 · 1.0 · 1.8 m) 물체까지 · 옆으로 지나갈 폭 (허리 높이 마주 보는 두 면 사이) · 문까지 —
        /// 엄하게 → 느슨하게 → 마지막 (판이 벽에 겹치지만 않게 — 그 칸에 다른 자리가 없을 때, 기록에 "좁은 자리"). 전체 검토 (사용자 2026-11-18 "추천대로 같이 고쳐줘")에서
        /// 판 테 3 ~ 10 cm에 수레 · 안내판 · 벽, 폭 1.6 ~ 2.3 m 골목 · 옆 통로를 판이 막고, 문 1.6 m 앞에 판이 있었음.
        /// 넷째 = 셋째와 같고 거리 규칙만 <see cref="Rules"/> 셋째 줄 (붐비는 갑판 — 궤도 미러 · 화물 환적소, 모든 면 연결 ③ 2026-11-19 · 앞 세 줄로 찾던 자리는 그대로).
        /// </summary>
        private static readonly (float Gap, float Width, float Door)[] LiftRules = { (0.25f, 2.8f, 2.4f), (0.2f, 2.4f, 1.9f), (0f, 0f, 0f), (0f, 0f, 0f) };

        /// <summary>승강판 규칙 줄 → <see cref="Rules"/> 줄 (앞 세 줄 = 예전처럼 둘째까지).</summary>
        private static int LiftBlockRule(int rule) => rule < 3 ? Mathf.Min(rule, 1) : 2;
        /// <summary>바닥 해치 틀 반 변 (키트 KIT_HatchFrame 1.9 m).</summary>
        public const float HatchFrameHalf = 0.95f;
        private const float PadGap = 1.0f;
        private const string KitPath = "Assets/_Project/Data/Interior/InteriorKit.asset";
        private static readonly Vector3 Away = new Vector3(10000f, -5000f, 10000f);

        /// <summary>모델에 위 해치용 금빛 사다리 · 진주 고리가 있는 방 — 하늘 해치를 그대로 둠 (바이오스피어 하늘 정원 · 항성 발전망 전망대 원판).</summary>
        public static readonly string[] KeepHatch = { "IT_EvoBiosphere", "IT_EvoStellarGrid" };

        /// <summary>
        /// 모델에 낸 승강판 착지 자리 (칸 가운데 기준 x · z, 사용자 2026-11-17 "모델에 착지 자리 내는 걸로" · 시안 2) — 빌더 (ds4_builder · rf_builder · dd · ff · rw_builder LIFT_PADS)와 같은 값.
        /// 도구는 이 자리 그대로 판을 놓음 (착지 원판 가운데 — 걸어서 닿는 마디가 0.25 m 안 · 판 · 기둥 · 장식 조건, 거리 규칙은 느슨하게). 안 맞으면 경고 뒤 보통 찾기.
        /// 정련 콤비나트 = 갑판 여섯 (앞 양옆 · 기념탑 왼쪽 · 북쪽 셋), 심우주 통신기지 = 왼쪽 옆 돔 옆 · 가운데 뒤 강가 · 오른쪽 옆 돔 옆.
        /// 전체 검토에서 남은 좁은 자리 (2026-11-18 시안, 사용자 "B까지 포함해서 진행"): 드론 정비창 = 옥상 관람석 앞줄 양 끝 (예전 1층 뒤 골목 · 13 m),
        /// 식량 공장 = 공장 남쪽 둥근 귀퉁이 옆 (문 2.4 m — 바퀴를 피하느라 남쪽 난간에 붙음은 그대로), 방사선 차폐벽 = 마당 서쪽 (예전 아래 해치 자리, 해치는 찻집 서쪽으로).
        /// </summary>
        public static readonly Dictionary<string, (Vector3Int Cell, Vector2 XZ)[]> Targets = new Dictionary<string, (Vector3Int, Vector2)[]>
        {
            {
                "IT_EvoRefineryCombine", new[]
                {
                    (new Vector3Int(0, 0, 0), new Vector2(3.0f, -2.7f)), (new Vector3Int(1, 0, 0), new Vector2(-1.6f, -2.25f)), (new Vector3Int(2, 0, 0), new Vector2(-3.0f, -2.7f)),
                    (new Vector3Int(0, 0, 1), new Vector2(3.0f, 2.5f)), (new Vector3Int(1, 0, 1), new Vector2(2.4f, 2.85f)), (new Vector3Int(2, 0, 1), new Vector2(-3.0f, 2.5f)),
                }
            },
            {
                "IT_EvoDeepSpaceComm", new[]
                {
                    (new Vector3Int(0, 0, 0), new Vector2(2.32f, 0.6f)), (new Vector3Int(1, 0, 0), new Vector2(3.0f, 2.85f)), (new Vector3Int(2, 0, 0), new Vector2(-2.32f, 0.6f)),
                }
            },
            { "IT_EvoDroneDepot", new[] { (new Vector3Int(0, 1, 1), new Vector2(0.35f, -2.2f)), (new Vector3Int(1, 1, 1), new Vector2(2.15f, -2.2f)) } },
            { "IT_EvoFoodFactory", new[] { (new Vector3Int(1, 1, 0), new Vector2(2.25f, -3.0f)), (new Vector3Int(3, 1, 0), new Vector2(-2.4f, -3.0f)) } },
            { "IT_EvoRadiationWall", new[] { (new Vector3Int(0, 0, 1), new Vector2(-2.2f, -3.1f)) } },
        };

        [MenuItem("SpaceStation/Interior/Set Sky Lift Spots")]
        public static void SetAll()
        {
            var kit = AssetDatabase.LoadAssetAtPath<InteriorKit>(KitPath);
            int lifts = 0, hatches = 0, missing = 0;
            var log = new System.Text.StringBuilder();
            foreach (var guid in AssetDatabase.FindAssets("t:InteriorTemplate", new[] { "Assets/_Project/Data/Interior" }))
            {
                var t = AssetDatabase.LoadAssetAtPath<InteriorTemplate>(AssetDatabase.GUIDToAssetPath(guid));
                if (t == null || t.Module == null || t.Prefab == null || kit == null)
                    continue;
                if (IsOpenSky(t, kit) || AllFaces(t.Module, kit))   // 닫힌 방 (1 · 2단계 · 기본) = 천장 해치 · 바닥 해치만
                    Place(t, kit, log, ref lifts, ref hatches, ref missing);
                if (ResidentsOnWays(t))
                {
                    ResidentSpotBaker.BakeOne(t);
                    log.AppendLine($"{t.name}: 주민 자리 다시 구움 (해치 · 판을 비킴)");
                }
            }
            AssetDatabase.SaveAssets();
            string text = $"[InteriorSkyLiftSpots] 승강판 {lifts}곳 · 천장 해치 {hatches}곳" + (missing > 0 ? $" · 자리 못 찾음 {missing}곳" : "") + $"\n{log}";
            if (missing > 0)
                Debug.LogWarning(text);
            else
                Debug.Log(text);
        }

        /// <summary>하늘이 트인 2단계+ 방 (프리팹 바로 아래 _Sky 조각).</summary>
        public static bool IsOpenSky(InteriorTemplate t, InteriorKit kit)
        {
            if (t == null || t.Module == null || t.Prefab == null || kit == null || kit.StageNumber(t.Module) < 2)
                return false;
            foreach (Transform c in t.Prefab.transform)
            {
                if (c.name.EndsWith("_Sky"))
                    return true;
            }
            return false;
        }

        /// <summary>외형 위 연결점 칸 (위 칸이 모듈 밖 · 면 깊이 있음 · 막히지 않은 면).</summary>
        public static List<Vector3Int> UpFaces(ModuleData module) => Faces(module, Vector3Int.up, false);

        /// <summary>
        /// 위 · 아래로 가는 길을 낼 칸: 1 · 2단계 · 기본 방 = 모든 윗면 · 아랫면 (모든 면 연결 ③, 사용자 2026-11-19 "1, 2단계 모듈 전부 칸별 연결, 상하연결이 완료될때까지"),
        /// 3단계 이상 = 외형 위 연결점이 있는 윗면만 (3단계는 나중에 모아서) · 아랫면은 빌더가 정함.
        /// </summary>
        public static List<Vector3Int> UpFaces(ModuleData module, InteriorKit kit) => Faces(module, Vector3Int.up, AllFaces(module, kit));

        public static List<Vector3Int> DownFaces(ModuleData module, InteriorKit kit) => AllFaces(module, kit) ? Faces(module, Vector3Int.down, true) : new List<Vector3Int>();

        /// <summary>1 · 2단계 · 기본 방 (모든 윗면 · 아랫면을 잇는 방).</summary>
        public static bool AllFaces(ModuleData module, InteriorKit kit) => module != null && kit != null && kit.StageNumber(module) < 3;

        private static List<Vector3Int> Faces(ModuleData module, Vector3Int dir, bool all)
        {
            // 태양을 따라 도는 패널이 있는 모듈 (태양광 · 1단계 태양광) = 윗면 연결점 없음 (ConnectorDepthBaker와 같음 — 위 모듈 · 통로가 패널을 뚫지 않게)
            if (dir == Vector3Int.up && module.Prefab != null && module.Prefab.GetComponentInChildren<SpaceStation.Building.SunFacingPanel>(true) != null)
                return new List<Vector3Int>();
            var cells = new HashSet<Vector3Int>(module.CellOffsets);
            var list = new List<Vector3Int>();
            foreach (var c in module.CellOffsets)
            {
                if (!cells.Contains(c + dir) && (all || module.TryGetFaceDepth(c, dir, out _)) && !module.IsClosedFace(c, dir))
                    list.Add(c);
            }
            return list;
        }

        /// <summary>판 둘레 평평 (가운데 + 반지름 절반 · 판 반지름 8방향이 같은 높이 ±0.06 · 거의 수평).</summary>
        public static bool Flat(Vector3 p) => PadSurface(p, out _, out float uneven) && uneven <= 0.06f;

        /// <summary>판이 앉을 땅 높이차 (언덕 위 판은 옆 치마 0.25 m가 땅에 묻힘 — 떠 보이지 않게).</summary>
        public const float MaxUneven = 0.15f;

        /// <summary>
        /// 판 둘레 땅 (가운데 + 반지름 절반 · 판 반지름 8방향): 모두 바닥이 있고 기울기 25° 아래 · 높이차 0.15 이하면 참.
        /// top = 가장 높은 점 (판 윗면을 여기에 — 낮은 쪽은 치마가 덮음), uneven = 높이차.
        /// </summary>
        public static bool PadSurface(Vector3 p, out float top, out float uneven)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            top = p.y;
            uneven = 0f;
            for (int ring = 0; ring <= 2; ring++)
            {
                int count = ring == 0 ? 1 : 8;
                for (int a = 0; a < count; a++)
                {
                    float ang = a * Mathf.PI * 0.25f;
                    var q = p + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (PadRadius * ring * 0.5f);
                    if (!Physics.Raycast(q + Vector3.up * 0.5f, Vector3.down, out var hit, 1.2f, ~0, QueryTriggerInteraction.Ignore))
                        return false;
                    if (hit.normal.y < 0.9f || Mathf.Abs(hit.point.y - p.y) > MaxUneven)
                        return false;
                    lo = Mathf.Min(lo, hit.point.y);
                    hi = Mathf.Max(hi, hit.point.y);
                }
            }
            top = hi;
            uneven = hi - lo;
            return uneven <= MaxUneven;
        }

        /// <summary>판 위 0.2 m에서 꼭대기 고리까지 반지름 radius 기둥에 충돌체가 없음.</summary>
        public static bool ColumnClear(Vector3 pad, float ringY, float radius)
        {
            var a = pad + Vector3.up * (0.2f + radius);
            var b = new Vector3(pad.x, Mathf.Max(a.y, ringY + 0.1f - radius), pad.z);
            return !Physics.CheckCapsule(a, b, radius, ~0, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// 충돌체 없는 장식 (동물 · 바닥 빛 케이블 · 풀 · 덩굴 · 매단 등)의 정점 격자 (0.5 m 칸, 월드) — 하늘 껍질은 뺌.
        /// 심우주 통신기지에서 판이 사슴 · 빛 케이블 위에 놓였음 (충돌체만 보면 안 보임, 2026-11-17).
        /// </summary>
        public sealed class DecorGrid
        {
            private const float Cell = 0.5f;
            private readonly Dictionary<(int, int), List<Vector3>> _cells = new Dictionary<(int, int), List<Vector3>>();

            /// <param name="decorOnly">충돌체 없는 조각만 · 읽기 꺼진 메시도 (AcquireReadOnlyMeshData — 에디터에서는 읽힘). 벽 위아래 이동 장치 (2026-11-20): 연구원 벽 빛 줄 (_Low, 읽기 꺼짐)이
            /// 안 보여 장치 뒤판 위로 보였음. 끄면 예전대로 (읽히는 메시 전부 — 이미 검토한 바닥 장치 · 빛 기둥 자리 결과를 그대로 두려고).</param>
            public DecorGrid(Transform root, bool decorOnly = false)
            {
                foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (f.sharedMesh == null || f.name.EndsWith("_Sky") || (decorOnly ? f.GetComponent<Collider>() != null : !f.sharedMesh.isReadable))   // 읽기 꺼진 메시 (기본 방 FBX) — 예전 방식은 오류 없이 건너뜀
                        continue;
                    var m = f.transform.localToWorldMatrix;
                    Vector3[] vertices;
                    if (f.sharedMesh.isReadable)
                        vertices = f.sharedMesh.vertices;
                    else
                    {
                        using (var data = Mesh.AcquireReadOnlyMeshData(f.sharedMesh))
                        {
                            var native = new Unity.Collections.NativeArray<Vector3>(data[0].vertexCount, Unity.Collections.Allocator.Temp);
                            data[0].GetVertices(native);
                            vertices = native.ToArray();
                            native.Dispose();
                        }
                    }
                    foreach (var v in vertices)
                    {
                        var w = m.MultiplyPoint3x4(v);
                        var key = (Mathf.FloorToInt(w.x / Cell), Mathf.FloorToInt(w.z / Cell));
                        if (!_cells.TryGetValue(key, out var list))
                            _cells[key] = list = new List<Vector3>();
                        list.Add(w);
                    }
                }
            }

            /// <summary>p 둘레 반지름 radius · 높이 yMin ~ yMax 원기둥 안에 정점이 있음.</summary>
            public bool Any(Vector3 p, float radius, float yMin, float yMax)
            {
                int x0 = Mathf.FloorToInt((p.x - radius) / Cell), x1 = Mathf.FloorToInt((p.x + radius) / Cell);
                int z0 = Mathf.FloorToInt((p.z - radius) / Cell), z1 = Mathf.FloorToInt((p.z + radius) / Cell);
                for (int i = x0; i <= x1; i++)
                {
                    for (int j = z0; j <= z1; j++)
                    {
                        if (!_cells.TryGetValue((i, j), out var list))
                            continue;
                        foreach (var v in list)
                        {
                            if (v.y > yMin && v.y < yMax && new Vector2(v.x - p.x, v.z - p.z).sqrMagnitude < radius * radius)
                                return true;
                        }
                    }
                }
                return false;
            }

            /// <summary>p 둘레 반지름 radius · 높이 yMin ~ yMax 원기둥 안의 정점 (위아래 이동 장치 자리 — 바닥 무늬 조각 찾기).</summary>
            public List<Vector3> Near(Vector3 p, float radius, float yMin, float yMax)
            {
                var found = new List<Vector3>();
                int x0 = Mathf.FloorToInt((p.x - radius) / Cell), x1 = Mathf.FloorToInt((p.x + radius) / Cell);
                int z0 = Mathf.FloorToInt((p.z - radius) / Cell), z1 = Mathf.FloorToInt((p.z + radius) / Cell);
                for (int i = x0; i <= x1; i++)
                {
                    for (int j = z0; j <= z1; j++)
                    {
                        if (!_cells.TryGetValue((i, j), out var list))
                            continue;
                        foreach (var v in list)
                        {
                            if (v.y > yMin && v.y < yMax && new Vector2(v.x - p.x, v.z - p.z).sqrMagnitude < radius * radius)
                                found.Add(v);
                        }
                    }
                }
                return found;
            }

            /// <summary>p 둘레 네모 (반 변 half) · 높이 yMin ~ yMax 안에 정점이 있음 (천장 해치 틀 자리).</summary>
            public bool AnyBox(Vector3 p, float half, float yMin, float yMax)
            {
                int x0 = Mathf.FloorToInt((p.x - half) / Cell), x1 = Mathf.FloorToInt((p.x + half) / Cell);
                int z0 = Mathf.FloorToInt((p.z - half) / Cell), z1 = Mathf.FloorToInt((p.z + half) / Cell);
                for (int i = x0; i <= x1; i++)
                {
                    for (int j = z0; j <= z1; j++)
                    {
                        if (!_cells.TryGetValue((i, j), out var list))
                            continue;
                        foreach (var v in list)
                        {
                            if (v.y > yMin && v.y < yMax && Mathf.Abs(v.x - p.x) < half && Mathf.Abs(v.z - p.z) < half)
                                return true;
                        }
                    }
                }
                return false;
            }

            /// <summary>판 위로 솟은 장식 (판 둘레 + 0.05, 판 윗면 위 — 땅 + 0.08 ~ 몸 높이) · 빛 기둥 속 장식 (몸 높이 ~ 고리 밑)이 없음.</summary>
            public bool Clear(Vector3 pad, float ringY)
            {
                return !Any(pad, PadRadius + 0.05f, pad.y + 0.08f, pad.y + 2.3f) && !Any(pad, 0.6f, pad.y + 2.3f, ringY - 0.1f);
            }
        }

        private static void Place(InteriorTemplate t, InteriorKit kit, System.Text.StringBuilder log, ref int lifts, ref int hatches, ref int missing)
        {
            bool openSky = IsOpenSky(t, kit);
            bool keep = !openSky || System.Array.IndexOf(KeepHatch, t.name) >= 0;   // 닫힌 방 = 있던 위 해치 그대로 (승강판으로 바꾸지 않음)
            var root = Object.Instantiate(t.Prefab);
            root.transform.SetPositionAndRotation(Away, Quaternion.identity);
            Physics.SyncTransforms();
            try
            {
                var sockets = new List<InteriorSocket>();
                var preferred = new Dictionary<Vector3Int, Vector2>();
                var exact = new HashSet<Vector3Int>();
                if (Targets.TryGetValue(t.name, out var designed))
                {
                    foreach (var (c, xz) in designed)
                    {
                        preferred[c] = xz;
                        exact.Add(c);
                    }
                }
                bool changed = false;
                foreach (var s in t.Sockets)
                {
                    if (s.SkyLift)
                    {
                        changed = true; // 다시 찾음 — 이미 놓인 판 자리를 먼저 봄 (다시 돌려도 자리가 흔들리지 않게)
                        if (!preferred.ContainsKey(s.Cell))
                            preferred[s.Cell] = new Vector2(s.Offset.x, s.Offset.z);
                        continue;
                    }
                    if (s.Direction == Vector3Int.up && !keep && IsSkyHatch(s))
                    {
                        if (!preferred.ContainsKey(s.Cell))
                            preferred[s.Cell] = new Vector2(s.Offset.x, s.Offset.z);
                        changed = true;
                        continue;
                    }
                    sockets.Add(s);
                }
                var covered = new HashSet<Vector3Int>();
                var coveredDown = new HashSet<Vector3Int>();
                foreach (var s in sockets)
                {
                    if (s.Direction == Vector3Int.up)
                        covered.Add(s.Cell);
                    if (s.Direction == Vector3Int.down)
                        coveredDown.Add(s.Cell);
                }
                var faces = UpFaces(t.Module, kit).FindAll(c => !covered.Contains(c));
                var downs = DownFaces(t.Module, kit).FindAll(c => !coveredDown.Contains(c));
                if (faces.Count == 0 && downs.Count == 0)
                {
                    if (changed)
                    {
                        t.EditorSetSockets(sockets);
                        EditorUtility.SetDirty(t);
                    }
                    return;
                }
                var graph = InteriorWalkGraph.ForTemplate(t, root.transform);
                var decor = new DecorGrid(root.transform);
                var pads = new List<Vector3>();
                var bridgePads = new List<Vector3>();   // 빛 다리 판 (문턱 높이 문 승강판) — 그 칸 위 승강판이 먼저 그 판 자리에 (판을 같이 씀)
                bool liftKit = kit.SkyLiftPadFor(t.Module) != null;
                foreach (var s in sockets)
                {
                    if (s.DoorLift)   // 2층 높이 옆문 승강판 (자동 문이 정함 — 그대로 두고 위 승강판이 비킴)
                    {
                        var p = Away + (Vector3)s.Cell * InteriorGeometry.CellSize + s.Offset;
                        pads.Add(p);
                        if (InteriorGeometry.IsBridgeLift(p.y, Away.y + InteriorGeometry.FloorY(s.Cell)))
                            bridgePads.Add(p);
                    }
                    else if (s.DoorSteps != 0)   // 문 계단 자리 (층계참 · 단 · 내려선 자리 점마다)
                    {
                        var c = Away + (Vector3)s.Cell * InteriorGeometry.CellSize;
                        foreach (var p in InteriorAutoDoors.StepsFootprint(c + (Vector3)s.Direction * s.Depth, s.Direction, s.DoorSteps))
                            pads.Add(new Vector3(p.x, c.y + s.Offset.y, p.z));
                    }
                }
                log.Append(t.name).Append(':');
                foreach (var cell in faces)
                {
                    var center = Away + (Vector3)cell * InteriorGeometry.CellSize;
                    var target = preferred.TryGetValue(cell, out var pre) ? pre : Vector2.zero;
                    var why = new Dictionary<string, int>();
                    var pad = default(Vector3);
                    bool atModel = exact.Contains(cell) && TryDesigned(t, graph, decor, cell, target, pads, out pad);
                    if (exact.Contains(cell) && !atModel)
                        Debug.LogWarning($"[InteriorSkyLiftSpots] {t.name} {cell}: 모델 착지 자리 ({target.x:F2}, {target.y:F2})에 판을 못 놓음 — 다른 곳을 찾음 (원판과 어긋남)");
                    // 빛 다리 판이 이 칸에 있으면 그 판 자리에 (화물 환적장 — 자동 문이 예전 위 승강판 자리를 골랐으면 같은 자리, 거리 규칙은 셋째 줄)
                    bool shared = false;
                    if (!atModel && liftKit && openSky)
                    {
                        foreach (var bp in bridgePads)
                        {
                            if (Mathf.Abs(bp.x - center.x) >= InteriorGeometry.CellSize * 0.5f || Mathf.Abs(bp.z - center.z) >= InteriorGeometry.CellSize * 0.5f || bp.y > center.y)
                                continue;
                            var others = pads.FindAll(p => Flat2(p, bp) > 0.01f);
                            if (TryDesigned(t, graph, decor, cell, new Vector2(bp.x - center.x, bp.z - center.z), others, out pad, 2) && Mathf.Abs(pad.y - bp.y) < 0.05f)
                            {
                                atModel = shared = true;
                                break;
                            }
                        }
                    }
                    int usedRule = 0;
                    float ringDepth = RingDepth;
                    // 닫힌 방 (1 · 2단계 · 기본): 천장 해치 먼저, 천장이 높거나 기운 칸만 빛 기둥 승강판 (고리까지 떠오른 뒤 어두워지며 위 방으로 — 천장을 지나는 모습은 안 보임).
                    // 승강판 = 하늘 고리 (3.8) 먼저, 기둥이 막히면 천장 · 유리 돔 밑 고리 (닫힌 방 · 아콜로지 돔) — 단계 승강판 조각이 있는 방만 (기본 · 1단계는 해치만)
                    if (!openSky && TryFindCeilingHatch(t, graph, decor, cell, target, pads, out var closedHatch, out var closedUnder, out float closedScale, why))
                    {
                        pads.Add(closedUnder);
                        sockets.Add(new InteriorSocket(cell, Vector3Int.up, closedHatch.y - center.y, new Vector3(closedHatch.x - center.x, 0f, closedHatch.z - center.z), hatchScale: closedScale < 0.99f ? closedScale : 0f));
                        hatches++;
                        log.Append($" {cell}→천장 해치 ({closedHatch.x - center.x:F2}, {closedHatch.y - center.y:F2}, {closedHatch.z - center.z:F2})" + (closedScale < 0.99f ? $" ×{closedScale}" : ""));
                    }
                    else if (atModel || (liftKit && (TryFindLift(t, graph, decor, cell, target, pads, why, false, out pad, out usedRule, out ringDepth)
                        || TryFindLift(t, graph, decor, cell, target, pads, why, true, out pad, out usedRule, out ringDepth))))
                    {
                        pads.Add(pad);
                        sockets.Add(new InteriorSocket(cell, Vector3Int.up, ringDepth, pad - center, skyLift: true));
                        lifts++;
                        log.Append($" {cell}→판 ({pad.x - center.x:F2}, {pad.y - center.y:F2}, {pad.z - center.z:F2})" + (usedRule >= 2 ? " [좁은 자리]" : "") + (shared ? " (판 같이 씀)" : "")
                            + (ringDepth < RingDepth - 0.01f ? $" 천장 밑 고리 {ringDepth:F2}" : ""));
                    }
                    else if (TryFindCeilingHatch(t, graph, decor, cell, target, pads, out var hatch, out var under, out float scale, why))
                    {
                        pads.Add(under);
                        sockets.Add(new InteriorSocket(cell, Vector3Int.up, hatch.y - center.y, new Vector3(hatch.x - center.x, 0f, hatch.z - center.z), hatchScale: scale < 0.99f ? scale : 0f));
                        hatches++;
                        log.Append($" {cell}→천장 해치 ({hatch.x - center.x:F2}, {hatch.y - center.y:F2}, {hatch.z - center.z:F2})" + (scale < 0.99f ? $" ×{scale}" : ""));
                    }
                    else
                    {
                        missing++;
                        log.Append($" {cell}→없음 [");
                        foreach (var kv in why)
                            log.Append($"{kv.Key} {kv.Value} ");
                        log.Append(']');
                    }
                }
                foreach (var cell in downs)
                {
                    var center = Away + (Vector3)cell * InteriorGeometry.CellSize;
                    var why = new Dictionary<string, int>();
                    if (TryFindFloorHatch(t, graph, decor, cell, pads, out var floor, out float scale, why))
                    {
                        pads.Add(floor);
                        sockets.Add(new InteriorSocket(cell, Vector3Int.down, center.y - floor.y, new Vector3(floor.x - center.x, 0f, floor.z - center.z), hatchScale: scale < 0.99f ? scale : 0f));
                        hatches++;
                        log.Append($" {cell}→바닥 해치 ({floor.x - center.x:F2}, {floor.y - center.y:F2}, {floor.z - center.z:F2})" + (scale < 0.99f ? $" ×{scale}" : ""));
                    }
                    else
                    {
                        missing++;
                        log.Append($" {cell}→아래 없음 [");
                        foreach (var kv in why)
                            log.Append($"{kv.Key} {kv.Value} ");
                        log.Append(']');
                    }
                }
                log.AppendLine();
                t.EditorSetSockets(sockets);
                EditorUtility.SetDirty(t);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Physics.SyncTransforms();
            }
        }

        /// <summary>
        /// 하늘 허공에 매단 위 해치: 깊이 3.5 이상 (하늘 껍질 3.95 바로 밑)이고 그 자리 바로 위 (0.3 m 안)에 천장 충돌체가 없음 —
        /// 탑 꼭대기 방 천장 해치 (보안 본부 전망실 3.78)처럼 위에 진짜 천장이 있으면 해치 그대로. Place가 템플릿을 놓은 뒤에 부름.
        /// </summary>
        private static bool IsSkyHatch(InteriorSocket s)
        {
            if (s.Depth < SkyHatchDepth)
                return false;
            var p = Away + (Vector3)s.Cell * InteriorGeometry.CellSize + s.Offset + Vector3.up * (s.Depth - 0.05f);
            return !Physics.Raycast(p, Vector3.up, 0.3f, ~0, QueryTriggerInteraction.Ignore);
        }

        private static void Count(Dictionary<string, int> why, string reason) => why[reason] = why.TryGetValue(reason, out int n) ? n + 1 : 1;

        /// <summary>칸 안의 걸어서 닿는 마디 (가장자리 0.9 m 안쪽).</summary>
        private static IEnumerable<int> CellNodes(InteriorWalkGraph graph, Vector3 center)
        {
            int found = 0;
            foreach (int k in CellNodes(graph, center, InteriorGeometry.CellSize * 0.5f - EdgeMargin))
            {
                found++;
                yield return k;
            }
            // 가장자리 안쪽에 없으면 (대형 토러스 모서리 칸 — 바퀴가 칸 귀퉁이만 지남, 모든 면 연결 ③) 칸 끝 0.3 m까지
            if (found == 0)
            {
                foreach (int k in CellNodes(graph, center, InteriorGeometry.CellSize * 0.5f - 0.3f))
                    yield return k;
            }
        }

        private static IEnumerable<int> CellNodes(InteriorWalkGraph graph, Vector3 center, float reach)
        {
            for (float x = -reach; x <= reach + 0.001f; x += InteriorWalkGraph.Step)
            {
                for (float z = -reach; z <= reach + 0.001f; z += InteriorWalkGraph.Step)
                {
                    foreach (int k in graph.Column(center.x + x, center.z + z))
                    {
                        if (graph.Reached(k))
                            yield return k;
                    }
                }
            }
        }

        /// <summary>
        /// 빛 기둥 승강판 자리. underCeiling = 기둥이 천장 · 유리 돔에 막히는 칸 (닫힌 방 · 아콜로지 돔): 고리를 판 위 가장 낮은 천장 밑 (<see cref="RingGap"/>)에 —
        /// 떠오를 높이 (<see cref="MinRise"/>)가 되는 자리만. ringDepth = 칸 중심 → 고리.
        /// </summary>
        private static bool TryFindLift(InteriorTemplate t, InteriorWalkGraph graph, DecorGrid decor, Vector3Int cell, Vector2 target, List<Vector3> pads, Dictionary<string, int> why,
            bool underCeiling, out Vector3 pad, out int usedRule, out float ringDepth)
        {
            pad = default;
            usedRule = -1;
            ringDepth = RingDepth;
            var center = Away + (Vector3)cell * InteriorGeometry.CellSize;
            float ringY = center.y + RingDepth;
            // 거리 규칙을 엄하게 먼저, 없으면 느슨하게 (언덕 · 붐비는 칸 — 심우주 통신기지), 마지막 = 판이 벽에 겹치지만 않게 (좁은 자리 — 기록에 표시)
            for (int rule = 0; rule < LiftRules.Length; rule++)
            {
                var candidates = new List<(float Score, Vector3 P)>();
                foreach (int k in CellNodes(graph, center))
                {
                    var p = graph[k];
                    if (ringY - p.y < MinRise)
                    {
                        Count(why, "낮음");
                        continue;
                    }
                    if (!underCeiling && ringY - p.y > MaxRise)
                    {
                        Count(why, "너무 깊음");
                        continue;
                    }
                    string blocked = Blocked(t, p, pads, LiftBlockRule(rule));
                    if (blocked != null)
                    {
                        Count(why, blocked);
                        continue;
                    }
                    candidates.Add((new Vector2(p.x - center.x - target.x, p.z - center.z - target.y).magnitude, p));
                }
                candidates.Sort((a, b) => a.Score.CompareTo(b.Score));
                foreach (float radius in ColumnRadii)
                {
                    float best = float.MaxValue;
                    foreach (var (score, p) in candidates)
                    {
                        if (score >= best)
                            break;
                        if (!PadSurface(p, out float top, out float uneven))
                        {
                            Count(why, "평평X");
                            continue;
                        }
                        var q = new Vector3(p.x, top, p.z);
                        float ring = ringY;
                        if (underCeiling)
                        {
                            if (!CeilingOver(q, ringY, out float ceiling))
                                continue;   // 천장 없음 = 하늘 고리 찾기에서 이미 봄
                            ring = ceiling - RingGap;
                            if (ring - q.y < MinRise)
                            {
                                Count(why, "천장 밑 낮음");
                                continue;
                            }
                            if (ringY - ring > MaxRise)   // 고리가 위 연결점보다 한참 아래 (아콜로지 1층 광장 알 밑 유리 — 꼭대기 칸 기둥)
                            {
                                Count(why, "너무 깊음");
                                continue;
                            }
                            // 맨 위층만 (아콜로지 — 아래층 천장 밑 고리에서 꼭대기 위 방으로 가면 어색함)
                            bool topmost = true;
                            foreach (int m in graph.Column(q.x, q.z))
                            {
                                if (graph.Reached(m) && graph[m].y > ring)
                                    topmost = false;
                            }
                            if (!topmost)
                            {
                                Count(why, "위층 있음");
                                continue;
                            }
                        }
                        if (!ColumnClear(q, ring, radius))
                        {
                            Count(why, $"기둥{radius}X");
                            continue;
                        }
                        if (!decor.Clear(q, ring))
                        {
                            Count(why, "장식X");
                            continue;
                        }
                        string tight = Tight(t, q, rule);
                        if (tight != null)
                        {
                            Count(why, tight);
                            continue;
                        }
                        // 둘레가 좁으면 (반지름 1.3 · 몸 높이까지 무언가) 덜 고름 — 길목 · 벽에 바짝 붙은 자리. 기운 땅도 덜 (평평한 길 · 광장 먼저)
                        float s = score + (Roomy(q, 1.3f, 2.3f) ? 0f : 1.5f) + uneven * 8f;
                        // 느슨한 규칙에서는 옛 자리에 가까운 곳보다 넉넉한 곳을 — 엄한 규칙에 모자란 만큼 덜 고름 (벽 · 기둥에 5 cm까지 붙은 판 · 문 바로 안쪽 판, 2026-11-18 눈높이 사진)
                        if (rule > 0)
                            s += Shortfall(t, q);
                        if (s < best)
                        {
                            best = s;
                            pad = q;
                            ringDepth = underCeiling ? Mathf.Round((ring - center.y) * 100f) / 100f : RingDepth;   // 먼 자리 (Away) 좌표라 빼면 3.7998 — 하늘 고리는 그대로
                        }
                    }
                    if (best < float.MaxValue)
                    {
                        usedRule = rule;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>판 위 (고리 둘레 반지름 1.1 · 9곳) 가장 낮은 천장 · 돔 (maxY + 0.3까지) — 없으면 false.</summary>
        private static bool CeilingOver(Vector3 pad, float maxY, out float ceiling)
        {
            ceiling = float.MaxValue;
            for (int a = 0; a < 9; a++)
            {
                var d = a == 0 ? Vector3.zero : Quaternion.Euler(0f, a * 45f, 0f) * Vector3.forward * 1.1f;
                var from = pad + d + Vector3.up * 0.3f;
                if (Physics.Raycast(from, Vector3.up, out var hit, maxY + 0.3f - from.y, ~0, QueryTriggerInteraction.Ignore))
                    ceiling = Mathf.Min(ceiling, hit.point.y);
            }
            return ceiling < float.MaxValue;
        }

        /// <summary>
        /// 모델에 낸 착지 원판 가운데 그대로 (<see cref="Targets"/>): 0.25 m 안에 걸어서 닿는 마디 (그 높이) · 거리 규칙 (느슨하게) · 판 밑 땅 · 기둥 (가는 쪽까지) · 장식 조건.
        /// 보통 찾기는 걸음 격자 (0.25 m)에 붙고 둘레가 좁으면 1.5 m까지 비켜 원판과 어긋남 — 원판이 판 테 밖으로 보여야 하므로 자리를 그대로 씀.
        /// </summary>
        private static bool TryDesigned(InteriorTemplate t, InteriorWalkGraph graph, DecorGrid decor, Vector3Int cell, Vector2 target, List<Vector3> pads, out Vector3 pad, int rule = 1)
        {
            pad = default;
            var center = Away + (Vector3)cell * InteriorGeometry.CellSize;
            float ringY = center.y + RingDepth;
            var p = new Vector3(center.x + target.x, 0f, center.z + target.y);
            int near = -1;
            float best = InteriorWalkGraph.Step;
            foreach (int k in CellNodes(graph, center))
            {
                var q = graph[k];
                float d = new Vector2(q.x - p.x, q.z - p.z).magnitude;
                if (d < best && ringY - q.y >= MinRise)
                {
                    best = d;
                    near = k;
                }
            }
            if (near < 0)
                return false;
            p.y = graph[near].y;
            if (Blocked(t, p, pads, rule) != null || !PadSurface(p, out float top, out _))
                return false;
            var at = new Vector3(p.x, top, p.z);
            if (!ColumnClear(at, ringY, ColumnRadii[ColumnRadii.Length - 1]) || !decor.Clear(at, ringY))
                return false;
            pad = at;
            return true;
        }

        /// <summary>
        /// 지붕 · 건물 천장 밑 보통 위 해치: 걸어서 닿는 바닥 위 2.0 ~ 4.0 m (눈 1.6 + 해치 광선)의 아래를 보는 평평한 천장 —
        /// 틀 (1.9 m 네모) 네 모서리 · 변 가운데까지 같은 높이 (체크리스트 155), 틀이 칸 안, 천장에 붙은 등 · 빛 고리 · 장식 (충돌체 없는 정점)이 틀 자리에 없음,
        /// 그 위로 같은 기둥에 걸어서 닿는 마디가 없음 (맨 위 천장 — 2층 바닥 밑이 아님). 돌려주는 점 = 천장 면.
        /// </summary>
        private static bool TryFindCeilingHatch(InteriorTemplate t, InteriorWalkGraph graph, DecorGrid decor, Vector3Int cell, Vector2 target, List<Vector3> pads, out Vector3 hatch, out Vector3 under,
            out float scale, Dictionary<string, int> why = null)
        {
            hatch = default;
            under = default;
            scale = 1f;
            var center = Away + (Vector3)cell * InteriorGeometry.CellSize;
            float half = InteriorGeometry.CellSize * 0.5f;
            float best = float.MaxValue;
            // 보통 틀 먼저, 없으면 작은 틀 (휜 천장 · 좁은 복도). 거리 규칙: 엄하게 먼저, 없으면 느슨하게 (머리 위 해치는 문 앞 길을 막지 않음 — 시민 회관 의료동 복도)
            foreach (float size in HatchScales)
            {
                float frame = HatchFrameHalf * size;
                for (int rule = 0; rule < Rules.Length && best == float.MaxValue; rule++)
                {
                    foreach (int k in CellNodes(graph, center))
                    foreach (var shift in HatchShifts)
                    {
                        var p = graph[k];
                        // 해치 가운데 = 서는 자리 + shift (0.25 m까지 — 칸 가장자리 · 돔 옆 평평한 천장 띠에 맞춤, 농장)
                        var c = new Vector3(p.x + shift.x, p.y, p.z + shift.y);
                        float score = new Vector2(c.x - center.x - target.x, c.z - center.z - target.y).magnitude + shift.magnitude * 0.5f;
                        if (score >= best || Mathf.Abs(c.x - center.x) > half - frame - 0.05f || Mathf.Abs(c.z - center.z) > half - frame - 0.05f)
                            continue;
                        if (Blocked(t, c, pads, rule, forHatch: true) is string b)
                        {
                            if (why != null) Count(why, "해치 " + b);
                            continue;
                        }
                        if (rule < 2 ? !Flat(p) : !Standable(p))
                        {
                            if (why != null) Count(why, "해치 바닥X");
                            continue;
                        }
                        if (!Physics.Raycast(c + Vector3.up * 0.3f, Vector3.up, out var hit, CeilingMax + 0.1f, ~0, QueryTriggerInteraction.Ignore))
                        {
                            if (why != null) Count(why, "천장 높이 안 없음");
                            continue;
                        }
                        float y = hit.point.y;
                        // 천장이 칸 밑면 아래여도 됨 — 그 칸에 바닥이 없고 아래 칸 방 천장이 맨 위일 때 (코어 2층 칸 = 1층 방 천장, 모든 면 연결 ③). 맨 위인지는 아래 topmost가 봄
                        if (y - p.y < CeilingMin || y - p.y > CeilingMax || hit.normal.y > -0.95f || y > center.y + half - 0.05f)
                        {
                            if (why != null) Count(why, hit.normal.y > -0.95f ? "천장 기움" : "천장 높이");
                            continue;
                        }
                        bool flat = true;
                        for (int a = 0; a < 9 && flat; a++)
                        {
                            if (a == 4)
                                continue;
                            var q = new Vector3(c.x + (a % 3 - 1) * (frame - 0.02f), p.y + 0.3f, c.z + (a / 3 - 1) * (frame - 0.02f));
                            flat = Physics.Raycast(q, Vector3.up, out var h2, CeilingMax + 0.3f, ~0, QueryTriggerInteraction.Ignore) && Mathf.Abs(h2.point.y - y) < 0.04f && h2.normal.y < -0.95f;
                        }
                        if (!flat || decor.AnyBox(new Vector3(c.x, y, c.z), frame + 0.03f, y - 0.3f, y - 0.012f))
                        {
                            if (why != null) Count(why, flat ? "천장 장식" : "천장 고르지 않음");
                            continue;
                        }
                        bool topmost = true;
                        foreach (int m in graph.Column(c.x, c.z))
                        {
                            if (graph.Reached(m) && graph[m].y > y)
                                topmost = false;
                        }
                        if (!topmost)
                        {
                            if (why != null) Count(why, "위층 있음");
                            continue;
                        }
                        best = score;
                        hatch = new Vector3(c.x, y, c.z);
                        under = p;
                        scale = size;
                    }
                }
                if (best < float.MaxValue)
                    break;
            }
            return best < float.MaxValue;
        }

        /// <summary>몸 하나 설 바닥 (가운데 + 0.35 m 네 곳이 ±0.1 · 거의 수평) — 천장 해치 밑 · 바닥 해치 셋째 규칙 (판 0.75 m 평평까지는 안 봄).</summary>
        private static bool Standable(Vector3 p)
        {
            for (int a = 0; a < 5; a++)
            {
                var d = a == 0 ? Vector3.zero : Quaternion.Euler(0f, a * 90f, 0f) * Vector3.forward * 0.35f;
                if (!Physics.Raycast(p + d + Vector3.up * 0.5f, Vector3.down, out var hit, 1.0f, ~0, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.9f || Mathf.Abs(hit.point.y - p.y) > 0.1f)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 바닥 해치 (모든 면 연결 ③ 아랫면, 2026-11-19): 칸 안 걸어서 닿는 가장 낮은 바닥 (그 기둥에 더 낮은 마디 없음 · 칸 밑면 위) 중 칸 가운데에 가까운 자리 —
        /// 틀 (1.9 m) 아래가 고르고 (9점 ±0.04) 틀 위 1.8 m에 충돌체 · 장식이 없고, 문 · 해치 · 주민 · 판에서 떨어짐 (천장 해치와 같은 거리 규칙). floor = 해치 윗면 가운데.
        /// </summary>
        private static bool TryFindFloorHatch(InteriorTemplate t, InteriorWalkGraph graph, DecorGrid decor, Vector3Int cell, List<Vector3> pads, out Vector3 floor, out float scale,
            Dictionary<string, int> why)
        {
            floor = default;
            scale = 1f;
            var center = Away + (Vector3)cell * InteriorGeometry.CellSize;
            float half = InteriorGeometry.CellSize * 0.5f;
            float best = float.MaxValue;
            // 그 칸 맨 아래층만 (걸어서 닿는 가장 낮은 마디 + 1.5 — 아콜로지 꼭대기 층 바닥에 아래 해치가 났었음, 시민 회관 낮은 마당 1.1 m는 같은 층)
            float lowestFloor = float.MaxValue;
            foreach (int k in CellNodes(graph, center))
                lowestFloor = Mathf.Min(lowestFloor, graph[k].y);
            // 보통 틀 먼저, 없으면 작은 틀 (좁은 복도 · 바큇살)
            foreach (float size in HatchScales)
            {
                float frame = HatchFrameHalf * size;
                for (int rule = 0; rule < Rules.Length && best == float.MaxValue; rule++)
                {
                    foreach (int k in CellNodes(graph, center))
                    {
                        var p = graph[k];
                        float score = new Vector2(p.x - center.x, p.z - center.z).magnitude + Mathf.Max(0f, p.y - (center.y + InteriorGeometry.FloorOffset)) * 2f;
                        if (score >= best || p.y < center.y - half || Mathf.Abs(p.x - center.x) > half - frame - 0.05f || Mathf.Abs(p.z - center.z) > half - frame - 0.05f)
                            continue;
                        bool lowest = p.y < lowestFloor + 1.5f;
                        foreach (int m in graph.Column(p.x, p.z))
                        {
                            if (graph[m].y < p.y - 0.3f)
                                lowest = false;
                        }
                        if (!lowest)
                        {
                            Count(why, "아래층 있음");
                            continue;
                        }
                        if (Blocked(t, p, pads, rule, forHatch: true) is string b)
                        {
                            Count(why, b);
                            continue;
                        }
                        if (rule < 2 ? !Flat(p) : !Standable(p))
                        {
                            Count(why, "바닥X");
                            continue;
                        }
                        bool flat = true;
                        for (int a = 0; a < 9 && flat; a++)
                        {
                            var q = new Vector3(p.x + (a % 3 - 1) * (frame - 0.02f), p.y + 0.3f, p.z + (a / 3 - 1) * (frame - 0.02f));
                            flat = Physics.Raycast(q, Vector3.down, out var h2, 0.6f, ~0, QueryTriggerInteraction.Ignore) && Mathf.Abs(h2.point.y - p.y) < 0.04f && h2.normal.y > 0.95f;
                        }
                        if (!flat)
                        {
                            Count(why, "틀 고르지 않음");
                            continue;
                        }
                        if (Physics.CheckBox(p + Vector3.up * 0.95f, new Vector3(frame, 0.85f, frame), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                        {
                            Count(why, "틀 위 막힘");
                            continue;
                        }
                        if (decor.AnyBox(p, frame + 0.03f, p.y + 0.02f, p.y + 1.8f))
                        {
                            Count(why, "틀 위 장식");
                            continue;
                        }
                        best = score;
                        floor = p;
                        scale = size;
                    }
                }
                if (best < float.MaxValue)
                    break;
            }
            return best < float.MaxValue;
        }

        private static bool Roomy(Vector3 p, float radius, float height)
        {
            return !Physics.CheckCapsule(p + Vector3.up * (0.3f + radius), p + Vector3.up * Mathf.Max(0.3f + radius, height - radius), radius, ~0, QueryTriggerInteraction.Ignore);
        }

        /// <summary>문 자리 · 아래 해치 · 나무 · 화분 · 주민 자리 · 다른 승강판 가까이면 그 이유 (아니면 null).</summary>
        private static string Blocked(InteriorTemplate t, Vector3 p, List<Vector3> pads, int rule, bool forHatch = false)
        {
            foreach (var s in t.Sockets)
            {
                var c = Away + (Vector3)s.Cell * InteriorGeometry.CellSize;
                if (s.Direction.y == 0)
                {
                    var door = c + (Vector3)s.Direction * s.Depth;
                    if (Flat2(door, p) < Rules[rule].Door && Mathf.Abs(c.y - 2f - p.y) < 2.5f)
                        return "문";
                }
                else if (s.Direction.y < 0)
                {
                    var hatch = c + s.Offset + Vector3.down * s.Depth;
                    if ((Flat2(hatch, p) < Rules[rule].Hatch || FrameGap(hatch, s.HatchScale, p) < Rules[rule].Frame + PadRadius) && Mathf.Abs(hatch.y - p.y) < 1.5f)
                        return "해치";
                }
            }
            foreach (var plant in t.PlantSpots)
            {
                bool tree = plant.Kind == PlantKind.Tree || plant.Kind == PlantKind.Pine || plant.Kind == PlantKind.Blossom;
                float clear = rule >= 2 ? (tree ? 1.3f * plant.Scale + 0.4f : 0.8f) : tree ? 1.3f * plant.Scale + PadRadius : 0.5f + PadRadius;
                var q = Away + plant.Position;
                if (Flat2(q, p) < clear && p.y < q.y + (tree ? 6f : 1.5f) && p.y > q.y - 3f)
                    return "식물";
            }
            // 셋째 규칙 + 주민 자리를 굽는 방 (고정 자리 표 없음 — 기본 방): 주민 자리를 안 봄 → SetAll 끝에 그 방 주민을 다시 구움 (주민이 해치 · 판을 비켜 섬)
            bool rebake = rule >= 2 && !HasFixedResidents(t);
            foreach (var r in t.ResidentSpots)
            {
                var q = Away + r.Position;
                if (!rebake && Flat2(q, p) < Rules[rule].Resident + PadRadius && Mathf.Abs(q.y - p.y) < 1.5f)
                    return "주민";
            }
            // 해치 셋째 규칙: 서는 자리끼리 1.5 m (농장 가운데 통로 천장 — 칸마다 해치 둘이 나란히)
            float gap = forHatch && rule >= 2 ? 1.5f : PadRadius * 2f + PadGap;
            foreach (var other in pads)
            {
                if (Flat2(other, p) < gap)
                    return "다른판";
            }
            return null;
        }

        private static float Flat2(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        private static readonly Dictionary<string, bool> _fixedResidents = new Dictionary<string, bool>();

        /// <summary>주민 자리가 고정 자리 표 (<see cref="ResidentSpotBaker"/>)에서 오는 방 — 진화 방 전부 · 행정실 등. 아니면 자리를 찾아 구움 (다시 구우면 해치를 비킴).</summary>
        public static bool HasFixedResidents(InteriorTemplate t)
        {
            if (!_fixedResidents.TryGetValue(t.name, out bool f))
                _fixedResidents[t.name] = f = ResidentSpotBaker.FixedSpotPositions(t.name.Replace("IT_", "")).Count > 0;
            return f;
        }

        /// <summary>주민 자리가 위 · 아래 길 (해치 · 판) 1 m 안에 있는 자동 주민 방 — SetAll 끝에 다시 구움.</summary>
        private static bool ResidentsOnWays(InteriorTemplate t)
        {
            if (HasFixedResidents(t))
                return false;
            foreach (var s in t.Sockets)
            {
                if (s.Direction.y == 0)
                    continue;
                var c = (Vector3)s.Cell * InteriorGeometry.CellSize + s.Offset;
                foreach (var r in t.ResidentSpots)
                {
                    if (new Vector2(r.Position.x - c.x, r.Position.z - c.z).magnitude < 1.0f)
                        return true;
                }
            }
            return false;
        }

        /// <summary>승강판 둘레가 좁으면 그 이유 (<see cref="LiftRules"/>) — 판 테 여유 · 옆으로 지나갈 폭 · 문 앞. 아니면 null.</summary>
        private static string Tight(InteriorTemplate t, Vector3 pad, int rule)
        {
            var r = LiftRules[rule];
            if (EdgeGap(pad) < r.Gap)
                return "테여유";
            if (PassWidth(pad) < r.Width)
                return "폭";
            return DoorDistance(t, pad) < r.Door ? "문앞" : null;
        }

        /// <summary>엄한 규칙 (<see cref="LiftRules"/> 첫 줄)에 모자란 만큼 (m, 판 테 여유는 4배 — 벽에 바짝 붙은 판이 가장 눈에 띔).</summary>
        private static float Shortfall(InteriorTemplate t, Vector3 pad)
        {
            var r = LiftRules[0];
            return Mathf.Max(0f, r.Gap - EdgeGap(pad)) * 4f + Mathf.Max(0f, r.Width - PassWidth(pad)) + Mathf.Max(0f, r.Door - DoorDistance(t, pad));
        }

        /// <summary>같은 층 (높이 2.5 m 안) 옆 문 자리에서 판까지 가장 가까운 평면 거리 (없으면 99).</summary>
        private static float DoorDistance(InteriorTemplate t, Vector3 pad)
        {
            float best = 99f;
            foreach (var s in t.Sockets)
            {
                if (s.Direction.y != 0)
                    continue;
                var c = Away + (Vector3)s.Cell * InteriorGeometry.CellSize;
                var door = c + (Vector3)s.Direction * s.Depth;
                if (Mathf.Abs(c.y - 2f - pad.y) < 2.5f)
                    best = Mathf.Min(best, Flat2(door, pad));
            }
            return best;
        }

        /// <summary>판 테 (반지름 <see cref="PadRadius"/>)에서 몸 높이 (0.3 · 1.0 · 1.8 m) 충돌체까지 가장 짧은 평면 거리 (16방향, 판 안으로 들어오면 음수) — 시험도 씀.</summary>
        public static float EdgeGap(Vector3 pad)
        {
            float gap = 99f;
            foreach (float h in new[] { 0.3f, 1.0f, 1.8f })
            {
                for (int a = 0; a < 16; a++)
                {
                    var d = Quaternion.Euler(0f, a * 22.5f, 0f) * Vector3.forward;
                    if (Physics.Raycast(pad + Vector3.up * h, d, out var hit, 8f, ~0, QueryTriggerInteraction.Ignore))
                        gap = Mathf.Min(gap, hit.distance - PadRadius);
                }
            }
            return gap;
        }

        /// <summary>판 가운데 허리 높이 (0.9 m)에서 마주 보는 두 면 사이 가장 좁은 폭 (8방향, 한쪽 8 m까지) — 판 1.5 m 옆으로 사람이 지나갈 폭인지.</summary>
        public static float PassWidth(Vector3 pad)
        {
            var p = pad + Vector3.up * 0.9f;
            float width = 99f;
            for (int a = 0; a < 8; a++)
            {
                var d = Quaternion.Euler(0f, a * 22.5f, 0f) * Vector3.forward;
                float d1 = Physics.Raycast(p, d, out var h1, 8f, ~0, QueryTriggerInteraction.Ignore) ? h1.distance : 8f;
                float d2 = Physics.Raycast(p, -d, out var h2, 8f, ~0, QueryTriggerInteraction.Ignore) ? h2.distance : 8f;
                width = Mathf.Min(width, d1 + d2);
            }
            return width;
        }

        /// <summary>바닥 해치 틀 (네모 1.9 m × 배율)에서 p까지 평면 거리 (틀 안이면 0) — 시험도 씀.</summary>
        public static float FrameGap(Vector3 hatch, float scale, Vector3 p)
        {
            float half = HatchFrameHalf * scale;
            float dx = Mathf.Max(0f, Mathf.Abs(p.x - hatch.x) - half), dz = Mathf.Max(0f, Mathf.Abs(p.z - hatch.z) - half);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
