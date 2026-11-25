using SpaceStation.Core;
using SpaceStation.Simulation;

namespace SpaceStation.Audio
{
    /// <summary>모듈 배치 · 철거 소리 크기 갈래 (SOUND_LIST_NEW D-6): 1칸 · 여러 칸 · 진화 모듈.</summary>
    public enum BuildSize
    {
        Small,
        Multi,
        Evolved,
    }

    /// <summary>
    /// 사운드 개편 1-1 A (2026-11-25): 배치 · 철거 소리를 모듈 크기로 고르고 (D-6 — 1칸 · 여러 칸 · 진화), 연달아 지을 때 0.15초 안 여러 번 = 한 번 (D-2).
    /// 판정은 순수 함수 (EditMode 시험 BuildSoundTests). 바깥 건설 (GameAudio) · 내부 패드 건설 (PadBuild) · 티저가 같은 규칙과 같은 문을 씀.
    /// </summary>
    public static class BuildSound
    {
        /// <summary>D-2: 이 안에 다시 오면 소리 한 번으로 묶음 (실시간 초).</summary>
        public const float BurstWindow = 0.15f;

        private static float _lastPlace = -999f;
        private static float _lastRemove = -999f;

        /// <summary>진화 모듈 (합성 결과)이면 진화, 아니면 점유 칸이 둘 이상이면 여러 칸.</summary>
        public static BuildSize SizeOf(int cellCount, bool evolved)
            => evolved ? BuildSize.Evolved : cellCount > 1 ? BuildSize.Multi : BuildSize.Small;

        public static BuildSize SizeOf(ModuleInstance module, SynthesisSystem synthesis)
        {
            if (module == null)
                return BuildSize.Small;
            bool evolved = synthesis != null && module.Data != null && synthesis.IsRecipeResult(module.Data);
            return SizeOf(module.Cells != null ? module.Cells.Count : 1, evolved);
        }

        /// <summary>배치 소리 칸 — 그 크기 칸에 클립이 없으면 한 단계 작은 칸 (진화 → 여러 칸 → 1칸).</summary>
        public static SoundCue PlaceCue(SoundLibrary lib, BuildSize size)
            => lib == null ? null : Pick(size, lib.BuildPlace, lib.BuildPlaceM, lib.BuildPlaceEvo);

        /// <summary>철거 소리 칸 (같은 대체 규칙).</summary>
        public static SoundCue RemoveCue(SoundLibrary lib, BuildSize size)
            => lib == null ? null : Pick(size, lib.BuildRemove, lib.BuildRemoveM, lib.BuildRemoveEvo);

        public static SoundCue Pick(BuildSize size, SoundCue small, SoundCue multi, SoundCue evolved)
        {
            if (size == BuildSize.Evolved && evolved != null && evolved.HasClip)
                return evolved;
            if (size != BuildSize.Small && multi != null && multi.HasClip)
                return multi;
            return small;
        }

        /// <summary>
        /// D-2 반복 막기: 마지막으로 울린 때 (last)에서 window 안이면 false (묶음 — 첫 소리 하나만), 아니면 last = now로 두고 true.
        /// now가 last보다 작으면 (플레이를 다시 켜 시계가 처음부터) 새로 울림.
        /// </summary>
        public static bool Admit(ref float last, float now, float window = BurstWindow)
        {
            if (now >= last && now - last < window)
                return false;
            last = now;
            return true;
        }

        /// <summary>배치 소리를 낼지 (모든 부르는 곳이 함께 쓰는 문 — 바깥 · 내부 패드 · 티저).</summary>
        public static bool AdmitPlace(float now) => Admit(ref _lastPlace, now);

        /// <summary>철거 소리를 낼지.</summary>
        public static bool AdmitRemove(float now) => Admit(ref _lastRemove, now);
    }
}
