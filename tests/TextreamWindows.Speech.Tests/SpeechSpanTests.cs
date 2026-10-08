using TextreamWindows.Speech.Analysis;

namespace TextreamWindows.Speech.Tests;

public class SpeechSpanTests
{
    private static float[] Signal(params (double seconds, float amplitude)[] parts)
    {
        var list = new List<float>();
        foreach (var (seconds, amplitude) in parts)
        {
            var n = (int)(seconds * 16000);
            for (var i = 0; i < n; i++)
            {
                list.Add(amplitude * (i % 2 == 0 ? 1 : -1));
            }
        }
        return list.ToArray();
    }

    [Fact]
    public void FindsOnsetAndOffsetOfTheLoudPart()
    {
        var samples = Signal((1.0, 0.001f), (2.0, 0.3f), (0.5, 0.001f));

        var span = SpeechSpan.Find(samples, 16000);

        Assert.NotNull(span);
        Assert.Equal(1.0, span.Value.Onset, 2);
        Assert.Equal(3.0, span.Value.Offset, 2);
    }

    [Fact]
    public void KeyboardNoiseBeforeSpeakingIsNotTheOnset()
    {
        // 2026-10-08 瓦基的 mic01.wav：按 Enter 開始錄音後有 0.5 秒約 -42 dBFS 的雜音，2.2 秒後才開口（約 -18 dBFS）
        var samples = Signal((0.5, 0.008f), (1.7, 0.0005f), (2.0, 0.12f), (0.5, 0.0005f));

        var span = SpeechSpan.Find(samples, 16000);

        Assert.NotNull(span);
        Assert.Equal(2.2, span.Value.Onset, 2);
        Assert.Equal(4.2, span.Value.Offset, 2);
    }

    [Fact]
    public void ASingleLoudClickIsNotSpeech()
    {
        // 一格 20 毫秒的喀一聲，前後都很安靜：不算開口
        var samples = Signal((1.0, 0.0005f), (0.02, 0.3f), (1.0, 0.0005f), (1.0, 0.2f));

        var span = SpeechSpan.Find(samples, 16000);

        Assert.NotNull(span);
        Assert.Equal(2.02, span.Value.Onset, 2);
    }

    [Fact]
    public void SilenceHasNoSpeech()
    {
        Assert.Null(SpeechSpan.Find(new float[16000], 16000));
    }

    [Fact]
    public void MedianOfEvenCountIsTheMeanOfTheMiddleTwo()
    {
        Assert.Equal(2.5, Stats.Median([4, 1, 3, 2]));
        Assert.Equal(3, Stats.Median([5, 3, 1]));
        Assert.Null(Stats.Median([]));
    }
}
