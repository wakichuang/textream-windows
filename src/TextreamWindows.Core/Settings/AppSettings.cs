using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using TextreamWindows.Core.Overlay;
using TextreamWindows.Core.Session;

namespace TextreamWindows.Core.Settings;

/// <summary>
/// 使用者設定，存在 %APPDATA%\Textream\settings.json（計畫書第 4.3 節、第 5 階段步驟 7）。
/// 檔案壞掉或缺欄位都退回預設值，不讓程式開不起來；模式存名字、中文直接存，方便手改或貼給 AI 看。
/// </summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>上次開的講稿，下次啟動自動開回來。</summary>
    public string? LastScriptPath { get; set; }

    public FollowMode Mode { get; set; } = FollowMode.WordTracking;

    /// <summary>麥克風的裝置 ID（MicrophoneInfo.Id）；null 用 Windows 預設的錄音裝置。</summary>
    public string? MicrophoneId { get; set; }

    /// <summary>定速捲動與有講話才捲的速度，每秒幾個字。</summary>
    public double ScrollSpeed { get; set; } = Session.ScrollSpeed.Default;

    /// <summary>浮層樣式，預設頂端膠囊（鏡頭在螢幕頂端正中央，第 3 節）。</summary>
    public OverlayStyle OverlayStyle { get; set; } = OverlayStyle.Capsule;

    public double CapsuleWidth { get; set; } = OverlayLayout.DefaultCapsuleWidth;

    public double CapsuleHeight { get; set; } = OverlayLayout.DefaultCapsuleHeight;

    /// <summary>浮動視窗上次的位置與大小（DIP）；null 用預設位置。</summary>
    public ScreenRect? FloatingBounds { get; set; }

    /// <summary>全螢幕用哪個螢幕（例如 \\.\DISPLAY2）；null 用主螢幕。</summary>
    public string? FullScreenDisplay { get; set; }

    /// <summary>三種浮層各自的字級（瓦基 2026-10-08），切換樣式時各記各的。</summary>
    public double CapsuleFontSize { get; set; } = OverlayLayout.DefaultFontSize(OverlayStyle.Capsule);

    public double FloatingFontSize { get; set; } = OverlayLayout.DefaultFontSize(OverlayStyle.Floating);

    public double FullScreenFontSize { get; set; } = OverlayLayout.DefaultFontSize(OverlayStyle.FullScreen);

    /// <summary>主視窗外觀，預設跟著系統。</summary>
    public AppTheme Theme { get; set; } = AppTheme.System;

    public double FontSizeFor(OverlayStyle style) => style switch
    {
        OverlayStyle.Capsule => CapsuleFontSize,
        OverlayStyle.Floating => FloatingFontSize,
        _ => FullScreenFontSize,
    };

    public void SetFontSize(OverlayStyle style, double size)
    {
        var clamped = OverlayLayout.ClampFontSize(size);
        switch (style)
        {
            case OverlayStyle.Capsule:
                CapsuleFontSize = clamped;
                break;
            case OverlayStyle.Floating:
                FloatingFontSize = clamped;
                break;
            default:
                FullScreenFontSize = clamped;
                break;
        }
    }

    /// <summary>錄影、分享畫面時看不到浮層（計畫書第 5 階段步驟 5，預設開啟）。</summary>
    public bool HideFromCapture { get; set; } = true;

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Textream", "settings.json");

    public static AppSettings Load(string path)
    {
        AppSettings settings;
        try
        {
            settings = File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new() : new();
        }
        catch (JsonException)
        {
            settings = new(); // 壞掉的設定檔：用預設值，下次存檔時蓋掉
        }
        settings.ScrollSpeed = Session.ScrollSpeed.Clamp(settings.ScrollSpeed);
        settings.CapsuleWidth = Math.Clamp(settings.CapsuleWidth, OverlayLayout.CapsuleMinWidth, OverlayLayout.CapsuleMaxWidth);
        settings.CapsuleHeight = Math.Clamp(settings.CapsuleHeight, OverlayLayout.CapsuleMinHeight, OverlayLayout.CapsuleMaxHeight);
        foreach (var style in Enum.GetValues<OverlayStyle>())
        {
            settings.SetFontSize(style, settings.FontSizeFor(style));
        }
        return settings;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }
}
