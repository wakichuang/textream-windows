namespace TextreamWindows.Core.Audio;

/// <summary>音量計。移植 Textream iOS 版 <c>AudioSampleLevelMeter.normalizedRMS</c>。</summary>
public static class AudioLevelMeter
{
    /// <summary>一段樣本（-1～1 的浮點數）的均方根音量，0～1。NaN、無限大跳過；沒有樣本回 0。</summary>
    public static float NormalizedRms(ReadOnlySpan<float> samples)
    {
        double squaredTotal = 0;
        var count = 0;
        foreach (var sample in samples)
        {
            if (!float.IsFinite(sample))
            {
                continue;
            }
            squaredTotal += (double)sample * sample;
            count++;
        }
        return count == 0 ? 0 : (float)Math.Min(1, Math.Sqrt(squaredTotal / count));
    }
}
