using TextreamWindows.Core.Session;

namespace TextreamWindows.Core.Overlay;

/// <summary>捲動動畫怎麼走：多久、先快後慢還是等速。</summary>
public readonly record struct ScrollMotion(double Seconds, bool EaseOut);

/// <summary>
/// 浮層的捲動（計畫書第 5 階段步驟 3、第 5.3 步）：目前那一行停在浮層上方約三分之一，
/// 位置變了就往那裡平滑地移過去。純計算，畫面在 App 的 OverlayView。
/// </summary>
public static class ReadingScroll
{
    /// <summary>目前那一行要停在浮層高度的幾分之幾。</summary>
    public const double Anchor = 1.0 / 3;

    /// <summary>目前那一行的頂端在 <paramref name="lineTop"/>（從講稿最上面算），講稿要往上捲多少。最少是 0（開頭不往下留白）。</summary>
    public static double Target(double lineTop, double viewportHeight) =>
        Math.Max(0, lineTop - viewportHeight * Anchor);

    /// <summary>
    /// 捲 <paramref name="distance"/> 要怎麼動。逐字追蹤：先快後慢，一行 0.35 秒、每多一行加 0.08 秒、最多 0.7 秒，
    /// 跳得遠滑得久一點，眼睛跟得上（瓦基 2026-10-08：跳過子標題時畫面一下切過去會跟不上）。
    /// 計時器模式每 50 毫秒推一次，每步 60 毫秒等速，看起來是連續往上流（Mac 版 MarqueeTextView 的做法）。
    /// </summary>
    public static ScrollMotion Motion(double distance, double lineHeight, FollowMode mode)
    {
        if (mode != FollowMode.WordTracking)
        {
            return new ScrollMotion(0.06, EaseOut: false);
        }
        var lines = Math.Abs(distance) / Math.Max(1, lineHeight);
        return new ScrollMotion(Math.Min(0.7, 0.35 + 0.08 * Math.Max(0, lines - 1)), EaseOut: true);
    }
}
