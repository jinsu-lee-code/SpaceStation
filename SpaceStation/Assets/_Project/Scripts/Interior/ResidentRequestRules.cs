using System;
using System.Collections.Generic;
using SpaceStation.Data;

namespace SpaceStation.Interior
{
    /// <summary>11-17 ③ 주민 요청 종류 (사용자 결정 2026-10-11).</summary>
    public enum RequestKind
    {
        /// <summary>시든 화분에 물 주기 (화분이 있는 방만).</summary>
        Water,
        /// <summary>다른 방에 두고 온 물건 가져다주기.</summary>
        Fetch,
        /// <summary>덜컹거리는 환풍구 손보기.</summary>
        Fix,
        /// <summary>말동무 (언제나 가능).</summary>
        Talk,
    }

    /// <summary>11-17 ③ 가져다줄 물건.</summary>
    public enum FetchItem
    {
        Toolbox,
        Medkit,
        Snack,
    }

    /// <summary>
    /// 11-17 ③ 주민 요청 규칙 (순수 계산, 테스트 대상). 간격 · 개수 · 만료 수치는 BALANCE 30번(<see cref="BalanceConfig"/>).
    /// - 종류: 할 수 있는 것(그 방에 화분 · 다른 방 · 벽 자리가 있는지) 중에서, 지금 걸린 요청과 겹치지 않게 무게대로
    /// - 말동무는 언제나 가능하지만 무게가 작음 (다른 일이 없을 때 주로)
    /// </summary>
    public static class ResidentRequestRules
    {
        public const float WaterWeight = 1f;
        public const float FetchWeight = 1f;
        public const float FixWeight = 0.8f;
        public const float TalkWeight = 0.6f;

        /// <param name="r">0~1 난수</param>
        public static float NextDelay(float min, float max, double r) => min + (Math.Max(min, max) - min) * (float)Math.Max(0.0, Math.Min(1.0, r));

        /// <summary>새 요청을 걸 수 있는지 (최대 수 미만).</summary>
        public static bool CanAdd(int active, int maxActive) => active < Math.Max(1, maxActive);

        /// <summary>
        /// 종류 고르기: 할 수 있고 지금 걸린 요청과 다른 종류 중 무게대로. 다 겹치면 할 수 있는 것 중에서. 없으면 null.
        /// </summary>
        /// <param name="r">0~1 난수</param>
        public static RequestKind? Pick(bool water, bool fetch, bool fix, ICollection<RequestKind> active, double r)
        {
            var pool = new List<(RequestKind Kind, float Weight)>(4);
            for (int pass = 0; pass < 2 && pool.Count == 0; pass++)
            {
                bool allowRepeat = pass == 1;
                void Add(RequestKind k, bool ok, float w)
                {
                    if (ok && (allowRepeat || active == null || !active.Contains(k)))
                        pool.Add((k, w));
                }
                Add(RequestKind.Water, water, WaterWeight);
                Add(RequestKind.Fetch, fetch, FetchWeight);
                Add(RequestKind.Fix, fix, FixWeight);
                Add(RequestKind.Talk, true, TalkWeight);
            }
            float total = 0f;
            foreach (var p in pool)
                total += p.Weight;
            float roll = (float)Math.Max(0.0, Math.Min(0.999999, r)) * total;
            foreach (var p in pool)
            {
                if (roll < p.Weight)
                    return p.Kind;
                roll -= p.Weight;
            }
            return pool.Count > 0 ? pool[pool.Count - 1].Kind : (RequestKind?)null;
        }

        /// <summary>가져다줄 물건: 다른 요청이 쓰지 않는 것 중에서 (다 쓰면 아무거나).</summary>
        public static FetchItem PickItem(ICollection<FetchItem> used, double r)
        {
            var pool = new List<FetchItem>(3);
            foreach (FetchItem i in Enum.GetValues(typeof(FetchItem)))
            {
                if (used == null || !used.Contains(i))
                    pool.Add(i);
            }
            if (pool.Count == 0)
                pool.AddRange((FetchItem[])Enum.GetValues(typeof(FetchItem)));
            return pool[Math.Min(pool.Count - 1, (int)(Math.Max(0.0, r) * pool.Count))];
        }

        public static string ItemName(FetchItem item)
        {
            switch (item)
            {
                case FetchItem.Toolbox: return "공구함";
                case FetchItem.Medkit: return "구급함";
                default: return "간식 바구니";
            }
        }

        /// <summary>말풍선 둘째 줄 (부탁 내용).</summary>
        public static string Ask(RequestKind kind, FetchItem item, string place)
        {
            switch (kind)
            {
                case RequestKind.Water: return "화분이 시들었어요\n물 좀 줄래요?";
                case RequestKind.Fetch: return $"{ItemName(item)}{Josa(ItemName(item))} {place}에 두고 왔어요\n가져다줄래요?";
                case RequestKind.Fix: return "환풍구가 덜컹거려요\n좀 봐줄래요?";
                default: return "심심해요...\n얘기 좀 해요";
            }
        }

