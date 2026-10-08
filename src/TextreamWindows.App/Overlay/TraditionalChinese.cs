using System.Runtime.InteropServices;

namespace TextreamWindows.App.Overlay;

/// <summary>
/// 簡體轉繁體，用 Windows 內建的 LCMapStringEx（不加套件）。辨識模型吐出的是簡體，浮層下方的即時文字給瓦基看繁體。
/// 一字一字轉，「后／後」「发／發／髮」這類一對多的字不一定轉對；只是讓人知道「有聽到」，比對本身不靠它（比的是拼音）。
/// </summary>
public static class TraditionalChinese
{
    private const uint LcmapTraditionalChinese = 0x04000000;

    public static string From(string simplified)
    {
        if (simplified.Length == 0)
        {
            return simplified;
        }
        var buffer = new char[simplified.Length];
        var written = LCMapStringEx("zh-TW", LcmapTraditionalChinese, simplified, simplified.Length, buffer, buffer.Length, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        return written > 0 ? new string(buffer, 0, written) : simplified; // 轉不了就照原樣顯示
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int LCMapStringEx(
        string localeName, uint mapFlags, string source, int sourceLength, [Out] char[] destination, int destinationLength,
        IntPtr versionInformation, IntPtr reserved, IntPtr sortHandle);
}
