using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEngine;

namespace SpaceStation.Editor.Balance
{
    /// <summary>
    /// CONNECT_1121 1번 광맥 (2026-11-27): 봇이 광맥까지 가지를 뻗는다 — 사람처럼 "가까운 광맥에 구조 프레임으로 길을 내고 끝에 채굴 도킹".
    /// 산소 · 물 · 식량이 급하지 않으면 (정비비 저축보다 먼저 — 금속이 모자랄수록 수입부터). 도킹 한도가 찼으면 가장 많이 고갈된 제자리 도킹을 광맥으로 옮긴다
    /// (CONNECT M4: 한도 = 광맥에 보낼 도킹과 제자리에 둘 도킹을 나누기). 제자리 봇(칸 고정)은 뻗지 않는다.
    /// 길 = 정거장에서 광맥 옆 도킹 자리의 뒷칸까지 빈 칸을 따라 (기존 도킹 접근로 · 광맥 칸은 피함, 위아래 포함) — 너무 멀면 안 감.
    /// </summary>
    public sealed partial class BalanceBot
    {
        /// <summary>비교 측정용 스위치 (끄면 광맥이 있어도 제자리 도킹만).</summary>
        public static bool ReachVeins = true;

        private const int MaxVeinPath = 14;          // 프레임 이보다 많이 깔아야 하면 안 감
        private const int DocksPerVein = 2;          // 광맥 하나에 붙일 도킹 수
        private const float VeinRetrySeconds = 30f;  // 길을 못 찾으면 다시 볼 때까지 (게임 초)

