using System.Diagnostics;

namespace TextreamWindows.Speech.Audio;

/// <summary>
/// 把錄音檔當成麥克風播：照真實速度（或加速）一批一批送出樣本，給回放測試與 Lab 用（計畫書第 4 階段步驟 1）。
/// <see cref="AudioChunk.Time"/> 永遠是音訊時間，加速只影響送出的快慢。
/// </summary>
public sealed class WavFileSource : IAudioSource
{
    private readonly float[] _samples;
    private readonly double _speed;
    private readonly int _chunkLength;
    private readonly CancellationTokenSource _stop = new();
    private Task _completion = Task.CompletedTask;
    private int _started;

    /// <param name="samples">16 kHz 單聲道樣本（<see cref="AudioFile.Load16kMono"/>）</param>
    /// <param name="speed">1 是真實速度，2 是兩倍速；<see cref="double.PositiveInfinity"/> 是能多快就多快</param>
    /// <param name="chunkMilliseconds">每批多長，預設 50 毫秒，跟麥克風的緩衝一樣</param>
    public WavFileSource(float[] samples, double speed = 1.0, int chunkMilliseconds = 50)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(speed);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkMilliseconds);
        _samples = samples;
        _speed = speed;
        _chunkLength = AudioFile.SampleRate * chunkMilliseconds / 1000;
    }

    public static WavFileSource FromFile(string path, double speed = 1.0, int chunkMilliseconds = 50) =>
        new(AudioFile.Load16kMono(path), speed, chunkMilliseconds);

    public event Action<AudioChunk>? ChunkAvailable;

    public event Action<Exception?>? Stopped;

    /// <summary>播完或被停止時完成（<see cref="Stopped"/> 觸發之後）。</summary>
    public Task Completion => _completion;

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            throw new InvalidOperationException("WavFileSource 只能 Start 一次。");
        }
        _completion = Task.Factory.StartNew(Play, TaskCreationOptions.LongRunning);
    }

    public void Stop() => _stop.Cancel();

    private void Play()
    {
        Exception? failure = null;
        try
        {
            var clock = Stopwatch.StartNew();
            for (var offset = 0; offset < _samples.Length && !_stop.IsCancellationRequested; offset += _chunkLength)
            {
                var time = offset / (double)AudioFile.SampleRate;
                if (!double.IsPositiveInfinity(_speed))
                {
                    // 第 n 批在牆上時鐘的 time / speed 秒送出，跟麥克風一樣是錄到了才送
                    var wait = time / _speed - clock.Elapsed.TotalSeconds;
                    if (wait > 0 && _stop.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(wait)))
                    {
                        break;
                    }
                }
                var chunk = _samples.AsSpan(offset, Math.Min(_chunkLength, _samples.Length - offset)).ToArray();
                ChunkAvailable?.Invoke(new AudioChunk(chunk, time));
            }
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        Stopped?.Invoke(failure);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }
}
