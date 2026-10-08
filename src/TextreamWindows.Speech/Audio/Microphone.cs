using System.Runtime.Versioning;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace TextreamWindows.Speech.Audio;

public sealed record MicrophoneInfo(int Index, string Id, string Name, bool IsDefault, string Format)
{
    /// <summary>名稱看起來像虛擬音效卡（例如變聲器），不該被當成預設麥克風。</summary>
    public bool LooksVirtual => Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Windows 的麥克風（WASAPI 共用模式）。</summary>
[SupportedOSPlatform("windows")]
public static class Microphone
{
    public static IReadOnlyList<MicrophoneInfo> List()
    {
        using var enumerator = new MMDeviceEnumerator();
        var defaultId = enumerator.TryGetDefaultAudioEndpoint(DataFlow.Capture, Role.Console, out var def) ? def.ID : null;
        def?.Dispose();

        var result = new List<MicrophoneInfo>();
        using var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
        foreach (var device in devices)
        {
            using (device)
            {
                string format;
                try
                {
                    using var client = device.CreateAudioClient();
                    var f = client.MixFormat;
                    format = $"{f.SampleRate} Hz、{f.Channels} 聲道";
                }
                catch (Exception ex)
                {
                    format = $"讀不到格式（{ex.GetType().Name}）";
                }
                result.Add(new MicrophoneInfo(result.Count, device.ID, device.FriendlyName, device.ID == defaultId, format));
            }
        }
        return result;
    }

    /// <summary>
    /// 依編號或名稱片段挑麥克風；沒指定就用 Windows 預設的錄音裝置。
    /// </summary>
    public static MicrophoneInfo Find(string? selector)
    {
        var all = List();
        if (all.Count == 0)
        {
            throw new InvalidOperationException("找不到任何啟用中的麥克風。");
        }
        if (string.IsNullOrWhiteSpace(selector))
        {
            return all.FirstOrDefault(m => m.IsDefault) ?? all[0];
        }
        if (int.TryParse(selector, out var index))
        {
            return all.FirstOrDefault(m => m.Index == index)
                ?? throw new ArgumentException($"沒有編號 {index} 的麥克風，用 devices 指令看清單。");
        }
        var matches = all.Where(m => m.Name.Contains(selector, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new ArgumentException($"沒有名稱含「{selector}」的麥克風，用 devices 指令看清單。"),
            _ => throw new ArgumentException($"名稱含「{selector}」的麥克風有 {matches.Count} 個，請改用編號。"),
        };
    }
}

/// <summary>
/// 開麥克風，持續送出 16 kHz 單聲道 float。事件在 WASAPI 的背景執行緒觸發。
/// <see cref="AudioChunk.Time"/> 用已送出的樣本數換算，跟 <see cref="WavFileSource"/> 一樣是音訊時間。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MicrophoneCapture : IAudioSource
{
    private readonly WasapiRecorder _recorder;
    private readonly BufferedWaveProvider _buffer;
    private readonly ISampleProvider _converted;
    private readonly float[] _out = new float[AudioFile.SampleRate];

    public MicrophoneCapture(MicrophoneInfo microphone, int bufferMilliseconds = 50)
    {
        using var enumerator = new MMDeviceEnumerator();
        _recorder = new WasapiRecorderBuilder()
            .WithDevice(enumerator.GetDevice(microphone.Id))
            .WithSharedMode()
            .WithEventSync()
            .WithBufferLength(bufferMilliseconds)
            .Build();
        // 用裝置原本的格式收，再自己轉成 16 kHz 單聲道，跟讀 WAV 檔走同一條轉換路徑
        _buffer = new BufferedWaveProvider(_recorder.WaveFormat, TimeSpan.FromSeconds(5))
        {
            ReadFully = false, // 沒資料時回傳 0，不要補靜音
            DiscardOnBufferOverflow = true,
        };
        _converted = AudioFile.To16kMono(_buffer.ToSampleProvider());
        _recorder.DataAvailable += OnDataAvailable;
        _recorder.RecordingStopped += (_, e) => Stopped?.Invoke(e.Exception);
    }

    private long _emittedSamples;

    /// <summary>新的一批 16 kHz 單聲道樣本。陣列每次都是新的，可以留著用。</summary>
    public event Action<AudioChunk>? ChunkAvailable;

    /// <summary>錄音停止；有錯誤時帶例外（例如麥克風被拔掉）。</summary>
    public event Action<Exception?>? Stopped;

    public string SourceFormat => $"{_recorder.WaveFormat.SampleRate} Hz、{_recorder.WaveFormat.Channels} 聲道、{_recorder.WaveFormat.Encoding}";

    public void Start() => _recorder.StartRecording();

    public void Stop() => _recorder.StopRecording();

    private void OnDataAvailable(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        _buffer.AddSamples(buffer);
        var collected = new List<float>();
        int read;
        while ((read = _converted.Read(_out)) > 0)
        {
            collected.AddRange(_out.AsSpan(0, read));
        }
        if (collected.Count > 0)
        {
            var time = _emittedSamples / (double)AudioFile.SampleRate;
            _emittedSamples += collected.Count;
            ChunkAvailable?.Invoke(new AudioChunk(collected.ToArray(), time));
        }
    }

    public void Dispose()
    {
        _recorder.DataAvailable -= OnDataAvailable;
        _recorder.Dispose();
    }
}
