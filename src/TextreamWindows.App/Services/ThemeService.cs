using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using TextreamWindows.Core.Settings;

namespace TextreamWindows.App.Services;

/// <summary>
/// 主視窗外觀（瓦基 2026-10-08）：跟著系統、淺色、深色，配色用閱讀前哨站的色票（<see cref="BrandPalette"/>）。
/// 不用 Application.ThemeMode：.NET 10 仍標為實驗性 API（WPF0001），程式裡設它會觸發警告，而專案規定不壓警告。
/// 改成換 Fluent 的淺色／深色資源字典（這是正式支援的做法），標題列顏色另外用 DWM 設定。
/// 選「跟著系統」時，Windows 切換深淺色會即時跟上。浮層一律深色，不歸這裡管。
/// </summary>
public sealed class ThemeService : IDisposable
{
    private const string LightSource = "pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.Light.xaml";
    private const string DarkSource = "pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.Dark.xaml";
    private const int DwmUseImmersiveDarkMode = 20;

    private readonly Application _app;
    private AppTheme _choice = AppTheme.System;
    private bool? _appliedDark;

    public ThemeService(Application app)
    {
        _app = app;
        Current = this;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>主視窗建的那一個；確認視窗用它把標題列設成同樣的深淺色。</summary>
    public static ThemeService? Current { get; private set; }

    /// <summary>Windows 的「應用程式模式」是不是淺色（登錄檔讀不到就當淺色）。</summary>
    public static bool SystemUsesLight() =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is not int value || value != 0;

    public void Apply(AppTheme choice)
    {
        _choice = choice;
        var dark = AppThemes.UseDark(choice, SystemUsesLight());
        if (_appliedDark == dark)
        {
            return;
        }
        _appliedDark = dark;

        // Fluent 載入時就把顏色算好塞進每個筆刷，事後疊一層字典蓋不掉；而且 WPF 看到加進來的字典
        // Source 是 Fluent 的路徑，會換成它自己重新載入的那份（2026-10-08 實測兩件事都會發生）。
        // 所以：載入 → 換成品牌色 → 抄進一份沒有 Source 的字典再加進去，最後疊上品牌字典
        var fluent = new ResourceDictionary { Source = new Uri(dark ? DarkSource : LightSource) };
        Recolor(fluent, dark);
        var copy = new ResourceDictionary();
        foreach (var key in fluent.Keys)
        {
            copy[key] = fluent[key];
        }
        var merged = _app.Resources.MergedDictionaries;
        merged.Clear();
        merged.Add(copy);
        merged.Add(BrandResources(dark));
        foreach (Window window in _app.Windows)
        {
            ApplyTitleBar(window);
        }
    }

    /// <summary>標題列跟著深淺色（視窗代碼建好之後才能設，新開的視窗在 SourceInitialized 呼叫）。</summary>
    public void ApplyTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || _appliedDark is not { } dark)
        {
            return;
        }
        var value = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref value, sizeof(int));
    }

    /// <summary>
    /// 蓋在 Fluent 上面的閱讀前哨站配色（<see cref="BrandPalette"/>）：底色、字色、強調色、開始鍵。
    /// Fluent 的滑桿、選項鈕、勾選框都用 SystemAccentColor 那 7 個鍵（動態參照），蓋掉它們就整套換色。
    /// </summary>
    public static ResourceDictionary BrandResources(bool dark)
    {
        var palette = dark ? BrandPalette.Dark : BrandPalette.Light;
        var d = new ResourceDictionary();
        void Color(string key, System.Windows.Media.Color value) => d[key] = value;
        void Brush(string key, System.Windows.Media.Color value) => d[key] = Frozen(value);

        Color("ApplicationBackgroundColor", palette.Background);
        Brush("ApplicationBackgroundBrush", palette.Background);
        Brush("WindowBackground", palette.Background);
        Color("SolidBackgroundFillColorBase", palette.Background);
        Brush("SolidBackgroundFillColorBaseBrush", palette.Background);
        Color("TextFillColorPrimary", palette.TextPrimary);
        Brush("TextFillColorPrimaryBrush", palette.TextPrimary);
        Color("TextFillColorSecondary", palette.TextSecondary);
        Brush("TextFillColorSecondaryBrush", palette.TextSecondary);

        // 強調色：淺色主題的填色取 Dark1、深色主題取 Light2，兩邊都對到 palette.Accent
        Color("SystemAccentColor", palette.Accent);
        Color("SystemAccentColorLight1", dark ? palette.AccentDeep : BrandPalette.Light.AccentDeep);
        Color("SystemAccentColorLight2", dark ? palette.Accent : BrandPalette.Dark.AccentDeep);
        Color("SystemAccentColorLight3", dark ? palette.Accent : BrandPalette.Sky);
        Color("SystemAccentColorDark1", dark ? palette.AccentDeep : palette.Accent);
        Color("SystemAccentColorDark2", palette.AccentDeep);
        Color("SystemAccentColorDark3", palette.AccentDeeper);

        // 開始鍵（唯一用 AccentButtonStyle 的鍵）：DESIGN.md 的 .btn-primary，天空藍底、深褐字，滑過變黃
        Brush("AccentButtonBackground", BrandPalette.Sky);
        Brush("AccentButtonBackgroundPointerOver", BrandPalette.Sun);
        Brush("AccentButtonBackgroundPressed", BrandPalette.Sun);
        foreach (var key in new[] { "AccentButtonForeground", "AccentButtonForegroundPointerOver", "AccentButtonForegroundPressed" })
        {
            Brush(key, BrandPalette.Heading);
        }
        return d;
    }

    /// <summary>
    /// 把 Fluent 字典裡的顏色依角色換成品牌色：強調色（Windows 的主題色，各種深淺）→ 品牌強調色；
    /// 預設底色 → 米白或暖深褐；黑字或白字（TextFillColor 與各控制項的 Foreground）→ 深褐或米白字，透明度照留（主要、次要、第三層文字的層次不變）。
    /// </summary>
    public static void Recolor(ResourceDictionary fluent, bool dark)
    {
        var palette = dark ? BrandPalette.Dark : BrandPalette.Light;
        var keys = fluent.Keys.OfType<string>().ToList();
        static System.Windows.Media.Color Opaque(System.Windows.Media.Color c) => System.Windows.Media.Color.FromRgb(c.R, c.G, c.B);
        static bool Gray(System.Windows.Media.Color c) => c.R == c.G && c.G == c.B;

        // Windows 主題色的各種深淺：所有 Accent 開頭的筆刷裡，不透明、有彩度的顏色
        var accents = keys.Where(k => k.StartsWith("Accent", StringComparison.Ordinal))
            .Select(k => fluent[k]).OfType<System.Windows.Media.SolidColorBrush>()
            .Where(b => b.Color.A == 0xFF && !Gray(b.Color)).Select(b => b.Color).ToHashSet();
        var background = ((System.Windows.Media.SolidColorBrush)fluent["ApplicationBackgroundBrush"]).Color;
        var textBase = dark ? System.Windows.Media.Colors.White : System.Windows.Media.Colors.Black;

        System.Windows.Media.Color? Map(string key, System.Windows.Media.Color c)
        {
            var rgb = Opaque(c);
            if (accents.Contains(rgb))
            {
                return System.Windows.Media.Color.FromArgb(c.A, palette.Accent.R, palette.Accent.G, palette.Accent.B);
            }
            if (rgb == Opaque(background))
            {
                return System.Windows.Media.Color.FromArgb(c.A, palette.Background.R, palette.Background.G, palette.Background.B);
            }
            // 文字：TextFillColor 系列，加上各控制項自己的 Foreground（輸入框、下拉選單的字）
            if ((key.StartsWith("TextFillColor", StringComparison.Ordinal) || key.Contains("Foreground", StringComparison.Ordinal)) && rgb == Opaque(textBase))
            {
                return System.Windows.Media.Color.FromArgb(c.A, palette.TextPrimary.R, palette.TextPrimary.G, palette.TextPrimary.B);
            }
            return null;
        }

        foreach (var key in keys)
        {
            switch (fluent[key])
            {
                case System.Windows.Media.SolidColorBrush brush when Map(key, brush.Color) is { } color:
                    fluent[key] = Frozen(color);
                    break;
                case System.Windows.Media.Color value when Map(key, value) is { } color:
                    fluent[key] = color;
                    break;
            }
        }
    }

    private static System.Windows.Media.SolidColorBrush Frozen(System.Windows.Media.Color color)
    {
        var brush = new System.Windows.Media.SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_choice == AppTheme.System && e.Category == UserPreferenceCategory.General)
        {
            _app.Dispatcher.BeginInvoke(() => Apply(AppTheme.System));
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
