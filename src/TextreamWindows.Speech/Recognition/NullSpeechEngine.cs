using TextreamWindows.Speech.Audio;

namespace TextreamWindows.Speech.Recognition;

/// <summary>不辨識的引擎：給「有講話才捲」這類只要麥克風音量、不要辨識結果的模式用，省下載入模型的時間與記憶體。</summary>
public sealed class NullSpeechEngine : ISpeechEngine
{
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public event Action<RecognitionResult>? PartialResult
    {
        add { }
        remove { }
    }

    public event Action<RecognitionResult>? EndOfUtterance
    {
        add { }
        remove { }
    }

    public Task Completion => _completion.Task;

    public void Accept(AudioChunk chunk)
    {
    }

    public void Complete() => _completion.TrySetResult();

    public void Dispose()
    {
    }
}
