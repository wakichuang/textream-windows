using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace TextreamWindows.App.Services;

/// <summary>
/// 全域快捷鍵（計畫書第 5 階段步驟 4）：別的程式（錄影軟體）在前面時也收得到。
/// 掛在主視窗上：主視窗開始後會收起來（Hide），但視窗代碼還在，快捷鍵照樣有效。
/// 組合鍵被別的程式佔走時 <see cref="Add"/> 回 false，不丟例外。
/// </summary>
public sealed class GlobalHotkeys : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000; // 按住不放不要一直觸發
    private const int FirstId = 0x5400;

    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = [];
    private readonly Dictionary<Key, int> _ids = [];
    private int _nextId = FirstId;

    public GlobalHotkeys(Window window)
    {
        _hwnd = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(_hwnd);
        _source.AddHook(WndProc);
    }

    /// <summary>註冊 <paramref name="modifiers"/>＋<paramref name="key"/>。被別的程式佔走回 false。</summary>
    public bool Add(ModifierKeys modifiers, Key key, Action action)
    {
        var id = _nextId++;
        var mods = (uint)modifiers | ModNoRepeat; // ModifierKeys 的 Alt=1、Control=2、Shift=4、Windows=8，跟 Win32 的 MOD_* 一樣
        if (!RegisterHotKey(_hwnd, id, mods, (uint)KeyInterop.VirtualKeyFromKey(key)))
        {
            return false;
        }
        _actions[id] = action;
        _ids[key] = id;
        return true;
    }

    /// <summary>放掉某個鍵（例如 Esc 只在浮層開著時註冊），還給別的程式。沒註冊過回 false。</summary>
    public bool Remove(Key key)
    {
        if (!_ids.Remove(key, out var id))
        {
            return false;
        }
        _actions.Remove(id);
        return UnregisterHotKey(_hwnd, id);
    }

    /// <summary>某個鍵註冊用的代號（測試送 WM_HOTKEY 用）。</summary>
    public int IdOf(Key key) => _ids[key];

    public void Dispose()
    {
        foreach (var id in _actions.Keys)
        {
            UnregisterHotKey(_hwnd, id);
        }
        _actions.Clear();
        _source.RemoveHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            action();
            handled = true;
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
