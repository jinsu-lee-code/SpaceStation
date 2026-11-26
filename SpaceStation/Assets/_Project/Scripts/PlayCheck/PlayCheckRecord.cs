using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SpaceStation.PlayCheck
{
    /// <summary>내부에 들어가기 직전 정거장 상황 (CONNECT_1121 V3 ①). 여럿이 겹칠 수 있다.</summary>
    [Flags]
    public enum EntryContext
    {
        None = 0,
        Damaged = 1,      // 파손된 모듈이 있음
        Crates = 2,       // 줍지 않은 보급 상자가 있음
        Synthesized = 4,  // 합성이 막 끝남 (게임 시간 2분 안)
        Incident = 8,     // 내부 상황이 났음 (지금은 안에서만 나므로 0 — 상황 개편 때 채움)
    }

    /// <summary>
    /// 플레이 기록 (CONNECT_1121 V3, 사용자 결정 2026-11-26 — 검증용 빌드에서만 켬). Unity 없이 쓰는 순수 기록:
    /// ① 내부 진입 횟수와 직전 상황 ② 등급별 바깥 · 안 시간 ③ 상황(내부 사고)을 푼 · 놓친 횟수 ⑤ 창을 연 횟수 (연구 창 포함) + 일시정지 · 배속 시간,
    /// 그리고 끝 질문의 답. ④ 광맥 · ③의 좋은 · 나쁜 이웃 변형 비율은 그 기능이 생기면 채운다.
    /// 시간은 실제 초(사람이 쓴 시간)와 게임 초를 함께 적는다.
    /// </summary>
    public sealed class PlayCheckRecord
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>끝 질문 (Claude 세션으로 못 보는 항목만 — CONNECT_1121 V3).</summary>
        public static readonly string[] EndQuestions =
        {
            "안에 들어간 이유가 있었다면 무엇이었나요? (구경 말고 다른 이유가 있었다면 그것도)",
            "지루하거나 늘어졌던 구간이 있었나요? 언제, 무엇을 기다렸나요?",
            "한 번에 익히기 벅찼던 것이 있었나요?",
        };

        public string Started { get; }
        public string Build { get; }
        public float GameSeconds { get; private set; }
        public float RealSeconds { get; private set; }
        public int Grade { get; private set; }
        public int MaxGrade { get; private set; }
        public bool Inside { get; private set; }
        public int Games { get; private set; }

        public int Entries { get; private set; }
        public readonly Dictionary<EntryContext, int> EntryContexts = new Dictionary<EntryContext, int>();
        public int EntriesWithNothing { get; private set; }
        public int IncidentsResolved { get; private set; }
        public int IncidentsMissed { get; private set; }
        public readonly SortedDictionary<string, int> WindowsOpened = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public float PausedRealSeconds { get; private set; }
        /// <summary>게임 밖(메인 메뉴 · 불러오기) 실제 시간 — 바깥 · 안 · 일시정지에 넣지 않음 (에디터 확인 2026-11-26: 메뉴 시간이 바깥 시간으로 잡혔음).</summary>
        public float MenuRealSeconds { get; private set; }
        public readonly SortedDictionary<float, float> SpeedRealSeconds = new SortedDictionary<float, float>();
        /// <summary>등급마다 (바깥 실제 초, 안 실제 초, 바깥 게임 초, 안 게임 초).</summary>
        public readonly SortedDictionary<int, float[]> GradeTimes = new SortedDictionary<int, float[]>();
        public readonly List<string[]> Answers = new List<string[]>();
        private readonly List<string> _events = new List<string>();
        private readonly Func<int, string> _gradeName;

        public IReadOnlyList<string> Events => _events;

        public PlayCheckRecord(string started, string build, Func<int, string> gradeName)
        {
            Started = started;
            Build = build;
            _gradeName = gradeName ?? (g => g.ToString(Inv));
        }

        public string GradeName(int grade) => _gradeName(grade);

        /// <summary>새 게임 (또는 불러온 게임)을 시작. 같은 파일에 이어 적는다.</summary>
        public void BeginGame(string label, int grade, float gameSeconds)
        {
            Games++;
            Grade = grade;
            MaxGrade = Math.Max(MaxGrade, grade);
            GameSeconds = gameSeconds;
            Inside = false;
            Log($"게임 {Games} 시작: {label} (등급 {GradeName(grade)})");
        }

        /// <summary>시간 흐름: 실제 <paramref name="realDelta"/>초 동안 게임 시간이 <paramref name="gameSeconds"/>가 됨.</summary>
        public void Advance(float realDelta, float gameSeconds, bool paused, float speed)
        {
            if (realDelta <= 0f)
                return;
            float gameDelta = Math.Max(0f, gameSeconds - GameSeconds);
            GameSeconds = gameSeconds;
            RealSeconds += realDelta;
            if (!GradeTimes.TryGetValue(Grade, out var t))
                GradeTimes[Grade] = t = new float[4];
            t[Inside ? 1 : 0] += realDelta;
            t[Inside ? 3 : 2] += gameDelta;
            if (paused)
            {
                PausedRealSeconds += realDelta;
            }
            else
            {
                SpeedRealSeconds.TryGetValue(speed, out float s);
                SpeedRealSeconds[speed] = s + realDelta;
            }
        }

        /// <summary>게임 밖에 있는 동안: 실제 시간만 센다.</summary>
        public void AdvanceMenu(float realDelta)
        {
            if (realDelta <= 0f)
                return;
            RealSeconds += realDelta;
            MenuRealSeconds += realDelta;
        }

        public void GradeChanged(int grade)
        {
            if (grade == Grade)
                return;
            Log($"등급 {GradeName(Grade)} → {GradeName(grade)}");
            Grade = grade;
            MaxGrade = Math.Max(MaxGrade, grade);
        }

        public void Entered(string room, EntryContext context, string detail)
        {
            Inside = true;
            Entries++;
            if (context == EntryContext.None)
            {
                EntriesWithNothing++;
            }
            else
            {
                foreach (EntryContext flag in Enum.GetValues(typeof(EntryContext)))
                {
                    if (flag == EntryContext.None || (context & flag) == 0)
                        continue;
                    EntryContexts.TryGetValue(flag, out int n);
                    EntryContexts[flag] = n + 1;
                }
            }
            Log($"안에 들어감: {room} (직전: {(context == EntryContext.None ? "아무 일 없음" : detail)})");
        }

        public void Exited(string room)
        {
            Inside = false;
            Log($"밖으로 나옴: {room}");
        }

        public void IncidentResolved(string what)
        {
            IncidentsResolved++;
            Log($"내부 상황 처리: {what}");
        }

        public void IncidentMissed(string what)
        {
            IncidentsMissed++;
            Log($"내부 상황 놓침: {what}");
        }

        public void WindowOpened(string window)
        {
            WindowsOpened.TryGetValue(window, out int n);
            WindowsOpened[window] = n + 1;
        }

        public void Answered(string when, string[] answers)
        {
            Answers.Add(answers);
            Log($"끝 질문에 답함 ({when})");
        }

        public void Note(string text) => Log(text);

        private void Log(string text)
        {
            _events.Add($"[게임 {Clock(GameSeconds)} | 실제 {Clock(RealSeconds)}] {text}");
        }

        public static string Clock(float seconds)
        {
            int s = (int)Math.Max(0f, seconds);
            return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60:00}:{s % 60:00}";
        }

        private static string Minutes(float seconds) => (seconds / 60f).ToString("0.0", Inv);

        public static string ContextName(EntryContext flag)
        {
            switch (flag)
            {
                case EntryContext.Damaged: return "파손";
                case EntryContext.Crates: return "보급 상자";
                case EntryContext.Synthesized: return "합성 직후";
                case EntryContext.Incident: return "상황 발생";
                default: return "아무 일 없음";
            }
        }

        /// <summary>사람 · Claude가 읽는 기록 파일 본문.</summary>
        public string ToText(string savedAt)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Another Earth 플레이 기록 (검증용 빌드)");
            sb.AppendLine($"시작 {Started} · 빌드 {Build} · 마지막 저장 {savedAt}");
            sb.AppendLine();
            sb.AppendLine("## 요약");
            sb.AppendLine($"- 실제 시간 {Minutes(RealSeconds)}분 · 게임 시간 {Clock(GameSeconds)} · 게임 {Games}판 · 최고 등급 {GradeName(MaxGrade)}");
            var ctx = new List<string>();
            foreach (var kv in EntryContexts)
                ctx.Add($"{ContextName(kv.Key)} {kv.Value}");
            ctx.Add($"아무 일 없음 {EntriesWithNothing}");
            sb.AppendLine($"- ① 내부 진입 {Entries}회 · 직전 상황: {string.Join(" · ", ctx)} (겹치면 둘 다 셈)");
            sb.Append("- ② 등급별 바깥 / 안 (실제 분, 괄호 = 게임 분):");
            foreach (var kv in GradeTimes)
                sb.Append($" {GradeName(kv.Key)} {Minutes(kv.Value[0])} / {Minutes(kv.Value[1])} ({Minutes(kv.Value[2])} / {Minutes(kv.Value[3])})");
            sb.AppendLine();
            sb.AppendLine($"- ③ 내부 상황 처리 {IncidentsResolved} · 놓침 {IncidentsMissed} (좋은 · 나쁜 이웃 변형 비율 = 상황 개편 뒤)");
            sb.AppendLine("- ④ 광맥 가지 = 광맥이 생긴 뒤");
            var windows = new List<string>();
            foreach (var kv in WindowsOpened)
                windows.Add($"{kv.Key} {kv.Value}");
            sb.AppendLine($"- ⑤ 창을 연 횟수: {(windows.Count > 0 ? string.Join(" · ", windows) : "없음")} (지름길로 연 연구 = 연구 트리 뒤)");
            var speeds = new List<string>();
            foreach (var kv in SpeedRealSeconds)
                speeds.Add($"{kv.Key.ToString("0.#", Inv)}배 {Minutes(kv.Value)}분");
            sb.AppendLine($"- 일시정지 {Minutes(PausedRealSeconds)}분 · 배속별 {(speeds.Count > 0 ? string.Join(" · ", speeds) : "-")} · 게임 밖(메뉴 · 불러오기) {Minutes(MenuRealSeconds)}분");
            sb.AppendLine();
            sb.AppendLine("## 끝 질문");
            if (Answers.Count == 0)
                sb.AppendLine("(아직 답하지 않음)");
            for (int a = 0; a < Answers.Count; a++)
            {
                if (Answers.Count > 1)
                    sb.AppendLine($"### {a + 1}번째 답");
                for (int q = 0; q < EndQuestions.Length; q++)
                {
                    string answer = q < Answers[a].Length ? Answers[a][q] : "";
                    sb.AppendLine($"{q + 1}. {EndQuestions[q]}");
                    sb.AppendLine($"   → {(string.IsNullOrWhiteSpace(answer) ? "(빈칸)" : answer.Trim().Replace("\n", "\n     "))}");
                }
            }
            sb.AppendLine();
            sb.AppendLine("## 사건 (시간순)");
            foreach (var e in _events)
                sb.AppendLine(e);
            return sb.ToString();
        }
    }
}
