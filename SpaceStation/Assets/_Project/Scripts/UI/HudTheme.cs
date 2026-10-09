using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.UI
{
    /// <summary>
    /// 5-8 SF 홀로그램 HUD 테마: 색·글자 크기·아이콘 이름을 한곳에서 관리한다.
    /// 아이콘은 TMP 기본 스프라이트 에셋(HUD 아이콘 아틀라스)의 이름으로 <c>&lt;sprite name=..&gt;</c>에 쓴다.
    /// </summary>
    public static class HudTheme
    {
        // 색 (Unity Color)
        public static readonly Color Accent = new Color(0.31f, 0.85f, 1f, 1f);          // 청록 홀로그램
        public static readonly Color AccentDim = new Color(0.31f, 0.85f, 1f, 0.45f);
        public static readonly Color PanelFill = new Color(0.03f, 0.07f, 0.11f, 0.82f);
        public static readonly Color ButtonNormal = new Color(0.22f, 0.45f, 0.58f, 1f);
        public static readonly Color ButtonSelected = new Color(0.3f, 0.85f, 1f, 1f);
        public static readonly Color ButtonWarning = new Color(1f, 0.6f, 0.2f, 1f);
        public static readonly Color Negative = new Color(1f, 0.33f, 0.3f, 1f);
        public static readonly Color Positive = new Color(0.45f, 1f, 0.6f, 1f);
        public static readonly Color Neutral = new Color(0.55f, 0.65f, 0.75f, 1f);

        // 11-13 홀로그램 명도 단계 (청록 한 가지 색의 밝기 위계 — 가장 밝음 → 가장 깊음)
        public static readonly Color HoloBright = new Color(0.72f, 0.97f, 1f, 1f);   // 강조 글 · 선택 테두리
        public static readonly Color HoloMid = new Color(0.16f, 0.58f, 0.78f, 1f);   // 버튼 · 칩 바탕
        public static readonly Color HoloLow = new Color(0.07f, 0.26f, 0.36f, 1f);   // 구분선 · 비활성 테두리
        public static readonly Color HoloDeep = new Color(0.012f, 0.045f, 0.075f, 1f); // 투사 화면 바탕
        public static readonly Color Warning = new Color(1f, 0.66f, 0.25f, 1f);      // 경고 주황 (= 노후 · 주의)
        public const string WarningHex = "#FFA840";
        public const string NegativeHex = "#FF544D";
        public const string MutedHex = "#8FA9BF";

        // 색 (리치 텍스트)
        public const string AccentHex = "#4FD8FF";
        public const string GreenHex = "#7CFF9A";
        public const string TextHex = "#E8F4FF";

        // 글자 크기 위계 (기준 해상도 1920×1080)
        public const float TitleSize = 24f;
        public const float BodySize = 19f;
        public const float SmallSize = 15f;

        /// <summary>자원 아이콘 태그.</summary>
        public static string Icon(ResourceType type) => $"<sprite name={IconName(type)}>";

        public static string Icon(string name) => $"<sprite name={name}>";

        public static string IconName(ResourceType type)
        {
            switch (type)
            {
                case ResourceType.Power: return "power";
                case ResourceType.Oxygen: return "oxygen";
                case ResourceType.Water: return "water";
                case ResourceType.Food: return "food";
                case ResourceType.Metal: return "metal";
                default: return "event";
            }
        }
    }
}
