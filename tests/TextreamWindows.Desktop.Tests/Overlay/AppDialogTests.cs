using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TextreamWindows.App.Dialogs;

namespace TextreamWindows.Desktop.Tests.Overlay;

/// <summary>
/// 程式自己的確認視窗（瓦基 2026-10-08：Windows 內建的訊息框很醜，要跟程式同一個風格）。
/// 只建視窗、不顯示：點鍵用 RaiseEvent，看選了什麼。開檔、存檔的選檔視窗照用 Windows 內建的。
/// </summary>
public class AppDialogTests
{
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
        return error is null ? result : throw new InvalidOperationException("STA 執行緒裡出錯", error);
    }

    private static List<Button> Buttons(DependencyObject root)
    {
        var found = new List<Button>();
        if (root is Button b)
        {
            found.Add(b);
        }
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            found.AddRange(Buttons(child));
        }
        return found;
    }

    [Theory]
    [InlineData("儲存", true)]
    [InlineData("不儲存", false)]
    [InlineData("取消", null)]
    public void SaveChangesOffersSaveDontSaveAndCancel(string click, bool? expected)
    {
        var (labels, chosen, message) = OnSta(() =>
        {
            var dialog = AppDialog.SaveChanges("follow01.md");
            var buttons = Buttons(dialog);
            buttons.Single(b => (string)b.Content == click).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            return (buttons.Select(b => (string)b.Content).ToList(), dialog.SaveChoice, dialog.Message);
        });

        Assert.Equal(["儲存", "不儲存", "取消"], labels);
        Assert.Equal(expected, chosen);
        Assert.Contains("follow01.md", message);
    }

    [Fact]
    public void EnterSavesAndEscCancels()
    {
        var (enter, esc) = OnSta(() =>
        {
            var buttons = Buttons(AppDialog.SaveChanges("講稿.md"));
            return ((string)buttons.Single(b => b.IsDefault).Content, (string)buttons.Single(b => b.IsCancel).Content);
        });

        Assert.Equal("儲存", enter);
        Assert.Equal("取消", esc);
    }

    [Fact]
    public void ClosingWithTheTitleBarCancels()
    {
        var chosen = OnSta(() =>
        {
            var dialog = AppDialog.SaveChanges("講稿.md");
            dialog.Close();
            return dialog.SaveChoice;
        });

        Assert.Null(chosen); // 關掉視窗＝取消，不要當成「不儲存」把稿丟掉
    }

    [Fact]
    public void AnErrorHasOneButton()
    {
        var (labels, message) = OnSta(() =>
        {
            var dialog = AppDialog.Error("沒辦法開始：找不到麥克風。");
            return (Buttons(dialog).Select(b => (string)b.Content).ToList(), dialog.Message);
        });

        Assert.Equal(["知道了"], labels);
        Assert.Equal("沒辦法開始：找不到麥克風。", message);
    }

    [Fact]
    public void ItUsesTheAppIconAndTheThemeColors()
    {
        var (icon, background, primaryStyle) = OnSta(() =>
        {
            var dialog = AppDialog.SaveChanges("講稿.md");
            var primary = Buttons(dialog).Single(b => b.IsDefault);
            return (dialog.Icon is not null,
                BindingOperationsHelper.IsDynamic(dialog, Control.BackgroundProperty),
                BindingOperationsHelper.IsDynamic(primary, FrameworkElement.StyleProperty));
        });

        Assert.True(icon, "標題列用 Textream for Windows 的圖示");
        Assert.True(background, "底色跟著主題（淺色米白、深色暖深褐）");
        Assert.True(primaryStyle, "主要的鍵用開始鍵同一款（天空藍）");
    }

    private static class BindingOperationsHelper
    {
        public static bool IsDynamic(DependencyObject target, DependencyProperty property) =>
            target.ReadLocalValue(property)?.GetType().Name == "ResourceReferenceExpression";
    }
}
