using System.Diagnostics;
using TextreamWindows.Speech.Audio;

namespace TextreamWindows.Speech.Tests;

public class WavFileSourceTests
{
    private static float[] Ramp(int count) => Enumerable.Range(0, count).Select(i => i / 100000f).ToArray();

    /// <summary>播完，收集所有批次與 Stopped 的次數。</summary>
    private static async Task<(List<AudioChunk> Chunks, int StoppedCount, Exception? Error, TimeSpan Elapsed)> Play(WavFileSource source)
    {
        var chunks = new List<AudioChunk>();
        var stoppedCount = 0;
        Exception? error = null;
        source.ChunkAvailable += c => { lock (chunks) { chunks.Add(c); } };
        source.Stopped += e => { Interlocked.Increment(ref stoppedCount); error = e; };
        var clock = Stopwatch.StartNew();
        source.Start();
        await source.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        return (chunks, stoppedCount, error, clock.Elapsed);
    }

    [Fact]
    public async Task EmitsEverySampleInOrderWithAudioTimestamps()
    {
        var samples = Ramp(16000); // 1 秒
        using var source = new WavFileSource(samples, speed: double.PositiveInfinity, chunkMilliseconds: 50);

        var (chunks, stoppedCount, error, _) = await Play(source);

        Assert.Equal(20, chunks.Count);
        Assert.Equal(samples, chunks.SelectMany(c => c.Samples));
        Assert.Equal(Enumerable.Range(0, 20).Select(i => Math.Round(i * 0.05, 6)), chunks.Select(c => Math.Round(c.Time, 6)));
        Assert.Equal(1, stoppedCount);
        Assert.Null(error);
    }

    [Fact]
    public async Task LastChunkMayBeShorter()
    {
        using var source = new WavFileSource(Ramp(16000 + 100), speed: double.PositiveInfinity, chunkMilliseconds: 50);

        var (chunks, _, _, _) = await Play(source);

        Assert.Equal(21, chunks.Count);
        Assert.Equal(100, chunks[^1].Samples.Length);
        Assert.Equal(1.0, chunks[^1].Time, 6);
    }

    [Fact]
    public async Task RealTimePlaybackTakesAboutTheAudioDuration()
    {
        using var source = new WavFileSource(Ramp(6400), speed: 1.0); // 0.4 秒

        var (_, _, _, elapsed) = await Play(source);

        Assert.InRange(elapsed.TotalSeconds, 0.33, 0.9);
    }

    [Fact]
    public async Task DoubleSpeedTakesAboutHalf()
    {
        using var source = new WavFileSource(Ramp(16000), speed: 2.0); // 1 秒的音訊

        var (chunks, _, _, elapsed) = await Play(source);

        Assert.InRange(elapsed.TotalSeconds, 0.4, 0.85);
        Assert.Equal(0.95, chunks[^1].Time, 6); // 時間戳記仍是音訊時間，不因加速而改變
    }

    [Fact]
    public async Task StopEndsPlaybackEarlyAndStillReportsStoppedOnce()
    {
        // 5 秒照真實速度播是 100 批。收到第一批就停：停得掉的話遠遠不到 100 批。
        // 第一版是「等 0.2 秒再停、最多 20 批」，2026-10-08 CI 的主機慢，0.2 秒實際等了約 2.7 秒、收到 55 批而失敗；
        // 改成看事件不看時鐘，主機快慢都一樣
        using var source = new WavFileSource(Ramp(16000 * 5), speed: 1.0);
        var stoppedCount = 0;
        var chunks = 0;
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.ChunkAvailable += _ =>
        {
            Interlocked.Increment(ref chunks);
            first.TrySetResult();
        };
        source.Stopped += _ => Interlocked.Increment(ref stoppedCount);

        source.Start();
        await first.Task.WaitAsync(TimeSpan.FromSeconds(5));
        source.Stop();

        await source.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.InRange(chunks, 1, 50);
        Assert.Equal(1, stoppedCount);
    }
}
