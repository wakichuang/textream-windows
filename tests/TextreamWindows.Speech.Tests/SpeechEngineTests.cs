using System.Diagnostics;
using TextreamWindows.Speech.Audio;
using TextreamWindows.Speech.Recognition;

namespace TextreamWindows.Speech.Tests;

public class SpeechEngineTests
{
    private sealed record Run(List<RecognitionResult> Partials, List<RecognitionResult> Endings, List<string> Order);

    /// <summary>把一個音檔全速餵進引擎，收集事件（Order 記錄「P:文字」「E:文字」的先後）。</summary>
    private static async Task<Run> Recognize(string wav)
    {
        using var engine = new SpeechEngine(TestModels.DefaultModel!);
        var run = new Run([], [], []);
        engine.PartialResult += r => { lock (run) { run.Partials.Add(r); run.Order.Add("P:" + r.Text); } };
        engine.EndOfUtterance += r => { lock (run) { run.Endings.Add(r); run.Order.Add("E:" + r.Text); } };

        using var source = WavFileSource.FromFile(wav, speed: double.PositiveInfinity);
        source.ChunkAvailable += engine.Accept;
        source.Stopped += _ => engine.Complete();
        source.Start();
        await engine.Completion.WaitAsync(TimeSpan.FromSeconds(60));
        return run;
    }

    [RequiresModelFact]
    public async Task EndsTheFirstUtteranceWithTheSameTextAsLabTranscribe()
    {
        // 2026-10-08 用 Lab transcribe A 0.wav --fp32 得到第 1 句「昨天是 MONDAY」，同一個模型、同樣設定，結果要一樣
        var run = await Recognize(TestModels.SampleWav("0.wav"));

        Assert.NotEmpty(run.Endings);
        Assert.Equal("昨天是 MONDAY", run.Endings[0].Text.Trim());
    }

    [RequiresModelFact]
    public async Task PartialResultsLeadUpToEachEnding()
    {
        var run = await Recognize(TestModels.SampleWav("3.wav"));

        Assert.NotEmpty(run.Partials);
        Assert.Contains("一般现在时", string.Concat(run.Endings.Select(e => e.Text)));
        // 每一句講完時帶的文字，就是它前面最後一個部分結果
        for (var i = 0; i < run.Order.Count; i++)
        {
            if (run.Order[i].StartsWith("E:", StringComparison.Ordinal))
            {
                Assert.True(i > 0 && run.Order[i - 1] == "P:" + run.Order[i][2..], $"第 {i} 個事件「{run.Order[i]}」前面不是同樣文字的部分結果");
            }
        }
    }

    [RequiresModelFact]
    public async Task TimesFollowTheAudioClock()
    {
        var run = await Recognize(TestModels.SampleWav("3.wav")); // 8.83 秒

        Assert.NotEmpty(run.Partials);
        var times = run.Partials.Concat(run.Endings).Select(r => r.Time).Order().ToList();
        Assert.All(times, t => Assert.InRange(t, 0, 8.83 + 1.0)); // 收尾時補了一小段靜音
        Assert.Equal(run.Partials.Select(p => p.Time).Order(), run.Partials.Select(p => p.Time)); // 部分結果的時間只增不減
    }

    [RequiresModelFact]
    public void AcceptDoesNotBlockTheCaller()
    {
        // 麥克風的執行緒不能被解碼卡住：一次送 10 秒的音訊也要馬上回來
        using var engine = new SpeechEngine(TestModels.DefaultModel!);
        var samples = AudioFile.Load16kMono(TestModels.SampleWav("0.wav"));
        var clock = Stopwatch.StartNew();

        for (var offset = 0; offset < samples.Length; offset += 800)
        {
            engine.Accept(new AudioChunk(samples.AsSpan(offset, Math.Min(800, samples.Length - offset)).ToArray(), offset / 16000.0));
        }

        Assert.True(clock.ElapsedMilliseconds < 200, $"送 10 秒音訊花了 {clock.ElapsedMilliseconds} 毫秒");
    }

    [RequiresModelFact]
    public async Task CompletingWithoutAudioRaisesNothing()
    {
        using var engine = new SpeechEngine(TestModels.DefaultModel!);
        var events = 0;
        engine.PartialResult += _ => Interlocked.Increment(ref events);
        engine.EndOfUtterance += _ => Interlocked.Increment(ref events);

        engine.Complete();
        await engine.Completion.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(0, events);
    }
}
