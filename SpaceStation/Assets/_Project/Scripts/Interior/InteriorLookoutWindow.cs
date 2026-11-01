using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 지붕 위로 솟은 전망실의 바깥 창 (진화 1단계 중력 거주구 — 사용자 2026-10-29 "2층은 아예 돌출되어 나와서 자유롭게 둘러보게").
    /// <see cref="InteriorExteriorView"/>가 이 창은 같은 방의 다른 창과 따로 묶어, 자기 모듈 바깥 모델도 보이게 하고 바깥 카메라를 <see cref="Lift"/>만큼 들어 올려 그린다:
    /// 실내 전망실은 칸 안(칸 위 경계 아래)에 있어 그대로 옮기면 바깥 카메라가 바깥 탑(꼭대기 돔 = 칸 중심 위 0.78칸) 속에 들어감 → 탑 꼭대기 위에서 링을 내려다보는 화면.
    /// 카메라가 이 창들 경계(위아래 여유 포함) 안에 있을 때만 그리고, 그동안 같은 방의 다른 창은 건너뜀 (전망실 안에서는 안 보임).
    /// </summary>
    public sealed class InteriorLookoutWindow : MonoBehaviour
    {
        [Tooltip("바깥 카메라를 들어 올리는 높이 (내부 m, 내부 1칸 8m)")]
        public float Lift = 3.8f;
    }
}
