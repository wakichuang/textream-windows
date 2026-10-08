using TextreamWindows.Speech.Audio;

namespace TextreamWindows.Speech.Tests.Golden;

/// <summary>
/// 從現成錄音剪出第 7.4 節的情境（2026-10-08 瓦基決定不另外錄）：剪掉一段＝跳讀、插靜音＝停頓、重複一句＝重講、
/// 插別段＝閒聊、時間壓縮＝講快。全部是 16 kHz 單聲道，時間單位秒。
/// </summary>
public static class AudioEdits
{
    private const int Rate = AudioFile.SampleRate;

    private static int At(double seconds, int length) => Math.Clamp((int)Math.Round(seconds * Rate), 0, length);

    public static float[] Slice(float[] samples, double from, double to) =>
        samples[At(from, samples.Length)..At(to, samples.Length)];

    public static float[] Cut(float[] samples, double from, double to) =>
        [.. samples[..At(from, samples.Length)], .. samples[At(to, samples.Length)..]];

    public static float[] Insert(float[] samples, double at, float[] clip) =>
        [.. samples[..At(at, samples.Length)], .. clip, .. samples[At(at, samples.Length)..]];

    public static float[] Silence(double seconds) => new float[(int)Math.Round(seconds * Rate)];

    /// <summary>
    /// 不變調的時間伸縮（WSOLA）：<paramref name="speed"/> 1.25 就是講快 25%。
    /// 用改取樣率的方式加速會連音高一起變（像花栗鼠），辨識會變差得不像真人講快，所以不用。
    /// </summary>
    public static float[] TimeStretch(float[] samples, double speed)
    {
        const int frame = 640;  // 40 毫秒
        const int hop = frame / 2;
        const int search = 160; // 往前後各找 10 毫秒，挑波形最接近的位置接上
        var window = Enumerable.Range(0, frame).Select(i => (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / frame))).ToArray();

        var outputLength = (int)(samples.Length / speed);
        var output = new float[outputLength + frame];
        var weight = new float[outputLength + frame];
        var previousEnd = 0; // 上一格在原音的結尾，下一格要接得像它的延續
        for (var outPos = 0; outPos < outputLength; outPos += hop)
        {
            var nominal = (int)(outPos * speed);
            var best = nominal;
            if (outPos > 0)
            {
                var bestScore = double.NegativeInfinity;
                for (var candidate = Math.Max(0, nominal - search); candidate <= nominal + search; candidate++)
                {
                    if (candidate + hop >= samples.Length || previousEnd + hop >= samples.Length)
                    {
                        break;
                    }
                    double score = 0;
                    for (var i = 0; i < hop; i++)
                    {
                        score += samples[candidate + i] * samples[previousEnd + i];
                    }
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }
            }
            for (var i = 0; i < frame && best + i < samples.Length && outPos + i < output.Length; i++)
            {
                output[outPos + i] += samples[best + i] * window[i];
                weight[outPos + i] += window[i];
            }
            previousEnd = best + hop;
        }

        var result = new float[outputLength];
        for (var i = 0; i < outputLength; i++)
        {
            result[i] = weight[i] > 1e-3f ? output[i] / weight[i] : 0;
        }
        return result;
    }
}
