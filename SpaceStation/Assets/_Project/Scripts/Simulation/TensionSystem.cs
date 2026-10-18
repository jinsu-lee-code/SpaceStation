using System;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 13-6a 서사판 긴장도 (REDESIGN S3, 사용자 결정 2026-10-18 D-10: 값 + 표시만 — 결과인 사회 위기는 6장부터).
    /// 쌓이는 곳: 예보가 있었는데 입은 피해(떨어지는 중인 유성우 · 포격의 파손마다) · 종족 불만(중시 요구를 못 채운 만큼) · 과밀(수용을 넘은 만큼) · 정책 반발(통제형 정책 × 반발하는 무리).
    /// 오를 이유가 없으면 천천히 내려감. 장마다 원인별로 얼마나 쌓였는지 기록 (장 결과 창 "가장 큰 원인").
    /// </summary>
    public sealed class TensionSystem : StationSystem
    {
        public const string SystemKey = "tension";

        public enum Source
        {
            /// <summary>예보가 있었는데 입은 피해.</summary>
            ForecastHit,
            /// <summary>종족이 중시하는 요구를 못 채움.</summary>
            Favored,
            /// <summary>수용 인원을 넘음.</summary>
            Crowd,
            /// <summary>통제형 정책에 반발.</summary>
            Backlash,
        }

        private const int SourceCount = 4;

        private readonly SocietyConfig _config;
        private readonly float[] _chapterTotals = new float[SourceCount];
        private readonly float[] _rates = new float[SourceCount];

        public TensionSystem(SocietyConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public override string Key => SystemKey;
        public SocietyConfig Config => _config;

        /// <summary>지금 긴장도 (0 ~ 최대).</summary>
        public float Value { get; private set; }

        public string LevelName => _config.LevelName(Value);
        public int Level => _config.Level(Value);

        /// <summary>지금 오르는 속도 (분당, 원인별 — 파손은 순간이라 0).</summary>
        public float RatePerMinute(Source source) => _rates[(int)source] * 60f;

        /// <summary>이 장에서 원인별로 쌓인 양.</summary>
        public float ChapterTotal(Source source) => _chapterTotals[(int)source];

        public static string SourceName(Source source)
        {
            switch (source)
            {
                case Source.ForecastHit: return "예보 뒤 피해";
                case Source.Favored: return "종족 불만";
                case Source.Crowd: return "과밀";
                default: return "정책 반발";
            }
        }

        /// <summary>이 장에서 가장 많이 쌓인 원인 (아무것도 없으면 false).</summary>
        public bool TopChapterCause(out Source source)
        {
            source = Source.ForecastHit;
            float best = 0.05f;
            bool found = false;
            for (int i = 0; i < SourceCount; i++)
                if (_chapterTotals[i] > best)
                {
                    best = _chapterTotals[i];
                    source = (Source)i;
                    found = true;
                }
            return found;
        }

        protected override void OnAttached()
        {
            Sim.Damage.Damaged += HandleDamaged;
            if (Sim.Chapters != null)
                Sim.Chapters.ChapterStarted += HandleChapterStarted;
        }

        private void HandleChapterStarted(int index) => Array.Clear(_chapterTotals, 0, SourceCount);

        private void HandleDamaged(DamageInfo info)
        {
            var shower = Sim.MeteorShower;
            if (shower != null && shower.Phase == MeteorShowerSystem.ShowerPhase.Falling) // 예보가 있었던 유성우 · 포격
                Add(Source.ForecastHit, _config.ForecastHit);
        }

        private void Add(Source source, float amount)
        {
            if (amount <= 0f)
                return;
            float before = Value;
            Value = Mathf.Min(_config.Max, Value + amount);
            _chapterTotals[(int)source] += Value - before;
        }

        public override void Tick(float dt)
        {
            if (dt <= 0f || Sim.Session.IsGameOver)
                return;
            Array.Clear(_rates, 0, SourceCount);
            int population = Sim.Resources.Population;
            if (population > 0)
            {
                _rates[(int)Source.Favored] = _config.FavoredPerMinute / 60f * FavoredUnmet();
                int capacity = Math.Max(1, Sim.Resources.HousingCapacity);
                float over = Mathf.Clamp01((population - capacity) / (float)capacity);
                _rates[(int)Source.Crowd] = _config.CrowdPerMinute / 60f * over;
                var policy = Sim.GetSystem<ChapterPolicySystem>();
                if (policy != null)
                    _rates[(int)Source.Backlash] = _config.BacklashPerMinute / 60f * policy.Opposition * policy.ActiveControl;
            }
            float rising = 0f;
            for (int i = 0; i < SourceCount; i++)
            {
                rising += _rates[i];
                Add((Source)i, _rates[i] * dt);
            }
            if (rising <= 0f)
                Value = Mathf.Max(0f, Value - _config.CalmPerMinute / 60f * dt);
        }

        /// <summary>Σ 종족 비율 × 그 종족이 중시하는 요구를 못 채운 비율 (그 요구가 아직 없으면 0).</summary>
        private float FavoredUnmet()
        {
            var ships = Sim.GetSystem<ArkShipSystem>();
            var roster = Sim.Residents;
            if (ships == null || roster == null || roster.Residents.Count == 0)
                return 0f;
            float sum = 0f;
            foreach (var s in ships.Config.Species)
            {
                if (s == null || s.FavoredNeed == ResidentNeed.None)
                    continue;
                int count = roster.CountKind(s.Id);
                if (count == 0)
                    continue;
                foreach (var status in Sim.Needs.Statuses)
                    if (status.Need == s.FavoredNeed)
                    {
                        sum += (float)count / roster.Residents.Count * (1f - status.Ratio);
                        break;
                    }
            }
            return sum;
        }

        /// <summary>지금 오르는 원인 이름 (빠른 순, 최대 2개, 없으면 "").</summary>
        public string RisingCauses()
        {
            int first = -1, second = -1;
            for (int i = 0; i < SourceCount; i++)
            {
                if (_rates[i] <= 1e-5f)
                    continue;
                if (first < 0 || _rates[i] > _rates[first])
                {
                    second = first;
                    first = i;
                }
                else if (second < 0 || _rates[i] > _rates[second])
                    second = i;
            }
            if (first < 0)
                return "";
            return second < 0 ? SourceName((Source)first) : $"{SourceName((Source)first)} · {SourceName((Source)second)}";
        }

        // ---------------- 세이브 ----------------

        [Serializable]
        private sealed class State
        {
            public float Value;
            public float[] ChapterTotals = new float[SourceCount];
        }

        public override string Capture(Func<ModuleInstance, int> indexOf) => JsonUtility.ToJson(new State
        {
            Value = Value,
            ChapterTotals = (float[])_chapterTotals.Clone(),
        });

        public override void Restore(string state, Func<int, ModuleInstance> moduleAt)
        {
            var s = string.IsNullOrEmpty(state) ? null : JsonUtility.FromJson<State>(state);
            if (s == null)
                return;
            Value = Mathf.Clamp(s.Value, 0f, _config.Max);
            Array.Clear(_chapterTotals, 0, SourceCount);
            if (s.ChapterTotals != null)
                for (int i = 0; i < SourceCount && i < s.ChapterTotals.Length; i++)
                    _chapterTotals[i] = Mathf.Max(0f, s.ChapterTotals[i]);
        }
    }
}
