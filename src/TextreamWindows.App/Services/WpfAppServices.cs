using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;
using TextreamWindows.App.Dialogs;
using TextreamWindows.App.Overlay;
using TextreamWindows.Core.Overlay;
using TextreamWindows.Core.Session;
using TextreamWindows.Core.Text;
using TextreamWindows.Speech.Audio;

namespace TextreamWindows.App.Services;

/// <summary>正式版的外部服務：Windows 的開檔／存檔對話框、程式自己的確認視窗（AppDialog）、WASAPI 麥克風清單、螢幕清單、浮層視窗。</summary>
public sealed class WpfAppServices(Window owner) : IAppServices
{
    private const string Filter = "講稿（*.md、*.txt）|*.md;*.txt|所有檔案|*.*";

    public IReadOnlyList<MicrophoneInfo> ListMicrophones()
    {
        try
        {
            return Microphone.List();
        }
        catch (COMException)
        {
            return []; // 音訊服務有問題時，至少讓「定速捲動」能用
        }
    }

    public string? AskOpenPath()
    {
        var dialog = new OpenFileDialog { Title = "開啟講稿", Filter = Filter };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public string? AskSavePath(string suggestedName)
    {
        var dialog = new SaveFileDialog { Title = "另存講稿", Filter = Filter, FileName = suggestedName, DefaultExt = ".md" };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    // 確認視窗用程式自己的（瓦基 2026-10-08：Windows 內建的訊息框跟程式風格不一致）；選檔案的視窗照用內建的
    public bool? AskSaveChanges(string displayName)
    {
        var dialog = AppDialog.SaveChanges(displayName);
        Show(dialog);
        return dialog.SaveChoice;
    }

    public void ShowError(string message) => Show(AppDialog.Error(message));

    /// <summary>主視窗看得到時掛在它上面置中；主視窗收起來時（浮層模式）就單獨置中在螢幕上。</summary>
    private void Show(Window dialog)
    {
        if (owner.IsVisible)
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        dialog.SourceInitialized += (_, _) => ThemeService.Current?.ApplyTitleBar(dialog);
        dialog.ShowDialog();
    }

    public Task<IFollowRunner> StartFollowAsync(PromptScript prompt, FollowMode mode, double scrollSpeed, MicrophoneInfo? microphone, int startAt, SpeechLanguage language) =>
        FollowRunners.StartAsync(prompt, mode, scrollSpeed, microphone, startAt, language);

    public IReadOnlyList<DisplayInfo> ListDisplays() => Displays.List();

    public IOverlay ShowOverlay(OverlayStyle style, ScreenRect bounds, PromptScript prompt, FollowMode mode, bool hideFromCapture, double fontSize)
    {
        var window = new OverlayWindow(style, bounds, prompt, mode, fontSize) { HideFromCapture = hideFromCapture };
        window.Show();
        return window;
    }
}