        /// <summary>말풍선 첫 줄 (요청 이름).</summary>
        public static string Title(RequestKind kind)
        {
            switch (kind)
            {
                case RequestKind.Water: return "화분 물 주기";
                case RequestKind.Fetch: return "물건 가져다주기";
                case RequestKind.Fix: return "소품 손보기";
                default: return "말동무";
            }
        }

        private static string Josa(string word) => Particle(word, "을", "를");

        /// <summary>받침에 맞는 조사 (받침 있음 = <paramref name="withFinal"/>, 없음 = <paramref name="withoutFinal"/>). 한글이 아니면 받침 없음으로.</summary>
        public static string Particle(string word, string withFinal, string withoutFinal)
        {
            if (string.IsNullOrEmpty(word))
                return withoutFinal;
            char c = word[word.Length - 1];
            return c >= 0xAC00 && c <= 0xD7A3 && (c - 0xAC00) % 28 != 0 ? withFinal : withoutFinal;
        }

        // ---------------- 말동무 대사 ----------------

        private static readonly Dictionary<ResidentTrait, string[]> TraitLines = new Dictionary<ResidentTrait, string[]>
        {
            { ResidentTrait.Technician, new[] { "어제 배관 이음을 새로 갈았어요. 소리가 조용해졌죠?", "고장 난 건 다 고칠 수 있어요. 시간만 있으면요!" } },
            { ResidentTrait.Scientist, new[] { "연구소 표본이 오늘 조금 자랐어요!", "별빛 스펙트럼을 재 봤는데... 아, 지루하죠?" } },
            { ResidentTrait.Gardener, new[] { "상추가 잘 자라요. 다음엔 토마토를 심을래요", "식물한테 말을 걸면 더 잘 자란대요" } },
            { ResidentTrait.Mechanic, new[] { "볼트 하나 풀린 것도 소리로 알 수 있어요", "정비는 미리미리가 제일이에요" } },
            { ResidentTrait.Optimist, new[] { "오늘도 별이 예쁘네요!", "여기 사는 게 정말 좋아요" } },
            { ResidentTrait.Complainer, new[] { "공기 필터 냄새가 또 나요... 그래도 들어줘서 고마워요", "음식이 조금만 더 맛있으면 좋겠어요" } },
            { ResidentTrait.BigEater, new[] { "오늘 저녁 메뉴 알아요? 배고파요...", "간식 창고 열쇠는 누가 갖고 있죠?" } },
            { ResidentTrait.LightEater, new[] { "저는 조금만 먹어도 배불러요", "물 한 잔이면 충분해요" } },
            { ResidentTrait.Sociable, new[] { "휴게실에서 같이 놀아요!", "다들 모이면 정말 즐거워요" } },
            { ResidentTrait.GravityLover, new[] { "회전 링에 가면 발이 땅에 붙어서 좋아요", "둥둥 뜨는 건 아직도 어색해요" } },
            { ResidentTrait.RadiationSensitive, new[] { "핵융합로 근처는 아무래도 불안해요", "차폐벽이 튼튼하겠죠?" } },
            { ResidentTrait.RadiationTolerant, new[] { "저는 핵융합로 옆도 괜찮아요. 따뜻하잖아요", "계기판 숫자가 높아도 끄떡없어요" } },
            { ResidentTrait.NoiseSensitive, new[] { "제련소 소리가 멀어서 다행이에요", "조용한 게 제일 좋아요" } },
            { ResidentTrait.NatureLover, new[] { "초록 잎을 보면 마음이 편해져요", "농장 흙 냄새가 좋아요" } },
        };

        private static readonly string[] CommonLines =
        {
            "창밖 별이 오늘따라 반짝여요",
            "여기 온 지 꽤 됐네요. 이제 집 같아요",
            "정거장이 점점 커지는 게 보여요!",
            "가끔 지구가 그리워요",
        };

        /// <summary>말동무 대사: 특성 대사가 있으면 그중에서, 아니면 공통 대사.</summary>
        public static string TalkLine(IReadOnlyList<ResidentTrait> traits, double r)
        {
            var pool = new List<string>();
            if (traits != null)
            {
                foreach (var t in traits)
                {
                    if (TraitLines.TryGetValue(t, out var lines))
                        pool.AddRange(lines);
                }
            }
            if (pool.Count == 0)
                pool.AddRange(CommonLines);
            return pool[Math.Min(pool.Count - 1, (int)(Math.Max(0.0, r) * pool.Count))];
        }

        /// <summary>요청을 들어줬을 때 고맙다는 말.</summary>
        public static string Thanks(RequestKind kind)
        {
            switch (kind)
            {
                case RequestKind.Water: return "고마워요! 잎이 금방 살아나겠어요";
                case RequestKind.Fetch: return "찾던 거예요! 고마워요";
                case RequestKind.Fix: return "이제 조용하네요. 고마워요!";
                default: return "얘기 들어줘서 고마워요";
            }
        }
    }
}
