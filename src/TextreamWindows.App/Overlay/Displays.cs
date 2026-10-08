using System.Runtime.InteropServices;
using TextreamWindows.Core.Overlay;

namespace TextreamWindows.App.Overlay;

/// <summary>
/// 列出螢幕（Win32 EnumDisplayMonitors），座標換成 WPF 的 DIP。
/// 不用 WinForms 的 Screen：它的 Application、MessageBox 等型別名稱會跟 WPF 打架。
/// </summary>
/// <remarks>
/// WPF 預設是「系統 DPI」模式，Windows 給的座標以系統縮放計，除以系統縮放就是 DIP。
/// 不同縮放比例的多螢幕之後再處理（這台 2026-10-08 只有一個螢幕）。
/// </remarks>
public static class Displays
{
    public static IReadOnlyList<DisplayInfo> List()
    {
        var scale = GetDpiForSystem() / 96.0;
        var result = new List<DisplayInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                result.Add(new DisplayInfo(
                    info.Device,
                    ToDip(info.Monitor, scale),
                    ToDip(info.Work, scale),
                    (info.Flags & MonitorInfoPrimary) != 0));
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static ScreenRect ToDip(Rect r, double scale) =>
        new(r.Left / scale, r.Top / scale, (r.Right - r.Left) / scale, (r.Bottom - r.Top) / scale);

    private const uint MonitorInfoPrimary = 1;

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Device;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();
}
