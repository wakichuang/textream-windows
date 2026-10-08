using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using TextreamWindows.App.Services;
using TextreamWindows.App.ViewModels;
using TextreamWindows.Core.Settings;

namespace TextreamWindows.App;

/// <summary>主視窗的外殼：建立 ViewModel、每 50 毫秒推一次（計時器模式前進、畫面更新）、關閉前問存檔。</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _timer;
    private GlobalHotkeys? _hotkeys;
    private readonly ThemeService _theme;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(new WpfAppServices(this), AppSettings.DefaultPath);
        DataContext = _viewModel;
        // 外觀：跟著系統／淺色／深色（瓦基 2026-10-08）
        _theme = new ThemeService(Application.Current);
        _theme.Apply(_viewModel.Theme);
        // 字級可以直接打數字：按 Enter 就套用（不必等離開欄位）
        FontSizeBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                System.Windows.Data.BindingOperations.GetBindingExpression(FontSizeBox, System.Windows.Controls.ComboBox.TextProperty)?.UpdateSource();
            }
        };
        // 編輯器的游標＝下次開始的起點：瓦基點哪裡就記哪裡；ViewModel 改了（停在半路、按「從頭開始」）就把游標搬過去
        Editor.SelectionChanged += (_, _) => _viewModel.CaretIndex = Editor.CaretIndex;
        // 開始後主視窗收起來、只留浮層，停了再叫回前面（2026-10-08 瓦基要求，參考 Mac 版）
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsOverlayOpen))
            {
                // 浮層開著時 Esc 是全域快捷鍵（瓦基 2026-10-08：頂端膠囊不搶焦點，原本按 Esc 常常沒反應）；關掉就還給別的程式
                if (_viewModel.IsOverlayOpen)
                {
                    _hotkeys?.Add(ModifierKeys.None, Key.Escape, _viewModel.EscapePressed);
                }
                else
                {
                    _hotkeys?.Remove(Key.Escape);
                }
                return;
            }
            if (e.PropertyName == nameof(MainViewModel.Theme))
            {
                _theme.Apply(_viewModel.Theme);
                return;
            }
            if (e.PropertyName == nameof(MainViewModel.CaretIndex) && Editor.CaretIndex != _viewModel.CaretIndex)
            {
                ShowCaret(_viewModel.CaretIndex);
                return;
            }
            if (e.PropertyName != nameof(MainViewModel.IsMainWindowHidden))
            {
                return;
            }
            if (_viewModel.IsMainWindowHidden)
            {
                Hide();
            }
            else
            {
                Show();
                Activate();
                ShowCaret(_viewModel.CaretIndex); // 回來時游標停在上次停下的地方，一眼看得到
            }
        };
        // 全域快捷鍵（第 5.4 步）：掛在主視窗上，主視窗收起來時照樣有效。
        // 瓦基 2026-10-08 改鍵：Ctrl+Alt+Space 在這台被別的程式佔走，開始／暫停改 Ctrl+Alt+L；跳一句改上下鍵比較直覺（↓ 往後，跟滾輪一樣）
        SourceInitialized += (_, _) =>
        {
            _theme.ApplyTitleBar(this);
            _hotkeys = new GlobalHotkeys(this);
            var taken = new List<string>();
            const ModifierKeys CtrlAlt = ModifierKeys.Control | ModifierKeys.Alt;
            if (!_hotkeys.Add(CtrlAlt, Key.L, () => _ = _viewModel.TogglePauseAsync()))
            {
                taken.Add("Ctrl+Alt+L");
            }
            if (!_hotkeys.Add(CtrlAlt, Key.Up, () => _viewModel.JumpSentence(-1)))
            {
                taken.Add("Ctrl+Alt+↑");
            }
            if (!_hotkeys.Add(CtrlAlt, Key.Down, () => _viewModel.JumpSentence(+1)))
            {
                taken.Add("Ctrl+Alt+↓");
            }
            _viewModel.NoteHotkeysTaken(taken);
        };
        // 優先順序一定要比 Render 低：2026-10-08 第一版用 Render，每 50 毫秒插隊，WPF 畫不了畫面，瓦基開起來整片白
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Background, (_, _) => _viewModel.Tick(), Dispatcher);

        // 開發用：TEXTREAM_SNAPSHOT=<檔名.png> 時，畫面畫好就把自己存成圖片再關掉，讓 AI 不靠螢幕截圖也看得到介面
        if (Environment.GetEnvironmentVariable("TEXTREAM_SNAPSHOT") is { Length: > 0 } snapshot)
        {
            ContentRendered += (_, _) => Dispatcher.BeginInvoke(() =>
            {
                SnapshotWriter.Save(this, snapshot);
                Close();
            }, DispatcherPriority.ApplicationIdle);
        }
    }

    /// <summary>游標搬到 <paramref name="index"/>，編輯器拿到焦點、捲到那一行。</summary>
    private void ShowCaret(int index)
    {
        Editor.CaretIndex = Math.Clamp(index, 0, Editor.Text.Length);
        if (!IsVisible)
        {
            return; // 浮層開著時主視窗收起來了，回來時再捲
        }
        Editor.Focus();
        // 等排版好再捲，不然剛顯示出來時算不出行號
        Dispatcher.BeginInvoke(() =>
        {
            var line = Editor.GetLineIndexFromCharacterIndex(Editor.CaretIndex);
            if (line >= 0)
            {
                Editor.ScrollToLine(line);
            }
        }, DispatcherPriority.Loaded);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_viewModel.ConfirmClose())
        {
            e.Cancel = true;
            return;
        }
        _timer.Stop();
        _hotkeys?.Dispose();
        _theme.Dispose();
        _viewModel.Shutdown();
        _viewModel.SaveSettings();
        base.OnClosing(e);
    }
}
