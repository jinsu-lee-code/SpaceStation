namespace SpaceStation.Interior
{
    /// <summary>
    /// 수리 사령부 체험 ① "구조 바구니" 높이 규칙 (TASKS 2026-11-13 "도르래 구조 바구니를 타고 F로 줄을 당겨 나무집 관제실로", 사용자 2026-11-15 "체험으로 넘어가자").
    /// Unity 없이 시험하는 순수 규칙. 높이 0 = 땅 정류장 · <see cref="Top"/> = 나무집 마루.
    /// 탄 사람이 줄을 한 번 당길 때마다 <see cref="StrokeRise"/> m를 <see cref="StrokeTime"/>초에 걸쳐 오름 (당기는 중에 또 누르면 이어서 한 번 더),
    /// 손을 떼면 멈춤쇠가 그 높이에 붙잡음 (떨어지지 않음). 내려가기 = 꾹 누르는 동안 멈춤쇠를 풀고 천천히 (떼면 멈춤). 빈 바구니는 부르면 혼자 오르내림.
    /// </summary>
    public sealed class RescueBasketRide
    {
        public enum Arrival { None, Top, Bottom }

        public const float StrokeRise = 0.62f, StrokeTime = 0.42f;
        public const float LowerSpeed = 1.1f, LowerAccel = 3.5f, CallSpeed = 1.5f;
        private const float Eps = 1e-4f;

        private float _strokeFrom, _strokeTo;
        private float _strokeT = -1f;    // 0 ~ 1 = 당기는 중, < 0 = 쉼
        private bool _queued;
        private bool _lowerHeld;
        private float _lowerSpeed;
        private int _call;               // +1 = 위로, −1 = 아래로 (빈 바구니)

        public RescueBasketRide(float top)
        {
            Top = top;
        }

        /// <summary>땅 정류장 → 나무집 마루 높이 차 (m).</summary>
        public float Top { get; }
        public float Height { get; private set; }
        /// <summary>지난 걸음의 오르는 빠르기 (m/s, 내려가면 음수) — 줄 삐걱 · 바구니 흔들림.</summary>
        public float Speed { get; private set; }
        /// <summary>이번에 탄 뒤 줄을 당긴 횟수.</summary>
        public int Strokes { get; private set; }
        public bool AtTop => Height >= Top - Eps;
        public bool AtBottom => Height <= Eps;
        public bool Pulling => _strokeT >= 0f;
        public bool Lowering => _lowerSpeed > 0f;
        public bool Called => _call != 0;
        public bool Moving => Pulling || Lowering || Called;
        /// <summary>당긴 횟수로 꼭대기까지 남은 번 (지금 당기는 것 빼고).</summary>
        public int StrokesLeft => (int)System.Math.Ceiling((Top - (Pulling ? _strokeTo : Height)) / StrokeRise - 1e-3);

        /// <summary>줄 한 번 당김 (탄 사람). 바로 당기기 시작했으면 true (당기는 중이면 다음 한 번을 이어 둠).</summary>
        public bool Pull()
        {
            if (Called || (AtTop && !Pulling))
                return false;
            _lowerSpeed = 0f;
            if (Pulling)
            {
                _queued = _strokeTo < Top - Eps;
                return false;
            }
            BeginStroke();
            return true;
        }

        /// <summary>내려가기 손잡이를 쥐고 있는지 (매 프레임 — 떼면 멈춤쇠가 붙잡음).</summary>
        public void Lower(bool held)
        {
            _lowerHeld = held;
        }

        /// <summary>빈 바구니 부르기 (정류장 · 마루 문 앞에 선 사람). 당기는 중이면 무시.</summary>
        public void Call(bool toTop)
        {
            if (Pulling)
                return;
            if (toTop ? AtTop : AtBottom)
                return;
            _lowerSpeed = 0f;
            _call = toTop ? 1 : -1;
        }

        /// <summary>새로 탄 사람 (당긴 횟수를 다시 셈).</summary>
        public void Board() => Strokes = 0;

        /// <summary>그 자리에 바로 세움 (방을 나가면 가까운 정류장으로).</summary>
        public void Park(float height)
        {
            Height = height < 0f ? 0f : height > Top ? Top : height;
            _strokeT = -1f;
            _queued = false;
            _lowerSpeed = 0f;
            _call = 0;
            Speed = 0f;
        }

        public Arrival Step(float dt)
        {
            if (dt <= 0f)
                return Arrival.None;
            float before = Height;
            if (_strokeT >= 0f)
            {
                _strokeT += dt / StrokeTime;
                float t = _strokeT >= 1f ? 1f : _strokeT;
                Height = _strokeFrom + (_strokeTo - _strokeFrom) * t * t * (3f - 2f * t);   // 당길 때마다 쭉 올라갔다 멈춤
                if (_strokeT >= 1f)
                {
                    _strokeT = -1f;
                    if (_queued && !AtTop)
                        BeginStroke();
                    _queued = false;
                }
            }
            else if (_call != 0)
            {
                float target = _call > 0 ? Top : 0f;
                float step = CallSpeed * dt;
                Height = System.Math.Abs(target - Height) <= step ? target : Height + (target > Height ? step : -step);
                if (Height == target)
                    _call = 0;
            }
            else
            {
                _lowerSpeed = _lowerHeld && !AtBottom ? System.Math.Min(LowerSpeed, _lowerSpeed + LowerAccel * dt) : 0f;
                Height -= _lowerSpeed * dt;
                if (Height <= 0f)
                {
                    Height = 0f;
                    _lowerSpeed = 0f;
                }
            }
            Speed = (Height - before) / dt;
            if (before < Top - Eps && AtTop)
                return Arrival.Top;
            if (before > Eps && AtBottom)
                return Arrival.Bottom;
            return Arrival.None;
        }

        private void BeginStroke()
        {
            _strokeFrom = Height;
            _strokeTo = System.Math.Min(Top, Height + StrokeRise);
            _strokeT = 0f;
            Strokes++;
        }
    }
}
