using TextreamWindows.Core.Session;
using TextreamWindows.Core.Text;
using TextreamWindows.Speech.Audio;
using TextreamWindows.Speech.Follow;
using TextreamWindows.Speech.Recognition;

namespace TextreamWindows.Speech.Tests;

public class FollowPipelineTests
{
    /// <summary>一段固定音量的樣本（正負交錯，RMS 就是 amplitude）。</summary>
    private static float[] Tone(double seconds, float amplitude) =>
        Enumerable.Range(0, (int)(seconds * AudioFile.SampleRate)).Select(i => i % 2 == 0 ? amplitude : -amplitude).ToArray();

    private static float[] Concat(params float[][] parts) => parts.SelectMany(p => p).ToArray();

    // ── 接線（假引擎，CI 能跑） ──

    [Fact]
    public void WordTrackingFollowsPartialsAndRestartsAtEachEndOfUtterance()
    {
        // 第二句只有 4 個字，比「找回位置」要求的 5 個字少：只有句尾重新起算才追得到
        var prompt = new PromptScript("我已經把這本書讀完了。今天很好。");
        var engine = new FakeSpeechEngine();
        using var source = new WavFileSource(Tone(30, 0), speed: 1.0); // 照真實速度播 30 秒：辨識結果送進來時管線還在聽
        using var pipeline = new FollowPipeline(source, engine, new PromptSession(prompt, FollowMode.WordTracking));
        var progress = new List<int>();
        pipeline.ProgressChanged += p => { lock (progress) { progress.Add(p); } };
        pipeline.Start();

        engine.Partial("我已经把这本书", 1.0);
        engine.Partial("我已经把这本书读完了", 1.5);
        engine.End("我已经把这本书读完了", 2.0);
        engine.Partial("今天很好", 3.0);

        Assert.Equal(prompt.CharacterCount, pipeline.EffectiveCharacterCount);
        Assert.Equal(progress.Order(), progress);
        Assert.Equal(prompt.CharacterCount, progress[^1]);
        pipeline.Stop();
    }

    [Fact]
    public async Task AudioLevelsDriveTheSpeakingState()
    {
        // 1 秒安靜、1 秒講話（-18 dBFS，mic01 開口的音量）、2 秒安靜：講話中、然後 0.75 秒餘韻後停
        var samples = Concat(Tone(1, 0.0005f), Tone(1, 0.12f), Tone(2, 0.0005f));
        using var source = new WavFileSource(samples, speed: double.PositiveInfinity);
        using var pipeline = new FollowPipeline(source, new FakeSpeechEngine(), new PromptSession(new PromptScript("今天想跟大家聊聊閱讀"), FollowMode.VoiceActivated));
        var changes = new List<bool>();
        pipeline.SpeakingChanged += s => { lock (changes) { changes.Add(s); } };

        pipeline.Start();
        await pipeline.Completion.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal([true, false], changes);
        Assert.False(pipeline.IsSpeaking);
    }

    [Fact]
    public async Task ItRemembersTheLatestAudioLevelForTheMeter()
    {
        // 浮層的麥克風音量條用（2026-10-08 瓦基要的 UI 調整）
        using var source = new WavFileSource(Concat(Tone(0.5, 0.0005f), Tone(0.5, 0.12f)), speed: double.PositiveInfinity);
        using var pipeline = new FollowPipeline(source, new FakeSpeechEngine(), new PromptSession(new PromptScript("我讀書"), FollowMode.VoiceActivated));

        pipeline.Start();
        await pipeline.Completion.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(0.12f, pipeline.AudioLevel, 0.001f);
    }

    [Theory]
    [InlineData(FollowMode.WordTracking, true)]
    [InlineData(FollowMode.Classic, false)]
    [InlineData(FollowMode.VoiceActivated, false)]
    public async Task AudioGoesToTheEngineOnlyInWordTracking(FollowMode mode, bool expectsAudio)
    {
        // 另外兩種模式不需要辨識，省下約 14% 的 CPU（第 13.2 節）
        var engine = new FakeSpeechEngine();
        using var source = new WavFileSource(Tone(1, 0.1f), speed: double.PositiveInfinity);
        using var pipeline = new FollowPipeline(source, engine, new PromptSession(new PromptScript("我讀書"), mode));

        pipeline.Start();
        await pipeline.Completion.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(expectsAudio ? 20 : 0, engine.AcceptedChunks);
    }

    [Fact]
    public async Task WhenTheSourceEndsTheEngineIsCompletedAndThePipelineStops()
    {
        var engine = new FakeSpeechEngine();
        using var source = new WavFileSource(Tone(0.5, 0.1f), speed: double.PositiveInfinity);
        using var pipeline = new FollowPipeline(source, engine, new PromptSession(new PromptScript("我讀書"), FollowMode.WordTracking));
        var stopped = 0;
        pipeline.Stopped += _ => Interlocked.Increment(ref stopped);

        pipeline.Start();
        await pipeline.Completion.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(engine.Completed);
        Assert.Equal(1, stopped);
        Assert.False(pipeline.Session.IsRunning);
    }

