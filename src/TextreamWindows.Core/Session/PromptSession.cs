using TextreamWindows.Core.Alignment;
using TextreamWindows.Core.Audio;
using TextreamWindows.Core.Chinese;
using TextreamWindows.Core.Text;

namespace TextreamWindows.Core.Session;

/// <summary>
/// 一次提詞的狀態機。移植 Textream iOS 版 <c>PromptSessionController</c> 與 <c>SpeechFollower</c> 跟平台無關的部分：
/// 逐字追蹤交給 <see cref="PromptMatcher"/>；定速捲動與有講話才捲用「每秒幾個詞」的計時器。
/// </summary>
/// <remarks>
/// 不碰計時器、麥克風、辨識引擎：時間（秒，單調遞增）由呼叫端傳入。
/// 第 4 階段的語音管線呼叫 <see cref="ProcessAudioLevel"/>、<see cref="ProcessTranscript"/>、<see cref="EndOfUtterance"/>；
/// 第 5 階段的 WPF 計時器每 50 毫秒呼叫 <see cref="Tick"/>。
/// </remarks>
public sealed class PromptSession
{
    /// <summary>計時器兩次 Tick 之間最多算幾秒：電腦卡頓時不要一次暴衝。</summary>
    private const double MaxTickSeconds = 0.25;

    /// <summary>跳轉後幾秒內的辨識結果不理：那是跳轉前就講出口、引擎還沒吐完的字。</summary>
    private const double IgnoreTranscriptsAfterJumpSeconds = 0.3;

    /// <summary>剪掉跳轉前講過的字時，最多保留多少舊字（原版的 24）：辨識引擎可能改寫了前面的字，共同前綴算短了也不至於留太多。</summary>
    private const int AnchorPrefixAllowance = 24;

    private readonly PromptMatcher _matcher;
    private readonly VoiceActivityDetector _voiceActivity = new();
    private double? _lastTickTime;
    private double _lastJumpAt = double.NegativeInfinity;
    private string _lastSpokenText = "";
    private string _spokenAnchorPrefix = "";

    public PromptSession(PromptScript prompt, FollowMode mode, double scrollSpeed = Session.ScrollSpeed.Default, PinyinTable? pinyin = null)
    {
        Prompt = prompt;
        Mode = mode;
        ScrollSpeed = Session.ScrollSpeed.Clamp(scrollSpeed);
        _matcher = new PromptMatcher(prompt, pinyin: pinyin);
    }

    public PromptScript Prompt { get; }

    public FollowMode Mode { get; }

    /// <summary>每秒幾個詞（中文一字一詞），夾在 <see cref="Session.ScrollSpeed.Minimum"/>～<see cref="Session.ScrollSpeed.Maximum"/>。</summary>
    public double ScrollSpeed { get; private set; }

    public bool IsRunning { get; private set; }

    /// <summary>有沒有在聽麥克風（逐字追蹤、有講話才捲在跑的時候）。</summary>
    public bool IsListening { get; private set; }

    /// <summary>語音活動偵測認為正在講話。</summary>
    public bool IsSpeaking { get; private set; }

    /// <summary>定速捲動、有講話才捲讀到第幾個詞（有小數，畫面平滑捲動用）。</summary>
    public double TimerWordProgress { get; private set; }

    /// <summary>高亮的位置（文字元素）。</summary>
    public int EffectiveCharacterCount => Mode == FollowMode.WordTracking
        ? _matcher.RecognizedCharacterCount
        : Prompt.CharacterOffset(TimerWordProgress);

    /// <summary>計時器模式的小數詞進度；逐字追蹤沒有，回傳 null。原版強調要有小數，整數位置做不出真正平滑的捲動。</summary>
    public double? ContinuousWordProgress => Mode == FollowMode.WordTracking ? null : TimerWordProgress;

    public bool IsFinished => Prompt.CharacterCount > 0 && EffectiveCharacterCount >= Prompt.CharacterCount;

    public void Start(double now)
    {
        if (IsFinished)
        {
            return;
        }
        IsRunning = true;
        _lastTickTime = now;
        _lastJumpAt = double.NegativeInfinity; // 每次開始都可能是新的時鐘（新的音訊來源從 0 起算），舊時鐘的跳轉時間不能留著比
        _voiceActivity.Reset();
        IsSpeaking = false;
        IsListening = Mode != FollowMode.Classic;
        if (Mode == FollowMode.WordTracking)
        {
            // 原版 beginRecognition／resume：辨識重新開始，從目前位置起算
            _matcher.RestartFromCurrentProgress();
            _spokenAnchorPrefix = "";
            _lastSpokenText = "";
        }
    }

