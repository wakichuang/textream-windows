using TextreamWindows.Core.Session;
using TextreamWindows.Core.Text;

namespace TextreamWindows.Core.Tests.Session;

public class PromptSessionTests
{
    /// <summary>照 WPF 計時器的節奏，每 50 毫秒 Tick 一次，從 <paramref name="from"/> 到 <paramref name="to"/>（含）。</summary>
    private static void TickEvery50ms(PromptSession session, double from, double to)
    {
        for (var t = from; t <= to + 1e-9; t += 0.05)
        {
            session.Tick(t);
        }
    }

    // ── 移植自 Textream PromptCoreTests.swift（commit 2c02f3e） ──

    [Fact]
    public void ClassicSessionTimerAdvancesFractionalWordProgress()
    {
        var session = new PromptSession(new PromptScript("One two three"), FollowMode.Classic, scrollSpeed: 3);
        session.Start(0);
        var startingProgress = session.ContinuousWordProgress ?? -1;

        TickEvery50ms(session, 0.05, 0.25);

        Assert.True(session.IsRunning);
        Assert.True((session.ContinuousWordProgress ?? -1) > startingProgress + 0.2, $"progress = {session.ContinuousWordProgress}");
    }

    [Fact]
    public void VoiceFollowerEntersSpeakingStateFromMicrophoneLevels()
    {
        var session = new PromptSession(new PromptScript("one two three"), FollowMode.VoiceActivated);
        session.Start(0);

        session.ProcessAudioLevel(0.05f, 0);

        Assert.True(session.IsListening);
        Assert.True(session.IsSpeaking);
    }

    [Fact]
    public void SessionSettingsClampLiveScrollSpeedToTheSupportedRange()
    {
        var session = new PromptSession(new PromptScript("One two three"), FollowMode.Classic, scrollSpeed: 3);

        session.SetScrollSpeed(99);
        Assert.Equal(ScrollSpeed.Maximum, session.ScrollSpeed);

        session.SetScrollSpeed(-1);
        Assert.Equal(ScrollSpeed.Minimum, session.ScrollSpeed);
    }

    // ── 定速捲動 ──

    [Fact]
    public void TheDefaultScrollSpeedIsFourCharactersPerSecond()
    {
        // 瓦基 2026-10-08：每秒四個字比較符合一般使用情境（原版預設 3）
        Assert.Equal(4.0, ScrollSpeed.Default);
        Assert.Equal(4.0, new PromptSession(new PromptScript("一二三"), FollowMode.Classic).ScrollSpeed);
    }

    [Fact]
    public void InitialScrollSpeedIsClampedToo()
    {
        Assert.Equal(ScrollSpeed.Maximum, new PromptSession(new PromptScript("一二三"), FollowMode.Classic, scrollSpeed: 50).ScrollSpeed);
        Assert.Equal(ScrollSpeed.Default, new PromptSession(new PromptScript("一二三"), FollowMode.Classic).ScrollSpeed);
    }

    [Fact]
    public void ClassicTimerStopsAtTheEndOfTheScript()
    {
        var session = new PromptSession(new PromptScript("我讀書"), FollowMode.Classic, scrollSpeed: 3);
        session.Start(0);

        TickEvery50ms(session, 0.05, 2);

        Assert.Equal(3.0, session.ContinuousWordProgress!.Value, 3);
        Assert.True(session.IsFinished);
        Assert.Equal(session.Prompt.CharacterCount, session.EffectiveCharacterCount);
    }

    [Fact]
    public void PauseStopsTheTimerAndResumingDoesNotCatchUp()
    {
        var session = new PromptSession(new PromptScript("今天想跟大家聊聊閱讀這件事情"), FollowMode.Classic, scrollSpeed: 2);
        session.Start(0);
        TickEvery50ms(session, 0.05, 1);
        var beforePause = session.TimerWordProgress;

        session.Pause();
        TickEvery50ms(session, 1.05, 5);
        Assert.False(session.IsRunning);
        Assert.Equal(beforePause, session.TimerWordProgress);

        session.Start(5);
        session.Tick(5.05);
        Assert.Equal(beforePause + 2 * 0.05, session.TimerWordProgress, 6);
    }

