using System;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 11-17 내부에서 바라보고 상호작용 키(F)를 길게 눌러 쓰는 물건 (현장 수리 지점 · 보급품 · 밸브 등).
    /// 같은 오브젝트나 부모에 Collider가 있어야 시선 광선에 잡힌다. 누르는 처리 · 안내 · 진행 막대는 <see cref="InteriorMode"/>가 한다.
    /// </summary>
    public sealed class InteriorInteractable : MonoBehaviour
    {
        /// <summary>안내 글 (키 표시 뒤). 예: "현장 수리 1/3".</summary>
        public Func<string> Prompt;
        /// <summary>지금 쓸 수 없는 이유 (null이면 쓸 수 있음). 예: "금속 부족".</summary>
        public Func<string> Blocked;
        /// <summary>끝까지 누르고 있어야 하는 시간(초).</summary>
        public float HoldSeconds = 1.5f;
        /// <summary>누르는 동안 매 프레임 (진행 0~1) — 용접 불꽃 등.</summary>
        public Action<float> Holding;
        /// <summary>다 눌렀을 때.</summary>
        public Action Completed;
    }
}
