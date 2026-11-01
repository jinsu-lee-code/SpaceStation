using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 앉은 주민을 의자 모양에 맞춘다 (사용자 2026-10-29: 너무 깊이 앉아 다리가 의자 앞판 속으로 들어가거나 꼬리가 등받이 뒤로 뚫고 나옴 ·
    /// 처진 꼬리 아랫부분이 앉는 판 속으로 들어감 · 든 꼬리가 뒤통수를 뚫음).
    /// 의자 = 방 충돌체를 광선으로 재서 앉는 판 앞 · 뒤 가장자리와 등받이까지 거리, 몸 = 자세 잡은 메시를 구워 앉는 면 아래로 늘어진 다리의 가장 뒤 ·
    /// 앉는 면 위 등의 가장 뒤. 꼬리는 드는 각마다 꼬리 본을 실제로 돌려 (실행 중 기분만큼 처지는 각 포함) 메시를 구워 잼 —
    /// 앉는 판 위에서 앉는 면 아래로 내려가지 않고 머리(타원체)에 들어가지 않는 가장 작은 각 (<see cref="ResidentMotion.SeatTailLift"/>).
    /// 다리가 앞 가장자리 앞에 · 꼬리 · 등이 등받이 앞에 오도록 앞으로만 옮기며, 엉덩이는 앉는 판 위에 남김.
    /// 앉는 판을 못 찾으면 (충돌체 없는 소품) 그대로 둔다. 주민을 놓을 때 한 번만 (<see cref="InteriorResidents"/>).
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
        private const float TailMoved = 0.02f;    // 꼬리 시험 회전(30°)에 이만큼 넘게 움직인 꼭짓점만 드는 각 판단에 씀
        private const float BackrestBelow = 0.25f; // 앉는 면에서 이만큼 아래까지는 꼬리도 등받이 앞이어야 함
        private static readonly List<(float lift, float roll)> Candidates = MakeCandidates();

        /// <summary>꼬리 후보 (드는 각 0 ~ 90 · 10°마다 × 옆으로 기울임 0 · ±25 · ±45) — 덜 돌리는 것부터.</summary>
        private static List<(float, float)> MakeCandidates()
        {
            var list = new List<(float, float)>();
            for (float lift = 0f; lift <= 90f; lift += 10f)
            {
                foreach (float roll in new[] { 0f, 25f, -25f, 45f, -45f })
                    list.Add((lift, roll));
            }
            list.Sort((a, b) => (a.Item1 + Mathf.Abs(a.Item2)).CompareTo(b.Item1 + Mathf.Abs(b.Item2)));
            return list;
        }
        // 머리 타원체 (동물 모델 공통 — animal_builder HEAD_C 0.71 · 반지름 0.20 / 0.185 / 0.18, 머리 본 0.54): 머리 본에서 위로 0.17, 반지름은 조금 크게
        private const float HeadUp = 0.17f;
        private static readonly Vector3 HeadRadius = new Vector3(0.22f, 0.21f, 0.21f);

        private static readonly RaycastHit[] Hits = new RaycastHit[16];
        private static readonly List<Vector3> Rest = new List<Vector3>();
        private static readonly List<Vector3> Posed = new List<Vector3>();
        private static readonly List<int> TailIdx = new List<int>();
        private static Mesh _baked;

        /// <summary>앞으로 옮긴 거리 (안 옮겼으면 0). seatHeight = 뿌리에서 앉는 면까지 높이 (InteriorResidents의 SitDrop), scale = 동물 크기.
        /// 기분(꼬리 처짐)이 정해진 뒤 (<see cref="ResidentFigure.Configure"/> 다음) 부른다.</summary>
        public static float Fit(Transform root, float seatHeight, float scale = 1f)
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
            float droop = motion != null ? motion.TailDroop : 0f;

            // 쉬는 자세 메시 + 꼬리 꼭짓점 (꼬리 본을 30° 돌렸을 때 TailMoved 넘게 움직인 꼭짓점 — 엉덩이에 섞인 꼬리 뿌리는
            //   조금만 움직여 어느 각으로도 앉는 면 위로 못 올라오므로 빼야 함. 넣었더니 모든 각이 탈락해 꼬리를 하나도 안 들었음)
            Bake(root, Rest);
            if (Rest.Count == 0)
                return 0f;
            TailIdx.Clear();
            var isTail = new HashSet<int>();
            Quaternion tailRest = tail != null ? tail.rotation : Quaternion.identity;
            if (tail != null)
            {
                tail.rotation = Quaternion.AngleAxis(30f, root.right) * tailRest;
                Bake(root, Posed);
                tail.rotation = tailRest;
                for (int i = 0; i < Rest.Count && i < Posed.Count; i++)
                {
                    float d2 = (Posed[i] - Rest[i]).sqrMagnitude;
                    if (d2 > 1e-6f)
                        isTail.Add(i);   // 몸 재기에서 뺌
                    if (d2 > TailMoved * TailMoved * scale * scale)
                        TailIdx.Add(i);
                }
            }

            // 다리 (앉는 면 아래로 늘어진 정강이 · 발 — 둥근 엉덩이가 방석 속으로 조금 내려간 곳은 뺌) · 등 (꼬리 뺀 몸)
            float legBack = float.MaxValue, bodyBack = float.MinValue;
            for (int i = 0; i < Rest.Count; i++)
            {
                if (isTail.Contains(i))
                    continue;
                var p = Rest[i];
                if (p.y < seatHeight - LegBelow)
                {
                    if (p.z > -0.05f)
                        legBack = Mathf.Min(legBack, p.z);
                }
                else if (p.y > seatHeight - 0.04f && p.y < seatHeight + 0.5f)
                    bodyBack = Mathf.Max(bodyBack, -p.z);
            }
            float maxShift = Mathf.Max(0f, front - HipOnSeat);
            float baseShift = legBack < front + LegClear ? front + LegClear - legBack : 0f;
            if (back < float.MaxValue && bodyBack > float.MinValue)
                baseShift = Mathf.Max(baseShift, bodyBack + TailClear - back);

            float bestLift = 0f, bestRoll = 0f, bestShift = baseShift;
            if (tail != null && TailIdx.Count > 0)
            {
                // 머리 타원체 (뿌리 기준)
                var headBone = FindBone(root, "Head");
                Vector3 headC = headBone != null ? root.InverseTransformPoint(headBone.position) + Vector3.up * (HeadUp * scale) : new Vector3(0f, 0.71f * scale, 0f);
                var headR = HeadRadius * scale;
                // 후보 = 드는 각 × 옆으로 기울이는 각 (작게 돌리는 것부터). 맞는 첫 후보를 씀 — 판 위에서 앉는 면 아래로 안 내려가고 ·
                //   머리에 안 들어가고 · 등받이 높이에서 등받이 앞 (옮김은 엉덩이 한도 안). 없으면 벌점이 가장 작은 후보 (머리 > 판 > 등받이 순으로 무겁게)
                float bestPenalty = float.MaxValue;
                foreach (var (lift, roll) in Candidates)
                {
                    tail.rotation = Quaternion.AngleAxis(roll, root.forward) * Quaternion.AngleAxis(lift - droop, root.right) * tailRest;
                    Bake(root, Posed);
                    float tailBack = float.MinValue;
                    bool inHead = false;
                    foreach (int i in TailIdx)
                    {
                        var p = Posed[i];
                        if (p.y > seatHeight - BackrestBelow)   // 등받이 높이 (아래로 늘어진 꼬리도 등받이 아래를 지나 뒤로 가야 함)
                            tailBack = Mathf.Max(tailBack, -p.z);
                        var d = p - headC;
                        if ((d.x * d.x) / (headR.x * headR.x) + (d.y * d.y) / (headR.y * headR.y) + (d.z * d.z) / (headR.z * headR.z) < 1f)
                            inHead = true;
                    }
                    float shift = baseShift;
                    if (back < float.MaxValue && tailBack > float.MinValue)
                        shift = Mathf.Max(shift, tailBack + TailClear - back);
                    // 옮긴 뒤 판 위에 있는 꼬리 꼭짓점이 앉는 면 아래로 내려간 깊이
                    float over = rear + Mathf.Min(shift, maxShift);
                    float dip = 0f;
                    foreach (int i in TailIdx)
                    {
                        var p = Posed[i];
                        if (-p.z < over)
                            dip = Mathf.Max(dip, seatHeight + TailAbove - p.y);
                    }
                    float excess = Mathf.Max(0f, shift - maxShift);
                    if (!inHead && dip <= 0f && excess <= 0f)
                    {
                        bestLift = lift;
                        bestRoll = roll;
                        bestShift = shift;
                        break;
                    }
                    float penalty = (inHead ? 10f : 0f) + dip * 20f + excess * 5f + (lift + Mathf.Abs(roll)) * 0.001f;
                    if (penalty < bestPenalty)
                    {
                        bestPenalty = penalty;
                        bestLift = lift;
                        bestRoll = roll;
                        bestShift = shift;
                    }
                }
                tail.rotation = tailRest;
            }
            float moved = Mathf.Clamp(bestShift, 0f, maxShift);
            if (moved > 0.001f)
                root.position += fwd * moved;
            if (motion != null)
            {
                motion.SeatTailLift = bestLift;
                motion.SeatTailRoll = bestRoll;
            }
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

        /// <summary>지금 본 자세로 메시를 구워 뿌리 기준 (앞 = +z) 꼭짓점 목록 (렌더러 순서대로 이어 붙임).</summary>
        private static void Bake(Transform root, List<Vector3> into)
        {
            into.Clear();
            if (_baked == null)
                _baked = new Mesh();
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                smr.BakeMesh(_baked);
                var toRoot = root.worldToLocalMatrix * smr.transform.localToWorldMatrix;
                _baked.GetVertices(Scratch);
                foreach (var v in Scratch)
                    into.Add(toRoot.MultiplyPoint3x4(v));
            }
        }

        private static readonly List<Vector3> Scratch = new List<Vector3>();

        private static Transform FindBone(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>())
            {
                if (t.name == name)
                    return t;
            }
            return null;
        }
    }
}