    /// <summary>
    /// 暫停後繼續（Ctrl+Alt+L）。跟 <see cref="Start"/> 一樣從目前位置起算；差別是麥克風與辨識在暫停時沒停，
    /// 同一句辨識結果裡暫停前、暫停中講的字要當成舊的剪掉（跟跳轉一樣），不然會把高亮拖走。
    /// </summary>
    public void Resume(double now)
    {
        var heard = _lastSpokenText;
        Start(now);
        if (Mode == FollowMode.WordTracking)
        {
            _spokenAnchorPrefix = heard;
            _lastSpokenText = heard;
        }
    }

    public void Pause()
    {
        IsRunning = false;
        _lastTickTime = null;
        IsListening = false;
        IsSpeaking = false;
        _voiceActivity.Reset();
    }

    /// <summary>計時器推進。逐字追蹤不靠計時器，什麼都不做。</summary>
    public void Tick(double now)
    {
        var elapsed = Math.Clamp(now - (_lastTickTime ?? now), 0, MaxTickSeconds);
        _lastTickTime = now;
        if (!IsRunning || IsFinished)
        {
            return;
        }

        if (Mode == FollowMode.VoiceActivated)
        {
            IsSpeaking = _voiceActivity.IsActive(now); // 沒有新的音量進來時，餘韻也要照時間結束
        }
        var advancing = Mode switch
        {
            FollowMode.Classic => true,
            FollowMode.VoiceActivated => IsListening && IsSpeaking,
            _ => false,
        };
        if (advancing)
        {
            TimerWordProgress = Math.Min(Prompt.Words.Count, TimerWordProgress + ScrollSpeed * elapsed);
        }
    }

    /// <summary>麥克風一格的音量（<see cref="AudioLevelMeter.NormalizedRms"/>）。</summary>
    public void ProcessAudioLevel(float level, double now)
    {
        _voiceActivity.Process(level, now);
        IsSpeaking = _voiceActivity.IsActive(now);
    }

    /// <summary>辨識引擎的部分結果：上次 <see cref="EndOfUtterance"/> 以來的完整文字。只在逐字追蹤、正在跑的時候有作用。</summary>
    public void ProcessTranscript(string transcript, double now)
    {
        if (Mode != FollowMode.WordTracking)
        {
            return;
        }
        if (!IsRunning)
        {
            _lastSpokenText = transcript; // 暫停中不比對，但記下來：繼續時要剪掉（Resume）
            return;
        }
        if (now - _lastJumpAt <= IgnoreTranscriptsAfterJumpSeconds)
        {
            return;
        }
        _lastSpokenText = transcript;
        var trimmed = TrimAnchorPrefix(transcript);
        if (trimmed.Length > 0)
        {
            _matcher.Match(trimmed);
        }
    }

    /// <summary>辨識引擎偵測到一句講完、清空結果重來。</summary>
    public void EndOfUtterance()
    {
        _matcher.RestartFromCurrentProgress();
        _spokenAnchorPrefix = "";
        _lastSpokenText = "";
    }

    /// <summary>點字跳轉、滾輪追趕：跳到指定位置（可以往回）。</summary>
    public void JumpTo(int characterOffset, double now)
    {
        var clamped = Math.Clamp(characterOffset, 0, Prompt.CharacterCount);
        if (Mode == FollowMode.WordTracking)
        {
            _matcher.Jump(clamped);
            if (IsRunning)
            {
                // 這一句已經講出口的字是跳轉前的，之後的部分結果要先剪掉它們（原版 spokenAnchorPrefix）
                _spokenAnchorPrefix = _lastSpokenText;
                _lastJumpAt = now;
            }
        }
        else
        {
            TimerWordProgress = Prompt.WordProgress(clamped);
            if (IsRunning)
            {
                _lastTickTime = now;
            }
        }
    }

    public void SetScrollSpeed(double speed) => ScrollSpeed = Session.ScrollSpeed.Clamp(speed);

    /// <summary>剪掉跳轉前已經講過的字：跟跳轉當時的辨識結果共同的開頭，至少剪到只剩最後 24 個舊字。以文字元素計。</summary>
    private string TrimAnchorPrefix(string transcript)
    {
        if (_spokenAnchorPrefix.Length == 0)
        {
            return transcript;
        }
        var full = TextElements.Split(transcript);
        var anchor = TextElements.Split(_spokenAnchorPrefix);
        var common = 0;
        while (common < full.Length && common < anchor.Length && full[common] == anchor[common])
        {
            common++;
        }
        var trim = Math.Min(full.Length, Math.Max(common, anchor.Length - AnchorPrefixAllowance));
        return string.Concat(full[trim..]);
    }
}
