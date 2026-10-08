namespace TextreamWindows.Speech.Analysis;

/// <summary>
/// 一段錄音裡「有聲音」的起點與終點（秒）。給 bench 量首字延遲與句尾延遲用，
/// 不是正式的 VAD（那是第 3 階段 Core 的 VoiceActivityDetector）。
/// </summary>
public readonly record struct SpeechSpan(double Onset, double Offset)
{
    private const double FrameSeconds = 0.02;
    private const float AbsoluteFloor = 0.005f; // 約 -46 dBFS，再小就當作沒聲音

    // 比整段最大音量低約 20 dB 以內、而且連續 60 毫秒才算在講話。
    // 原本是 5%（-26 dB）且一格就算，2026-10-08 被按 Enter 的雜音（約 -42 dBFS）騙過，首字延遲多算了 2 秒
    private const float RelativeThreshold = 0.10f;
    private const int MinConsecutiveFrames = 3;

    public static SpeechSpan? Find(float[] samples, int sampleRate)
    {
        var frameLength = (int)(sampleRate * FrameSeconds);
        var frameCount = samples.Length / frameLength;
        var rms = new float[frameCount];
        for (var f = 0; f < frameCount; f++)
        {
            double sum = 0;
            for (var i = f * frameLength; i < (f + 1) * frameLength; i++)
            {
                sum += samples[i] * samples[i];
            }
            rms[f] = (float)Math.Sqrt(sum / frameLength);
        }

        var peak = rms.Length == 0 ? 0 : rms.Max();
        if (peak < AbsoluteFloor)
        {
            return null;
        }
        var threshold = Math.Max(AbsoluteFloor, peak * RelativeThreshold);
        bool Loud(int f) => f >= 0 && f < rms.Length && rms[f] >= threshold;
        bool RunStartsAt(int f) => Enumerable.Range(f, MinConsecutiveFrames).All(Loud);

        var first = Enumerable.Range(0, frameCount).Cast<int?>().FirstOrDefault(f => RunStartsAt(f!.Value));
        if (first is null)
        {
            return null; // 只有零星的喀喀聲，沒有持續的聲音
        }
        var last = Enumerable.Range(0, frameCount).Reverse().First(f => RunStartsAt(f - MinConsecutiveFrames + 1));
        return new SpeechSpan(first.Value * FrameSeconds, (last + 1) * FrameSeconds);
    }
}

public static class Stats
{
    public static double? Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0)
        {
            return null;
        }
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}
