using System.Diagnostics;
using System.IO;
using TextreamWindows.App.Mvvm;
using TextreamWindows.App.Services;
using TextreamWindows.Core.Documents;
using TextreamWindows.Core.Overlay;
using TextreamWindows.Core.Session;
using TextreamWindows.Core.Settings;
using TextreamWindows.Core.Text;
using TextreamWindows.Speech.Audio;

namespace TextreamWindows.App.ViewModels;

/// <summary>麥克風下拉選單的一項。</summary>
public sealed record MicrophoneOption(MicrophoneInfo Info)
{
    public string Label => Info.Name + (Info.IsDefault ? "（預設）" : "") + (Info.LooksVirtual ? "（虛擬裝置）" : "");
}

/// <summary>「錄影、分享畫面時」下拉選單的一項。</summary>
public sealed record CaptureOption(bool HideFromCapture, string Label);

/// <summary>外觀下拉選單的一項。</summary>
public sealed record ThemeOption(AppTheme Value, string Label);

/// <summary>全螢幕下拉選單的一項。</summary>
public sealed record DisplayOption(DisplayInfo Info, int Number)
{
    public string Label => $"螢幕 {Number}{(Info.IsPrimary ? "（主）" : "")} {Info.Bounds.Width:0}×{Info.Bounds.Height:0}";
}

