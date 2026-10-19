using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 13-7a 꺼져 가는 지구의 불빛 = 시각적 시계 (REDESIGN T8 · M1, 사용자 결정 2026-10-19: 장 안에서 서서히 + 장 전환 때 크게).
    /// 장마다 시작 값 → 끝 값으로 장 시간에 따라 서서히 내려가고, 다음 장이 시작되면 그 장의 시작 값으로 크게 떨어진다 (화면은 몇 초에 걸쳐 꺼짐).
    /// 장 시간 · 장 번호에서 바로 나오는 값이라 따로 저장하지 않는다. 장 틀이 없으면 1 (모두 켜짐).
    /// </summary>
    public static class EarthLights
    {
        public static float Value(ChapterSystem chapters)
        {
            if (chapters == null || chapters.Count == 0)
                return 1f;
            var chapter = chapters.Current;
            if (chapters.Phase == ChapterPhase.Transition || chapters.Phase == ChapterPhase.Finished)
                return chapter.EarthLightsEnd;
            return Mathf.Lerp(chapter.EarthLightsStart, chapter.EarthLightsEnd, Mathf.Clamp01(chapters.ChapterSeconds / chapter.EarthDimSeconds));
        }
    }
}
