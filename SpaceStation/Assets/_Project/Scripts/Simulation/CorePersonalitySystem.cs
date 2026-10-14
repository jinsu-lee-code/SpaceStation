using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>13-2: 축 이동 기록 하나 (나중에 엔딩 · 회고용 — 무엇 때문에 어느 쪽으로 움직였나).</summary>
    [Serializable]
    public sealed class CoreShiftRecord
    {
        public float Time;
        public int Chapter;
        public string Cause;
        public CoreAxis Axis;
        public float Delta;
    }

    /// <summary>13-2: 코어 로그 한 줄.</summary>
    [Serializable]
    public sealed class CoreLogEntry
    {
        public float Time;
        public int Chapter;
        public string Text;
    }

    /// <summary>
    /// 13-2 코어 성격 4축 (사용자 결정 2026-10-15, REDESIGN E1 · E2 · D-4). 플레이어 = 방주 "코어"의 AI.
    /// - 축 값 −100 ~ +100 (숨김, 화면에는 말로만 — <see cref="Describe"/>), 선택마다 3단계 폭으로 움직임 (<see cref="Apply"/>, 원인 기록).
    /// - 문장은 정해진 표에서 고름 (<see cref="Say"/>): 계기 키마다 기본 문장 + 축별 성향 문장, 가장 강하게 기운 축의 문장이 우선.
    /// - 코어 로그 (<see cref="Log"/>) — 로그 창 · 장 결과 독백 · 상태 줄 말투가 같은 표를 씀.
    /// 서사판 전용: 게임(SimulationHost)이 <see cref="StationSimulation.AddSystem"/>로 붙인다 (공통 바닥은 그대로).
    /// </summary>
    public sealed class CorePersonalitySystem : StationSystem
    {
        public const string SystemKey = "core";
        public const float Limit = 100f;
        private const int MaxLog = 200;

        private readonly CoreConfig _config;
        private readonly float[] _values = new float[4];
        private readonly List<CoreShiftRecord> _shifts = new List<CoreShiftRecord>();
        private readonly List<CoreLogEntry> _log = new List<CoreLogEntry>();

        /// <summary>축이 움직임 (축, 이동량).</summary>
        public event Action<CoreAxis, float> Shifted;
        /// <summary>로그 한 줄이 쌓임.</summary>
        public event Action<CoreLogEntry> Logged;

        public CorePersonalitySystem(CoreConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public override string Key => SystemKey;
        public CoreConfig Config => _config;
        public IReadOnlyList<CoreShiftRecord> Shifts => _shifts;
        public IReadOnlyList<CoreLogEntry> Entries => _log;

        /// <summary>축 값 (−100 ~ +100). 화면에는 숫자로 보이지 않음.</summary>
        public float Value(CoreAxis axis) => _values[(int)axis];

        private int Chapter => Sim.Chapters != null ? Sim.Chapters.Index : -1;

        // ---------------- 이동 ----------------

        /// <summary>선택 하나의 축 이동들을 적용하고 원인을 남김.</summary>
        public void Apply(IReadOnlyList<AxisShift> shifts, string cause)
        {
            if (shifts == null)
                return;
            foreach (var s in shifts)
            {
                if (s.Toward == CorePole.Neutral)
                    continue;
                float amount = _config.Amount(s.Size) * (s.Toward == CorePole.First ? -1f : 1f);
                int i = (int)s.Axis;
                float before = _values[i];
                _values[i] = Mathf.Clamp(before + amount, -Limit, Limit);
                float delta = _values[i] - before;
                _shifts.Add(new CoreShiftRecord { Time = Sim.ElapsedSeconds, Chapter = Chapter, Cause = cause, Axis = s.Axis, Delta = delta });
                Shifted?.Invoke(s.Axis, delta);
            }
        }

        // ---------------- 성향 ----------------

        /// <summary>축이 어느 쪽으로 기울었는지 (문턱 아래면 Neutral).</summary>
        public CorePole Lean(CoreAxis axis)
        {
            float v = Value(axis);
            if (Mathf.Abs(v) < _config.Slight)
                return CorePole.Neutral;
            return v < 0f ? CorePole.First : CorePole.Second;
        }

        /// <summary>축 성향을 말로 ("균형", "연민 쪽으로 조금", "연민 쪽으로 기울어짐", "연민에 깊이 기울어짐").</summary>
        public string Describe(CoreAxis axis)
        {
            float v = Value(axis), a = Mathf.Abs(v);
            if (a < _config.Slight)
                return "균형";
            string pole = CoreConfig.PoleName(axis, v < 0f ? CorePole.First : CorePole.Second);
            if (a < _config.Leaning)
                return $"{pole} 쪽으로 조금";
            if (a < _config.Deep)
                return $"{pole} 쪽으로 기울어짐";
            return $"{pole}에 깊이 기울어짐";
        }

        // ---------------- 문장 ----------------

        /// <summary>
        /// 계기 키에 맞는 문장 (지금 성향으로 고름): 그 키의 성향 문장 중 지금 가장 강하게 기운 축 · 쪽에 맞는 것 → 없으면 기본(Neutral) 문장 → 없으면 fallback.
        /// args는 문장 안의 {0}, {1} … 자리에 들어감.
        /// </summary>
        public string Say(string key, string fallback, params object[] args)
        {
            CoreLine best = null, neutral = null;
            float bestStrength = 0f;
            foreach (var line in _config.Lines)
            {
                if (line == null || line.Key != key || string.IsNullOrEmpty(line.Text))
                    continue;
                if (line.Pole == CorePole.Neutral)
                {
                    neutral = neutral ?? line;
                    continue;
                }
                if (Lean(line.Axis) != line.Pole)
                    continue;
                float strength = Mathf.Abs(Value(line.Axis));
                if (strength > bestStrength)
                {
                    best = line;
                    bestStrength = strength;
                }
            }
            string text = (best ?? neutral)?.Text ?? fallback;
            if (string.IsNullOrEmpty(text) || args == null || args.Length == 0)
                return text;
            try
            {
                return string.Format(text, args);
            }
            catch (FormatException)
            {
                return text; // 문장 표 오타 — 자리 표시를 그대로 둠
            }
        }

        /// <summary>코어 로그 한 줄 (오래된 것부터 지워 최대 200줄).</summary>
        public CoreLogEntry Log(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;
            var entry = new CoreLogEntry { Time = Sim.ElapsedSeconds, Chapter = Chapter, Text = text };
            _log.Add(entry);
            if (_log.Count > MaxLog)
                _log.RemoveAt(0);
            Logged?.Invoke(entry);
            return entry;
        }

        /// <summary>문장을 골라 로그에 남기고 돌려줌 (상태 줄 알림 등에 같은 문장을 씀).</summary>
        public string SayAndLog(string key, string fallback, params object[] args)
        {
            string text = Say(key, fallback, args);
            Log(text);
            return text;
        }

        // ---------------- 세이브 ----------------

        [Serializable]
        private sealed class State
        {
            public float[] Values = new float[4];
            public List<CoreShiftRecord> Shifts = new List<CoreShiftRecord>();
            public List<CoreLogEntry> Log = new List<CoreLogEntry>();
        }

        public override string Capture(Func<ModuleInstance, int> indexOf) => JsonUtility.ToJson(new State
        {
            Values = (float[])_values.Clone(),
            Shifts = new List<CoreShiftRecord>(_shifts),
            Log = new List<CoreLogEntry>(_log),
        });

        public override void Restore(string state, Func<int, ModuleInstance> moduleAt)
        {
            var s = string.IsNullOrEmpty(state) ? null : JsonUtility.FromJson<State>(state);
            if (s == null)
                return;
            for (int i = 0; i < _values.Length && s.Values != null && i < s.Values.Length; i++)
                _values[i] = Mathf.Clamp(s.Values[i], -Limit, Limit);
            _shifts.Clear();
            if (s.Shifts != null)
                _shifts.AddRange(s.Shifts);
            _log.Clear();
            if (s.Log != null)
                _log.AddRange(s.Log);
        }
    }
}
