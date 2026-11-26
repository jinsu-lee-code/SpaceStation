using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 닫힌 방 (기본 · 1단계, 소리 풍경이 없는 방) 밤 층 규칙 (2026-11-26, SOUND_LIST_NEW 4-4 ③ · D-5 ③ "밤 = 정원은 풀벌레, 기계 방은 조용히").
    /// 밤 판단 = 바깥 하늘 · 소리 풍경과 같은 게임 낮 / 밤 (<see cref="InteriorAudio.Tick"/>의 day), 5초에 걸쳐 넘어감.
    /// 성격마다 밤에 바탕 배율 · 가끔 소리 빠르기 (1 = 낮과 같음, 0.5 = 절반만 — 드물게) · 밤 루프 (생활 = 불 끈 숙소로 바꿈, 물 · 식물 = 밤 풀벌레를 옅게 겹침).
    /// 기계 · 전기 방은 가장 조용해지고, 고열 (불은 계속 탐) · 넓은 공간은 조금만. 소리 풍경이 있는 2단계 이상 방은 그 에셋의 Day / Night 값을 씀.
    /// </summary>
    public static class InteriorNightLayer
    {
        public enum LoopKind
        {
            None,      // 밤 루프 없음 — 낮 바탕을 낮추고 가끔 소리만 드물게
            Replace,   // 밤 루프로 바꿈 (낮 바탕을 많이 낮춤)
            Overlay,   // 낮 바탕 위에 밤 루프를 겹침
        }

        public readonly struct Rule
        {
            public readonly float Bed;       // 밤 바탕 배율
            public readonly float SpotRate;  // 밤 가끔 소리 빠르기 (타이머가 흐르는 속도)
            public readonly LoopKind Loop;

            public Rule(float bed, float spotRate, LoopKind loop)
            {
                Bed = bed;
                SpotRate = spotRate;
                Loop = loop;
            }
        }

        /// <summary>낮 ↔ 밤 넘어가는 빠르기 (초당, 5초) — 소리 풍경 (<see cref="InteriorSoundscape"/>)과 같음.</summary>
        public const float FadePerSecond = 0.2f;

        public static Rule For(InteriorAmbience ambience)
        {
            switch (ambience)
            {
                case InteriorAmbience.Life: return new Rule(0.35f, 0.4f, LoopKind.Replace);       // 거주 · 의료 · 휴게 = 불 끈 숙소 (공조만 낮게) · 그릇 · 서랍 소리 드물게
                case InteriorAmbience.Water: return new Rule(0.75f, 0.6f, LoopKind.Overlay);      // 농장 · 물 · 산소 = 펌프 조금 낮게 + 수경 재배실 밤 풀벌레
                case InteriorAmbience.Machine: return new Rule(0.6f, 0.45f, LoopKind.None);       // 창고 · 정비 · 화물 · 연구 = 조용해짐
                case InteriorAmbience.Heat: return new Rule(0.8f, 0.6f, LoopKind.None);           // 제련 · 핵융합 = 불은 계속 — 조금만
                case InteriorAmbience.Electric: return new Rule(0.6f, 0.45f, LoopKind.None);      // 배터리 · 태양광 · 실드 = 조용해짐
                default: return new Rule(0.75f, 0.6f, LoopKind.None);                              // 넓은 공간 (코어 · 회전 링)
            }
        }

        /// <summary>낮 값 → 밤 값 (night 0 = 낮, 1 = 밤).</summary>
        public static float Mix(float day, float night, float nightLevel) => Mathf.Lerp(day, night, Mathf.Clamp01(nightLevel));

        /// <summary>밤 정도를 낮 / 밤 쪽으로 한 걸음 (5초에 0 ↔ 1).</summary>
        public static float Step(float nightLevel, bool day, float dt) => Mathf.MoveTowards(nightLevel, day ? 0f : 1f, FadePerSecond * Mathf.Max(0f, dt));
    }
}
