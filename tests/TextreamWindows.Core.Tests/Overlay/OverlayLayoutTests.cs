using TextreamWindows.Core.Overlay;

namespace TextreamWindows.Core.Tests.Overlay;

public class OverlayLayoutTests
{
    /// <summary>瓦基這台：2560×1440、150% 縮放 → 1707×960 DIP，工作列在下面約 32 DIP（2026-10-08 實測只有一個螢幕）。</summary>
    private static readonly DisplayInfo Laptop = new(@"\\.\DISPLAY1", new(0, 0, 1707, 960), new(0, 0, 1707, 928), IsPrimary: true);

    /// <summary>假想的外接螢幕，放在主螢幕左邊（座標是負的）。</summary>
    private static readonly DisplayInfo External = new(@"\\.\DISPLAY2", new(-1920, 0, 1920, 1080), new(-1920, 0, 1920, 1040), IsPrimary: false);

    // ── 頂端膠囊 ──

    [Fact]
    public void CapsuleSitsAtTheVeryTopCentreOfThePrimaryScreen()
    {
        var rect = OverlayLayout.Capsule([External, Laptop], 420, 160);

        Assert.Equal(new ScreenRect((1707 - 420) / 2.0, 0, 420, 160), rect);
    }

    [Fact]
    public void TheSizeSlidersHaveEvenTicksFromEndToEnd()
    {
        // 瓦基 2026-10-08：拉條要一格一格，看得出怎麼調；兩端都要落在刻度上
        Assert.Equal(0, (OverlayLayout.CapsuleMaxWidth - OverlayLayout.CapsuleMinWidth) % OverlayLayout.CapsuleWidthStep);
        Assert.Equal(0, (OverlayLayout.CapsuleMaxHeight - OverlayLayout.CapsuleMinHeight) % OverlayLayout.CapsuleHeightStep);
        // 瓦基 2026-10-08 第二次：寬和高的間隔要一樣（原本寬每格 50、高每格 30，看起來疏密不同）
        Assert.Equal(OverlayLayout.CapsuleWidthStep, OverlayLayout.CapsuleHeightStep);
        Assert.InRange((OverlayLayout.CapsuleMaxWidth - OverlayLayout.CapsuleMinWidth) / OverlayLayout.CapsuleWidthStep, 15, 25);
        Assert.InRange((OverlayLayout.CapsuleMaxHeight - OverlayLayout.CapsuleMinHeight) / OverlayLayout.CapsuleHeightStep, 15, 25);
        Assert.Equal(0, (OverlayLayout.DefaultCapsuleWidth - OverlayLayout.CapsuleMinWidth) % OverlayLayout.CapsuleWidthStep);
        Assert.Equal(0, (OverlayLayout.DefaultCapsuleHeight - OverlayLayout.CapsuleMinHeight) % OverlayLayout.CapsuleHeightStep);
    }

    [Theory]
    [InlineData(OverlayStyle.Capsule, 22)]
    [InlineData(OverlayStyle.Floating, 28)]
    [InlineData(OverlayStyle.FullScreen, 52)]
    public void EachOverlayStyleHasItsOwnDefaultFontSize(OverlayStyle style, double expected)
    {
        Assert.Equal(expected, OverlayLayout.DefaultFontSize(style));
    }

    [Theory]
    [InlineData(5, 14)]
    [InlineData(23, 23)]
    [InlineData(500, 96)]
    public void FontSizeIsClamped(double size, double expected)
    {
        // 瓦基 2026-10-08：字級讓使用者調，拉條或直接打數字
        Assert.Equal(expected, OverlayLayout.ClampFontSize(size));
    }

    // 瓦基 2026-10-08：上限多放 30%（寬 500→650、高 400→520），寬的下限放到 250；高的下限維持 100（再矮放不下一行字）
    [Theory]
    [InlineData(100, 50, 250, 100)]
    [InlineData(900, 900, 650, 520)]
    public void CapsuleSizeIsClampedToTheAllowedRange(double width, double height, double expectedWidth, double expectedHeight)
    {
        var rect = OverlayLayout.Capsule([Laptop], width, height);

        Assert.Equal(expectedWidth, rect.Width);
        Assert.Equal(expectedHeight, rect.Height);
    }

    // ── 浮動視窗 ──

    [Fact]
    public void FloatingStartsNearTheTopOfThePrimaryWorkArea()
    {
        var rect = OverlayLayout.Floating([Laptop], saved: null);

        Assert.Equal((1707 - rect.Width) / 2, rect.X, 3);
        Assert.InRange(rect.Y, 0, 928 * 0.2);
        Assert.True(rect.Width >= 400 && rect.Height >= 150, $"{rect}");
    }

    [Fact]
    public void FloatingKeepsItsSavedPlace()
    {
        var saved = new ScreenRect(-1500, 200, 700, 260); // 上次放在外接螢幕上

        Assert.Equal(saved, OverlayLayout.Floating([Laptop, External], saved));
    }

    [Fact]
    public void FloatingComesBackWhenItsScreenIsGone()
    {
        // 外接螢幕拔掉了：存下來的位置整塊看不到，回到主螢幕的預設位置
        var saved = new ScreenRect(-1500, 200, 700, 260);

        Assert.Equal(OverlayLayout.Floating([Laptop], null), OverlayLayout.Floating([Laptop], saved));
    }

    [Fact]
    public void FloatingMostlyOffScreenAlsoComesBack()
    {
        // 只剩右下角一小塊露在螢幕上，抓不到標題列：當作不見
        var saved = new ScreenRect(1680, 900, 700, 260);

        Assert.Equal(OverlayLayout.Floating([Laptop], null), OverlayLayout.Floating([Laptop], saved));
    }

    [Fact]
    public void ATinySavedFloatingWindowIsEnlarged()
    {
        var rect = OverlayLayout.Floating([Laptop], new ScreenRect(100, 100, 20, 10));

        Assert.Equal(100, rect.X);
        Assert.Equal(100, rect.Y);
        Assert.True(rect.Width >= 200 && rect.Height >= 80, $"{rect}");
    }

    // ── 全螢幕 ──

    [Fact]
    public void FullScreenCoversTheChosenScreen()
    {
        Assert.Equal(External.Bounds, OverlayLayout.FullScreen([Laptop, External], @"\\.\DISPLAY2"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(@"\\.\DISPLAY9")] // 選過的螢幕拔掉了
    public void FullScreenFallsBackToThePrimaryScreen(string? name)
    {
        Assert.Equal(Laptop.Bounds, OverlayLayout.FullScreen([External, Laptop], name));
    }
}
