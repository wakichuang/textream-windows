using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TextreamWindows.App.Dialogs;

/// <summary>
/// 程式自己的確認視窗（瓦基 2026-10-08：Windows 內建的訊息框很醜，要跟程式同一個風格）。
/// 底色、字色、按鍵跟著主視窗的主題（閱讀前哨站配色，淺色／深色），標題列用 Textream for Windows 的圖示。
/// 主要的鍵是開始鍵同一款（天空藍），Enter 按它、Esc 取消；按標題列的 ✕ 關掉也算取消。
/// 選檔案的開啟／另存視窗照用 Windows 內建的（瓦基同意）。
/// </summary>
public sealed class AppDialog : Window
{
    private readonly record struct Choice(string Label, bool? Value, bool IsPrimary, bool IsCancel);

    private bool? _picked;
    private bool _answered;

    private AppDialog(string title, string message, IReadOnlyList<Choice> choices)
    {
        Title = title;
        Message = message;
        Icon = AppIcon.Image;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Microsoft JhengHei UI");
        FontSize = 15;
        SetResourceReference(StyleProperty, typeof(Window));
        SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
        SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");

        var text = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420,
            LineHeight = 24,
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        var icon = new Image { Source = AppIcon.Image, Width = 44, Height = 44, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Top };
        var body = new DockPanel { Margin = new Thickness(24, 22, 24, 18) };
        DockPanel.SetDock(icon, Dock.Left);
        body.Children.Add(icon);
        body.Children.Add(text);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(24, 0, 24, 20) };
        foreach (var choice in choices)
        {
            var button = new Button
            {
                Content = choice.Label,
                IsDefault = choice.IsPrimary,
                IsCancel = choice.IsCancel,
                MinWidth = 96,
                Padding = new Thickness(16, 6, 16, 6),
                Margin = new Thickness(8, 0, 0, 0),
            };
            if (choice.IsPrimary)
            {
                button.SetResourceReference(StyleProperty, "AccentButtonStyle");
            }
            var value = choice.Value;
            button.Click += (_, _) =>
            {
                _picked = value;
                _answered = true;
                Close();
            };
            buttons.Children.Add(button);
        }

        var layout = new StackPanel { MinWidth = 360 };
        layout.Children.Add(body);
        layout.Children.Add(buttons);
        Content = layout;
    }

    /// <summary>顯示的訊息（測試用）。</summary>
    public string Message { get; }

    /// <summary>「要先存起來嗎？」的答案：true 儲存、false 不儲存、null 取消（含按 Esc、關掉視窗）；「有新版」的答案：true 前往下載。</summary>
    public bool? SaveChoice => _answered ? _picked : null;

    public static AppDialog SaveChanges(string displayName) => new(
        "Textream for Windows",
        $"「{displayName.TrimEnd('*')}」有還沒存的修改，要先存起來嗎？",
        [new("儲存", true, IsPrimary: true, IsCancel: false), new("不儲存", false, false, false), new("取消", null, false, IsCancel: true)]);

    /// <summary>有新版（照 Mac 版 textream-zh）：前往下載開 GitHub 的發佈頁。</summary>
    public static AppDialog UpdateAvailable(string latestVersion, string currentVersion) => new(
        "Textream for Windows",
        $"有新版：Textream for Windows {latestVersion}\n\n你目前是 {currentVersion}。到發佈頁面下載新的 zip，解壓縮後取代舊的資料夾就能更新（設定與講稿不會不見）。",
        [new("前往下載", true, IsPrimary: true, IsCancel: false), new("稍後", false, false, IsCancel: true)]);

    public static AppDialog Info(string message) => new(
        "Textream for Windows",
        message,
        [new("好", null, IsPrimary: true, IsCancel: true)]);

    public static AppDialog Error(string message) => new(
        "Textream for Windows",
        message,
        [new("知道了", null, IsPrimary: true, IsCancel: true)]);
}

/// <summary>程式圖示（Textream 原版＋四格視窗徽章，scripts/make_icon.py）。從內嵌資源讀，不靠 pack 路徑，測試裡也拿得到。</summary>
public static class AppIcon
{
    private static readonly Lazy<BitmapFrame?> Frame = new(() =>
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("TextreamWindows.AppIcon.png");
        if (stream is null)
        {
            return null;
        }
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        frame.Freeze(); // 凍結才能跨執行緒共用
        return frame;
    });

    public static ImageSource? Image => Frame.Value;
}
