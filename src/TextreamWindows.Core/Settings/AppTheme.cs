namespace TextreamWindows.Core.Settings;

/// <summary>主視窗的外觀（瓦基 2026-10-08）：預設跟著系統，也可以固定淺色或深色。浮層一律深色，不受影響。</summary>
public enum AppTheme
{
    System,
    Light,
    Dark,
}

public static class AppThemes
{
    /// <summary>這個選擇加上系統目前的設定，主視窗該不該用深色。</summary>
    public static bool UseDark(AppTheme choice, bool systemUsesLight) => choice switch
    {
        AppTheme.Light => false,
        AppTheme.Dark => true,
        _ => !systemUsesLight,
    };
}
