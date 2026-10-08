using System.Windows.Media;

namespace TextreamWindows.App.Services;

/// <summary>主視窗一種外觀用的顏色。</summary>
public sealed record AppPalette(Color Background, Color TextPrimary, Color TextSecondary, Color Accent, Color AccentDeep, Color AccentDeeper);

/// <summary>
/// 閱讀前哨站配色（瓦基 2026-10-08）。淺色照閱讀前哨站品牌規範（DESIGN.md）的色票：
/// 米白底 --bg、深褐字 --heading、次要字 --muted、主要按鈕天空藍 --sky（壓深褐字，滑過變 --sun）、
/// 細的控制項用 --blue（天空藍在米白底上太淡，DESIGN.md 也規定它只當底色）。
/// 深色是同一套色票推出來的暖深色：DESIGN.md 只定網站的淺色，App 要深色是瓦基另外要的。
/// 浮層一律用暖深色，三種字色照舊（讀過反灰、目前黃、還沒讀白），黃換成品牌的 --sun。
/// </summary>
public static class BrandPalette
{
    public static readonly Color Sky = Hex(0x8A, 0xDF, 0xFF);
    public static readonly Color Sun = Hex(0xFD, 0xCC, 0x50);
    public static readonly Color Heading = Hex(0x2F, 0x28, 0x20);

    public static readonly AppPalette Light = new(
        Background: Hex(0xFB, 0xF8, 0xF4), // --bg
        TextPrimary: Heading, // --heading
        TextSecondary: Hex(0x75, 0x66, 0x56), // --muted
        Accent: Hex(0x13, 0x71, 0x94), // --blue
        AccentDeep: Hex(0x0F, 0x5A, 0x76),
        AccentDeeper: Hex(0x0B, 0x44, 0x59));

    public static readonly AppPalette Dark = new(
        Background: Hex(0x1C, 0x18, 0x14), // 比 --heading 再深一階的暖褐
        TextPrimary: Hex(0xF6, 0xF1, 0xEA),
        TextSecondary: Hex(0xBC, 0xAE, 0x9C),
        Accent: Sky, // 深底上天空藍夠亮，細的控制項也看得清楚
        AccentDeep: Hex(0x1B, 0x9D, 0xCE), // --blue-bright
        AccentDeeper: Hex(0x13, 0x71, 0x94)); // --blue

    /// <summary>浮層底色：跟深色主題同一個暖褐。</summary>
    public static readonly Color OverlayBackground = Dark.Background;

    /// <summary>浮層還沒讀的字：--bg 米白。</summary>
    public static readonly Color OverlayText = Light.Background;

    private static Color Hex(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
}
