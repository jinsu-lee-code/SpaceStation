using System;
using SpaceStation.Data;

namespace SpaceStation.Core
{
    /// <summary>11-18 튜토리얼 중 막을 수 있는 조작.</summary>
    public enum TutorialAction
    {
        Build,
        Repair,
        Maintain,
        Rebuild,
        Demolish,
        Research,
        Roster,
        EnterInterior,
        /// <summary>배속 올리기 (일시정지는 늘 가능).</summary>
        Speed,
    }

    /// <summary>
    /// 11-18 튜토리얼 중에는 그 단계에 해당하는 조작만 (사용자 결정 2026-10-11 — 튜토리얼 상태로 쭉 진행하지 못하게, 건너뛰면 자유).
    /// 판정은 시뮬레이션의 <c>TutorialRunner.Allows</c>를 게임 씬 호스트가 연결하고, 화면 쪽(건설 · 선택 패널 · 창 · 배속)은 여기서 묻는다.
    /// 막히면 <see cref="Blocked"/>로 상태 표시줄에 알림.
    /// </summary>
    public static class TutorialGate
    {
        public const string Message = "튜토리얼 중에는 지금 단계의 조작만 할 수 있습니다 (건너뛰기를 누르면 자유롭게)";

        /// <summary>판정 (null이면 모두 허용 — 튜토리얼 없음).</summary>
        public static Func<TutorialAction, ModuleData, bool> Allows;

        /// <summary>막힌 조작 알림 (글).</summary>
        public static event Action<string> Blocked;

        /// <summary>허용이면 true. 막히면 알림을 보내고 false.</summary>
        public static bool Check(TutorialAction action, ModuleData data = null)
        {
            if (IsAllowed(action, data))
                return true;
            Blocked?.Invoke(Message);
            return false;
        }

        /// <summary>알림 없이 판정만 (버튼 활성 상태 등).</summary>
        public static bool IsAllowed(TutorialAction action, ModuleData data = null) => Allows == null || Allows(action, data);
    }
}
