namespace TextreamWindows.Core.Session;

/// <summary>定速捲動的速度，單位是每秒幾個詞（中文一字一詞）。範圍照原版 <c>PromptScrollSpeedAdjustment</c>。</summary>
public static class ScrollSpeed
{
    public const double Minimum = 0.5;
    public const double Maximum = 8.0;
    public const double Step = 0.5;
    public const double Default = 4.0; // 瓦基 2026-10-08：每秒四個字比較符合一般使用情境（原版是 3）

    public static double Clamp(double speed) => Math.Clamp(speed, Minimum, Maximum);
}
