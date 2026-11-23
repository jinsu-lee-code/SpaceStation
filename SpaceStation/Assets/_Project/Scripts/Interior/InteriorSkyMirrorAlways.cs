using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 하늘 화면 복제 (<see cref="InteriorExteriorView"/> 방 복제)에 높이와 상관없이 넣음 — 바닥 칸 천장보다 낮아도 높은 곳 (곶 윗면 · 등대)에서
    /// 하늘 면 (칸 턱) 너머로 보이는 렌더러. 복제가 없으면 그 너머가 바깥 화면으로 덮여 직선으로 잘려 보였음 (궤도 방어망 오로라 보호막, 2026-11-13).
    /// 메시 · 재질 · 상태 블록은 원본과 같이 씀 (꼭짓점 색을 매 프레임 바꾸는 막도 그대로).
    /// </summary>
    public sealed class InteriorSkyMirrorAlways : MonoBehaviour
    {
    }
}
