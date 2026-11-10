using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 진화 2단계 화물 환적장 내부 "은하수 후룸 항구" 물길 한 바퀴 (Blender ct_builder 값 — SEGS · water(s) · ramp · solve_th를 그대로 옮김).
    /// s = 정차 자리에서 잰 평면 길이 (0 ~ <see cref="Loop"/>, 닫힌 고리), 좌표는 템플릿 루트 기준. 직선 · 원호 조각 + 리프트 · 큰 낙하 비탈 (캣멀-롬 없음).
    /// 탈 때 쓰는 속도 표 (<see cref="Speed"/>)도 여기서 구움 — 런타임 <see cref="InteriorFlumeRide"/>와 EditMode 시험이 같은 것을 씀.
    /// </summary>
    public static class CargoFlumeCourse
    {
        public const float LowWater = -1.33f, HighWater = 1.40f;   // LW · YS
        public const float StationX = 11.60f, LaneZ = 8.65f;       // SX · LANE_Z
        public const float LiftChainSpeed = 1.0f;
        public const float SplashDragEnd = 39.61f;

        private struct Seg
        {
            public bool Arc;
            public Vector2 A, B;      // 직선: 시작 · 끝 / 원호: 중심
            public float R, A0, A1;   // 원호: 반지름 · 시작 · 끝 각 (x = cos, z = sin)
            public string Name;
            public float Length => Arc ? R * Mathf.Abs(A1 - A0) : Vector2.Distance(A, B);
        }

        private static Seg Line(string name, float x0, float z0, float x1, float z1) => new Seg { Name = name, A = new Vector2(x0, z0), B = new Vector2(x1, z1) };
        private static Seg Arc(string name, float cx, float cz, float r, float a0, float a1) => new Seg { Arc = true, Name = name, A = new Vector2(cx, cz), R = r, A0 = a0, A1 = a1 };

        private const float Pi = Mathf.PI, R90 = Mathf.PI * 0.5f, BZ = -0.10f;

        private static readonly Seg[] Segs =
        {
            Line("항구 출발", StationX, LaneZ, 14.9f, LaneZ),
            Arc("항구 출구 굽이", 14.9f, 5.15f, 3.5f, R90, 0f),
            Line("오른쪽 물길", 18.4f, 5.15f, 18.4f, BZ),
            Arc("회전 못", 17.2f, BZ, 1.2f, 0f, -Pi),
            Line("체인 리프트", 16.0f, BZ, 16.0f, 5.0f),
            Arc("마루 굽이", 14.2f, 5.0f, 1.8f, 0f, R90),
            Line("산 뒤 능선", 14.2f, 6.8f, 10.0f, 6.8f),
            Arc("정상 터널", 10.0f, 5.0f, 1.8f, R90, Pi),
            Line("큰 낙하!", 8.2f, 5.0f, 8.2f, -1.0f),
            Arc("물보라 못", 6.7f, -1.0f, 1.5f, 0f, -R90),
            Line("앞 물길", 6.7f, -2.5f, -0.4f, -2.5f),
            Arc("남서 굽이", -0.4f, -0.5f, 2.0f, -R90, -Pi),
            Line("왼쪽 물길", -2.4f, -0.5f, -2.4f, 6.65f),
            Arc("북서 굽이", -0.4f, 6.65f, 2.0f, Pi, R90),
            Line("항구로", -0.4f, LaneZ, StationX, LaneZ),
        };

        private static float[] _s0;
        private static float _loop;
        private static Ramp _lift, _drop;
        private static float _liftS0, _dropS0;
        private static float[] _speed;
        private const float Table = 0.05f;

        public static float Loop { get { Init(); return _loop; } }
        public static int SegmentCount => Segs.Length;
        public static float SegmentStart(int i) { Init(); return _s0[i]; }
        public static float LiftStart { get { Init(); return _liftS0; } }
        public static float LiftLength { get { Init(); return _lift.Total; } }
        public static float LiftAngle { get { Init(); return _lift.Theta * Mathf.Rad2Deg; } }
        public static float DropStart { get { Init(); return _dropS0; } }
        public static float DropLength { get { Init(); return _drop.Total; } }
        public static float TunnelIn { get { Init(); return _s0[6] + (14.2f - 10.6f); } }
        public static float TunnelOut { get { Init(); return _s0[8] + 0.30f; } }
        /// <summary>물보라 (낙하 끝 z 0.40) — 아무 보트나 지나가면 터짐.</summary>
        public static float SplashS { get { Init(); return _s0[8] + (5.0f - 0.40f); } }

        /// <summary>리프트 · 낙하 비탈 (아래 굽이 R1 · 곧은 비탈 θ · 위 굽이 R2), d = 비탈 시작에서 잰 평면 길이.</summary>
        private struct Ramp
        {
            public float Rise, R1, R2, Theta, Hb1, Hs, Hb2, Total;

            public static Ramp Make(float rise, float r1, float r2, float th)
            {
                var m = new Ramp { Rise = rise, R1 = r1, R2 = r2, Theta = th };
                float a = Mathf.Abs(rise);
                float yb1 = r1 * (1f - Mathf.Cos(th)), yb2 = r2 * (1f - Mathf.Cos(th));
                m.Hb1 = r1 * Mathf.Sin(th);
                m.Hb2 = r2 * Mathf.Sin(th);
                m.Hs = (a - yb1 - yb2) / Mathf.Tan(th);
                m.Total = m.Hb1 + m.Hs + m.Hb2;
                return m;
            }

            public float Y(float d)
            {
                float a = Mathf.Abs(Rise), sg = Rise > 0f ? 1f : -1f;
                if (d <= 0f)
                    return 0f;
                if (d < Hb1)
                    return sg * (R1 - Mathf.Sqrt(R1 * R1 - d * d));
                if (d < Hb1 + Hs)
                    return sg * (R1 * (1f - Mathf.Cos(Theta)) + (d - Hb1) * Mathf.Tan(Theta));
                if (d < Total)
                {
                    float e = Total - d;
                    return sg * (a - (R2 - Mathf.Sqrt(R2 * R2 - e * e)));
                }
                return sg * a;
            }

            public static float Solve(float rise, float r1, float r2, float run)
            {
                float lo = 5f * Mathf.Deg2Rad, hi = 70f * Mathf.Deg2Rad;
                for (int i = 0; i < 80; i++)
                {
                    float m = (lo + hi) * 0.5f;
                    if (Make(rise, r1, r2, m).Total > run)
                        lo = m;
                    else
                        hi = m;
                }
                return (lo + hi) * 0.5f;
            }
        }

        private static void Init()
        {
            if (_s0 != null)
                return;
            var s0 = new float[Segs.Length];
            float acc = 0f;
            for (int i = 0; i < Segs.Length; i++)
            {
                s0[i] = acc;
                acc += Segs[i].Length;
            }
            _loop = acc;
            _liftS0 = s0[4];
            float rise = HighWater - LowWater;
            _lift = Ramp.Make(rise, 1.8f, 1.8f, Ramp.Solve(rise, 1.8f, 1.8f, s0[5] - _liftS0));
            _dropS0 = s0[8] + 0.50f;
            _drop = Ramp.Make(-rise, 2.5f, 3.0f, 42f * Mathf.Deg2Rad);
            _s0 = s0;
            BuildSpeeds();
        }

        public static float Wrap(float s)
        {
            float l = Loop;
            s %= l;
            return s < 0f ? s + l : s;
        }

        private static int SegAt(float s)
        {
            int i = 0;
            for (int k = 0; k < Segs.Length; k++)
            {
                if (s >= _s0[k] - 1e-5f)
                    i = k;
            }
            return i;
        }

        /// <summary>평면 자리 (x, z).</summary>
        public static Vector2 Plan(float s)
        {
            Init();
            s = Wrap(s);
            int i = SegAt(s);
            float u = s - _s0[i];
            var sg = Segs[i];
            if (!sg.Arc)
                return Vector2.Lerp(sg.A, sg.B, u / sg.Length);
            float a = sg.A0 + (sg.A1 > sg.A0 ? 1f : -1f) * u / sg.R;
            return sg.A + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * sg.R;
        }

        /// <summary>물 높이.</summary>
        public static float Water(float s)
        {
            Init();
            s = Wrap(s);
            if (s >= _liftS0 && s <= _liftS0 + _lift.Total)
                return LowWater + _lift.Y(s - _liftS0);
            if (s > _liftS0 + _lift.Total && s < _dropS0)
                return HighWater;
            if (s >= _dropS0 && s <= _dropS0 + _drop.Total)
                return HighWater + _drop.Y(s - _dropS0);
            return LowWater;
        }

        public static Vector3 Point(float s)
        {
            var p = Plan(s);
            return new Vector3(p.x, Water(s), p.y);
        }

        public static bool OnChain(float s)
        {
            Init();
            s = Wrap(s);
            return s >= _liftS0 && s <= _liftS0 + _lift.Total;
        }

        public static bool OnSlope(float s)
        {
            Init();
            s = Wrap(s);
            return OnChain(s) || (s >= _dropS0 && s <= _dropS0 + _drop.Total);
        }

        /// <summary>
        /// 보트 자세 (ct_final_plan 3-3): 앞 = P(s + 1.15) − P(s − 1.15), 위 = 세계 위를 앞에 수직으로 (옆 기울기 없음),
        /// 골짜기에서는 중심을 앞뒤 점의 가운데로 올림 (선체가 물통 바닥을 파고들지 않게). 보트 로컬 +z = 앞, 원점 = 흘수선 가운데.
        /// </summary>
        public static void Pose(float s, out Vector3 position, out Quaternion rotation)
        {
            const float Half = 1.15f;
            var a = Point(s - Half);
            var b = Point(s + Half);
            position = Point(s);
            position.y = Mathf.Max(position.y, (a.y + b.y) * 0.5f);
            var fwd = b - a;
            if (fwd.sqrMagnitude < 1e-8f)
                fwd = Vector3.forward;
            rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);
        }

        /// <summary>탄 사람 안내 줄: 지금 조각 이름.</summary>
        public static string SectionName(float s)
        {
            Init();
            s = Wrap(s);
            if (s >= TunnelIn && s < TunnelOut)
                return "정상 터널";
            if (s >= SplashS && s < SplashDragEnd)
                return "물보라 못";
            return Segs[SegAt(s)].Name;
        }

        /// <summary>조각 i가 원호면 반지름, 아니면 0.</summary>
        private static float ArcRadius(float s)
        {
            var sg = Segs[SegAt(Wrap(s))];
            return sg.Arc ? sg.R : 0f;
        }

        /// <summary>목표 속도 (ct_final_plan 3-3): 출발 1.5 · 항구로 1.2 · 터널 앞부터 문까지 1.2 · 물보라 1.3 · 나머지 1.8.</summary>
        private static float Target(float s)
        {
            if (s < _s0[1])
                return 1.5f;
            if (s >= _s0[14])
                return 1.2f;
            if (s >= TunnelIn - 1f && s < TunnelOut)
                return 1.2f;
            if (s >= SplashS && s < SplashDragEnd)
                return 1.3f;
            return 1.8f;
        }

        /// <summary>
        /// 0.05 m 간격 속도 표: 앞으로 (높이 변화 v² −= 2 · 7.848 · Δy, 평지 · 물보라 = 목표로 끌림 2.5 m · 0.5 m, 느리면 0.6 m/s², 체인 1.0, 원호 v ≤ 0.8727 R),
        /// 뒤로 (평지에서만 0.8 m/s²로 미리 감속해 원호 한도에 맞춤). 정차 · 앞 보트 간격은 런타임이 따로 줄임.
        /// </summary>
        private static void BuildSpeeds()
        {
            int n = Mathf.CeilToInt(_loop / Table) + 1;
            var v = new float[n];
            float cur = 0f;
            for (int i = 0; i < n; i++)
            {
                float s = Mathf.Min(i * Table, _loop);
                if (i > 0)
                {
                    float sp = (i - 1) * Table;
                    float dy = Water(s) - Water(sp);
                    if (OnChain(s))
                        cur = LiftChainSpeed;
                    else
                    {
                        float target = Target(s);
                        bool splash = s >= SplashS && s < SplashDragEnd;
                        if (Mathf.Abs(dy) > 1e-5f)
                        {
                            cur = Mathf.Sqrt(Mathf.Max(0.04f, cur * cur - 2f * 7.848f * dy));
                            if (splash && cur > target)
                                cur = target + (cur - target) * Mathf.Exp(-Table / 0.5f);
                        }
                        else if (cur > target)
                            cur = target + (cur - target) * Mathf.Exp(-Table / (splash ? 0.5f : 2.5f));
                        else
                            cur = Mathf.Min(target, Mathf.Sqrt(cur * cur + 2f * 0.6f * Table));
                    }
                    float r = ArcRadius(s);
                    if (r > 0f)
                        cur = Mathf.Min(cur, 0.8727f * r);
                }
                v[i] = cur;
            }
            for (int i = n - 2; i >= 0; i--)
            {
                float s = i * Table;
                if (!OnSlope(s))
                    v[i] = Mathf.Min(v[i], Mathf.Sqrt(v[i + 1] * v[i + 1] + 2f * 0.8f * Table));
            }
            v[0] = Mathf.Max(v[0], 0.3f);   // 정차 자리에서 출발 (런타임이 0.6 m/s²로 올림)
            _speed = v;
        }

        /// <summary>표 속도 (m/s, 평면 길이 기준이 아니라 물길을 따라).</summary>
        public static float Speed(float s)
        {
            Init();
            float f = Wrap(s) / Table;
            int i = Mathf.Clamp(Mathf.FloorToInt(f), 0, _speed.Length - 2);
            return Mathf.Lerp(_speed[i], _speed[i + 1], f - i);
        }

        /// <summary>물길 비탈의 코사인 (물길을 따라 v로 가면 평면 s는 v · cos만큼).</summary>
        public static float PlanFactor(float s)
        {
            float dy = Water(s + 0.05f) - Water(s - 0.05f);
            return 0.1f / Mathf.Sqrt(0.01f + dy * dy);
        }
    }
}
