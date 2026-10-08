using System.Diagnostics;
using SherpaOnnx;
using TextreamWindows.Speech.Audio;
using TextreamWindows.Speech.Models;

namespace TextreamWindows.Lab;

/// <summary>辨識出的一句（句尾偵測切開的一段）。時間單位是秒，從音訊開頭算起。</summary>
public sealed record Segment(int Index, double Start, string Text, string[] Tokens, double[] TokenTimes);

/// <summary>
/// 把音訊一小段一小段餵給 sherpa-onnx，處理句尾偵測與每句的時間位移。
/// 這是 Lab 用的簡易版，正式的 SpeechEngine 在第 4 階段做。
/// </summary>
public sealed class StreamingSession : IDisposable
{
    private readonly OnlineRecognizer _recognizer;
    private OnlineStream _stream;
    private readonly Stopwatch _decodeClock = new();
    private readonly List<Segment> _finished = [];
    private double _segmentStart;
    private string _lastText = "";

    public StreamingSession(SpeechModel model, int numThreads, bool enableEndpoint = true)
    {
        Model = model;
        _recognizer = new OnlineRecognizer(model.ToRecognizerConfig(numThreads, enableEndpoint));
        _stream = _recognizer.CreateStream();
    }

    public SpeechModel Model { get; }

    /// <summary>已經餵進去的音訊長度（秒）。</summary>
    public double FedSeconds { get; private set; }

    /// <summary>花在 Decode 上的累計時間，算 RTF 用。</summary>
    public TimeSpan DecodeTime => _decodeClock.Elapsed;

    public IReadOnlyList<Segment> Finished => _finished;

    /// <summary>目前這一句還沒講完的辨識結果。</summary>
    public Segment Current { get; private set; } = new(0, 0, "", [], []);

    /// <summary>每次解碼後觸發，帶目前這一句的最新結果（含已經講完、剛被切開的那句）。</summary>
    public event Action<Segment>? Updated;

    /// <summary>偵測到一句講完時觸發。</summary>
    public event Action<Segment>? SegmentFinished;

    public string Transcript => string.Join("", _finished.Select(s => s.Text)) + Current.Text;

    public void Feed(float[] samples)
    {
        _stream.AcceptWaveform(AudioFile.SampleRate, samples);
        FedSeconds += samples.Length / (double)AudioFile.SampleRate;
        DecodeAvailable();
    }

    /// <summary>音訊結束：補一小段靜音把最後幾個字推出來，再收尾。</summary>
    public void Finish()
    {
        Feed(new float[(int)(AudioFile.SampleRate * 0.66)]);
        _stream.InputFinished();
        DecodeAvailable();
        if (Current.Text.Length > 0)
        {
            CloseSegment();
            Current = new Segment(_finished.Count, FedSeconds, "", [], []);
        }
    }

    private void DecodeAvailable()
    {
        while (_recognizer.IsReady(_stream))
        {
            _decodeClock.Start();
            _recognizer.Decode(_stream);
            _decodeClock.Stop();
        }

        var result = _recognizer.GetResult(_stream);
        Current = new Segment(
            _finished.Count,
            _segmentStart,
            result.Text,
            result.Tokens,
            // sherpa-onnx 的時間戳記從這一句開頭（上次 Reset）起算，換成從音訊開頭起算
            result.Timestamps.Select(t => _segmentStart + t).ToArray());
        if (result.Text != _lastText)
        {
            _lastText = result.Text;
            Updated?.Invoke(Current);
        }

        if (_recognizer.IsEndpoint(_stream))
        {
            if (Current.Text.Length > 0)
            {
                CloseSegment();
            }
            _recognizer.Reset(_stream);
            _segmentStart = FedSeconds;
            _lastText = "";
            Current = new Segment(_finished.Count, _segmentStart, "", [], []);
        }
    }

    private void CloseSegment()
    {
        _finished.Add(Current);
        SegmentFinished?.Invoke(Current);
    }

    public void Dispose()
    {
        _stream.Dispose();
        _recognizer.Dispose();
    }
}