    [Fact]
    public void ALongGapBetweenTicksCountsAsAtMostAQuarterSecond()
    {
        // 電腦卡頓、計時器延遲時不要一次暴衝：原版一次最多算 0.25 秒
        var session = new PromptSession(new PromptScript("今天想跟大家聊聊閱讀這件事情"), FollowMode.Classic, scrollSpeed: 2);
        session.Start(0);

        session.Tick(10);

        Assert.Equal(2 * 0.25, session.TimerWordProgress, 6);
    }

    [Fact]
    public void JumpInClassicModeMovesTheTimer()
    {
        var session = new PromptSession(new PromptScript("我讀書"), FollowMode.Classic);

        session.JumpTo(4, 0); // 「書」

        Assert.Equal(2.0, session.TimerWordProgress, 6);
        Assert.Equal(4, session.EffectiveCharacterCount);
    }

    [Fact]
    public void StartingAFinishedSessionDoesNothing()
    {
        var session = new PromptSession(new PromptScript("我讀書"), FollowMode.Classic, scrollSpeed: 8);
        session.Start(0);
        TickEvery50ms(session, 0.05, 2);
        session.Pause();

        session.Start(3);

        Assert.True(session.IsFinished);
        Assert.False(session.IsRunning);
    }

    // ── 有講話才捲 ──

    [Fact]
    public void VoiceActivatedAdvancesOnlyWhileSpeaking()
    {
        var session = new PromptSession(new PromptScript("今天想跟大家聊聊閱讀這件事情，我們從一本書開始"), FollowMode.VoiceActivated, scrollSpeed: 2);
        session.Start(0);

        TickEvery50ms(session, 0.05, 1); // 還沒開口
        Assert.Equal(0, session.TimerWordProgress);

        session.ProcessAudioLevel(0.12f, 1); // 開口：之後 0.75 秒都算在講話
        TickEvery50ms(session, 1.05, 3);

        Assert.False(session.IsSpeaking);
        Assert.InRange(session.TimerWordProgress, 2 * 0.7, 2 * 0.8); // 只走了 0.75 秒左右
    }

    [Fact]
    public void ClassicModeDoesNotListen()
    {
        var session = new PromptSession(new PromptScript("我讀書"), FollowMode.Classic);
        session.Start(0);

        Assert.True(session.IsRunning);
        Assert.False(session.IsListening);
    }

    // ── 逐字追蹤 ──

    [Fact]
    public void WordTrackingFollowsTranscriptsOnlyWhileRunning()
    {
        var prompt = new PromptScript("我已經讀完了。今天天氣很好。");
        var session = new PromptSession(prompt, FollowMode.WordTracking);

        session.ProcessTranscript("我已经读完了", 0); // 還沒開始，不理
        Assert.Equal(0, session.EffectiveCharacterCount);

        session.Start(0);
        Assert.True(session.IsListening);
        Assert.Null(session.ContinuousWordProgress);
        session.ProcessTranscript("我已经读完了", 0.5);
        Assert.True(session.EffectiveCharacterCount > 0);

        session.EndOfUtterance();
        session.ProcessTranscript("今天天气很好", 2);
        Assert.Equal(prompt.CharacterCount, session.EffectiveCharacterCount);
        Assert.True(session.IsFinished);
    }

