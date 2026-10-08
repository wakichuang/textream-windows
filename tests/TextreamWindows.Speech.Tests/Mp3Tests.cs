using NAudio.MediaFoundation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using TextreamWindows.Speech.Audio;

namespace TextreamWindows.Speech.Tests;

/// <summary>
/// 這台 Windows 有 Media Foundation 的 MP3 編碼器才跑。
/// GitHub 的 Windows Server 主機不一定裝了媒體功能，沒有就跳過，不算失敗。
/// </summary>
public sealed class Mp3EncoderFactAttribute : FactAttribute
{
    public Mp3EncoderFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "MP3 走 Windows 的 Media Foundation";
            return;
        }
        try
        {
            if (MediaFoundationEncoder.GetOutputMediaTypes(AudioSubtypes.MFAudioFormat_MP3).Length == 0)
            {
                Skip = "這台沒有 Media Foundation 的 MP3 編碼器";
            }
        }
        catch (Exception ex)
        {
            Skip = $"Media Foundation 不可用：{ex.GetType().Name}";
        }
    }
}

public sealed class Mp3Tests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("textream-mp3-");

    public void Dispose() => _root.Delete(recursive: true);

    [Mp3EncoderFact]
    public void Mp3IsDecodedToMono16k()
    {
        // 一秒、44.1 kHz 雙聲道、振幅 0.5 的 440 Hz 正弦波，Podcast 成品常見的格式
        var path = Path.Combine(_root.FullName, "tone.mp3");
        var tone = new SignalGenerator(44100, 2) { Frequency = 440, Gain = 0.5, Type = SignalGeneratorType.Sin }
            .Take(TimeSpan.FromSeconds(1));
        if (OperatingSystem.IsWindows())
        {
            MediaFoundationEncoder.EncodeToMp3(tone.ToWaveProvider16(), path, 128000);
        }

        var loaded = AudioFile.Load16kMono(path);

        // MP3 編碼器會在頭尾補幾十毫秒
        Assert.InRange(loaded.Length, 15500, 17600);
        var middle = loaded.AsSpan(4000, 8000).ToArray();
        var rms = Math.Sqrt(middle.Average(x => (double)x * x));
        Assert.Equal(0.5 / Math.Sqrt(2), rms, 0.03);
    }
}
