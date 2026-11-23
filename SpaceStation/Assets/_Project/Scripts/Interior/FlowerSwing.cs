using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 항성 발전망 체험 "정자 꽃 그네" 규칙 (사용자 4안 — F 박자로 굴러 높이 오르기 · 수정 꽃송이 종 울리기). Unity 없이 시험하는 순수 규칙.
    /// 그네 = 줄 길이 L 진자 (각 + = 앞, 공기 저항). F = 구르기: 그네가 앞뒤 끝에 가까울수록 (지금 각 / 흔들림 폭) 높이가 많이 오름 (박자 딱 +7° · 좋아요 +4° ·
    /// 일러요 · 늦어요 +0.5°), 흔들림 한 번 (바닥을 지나 다음 바닥까지)에 한 번만. 멈춰 있을 때 F = 출발 (12°). 박자를 안 맞추고 누르기만 하면 15° 안팎에서 멈춤.
    /// 수정 꽃송이 종 = 앞 셋 (0 ~ 2) · 뒤 셋 (3 ~ 5), 그네가 그 각에 닿으면 울림 — 여섯 개를 다 울리면 끝 (걸린 시간 = 최고 기록).
    /// </summary>
    public sealed class FlowerSwing
    {
        public enum Kick { Start, Perfect, Good, Early, Late, Again }

        public const float Gravity = 9.81f;
        /// <summary>가장 높이 (가장 높은 종 60° 위 — 정자 돔 · 꽃 고리 화단에 안 닿는 각).</summary>
        public const float MaxAngle = 66f;
        public const float StartAngle = 12f, PerfectGain = 7f, GoodGain = 4f, MissGain = 0.5f;
        /// <summary>박자 판정 = 지금 각 / 흔들림 폭 (끝 = 1, 바닥 = 0).</summary>
        public const float PerfectWindow = 0.85f, GoodWindow = 0.6f;
        /// <summary>공기 저항 (흔들림 한 번에 폭이 약 7 % 줄어듦) · 발로 끌어 멈출 때.</summary>
        public const float Drag = 0.035f, BrakeDrag = 0.9f;
        /// <summary>종 = 그네가 그 각 − 1°에 닿으면.</summary>
        public const float BellReach = 1f;
        public const int BellCount = 6;
        /// <summary>이만큼 아래로 흔들리면 멈췄다고 봄 (내리기 · 출발).</summary>
        public const float RestAmplitude = 1.5f;

        private readonly float _k;            // g / L (1/초²)
        private readonly float[] _bells;      // 종 각 (앞 +, 뒤 −)
        private readonly bool[] _rung = new bool[BellCount];
        private readonly bool[] _touching = new bool[BellCount];
        private float _theta, _omega;         // 라디안 · 라디안/초
        private bool _kicked;

        /// <summary>지금 각 (도, + = 앞).</summary>
        public float Angle => _theta * Mathf.Rad2Deg;
        /// <summary>각속도 (도/초).</summary>
        public float Velocity => _omega * Mathf.Rad2Deg;
        /// <summary>발로 끌어 멈추는 중 (S 꾹 · 아무도 안 탈 때).</summary>
        public bool Braking { get; set; }
        /// <summary>이번 흔들림 (바닥 ~ 바닥)에 이미 구름.</summary>
        public bool Kicked => _kicked;
        /// <summary>이번에 탄 뒤 가장 높이 오른 폭 (도).</summary>
        public float Highest { get; private set; }

        /// <param name="length">줄 길이 (굴대 → 자리, m)</param>
        /// <param name="bellAngles">종 각 (낮은 것부터, 앞뒤 같음)</param>
        public FlowerSwing(float length, IReadOnlyList<float> bellAngles)
        {
            _k = Gravity / Mathf.Max(0.5f, length);
            _bells = new float[BellCount];
            for (int i = 0; i < 3; i++)
            {
                _bells[i] = bellAngles[i];
                _bells[3 + i] = -bellAngles[i];
            }
        }

        /// <summary>종 각 (도, 앞 0 ~ 2 = +, 뒤 3 ~ 5 = −).</summary>
        public float BellAngle(int i) => _bells[i];
        public bool Rung(int i) => _rung[i];

        public int RungCount
        {
            get
            {
                int n = 0;
                foreach (bool r in _rung)
                    if (r)
                        n++;
                return n;
            }
        }

        public bool AllRung => RungCount == BellCount;

        /// <summary>지금 흔들림 폭 (도) — 에너지 (½ω² + k(1 − cos θ))가 다 위치 에너지가 되는 각.</summary>
        public float Amplitude
        {
            get
            {
                float e = 0.5f * _omega * _omega + _k * (1f - Mathf.Cos(_theta));
                return Mathf.Acos(Mathf.Clamp(1f - e / _k, -1f, 1f)) * Mathf.Rad2Deg;
            }
        }

        /// <summary>박자 = 지금 각 / 흔들림 폭 (0 = 바닥, 1 = 끝).</summary>
        public float Timing
        {
            get
            {
                float a = Amplitude;
                return a < 0.01f ? 0f : Mathf.Clamp01(Mathf.Abs(Angle) / a);
            }
        }

        public bool AtRest => Amplitude < RestAmplitude;

        /// <summary>새로 탐: 멈춘 그네 · 종 · 기록을 처음으로.</summary>
        public void Begin()
        {
            for (int i = 0; i < BellCount; i++)
                _rung[i] = _touching[i] = false;
            Highest = Amplitude;
            _kicked = false;
            Braking = false;
        }

        /// <summary>바로 멈춤 (방을 나갈 때).</summary>
        public void Stop()
        {
            _theta = _omega = 0f;
            _kicked = false;
            for (int i = 0; i < BellCount; i++)
                _touching[i] = false;
        }

        /// <summary>F = 구르기.</summary>
        public Kick Pump()
        {
            float a = Amplitude;
            if (a < RestAmplitude)
            {
                SetAmplitude(StartAngle, 1f);
                _kicked = true;
                return Kick.Start;
            }
            if (_kicked)
                return Kick.Again;
            _kicked = true;
            float q = Timing;
            var kick = q >= PerfectWindow ? Kick.Perfect : q >= GoodWindow ? Kick.Good : _theta * _omega > 0f ? Kick.Early : Kick.Late;
            float gain = kick == Kick.Perfect ? PerfectGain : kick == Kick.Good ? GoodGain : MissGain;
            SetAmplitude(Mathf.Min(MaxAngle, a + gain), 0f);
            return kick;
        }

        /// <summary>각은 그대로 두고 폭만 바꿈 (각속도 크기를 에너지에 맞춤). dir = 멈춰 있을 때 나갈 쪽.</summary>
        private void SetAmplitude(float amplitude, float dir)
        {
            float a = amplitude * Mathf.Deg2Rad;
            float w2 = 2f * _k * (Mathf.Cos(_theta) - Mathf.Cos(a));
            float w = Mathf.Sqrt(Mathf.Max(0f, w2));
            float sign = Mathf.Abs(_omega) > 1e-3f ? Mathf.Sign(_omega) : dir != 0f ? Mathf.Sign(dir) : _theta > 0f ? -1f : 1f;
            _omega = sign * w;
            Highest = Mathf.Max(Highest, amplitude);
        }

        /// <summary>시간을 보냄. 새로 울린 종 번호를 rang에 더함 (null이면 안 더함).</summary>
        public void Step(float dt, List<int> rang)
        {
            if (dt <= 0f)
                return;
            int n = Mathf.Max(1, Mathf.CeilToInt(dt / (1f / 240f)));
            float h = dt / n;
            float max = MaxAngle * Mathf.Deg2Rad;
            for (int s = 0; s < n; s++)
            {
                float before = _theta;
                float drag = Braking ? BrakeDrag : Drag;
                _omega += (-_k * Mathf.Sin(_theta) - drag * _omega) * h;
                _theta += _omega * h;
                if (Mathf.Abs(_theta) > max)   // 구르기로 넘친 몫 (흔들림 폭이 MaxAngle 안이라 거의 없음)
                {
                    _theta = Mathf.Sign(_theta) * max;
                    _omega = 0f;
                }
                if (before != 0f && Mathf.Sign(before) != Mathf.Sign(_theta))
                    _kicked = false;   // 바닥을 지남 = 새 흔들림
            }
            Highest = Mathf.Max(Highest, Mathf.Abs(Angle));
            float deg = Angle;
            for (int i = 0; i < BellCount; i++)
            {
                float b = _bells[i];
                bool touch = Mathf.Sign(deg) == Mathf.Sign(b) && Mathf.Abs(deg) >= Mathf.Abs(b) - BellReach;
                if (touch && !_touching[i])
                {
                    _rung[i] = true;
                    rang?.Add(i);
                }
                _touching[i] = touch;
            }
        }
    }
}
