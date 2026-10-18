using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SpaceStation.Data;
using SpaceStation.Simulation;

namespace SpaceStation.Editor.Balance
{
    /// <summary>
    /// 13-7 '지루함' 측정 (사용자 결정 2026-10-16): 한 판에서 처음 생긴 일(새 등급 · 지을 수 있게 된 모듈 · 새 요구 · 새 시스템 · 처음 겪은 위기 ·
    /// 첫 합성)의 시각을 모아 '새 일 간격'(가장 긴 공백)을 보고, 위기 빈도 · 여유 자원 시간도 함께 센다.
    /// 기준점 빌드(baseline-before-rework)에도 같은 파일을 넣어 돌릴 수 있게 그때 있던 API만 쓴다 (봇 루프에 두 줄: 만들기 · Tick).
    /// 결과는 정적 목록에 모이고 <see cref="WriteReport"/>가 milestones.csv · milestones.txt로 쓴다.
    /// </summary>
    public sealed class BalanceMilestones
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly ResourceType[] Stocks = { ResourceType.Oxygen, ResourceType.Water, ResourceType.Food, ResourceType.Metal };
        /// <summary>새 일 간격을 볼 마지막 등급 (도시급 = 4, 13-7 범위 소형 ~ 도시).</summary>
        public const int LastGrade = 4;
        /// <summary>여유 자원: 네 자원 모두 한도의 이 비율 이상.</summary>
        public const float SlackRatio = 0.7f;
        private const float CheckSeconds = 5f;

        /// <summary>이번 측정의 판들 (측정 시작 때 <see cref="Clear"/>).</summary>
        public static readonly List<BalanceMilestones> Runs = new List<BalanceMilestones>();

        public static void Clear() => Runs.Clear();

        public readonly struct Item
        {
            public readonly float Minutes;
            public readonly string Kind;
            public readonly string Name;

            public Item(float minutes, string kind, string name)
            {
                Minutes = minutes;
                Kind = kind;
                Name = name;
            }
        }

        private readonly StationSimulation _sim;
        private readonly IReadOnlyList<ModuleData> _buildable;
        private readonly HashSet<ModuleData> _modules = new HashSet<ModuleData>();
        private readonly HashSet<ResidentNeed> _needs = new HashSet<ResidentNeed>();
        private readonly HashSet<string> _seen = new HashSet<string>();
        private readonly HashSet<object> _activeEvents = new HashSet<object>();
        private readonly List<object> _scratch = new List<object>();
        private int _grade;
        private bool _showerWasActive;
        private float _nextCheck;

        public int Seed { get; }
        public List<Item> Items { get; } = new List<Item>();
        public float Seconds { get; private set; }
        public float SlackSeconds { get; private set; }
        /// <summary>위기 시작 횟수 (이벤트 + 유성우).</summary>
        public int Crises { get; private set; }
        public bool GameOver { get; private set; }

        public BalanceMilestones(StationSimulation sim, IReadOnlyList<ModuleData> buildable, int seed)
        {
            _sim = sim;
            _buildable = buildable ?? Array.Empty<ModuleData>();
            Seed = seed;
            _grade = sim.Progression.GradeIndex;
            // 처음부터 있던 것은 새 일이 아님
            foreach (var m in _buildable)
                if (m != null && sim.Progression.IsUnlocked(m))
                    _modules.Add(m);
            foreach (var n in sim.ActiveNeeds)
                _needs.Add(n);
            CheckSystems(0f, record: false);
            lock (Runs)
                Runs.Add(this);
            if (sim.Synthesis != null)
                sim.Synthesis.Completed += (job, module) =>
                {
                    if (module?.Data != null)
                        Add(Seconds / 60f, "합성", module.Data.DisplayName);
                };
        }