/// <summary>
/// 主視窗：講稿編輯器、模式、麥克風、速度、開始／停止（計畫書第 5 階段步驟 1）。
/// 不碰 WPF：對話框與跟讀都經過 <see cref="IAppServices"/>，畫面由 UI 計時器每 50 毫秒呼叫 <see cref="Tick"/> 更新。
/// 按「開始」開浮層（第 5.2 步），下面那條狀態列也同步顯示讀到哪裡。
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private const int UpcomingLength = 24;

    /// <summary>起點提示裡秀幾個字，讓瓦基確認是不是要從這裡開始。</summary>
    private const int StartPointPreviewLength = 12;

    private readonly IAppServices _services;
    private readonly string _settingsPath;
    private ScriptDocument _document = new();
    private FollowMode _mode;
    private SpeechLanguage _speechLanguage;
    private MicrophoneOption? _selectedMicrophone;
    private double _scrollSpeed;
    private IFollowRunner? _runner;
    private PromptScript? _runningPrompt;
    private bool _isStarting;
    private double _progressPercent;
    private string _statusText = "";
    private string _upcomingText = "";
    private OverlayStyle _overlayStyle;
    private double _capsuleWidth;
    private double _capsuleHeight;
    private ScreenRect? _floatingBounds;
    private DisplayOption? _selectedDisplay;
    private IOverlay? _overlay;
    private readonly Stopwatch _runClock = new();
    private bool _stopRequested;
    private bool _showingDone;
    private bool _restarting;
    private int _caretIndex;
    private bool _hideFromCapture;
    private AppTheme _theme;
    private readonly AppSettings _fontSizes = new(); // 只拿來記三種浮層的字級

    public MainViewModel(IAppServices services, string settingsPath)
    {
        _services = services;
        _settingsPath = settingsPath;
        OpenCommand = new RelayCommand(Open, () => IsEditable);
        SaveCommand = new RelayCommand(() => Save(), () => IsEditable);
        SaveAsCommand = new RelayCommand(() => SaveAs(), () => IsEditable);
        StartStopCommand = new RelayCommand(() => _ = StartStopAsync(), () => !_isStarting && (IsRunning || !string.IsNullOrWhiteSpace(ScriptText)));
        StartFromTopCommand = new RelayCommand(() => CaretIndex = 0, () => IsEditable && StartPoint(new PromptScript(ScriptText)) > 0);

        Microphones = services.ListMicrophones().Select(m => new MicrophoneOption(m)).ToList();
        var settings = AppSettings.Load(settingsPath);
        _mode = settings.Mode;
        _speechLanguage = settings.SpeechLanguage;
        _scrollSpeed = settings.ScrollSpeed;
        Displays = services.ListDisplays().Select((d, i) => new DisplayOption(d, i + 1)).ToList();
        _overlayStyle = settings.OverlayStyle;
        _capsuleWidth = settings.CapsuleWidth;
        _capsuleHeight = settings.CapsuleHeight;
        _floatingBounds = settings.FloatingBounds;
        _hideFromCapture = settings.HideFromCapture;
        _theme = settings.Theme;
        foreach (var style in Enum.GetValues<OverlayStyle>())
        {
            _fontSizes.SetFontSize(style, settings.FontSizeFor(style));
        }
        _selectedDisplay = Displays.FirstOrDefault(d => d.Info.Name == settings.FullScreenDisplay)
            ?? Displays.FirstOrDefault(d => d.Info.IsPrimary)
            ?? Displays.FirstOrDefault();
        _selectedMicrophone = Microphones.FirstOrDefault(m => m.Info.Id == settings.MicrophoneId)
            ?? Microphones.FirstOrDefault(m => m.Info.IsDefault && !m.Info.LooksVirtual)
            ?? Microphones.FirstOrDefault(m => !m.Info.LooksVirtual)
            ?? Microphones.FirstOrDefault();
        if (settings.LastScriptPath is { } last && File.Exists(last))
        {
            TryLoad(last);
        }
        _statusText = "準備好了：貼上或開啟講稿，按「開始」。";
    }

    // ── 編輯器 ──

    public string ScriptText
    {
        get => _document.Text;
        set
        {
            if (_document.Text == value)
            {
                return;
            }
            _document.Text = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(StartPointText));
            ShowStartPointProgress();
            StartStopCommand.Refresh();
            StartFromTopCommand.Refresh();
        }
    }

    public string WindowTitle => $"Textream for Windows — {_document.DisplayName}";

    /// <summary>跑的時候講稿鎖住，不然高亮會對不上。</summary>
    public bool IsEditable => !IsRunning && !_isStarting;

    public RelayCommand OpenCommand { get; }

    public RelayCommand SaveCommand { get; }

    public RelayCommand SaveAsCommand { get; }

    /// <summary>
    /// 編輯器的游標（<see cref="ScriptText"/> 的 UTF-16 位置），也是下次按「開始」的起點（瓦基 2026-10-08）。
    /// 停在半路時，這裡會被設成停下的地方，回到編輯器調一調再按開始就接著念。
    /// </summary>
    public int CaretIndex
    {
        get => _caretIndex;
        set
        {
            if (Set(ref _caretIndex, Math.Max(0, value)))
            {
                ShowStartPointProgress();
                OnPropertyChanged(nameof(StartPointText));
                StartFromTopCommand.Refresh();
            }
        }
    }

    /// <summary>按「開始」會從哪裡念，主視窗下方顯示。</summary>
    public string StartPointText
    {
        get
        {
            var prompt = new PromptScript(ScriptText);
            var start = StartPoint(prompt);
            if (start == 0)
            {
                return "從頭開始。想從中間開始：在講稿裡點一下要開始的地方。";
            }
            var upcoming = string.Concat(prompt.Elements.Skip(start).Where(e => !TextElements.IsWhitespace(e)).Take(StartPointPreviewLength));
            return $"從游標處開始（約 {ReadPercent(prompt, start):0}%）：「{upcoming}…」";
        }
    }

    /// <summary>游標放回最前面。</summary>
    public RelayCommand StartFromTopCommand { get; }

    // ── 模式、麥克風、速度 ──

    public FollowMode Mode
    {
        get => _mode;
        set
        {
            if (Set(ref _mode, value))
            {
                OnPropertyChanged(nameof(IsMicrophoneEnabled));
                OnPropertyChanged(nameof(IsSpeedEnabled));
                OnPropertyChanged(nameof(IsSpeechLanguageEnabled));
            }
        }
    }

    /// <summary>講稿念的是哪種語言（瓦基 2026-10-08）：逐字追蹤照它選比對規則，模型不變。</summary>
    public SpeechLanguage SpeechLanguage
    {
        get => _speechLanguage;
        set => Set(ref _speechLanguage, value);
    }

    /// <summary>只有逐字追蹤聽字，另外兩種模式用不到辨識語言。</summary>
    public bool IsSpeechLanguageEnabled => Mode == FollowMode.WordTracking && IsEditable;

    /// <summary>定速捲動不聽聲音，不用麥克風。</summary>
    public bool IsMicrophoneEnabled => Mode != FollowMode.Classic && IsEditable;

    /// <summary>逐字追蹤跟著聲音走，速度用不到。</summary>
    public bool IsSpeedEnabled => Mode != FollowMode.WordTracking && IsEditable;

    public IReadOnlyList<MicrophoneOption> Microphones { get; }

    public MicrophoneOption? SelectedMicrophone
    {
        get => _selectedMicrophone;
        set => Set(ref _selectedMicrophone, value);
    }

    /// <summary>每秒幾個字，夾在 0.5～8。</summary>
    public double ScrollSpeed
    {
        get => _scrollSpeed;
        set => Set(ref _scrollSpeed, Core.Session.ScrollSpeed.Clamp(value));
    }

    // ── 浮層 ──

    public OverlayStyle OverlayStyle
    {
        get => _overlayStyle;
        set
        {
            if (Set(ref _overlayStyle, value))
            {
                OnPropertyChanged(nameof(IsCapsuleSizeEnabled));
                OnPropertyChanged(nameof(IsDisplayChoiceEnabled));
                OnPropertyChanged(nameof(OverlayFontSize));
                OnPropertyChanged(nameof(SizeHint));
            }
        }
    }

    /// <summary>膠囊寬度，夾在 280～500。</summary>
    public double CapsuleWidth
    {
        get => _capsuleWidth;
        set => Set(ref _capsuleWidth, Math.Clamp(value, OverlayLayout.CapsuleMinWidth, OverlayLayout.CapsuleMaxWidth));
    }

    /// <summary>膠囊高度，夾在 100～400。</summary>
    public double CapsuleHeight
    {
        get => _capsuleHeight;
        set => Set(ref _capsuleHeight, Math.Clamp(value, OverlayLayout.CapsuleMinHeight, OverlayLayout.CapsuleMaxHeight));
    }

    /// <summary>膠囊大小只對頂端膠囊有用。</summary>
    public bool IsCapsuleSizeEnabled => OverlayStyle == OverlayStyle.Capsule && IsEditable;

    public IReadOnlyList<DisplayOption> Displays { get; }

    public DisplayOption? SelectedDisplay
    {
        get => _selectedDisplay;
        set => Set(ref _selectedDisplay, value);
    }

    /// <summary>
    /// 錄影、分享畫面時浮層看不看得到（瓦基 2026-10-08）：平常看不到（預設），錄示範給朋友看時切成錄得到。
    /// 背後就是 <see cref="HideFromCapture"/>，原本是一個勾選框，改成講清楚的兩個選項。
    /// </summary>
    public IReadOnlyList<CaptureOption> CaptureOptions { get; } =
    [
        new(true, "看不到浮層（預設）"),
        new(false, "錄得到浮層（示範用）"),
    ];

    /// <summary>目前選的浮層樣式的講稿字級（瓦基 2026-10-08）；三種浮層各記各的，夾在 14～96。</summary>
    public double OverlayFontSize
    {
        get => _fontSizes.FontSizeFor(OverlayStyle);
        set
        {
            var before = OverlayFontSize;
            _fontSizes.SetFontSize(OverlayStyle, value);
            if (OverlayFontSize != before)
            {
                OnPropertyChanged();
            }
        }
    }

    /// <summary>寬高拉條只管頂端膠囊；選別的浮層時，拉條旁邊講一聲大小怎麼調。</summary>
    public string SizeHint => OverlayStyle switch
    {
        OverlayStyle.Floating => "浮動視窗：拖它的邊框調大小，會記住",
        OverlayStyle.FullScreen => "全螢幕：蓋滿選定的螢幕",
        _ => "",
    };

    /// <summary>字級下拉的常用值（也可以直接打數字）。</summary>
    public IReadOnlyList<double> FontSizeChoices { get; } = [16, 20, 24, 28, 32, 40, 48, 56, 64, 72, 96];

    /// <summary>主視窗外觀（瓦基 2026-10-08）：跟著系統、淺色、深色。</summary>
    public AppTheme Theme
    {
        get => _theme;
        set => Set(ref _theme, value);
    }

    public IReadOnlyList<ThemeOption> ThemeOptions { get; } =
    [
        new(AppTheme.System, "跟著系統"),
        new(AppTheme.Light, "淺色"),
        new(AppTheme.Dark, "深色"),
    ];

    /// <summary>錄影、分享畫面時看不到浮層（計畫書第 5 階段步驟 5，預設開啟）。</summary>
    public bool HideFromCapture
    {
        get => _hideFromCapture;
        set => Set(ref _hideFromCapture, value);
    }

    /// <summary>選螢幕只對全螢幕有用。</summary>
    public bool IsDisplayChoiceEnabled => OverlayStyle == OverlayStyle.FullScreen && IsEditable;

    // ── 開始／停止 ──

    public RelayCommand StartStopCommand { get; }

    public string StartStopLabel => _isStarting ? "準備中…" : IsRunning ? "停止" : "開始";

    public bool IsRunning => _runner is not null;

    /// <summary>跑的時候主視窗收起來、只留浮層（參考 Mac 版）；停了再回來。</summary>
    public bool IsMainWindowHidden => IsRunning || _showingDone || _restarting;

    public double ProgressPercent
    {
        get => _progressPercent;
        private set => Set(ref _progressPercent, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    /// <summary>接下來要念的幾個字（5.2 浮層出來前的暫時顯示）。</summary>
    public string UpcomingText
    {
        get => _upcomingText;
        private set => Set(ref _upcomingText, value);
    }

    public async Task StartStopAsync()
    {
        if (_runner is not null)
        {
            _stopRequested = true;
            _runner.Stop();
            return; // 下一次 Tick 看到 HasStopped 才收尾，跟「讀完自己停」走同一條路
        }
        if (_showingDone)
        {
            EndDoneDisplay(); // 「讀完了」畫面上又按開始（例如 Ctrl+Alt+L）：先收掉再開始
        }
        if (_isStarting || string.IsNullOrWhiteSpace(ScriptText))
        {
            return;
        }

        var prompt = new PromptScript(ScriptText);
        SetStarting(true);
        StatusText = Mode == FollowMode.WordTracking ? "準備中：載入語音模型…" : "準備中…";
        try
        {
            var microphone = Mode == FollowMode.Classic ? null : SelectedMicrophone?.Info;
            _runner = await _services.StartFollowAsync(prompt, Mode, ScrollSpeed, microphone, StartPoint(prompt), SpeechLanguage);
            _runningPrompt = prompt;
            _stopRequested = false;
            _runClock.Restart();
            _overlay = _services.ShowOverlay(OverlayStyle, OverlayBounds(), prompt, Mode, HideFromCapture, OverlayFontSize);
            StatusText = "開始念吧。";
        }
        catch (Exception ex)
        {
            _runner?.Stop(); // 跟讀開了、浮層卻開不起來時，麥克風也要關
            _runner?.Dispose();
            _runner = null;
            _services.ShowError($"沒辦法開始：{ex.Message}");
            StatusText = "沒有開始。";
        }
        finally
        {
            _restarting = false;
            SetStarting(false);
        }
        UpdateProgress();
    }

    /// <summary>UI 計時器每 50 毫秒呼叫：推進計時器模式、更新畫面、發現停了就收尾。</summary>
    public void Tick()
    {
        if (_runner is null)
        {
            if (_showingDone && _overlay is { } done)
            {
                // 「讀完了」停在畫面上，等瓦基自己選（2026-10-08）：✕／Esc 回編輯器、「重來」從頭再念一次
                if (done.RestartRequested)
                {
                    _ = RestartFromTopAsync();
                }
                else if (done.WasClosedByUser)
                {
                    EndDoneDisplay();
                }
            }
            return;
        }
        if (_overlay?.WasClosedByUser == true && !_runner.HasStopped)
        {
            _stopRequested = true;
            _runner.Stop(); // 瓦基在浮層上按了 Esc 或 ✕
        }
        if (_overlay?.TakeJumpRequest() is { } jump && !_runner.HasStopped)
        {
            _runner.JumpTo(jump); // 瓦基在浮層上點了字、或轉了滾輪
        }
        _runner.Tick();
        UpdateProgress();
        if (_runner.HasStopped)
        {
            FinishRun();
        }
    }

    /// <summary>Ctrl+Alt+L：沒在跑就開始；跑的時候暫停或繼續，浮層留著（計畫書第 5 階段步驟 4）。</summary>
    public async Task TogglePauseAsync()
    {
        if (_runner is not { } runner)
        {
            // 「讀完了」畫面上按 Ctrl+Alt+L：從頭再念一次（瓦基 2026-10-08）
            await (_showingDone ? RestartFromTopAsync() : StartStopAsync());
            return;
        }
        if (runner.HasStopped)
        {
            return;
        }
        if (runner.IsPaused)
        {
            runner.Resume();
            _runClock.Start();
        }
        else
        {
            runner.Pause();
            _runClock.Stop(); // 經過時間也停
        }
        UpdateProgress();
    }

    /// <summary>Ctrl+Alt+↑ ／ ↓：跳到上一句或下一句（<see cref="SentenceNavigation"/>）。</summary>
    public void JumpSentence(int direction)
    {
        if (_runner is not { } runner || _runningPrompt is not { } prompt)
        {
            return;
        }
        var display = new PromptDisplay(prompt);
        var progress = Math.Clamp(runner.Progress, 0, prompt.CharacterCount);
        runner.JumpTo(direction > 0
            ? SentenceNavigation.Next(prompt, display, progress)
            : SentenceNavigation.Previous(prompt, display, progress));
        UpdateProgress();
    }

    /// <summary>全域快捷鍵被別的程式佔走（或開了兩個 Textream）：狀態列講一聲就好，不跳訊息框。</summary>
    /// <summary>浮層在畫面上（念的時候、暫停中、「讀完了」畫面）：這段時間 Esc 是全域快捷鍵。</summary>
    public bool IsOverlayOpen => _overlay is not null;

    /// <summary>
    /// Esc（瓦基 2026-10-08：有時候按了沒反應）：念的時候、暫停中都是停止，「讀完了」畫面是回編輯器。
    /// 浮層開著時主視窗把 Esc 註冊成全域快捷鍵，不靠浮層有沒有拿到焦點（頂端膠囊刻意不搶焦點）。
    /// </summary>
    public void EscapePressed()
    {
        if (_showingDone)
        {
            EndDoneDisplay();
        }
        else if (_runner is { HasStopped: false } runner)
        {
            _stopRequested = true;
            runner.Stop(); // 下一次 Tick 收尾，跟按 ✕ 一樣，也會記住停在哪
        }
    }

    public void NoteHotkeysTaken(IReadOnlyList<string> taken)
    {
        if (taken.Count > 0)
        {
            StatusText = $"快捷鍵 {string.Join("、", taken)} 被別的程式佔走了，這次請用按鈕。";
        }
    }

    public bool ConfirmClose() => ResolveUnsavedChanges();

    /// <summary>關視窗：還在跑就停掉（麥克風不能留在背景錄）。</summary>
    public void Shutdown()
    {
        _showingDone = false;
        CloseOverlay();
        if (_runner is { } runner)
        {
            _runner = null;
            runner.Stop();
            runner.Dispose();
        }
    }

    public void SaveSettings() => new AppSettings
    {
        LastScriptPath = _document.FilePath,
        Mode = Mode,
        SpeechLanguage = SpeechLanguage,
        MicrophoneId = SelectedMicrophone?.Info.Id,
        ScrollSpeed = ScrollSpeed,
        OverlayStyle = OverlayStyle,
        CapsuleWidth = CapsuleWidth,
        CapsuleHeight = CapsuleHeight,
        FloatingBounds = _floatingBounds,
        FullScreenDisplay = SelectedDisplay?.Info.Name,
        HideFromCapture = HideFromCapture,
        CapsuleFontSize = _fontSizes.CapsuleFontSize,
        FloatingFontSize = _fontSizes.FloatingFontSize,
        FullScreenFontSize = _fontSizes.FullScreenFontSize,
        Theme = Theme,
    }.Save(_settingsPath);

    /// <summary>照樣式算浮層的位置（OverlayLayout）。</summary>
    private ScreenRect OverlayBounds()
    {
        var displays = Displays.Select(d => d.Info).ToList();
        return OverlayStyle switch
        {
            OverlayStyle.Capsule => OverlayLayout.Capsule(displays, CapsuleWidth, CapsuleHeight),
            OverlayStyle.Floating => OverlayLayout.Floating(displays, _floatingBounds),
            _ => OverlayLayout.FullScreen(displays, SelectedDisplay?.Info.Name),
        };
    }

    /// <summary>關浮層；浮動視窗被拖過、拉過的話，記下最後的位置給下次用。</summary>
    private void CloseOverlay()
    {
        if (_overlay is not { } overlay)
        {
            return;
        }
        _overlay = null;
        if (OverlayStyle == OverlayStyle.Floating)
        {
            _floatingBounds = overlay.Bounds;
        }
        else if (OverlayStyle == OverlayStyle.Capsule)
        {
            // 瓦基可能拖邊框改了膠囊大小：記下來，下次照這個大小開
            CapsuleWidth = overlay.Bounds.Width;
            CapsuleHeight = overlay.Bounds.Height;
        }
        overlay.Close();
    }

    private void UpdateProgress()
    {
        if (_runner is null || _runningPrompt is null)
        {
            return;
        }
        var prompt = _runningPrompt;
        var progress = Math.Clamp(_runner.Progress, 0, prompt.CharacterCount);
        ProgressPercent = ReadPercent(prompt, progress);
        _overlay?.Update(new OverlayStatus(progress, _runClock.Elapsed, _runner.AudioLevel, _runner.IsSpeaking, _runner.IsPaused, _runner.Heard));
        UpcomingText = string.Concat(prompt.Elements.Skip(progress).Where(e => !TextElements.IsWhitespace(e)).Take(UpcomingLength));
        if (!_runner.HasStopped)
        {
            StatusText = _runner.IsPaused ? "暫停中（Ctrl+Alt+L 繼續）" : Mode switch
            {
                FollowMode.Classic => $"定速捲動，每秒 {ScrollSpeed:0.0} 字",
                _ => _runner.IsSpeaking ? "● 講話中" : "○ 安靜",
            };
        }
    }

    private void FinishRun()
    {
        var runner = _runner!;
        _runner = null;
        _runClock.Stop();
        var readToTheEnd = runner.Error is null && !_stopRequested && _runningPrompt is { } prompt && runner.Progress >= prompt.CharacterCount;
        if (_runningPrompt is { } stoppedIn)
        {
            // 記住停在哪裡：游標放過去，下次按開始就從這裡接著念（讀到結尾就是最後面，下次從頭）
            CaretIndex = new PromptDisplay(stoppedIn).ToDisplayOffset(runner.Progress);
        }
        runner.Dispose();
        if (readToTheEnd && _overlay is { } overlay)
        {
            // 讀完了：浮層停在「讀完了」，等瓦基選重來或結束（2026-10-08，原本照 Mac 版一秒後自己收），主視窗還不出來
            overlay.ShowDone();
            _showingDone = true;
            StatusText = "讀完了。";
            RefreshRunState();
            return;
        }

        CloseOverlay();
        RefreshRunState(); // 主視窗先回來，下面的訊息框才掛得上去
        if (runner.Error is { } error)
        {
            StatusText = "中斷了。";
            _services.ShowError($"跟讀中斷：{error.Message}");
        }
        else
        {
            StatusText = runner.Progress >= (_runningPrompt?.CharacterCount ?? int.MaxValue) ? "讀完了。" : "停止了。";
        }
    }

    /// <summary>「讀完了」畫面上按「重來」或 Ctrl+Alt+L：從頭再念。載入模型那一兩秒，編輯器不要先跳出來又收回去。</summary>
    private async Task RestartFromTopAsync()
    {
        _restarting = true;
        try
        {
            EndDoneDisplay();
            CaretIndex = 0;
            await StartStopAsync();
        }
        finally
        {
            if (_restarting)
            {
                _restarting = false; // 沒開始成（例如講稿是空的）：編輯器要回來
                RefreshRunState();
            }
        }
    }

    private void EndDoneDisplay()
    {
        _showingDone = false;
        CloseOverlay();
        RefreshRunState();
    }

    private void SetStarting(bool starting)
    {
        _isStarting = starting;
        RefreshRunState();
    }

    private void RefreshRunState()
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsMainWindowHidden));
        OnPropertyChanged(nameof(IsOverlayOpen));
        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(IsMicrophoneEnabled));
        OnPropertyChanged(nameof(IsSpeechLanguageEnabled));
        OnPropertyChanged(nameof(IsSpeedEnabled));
        OnPropertyChanged(nameof(IsCapsuleSizeEnabled));
        OnPropertyChanged(nameof(IsDisplayChoiceEnabled));
        OnPropertyChanged(nameof(StartStopLabel));
        StartStopCommand.Refresh();
        OpenCommand.Refresh();
        SaveCommand.Refresh();
        SaveAsCommand.Refresh();
        StartFromTopCommand.Refresh();
    }

    /// <summary>按「開始」要從講稿哪裡念：游標的位置；游標後面已經沒有字（最後面）就從頭。</summary>
    private int StartPoint(PromptScript prompt)
    {
        var start = new PromptDisplay(prompt).ToCharacterOffset(Math.Min(CaretIndex, prompt.RawText.Length));
        return prompt.Elements.Skip(start).Any(TextElements.IsLetterOrNumber) ? start : 0;
    }

    /// <summary>沒在念的時候，進度條顯示下次開始的位置（瓦基 2026-10-08 選 A：跟「從游標處開始（約 N%）」同一個數字）。</summary>
    private void ShowStartPointProgress()
    {
        if (!IsRunning)
        {
            var prompt = new PromptScript(ScriptText);
            ProgressPercent = ReadPercent(prompt, StartPoint(prompt));
        }
    }

    /// <summary>念到 <paramref name="offset"/> 時讀過幾 %（只算字，標點與空白不算）。</summary>
    private static double ReadPercent(PromptScript prompt, int offset)
    {
        var readable = prompt.Elements.Take(offset).Count(TextElements.IsLetterOrNumber);
        var total = Math.Max(1, prompt.Elements.Count(TextElements.IsLetterOrNumber));
        return 100.0 * readable / total;
    }

    // ── 開檔、存檔 ──

    private void Open()
    {
        if (!ResolveUnsavedChanges() || _services.AskOpenPath() is not { } path)
        {
            return;
        }
        TryLoad(path);
    }

    private void TryLoad(string path)
    {
        try
        {
            _document = ScriptDocument.Load(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _services.ShowError($"開不了 {path}：{ex.Message}");
            return;
        }
        OnPropertyChanged(nameof(ScriptText));
        OnPropertyChanged(nameof(WindowTitle));
        CaretIndex = 0;
        ShowStartPointProgress();
        OnPropertyChanged(nameof(StartPointText));
        StartStopCommand.Refresh();
        StartFromTopCommand.Refresh();
    }

    private bool Save() => _document.FilePath is null ? SaveAs() : Write(() => _document.Save());

    private bool SaveAs()
    {
        var suggested = _document.FilePath is { } current ? Path.GetFileName(current) : "講稿.md";
        return _services.AskSavePath(suggested) is { } path && Write(() => _document.SaveAs(path));
    }

    private bool Write(Action save)
    {
        try
        {
            save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _services.ShowError($"存不了：{ex.Message}");
            return false;
        }
        OnPropertyChanged(nameof(WindowTitle));
        return true;
    }

    /// <summary>有沒存的修改時先問。回 false 代表瓦基取消、或存檔失敗，原本的動作不要做。</summary>
    private bool ResolveUnsavedChanges()
    {
        if (!_document.IsDirty)
        {
            return true;
        }
        return _services.AskSaveChanges(_document.DisplayName) switch
        {
            true => Save(),
            false => true,
            null => false,
        };
    }
}