    /// <summary>
    /// 辨識語言 English（瓦基 2026-10-08）要一路傳到比對：講稿結尾「from the earth」辨識成「FROM THIS EARTH」，
    /// English 規則讀得完，繁體中文規則停在最後一個詞前面（EN-02）。
    /// </summary>
    [Theory]
    [InlineData(SpeechLanguage.English, true)]
    [InlineData(SpeechLanguage.TraditionalChinese, false)]
    public void WordTrackingUsesTheChosenSpeechLanguage(SpeechLanguage language, bool finishes)
    {
        var session = new PromptSession(new PromptScript("shall not perish from the earth."), FollowMode.WordTracking, language: language);
        session.Start(0);

        session.ProcessTranscript("SHALL NOT PERISH FROM THIS EARTH", 1);

        Assert.Equal(language, session.Language);
        Assert.Equal(finishes, session.IsFinished);
    }

    [Fact]
    public void PausedWordTrackingIgnoresTranscripts()
    {
        var session = new PromptSession(new PromptScript("我已經讀完了。今天天氣很好。"), FollowMode.WordTracking);
        session.Start(0);
        session.Pause();

        session.ProcessTranscript("我已经读完了", 0.5);

        Assert.Equal(0, session.EffectiveCharacterCount);
        Assert.False(session.IsListening);
    }

    /// <summary>
    /// 點回前面想重念：同一句辨識結果裡還留著剛念過的字，不剪掉的話會立刻又對上、把高亮拉回原處。
    /// （上一條往後跳的情境，沒有剪也碰巧過得了；2026-10-08 突變測試發現後補這條。）
    /// </summary>
    [Fact]
    public void JumpBackToRereadIsNotUndoneByWordsAlreadySpoken()
    {
        var prompt = new PromptScript("第一句話在這裡。第二句話在那裡。");
        var session = new PromptSession(prompt, FollowMode.WordTracking);
        session.Start(0);
        session.ProcessTranscript("第一句话在这里", 0.5);
        Assert.True(session.EffectiveCharacterCount > 0);

        session.JumpTo(0, 1.0);
        session.ProcessTranscript("第一句话在这里嗯", 2.5); // 就定位時間（1 秒）過後，同一句還沒結束，只多了一個「嗯」

        Assert.Equal(0, session.EffectiveCharacterCount);
    }

    /// <summary>
    /// 暫停後繼續時，新的音訊來源時鐘從 0 重新算（第 4.1 步 FollowPipeline）。
    /// 舊時鐘第 50 秒的跳轉，不能讓新時鐘的辨識結果全部落在「跳轉後 1 秒內」而被永遠忽略。
    /// </summary>
    [Fact]
    public void StartingAgainBeginsANewClock()
    {
        var prompt = new PromptScript("第一句話在這裡。第二句話在那裡。");
        var session = new PromptSession(prompt, FollowMode.WordTracking);
        session.Start(0);
        session.JumpTo(0, 50);
        session.Pause();

        session.Start(0);
        session.ProcessTranscript("第一句话在这里", 0.5);

        Assert.True(session.EffectiveCharacterCount > 0);
    }

    [Fact]
    public void JumpInWordTrackingIgnoresTheRestOfTheOldUtterance()
    {
        // 原版 SpeechFollower.jump：跳轉後 0.3 秒內的辨識結果不理；同一句裡跳轉前已經講過的字要剪掉，從跳轉點重新比。
        // 2026-10-10 起就定位時間是 1 秒，期間的結果當成已經講過（Mac 繁中版 1.7.1.4），所以第二筆移到 2.5 秒
        var prompt = new PromptScript("第一句話在這裡。中間有一段不念。第二句話在那裡。");
        var session = new PromptSession(prompt, FollowMode.WordTracking);
        session.Start(0);
        session.ProcessTranscript("第一句话", 0.5);
        var words = prompt.Words;
        var second = Enumerable.Range(0, words.Count - 1).First(i => words[i].Text == "第" && words[i + 1].Text == "二");
        var target = words[second].CharacterRange.Start; // 全文中文字之間有空格，不能用 Text.IndexOf("第二")

        session.JumpTo(target, 1.0);
        Assert.Equal(target, session.EffectiveCharacterCount);

        session.ProcessTranscript("第一句话在这里中间有一段不念第二句话在那里", 1.1); // 就定位時間內：不比對
        Assert.Equal(target, session.EffectiveCharacterCount);

        session.ProcessTranscript("第一句话第二句话在那里", 2.5); // 剪掉跳轉前講過的「第一句话」，剩下的從跳轉點比
        Assert.Equal(prompt.CharacterCount, session.EffectiveCharacterCount);
    }

