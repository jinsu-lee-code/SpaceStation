using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// CONNECT_1121 3번 ④ V4 (2026-11-28): 지금 내부의 상황 · 부탁 — 방 안 화면(허브 표지판 · 관제소 지도 · 상태 화면)이 읽는 요약.
    /// <see cref="InteriorMode"/>가 매 프레임 채운다 (안에 있을 때만, 나가면 비움).
    /// </summary>
    public static class InteriorSituations
    {
        /// <summary>진행 중인 사고의 방 · 제목 (없으면 null).</summary>
        public static ModuleInstance IncidentRoom { get; private set; }
        public static string IncidentTitle { get; private set; }
        /// <summary>아직 안 들어준 첫 부탁의 방 · 제목 (없으면 null).</summary>
        public static ModuleInstance RequestRoom { get; private set; }
        public static string RequestTitle { get; private set; }

        private static InteriorBuilder _builder;
        private static StationSimulation _sim;
        private static readonly List<InnerHit> Hits = new List<InnerHit>();

        internal static void Set(InteriorBuilder builder, StationSimulation sim, ModuleInstance incidentRoom, string incidentTitle, ModuleInstance requestRoom, string requestTitle)
        {
            _builder = builder;
            _sim = sim;
            IncidentRoom = incidentRoom;
            IncidentTitle = incidentTitle;
            RequestRoom = requestRoom;
            RequestTitle = requestTitle;
        }

        internal static void Clear()
        {
            IncidentRoom = RequestRoom = null;
            IncidentTitle = RequestTitle = null;
        }

        /// <summary>from 방에서 target 방으로 가는 첫 이웃 방 (열린 통로 기준, 길이 없으면 null).</summary>
        public static ModuleInstance NextStep(ModuleInstance from, ModuleInstance target)
        {
            if (_builder == null || from == null || target == null || from == target)
                return null;
            var parent = _builder.RoomParent(from);
            var near = parent != null ? parent.position : Vector3.zero;
            return _builder.TryGetWayPoint(from, target, near, out _, out var next, out _) ? next : null;
        }

        /// <summary>그 방이 그 안쪽 얼굴을 받는지 (인접 관계 — CONNECT A5).</summary>
        public static bool HasInner(ModuleInstance room, InnerFace face)
        {
            if (_sim == null || _sim.Adjacency == null || room == null)
                return false;
            _sim.Adjacency.InnerFor(_sim.Grid, room, Hits);
            foreach (var h in Hits)
            {
                if ((h.Face & face) != 0)
                    return true;
            }
            return false;
        }
    }
}
