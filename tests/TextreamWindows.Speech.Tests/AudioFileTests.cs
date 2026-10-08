using NAudio.Wave;
using TextreamWindows.Speech.Audio;

namespace TextreamWindows.Speech.Tests;

public sealed class AudioFileTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("textream-audio-");

    public void Dispose() => _root.Delete(recursive: true);

    private string Path_(string name) => Path.Combine(_root.FullName, name);

    [Fact]
    public void Mono16kFileLoadsUnchanged()
    {
        var path = Path_("mono16k.wav");
        var samples = Enumerable.Range(0, 1600).Select(i => (float)Math.Sin(i * 0.05) * 0.5f).ToArray();
        AudioFile.Save16kMono(path, samples);

        var loaded = AudioFile.Load16kMono(path);

        Assert.Equal(samples.Length, loaded.Length);
        for (var i = 0; i < samples.Length; i++)
        {
            // 16-bit 量化誤差最多 1/32768 ≈ 3e-5。不用 precision 參數：它是先四捨五入再比，邊界上會誤判
            Assert.Equal(samples[i], loaded[i], 1e-4f);
        }
    }

    [Fact]
    public void SavedFileIs16kMono16Bit()
    {
        var path = Path_("fmt.wav");
        AudioFile.Save16kMono(path, new float[160]);

        using var reader = new WaveFileReader(path);

        Assert.Equal(16000, reader.WaveFormat.SampleRate);
        Assert.Equal(1, reader.WaveFormat.Channels);
        Assert.Equal(16, reader.WaveFormat.BitsPerSample);
    }

    [Fact]
    public void Stereo48kIsAveragedAndResampledTo16k()
    {
        // 左聲道 0.6、右聲道 0.2 的直流訊號，一秒
        var path = Path_("stereo48k.wav");
        using (var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)))
        {
            for (var i = 0; i < 48000; i++)
            {
                writer.WriteSample(0.6f);
                writer.WriteSample(0.2f);
            }
        }

        var loaded = AudioFile.Load16kMono(path);

        Assert.InRange(loaded.Length, 15900, 16100);
        // 重新取樣的濾波器在開頭有暫態，取中段檢查
        Assert.Equal(0.4f, loaded[8000], 2);
    }
}
