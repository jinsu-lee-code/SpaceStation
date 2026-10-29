using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 앉은 주민을 의자 모양에 맞춰 앞뒤로 옮긴다 (사용자 2026-10-29: 너무 깊이 앉아 다리가 의자 앞판 속으로 들어가거나 꼬리가 등받이 뒤로 뚫고 나옴).
    /// 의자 = 방 충돌체를 광선으로 재서 앉는 판 앞 가장자리 · 등받이까지 거리, 몸 = 자세 잡은 메시를 구워 앉는 면 아래로 늘어진 다리의 가장 뒤 ·
    /// 앉는 면 위 꼬리 · 등의 가장 뒤. 다리가 앞 가장자리보다 앞에, 꼬리가 등받이보다 앞에 오도록 앞으로만 옮기고, 엉덩이는 앉는 판 위에 남김.
    /// 앉는 판을 못 찾으면 (충돌체 없는 소품) 그대로 둔다. 주민을 놓을 때 한 번만 (<see cref="InteriorResidents"/>).
    /// </summary>
    public static class ResidentSeatFit
    {
        private const float Step = 0.02f;
        private const float MaxDepth = 0.9f;
        private const float SeatDrop = 0.12f;     // 앉는 면에서 이만큼 아래까지 맞은 면은 앉는 판으로 봄 (방석 부풂 · 받침)
        private const float LegClear = 0.025f;    // 다리 뒤 ↔ 앞 가장자리 여유
        private const float TailClear = 0.04f;    // 꼬리 · 등 ↔ 등받이 여유 (꼬리 흔들림 포함)
        private const float HipOnSeat = 0.05f;    // 엉덩이가 앞 가장자리에서 이만큼은 안쪽
        private const float LegBelow = 0.1f;      // 앉는 면에서 이만큼 아래부터 다리로 봄

        private static readonly RaycastHit[] Hits = new RaycastHit[16];
        private static Mesh _baked;

        /// <summary>앞으로 옮긴 거리 (안 옮겼으면 0). seatHeight = 뿌리에서 앉는 면까지 높이 (InteriorResidents의 SitDrop).</summary>
        public static float Fit(Transform root, float seatHeight)
        {
            var up = root.up;
            var fwd = root.forward;
            var seat = root.position + up * seatHeight;
            Physics.SyncTransforms();
            // 앉는 판 앞 가장자리: 앞으로 가며 아래로 쏴서 앉는 면 높이 근처가 끝나는 곳
            if (!SeatBelow(seat, up))
                return 0f;
            float front = MaxDepth;
            for (float d = Step; d <= MaxDepth; d += Step)
            {
                if (!SeatBelow(seat + fwd * d, up))
                {
                    front = d;
                    break;
                }
            }
            // 등받이: 앉는 면 위 몇 높이에서 뒤로 쏜 가장 가까운 면
            float back = float.MaxValue;
            foreach (float h in new[] { 0.06f, 0.16f, 0.3f })
            {
                if (Nearest(seat + up * h, -fwd, MaxDepth, out float dist))
                    back = Mathf.Min(back, dist);
            }
            // 몸: 앉는 면 아래 다리의 가장 뒤 (앞 방향 값이 가장 작은 곳), 앉는 면 위 꼬리 · 등의 가장 뒤
            if (!Measure(root, seatHeight, out float legBack, out float bodyBack))
                return 0f;
            float shift = 0f;
            if (legBack < front + LegClear)
                shift = Mathf.Max(shift, front + LegClear - legBack);
            if (back < float.MaxValue && bodyBack + TailClear > back)
                shift = Mathf.Max(shift, bodyBack + TailClear - back);
            shift = Mathf.Min(shift, Mathf.Max(0f, front - HipOnSeat));
            if (shift > 0.001f)
                root.position += fwd * shift;
            return shift;
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

        /// <summary>자세 잡은 메시를 구워 뿌리 기준 (앞 = +z)으로 다리 · 꼬리 끝을 잼.</summary>
        private static bool Measure(Transform root, float seatHeight, out float legBack, out float bodyBack)
        {
            legBack = float.MaxValue;
            bodyBack = float.MinValue;
            if (_baked == null)
                _baked = new Mesh();
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                smr.BakeMesh(_baked);
                var toRoot = root.worldToLocalMatrix * smr.transform.localToWorldMatrix;
                foreach (var v in _baked.vertices)
                {
                    var p = toRoot.MultiplyPoint3x4(v);
                    // 다리 = 앉는 면보다 LegBelow 아래로 늘어진 정강이 · 발 (둥근 엉덩이가 방석 속으로 조금 내려간 곳은 빼야 함 —
                    //   넣었더니 엉덩이를 다리로 재서 앞 가장자리까지 0.24m나 밀려 나옴, 확인 2026-10-29)
                    if (p.y < seatHeight - LegBelow)
                    {
                        if (p.z > -0.05f)   // 뒤쪽 아래 (늘어진 꼬리 끝)는 다리가 아님
                            legBack = Mathf.Min(legBack, p.z);
                    }
                    else if (p.y > seatHeight - 0.04f && p.y < seatHeight + 0.5f)
                        bodyBack = Mathf.Max(bodyBack, -p.z);
                }
            }
            return legBack < float.MaxValue || bodyBack > float.MinValue;
        }
    }
}
