using TextreamWindows.Speech.Audio;
using TextreamWindows.Speech.Recognition;

namespace TextreamWindows.Speech.Tests;

/// <summary>假的辨識引擎：記下收到的音訊，由測試決定什麼時候吐出什麼結果。不需要模型，CI 能跑。</summary>
public sealed class FakeSpeechEngine : ISpeechEngine
{
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _acceptedChunks;

    public event Action<RecognitionResult>? PartialResult;

    public event Action<RecognitionResult>? EndOfUtterance;

    public int AcceptedChunks => Volatile.Read(ref _acceptedChunks);

    public bool Completed { get; private set; }

    public Task Completion => _completion.Task;

    public void Accept(AudioChunk chunk) => Interlocked.Increment(ref _acceptedChunks);

    public void Complete()
    {
        Completed = true;
        _completion.TrySetResult();
    }

    public void Partial(string text, double time) => PartialResult?.Invoke(new RecognitionResult(text, time));

    public void End(string text, double time) => EndOfUtterance?.Invoke(new RecognitionResult(text, time));

    public void Dispose()
    {
    }
}
