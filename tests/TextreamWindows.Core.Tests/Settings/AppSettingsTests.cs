using TextreamWindows.Core.Overlay;
using TextreamWindows.Core.Session;
using TextreamWindows.Core.Settings;

namespace TextreamWindows.Core.Tests.Settings;

public sealed class AppSettingsTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("textream-settings-");

    public void Dispose() => _dir.Delete(recursive: true);

    private string PathOf(params string[] parts) => Path.Combine([_dir.FullName, .. parts]);

    [Fact]
    public void DefaultPathIsUnderAppData()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Textream", "settings.json");

        Assert.Equal(expected, AppSettings.DefaultPath);
    }

    [Fact]
    public void MissingFileGivesDefaults()
    {
        var settings = AppSettings.Load(PathOf("沒有這個檔.json"));

        Assert.Null(settings.LastScriptPath);
        Assert.Equal(FollowMode.WordTracking, settings.Mode); // 第 1 節：主打逐字追蹤
        Assert.Null(settings.MicrophoneId);
        Assert.Equal(ScrollSpeed.Default, settings.ScrollSpeed);
    }

    [Fact]
    public void HidingFromScreenCaptureIsOnByDefaultAndRemembered()
    {
        // 計畫書第 5 階段步驟 5：錄影時隱藏，一個開關，預設開啟
        Assert.True(AppSettings.Load(PathOf("沒有這個檔.json")).HideFromCapture);
        Assert.True(AppSettings.Load(WriteJson("old.json", "{\"mode\": \"Classic\"}")).HideFromCapture); // 舊設定檔沒有這欄

        var path = PathOf("off.json");
        new AppSettings { HideFromCapture = false }.Save(path);
        Assert.False(AppSettings.Load(path).HideFromCapture);
    }

    private string WriteJson(string name, string json)
    {
        var path = PathOf(name);
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void SaveCreatesTheFolderAndLoadReadsItBack()
    {
        var path = PathOf("Textream", "settings.json");
        var saved = new AppSettings
        {
            LastScriptPath = @"C:\講稿\EP.660.md",
            Mode = FollowMode.VoiceActivated,
            MicrophoneId = "{0.0.1.00000000}.{focusrite}",
            ScrollSpeed = 4.5,
        };

        saved.Save(path);
        var loaded = AppSettings.Load(path);

        Assert.Equal(saved.LastScriptPath, loaded.LastScriptPath);
        Assert.Equal(saved.Mode, loaded.Mode);
        Assert.Equal(saved.MicrophoneId, loaded.MicrophoneId);
        Assert.Equal(saved.ScrollSpeed, loaded.ScrollSpeed);
    }

    [Fact]
    public void TheFileIsReadableJsonWithModeNames()
    {
        // 設定檔偶爾要手改或貼給 AI 看：模式存名字、中文不轉成 \uXXXX
        var path = PathOf("settings.json");

        new AppSettings { Mode = FollowMode.Classic, LastScriptPath = @"C:\講稿\稿.md" }.Save(path);
        var json = File.ReadAllText(path);

        Assert.Contains("\"mode\": \"Classic\"", json);
        Assert.Contains("講稿", json);
    }

    [Fact]
    public void ACorruptFileFallsBackToDefaults()
    {
        var path = PathOf("settings.json");
        File.WriteAllText(path, "{ 這不是 JSON");

        var settings = AppSettings.Load(path);

        Assert.Equal(FollowMode.WordTracking, settings.Mode);
    }

    [Fact]
    public void UnknownFieldsAreIgnoredAndMissingOnesGetDefaults()
    {
        var path = PathOf("settings.json");
        File.WriteAllText(path, """{ "mode": "Classic", "fromAFutureVersion": 42 }""");

        var settings = AppSettings.Load(path);

        Assert.Equal(FollowMode.Classic, settings.Mode);
        Assert.Equal(ScrollSpeed.Default, settings.ScrollSpeed);
    }

    [Fact]
    public void OverlayDefaultsToTheTopCapsule()
    {
        var settings = AppSettings.Load(PathOf("沒有這個檔.json"));

        Assert.Equal(OverlayStyle.Capsule, settings.OverlayStyle); // 第 3 節：鏡頭在頂端正中央
        Assert.Equal(OverlayLayout.DefaultCapsuleWidth, settings.CapsuleWidth);
        Assert.Equal(OverlayLayout.DefaultCapsuleHeight, settings.CapsuleHeight);
        Assert.Null(settings.FloatingBounds);
        Assert.Null(settings.FullScreenDisplay);
    }

    [Fact]
    public void FontSizesAndThemeHaveDefaultsAndComeBackClamped()
    {
        // 瓦基 2026-10-08：浮層字級可調、要記住；外觀預設跟著系統，可選淺色或深色
        var defaults = AppSettings.Load(PathOf("沒有這個檔.json"));
        Assert.Equal(AppTheme.System, defaults.Theme);
        Assert.Equal(OverlayLayout.DefaultFontSize(OverlayStyle.Capsule), defaults.FontSizeFor(OverlayStyle.Capsule));
        Assert.Equal(OverlayLayout.DefaultFontSize(OverlayStyle.FullScreen), defaults.FontSizeFor(OverlayStyle.FullScreen));

        var path = PathOf("fonts.json");
        var saved = new AppSettings { Theme = AppTheme.Dark };
        saved.SetFontSize(OverlayStyle.Floating, 36);
        saved.Save(path);
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"fullScreenFontSize\": 52", "\"fullScreenFontSize\": 999"));

        var loaded = AppSettings.Load(path);
        Assert.Equal(AppTheme.Dark, loaded.Theme);
        Assert.Equal(36, loaded.FontSizeFor(OverlayStyle.Floating));
        Assert.Equal(OverlayLayout.MaxFontSize, loaded.FontSizeFor(OverlayStyle.FullScreen));
        Assert.Contains("\"theme\": \"Dark\"", File.ReadAllText(path));
    }

    [Theory]
    [InlineData(AppTheme.System, true, false)]
    [InlineData(AppTheme.System, false, true)]
    [InlineData(AppTheme.Light, false, false)]
    [InlineData(AppTheme.Dark, true, true)]
    public void TheThemeChoiceDecidesDarkness(AppTheme choice, bool systemUsesLight, bool dark)
    {
        Assert.Equal(dark, AppThemes.UseDark(choice, systemUsesLight));
    }

    [Fact]
    public void OverlaySettingsComeBack()
    {
        var path = PathOf("settings.json");
        new AppSettings
        {
            OverlayStyle = OverlayStyle.Floating,
            CapsuleWidth = 480,
            CapsuleHeight = 220,
            FloatingBounds = new ScreenRect(-1500, 200, 700, 260),
            FullScreenDisplay = @"\\.\DISPLAY2",
        }.Save(path);

        var loaded = AppSettings.Load(path);

        Assert.Equal(OverlayStyle.Floating, loaded.OverlayStyle);
        Assert.Equal(480, loaded.CapsuleWidth);
        Assert.Equal(220, loaded.CapsuleHeight);
        Assert.Equal(new ScreenRect(-1500, 200, 700, 260), loaded.FloatingBounds);
        Assert.Equal(@"\\.\DISPLAY2", loaded.FullScreenDisplay);
        Assert.Contains("\"overlayStyle\": \"Floating\"", File.ReadAllText(path));
    }

    [Fact]
    public void ACapsuleSizeOutOfRangeIsClampedOnLoad()
    {
        var path = PathOf("settings.json");
        File.WriteAllText(path, """{ "capsuleWidth": 50, "capsuleHeight": 9999 }""");

        var settings = AppSettings.Load(path);

        Assert.Equal(OverlayLayout.CapsuleMinWidth, settings.CapsuleWidth);
        Assert.Equal(OverlayLayout.CapsuleMaxHeight, settings.CapsuleHeight);
    }

    [Fact]
    public void ASpeedOutOfRangeIsClampedOnLoad()
    {
        var path = PathOf("settings.json");
        File.WriteAllText(path, """{ "scrollSpeed": 99 }""");

        Assert.Equal(ScrollSpeed.Maximum, AppSettings.Load(path).ScrollSpeed);
    }
}
