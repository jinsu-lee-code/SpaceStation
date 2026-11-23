using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 궤도 방어망 체험 "등대 빛 돌리기" 규칙 (사용자 2026-11-13 "비추면 반응" — 손잡이를 잡고 A · D로 렌즈를 돌려 밤바다를 비춤). Unity 없이 시험하는 순수 규칙.
    /// 렌즈 = 앞뒤 빛 두 줄기 (앞 = 손잡이 쪽 <see cref="Yaw"/>, 뒤 = +180°). 아무도 안 잡으면 천천히 돎 (<see cref="IdleSpeed"/>),
    /// 잡으면 A · D로 돌리고 (가속 · 손을 떼면 멈춤), 빛 한 줄기가 곳 (방향 ± 폭)에 <see cref="Dwell"/>초 머물면 그곳이 답함 (빛에서 벗어났다 다시 들어오면 또).
    /// 빠르게 쓸고 지나가기만 하면 답하지 않음 (돌리다 멈춰 비추는 맛 — 두 줄기라 한 바퀴 쓸면 4초에 끝났음).
    /// 잡은 동안 처음 답한 곳을 셈 — 모두 답하면 끝 (걸린 시간 = 최고 기록). 방향 = Unity yaw (도, +z에서 시계 방향).
    /// </summary>
    public sealed class LighthouseBeam
    {
        /// <summary>아무도 안 잡을 때 도는 빠르기 (도/초, 시계 방향) — 먼 곳 (± 2.5°)도 머무는 시간을 넘김.</summary>
        public const float IdleSpeed = 7f;
        /// <summary>잡고 돌릴 때 가장 빠르기 (도/초) · 가속 (도/초²).</summary>
        public const float TurnSpeed = 36f, TurnAccel = 110f;
        /// <summary>빛이 이만큼 머물면 답함 (초).</summary>
        public const float Dwell = 0.5f;

        private readonly float[] _targets;     // 곳 방향 (yaw)
        private readonly float[] _widths;      // 곳 폭 (± 도)
        private readonly float[] _litTime;     // 이번에 빛 안에 든 시간
        private readonly bool[] _answered;     // 이번에 빛 안에 든 동안 답함
        private readonly bool[] _found;
        private float _yaw;
        private float _speed;

        /// <summary>앞 빛줄기 방향 (yaw 0 ~ 360).</summary>
        public float Yaw => _yaw;
        /// <summary>지금 도는 빠르기 (도/초, + = 시계 방향 = 오른쪽).</summary>
        public float Speed => _speed;
        /// <summary>손잡이를 잡음 (A · D로 돌림, 손을 떼면 멈춤).</summary>
        public bool Held { get; set; }
        /// <summary>잡은 동안 돌리는 쪽 (−1 = 왼쪽 A · +1 = 오른쪽 D).</summary>
        public float Steer { get; set; }
        public int TargetCount => _targets.Length;
        /// <summary>이번에 잡은 뒤 답한 곳 수.</summary>
        public int FoundCount { get; private set; }
        public bool AllFound => FoundCount >= _targets.Length && _targets.Length > 0;
        public bool Found(int i) => i >= 0 && i < _found.Length && _found[i];
        /// <summary>곳 i를 비추는 정도 0 ~ 1 (머문 시간 / Dwell, 답하면 1).</summary>
        public float Charge(int i) => _answered[i] ? 1f : Mathf.Clamp01(_litTime[i] / Dwell);

        /// <param name="startYaw">앞 빛줄기 처음 방향</param>
        /// <param name="targetYaws">답하는 곳 방향</param>
        /// <param name="targetWidths">곳 폭 (± 도, 멀수록 좁음)</param>
        public LighthouseBeam(float startYaw, IReadOnlyList<float> targetYaws, IReadOnlyList<float> targetWidths)
        {
            _yaw = Mathf.Repeat(startYaw, 360f);
            int n = targetYaws.Count;
            _targets = new float[n];
            _widths = new float[n];
            _litTime = new float[n];
            _answered = new bool[n];
            _found = new bool[n];
            for (int i = 0; i < n; i++)
            {
                _targets[i] = Mathf.Repeat(targetYaws[i], 360f);
                _widths[i] = targetWidths[i];
            }
        }

        /// <summary>잡음: 이번 셈을 새로 시작 (지금 빛 안에 있는 곳도 머물면 답함).</summary>
        public void Begin()
        {
            Held = true;
            Steer = 0f;
            _speed = 0f;
            FoundCount = 0;
            for (int i = 0; i < _found.Length; i++)
            {
                _found[i] = false;
                _answered[i] = false;
                _litTime[i] = 0f;
            }
        }

        /// <summary>놓음: 다시 천천히 돎.</summary>
        public void Release()
        {
            Held = false;
            Steer = 0f;
        }

        /// <summary>한 프레임: 돌리고, 이번에 답한 곳을 <paramref name="answered"/>에, 잡은 동안 처음 답한 곳을 <paramref name="firstFound"/>에.</summary>
        public void Step(float dt, List<int> answered, List<int> firstFound = null)
        {
            float want = Held ? Mathf.Clamp(Steer, -1f, 1f) * TurnSpeed : IdleSpeed;
            _speed = Mathf.MoveTowards(_speed, want, TurnAccel * dt);
            _yaw = Mathf.Repeat(_yaw + _speed * dt, 360f);
            for (int i = 0; i < _targets.Length; i++)
            {
                if (!Lit(i))
                {
                    _litTime[i] = 0f;
                    _answered[i] = false;
                    continue;
                }
                _litTime[i] += dt;
                if (_answered[i] || _litTime[i] < Dwell)
                    continue;
                _answered[i] = true;
                answered?.Add(i);
                if (Held && !_found[i])
                {
                    _found[i] = true;
                    FoundCount++;
                    firstFound?.Add(i);
                }
            }
        }

        /// <summary>곳 i가 지금 빛 (앞 · 뒤 줄기 하나라도) 안에 있는지.</summary>
        public bool Lit(int i)
        {
            float d = Mathf.Abs(Mathf.DeltaAngle(_yaw, _targets[i]));
            return d <= _widths[i] || 180f - d <= _widths[i];
        }
    }
}
