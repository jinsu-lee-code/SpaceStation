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

        [Header("테크 테두리 (레퍼런스: 깎인 모서리 · 이음점 · 사선 줄무늬 · 회로 선)")]
        [Tooltip("깎인 모서리 바탕 (왼쪽 위 · 오른쪽 아래 크게, 색은 Image.color)")]
        public Sprite TechFill;
        [Tooltip("깎인 모서리 테두리 + 안쪽 가는 선 + 큰 모서리 굵은 강조 + 이음점")]
        public Sprite TechFrame;
        [Tooltip("사선 줄무늬 (반복, RawImage uvRect)")]
        public Texture2D Hatch;
        [Tooltip("회로 선 끝 점")]
        public Sprite Dot;
        [Tooltip("테크 버튼 (깎인 모서리 · 왼쪽 위 굵은 강조 · 오른쪽 아래 사선 줄무늬, 색은 Image.color)")]
        public Sprite TechButton;
        [Tooltip("모서리 브래킷 (왼쪽 위 ㄱ자, 돌려서 네 모서리)")]
        public Sprite Bracket;
    }
}
