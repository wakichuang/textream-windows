using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace TextreamWindows.Speech.Audio;

/// <summary>音檔讀寫。辨識引擎吃的格式一律是 16 kHz 單聲道 float。</summary>
public static class AudioFile
{
    public const int SampleRate = 16000;

    /// <summary>讀 WAV 或 MP3（Podcast 成品），轉成 16 kHz 單聲道 float（多聲道取平均）。</summary>
    public static float[] Load16kMono(string path)
    {
        using var reader = Open(path);
        var source = To16kMono(reader.ToSampleProvider());

        var seconds = reader.Length / (double)reader.WaveFormat.AverageBytesPerSecond;
        var result = new List<float>((int)(seconds * SampleRate) + SampleRate);
        var buffer = new float[SampleRate];
        int read;
        while ((read = source.Read(buffer)) > 0)
        {
            result.AddRange(buffer.AsSpan(0, read));
        }
        return result.ToArray();
    }

    private static WaveStream Open(string path)
    {
        if (Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase))
        {
            return new WaveFileReader(path);
        }
        if (OperatingSystem.IsWindows())
        {
            // MP3、M4A 等交給 Windows 內建的 Media Foundation 解碼
            return new MediaFoundationReader(path);
        }
        throw new NotSupportedException($"這個平台只讀得了 WAV：{path}");
    }

    /// <summary>存成 16 kHz、單聲道、16-bit 的 WAV（黃金錄音的格式，計畫書第 7.4 節）。</summary>
    public static void Save16kMono(string path, float[] samples)
    {
        using var writer = Create16kMonoWriter(path);
        writer.WriteSamples(samples, 0, samples.Length);
    }

    public static WaveFileWriter Create16kMonoWriter(string path) =>
        new(path, new WaveFormat(SampleRate, 16, 1));

    /// <summary>把任意取樣率、聲道數的來源接成 16 kHz 單聲道。</summary>
    public static ISampleProvider To16kMono(ISampleProvider source)
    {
        if (source.WaveFormat.Channels > 1)
        {
            source = new MonoMixSampleProvider(source);
        }
        if (source.WaveFormat.SampleRate != SampleRate)
        {
            source = new WdlResamplingSampleProvider(source, SampleRate);
        }
        return source;
    }
}

/// <summary>把多聲道平均成單聲道。NAudio 內建的 ToMono 只接受雙聲道。</summary>
public sealed class MonoMixSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private float[] _buffer = [];

    public MonoMixSampleProvider(ISampleProvider source)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(Span<float> buffer)
    {
        var needed = buffer.Length * _channels;
        if (_buffer.Length < needed)
        {
            _buffer = new float[needed];
        }
        var read = _source.Read(_buffer.AsSpan(0, needed));
        var frames = read / _channels;
        for (var f = 0; f < frames; f++)
        {
            var sum = 0f;
            for (var c = 0; c < _channels; c++)
            {
                sum += _buffer[f * _channels + c];
            }
            buffer[f] = sum / _channels;
        }
        return frames;
    }
}
