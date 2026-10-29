using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 앉은 주민을 의자 모양에 맞춘다 (사용자 2026-10-29: 너무 깊이 앉아 다리가 의자 앞판 속으로 들어가거나 꼬리가 등받이 뒤로 뚫고 나옴 ·
    /// 처진 꼬리 아랫부분이 앉는 판 속으로 들어감).
    /// 의자 = 방 충돌체를 광선으로 재서 앉는 판 앞 · 뒤 가장자리와 등받이까지 거리, 몸 = 자세 잡은 메시를 구워 앉는 면 아래로 늘어진 다리의 가장 뒤 ·
    /// 앉는 면 위 꼬리 · 등의 가장 뒤 · 꼬리 (꼬리 뿌리 뒤 꼭짓점 — 실행 중 기분만큼 처지는 각까지 넣어 잼).
    /// 꼬리를 앉는 판 위로 들고(<see cref="ResidentMotion.SeatTailLift"/>), 다리가 앞 가장자리 앞에 · 꼬리가 등받이 앞에 오도록 앞으로만 옮기며,
    /// 엉덩이는 앉는 판 위에 남김. 앉는 판을 못 찾으면 (충돌체 없는 소품) 그대로 둔다. 주민을 놓을 때 한 번만 (<see cref="InteriorResidents"/>).
    /// </summary>
    public static class ResidentSeatFit
    {
        private const float Step = 0.02f;
        private const float MaxDepth = 0.9f;
        private const float SeatDrop = 0.12f;     // 앉는 면에서 이만큼 아래까지 맞은 면은 앉는 판으로 봄 (방석 부풂 · 받침)
        private const float LegClear = 0.025f;    // 다리 뒤 ↔ 앞 가장자리 여유
        private const float TailClear = 0.04f;    // 꼬리 · 등 ↔ 등받이 여유 (꼬리 흔들림 포함)
        private const float TailAbove = 0.015f;   // 꼬리 아래 ↔ 앉는 면 여유
        private const float HipOnSeat = 0.05f;    // 엉덩이가 앞 가장자리에서 이만큼은 안쪽
        private const float LegBelow = 0.1f;      // 앉는 면에서 이만큼 아래부터 다리로 봄
        private const float LiftStep = 5f;
        private const float MaxLift = 90f;

        private static readonly RaycastHit[] Hits = new RaycastHit[16];
        private static readonly List<Vector3> Body = new List<Vector3>();
        private static readonly List<Vector3> TailPoints = new List<Vector3>();
        private static Mesh _baked;

        /// <summary>앞으로 옮긴 거리 (안 옮겼으면 0). seatHeight = 뿌리에서 앉는 면까지 높이 (InteriorResidents의 SitDrop).
        /// 기분(꼬리 처짐)이 정해진 뒤 (<see cref="ResidentFigure.Configure"/> 다음) 부른다.</summary>
        public static float Fit(Transform root, float seatHeight)
        {
            var up = root.up;
            var fwd = root.forward;
            var seat = root.position + up * seatHeight;
            Physics.SyncTransforms();
            if (!SeatBelow(seat, up))
                return 0f;
            // 앉는 판 앞 · 뒤 가장자리 (앞뒤로 가며 아래로 쏴서 앉는 면 높이 근처가 끝나는 곳)
            float front = Edge(seat, fwd, up);
            float rear = Edge(seat, -fwd, up);
            // 등받이: 앉는 면 위 몇 높이에서 뒤로 쏜 가장 가까운 면
            float back = float.MaxValue;
            foreach (float h in new[] { 0.06f, 0.16f, 0.3f })
            {
                if (Nearest(seat + up * h, -fwd, MaxDepth, out float dist))
                    back = Mathf.Min(back, dist);
            }
            var motion = root.GetComponent<ResidentFigure>()?.Motion;
            var tail = motion != null ? motion.Tail : null;
            Vector3 tailRoot = tail != null ? root.InverseTransformPoint(tail.position) : Vector3.zero;
            if (!Measure(root, tail != null, tailRoot))
                return 0f;
            float droop = motion != null ? motion.TailDroop : 0f;

            // 다리: 앉는 면 아래로 늘어진 정강이 · 발 (둥근 엉덩이가 방석 속으로 조금 내려간 곳은 빼야 함 — 넣었더니 앞 가장자리까지 밀려 나옴)
            float legBack = float.MaxValue, bodyBack = float.MinValue;
            foreach (var p in Body)
            {
                if (p.y < seatHeight - LegBelow)
                {
                    if (p.z > -0.05f)
                        legBack = Mathf.Min(legBack, p.z);
                }
                else if (p.y > seatHeight - 0.04f && p.y < seatHeight + 0.5f)
                    bodyBack = Mathf.Max(bodyBack, -p.z);
            }
            float maxShift = Mathf.Max(0f, front - HipOnSeat);
            float legShift = legBack < front + LegClear ? front + LegClear - legBack : 0f;

            // 꼬리 드는 각: 앉는 판 위에 놓이고(옮긴 만큼 판 뒤 가장자리도 멀어짐) 등받이 맞춤이 엉덩이 한도 안에 드는 가장 작은 각
            float bestLift = 0f, bestShift = float.MaxValue;
            for (float lift = 0f; lift <= MaxLift; lift += LiftStep)
            {
                var rot = Quaternion.AngleAxis(lift - droop, Vector3.right);   // 인물 오른쪽 축, +면 꼬리 끝이 위로
                float tailBack = float.MinValue;
                foreach (var p0 in TailPoints)
                    tailBack = Mathf.Max(tailBack, -(tailRoot + rot * (p0 - tailRoot)).z);
                float shift = legShift;
                if (back < float.MaxValue)
                {
                    shift = Mathf.Max(shift, bodyBack + TailClear - back);
                    if (tailBack > float.MinValue)
                        shift = Mathf.Max(shift, tailBack + TailClear - back);
                }
                // 옮긴 뒤 판 위에 있는 꼬리 꼭짓점이 앉는 면 아래로 내려가면 이 각은 안 됨
                float over = rear + Mathf.Min(shift, maxShift);
                bool onSeat = true;
                foreach (var p0 in TailPoints)
                {
                    var p = tailRoot + rot * (p0 - tailRoot);
                    if (-p.z < over && p.y < seatHeight + TailAbove)
                    {
                        onSeat = false;
                        break;
                    }
                }
                if (!onSeat)
                    continue;
                if (shift <= maxShift)
                {
                    bestLift = lift;
                    bestShift = shift;
                    break;
                }
                if (shift < bestShift)
                {
                    bestLift = lift;
                    bestShift = shift;
                }
            }
            if (bestShift == float.MaxValue)   // 어느 각으로도 판 위에 못 놓임 (아주 긴 꼬리) — 다리 · 등만 맞춤
                bestShift = back < float.MaxValue ? Mathf.Max(legShift, bodyBack + TailClear - back) : legShift;
            float moved = Mathf.Clamp(bestShift, 0f, maxShift);
            if (moved > 0.001f)
                root.position += fwd * moved;
            if (motion != null)
                motion.SeatTailLift = bestLift;
            return moved;
        }

        private static float Edge(Vector3 seat, Vector3 dir, Vector3 up)
        {
            for (float d = Step; d <= MaxDepth; d += Step)
            {
                if (!SeatBelow(seat + dir * d, up))
                    return d;
            }
            return MaxDepth;
        }

        /// <summary>p 바로 아래 (앉는 면 위 0.15 ~ 아래 SeatDrop)에 주민이 아닌 면이 있는지.</summary>
        private static bool SeatBelow(Vector3 p, Vector3 up)
        {
            return Nearest(p + up * 0.15f, -up, 0.15f + SeatDrop, out _);
        }

        private static bool Nearest(Vector3 from, Vector3 dir, float length, out float distance)
        {
            distance = float.MaxValue;
            int n = Physics.RaycastNonAlloc(from, dir, Hits, length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (Hits[i].collider.GetComponentInParent<ResidentFigure>() != null)
                    continue;
                distance = Mathf.Min(distance, Hits[i].distance);
            }
            return distance < float.MaxValue;
        }

        /// <summary>자세 잡은 메시를 구워 뿌리 기준 (앞 = +z) 꼭짓점을 몸 · 꼬리(꼬리 뿌리보다 뒤)로 나눔.</summary>
        private static bool Measure(Transform root, bool hasTail, Vector3 tailRoot)
        {
            Body.Clear();
            TailPoints.Clear();
            if (_baked == null)
                _baked = new Mesh();
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                smr.BakeMesh(_baked);
                var toRoot = root.worldToLocalMatrix * smr.transform.localToWorldMatrix;
                foreach (var v in _baked.vertices)
                {
                    var p = toRoot.MultiplyPoint3x4(v);
                    if (hasTail && p.z < tailRoot.z - 0.005f)
                        TailPoints.Add(p);
                    else
                        Body.Add(p);
                }
            }
            return Body.Count > 0;
        }
    }
}
