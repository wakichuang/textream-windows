namespace TextreamWindows.Speech.Tests.Golden;

/// <summary>剪接工具本身的測試：不需要模型或音檔，CI 能跑。</summary>
public class AudioEditsTests
{
    private static float[] Sine(double seconds, double hertz) =>
        Enumerable.Range(0, (int)(seconds * 16000)).Select(i => (float)(0.3 * Math.Sin(2 * Math.PI * hertz * i / 16000))).ToArray();

    private static float[] Numbered(int count) => Enumerable.Range(0, count).Select(i => (float)i).ToArray();

    /// <summary>每秒穿過零點幾次（由負到正），等於頻率。</summary>
    private static double Frequency(float[] samples)
    {
        var crossings = 0;
        for (var i = 1; i < samples.Length; i++)
        {
            if (samples[i - 1] < 0 && samples[i] >= 0)
            {
                crossings++;
            }
        }
        return crossings / (samples.Length / 16000.0);
    }

    [Fact]
    public void CutRemovesTheRange()
    {
        var result = AudioEdits.Cut(Numbered(48000), 1, 2);

        Assert.Equal(32000, result.Length);
        Assert.Equal(15999f, result[15999]);
        Assert.Equal(32000f, result[16000]);
    }

    [Fact]
    public void InsertPutsTheClipAtTheTime()
    {
        var result = AudioEdits.Insert(Numbered(32000), 1, AudioEdits.Silence(0.5));

        Assert.Equal(40000, result.Length);
        Assert.Equal(15999f, result[15999]);
        Assert.Equal(0f, result[16000]);
        Assert.Equal(16000f, result[24000]);
    }

    [Fact]
    public void SliceTakesTheRange()
    {
        var result = AudioEdits.Slice(Numbered(48000), 0.5, 1.5);

        Assert.Equal(16000, result.Length);
        Assert.Equal(8000f, result[0]);
    }

    [Fact]
    public void TimeStretchShortensWithoutChangingPitch()
    {
        var tone = Sine(2, 220);

        var faster = AudioEdits.TimeStretch(tone, 1.25);

        Assert.Equal(25600, faster.Length); // 2 秒 ÷ 1.25 = 1.6 秒
        Assert.InRange(Frequency(faster), 210, 230); // 音高還是 220 Hz；改取樣率的做法會變成 275 Hz
    }
}
