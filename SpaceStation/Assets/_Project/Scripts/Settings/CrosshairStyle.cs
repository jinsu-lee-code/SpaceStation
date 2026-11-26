using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SpaceStation.Settings
{
    /// <summary>
    /// REVIEW_1115 1-8 조준점 꾸미기 (발로란트식) 한 벌의 값. 순수 C# (Unity 없이 시험) — 저장은 <see cref="CrosshairSettings"/>, 그리기는 UI.CrosshairView.
    /// 길이 · 두께 · 간격 · 크기는 화면 픽셀 (해상도 · UI 크기와 상관없이 또렷하게), 불투명은 0 ~ 100 %.
    /// 사격이 없는 게임이라 발로란트의 움직임 · 사격 오차는 없음.
    /// </summary>
    public struct CrosshairStyle : IEquatable<CrosshairStyle>
    {
        // ---------------- 범위 ----------------
        public const int OutlineMin = 1, OutlineMax = 6;
        public const int DotMin = 1, DotMax = 8;
        public const int LengthMin = 1, LengthMax = 20;
        public const int ThicknessMin = 1, ThicknessMax = 10;
        public const int InnerGapMax = 20;
        public const int OuterGapMax = 40;
        public const int OpacityMax = 100;

        /// <summary>안쪽 선 · 바깥 선 한 벌 (위 · 아래 · 왼 · 오른 네 줄이 같은 값).</summary>
        public struct Lines : IEquatable<Lines>
        {
            public bool On;
            public int Length;
            public int Thickness;
            /// <summary>가운데에서 선이 시작하는 곳까지 (픽셀).</summary>
            public int Gap;
            public int Opacity;

            public Lines(bool on, int length, int thickness, int gap, int opacity)
            {
                On = on;
                Length = length;
                Thickness = thickness;
                Gap = gap;
                Opacity = opacity;
            }

            public Lines Clamped(int gapMax) => new Lines(On,
                Clamp(Length, LengthMin, LengthMax), Clamp(Thickness, ThicknessMin, ThicknessMax),
                Clamp(Gap, 0, gapMax), Clamp(Opacity, 0, OpacityMax));

            public bool Equals(Lines o) => On == o.On && Length == o.Length && Thickness == o.Thickness && Gap == o.Gap && Opacity == o.Opacity;
            public override bool Equals(object obj) => obj is Lines o && Equals(o);
            public override int GetHashCode() => (On ? 1 : 0) ^ (Length << 1) ^ (Thickness << 6) ^ (Gap << 11) ^ (Opacity << 17);
        }

        public bool Visible;
        /// <summary>0xRRGGBB.</summary>
        public int Color;
        public bool OutlineOn;
        public int OutlineThickness;
        public int OutlineOpacity;
        public bool DotOn;
        public int DotSize;
        public int DotOpacity;
        public Lines Inner;
        public Lines Outer;

        /// <summary>범위를 벗어난 값을 가장 가까운 끝으로 (저장값이 손상돼도 그릴 수 있게).</summary>
        public CrosshairStyle Clamped()
        {
            var s = this;
            s.Color &= 0xFFFFFF;
            s.OutlineThickness = Clamp(s.OutlineThickness, OutlineMin, OutlineMax);
            s.OutlineOpacity = Clamp(s.OutlineOpacity, 0, OpacityMax);
            s.DotSize = Clamp(s.DotSize, DotMin, DotMax);
            s.DotOpacity = Clamp(s.DotOpacity, 0, OpacityMax);
            s.Inner = s.Inner.Clamped(InnerGapMax);
            s.Outer = s.Outer.Clamped(OuterGapMax);
            return s;
        }

        // ---------------- 기본 모양 (읽기 전용 틀) ----------------

        public static readonly string[] BuiltInNames = { "점", "십자", "작은 십자 + 점", "넓은 십자" };
        public static int BuiltInCount => BuiltInNames.Length;

        /// <summary>
        /// 0 = 점 (= 기본값, 예전 조준점과 비슷하게 흰 점 + 얇은 검은 외곽선), 1 = 십자, 2 = 작은 십자 + 점, 3 = 넓은 십자 (바깥 선까지).
        /// 꺼 둔 선에도 알맞은 값을 넣어 둠 — 켜기만 하면 바로 쓸 만한 모양.
        /// </summary>
        public static CrosshairStyle BuiltIn(int index)
        {
            var s = new CrosshairStyle
            {
                Visible = true,
                Color = 0xFFFFFF,
                OutlineOn = true,
                OutlineThickness = 1,
                OutlineOpacity = 50,
                DotOn = true,
                DotSize = 4,
                DotOpacity = 100,
                Inner = new Lines(false, 6, 2, 3, 100),
                Outer = new Lines(false, 2, 2, 12, 50),
            };
            switch (index)
            {
                case 1: // 십자
                    s.DotOn = false;
                    s.DotSize = 2;
                    s.Inner = new Lines(true, 6, 2, 3, 100);
                    break;
                case 2: // 작은 십자 + 점
                    s.DotSize = 2;
                    s.Inner = new Lines(true, 4, 2, 3, 100);
                    break;
                case 3: // 넓은 십자
                    s.DotOn = false;
                    s.DotSize = 2;
                    s.Inner = new Lines(true, 6, 2, 10, 100);
                    s.Outer = new Lines(true, 4, 2, 20, 50);
                    break;
            }
            return s;
        }

        public static CrosshairStyle Default => BuiltIn(0);

        // ---------------- 색 ----------------

        public static readonly string[] SwatchNames = { "흰", "노랑", "하늘", "초록", "분홍", "빨강", "주황", "검정" };
        public static readonly int[] Swatches = { 0xFFFFFF, 0xFFFF00, 0x00FFFF, 0x00FF00, 0xFF66CC, 0xFF0000, 0xFF9900, 0x000000 };

        /// <summary>견본 8색 중 같은 색의 번호, 없으면 -1 (직접 입력한 색).</summary>
        public static int SwatchIndex(int rgb)
        {
            for (int i = 0; i < Swatches.Length; i++)
                if (Swatches[i] == (rgb & 0xFFFFFF))
                    return i;
            return -1;
        }

        /// <summary>"#RRGGBB" (대문자).</summary>
        public static string ToHex(int rgb) => "#" + (rgb & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture);

        /// <summary>"#RRGGBB" 또는 "RRGGBB" (앞뒤 빈칸 · 대소문자 상관없음)만 받음. 세 자리 줄임 · 알파 · 그 밖은 거부.</summary>
        public static bool TryParseHex(string text, out int rgb)
        {
            rgb = 0;
            if (text == null)
                return false;
            string t = text.Trim();
            if (t.StartsWith("#", StringComparison.Ordinal))
                t = t.Substring(1);
            if (t.Length != 6)
                return false;
            foreach (char c in t)
                if (!IsHexDigit(c))
                    return false;
            rgb = int.Parse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return true;
        }

        public static bool IsHexDigit(char c) => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

        // ---------------- 저장 문자열 ----------------

        private const string Version = "v1";

        /// <summary>한 줄 저장 문자열 — 예 "v1;vis=1;col=FFFFFF;ol=1,1,50;dot=1,4,100;in=0,6,2,3,100;out=0,2,2,12,50".</summary>
        public string Serialize()
        {
            var sb = new StringBuilder(96);
            sb.Append(Version);
            sb.Append(";vis=").Append(B(Visible));
            sb.Append(";col=").Append((Color & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture));
            sb.Append(";ol=").Append(B(OutlineOn)).Append(',').Append(I(OutlineThickness)).Append(',').Append(I(OutlineOpacity));
            sb.Append(";dot=").Append(B(DotOn)).Append(',').Append(I(DotSize)).Append(',').Append(I(DotOpacity));
            sb.Append(";in=").Append(L(Inner));
            sb.Append(";out=").Append(L(Outer));
            return sb.ToString();
        }

        /// <summary>
        /// 저장 문자열 읽기. 판(v1)이 다르거나 비었으면 false. 모르는 칸은 무시, 빠지거나 깨진 칸은 기본값(점), 값은 범위 안으로.
        /// </summary>
        public static bool TryParse(string text, out CrosshairStyle style)
        {
            style = Default;
            if (string.IsNullOrEmpty(text))
                return false;
            var parts = text.Split(';');
            if (parts[0].Trim() != Version)
                return false;
            var s = Default;
            for (int i = 1; i < parts.Length; i++)
            {
                int eq = parts[i].IndexOf('=');
                if (eq <= 0)
                    continue;
                string key = parts[i].Substring(0, eq).Trim();
                string[] v = parts[i].Substring(eq + 1).Split(',');
                switch (key)
                {
                    case "vis":
                        if (v.Length == 1 && TryBool(v[0], out bool vis))
                            s.Visible = vis;
                        break;
                    case "col":
                        if (v.Length == 1 && TryParseHex(v[0], out int rgb))
                            s.Color = rgb;
                        break;
                    case "ol":
                        if (v.Length == 3 && TryBool(v[0], out bool olOn) && TryInt(v[1], out int olT) && TryInt(v[2], out int olO))
                        {
                            s.OutlineOn = olOn;
                            s.OutlineThickness = olT;
                            s.OutlineOpacity = olO;
                        }
                        break;
                    case "dot":
                        if (v.Length == 3 && TryBool(v[0], out bool dOn) && TryInt(v[1], out int dS) && TryInt(v[2], out int dO))
                        {
                            s.DotOn = dOn;
                            s.DotSize = dS;
                            s.DotOpacity = dO;
                        }
                        break;
                    case "in":
                        if (TryLines(v, out var inner))
                            s.Inner = inner;
                        break;
                    case "out":
                        if (TryLines(v, out var outer))
                            s.Outer = outer;
                        break;
                }
            }
            style = s.Clamped();
            return true;
        }

        private static bool TryLines(string[] v, out Lines lines)
        {
            lines = default;
            if (v.Length != 5 || !TryBool(v[0], out bool on) || !TryInt(v[1], out int len) || !TryInt(v[2], out int th)
                || !TryInt(v[3], out int gap) || !TryInt(v[4], out int op))
                return false;
            lines = new Lines(on, len, th, gap, op);
            return true;
        }

        private static string B(bool b) => b ? "1" : "0";
        private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);
        private static string L(Lines l) => $"{B(l.On)},{I(l.Length)},{I(l.Thickness)},{I(l.Gap)},{I(l.Opacity)}";

        private static bool TryBool(string s, out bool b)
        {
            s = s.Trim();
            b = s == "1";
            return s == "0" || s == "1";
        }

        private static bool TryInt(string s, out int v) => int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

        internal static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;

        // ---------------- 비교 ----------------

        public bool Equals(CrosshairStyle o) => Visible == o.Visible && (Color & 0xFFFFFF) == (o.Color & 0xFFFFFF)
            && OutlineOn == o.OutlineOn && OutlineThickness == o.OutlineThickness && OutlineOpacity == o.OutlineOpacity
            && DotOn == o.DotOn && DotSize == o.DotSize && DotOpacity == o.DotOpacity
            && Inner.Equals(o.Inner) && Outer.Equals(o.Outer);

        /// <summary>표시 켬 / 끔은 빼고 모양만 같은지 (프로필 고르기 줄 — 꺼 둔 채로도 어느 프로필인지 보이게).</summary>
        public bool SameShape(CrosshairStyle o)
        {
            var a = this;
            a.Visible = o.Visible;
            return a.Equals(o);
        }

        public override bool Equals(object obj) => obj is CrosshairStyle o && Equals(o);
        public override int GetHashCode() => Serialize().GetHashCode();
        public static bool operator ==(CrosshairStyle a, CrosshairStyle b) => a.Equals(b);
        public static bool operator !=(CrosshairStyle a, CrosshairStyle b) => !a.Equals(b);
        public override string ToString() => Serialize();
    }

    /// <summary>
    /// 프로필 고르기 줄의 목록: 기본 모양 4 (0 ~ 3) + 내 프로필 3칸 (4 ~ 6). 순수 함수 (시험용).
    /// </summary>
    public static class CrosshairProfiles
    {
        public const int UserSlots = 3;
        public static int Count => CrosshairStyle.BuiltInCount + UserSlots;

        public static bool IsUserSlot(int index) => index >= CrosshairStyle.BuiltInCount && index < Count;
        public static int SlotOf(int index) => index - CrosshairStyle.BuiltInCount;

        /// <summary>줄에 보일 이름. 빈 내 프로필 = "내 프로필 n (비어 있음)".</summary>
        public static string Name(int index, IReadOnlyList<CrosshairStyle?> slots)
        {
            if (index >= 0 && index < CrosshairStyle.BuiltInCount)
                return CrosshairStyle.BuiltInNames[index];
            int slot = SlotOf(index);
            bool empty = slots == null || slot < 0 || slot >= slots.Count || !slots[slot].HasValue;
            return empty ? $"내 프로필 {slot + 1} (비어 있음)" : $"내 프로필 {slot + 1}";
        }

        /// <summary>번호의 값 (빈 칸 · 범위 밖 = null).</summary>
        public static CrosshairStyle? Get(int index, IReadOnlyList<CrosshairStyle?> slots)
        {
            if (index >= 0 && index < CrosshairStyle.BuiltInCount)
                return CrosshairStyle.BuiltIn(index);
            int slot = SlotOf(index);
            if (slots == null || slot < 0 || slot >= slots.Count)
                return null;
            return slots[slot];
        }

        /// <summary>
        /// 지금 조준점과 모양이 같은 프로필 번호 — prefer가 같으면 그것 (같은 모양이 여럿일 때 고른 칸을 지킴), 아니면 앞에서부터, 없으면 -1.
        /// </summary>
        public static int Match(CrosshairStyle current, IReadOnlyList<CrosshairStyle?> slots, int prefer = -1)
        {
            var p = Get(prefer, slots);
            if (p.HasValue && p.Value.SameShape(current))
                return prefer;
            for (int i = 0; i < Count; i++)
            {
                var s = Get(i, slots);
                if (s.HasValue && s.Value.SameShape(current))
                    return i;
            }
            return -1;
        }
    }
}