    [Fact]
    public async Task StopEndsAPlaybackEarly()
    {
        using var source = new WavFileSource(Tone(30, 0.1f), speed: 1.0); // 30 秒，照真實速度
        using var pipeline = new FollowPipeline(source, new FakeSpeechEngine(), new PromptSession(new PromptScript("我讀書"), FollowMode.WordTracking));

        pipeline.Start();
        await Task.Delay(150);
        pipeline.Stop();

        await pipeline.Completion.WaitAsync(TimeSpan.FromSeconds(3));
    }

    // ── 即時辨識文字（瓦基 2026-10-08：浮層下方顯示講了什麼，讓人感覺真的有被聽到） ──

    [Fact]
    public void ItRemembersWhatWasJustHeardAcrossTheEndOfAnUtterance()
    {
        var engine = new FakeSpeechEngine();
        using var source = new WavFileSource(Tone(30, 0), speed: 1.0);
        using var pipeline = new FollowPipeline(source, engine, new PromptSession(new PromptScript("我已經讀完了。今天很好。"), FollowMode.WordTracking));
        pipeline.Start();
        Assert.Equal("", pipeline.Heard);

        engine.Partial("我已经", 1.0);
        Assert.Equal("我已经", pipeline.Heard);

        engine.End("我已经读完了", 2.0);
        Assert.Equal("我已经读完了", pipeline.Heard); // 一句講完、引擎清空重來：畫面上先留著，不要一閃變空白

        engine.Partial("今天", 3.0);
        Assert.Equal("今天", pipeline.Heard);
        pipeline.Stop();
    }

    // ── 暫停／繼續（Ctrl+Alt+L，計畫書第 5 階段步驟 4） ──

    [Fact]
    public void PausingStopsFollowingAndResumingDoesNotCountWordsSaidWhilePaused()
    {
        var prompt = new PromptScript("第一句話在這裡。第二句話在那裡。");
        var engine = new FakeSpeechEngine();
        using var source = new WavFileSource(Tone(30, 0), speed: 1.0);
        using var pipeline = new FollowPipeline(source, engine, new PromptSession(prompt, FollowMode.WordTracking));
        pipeline.Start();
        engine.Partial("第一句话", 1.0);
        var before = pipeline.EffectiveCharacterCount;

        pipeline.Pause();
        engine.Partial("第一句话在这里", 1.5);
        Assert.True(pipeline.IsPaused);
        Assert.Equal(before, pipeline.EffectiveCharacterCount);

        pipeline.Resume();
        engine.Partial("第一句话在这里", 2.0);
        Assert.False(pipeline.IsPaused);
        Assert.Equal(before, pipeline.EffectiveCharacterCount);
        engine.Partial("第一句话在这里在这里", 2.5);
        Assert.True(pipeline.EffectiveCharacterCount > before);
        pipeline.Stop();
    }

    [Fact]
    public async Task WhilePausedTheMeterMovesButNobodyIsSpeaking()
    {
        // 暫停時麥克風沒關：音量條照動（看得出麥克風還活著），但狀態不能顯示「跟著你念」
        using var source = new WavFileSource(Tone(30, 0.12f), speed: 1.0);
        using var pipeline = new FollowPipeline(source, new FakeSpeechEngine(), new PromptSession(new PromptScript("我讀書"), FollowMode.VoiceActivated));
        pipeline.Start();
        await WaitUntil(() => pipeline.IsSpeaking);

        pipeline.Pause();
        await Task.Delay(300);

        Assert.False(pipeline.IsSpeaking);
        Assert.Equal(0.12f, pipeline.AudioLevel, 0.001f);
        pipeline.Stop();
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(20);
        }
        Assert.True(condition());
    }

    // ── 端對端（真的模型，本機才跑；計畫書第 4 階段步驟 5 的冒煙測試） ──

    [RequiresModelFact]
    public async Task FollowsTheBundledSampleToTheEndOfItsScript()
    {
        // 模型附帶的 3.wav（不是瓦基的聲音），講稿是它的內容改寫成繁體、加上標點；模型輸出的是簡體
        var prompt = new PromptScript("第一句是個什麼時態？加了 ES，是一般現在時。對，後面它時態寫上。");
        using var engine = new SpeechEngine(TestModels.DefaultModel!);
        using var source = WavFileSource.FromFile(TestModels.SampleWav("3.wav"), speed: double.PositiveInfinity);
        using var pipeline = new FollowPipeline(source, engine, new PromptSession(prompt, FollowMode.WordTracking));

        pipeline.Start();
        await pipeline.Completion.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(prompt.CharacterCount, pipeline.EffectiveCharacterCount);
    }
}