        /// <summary>봇 틱마다 (경과 초 · 틱 간격).</summary>
        public void Tick(float elapsed, float dt)
        {
            Seconds = elapsed;
            var r = _sim.Resources;
            bool slack = true;
            foreach (var t in Stocks)
                slack &= r.GetCapacity(t) > 0f && r.GetStock(t) >= r.GetCapacity(t) * SlackRatio;
            if (slack)
                SlackSeconds += dt;
            if (_sim.Session.IsGameOver)
                GameOver = true;

            // 위기 시작 (이벤트 인스턴스 · 유성우)
            _scratch.Clear();
            foreach (var a in _sim.Events.ActiveEvents)
            {
                _scratch.Add(a);
                if (_activeEvents.Add(a))
                {
                    Crises++;
                    if (a.Data != null)
                        Add(elapsed / 60f, "위기", a.Data.DisplayName);
                }
            }
            _activeEvents.IntersectWith(_scratch);
            bool shower = _sim.MeteorShower != null && _sim.MeteorShower.Active;
            if (shower && !_showerWasActive)
            {
                Crises++;
                Add(elapsed / 60f, "위기", "유성우");
            }
            _showerWasActive = shower;

            if (elapsed < _nextCheck)
                return;
            _nextCheck = elapsed + CheckSeconds;
            float min = elapsed / 60f;
            int grade = _sim.Progression.GradeIndex;
            if (grade > _grade)
            {
                for (int g = _grade + 1; g <= grade; g++)
                    Add(min, "등급", _sim.Progression.GetGrade(g).DisplayName);
                _grade = grade;
            }
            foreach (var m in _buildable)
                if (m != null && !_modules.Contains(m) && _sim.Progression.IsUnlocked(m))
                {
                    _modules.Add(m);
                    Add(min, "모듈", m.DisplayName);
                }
            foreach (var n in _sim.ActiveNeeds)
                if (_needs.Add(n))
                    Add(min, "요구", n.DisplayName());
            CheckSystems(min, record: true);
        }

        /// <summary>새 시스템이 열림 (열 · 정책 · 외부 접촉 · 합성 · 사회 위기 경고).</summary>
        private void CheckSystems(float min, bool record)
        {
            void Check(string name, bool open)
            {
                if (open && _seen.Add("sys:" + name) && record)
                    Items.Add(new Item(min, "시스템", name));
            }
            Check("열 관리", _sim.Heat != null && _sim.Heat.Active);
            Check("정책", _sim.Policies != null && _sim.Policies.SlotCount > 0);
            Check("외부 접촉", _sim.Contact != null && _sim.Contact.Unlocked);
            bool synthesis = false;
            if (_sim.Synthesis != null)
                foreach (var recipe in _sim.Synthesis.Recipes)
                    synthesis |= _sim.Synthesis.IsUnlocked(recipe);
            Check("합성", synthesis);
            if (_sim.Crisis != null && _sim.Crisis.Phase.ToString() != "Idle" && _seen.Add("crisis") && record)
                Items.Add(new Item(min, "위기", "사회 위기"));
        }

        /// <summary>같은 종류 · 이름은 처음 한 번만.</summary>
        private void Add(float min, string kind, string name)
        {
            if (_seen.Add(kind + ":" + name))
                Items.Add(new Item(min, kind, name));
        }

        // ---------------- 보고 ----------------

        /// <summary>도시급(또는 끝)까지의 새 일 사이 공백들 (분). 판 시작 0분부터.</summary>
        public List<(float From, float To)> Gaps()
        {
            float end = Seconds / 60f;
            foreach (var i in Items)
                if (i.Kind == "등급" && GradeIndexOf(i.Name) == LastGrade)
                    end = Math.Min(end, i.Minutes);
            var times = Items.Where(i => i.Minutes <= end + 1e-3f).Select(i => i.Minutes).OrderBy(t => t).ToList();
            var gaps = new List<(float, float)>();
            float last = 0f;
            foreach (var t in times)
            {
                if (t - last > 1e-3f)
                    gaps.Add((last, t));
                last = t;
            }
            if (end - last > 1e-3f && !GameOver)
                gaps.Add((last, end));
            return gaps;
        }

