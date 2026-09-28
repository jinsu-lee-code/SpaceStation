using UnityEngine.EventSystems;

namespace SpaceStation.Building
{
    /// <summary>마우스가 HUD(uGUI) 위에 있는지. 월드 클릭(배치/선택)을 막는 데 쓴다.</summary>
    public static class UiPointer
    {
        public static bool IsOverUi()
        {
            var eventSystem = EventSystem.current;
            return eventSystem != null && eventSystem.IsPointerOverGameObject();
        }
    }
}
