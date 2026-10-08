using TextreamWindows.Core.Session;
using TextreamWindows.Core.Text;
using TextreamWindows.Speech.Audio;
using TextreamWindows.Speech.Follow;
using TextreamWindows.Speech.Recognition;

namespace TextreamWindows.Speech.Tests.Golden;

/// <summary>一次回放的結果：每次辨識結果進來後的（音訊時間, 高亮位置），以及最後的位置。</summary>
public sealed record ReplayResult(PromptScript Prompt, IReadOnlyList<(double Time, int Progress)> Timeline, int Final)
{
    /// <summary>位置換成「讀到第幾個可讀字」（不算空白與標點）。</summary>
    public int Readable(int offset) => Prompt.Elements.Take(offset).Count(TextElements.IsLetterOrNumber);

    public int TotalReadable => Readable(Prompt.CharacterCount);

    /// <summary>第 7.4 節第 2 條：最後追到講稿的幾成（可讀字）。</summary>
    public double Coverage => (double)Readable(Final) / TotalReadable;

    /// <summary>第 1 條：高亮往回走的次數。</summary>
    public int Backwards => Timeline.Zip(Timeline.Skip(1)).Count(p => p.Second.Progress < p.First.Progress);

    /// <summary>某個音訊時間點的高亮位置（那之前最後一次的結果）。</summary>
    public int ProgressAt(double time) => Timeline.LastOrDefault(e => e.Time <= time).Progress;

    /// <summary>高亮第一次到講稿結尾的音訊時間；沒到過回傳 null。</summary>
    public double? TimeReachingEnd => Timeline.FirstOrDefault(e => e.Progress >= Prompt.CharacterCount) is { } hit && hit.Progress > 0 ? hit.Time : null;

    /// <summary>最後停在講稿的哪裡（失敗時好判斷）。</summary>
    public string Around(int offset) => string.Concat(Prompt.Elements.Skip(Math.Max(0, offset - 10)).Take(20)).Replace(" ", "");
}

/// <summary>把一段音訊用真的模型（A，fp32）全速走一遍 FollowPipeline。時間都是音訊時間，所以跟照真實速度播的結果一樣。</summary>
public static class Replay
{
    public static async Task<ReplayResult> Run(string script, float[] samples)
    {
        var prompt = new PromptScript(script);
        using var engine = new SpeechEngine(TestModels.DefaultModel!);
        using var source = new WavFileSource(samples, speed: double.PositiveInfinity);
        using var pipeline = new FollowPipeline(source, engine, new PromptSession(prompt, FollowMode.WordTracking));

        // 在管線之後訂閱：管線先處理完這個結果，這裡再記下處理後的位置
        var timeline = new List<(double, int)>();
        void Record(RecognitionResult r)
        {
            lock (timeline)
            {
                timeline.Add((r.Time, pipeline.EffectiveCharacterCount));
            }
        }
        engine.PartialResult += Record;
        engine.EndOfUtterance += Record;

        pipeline.Start();
        await pipeline.Completion.WaitAsync(TimeSpan.FromMinutes(10));
        return new ReplayResult(prompt, timeline, pipeline.EffectiveCharacterCount);
    }
}
