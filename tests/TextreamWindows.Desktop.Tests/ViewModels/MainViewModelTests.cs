using TextreamWindows.App.Services;
using TextreamWindows.App.ViewModels;
using TextreamWindows.Core.Overlay;
using TextreamWindows.Core.Session;
using TextreamWindows.Core.Settings;
using TextreamWindows.Core.Text;
using TextreamWindows.Speech.Audio;

namespace TextreamWindows.Desktop.Tests.ViewModels;

/// <summary>主視窗的行為（不開真的視窗、不開麥克風）：對話框與跟讀都換成假的。</summary>
public sealed class MainViewModelTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("textream-vm-");
    private readonly FakeServices _services = new();

    public void Dispose() => _dir.Delete(recursive: true);

    private string SettingsPath => Path.Combine(_dir.FullName, "settings.json");

    private string ScriptPath(string name, string text)
    {
        var path = Path.Combine(_dir.FullName, name);
        File.WriteAllText(path, text);
        return path;
    }

    private MainViewModel Create() => new(_services, SettingsPath);

    // ── 編輯器 ──

    [Fact]
    public void StartsUntitledAndCannotStartWithAnEmptyScript()
    {
        var vm = Create();

        Assert.Equal("Textream for Windows — 未命名", vm.WindowTitle);
        Assert.False(vm.StartStopCommand.CanExecute(null));
    }

    [Fact]
    public void TypingMarksTheTitleAndEnablesStart()
    {
        var vm = Create();

        vm.ScriptText = "我已經讀完了";

        Assert.Equal("Textream for Windows — 未命名*", vm.WindowTitle);
        Assert.True(vm.StartStopCommand.CanExecute(null));
    }

    [Fact]
    public void OpenLoadsTheFileWhenThereIsNothingToLose()
    {
        _services.NextOpenPath = ScriptPath("EP.660.md", "## 開場\n大家好");
        var vm = Create();

        vm.OpenCommand.Execute(null);

        Assert.Equal("## 開場\n大家好", vm.ScriptText);
        Assert.Equal("Textream for Windows — EP.660.md", vm.WindowTitle);
    }

    [Theory]
    [InlineData(null, "沒存的稿")] // 取消：什麼都不做
    [InlineData(false, "新檔內容")] // 不存：直接開新檔
    public void OpeningOverUnsavedChangesAsksFirst(bool? answer, string expectedText)
    {
        _services.NextOpenPath = ScriptPath("新檔.md", "新檔內容");
        _services.SaveChangesAnswer = answer;
        var vm = Create();
        vm.ScriptText = "沒存的稿";

        vm.OpenCommand.Execute(null);

        Assert.Equal(["未命名*"], _services.AskedToSave);
        Assert.Equal(expectedText, vm.ScriptText);
    }

    [Fact]
    public void SavingAnUntitledScriptAsksForAPath()
    {
        var target = Path.Combine(_dir.FullName, "新稿.md");
        _services.NextSavePath = target;
        var vm = Create();
        vm.ScriptText = "第一版";

        vm.SaveCommand.Execute(null);

        Assert.Equal("第一版", File.ReadAllText(target));
        Assert.Equal("Textream for Windows — 新稿.md", vm.WindowTitle);
    }

    [Fact]
    public void ClosingWithUnsavedChangesCanBeCancelled()
    {
        _services.SaveChangesAnswer = null;
        var vm = Create();
        vm.ScriptText = "沒存的稿";

        Assert.False(vm.ConfirmClose());
    }

    // ── 模式、麥克風、速度 ──

    [Theory]
    [InlineData(FollowMode.WordTracking, true, false)]
    [InlineData(FollowMode.Classic, false, true)]
    [InlineData(FollowMode.VoiceActivated, true, true)]
    public void ModeDecidesWhichControlsApply(FollowMode mode, bool microphone, bool speed)
    {
        var vm = Create();

        vm.Mode = mode;

        Assert.Equal(microphone, vm.IsMicrophoneEnabled);
        Assert.Equal(speed, vm.IsSpeedEnabled);
    }

    [Fact]
    public void PicksTheWindowsDefaultMicrophoneWhenNothingIsSaved()
    {
        var vm = Create();

        Assert.Equal("Analogue 1 + 2 (Focusrite USB Audio)", vm.SelectedMicrophone?.Info.Name);
    }

    [Fact]
    public void SpeedIsClamped()
    {
        var vm = Create();

        vm.ScrollSpeed = 99;

        Assert.Equal(ScrollSpeed.Maximum, vm.ScrollSpeed);
    }

    // ── 設定 ──

    [Fact]
    public void SettingsComeBackOnTheNextLaunch()
    {
        var script = ScriptPath("EP.660.md", "大家好");
        _services.NextOpenPath = script;
        var first = Create();
        first.OpenCommand.Execute(null);
        first.Mode = FollowMode.VoiceActivated;
        first.ScrollSpeed = 4.5;
        first.SelectedMicrophone = first.Microphones.Single(m => m.Info.Name.Contains("Voice Changer"));
        first.SaveSettings();

        var second = Create();

        Assert.Equal("大家好", second.ScriptText);
        Assert.Equal(FollowMode.VoiceActivated, second.Mode);
        Assert.Equal(4.5, second.ScrollSpeed);
        Assert.Contains("Voice Changer", second.SelectedMicrophone?.Info.Name);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HidingFromScreenCaptureGoesToTheOverlayAndIsRemembered(bool hide)
    {
        var first = Create();
        Assert.True(first.HideFromCapture); // 預設開啟
        first.HideFromCapture = hide;
        first.ScriptText = "我讀書";

        await first.StartStopAsync();
        first.SaveSettings();

        Assert.Equal(hide, _services.Overlays[0].HideFromCapture);
        Assert.Equal(hide, Create().HideFromCapture);
    }

    // ── 字級、外觀（瓦基 2026-10-08） ──

    [Fact]
    public async Task EachOverlayStyleRemembersItsOwnFontSize()
    {
        var first = Create();
        Assert.Equal(22, first.OverlayFontSize); // 頂端膠囊預設
        first.OverlayFontSize = 30;
        first.OverlayStyle = OverlayStyle.Floating;
        Assert.Equal(28, first.OverlayFontSize);
        first.OverlayStyle = OverlayStyle.Capsule;
        Assert.Equal(30, first.OverlayFontSize);
        first.ScriptText = "我讀書";

        await first.StartStopAsync();
        first.SaveSettings();

        Assert.Equal(30, _services.Overlays[0].FontSize);
        Assert.Equal(30, Create().OverlayFontSize);
    }

    [Theory]
    [InlineData(OverlayStyle.Capsule, "")]
    [InlineData(OverlayStyle.Floating, "拖")]
    [InlineData(OverlayStyle.FullScreen, "螢幕")]
    public void TheSizeHintSaysWhatTheSlidersApplyTo(OverlayStyle style, string expected)
    {
        // 寬高拉條只管頂端膠囊；選別的浮層時講一聲怎麼調，免得以為壞了（UUPM：helper text）
        var vm = Create();

        vm.OverlayStyle = style;

        Assert.Contains(expected, vm.SizeHint);
        Assert.Equal(style == OverlayStyle.Capsule, vm.SizeHint.Length == 0);
    }

    [Fact]
    public void CommonFontSizesAreOfferedAndAllAllowed()
    {
        var vm = Create();

        Assert.Contains(20, vm.FontSizeChoices);
        Assert.Contains(24, vm.FontSizeChoices);
        Assert.All(vm.FontSizeChoices, size => Assert.InRange(size, 14, 96));
    }

    [Fact]
    public void FontSizeIsClamped()
    {
        var vm = Create();

        vm.OverlayFontSize = 500;

        Assert.Equal(96, vm.OverlayFontSize);
    }

    [Fact]
    public void RecordingVisibilityIsSpelledOutAsTwoChoices()
    {
        // 瓦基 2026-10-08：平常錄影、分享畫面時看不到浮層（預設）；錄示範給朋友看時要能切成錄得到
        var vm = Create();

        Assert.Equal(2, vm.CaptureOptions.Count);
        Assert.True(vm.CaptureOptions[0].HideFromCapture);
        Assert.Contains("預設", vm.CaptureOptions[0].Label);
        Assert.False(vm.CaptureOptions[1].HideFromCapture);
        Assert.Contains("錄得到", vm.CaptureOptions[1].Label);
        Assert.True(vm.HideFromCapture);
    }

    [Fact]
    public void TheThemeChoiceIsRemembered()
    {
        var first = Create();
        Assert.Equal(AppTheme.System, first.Theme);
        Assert.Equal(["跟著系統", "淺色", "深色"], first.ThemeOptions.Select(o => o.Label));

        first.Theme = AppTheme.Dark;
        first.SaveSettings();

        Assert.Equal(AppTheme.Dark, Create().Theme);
    }

    [Fact]
    public void AMissingLastScriptOrMicrophoneFallsBackQuietly()
    {
        new AppSettings { LastScriptPath = Path.Combine(_dir.FullName, "刪掉了.md"), MicrophoneId = "拔掉的麥克風" }.Save(SettingsPath);

        var vm = Create();

        Assert.Equal("", vm.ScriptText);
        Assert.Equal("Analogue 1 + 2 (Focusrite USB Audio)", vm.SelectedMicrophone?.Info.Name);
        Assert.Empty(_services.Errors);
    }

    // ── 開始／停止 ──

    [Fact]
    public async Task StartRunsTheChosenModeAndLocksTheEditor()
    {
        var vm = Create();
        vm.ScriptText = "我已經讀完了。今天天氣很好。";
        vm.Mode = FollowMode.Classic;
        vm.ScrollSpeed = 2;

        await vm.StartStopAsync();

        var started = Assert.Single(_services.Started);
        Assert.Equal(FollowMode.Classic, started.Mode);
        Assert.Equal(2, started.Speed);
        Assert.Equal("我", started.Prompt.Words[0].Text); // 交出去的是編輯器裡的講稿
        Assert.True(vm.IsRunning);
        Assert.False(vm.IsEditable);
        Assert.Equal("停止", vm.StartStopLabel);
    }

    [Fact]
    public async Task TicksShowProgressAndWhatComesNext()
    {
        var vm = Create();
        vm.ScriptText = "我讀書";
        await vm.StartStopAsync();

        _services.Runner.Progress = 2; // 「讀」的開頭（全文是「我 讀 書」）
        vm.Tick();

        Assert.Equal(100.0 * 1 / 3, vm.ProgressPercent, 1);
        Assert.Equal("讀書", vm.UpcomingText);
    }

    [Fact]
    public async Task StopEndsTheRunAndUnlocksTheEditor()
    {
        var vm = Create();
        vm.ScriptText = "我讀書";
        await vm.StartStopAsync();

        await vm.StartStopAsync();
        vm.Tick();

        Assert.True(_services.Runner.Stopped);
        Assert.False(vm.IsRunning);
        Assert.True(vm.IsEditable);
        Assert.Equal("開始", vm.StartStopLabel);
    }

    [Fact]
    public async Task AFailedStartIsReportedAndNothingIsRunning()
    {
        _services.StartFailure = new FileNotFoundException("找不到模型 A");
        var vm = Create();
        vm.ScriptText = "我讀書";

        await vm.StartStopAsync();

        Assert.Contains("找不到模型 A", Assert.Single(_services.Errors));
        Assert.False(vm.IsRunning);
        Assert.True(vm.IsEditable);
    }

    [Fact]
    public async Task AMicrophoneErrorWhileRunningIsReported()
    {
        var vm = Create();
        vm.ScriptText = "我讀書";
        await vm.StartStopAsync();

        _services.Runner.Error = new InvalidOperationException("麥克風被拔掉了");
        _services.Runner.HasStopped = true;
        vm.Tick();

        Assert.Contains("麥克風被拔掉了", Assert.Single(_services.Errors));
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public async Task ShuttingDownWhileRunningStopsTheRunner()
    {
        // 關視窗時麥克風要關掉，不能留著在背景錄
        var vm = Create();
        vm.ScriptText = "我讀書";
        await vm.StartStopAsync();

        vm.Shutdown();

        Assert.True(_services.Runner.Stopped);
        Assert.True(_services.Runner.Disposed);
    }

    // ── 浮層（第 5.2 步） ──

    [Fact]
    public async Task StartingShowsTheTopCapsuleByDefault()
    {
        var vm = Create();
        vm.ScriptText = "我讀書";

        await vm.StartStopAsync();

        var overlay = Assert.Single(_services.Overlays);
        Assert.Equal(OverlayStyle.Capsule, overlay.Style);
        Assert.Equal(new ScreenRect((1707 - 450) / 2.0, 0, 450, 160), overlay.Bounds); // 預設寬 450（2026-10-08 改，要落在每 50 一格的刻度上）
    }

    [Theory]
    [InlineData(OverlayStyle.Capsule, true, false)]
    [InlineData(OverlayStyle.Floating, false, false)]
    [InlineData(OverlayStyle.FullScreen, false, true)]
    public void OverlayStyleDecidesWhichControlsApply(OverlayStyle style, bool capsuleSize, bool displayChoice)
    {
        var vm = Create();

        vm.OverlayStyle = style;

        Assert.Equal(capsuleSize, vm.IsCapsuleSizeEnabled);
        Assert.Equal(displayChoice, vm.IsDisplayChoiceEnabled);
    }

    [Fact]
    public async Task CapsuleUsesTheChosenSizeClamped()
    {
        var vm = Create();
        vm.ScriptText = "我讀書";
        vm.CapsuleWidth = 999;
        vm.CapsuleHeight = 300;

        await vm.StartStopAsync();

        Assert.Equal(650, vm.CapsuleWidth); // 上限 650（瓦基 2026-10-08 放寬，原本 500）
        Assert.Equal(new ScreenRect((1707 - 650) / 2.0, 0, 650, 300), _services.Overlays[0].Bounds);
    }

    [Fact]
    public async Task FullScreenCoversTheChosenDisplay()
    {
        var vm = Create();
        vm.ScriptText = "我讀書";
        vm.OverlayStyle = OverlayStyle.FullScreen;
        vm.SelectedDisplay = vm.Displays.Single(d => !d.Info.IsPrimary);

        await vm.StartStopAsync();

        Assert.Equal(new ScreenRect(-1920, 0, 1920, 1080), _services.Overlays[0].Bounds);
    }

    [Fact]
    public async Task TicksPushProgressToTheOverlay()
    {
        var vm = Create();
        vm.ScriptText = "我讀書";
        await vm.StartStopAsync();

        _services.Runner.Progress = 2;
        vm.Tick();

        Assert.Equal(2, _services.Overlays[0].LastStatus.Progress);
    }

    [Fact]
    public async Task StoppingClosesTheOverlay()
    {
        var vm = Create();
        vm.ScriptText = "我讀書";
        await vm.StartStopAsync();

        await vm.StartStopAsync();
        vm.Tick();

        Assert.True(_services.Overlays[0].Closed);
    }

    [Fact]
    public async Task EscOnTheOverlayStopsTheRun()
    {
        var vm = Create();
        vm.ScriptText = "我讀書";
        await vm.StartStopAsync();

        _services.Overlays[0].WasClosedByUser = true;
        vm.Tick();
        vm.Tick();

        Assert.True(_services.Runner.Stopped);
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public async Task AMovedFloatingWindowIsRememberedForNextTimeAndNextLaunch()
    {
        var vm = Create();
        vm.ScriptText = "我讀書";
        vm.OverlayStyle = OverlayStyle.Floating;
        await vm.StartStopAsync();
        _services.Overlays[0].Bounds = new ScreenRect(300, 500, 800, 300); // 瓦基拖過去、拉大
        await vm.StartStopAsync();
        vm.Tick();

        await vm.StartStopAsync();
        vm.SaveSettings();
        var nextLaunch = Create();

        Assert.Equal(new ScreenRect(300, 500, 800, 300), _services.Overlays[1].Bounds);
        Assert.Equal(OverlayStyle.Floating, nextLaunch.OverlayStyle);
        Assert.Equal(new ScreenRect(300, 500, 800, 300), AppSettings.Load(SettingsPath).FloatingBounds);
    }

    [Fact]
    public void OverlayChoicesComeBackOnTheNextLaunch()
    {
        var first = Create();
        first.OverlayStyle = OverlayStyle.FullScreen;
        first.CapsuleWidth = 460;
        first.CapsuleHeight = 200;
        first.SelectedDisplay = first.Displays.Single(d => !d.Info.IsPrimary);
        first.SaveSettings();

        var second = Create();

        Assert.Equal(OverlayStyle.FullScreen, second.OverlayStyle);
        Assert.Equal(460, second.CapsuleWidth);
        Assert.Equal(200, second.CapsuleHeight);
        Assert.False(second.SelectedDisplay?.Info.IsPrimary);
    }

    [Fact]
    public async Task ShuttingDownClosesTheOverlay()
    {
        var vm = Create();
        vm.ScriptText = "我讀書";
        await vm.StartStopAsync();

        vm.Shutdown();

        Assert.True(_services.Overlays[0].Closed);
    }

    // ── 主視窗收起來（2026-10-08 瓦基要求，參考 Mac 版） ──

    [Fact]
    public async Task StartingHidesTheMainWindowAndStoppingBringsItBack()
    {
        var vm = Create();
        vm.ScriptText = "我讀書";
        Assert.False(vm.IsMainWindowHidden);

        await vm.StartStopAsync();
        Assert.True(vm.IsMainWindowHidden);

        await vm.StartStopAsync();
        vm.Tick();
        Assert.False(vm.IsMainWindowHidden);
    }

    [Fact]
    public async Task AFailedStartKeepsTheMainWindow()
    {
        _services.StartFailure = new FileNotFoundException("找不到模型 A");
        var vm = Create();
        vm.ScriptText = "我讀書";

        await vm.StartStopAsync();

        Assert.False(vm.IsMainWindowHidden);
    }

    [Fact]
    public async Task ErrorsAreShownAfterTheMainWindowIsBack()
    {
        // 錯誤訊息框掛在主視窗上；主視窗還收著的話訊息框可能出不來
        var vm = Create();
        vm.ScriptText = "我讀書";
        await vm.StartStopAsync();
        // 視窗是收到 PropertyChanged(IsMainWindowHidden) 才真的叫回來，所以要看「這個通知在訊息框之前發了沒」
        var windowBack = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.IsMainWindowHidden) && !vm.IsMainWindowHidden)
            {
                windowBack = true;
            }
        };
        var hiddenWhenErrorShown = new List<bool>();
        _services.OnError = () => hiddenWhenErrorShown.Add(!windowBack);

        _services.Runner.Error = new InvalidOperationException("麥克風被拔掉了");
        _services.Runner.HasStopped = true;
        vm.Tick();

        Assert.Equal([false], hiddenWhenErrorShown);
    }

    // ── 浮層的資訊、讀完、膠囊大小（2026-10-08 瓦基要求的 UI 調整） ──

    [Fact]
    public async Task TheOverlayGetsTheModeAndLiveStatus()
    {
        var vm = Create();
        vm.ScriptText = "我讀書";
        vm.Mode = FollowMode.VoiceActivated;
        await vm.StartStopAsync();

        _services.Runner.AudioLevel = 0.12f;
        _services.Runner.IsSpeaking = true;
        vm.Tick();

        var overlay = _services.Overlays[0];
        Assert.Equal(FollowMode.VoiceActivated, overlay.Mode);
        Assert.Equal(0.12f, overlay.LastStatus.AudioLevel);
        Assert.True(overlay.LastStatus.IsSpeaking);
        Assert.True(overlay.LastStatus.Elapsed >= TimeSpan.Zero);
    }

    // 瓦基 2026-10-08：讀完停在「讀完了」，由他自己選重來或結束，不要一秒後自動跳出（原本是一秒後自己收）

    private async Task<FakeOverlay> ReadToTheEnd(MainViewModel vm, string script)
    {
        vm.ScriptText = script;
        await vm.StartStopAsync();
        _services.Runner.Progress = new PromptScript(script).CharacterCount;
        _services.Runner.HasStopped = true; // 讀到結尾、跟讀自己停了
        vm.Tick();
        return _services.Overlays[^1];
    }

    [Fact]
    public async Task ReadingToTheEndStaysOnTheDoneScreen()
    {
        var vm = Create();
        var overlay = await ReadToTheEnd(vm, "我讀書");

        for (var i = 0; i < 200; i++)
        {
            vm.Tick(); // 10 秒
        }

        Assert.True(overlay.DoneShown);
        Assert.False(overlay.Closed);
        Assert.True(vm.IsMainWindowHidden);
        Assert.Equal("讀完了。", vm.StatusText);
    }

    [Fact]
    public async Task OnTheDoneScreenCloseOrEscBringsTheEditorBack()
    {
        var vm = Create();
        var overlay = await ReadToTheEnd(vm, "我讀書");

        overlay.WasClosedByUser = true; // ✕ 或 Esc
        vm.Tick();

        Assert.True(overlay.Closed);
        Assert.False(vm.IsMainWindowHidden);
    }

    [Fact]
    public async Task OnTheDoneScreenThePauseKeyRestartsFromTheTop()
    {
        // 瓦基 2026-10-08：讀完時再按 Ctrl+Alt+L 就是重來
        var vm = Create();
        await ReadToTheEnd(vm, TwoSentences);
        var hiddenThroughout = true;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsMainWindowHidden))
            {
                hiddenThroughout &= vm.IsMainWindowHidden;
            }
        };

        await vm.TogglePauseAsync();

        Assert.Equal(2, _services.Started.Count);
        Assert.Equal(0, _services.Started[1].StartAt);
        Assert.True(vm.IsRunning);
        Assert.True(hiddenThroughout, "重來時編輯器不要先跳出來又收回去");
    }

    // ── Esc（瓦基 2026-10-08：有時候按 Esc 沒反應）：浮層開著時 Esc 是全域快捷鍵，不靠浮層有沒有焦點 ──

    [Fact]
    public async Task TheOverlayIsOpenFromStartUntilItCloses()
    {
        var vm = Create();
        Assert.False(vm.IsOverlayOpen);

        var overlay = await ReadToTheEnd(vm, "我讀書");
        Assert.True(vm.IsOverlayOpen); // 「讀完了」畫面也算

        overlay.WasClosedByUser = true;
        vm.Tick();
        Assert.False(vm.IsOverlayOpen);
    }

    [Fact]
    public async Task EscWhileReadingStops()
    {
        var vm = Create();
        vm.ScriptText = TwoSentences;
        await vm.StartStopAsync();
        _services.Runner.Progress = CharacterAt(TwoSentences, '今');

        vm.EscapePressed();
        vm.Tick();

        Assert.True(_services.Runner.Stopped);
        Assert.False(vm.IsRunning);
        Assert.False(vm.IsMainWindowHidden);
        Assert.Equal(TwoSentences.IndexOf('今'), vm.CaretIndex); // 一樣記住停在哪
    }

    [Fact]
    public async Task EscWhilePausedStopsToo()
    {
        var vm = Create();
        vm.ScriptText = TwoSentences;
        await vm.StartStopAsync();
        await vm.TogglePauseAsync();

        vm.EscapePressed();
        vm.Tick();

        Assert.False(vm.IsRunning);
    }

    [Fact]
    public async Task EscOnTheDoneScreenBringsTheEditorBack()
    {
        var vm = Create();
        var overlay = await ReadToTheEnd(vm, "我讀書");

        vm.EscapePressed();

        Assert.True(overlay.Closed);
        Assert.False(vm.IsMainWindowHidden);
    }

    [Fact]
    public async Task OnTheDoneScreenRestartReadsAgainFromTheTop()
    {
        var vm = Create();
        var overlay = await ReadToTheEnd(vm, TwoSentences);

        overlay.RestartRequested = true;
        vm.Tick();
        await Task.Yield();

        Assert.True(overlay.Closed);
        Assert.Equal(2, _services.Started.Count);
        Assert.Equal(0, _services.Started[1].StartAt);
        Assert.True(vm.IsRunning);
        Assert.True(vm.IsMainWindowHidden);
    }

    [Fact]
    public async Task StoppingMidwayDoesNotShowDone()
    {
        var vm = Create();
        vm.ScriptText = "我讀書";
        await vm.StartStopAsync();

        await vm.StartStopAsync();
        vm.Tick();

        Assert.False(_services.Overlays[0].DoneShown);
        Assert.True(_services.Overlays[0].Closed);
    }

    // ── 從游標處開始、記住中斷點（瓦基 2026-10-08：講到一半失敗，回編輯器調一調，從游標的地方接著講） ──

    private const string TwoSentences = "我已經讀完了。今天天氣很好。";

    private static int CharacterAt(string script, char c) =>
        new PromptScript(script).Words.First(w => w.Text == c.ToString()).CharacterRange.Start;

    [Fact]
    public async Task StartBeginsAtTheCaret()
    {
        var vm = Create();
        vm.ScriptText = TwoSentences;
        vm.CaretIndex = TwoSentences.IndexOf('今');

        await vm.StartStopAsync();

        Assert.Equal(CharacterAt(TwoSentences, '今'), _services.Started[0].StartAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(14)] // 最後面（剛貼上講稿時游標通常在這裡）
    public async Task ACaretAtTheVeryBeginningOrEndStartsFromTheTop(int caret)
    {
        var vm = Create();
        vm.ScriptText = TwoSentences + "\n";
        vm.CaretIndex = caret == 14 ? vm.ScriptText.Length : caret;

        await vm.StartStopAsync();

        Assert.Equal(0, _services.Started[0].StartAt);
    }

    [Fact]
    public async Task StoppingMidwayLeavesTheCaretWhereReadingStopped()
    {
        var vm = Create();
        vm.ScriptText = TwoSentences;
        await vm.StartStopAsync();
        _services.Runner.Progress = CharacterAt(TwoSentences, '今');

        await vm.StartStopAsync();
        vm.Tick();

        Assert.Equal(TwoSentences.IndexOf('今'), vm.CaretIndex);
        await vm.StartStopAsync(); // 再按開始：從剛剛停下的地方接著念
        Assert.Equal(CharacterAt(TwoSentences, '今'), _services.Started[1].StartAt);
    }

    [Fact]
    public async Task ReadingToTheEndMeansTheNextStartIsFromTheTop()
    {
        var vm = Create();
        vm.ScriptText = TwoSentences;
        vm.CaretIndex = TwoSentences.IndexOf('今');
        await vm.StartStopAsync();
        _services.Runner.Progress = new PromptScript(TwoSentences).CharacterCount;
        _services.Runner.HasStopped = true;
        for (var i = 0; i < 25; i++)
        {
            vm.Tick();
        }

        await vm.StartStopAsync();

        Assert.Equal(0, _services.Started[1].StartAt);
    }

    [Fact]
    public void TheStartPointIsSpelledOut()
    {
        var vm = Create();
        vm.ScriptText = TwoSentences;
        Assert.Contains("從頭", vm.StartPointText);
        Assert.False(vm.StartFromTopCommand.CanExecute(null));

        vm.CaretIndex = TwoSentences.IndexOf('今');

        Assert.Contains("游標", vm.StartPointText);
        Assert.Contains("今天天氣很好", vm.StartPointText); // 讓瓦基確認是不是要從這裡開始
        Assert.Contains("50%", vm.StartPointText); // 前面 6 個字、全部 12 個字
        Assert.True(vm.StartFromTopCommand.CanExecute(null));
    }

    [Fact]
    public void WhenIdleTheProgressBarShowsWhereTheNextStartIs()
    {
        // 瓦基 2026-10-08 選 A：沒在念的時候，進度條＝下次開始的位置，跟「從游標處開始（約 N%）」同一個數字
        var vm = Create();
        vm.ScriptText = TwoSentences;
        Assert.Equal(0, vm.ProgressPercent);

        vm.CaretIndex = TwoSentences.IndexOf('今');

        Assert.Equal(50, vm.ProgressPercent, 1);
    }

    [Fact]
    public async Task AfterStoppingMidwayTheBarStaysAtTheStopAndTheStatusSaysStopped()
    {
        var vm = Create();
        vm.ScriptText = TwoSentences;
        await vm.StartStopAsync();
        _services.Runner.Progress = CharacterAt(TwoSentences, '今');

        await vm.StartStopAsync();
        vm.Tick();

        Assert.Equal(50, vm.ProgressPercent, 1);
        Assert.Equal("停止了。", vm.StatusText);
    }

    [Fact]
    public void StartFromTopMovesTheCaretBack()
    {
        var vm = Create();
        vm.ScriptText = TwoSentences;
        vm.CaretIndex = TwoSentences.IndexOf('今');

        vm.StartFromTopCommand.Execute(null);

        Assert.Equal(0, vm.CaretIndex);
        Assert.Contains("從頭", vm.StartPointText);
    }

    // ── 第 5.4 步：快捷鍵（Ctrl+Alt+L 開始／暫停、Ctrl+Alt+↑／↓ 跳一句；原訂 Space 與 ←→，瓦基 2026-10-08 改）、點字跳轉、滾輪追趕 ──

    [Fact]
    public async Task ThePauseKeyStartsWhenNothingIsRunning()
    {
        var vm = Create();
        vm.ScriptText = TwoSentences;

        await vm.TogglePauseAsync();

        Assert.Single(_services.Started);
        Assert.True(vm.IsRunning);
    }

    [Fact]
    public async Task ThePauseKeyPausesAndResumesWithoutClosingTheOverlay()
    {
        var vm = Create();
        vm.ScriptText = TwoSentences;
        await vm.StartStopAsync();
        var overlay = _services.Overlays[0];

        await vm.TogglePauseAsync();
        vm.Tick();
        Assert.True(_services.Runner.IsPaused);
        Assert.True(overlay.LastStatus.IsPaused);
        Assert.False(overlay.Closed);
        Assert.True(vm.IsRunning);

        Assert.Contains("Ctrl+Alt+L", vm.StatusText); // 瓦基 2026-10-08 改鍵：Space 在這台被別的程式佔走

        await vm.TogglePauseAsync();
        vm.Tick();
        Assert.False(_services.Runner.IsPaused);
        Assert.False(overlay.LastStatus.IsPaused);
    }

    [Fact]
    public async Task WhatWasJustHeardGoesToTheOverlay()
    {
        // 瓦基 2026-10-08：浮層下方即時顯示講了什麼
        var vm = Create();
        vm.ScriptText = TwoSentences;
        await vm.StartStopAsync();

        _services.Runner.Heard = "我已经读完";
        vm.Tick();

        Assert.Equal("我已经读完", _services.Overlays[0].LastStatus.Heard);
    }

    [Fact]
    public async Task NextAndPreviousSentenceJump()
    {
        var vm = Create();
        vm.ScriptText = TwoSentences;
        await vm.StartStopAsync();

        vm.JumpSentence(+1);
        Assert.Equal(CharacterAt(TwoSentences, '今'), _services.Runner.Progress);

        vm.JumpSentence(-1);
        Assert.Equal(0, _services.Runner.Progress);
    }

    [Fact]
    public async Task ClickingOrScrollingOnTheOverlayJumpsThere()
    {
        var vm = Create();
        vm.ScriptText = TwoSentences;
        await vm.StartStopAsync();
        var overlay = _services.Overlays[0];

        overlay.PendingJump = CharacterAt(TwoSentences, '天');
        vm.Tick();

        Assert.Equal(CharacterAt(TwoSentences, '天'), _services.Runner.Progress);
        Assert.Equal(CharacterAt(TwoSentences, '天'), overlay.LastStatus.Progress);
        Assert.Null(overlay.PendingJump); // 拿走了，下一次 Tick 不會再跳一次
    }

    [Fact]
    public void HotkeysTakenByAnotherProgramAreMentionedQuietly()
    {
        var vm = Create();

        vm.NoteHotkeysTaken(["Ctrl+Alt+L"]);

        Assert.Contains("Ctrl+Alt+L", vm.StatusText);
        Assert.Empty(_services.Errors); // 不跳訊息框，主視窗按鈕照樣能用
    }

    [Fact]
    public void WhenAllHotkeysAreFreeTheStatusStaysAsItWas()
    {
        var vm = Create();
        var before = vm.StatusText;

        vm.NoteHotkeysTaken([]);

        Assert.Equal(before, vm.StatusText);
    }

    [Fact]
    public void JumpingWhenNothingIsRunningDoesNothing()
    {
        var vm = Create();
        vm.ScriptText = TwoSentences;

        vm.JumpSentence(+1); // 不出錯就好

        Assert.Empty(_services.Started);
    }

    [Fact]
    public async Task AResizedCapsuleKeepsItsNewSize()
    {
        var vm = Create();
        vm.ScriptText = "我讀書";
        await vm.StartStopAsync();
        _services.Overlays[0].Bounds = new ScreenRect(600, 0, 480, 240); // 瓦基拖邊框把膠囊拉大

        await vm.StartStopAsync();
        vm.Tick();

        Assert.Equal(480, vm.CapsuleWidth);
        Assert.Equal(240, vm.CapsuleHeight);
    }

    // ── 假的外部服務 ──

    private sealed class FakeServices : IAppServices
    {
        public string? NextOpenPath { get; set; }
        public string? NextSavePath { get; set; }
        public bool? SaveChangesAnswer { get; set; } = false;
        public Exception? StartFailure { get; set; }
        public List<string> AskedToSave { get; } = [];
        public List<string> Errors { get; } = [];
        public List<(PromptScript Prompt, FollowMode Mode, double Speed, MicrophoneInfo? Microphone, int StartAt)> Started { get; } = [];
        public FakeRunner Runner { get; private set; } = new();
        public List<FakeOverlay> Overlays { get; } = [];

        public IReadOnlyList<DisplayInfo> ListDisplays() =>
        [
            new(@"\\.\DISPLAY2", new(-1920, 0, 1920, 1080), new(-1920, 0, 1920, 1040), IsPrimary: false),
            new(@"\\.\DISPLAY1", new(0, 0, 1707, 960), new(0, 0, 1707, 928), IsPrimary: true),
        ];

        public IOverlay ShowOverlay(OverlayStyle style, ScreenRect bounds, PromptScript prompt, FollowMode mode, bool hideFromCapture, double fontSize)
        {
            var overlay = new FakeOverlay(style, bounds, mode) { HideFromCapture = hideFromCapture, FontSize = fontSize };
            Overlays.Add(overlay);
            return overlay;
        }

        public IReadOnlyList<MicrophoneInfo> ListMicrophones() =>
        [
            new(0, "{voice-changer}", "Voice Changer Virtual Audio Device", false, "48000 Hz、2 聲道"),
            new(1, "{focusrite}", "Analogue 1 + 2 (Focusrite USB Audio)", true, "48000 Hz、2 聲道"),
        ];

        public string? AskOpenPath() => NextOpenPath;

        public string? AskSavePath(string suggestedName) => NextSavePath;

        public bool? AskSaveChanges(string displayName)
        {
            AskedToSave.Add(displayName);
            return SaveChangesAnswer;
        }

        public Action? OnError { get; set; }

        public void ShowError(string message)
        {
            Errors.Add(message);
            OnError?.Invoke();
        }

        public Task<IFollowRunner> StartFollowAsync(PromptScript prompt, FollowMode mode, double scrollSpeed, MicrophoneInfo? microphone, int startAt)
        {
            if (StartFailure is not null)
            {
                return Task.FromException<IFollowRunner>(StartFailure);
            }
            Started.Add((prompt, mode, scrollSpeed, microphone, startAt));
            Runner = new FakeRunner { Progress = startAt };
            return Task.FromResult<IFollowRunner>(Runner);
        }
    }

    private sealed class FakeOverlay(OverlayStyle style, ScreenRect bounds, FollowMode mode) : IOverlay
    {
        public OverlayStyle Style { get; } = style;
        public FollowMode Mode { get; } = mode;
        public ScreenRect Bounds { get; set; } = bounds;
        public bool WasClosedByUser { get; set; }
        public OverlayStatus LastStatus { get; private set; } = new(-1, TimeSpan.Zero, 0, false);
        public bool DoneShown { get; private set; }
        public bool Closed { get; private set; }
        public int? PendingJump { get; set; }
        public bool RestartRequested { get; set; }
        public bool HideFromCapture { get; init; }
        public double FontSize { get; init; }

        public int? TakeJumpRequest()
        {
            var jump = PendingJump;
            PendingJump = null;
            return jump;
        }

        public void Update(OverlayStatus status) => LastStatus = status;

        public void ShowDone() => DoneShown = true;

        public void Close() => Closed = true;
    }

    private sealed class FakeRunner : IFollowRunner
    {
        public int Progress { get; set; }
        public bool IsSpeaking { get; set; }
        public float AudioLevel { get; set; }
        public bool HasStopped { get; set; }
        public Exception? Error { get; set; }
        public bool Stopped { get; private set; }
        public bool Disposed { get; private set; }
        public bool IsPaused { get; private set; }
        public string Heard { get; set; } = "";

        public void Tick()
        {
        }

        public void Pause() => IsPaused = true;

        public void Resume() => IsPaused = false;

        public void JumpTo(int characterOffset) => Progress = characterOffset;

        public void Stop()
        {
            Stopped = true;
            HasStopped = true;
        }

        public void Dispose() => Disposed = true;
    }
}