        private int GradeIndexOf(string name)
        {
            for (int g = 0; g < _sim.Progression.GradeCount; g++)
                if (_sim.Progression.GetGrade(g).DisplayName == name)
                    return g;
            return -1;
        }

        /// <summary>milestones.csv (판 · 분 · 종류 · 이름) + milestones.txt (요약) 를 폴더에 씀. 반환 = 요약 글.</summary>
        public static string WriteReport(string folder, string title = null)
        {
            List<BalanceMilestones> runs;
            lock (Runs)
                runs = Runs.OrderBy(r => r.Seed).ToList();
            var csv = new StringBuilder("seed,minutes,kind,name\n");
            foreach (var r in runs)
                foreach (var i in r.Items)
                    csv.Append(r.Seed).Append(',').Append(i.Minutes.ToString("0.0", Inv)).Append(',').Append(i.Kind).Append(',').Append(i.Name.Replace(',', ' ')).Append('\n');

            var sb = new StringBuilder();
            sb.AppendLine(title ?? "13-7 새 일 간격 · 위기 빈도 · 여유 자원");
            sb.AppendLine($"판 {runs.Count} · 새 일 = 등급 · 모듈 · 요구 · 시스템 · 처음 겪은 위기 · 첫 합성 / 공백은 시작 ~ 도시급 도달(못 가면 끝)까지");
            var longest = new List<float>();
            var means = new List<float>();
            var crisisRate = new List<float>();
            var slack = new List<float>();
            foreach (var r in runs)
            {
                var gaps = r.Gaps();
                float max = gaps.Count > 0 ? gaps.Max(g => g.To - g.From) : 0f;
                var top = gaps.OrderByDescending(g => g.To - g.From).FirstOrDefault();
                float mean = gaps.Count > 0 ? gaps.Average(g => g.To - g.From) : 0f;
                float minutes = Math.Max(1e-3f, r.Seconds / 60f);
                longest.Add(max);
                means.Add(mean);
                crisisRate.Add(r.Crises / minutes * 10f);
                slack.Add(r.SlackSeconds / Math.Max(1e-3f, r.Seconds));
                string where = gaps.Count > 0 ? $"{top.From:0.0}~{top.To:0.0}분 {GradeAt(r, top.From)}" : "-";
                sb.AppendLine($"시드 {r.Seed}: 새 일 {r.Items.Count}개 · 평균 간격 {mean:0.0}분 · 가장 긴 공백 {max:0.0}분 ({where}) · 위기 10분당 {r.Crises / minutes * 10f:0.0}회 · 여유 자원 {r.SlackSeconds / Math.Max(1e-3f, r.Seconds) * 100f:0}%" + (r.GameOver ? " · 게임 오버" : ""));
            }
            if (runs.Count > 0)
                sb.AppendLine($"평균: 간격 {means.Average():0.0}분 · 가장 긴 공백 {longest.Average():0.0}분 (최대 {longest.Max():0.0}) · 위기 10분당 {crisisRate.Average():0.0}회 · 여유 자원 {slack.Average() * 100f:0}%");
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "milestones.csv"), csv.ToString(), new UTF8Encoding(true));
                File.WriteAllText(Path.Combine(folder, "milestones.txt"), sb.ToString(), new UTF8Encoding(true));
            }
            return sb.ToString();
        }

        /// <summary>그 시각에 도달해 있던 등급 이름 (공백이 어느 등급에서 생겼는지).</summary>
        private static string GradeAt(BalanceMilestones r, float minutes)
        {
            string name = r._sim.Progression.GetGrade(0).DisplayName;
            foreach (var i in r.Items)
                if (i.Kind == "등급" && i.Minutes <= minutes + 1e-3f)
                    name = i.Name;
            return name;
        }
    }
}