        private static readonly Vector3Int[] FaceDirs =
        {
            Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1),
        };
        private static readonly Vector3Int[] FlatDirs = { Vector3Int.right, Vector3Int.left, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) };

        private ModuleData _frame;
        private bool _frameLooked;
        private ExploreSite _veinTarget;
        private readonly List<Vector3Int> _veinPath = new List<Vector3Int>();
        private int _veinStep;
        private Vector3Int _veinDock;
        private int _veinRotation;
        private float _veinRetryAt;
        private bool _reconnecting;

        public int VeinFrames { get; private set; }
        public int VeinDocksBuilt { get; private set; }
        public int VeinDocksMoved { get; private set; }
        /// <summary>끊긴 가지를 다시 이은 횟수.</summary>
        public int VeinReconnects { get; private set; }

        /// <summary>광맥 쪽으로 한 걸음 (프레임 하나 · 도킹 · 옮기기). 무언가 했으면 true (이번 결정 끝).</summary>
        private bool VeinStep()
        {
            if (!ReachVeins || Holding || _metal == null)
                return false;
            var veins = _sim.GetSystem<OreVeinSystem>();
            if (veins == null || !veins.Active || veins.Exploration == null)
                return false;
            var r = _sim.Resources;
            if (LifeUrgent(r))
                return false; // 산소 · 물 · 식량이 줄면서 바닥에 가까우면 그쪽 먼저
            // 시험 판 (시드 1): '생존 자원 · 밤 전력이 안정적일 때만'으로 막았더니 고갈로 살림이 흔들리자 광맥에 못 가 금속이 더 줄어 게임 오버
            // → 정말 급할 때만 미룸 (옮기기 · 프레임은 금속 10씩이라 수입이 먼저)
            if (!_reconnecting && _veinTarget != null && (_veinTarget.Reserve <= 0f || DocksOn(veins, _veinTarget) >= DocksPerVein))
                ClearVeinPlan();
            if (_veinTarget == null && !_reconnecting)
            {
                if (_sim.ElapsedSeconds < _veinRetryAt)
                    return false;
                // 끊긴 가지부터 다시 잇기 (운석이 길 프레임을 부수면 끝쪽 도킹이 멈춤 — 시험 판: 광맥 매장량 3,582를 두고 도킹 0)
                if (!PlanReconnect() && !PlanVein(veins))
                    return false;
            }

            if (_veinStep < _veinPath.Count)
            {
                var cell = _veinPath[_veinStep];
                if (!_sim.CanAfford(_frame))
                {
                    LastNeed = "vein-save";
                    return false;
                }
                if (_sim.Grid.IsOccupied(cell) || _sim.EvaluatePlacement(_frame, cell, 0) != PlacementResult.Valid
                    || !_sim.TryPlace(_frame, cell, 0, out _))
                {
                    RetryVeinLater();
                    return false;
                }
                _veinStep++;
                VeinFrames++;
                ModulesBuilt++;
                LastNeed = "vein-frame";
                return true;
            }

            if (_reconnecting)
            {
                VeinReconnects++;
                ClearVeinPlan();
                return false; // 다 이음 — 이번 결정은 다른 규칙으로
            }
            if (PowerDemandOf(_metal) > PowerSurplus())
                return false; // 발전 먼저 (다음 결정에서 발전 규칙이 처리)
            if (CanBuildMetal())
            {
                if (!_sim.CanAfford(_metal))
                {
                    LastNeed = "vein-save";
                    return false;
                }
                if (_sim.EvaluatePlacement(_metal, _veinDock, _veinRotation) == PlacementResult.Valid
                    && _sim.TryPlace(_metal, _veinDock, _veinRotation, out _))
                {
                    VeinDocksBuilt++;
                    ModulesBuilt++;
                    LastNeed = "vein-dock";
                    ClearVeinPlan();
                    return true;
                }
                RetryVeinLater();
                return false;
            }

            // 한도가 찼음: 가장 많이 고갈된 제자리 도킹을 광맥으로 옮김 (광맥 배율이 늘 더 큼)
            ModuleInstance worst = null;
            float worstScale = float.MaxValue;
            foreach (var m in _sim.Grid.Modules)
            {
                if (m.Data != _metal || veins.VeinOf(m) != null || _sim.CheckMovable(m) != MoveBlock.None)
                    continue;
                float s = veins.InPlaceScale(m);
                if (s < worstScale)
                {
                    worst = m;
                    worstScale = s;
                }
            }
            if (worst == null)
            {
                RetryVeinLater();
                return false;
            }
            var check = _sim.EvaluateMove(worst, _veinDock, _veinRotation);
            if (check == PlacementResult.InsufficientResources)
            {
                LastNeed = "vein-save";
                return false;
            }
            if (check == PlacementResult.Valid && _sim.TryMove(worst, _veinDock, _veinRotation))
            {
                VeinDocksMoved++;
                LastNeed = "vein-move";
                ClearVeinPlan();
                return true;
            }
            RetryVeinLater();
            return false;
        }

        /// <summary>산소 · 물 · 식량 중 하나가 줄고 있고 재고가 한도의 30% 아래.</summary>
        private bool LifeUrgent(ResourceSimulation r)
        {
            foreach (var t in new[] { ResourceType.Oxygen, ResourceType.Water, ResourceType.Food })
                if (r.GetNetRate(t) < 0f && r.GetStock(t) < r.GetCapacity(t) * 0.3f)
                    return true;
            return false;
        }

        private int DocksOn(OreVeinSystem veins, ExploreSite site)
        {
            int n = 0;
            foreach (var m in _sim.Grid.Modules)
                if (m.Data == _metal && veins.VeinOf(m) == site)
                    n++;
            return n;
        }

        private void ClearVeinPlan()
        {
            _veinTarget = null;
            _reconnecting = false;
            _veinPath.Clear();
            _veinStep = 0;
        }

        private void RetryVeinLater()
        {
            ClearVeinPlan();
            _veinRetryAt = _sim.ElapsedSeconds + VeinRetrySeconds;
        }

        /// <summary>가까운 광맥부터 길을 찾아 계획을 세움.</summary>
        private bool PlanVein(OreVeinSystem veins)
        {
            if (!_frameLooked)
            {
                _frameLooked = true;
                foreach (var m in _buildable)
                    if (m != null && m.name == "MD_StructureFrame")
                        _frame = m;
            }
            if (_frame == null || _sim.CheckBuildable(_frame) != PlacementResult.Valid)
                return false;
            var explore = veins.Exploration;
            var order = new List<(ExploreSite site, int dist)>();
            foreach (var site in explore.Sites)
            {
                if (site.Kind != SiteKind.Asteroid || site.Reserve <= 0f || DocksOn(veins, site) >= DocksPerVein)
                    continue;
                int d = explore.Distance(site);
                if (d <= MaxVeinPath + 2)
                    order.Add((site, d));
            }
            order.Sort((a, b) => a.dist.CompareTo(b.dist));
            foreach (var (site, _) in order)
            {
                if (FindVeinPath(site))
                {
                    _veinTarget = site;
                    _veinStep = 0;
                    return true;
                }
            }
            _veinRetryAt = _sim.ElapsedSeconds + VeinRetrySeconds;
            return false;
        }

        /// <summary>끊긴(코어와 이어지지 않은) 채굴 도킹이 있으면, 끊긴 쪽 모듈 옆 칸까지 프레임 길을 찾아 다시 잇는 계획.</summary>
        private bool PlanReconnect()
        {
            if (!_frameLooked || _frame == null || _sim.CheckBuildable(_frame) != PlacementResult.Valid)
                return false; // 아직 광맥 길을 낸 적 없음
            var cut = new HashSet<Vector3Int>();
            bool cutDock = false;
            foreach (var m in _sim.Grid.Modules)
            {
                if (m.Data == null || _sim.Connectivity.IsActive(m))
                    continue;
                foreach (var c in m.Cells)
                    cut.Add(c);
                if (m.Data == _metal)
                    cutDock = true;
            }
            if (!cutDock)
                return false;
            var blocked = new HashSet<Vector3Int>();
            var veins = _sim.GetSystem<OreVeinSystem>();
            if (veins?.Exploration != null)
                foreach (var site in veins.Exploration.Sites)
                    blocked.Add(site.Cell);
            Search(blocked, out var parent, out var depth);
            int best = int.MaxValue;
            Vector3Int end = default;
            foreach (var kv in depth)
            {
                if (kv.Value >= best)
                    continue;
                foreach (var f in FaceDirs)
                {
                    if (!cut.Contains(kv.Key + f))
                        continue;
                    best = kv.Value;
                    end = kv.Key;
                    break;
                }
            }
            if (best == int.MaxValue)
                return false;
            _veinPath.Clear();
            _veinPath.AddRange(Trace(parent, end));
            _veinStep = 0;
            _reconnecting = true;
            return true;
        }

        /// <summary>정거장(코어에 이어진 모듈) 옆 빈 칸에서 시작하는 너비 우선 탐색 (깊이 = 깔 프레임 수). 도킹 접근로 · 막은 칸은 피함.</summary>
        private void Search(HashSet<Vector3Int> blocked, out Dictionary<Vector3Int, Vector3Int> parent, out Dictionary<Vector3Int, int> depth)
        {
            var lanes = new HashSet<Vector3Int>();
            Vector3Int min = Vector3Int.zero, max = Vector3Int.zero;
            bool first = true;
            foreach (var m in _sim.Grid.Modules)
            {
                foreach (var c in m.Cells)
                {
                    min = first ? c : Vector3Int.Min(min, c);
                    max = first ? c : Vector3Int.Max(max, c);
                    first = false;
                }
                if (m.Data != null && m.Data.TerminalOnly)
                {
                    var front = PlacementRules.DockFrontWorld(m.Data, m.Rotation);
                    foreach (var c in m.Cells)
                        for (int k = 1; k <= m.Data.ApproachLaneLength; k++)
                            lanes.Add(c + front * k);
                }
            }
            foreach (var b in blocked)
            {
                min = Vector3Int.Min(min, b);
                max = Vector3Int.Max(max, b);
            }
            min -= Vector3Int.one * 3;
            max += Vector3Int.one * 3;
            bool Free(Vector3Int c) => c.x >= min.x && c.y >= min.y && c.z >= min.z && c.x <= max.x && c.y <= max.y && c.z <= max.z
                                       && !blocked.Contains(c) && !lanes.Contains(c) && !_sim.Grid.IsOccupied(c);
            parent = new Dictionary<Vector3Int, Vector3Int>();
            depth = new Dictionary<Vector3Int, int>();
            var queue = new Queue<Vector3Int>();
            foreach (var m in _sim.Grid.Modules)
            {
                if (!_sim.Connectivity.IsActive(m))
                    continue;
                foreach (var c in m.Cells)
                    foreach (var f in FaceDirs)
                    {
                        var n = c + f;
                        if (depth.ContainsKey(n) || !Free(n))
                            continue;
                        depth[n] = 1;
                        parent[n] = n;
                        queue.Enqueue(n);
                    }
            }
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                int dd = depth[c];
                if (dd >= MaxVeinPath)
                    continue;
                foreach (var f in FaceDirs)
                {
                    var n = c + f;
                    if (depth.ContainsKey(n) || !Free(n))
                        continue;
                    depth[n] = dd + 1;
                    parent[n] = c;
                    queue.Enqueue(n);
                }
            }
        }

        private static List<Vector3Int> Trace(Dictionary<Vector3Int, Vector3Int> parent, Vector3Int end)
        {
            var path = new List<Vector3Int>();
            var c = end;
            while (true)
            {
                path.Add(c);
                var p = parent[c];
                if (p == c)
                    break;
                c = p;
            }
            path.Reverse();
            return path;
        }

        /// <summary>
        /// 광맥 옆 도킹 자리(D, 앞쪽 h)와 그 뒷칸(B = D − h)까지의 프레임 길을 찾음. B에 이미 정거장 모듈이 있으면 길 없이 바로 도킹.
        /// </summary>
        private bool FindVeinPath(ExploreSite site)
        {
            var vein = site.Cell;
            var lanes = new HashSet<Vector3Int>();
            Vector3Int min = vein, max = vein;
            foreach (var m in _sim.Grid.Modules)
            {
                foreach (var c in m.Cells)
                {
                    min = Vector3Int.Min(min, c);
                    max = Vector3Int.Max(max, c);
                }
                if (m.Data != null && m.Data.TerminalOnly)
                {
                    var front = PlacementRules.DockFrontWorld(m.Data, m.Rotation);
                    foreach (var c in m.Cells)
                        for (int k = 1; k <= m.Data.ApproachLaneLength; k++)
                            lanes.Add(c + front * k);
                }
            }
            min -= Vector3Int.one * 3;
            max += Vector3Int.one * 3;
            bool InBox(Vector3Int c) => c.x >= min.x && c.y >= min.y && c.z >= min.z && c.x <= max.x && c.y <= max.y && c.z <= max.z;
            bool Free(Vector3Int c) => InBox(c) && c != vein && !lanes.Contains(c) && !_sim.Grid.IsOccupied(c);

            // 후보: D = 광맥 옆 빈 칸, 앞쪽 h = 가로 4방향 (도킹은 가로로만 돎), 접근로 2칸 비어 있음
            var candidates = new List<(Vector3Int dock, Vector3Int back, Vector3Int front, int rotation)>();
            foreach (var f in FaceDirs)
            {
                var d = vein + f;
                if (!Free(d))
                    continue;
                foreach (var h in FlatDirs)
                {
                    var back = d - h;
                    if (back == vein || _sim.Grid.IsOccupied(d + h) || _sim.Grid.IsOccupied(d + h * 2))
                        continue;
                    int rot = -1;
                    for (int r = 0; r < 4; r++)
                        if (GridDirections.Rotate(_metal.DockFront, r) == h)
                            rot = r;
                    if (rot < 0)
                        continue;
                    candidates.Add((d, back, h, rot));
                }
            }
            if (candidates.Count == 0)
                return false;

            // 정거장 옆 빈 칸에서 시작하는 너비 우선 탐색 (깊이 = 깔 프레임 수)
            var parent = new Dictionary<Vector3Int, Vector3Int>();
            var depth = new Dictionary<Vector3Int, int>();
            var queue = new Queue<Vector3Int>();
            foreach (var m in _sim.Grid.Modules)
            {
                if (!_sim.Connectivity.IsActive(m))
                    continue;
                foreach (var c in m.Cells)
                    foreach (var f in FaceDirs)
                    {
                        var n = c + f;
                        if (depth.ContainsKey(n) || !Free(n))
                            continue;
                        depth[n] = 1;
                        parent[n] = n;
                        queue.Enqueue(n);
                    }
            }
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                int dd = depth[c];
                if (dd >= MaxVeinPath)
                    continue;
                foreach (var f in FaceDirs)
                {
                    var n = c + f;
                    if (depth.ContainsKey(n) || !Free(n))
                        continue;
                    depth[n] = dd + 1;
                    parent[n] = c;
                    queue.Enqueue(n);
                }
            }

            int best = int.MaxValue;
            foreach (var (dock, back, front, rotation) in candidates)
            {
                List<Vector3Int> path;
                if (_sim.Grid.TryGetModule(back, out var existing) && existing != null && _sim.Connectivity.IsActive(existing))
                {
                    path = new List<Vector3Int>(); // 정거장에 바로 붙음
                }
                else if (depth.TryGetValue(back, out int len))
                {
                    if (len >= best)
                        continue;
                    path = new List<Vector3Int>();
                    var c = back;
                    while (true)
                    {
                        path.Add(c);
                        var p = parent[c];
                        if (p == c)
                            break;
                        c = p;
                    }
                    path.Reverse();
                    bool blocks = false;
                    foreach (var pc in path)
                        if (pc == dock || pc == dock + front || pc == dock + front * 2)
                            blocks = true;
                    if (blocks)
                        continue;
                }
                else
                {
                    continue;
                }
                if (path.Count >= best)
                    continue;
                best = path.Count;
                _veinPath.Clear();
                _veinPath.AddRange(path);
                _veinDock = dock;
                _veinRotation = rotation;
            }
            return best != int.MaxValue;
        }
    }
}
