using System.Collections.Concurrent;
using SherpaOnnx;
using TextreamWindows.Speech.Audio;
using TextreamWindows.Speech.Models;

namespace TextreamWindows.Speech.Recognition;

/// <summary>
/// sherpa-onnx 串流辨識（計畫書第 4 階段步驟 2）：<see cref="Accept"/> 只把音訊排進佇列，解碼在專用的背景執行緒；
/// 偵測到句尾就發 <see cref="EndOfUtterance"/> 並 Reset 串流（第 6.2 節）。
/// </summary>
/// <remarks>Lab 的 <c>StreamingSession</c> 是第 2 階段量測用的同步簡易版，會另外留著給 bench 量時間。</remarks>
public sealed class SpeechEngine : ISpeechEngine
{
    /// <summary>收尾時補的靜音長度：串流模型要再聽一小段才會把最後幾個字吐出來（跟 Lab 一樣）。</summary>
    private const double TailPaddingSeconds = 0.66;

    private readonly OnlineRecognizer _recognizer;
    private readonly OnlineStream _stream;
    private readonly BlockingCollection<AudioChunk> _queue = new();
    private readonly Task _worker;
    private string _lastText = "";
    private double _heard;
    private volatile bool _disposing;

    public SpeechEngine(SpeechModel model, int numThreads = 2)
    {
        Model = model;
        _recognizer = new OnlineRecognizer(model.ToRecognizerConfig(numThreads, enableEndpoint: true));
        _stream = _recognizer.CreateStream();
        _worker = Task.Factory.StartNew(DecodeLoop, TaskCreationOptions.LongRunning);
    }

    public SpeechModel Model { get; }

    public event Action<RecognitionResult>? PartialResult;

    public event Action<RecognitionResult>? EndOfUtterance;

    public Task Completion => _worker;

    public void Accept(AudioChunk chunk)
    {
        if (!_queue.IsAddingCompleted)
        {
            _queue.Add(chunk);
        }
    }

    public void Complete() => _queue.CompleteAdding();

    private void DecodeLoop()
    {
        foreach (var chunk in _queue.GetConsumingEnumerable())
        {
            if (_disposing)
            {
                return; // 要釋放了：佇列裡沒解碼的音訊直接丟掉，不收尾
            }
            _stream.AcceptWaveform(AudioFile.SampleRate, chunk.Samples);
            _heard = chunk.End;
            DecodeAvailable();
        }

        if (_heard > 0 && !_disposing)
        {
            var padding = new float[(int)(AudioFile.SampleRate * TailPaddingSeconds)];
            _stream.AcceptWaveform(AudioFile.SampleRate, padding);
            _heard += TailPaddingSeconds;
            _stream.InputFinished();
            DecodeAvailable();
            if (_lastText.Length > 0)
            {
                EndOfUtterance?.Invoke(new RecognitionResult(_lastText, _heard));
            }
        }
    }

    private void DecodeAvailable()
    {
        while (_recognizer.IsReady(_stream))
        {
            _recognizer.Decode(_stream);
        }

        var text = _recognizer.GetResult(_stream).Text;
        if (text != _lastText)
        {
            _lastText = text;
            PartialResult?.Invoke(new RecognitionResult(text, _heard));
        }

        if (_recognizer.IsEndpoint(_stream))
        {
            if (_lastText.Length > 0)
            {
                EndOfUtterance?.Invoke(new RecognitionResult(_lastText, _heard));
            }
            _recognizer.Reset(_stream);
            _lastText = "";
        }
    }

    /// <summary>
    /// 先叫解碼執行緒丟掉沒解碼的音訊，等它真的停了才釋放原生物件。
    /// 原本最多等 5 秒就釋放，佇列還很長（電腦正忙）時解碼執行緒會用到已釋放的物件，整個程式當掉（2026-10-08 測試主機 Fatal error）。
    /// </summary>
    public void Dispose()
    {
        _disposing = true;
        _queue.CompleteAdding();
        _worker.Wait(); // 最多再解碼一批（50 毫秒的音訊）
        _stream.Dispose();
        _recognizer.Dispose();
        _queue.Dispose();
    }
}
