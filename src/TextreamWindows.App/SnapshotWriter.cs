using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TextreamWindows.App;

/// <summary>把視窗內容畫成 PNG（RenderTargetBitmap，不經過螢幕），給開發時檢查介面用。</summary>
internal static class SnapshotWriter
{
    /// <summary>
    /// 視窗背景：Fluent 主題的視窗背景是系統的半透明材質（畫不進圖），改用主題的不透明底色；沒有主題就用視窗自己的背景或白色。
    /// </summary>
    private static Brush BackgroundOf(Window window) =>
        window.TryFindResource("SolidBackgroundFillColorBaseBrush") as Brush
        ?? (window.Background is SolidColorBrush { Color.A: 255 } solid ? solid : Brushes.White);

    public static void Save(Window window, string path)
    {
        if (window.Content is not FrameworkElement content)
        {
            return;
        }
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);

        // 視窗背景先鋪上，不然透明的地方存出來是黑的
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(BackgroundOf(window), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
            context.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        }
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
