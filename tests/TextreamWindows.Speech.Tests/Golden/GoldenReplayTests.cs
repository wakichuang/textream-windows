using Xunit.Abstractions;
using TextreamWindows.Speech.Analysis;

namespace TextreamWindows.Speech.Tests.Golden;

/// <summary>
/// 第 7.4 節的錄音回放測試（第 2b 層，只在本機跑）。全部從 mic01 剪出來：瓦基 2026-10-08 用 Focusrite 照稿逐字念 podcast 講稿第 9～14 行，84.5 秒，沒有後製。
/// </summary>
/// <remarks>
/// mic01 六行講稿在音檔裡的位置（2026-10-08 用 Lab transcribe A --fp32 的逐字時間找出句間空檔）：
/// 第 1 行 2.7～11.9 秒、第 2 行 12.7～19.2、第 3 行 19.8～43.0、第 4 行 43.7～57.7、第 5 行 59.8～71.8、第 6 行 72.4～83.0。
/// 切點取空檔中間：12.3、19.5、43.4、58.7、72.1。
/// </remarks>
public class GoldenReplayTests(ITestOutputHelper output)
{
    private const double Line2Start = 12.3;
    private const double Line3Start = 19.5;
    private const double Line4Start = 43.4;
    private const double Line5Start = 58.7;

    /// <summary>判斷標準（第 7.4 節）的門檻。</summary>
    private const double MinCoverage = 0.95;
    private const double MaxSecondsToReachEnd = 1.0;
    private const int MaxAdLibDrift = 10;

    private static string Mic01Script => GoldenAudio.Script("mic01");

    /// <summary>第 1～3 條：只進不退、追到 95%、最後一個字的聲音結束後 1 秒內到結尾。</summary>
    private void AssertFollowsToTheEnd(ReplayResult result, float[] samples, string scenario)
    {
        var lastSoundForLog = LastSoundEnd(samples);
        output.WriteLine($"{scenario}：追到 {result.Coverage:P1}（{result.Readable(result.Final)}／{result.TotalReadable}）、倒退 {result.Backwards} 次、" +
            $"最後一個字 {lastSoundForLog:F2} 秒結束、高亮 {(result.TimeReachingEnd is { } t ? $"{t:F2} 秒到結尾（晚 {t - lastSoundForLog:F2} 秒）" : "沒到結尾")}");
        Assert.True(result.Backwards == 0, $"{scenario}：高亮往回走了 {result.Backwards} 次");
        Assert.True(result.Coverage >= MinCoverage,
            $"{scenario}：只追到 {result.Coverage:P1}（可讀字 {result.Readable(result.Final)}／{result.TotalReadable}），停在「{result.Around(result.Final)}」");

        var lastSound = LastSoundEnd(samples);
        var reached = result.TimeReachingEnd;
        Assert.True(reached is not null && reached.Value - lastSound <= MaxSecondsToReachEnd,
            $"{scenario}：最後一個字的聲音在 {lastSound:F2} 秒結束，高亮{(reached is null ? "始終沒到結尾" : $"在 {reached:F2} 秒才到結尾")}");
    }

    /// <summary>
    /// 最後一個字的聲音在第幾秒結束：只看最後 10 秒（每個情境的結尾都是 mic01 本身）。
    /// 看整段的話，g06 插進來的 Podcast 經過後製、比 mic01 大聲，有聲段的門檻（最大音量的 10%）被拉高，
    /// mic01 最後比較小聲的字會被當成安靜，2026-10-08 因此誤判成 96.30 秒（實際約 98 秒）。
    /// </summary>
    private static double LastSoundEnd(float[] samples)
    {
        var tailStart = Math.Max(0, samples.Length - 10 * 16000);
        return tailStart / 16000.0 + SpeechSpan.Find(samples[tailStart..], 16000)!.Value.Offset;
    }

    [RequiresGoldenFact]
    public async Task G01_NormalReading()
    {
        var samples = GoldenAudio.Mic01;

        var result = await Replay.Run(Mic01Script, samples);

        AssertFollowsToTheEnd(result, samples, "g01 照稿念");
    }

    [RequiresGoldenFact]
    public async Task G02_FastReading()
    {
        var samples = AudioEdits.TimeStretch(GoldenAudio.Mic01, 1.25); // 講快 25%，不變調

        var result = await Replay.Run(Mic01Script, samples);

        AssertFollowsToTheEnd(result, samples, "g02 講快 1.25 倍");
    }

