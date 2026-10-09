using TMPro;
using UnityEngine;

namespace SpaceStation.UI
{
    /// <summary>
    /// 11-13 홀로그램 UI 테마 아트 묶음 (메뉴 SpaceStation/UI/Build Holo Theme Art가 만들고 채움). 모두 코드로 그린 그림이다.
    /// 패널 바탕 · 테두리 · 버튼(5-8) + 주사선(반복) · 빛 번짐(9-slice) · 격자(반복) · 스캔 띠.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/UI/Holo Art", fileName = "HoloArt")]
    public sealed class HoloArt : ScriptableObject
    {
        public TMP_FontAsset Font;
        [Tooltip("모서리 깎인 바탕 (색은 Image.color)")]
        public Sprite Fill;
        [Tooltip("테두리 + 모서리 브래킷")]
        public Sprite Frame;
        public Sprite Button;
        [Tooltip("가장자리 빛 번짐 (패널 뒤에 크게 깔아 Bloom과 함께 빛남)")]
        public Sprite Glow;
        [Tooltip("주사선 (가로줄, 반복 — RawImage uvRect로 흘림)")]
        public Texture2D Scan;
        [Tooltip("격자 (반복)")]
        public Texture2D Grid;
        [Tooltip("스캔 띠 (세로 그러데이션, 위아래로 지나감)")]
        public Sprite Sweep;
    }
}
