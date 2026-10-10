using System;

namespace SpaceStation.UI
{
    /// <summary>
    /// 11-15 바깥 HUD 창(연구 · 주민 등)은 한 번에 하나만: 창이 열릴 때 알리면 다른 창은 스스로 닫는다 (겹쳐 보이지 않게).
    /// </summary>
    public static class HudWindows
    {
        public static event Action<object> Opened;

        public static void NotifyOpened(object window) => Opened?.Invoke(window);
    }
}
