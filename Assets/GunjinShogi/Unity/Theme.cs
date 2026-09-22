using UnityEngine;

namespace GunjinShogi.UnityView
{
    /// <summary>
    /// 配色。古い作戦地図（紙・墨・川）の上に、朱と藍の駒を置くイメージ。
    /// 意味は色だけで伝えない（形・文字と必ず組み合わせる）。
    /// </summary>
    public static class Theme
    {
        public static readonly Color Background = Hex("1E2320");
        public static readonly Color Panel = Hex("262C27");
        public static readonly Color PanelLine = Hex("3A423B");
        public static readonly Color Text = Hex("ECE3CC");
        public static readonly Color TextMuted = Hex("A39D88");

        public static readonly Color Paper = Hex("E4D8B8");
        public static readonly Color PaperHq = Hex("D3C08F");
        public static readonly Color Grid = Hex("857A5C");
        public static readonly Color Ink = Hex("2E2B24");
        public static readonly Color River = Hex("6F8C8E");
        public static readonly Color Bridge = Hex("CDBF99");

        public static readonly Color[] Team = { Hex("B5402B"), Hex("2D4B61") };
        public static readonly Color[] TeamBorder = { Hex("7A2618"), Hex("15293A") };
        public static readonly string[] TeamName = { "朱", "藍" };
        public static readonly Color PieceLabel = Hex("F6EEDB");

        public static readonly Color Brass = Hex("E2B23C");
        public static readonly Color StampWin = Hex("C7361F");
        public static readonly Color StampLose = Hex("3C3A33");
        public static readonly Color StampBoth = Hex("7A5A2E");

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }

        public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);
    }
}
