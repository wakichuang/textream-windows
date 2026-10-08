using TextreamWindows.Core.Audio;
using TextreamWindows.Core.Session;
using TextreamWindows.Speech.Audio;
using TextreamWindows.Speech.Recognition;

namespace TextreamWindows.Speech.Follow;

/// <summary>
/// 音訊來源 → 辨識引擎 → <see cref="PromptSession"/>，整條接起來（計畫書第 4 階段步驟 3）。
/// 一個 <see cref="FollowPipeline"/> 對應一次「開始聽」到「停」；暫停後繼續就用同一個 session 建新的管線。
/// </summary>
/// <remarks>
/// 執行緒：音量在音訊來源的執行緒進來，辨識結果在引擎的解碼執行緒進來，介面在 UI 執行緒讀。
/// <see cref="PromptSession"/> 不是執行緒安全的，所有存取都經過同一把鎖；事件在鎖外、在送出它的執行緒觸發，UI 要自己切回 UI 執行緒。
/// 時間一律用音訊時間（<see cref="AudioChunk.Time"/>、<see cref="RecognitionResult.Time"/>），新管線從 0 起算，session 每次 Start 會換新時鐘。
/// </remarks>
public sealed class FollowPipeline : IDisposable
{
    private readonly IAudioSource _source;
    private readonly ISpeechEngine _engine;
    private readonly object _gate = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _lastProgress;
    private bool _lastSpeaking;
    private double _lastAudioTime;
    private float _audioLevel;
    private bool _paused;
    private string _heard = "";

    public FollowPipeline(IAudioSource source, ISpeechEngine engine, PromptSession session)
    {
        _source = source;
        _engine = engine;
        Session = session;
        _lastProgress = session.EffectiveCharacterCount;

        _source.ChunkAvailable += OnChunk;
        _source.Stopped += OnSourceStopped;
        _engine.PartialResult += OnPartial;
        _engine.EndOfUtterance += OnEndOfUtterance;
    }

    /// <summary>直接讀它的屬性不保證執行緒安全；跨執行緒請用 <see cref="EffectiveCharacterCount"/>、<see cref="IsSpeaking"/>。</summary>
    public PromptSession Session { get; }

    /// <summary>高亮位置變了（文字元素）。</summary>
    public event Action<int>? ProgressChanged;

    /// <summary>有沒有在講話變了。</summary>
    public event Action<bool>? SpeakingChanged;

    /// <summary>音訊來源結束、引擎也把最後的結果送完了。出錯時帶例外（例如麥克風被拔掉）。</summary>
    public event Action<Exception?>? Stopped;

    public Task Completion => _completion.Task;

    public int EffectiveCharacterCount
    {
        get
        {
            lock (_gate)
            {
                return Session.EffectiveCharacterCount;
            }
        }
    }

    /// <summary>最後一批音訊的音量（RMS，0～1），給浮層的音量條。</summary>
    public float AudioLevel
    {
        get
        {
            lock (_gate)
            {
                return _audioLevel;
            }
        }
    }

    public bool IsSpeaking
    {
        get
        {
            lock (_gate)
            {
                return Session.IsSpeaking;
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            Session.Start(0);
        }
        _source.Start();
    }

    /// <summary>
    /// 剛剛聽到的話（辨識引擎這一句的文字，簡體、原樣），給浮層下方即時顯示（瓦基 2026-10-08）。
    /// 一句講完、引擎清空重來時先留著上一句，等下一句有字才換，畫面不會一閃變空白。
    /// </summary>
    public string Heard
    {
        get
        {
            lock (_gate)
            {
                return _heard;
            }
        }
    }

    /// <summary>暫停中（Ctrl+Alt+L）：麥克風與辨識照跑，但高亮不動、不算講話。</summary>
    public bool IsPaused
    {
        get
        {
            lock (_gate)
            {
                return _paused;
            }
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            _paused = true;
            Session.Pause();
        }
        Publish();
    }

    /// <summary>從暫停的地方繼續，暫停前、暫停中講的字不算（<see cref="PromptSession.Resume"/>）。</summary>
    public void Resume()
    {
        lock (_gate)
        {
            if (!_paused)
            {
                return;
            }
            _paused = false;
            Session.Resume(_lastAudioTime);
        }
        Publish();
    }

    /// <summary>停止聽：音訊來源停下，引擎收尾，最後觸發 <see cref="Stopped"/>。</summary>
    public void Stop() => _source.Stop();

    /// <summary>定速捲動、有講話才捲的計時器（第 5 階段由 WPF 每 50 毫秒呼叫）。時間用最後一批音訊的音訊時間。</summary>
    public void Tick()
    {
        lock (_gate)
        {
            Session.Tick(_lastAudioTime);
        }
        Publish();
    }

    /// <summary>點字跳轉、滾輪追趕。</summary>
    public void JumpTo(int characterOffset)
    {
        lock (_gate)
        {
            Session.JumpTo(characterOffset, _lastAudioTime);
        }
        Publish();
    }

    private void OnChunk(AudioChunk chunk)
    {
        lock (_gate)
        {
            _lastAudioTime = chunk.End;
            _audioLevel = AudioLevelMeter.NormalizedRms(chunk.Samples);
            if (!_paused)
            {
                Session.ProcessAudioLevel(_audioLevel, chunk.End);
            }
        }
        if (Session.Mode == FollowMode.WordTracking)
        {
            _engine.Accept(chunk);
        }
        Publish();
    }

    private void OnPartial(RecognitionResult result)
    {
        lock (_gate)
        {
            if (result.Text.Length > 0)
            {
                _heard = result.Text;
            }
            Session.ProcessTranscript(result.Text, result.Time);
        }
        Publish();
    }

    private void OnEndOfUtterance(RecognitionResult result)
    {
        lock (_gate)
        {
            if (result.Text.Length > 0)
            {
                _heard = result.Text;
            }
            Session.ProcessTranscript(result.Text, result.Time);
            Session.EndOfUtterance();
        }
        Publish();
    }

    private void OnSourceStopped(Exception? error)
    {
        _engine.Complete();
        _engine.Completion.ContinueWith(_ =>
        {
            lock (_gate)
            {
                Session.Pause();
            }
            Publish();
            Stopped?.Invoke(error);
            _completion.TrySetResult();
        }, TaskScheduler.Default);
    }

    /// <summary>位置或講話狀態變了才發事件。在鎖外觸發，免得訂閱者回頭讀屬性時卡住。</summary>
    private void Publish()
    {
        int progress;
        bool speaking;
        bool progressChanged;
        bool speakingChanged;
        lock (_gate)
        {
            progress = Session.EffectiveCharacterCount;
            speaking = Session.IsSpeaking;
            progressChanged = progress != _lastProgress;
            speakingChanged = speaking != _lastSpeaking;
            _lastProgress = progress;
            _lastSpeaking = speaking;
        }
        if (progressChanged)
        {
            ProgressChanged?.Invoke(progress);
        }
        if (speakingChanged)
        {
            SpeakingChanged?.Invoke(speaking);
        }
    }

    public void Dispose()
    {
        _source.ChunkAvailable -= OnChunk;
        _source.Stopped -= OnSourceStopped;
        _engine.PartialResult -= OnPartial;
        _engine.EndOfUtterance -= OnEndOfUtterance;
    }
}