    /// <summary>
    /// g03 中英混講：mic01 第 3 行念「基模治療（Schema Therapy）又把它們切得更細」，模型 A 沒辨識出這兩個英文字。
    /// 2026-10-08 試過 sherpa-onnx 熱詞（分數 1.5～8）也救不回來，瓦基決定先不做熱詞（計畫書第 13.7 節）。
    /// 這條測的是「英文沒被辨識出來時不會卡住」：講完「切得更細」（約 32.0 秒），1 秒內高亮就要越過它。
    /// </summary>
    [RequiresGoldenFact]
    public async Task G03_UnrecognizedEnglishDoesNotStall()
    {
        var result = await Replay.Run(Mic01Script, GoldenAudio.Mic01);

        var words = result.Prompt.Words;
        var thinner = Enumerable.Range(0, words.Count - 3)
            .First(i => words[i].Text == "切" && words[i + 1].Text == "得" && words[i + 2].Text == "更" && words[i + 3].Text == "細");
        var pastEnglish = words[thinner + 3].CharacterRange.End;
        var progress = result.ProgressAt(33.0);
        output.WriteLine($"g03：33.0 秒時高亮在「{result.Around(progress)}」，要越過「切得更細」");
        Assert.True(progress >= pastEnglish, $"g03：講完「切得更細」1 秒後，高亮還停在「{result.Around(progress)}」");
    }

    [RequiresGoldenFact]
    public async Task G04_SkipAParagraph()
    {
        var samples = AudioEdits.Cut(GoldenAudio.Mic01, Line3Start, Line4Start); // 跳過第 3 行（約 100 字、24 秒）

        var result = await Replay.Run(Mic01Script, samples);

        AssertFollowsToTheEnd(result, samples, "g04 跳過第 3 行");
    }

    [RequiresGoldenFact]
    public async Task G05_PauseAndRereadASentence()
    {
        // 第 2 行念完再念一次（重講），第 4 行念完停 5 秒
        var reread = AudioEdits.Insert(GoldenAudio.Mic01, Line3Start, AudioEdits.Slice(GoldenAudio.Mic01, Line2Start, Line3Start));
        var pauseAt = Line5Start + (Line3Start - Line2Start);
        var samples = AudioEdits.Insert(reread, pauseAt, AudioEdits.Silence(5));

        var result = await Replay.Run(Mic01Script, samples);

        AssertFollowsToTheEnd(result, samples, "g05 重講＋停頓");
        // 第 4 條：停頓期間不動。給 1 秒讓停頓前最後幾個字的結果送完
        var settled = result.ProgressAt(pauseAt + 1.0);
        var beforeResuming = result.ProgressAt(pauseAt + 5.0);
        output.WriteLine($"g05：停頓期間高亮移動 {result.Readable(beforeResuming) - result.Readable(settled)} 個字");
        Assert.True(settled == beforeResuming,
            $"g05：停頓的 5 秒裡高亮從「{result.Around(settled)}」動到「{result.Around(beforeResuming)}」");
    }

    [RequiresGoldenFact(needsPodcast: true)]
    public async Task G06_AdLibInTheMiddle()
    {
        // 第 3 行念完，插入整集 Podcast 第 27 分鐘約 15 秒跟講稿無關的話（講「愛硬拗的我」的個人經驗），再接第 4 行
        var adLib = AudioEdits.Slice(GoldenAudio.Podcast, 1620, 1635);
        var samples = AudioEdits.Insert(GoldenAudio.Mic01, Line4Start, adLib);
        var adLibEnd = Line4Start + 15;

        var result = await Replay.Run(Mic01Script, samples);

        AssertFollowsToTheEnd(result, samples, "g06 中間插 15 秒閒聊");
        // 第 5 條：閒聊期間往前跳不超過 10 個字。給 1 秒讓第 3 行最後幾個字的結果送完
        var drift = result.Readable(result.ProgressAt(adLibEnd)) - result.Readable(result.ProgressAt(Line4Start + 1.0));
        output.WriteLine($"g06：閒聊期間高亮往前 {drift} 個字");
        Assert.True(drift <= MaxAdLibDrift, $"g06：閒聊期間高亮往前跑了 {drift} 個字，到「{result.Around(result.ProgressAt(adLibEnd))}」");
    }

    /// <summary>整集 Podcast（29 分 39 秒，看稿講），約 3～4 分鐘。設環境變數 TEXTREAM_RUN_LONG=1 才跑。</summary>
    [RequiresGoldenFact(needsPodcast: true)]
    public async Task Podcast_WholeEpisode()
    {
        if (Environment.GetEnvironmentVariable("TEXTREAM_RUN_LONG") != "1")
        {
            return; // xUnit 2 不能在執行中改成略過；沒開就直接當通過，名字與說明寫清楚
        }
        var result = await Replay.Run(GoldenAudio.Script("podcast"), GoldenAudio.Podcast);

        Assert.True(result.Backwards == 0, $"整集 Podcast：高亮往回走了 {result.Backwards} 次");
        Assert.True(result.Coverage >= MinCoverage, $"整集 Podcast：只追到 {result.Coverage:P1}");
    }
}
