using System.Windows;
using System.Windows.Media;
using TextreamWindows.App.Services;

namespace TextreamWindows.Desktop.Tests.Overlay;

/// <summary>
/// 閱讀前哨站配色（瓦基 2026-10-08）：淺色照閱讀前哨站品牌規範（DESIGN.md）的色票；深色是同一套色票推出來的暖深色
/// （DESIGN.md 只定網站的淺色，App 要深色是瓦基另外要的）。
/// </summary>
public class BrandThemeTests
{
    private static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static T OnSta<T>(Func<T> work)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return error is null ? result : throw new InvalidOperationException("STA 執行緒裡出錯", error);
    }

    [Fact]
    public void TheLightPaletteIsTheBrandGuide()
    {
        // DESIGN.md〈Colors〉：--bg、--heading、--muted、--sky、--sun、--blue
        Assert.Equal(Hex("#fbf8f4"), BrandPalette.Light.Background);
        Assert.Equal(Hex("#2f2820"), BrandPalette.Light.TextPrimary);
        Assert.Equal(Hex("#756656"), BrandPalette.Light.TextSecondary);
        Assert.Equal(Hex("#8adfff"), BrandPalette.Sky);
        Assert.Equal(Hex("#fdcc50"), BrandPalette.Sun);
        Assert.Equal(Hex("#137194"), BrandPalette.Light.Accent); // 細的控制項用加深的藍：天空藍壓米白底太淡
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheOverridesRecolorBackgroundTextAccentAndThePrimaryButton(bool dark)
    {
        var palette = dark ? BrandPalette.Dark : BrandPalette.Light;
        var colors = OnSta(() =>
        {
            var d = ThemeService.BrandResources(dark);
            Color Brush(string key) => ((SolidColorBrush)d[key]).Color;
            return (Brush("ApplicationBackgroundBrush"), (Color)d["ApplicationBackgroundColor"], Brush("TextFillColorPrimaryBrush"),
                (Color)d["SystemAccentColor"], (Color)d["SystemAccentColorLight2"], (Color)d["SystemAccentColorDark1"],
                Brush("AccentButtonBackground"), Brush("AccentButtonBackgroundPointerOver"), Brush("AccentButtonForeground"));
        });

        Assert.Equal(palette.Background, colors.Item1);
        Assert.Equal(palette.Background, colors.Item2);
        Assert.Equal(palette.TextPrimary, colors.Item3);
        Assert.Equal(palette.Accent, colors.Item4);
        Assert.Equal(palette.Accent, dark ? colors.Item5 : colors.Item6); // 淺色主題的填色用 Dark1、深色主題用 Light2
        Assert.Equal(BrandPalette.Sky, colors.Item7); // 開始鍵：天空藍底（DESIGN.md .btn-primary）
        Assert.Equal(BrandPalette.Sun, colors.Item8); // 滑過變黃
        Assert.Equal(Hex("#2f2820"), colors.Item9); // 上面壓深褐字，不壓白字
    }

    [Fact]
    public void TextIsReadableOnBothBackgrounds()
    {
        // WCAG AA：內文 4.5:1
        Assert.True(Contrast(BrandPalette.Light.TextSecondary, BrandPalette.Light.Background) >= 4.5);
        Assert.True(Contrast(BrandPalette.Dark.TextPrimary, BrandPalette.Dark.Background) >= 7);
        Assert.True(Contrast(BrandPalette.Dark.TextSecondary, BrandPalette.Dark.Background) >= 4.5);
        Assert.True(Contrast(BrandPalette.Light.Accent, BrandPalette.Light.Background) >= 4.5);
        Assert.True(Contrast(BrandPalette.Dark.Accent, BrandPalette.Dark.Background) >= 4.5);
        Assert.True(Contrast(BrandPalette.OverlayText, BrandPalette.OverlayBackground) >= 7);
        Assert.True(Contrast(BrandPalette.Sun, BrandPalette.OverlayBackground) >= 7);
    }

    private static double Contrast(Color a, Color b)
    {
        static double Lum(Color c)
        {
            static double Ch(byte v)
            {
                var s = v / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
        }
        var (l1, l2) = (Lum(a), Lum(b));
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }
}
