namespace TextreamWindows.Speech.Audio;

/// <summary>一批 16 kHz 單聲道樣本，<see cref="Time"/> 是它在音訊時間軸上的起點（秒，從開始錄或開始播算起）。</summary>
public sealed record AudioChunk(float[] Samples, double Time)
{
    public double Duration => Samples.Length / (double)AudioFile.SampleRate;

    public double End => Time + Duration;
}

/// <summary>
/// 音訊來源（計畫書第 4.1 節的關鍵設計）：麥克風與「照真實速度播放的錄音檔」是同一個介面，
/// 所以除了麥克風本身，整條流程都能用錄音檔自動測試。
/// 時間一律用「音訊時間」（樣本數換算），不用牆上時鐘：加速播放時，下游看到的時間跟真的講話一樣。
/// </summary>
public interface IAudioSource : IDisposable
{
    /// <summary>新的一批樣本。在來源自己的背景執行緒觸發。</summary>
    event Action<AudioChunk>? ChunkAvailable;

    /// <summary>來源結束（播完、被停止、或出錯時帶例外）。只觸發一次。</summary>
    event Action<Exception?>? Stopped;

    void Start();

    void Stop();
}