    /// <summary>
    /// 從中間開始（瓦基 2026-10-08：上次講到一半失敗，在編輯器把游標放在想重來的地方再按開始）：
    /// 開始前先跳到那裡，開始後就從那裡往後追。
    /// </summary>
    [Theory]
    [InlineData(FollowMode.WordTracking)]
    [InlineData(FollowMode.Classic)]
    [InlineData(FollowMode.VoiceActivated)]
    public void JumpingBeforeStartStartsFromThere(FollowMode mode)
    {
        var prompt = new PromptScript("第一句話在這裡。第二句話在那裡。");
        var session = new PromptSession(prompt, mode);
        var second = prompt.Words.First(w => w.Text == "第" && w.Id > 0).CharacterRange.Start;

        session.JumpTo(second, 0);
        session.Start(0);

        Assert.True(session.IsRunning);
        Assert.Equal(second, session.EffectiveCharacterCount);
    }

    /// <summary>
    /// Ctrl+Alt+L 暫停後繼續（計畫書第 5 階段步驟 4）：麥克風與辨識沒停，暫停期間講的話（跟旁邊的人說話）
    /// 還留在同一句的辨識結果裡；繼續之後要把它們當成舊的剪掉，不能把高亮拖走。
    /// </summary>
    [Fact]
    public void WordsHeardWhilePausedDoNotMoveTheHighlightAfterResuming()
    {
        var prompt = new PromptScript("第一句話在這裡。第二句話在那裡。");
        var session = new PromptSession(prompt, FollowMode.WordTracking);
        session.Start(0);
        session.ProcessTranscript("第一句话", 0.5);
        var before = session.EffectiveCharacterCount;

        session.Pause();
        session.ProcessTranscript("第一句话在这里", 1.0); // 暫停中講的
        session.Resume(2.0);
        session.ProcessTranscript("第一句话在这里", 2.5); // 同一句辨識結果，沒有新字

        Assert.True(session.IsRunning);
        Assert.Equal(before, session.EffectiveCharacterCount);

        session.ProcessTranscript("第一句话在这里在这里", 3.0); // 繼續之後真的念了「在這裡」
        Assert.True(session.EffectiveCharacterCount > before);
    }

    [Fact]
    public void ResumingTheTimerModesContinuesFromWhereTheyPaused()
    {
        var session = new PromptSession(new PromptScript("今天想跟大家聊聊閱讀這件事情"), FollowMode.Classic, scrollSpeed: 2);
        session.Start(0);
        TickEvery50ms(session, 0.05, 1.0);
        session.Pause();
        var paused = session.TimerWordProgress;
        TickEvery50ms(session, 1.05, 3.0); // 暫停中：不動

        session.Resume(3.0);
        TickEvery50ms(session, 3.05, 3.5);

        Assert.Equal(paused + 2 * 0.5, session.TimerWordProgress, 1);
    }

    [Fact]
    public void WordTrackingStartedMidwayFollowsFromThere()
    {
        var prompt = new PromptScript("第一句話在這裡。第二句話在那裡。");
        var session = new PromptSession(prompt, FollowMode.WordTracking);
        var second = prompt.Words.First(w => w.Text == "第" && w.Id > 0).CharacterRange.Start;
        session.JumpTo(second, 0);
        session.Start(0);

        session.ProcessTranscript("第二句话在那里", 0.5);

        Assert.Equal(prompt.CharacterCount, session.EffectiveCharacterCount);
    }
}
