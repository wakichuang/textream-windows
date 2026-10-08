namespace TextreamWindows.Core.Overlay;

/// <summary>浮層的三種樣式（計畫書第 5 階段步驟 2）。</summary>
public enum OverlayStyle
{
    /// <summary>貼在主螢幕最上緣正中央，靠近筆電鏡頭（瀏海膠囊的替代方案，第 3 節）。</summary>
    Capsule,

    /// <summary>可拖曳、可拉大小的視窗，記住位置。</summary>
    Floating,

    /// <summary>蓋滿選定的螢幕，Esc 結束。</summary>
    FullScreen,
}

/// <summary>螢幕上的一塊矩形，單位是 WPF 的裝置無關像素（DIP）。</summary>
public readonly record struct ScreenRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    /// <summary>兩塊重疊的面積。</summary>
    public double OverlapArea(ScreenRect other) =>
        Math.Max(0, Math.Min(Right, other.Right) - Math.Max(X, other.X)) *
        Math.Max(0, Math.Min(Bottom, other.Bottom) - Math.Max(Y, other.Y));
}

/// <summary>一個螢幕：名稱（例如 \\.\DISPLAY1）、整塊範圍、扣掉工作列的範圍、是不是主螢幕。</summary>
public sealed record DisplayInfo(string Name, ScreenRect Bounds, ScreenRect WorkArea, bool IsPrimary);

/// <summary>三種浮層各放在哪裡、多大。純計算，不碰視窗。</summary>
public static class OverlayLayout
{
    // 瓦基 2026-10-08：上限多放 30%（寬 500→650、高 400→520），寬的下限 280→250；
    // 高的下限維持 100：再矮，上方狀態列加下方音量列就放不下一行講稿
    public const double CapsuleMinWidth = 250;
    public const double CapsuleMaxWidth = 650;
    public const double CapsuleMinHeight = 100;
    public const double CapsuleMaxHeight = 520;
    public const double DefaultCapsuleWidth = 450; // 落在刻度上（原本 420）
    public const double DefaultCapsuleHeight = 160;

    /// <summary>
    /// 主視窗拉條一格多少（瓦基 2026-10-08：一格一格比較知道怎麼調）。寬高同一個間隔（第一版寬 50、高 30，看起來疏密不同），
    /// 寬 20 格、高 21 格，兩端都在刻度上。拖浮層邊框改大小時照樣逐點、不吸附。
    /// </summary>
    public const double CapsuleWidthStep = 20;

    public const double CapsuleHeightStep = 20;

    /// <summary>浮層字級（瓦基 2026-10-08：讓使用者調，拉條或直接打數字），單位跟 WPF 一樣是 1/96 吋（100% 縮放時就是 px）。</summary>
    public const double MinFontSize = 14;

    public const double MaxFontSize = 96;

    public const double FontSizeStep = 2;

    /// <summary>每種浮層預設的字級：膠囊小、浮動視窗中、全螢幕大。</summary>
    public static double DefaultFontSize(OverlayStyle style) => style switch
    {
        OverlayStyle.Capsule => 22,
        OverlayStyle.Floating => 28,
        _ => 52,
    };

    public static double ClampFontSize(double size) => Math.Clamp(size, MinFontSize, MaxFontSize);

    /// <summary>浮動視窗最小多大：再小就看不到一整行字、也抓不到。</summary>
    private const double FloatingMinWidth = 200;
    private const double FloatingMinHeight = 80;

    /// <summary>存下來的浮動視窗至少要有這個比例落在某個螢幕的工作區裡，才算還找得到。</summary>
    private const double FloatingMinVisibleShare = 0.3;

    /// <summary>主螢幕最上緣正中央（用整塊範圍、不是工作區：鏡頭在螢幕最上面）。寬高夾在允許範圍內。</summary>
    public static ScreenRect Capsule(IReadOnlyList<DisplayInfo> displays, double width, double height)
    {
        var screen = Primary(displays).Bounds;
        var w = Math.Clamp(width, CapsuleMinWidth, CapsuleMaxWidth);
        var h = Math.Clamp(height, CapsuleMinHeight, CapsuleMaxHeight);
        return new ScreenRect(screen.X + (screen.Width - w) / 2, screen.Y, w, h);
    }

    /// <summary>
    /// 上次存的位置；沒有存過、或那個位置大半已經不在任何螢幕上（例如外接螢幕拔掉了），
    /// 就放在主螢幕工作區上方約十分之一、水平置中。
    /// </summary>
    public static ScreenRect Floating(IReadOnlyList<DisplayInfo> displays, ScreenRect? saved)
    {
        if (saved is { } rect)
        {
            var sized = rect with { Width = Math.Max(rect.Width, FloatingMinWidth), Height = Math.Max(rect.Height, FloatingMinHeight) };
            var visible = displays.Sum(d => sized.OverlapArea(d.WorkArea));
            if (visible >= FloatingMinVisibleShare * sized.Width * sized.Height)
            {
                return sized;
            }
        }

        var work = Primary(displays).WorkArea;
        var width = Math.Min(720, work.Width * 0.6);
        var height = Math.Min(240, work.Height * 0.3);
        return new ScreenRect(work.X + (work.Width - width) / 2, work.Y + work.Height * 0.1, width, height);
    }

    /// <summary>選定的螢幕（照名稱找）；找不到就主螢幕。</summary>
    public static ScreenRect FullScreen(IReadOnlyList<DisplayInfo> displays, string? displayName) =>
        (displays.FirstOrDefault(d => d.Name == displayName) ?? Primary(displays)).Bounds;

    private static DisplayInfo Primary(IReadOnlyList<DisplayInfo> displays) =>
        displays.FirstOrDefault(d => d.IsPrimary) ?? displays.FirstOrDefault()
        ?? throw new InvalidOperationException("找不到任何螢幕。");
}
