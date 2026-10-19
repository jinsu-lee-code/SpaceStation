using System;
using System.Collections.Generic;
using SpaceStation.Data;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 13-7c 주민 한마디 고르기 (사용자 결정 2026-10-19: 장 시작 · 절정 예고 · 결과 · 이민선 때 한 명이, 종족 말투로).
    /// 사건 키를 앞에서부터 보며(예: 지난 절정 결과가 붙은 <c>chapter.start.2.Clean</c> → <c>chapter.start.2</c>) 그 키 문장을 가진 종족의 주민 중 하나를 고른다.
    /// 화면과 상관없는 순수 계산 — 누가 보이는지(내부 인물) 거르기는 부르는 쪽이 후보로 넘긴다.
    /// </summary>
    public static class ResidentRemarks
    {
        /// <param name="candidates">말할 수 있는 주민 (부른 쪽이 거름 — 안이면 보이는 인물만 등)</param>
        /// <param name="find">종류 Id → 종족 (없으면 null)</param>
        /// <param name="keys">앞쪽이 먼저 (그 키 문장을 가진 후보가 있으면 거기서 끝)</param>
        /// <param name="exclude">말하지 않을 종류 (예: 처음 온 종족을 맞는 쪽은 기존 주민)</param>
        public static bool Pick(IReadOnlyList<Resident> candidates, Func<string, SpeciesData> find, IReadOnlyList<string> keys, Func<float> random01,
            out Resident speaker, out string text, string exclude = null)
        {
            speaker = null;
            text = null;
            if (candidates == null || find == null || keys == null)
                return false;
            var pool = new List<(Resident who, string line)>();
            foreach (var key in keys)
            {
                pool.Clear();
                foreach (var r in candidates)
                {
                    if (r == null || (exclude != null && r.Kind == exclude))
                        continue;
                    var species = find(r.Kind);
                    if (species == null)
                        continue;
                    foreach (var remark in species.Remarks)
                        if (remark != null && remark.Key == key && !string.IsNullOrEmpty(remark.Text))
                            pool.Add((r, remark.Text));
                }
                if (pool.Count == 0)
                    continue;
                var pick = pool[Math.Min(pool.Count - 1, (int)(Math.Max(0f, random01()) * pool.Count))];
                speaker = pick.who;
                text = pick.line;
                return true;
            }
            return false;
        }

        /// <summary>장 시작: 지난 장 절정 결과가 붙은 키 먼저 (n = 시작한 장 번호, 1부터).</summary>
        public static string[] ChapterStartKeys(int number, ChapterOutcome? previous) => previous.HasValue
            ? new[] { $"chapter.start.{number}.{previous.Value}", $"chapter.start.{number}" }
            : new[] { $"chapter.start.{number}" };

        public static string[] WarningKeys(int number) => new[] { $"climax.warning.{number}", "climax.warning" };

        /// <summary>이민선에 답한 뒤: 처음 만나는 종족을 받으면 <c>ship.new</c>, 아니면 답대로.</summary>
        public static string[] ShipKeys(ShipAnswer answer, bool newSpecies, int boarded)
        {
            if (newSpecies && boarded > 0)
                return new[] { "ship.new", "ship.accept" };
            switch (answer)
            {
                case ShipAnswer.Accept: return new[] { "ship.accept" };
                case ShipAnswer.Partial: return new[] { "ship.partial", "ship.accept" };
                default: return new[] { "ship.refuse" };
            }
        }
    }
}
