using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using TextreamWindows.App.Overlay;
using TextreamWindows.App.Services;
using TextreamWindows.Core.Overlay;
using TextreamWindows.Core.Session;
using TextreamWindows.Core.Text;

namespace TextreamWindows.Desktop.Tests.Overlay;

/// <summary>
/// 第 5.4 步碰到 Win32 的部分：錄影時隱藏、全域快捷鍵。只建視窗代碼（EnsureHandle），不把視窗顯示出來。
/// 用真的錄影畫面驗「看不到」是第 6.1 步（計畫書第 7.5 節的洋紅色測試）。
/// </summary>
public class OverlayWindowTests
{
    private const uint WdaNone = 0x0;
    private const uint WdaExcludeFromCapture = 0x11;
    private const int WmHotkey = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

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
        if (error is not null)
        {
            throw new InvalidOperationException("STA 執行緒裡出錯", error);
        }
        return result;
    }

    [Theory]
    [InlineData(true, WdaExcludeFromCapture)]
    [InlineData(false, WdaNone)]
    public void TheOverlayIsExcludedFromScreenCaptureWhenAsked(bool hide, uint expected)
    {
        var affinity = OnSta(() =>
        {
            var window = new OverlayWindow(OverlayStyle.Floating, new ScreenRect(-5000, -5000, 300, 200), new PromptScript("我讀書"), FollowMode.Classic, 28)
            {
                HideFromCapture = hide,
            };
            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            Assert.True(GetWindowDisplayAffinity(hwnd, out var value));
            ((IOverlay)window).Close();
            return value;
        });

        Assert.Equal(expected, affinity);
    }

    [Fact]
    public void AHotkeyMessageRunsItsAction()
    {
        // 用一組不會跟正式快捷鍵（Ctrl+Alt+L、↑、↓）撞到的組合：瓦基的 Textream 開著時測試也能跑
        var (registered, pressed, second) = OnSta(() =>
        {
            var window = new Window();
            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            var count = 0;
            using var hotkeys = new GlobalHotkeys(window);
            var ok = hotkeys.Add(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift, Key.F24, () => count++);

            SendMessage(hwnd, WmHotkey, (IntPtr)hotkeys.IdOf(Key.F24), IntPtr.Zero);

            // 同一組合再註冊一次（像是開了兩個 Textream）：回 false，不丟例外
            var other = new Window();
            new WindowInteropHelper(other).EnsureHandle();
            using var again = new GlobalHotkeys(other);
            var duplicate = again.Add(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift, Key.F24, () => { });
            other.Close();
            window.Close();
            return (ok, count, duplicate);
        });

        Assert.True(registered);
        Assert.Equal(1, pressed);
        Assert.False(second);
    }

    [Fact]
    public void ARemovedHotkeyIsReleasedForOthers()
    {
        // Esc 只在浮層開著時註冊，關掉就要還給別的程式（瓦基 2026-10-08）
        var (removed, takenByOther, removedTwice) = OnSta(() =>
        {
            var window = new Window();
            new WindowInteropHelper(window).EnsureHandle();
            using var hotkeys = new GlobalHotkeys(window);
            hotkeys.Add(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift, Key.F23, () => { });

            var ok = hotkeys.Remove(Key.F23);
            var other = new Window();
            new WindowInteropHelper(other).EnsureHandle();
            using var again = new GlobalHotkeys(other);
            var taken = again.Add(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift, Key.F23, () => { });
            var twice = hotkeys.Remove(Key.F23); // 沒註冊的鍵：回 false，不丟例外
            other.Close();
            window.Close();
            return (ok, taken, twice);
        });

        Assert.True(removed);
        Assert.True(takenByOther);
        Assert.False(removedTwice);
    }
}
