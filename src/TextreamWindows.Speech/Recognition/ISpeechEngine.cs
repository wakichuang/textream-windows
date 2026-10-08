using TextreamWindows.Speech.Audio;

namespace TextreamWindows.Speech.Recognition;

/// <summary>辨識結果：<see cref="Text"/> 是這一句（上次句尾以來）的完整文字，<see cref="Time"/> 是產生它時已經聽到第幾秒（音訊時間）。</summary>
public sealed record RecognitionResult(string Text, double Time);

/// <summary>
/// 串流語音辨識引擎（計畫書第 4 階段步驟 2）。做成介面，FollowPipeline 的接線邏輯可以用假引擎在 CI 測，不必有模型。
/// </summary>
public interface ISpeechEngine : IDisposable
{
    /// <summary>這一句的辨識結果變了（變長或被改寫）。在引擎的解碼執行緒觸發。</summary>
    event Action<RecognitionResult>? PartialResult;

    /// <summary>偵測到一句講完（句尾），帶這一句最後的文字；之後的部分結果從空的重新開始。沒辨識出字的句子不觸發。</summary>
    event Action<RecognitionResult>? EndOfUtterance;

    /// <summary>送進一批音訊。不會卡住呼叫端（麥克風的執行緒），實際解碼在背景。</summary>
    void Accept(AudioChunk chunk);

    /// <summary>不會再有音訊了：把最後幾個字推出來、收尾。</summary>
    void Complete();

    /// <summary><see cref="Complete"/> 之後、最後一個事件送完時完成。</summary>
    Task Completion { get; }
}
