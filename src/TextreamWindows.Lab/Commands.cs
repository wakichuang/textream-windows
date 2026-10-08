using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using TextreamWindows.Speech.Analysis;
using TextreamWindows.Speech.Audio;
using TextreamWindows.Speech.Models;

namespace TextreamWindows.Lab;

public static class Commands
{
    private const int DefaultThreads = 2;

    // ── devices ────────────────────────────────────────────────────────────

    public static int Devices(CommandLine cl)
    {
        cl.RejectUnknown();
        var list = Microphone.List();
        if (list.Count == 0)
        {
            Console.WriteLine("找不到任何啟用中的麥克風。");
            return 1;
        }
        Console.WriteLine("編號  預設  名稱（格式）");
        foreach (var m in list)
        {
            var mark = m.IsDefault ? " *  " : "    ";
            var note = m.LooksVirtual ? "  ← 虛擬裝置，不要拿來錄音" : "";
            Console.WriteLine($"{m.Index,4}  {mark}  {m.Name}（{m.Format}）{note}");
        }
        Console.WriteLine();
        Console.WriteLine("其他指令用 --device <編號或名稱片段> 指定，例如 --device Focusrite");
        return 0;
    }

    private static MicrophoneInfo PickMicrophone(CommandLine cl)
    {
        var mic = Microphone.Find(cl.Get("device"));
        Console.WriteLine($"麥克風：{mic.Name}");
        if (mic.LooksVirtual)
        {
            Console.Error.WriteLine("警告：這看起來是虛擬音效裝置，不是真的麥克風。用 --device 指定，devices 指令看清單。");
        }
        return mic;
    }

    // ── record ─────────────────────────────────────────────────────────────

    public static int Record(CommandLine cl)
    {
        var path = Path.GetFullPath(cl.Arg(0, "檔名.wav"));
        var seconds = cl.Double("seconds", 0);
        var force = cl.Flag("force");
        var mic = PickMicrophone(cl);
        cl.RejectUnknown();

        if (File.Exists(path) && !force)
        {
            Console.Error.WriteLine($"{path} 已經存在。要覆蓋請加 --force（黃金錄音別輕易覆蓋）。");
            return 2;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var capture = new MicrophoneCapture(mic);
        using var writer = AudioFile.Create16kMonoWriter(path);
        var gate = new object();
        long total = 0;
        float peak = 0, windowPeak = 0;
        var stopped = new ManualResetEventSlim();
        Exception? failure = null;

        capture.ChunkAvailable += chunk =>
        {
            var s = chunk.Samples;
            lock (gate)
            {
                writer.WriteSamples(s, 0, s.Length);
                total += s.Length;
                foreach (var x in s)
                {
                    var a = Math.Abs(x);
                    peak = Math.Max(peak, a);
                    windowPeak = Math.Max(windowPeak, a);
                }
            }
        };
        capture.Stopped += ex => { failure = ex; stopped.Set(); };
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; capture.Stop(); };

        Console.WriteLine($"來源格式：{capture.SourceFormat} → 存成 16 kHz 單聲道 16-bit");
        Console.WriteLine(seconds > 0 ? $"開始錄音 {seconds} 秒，Ctrl+C 可提早結束。" : "開始錄音，按 Ctrl+C 結束。");
        capture.Start();
        while (!stopped.Wait(250))
        {
            double elapsed;
            float level;
            lock (gate)
            {
                elapsed = total / (double)AudioFile.SampleRate;
                level = windowPeak;
                windowPeak = 0;
            }
            var bar = new string('█', (int)Math.Round(Math.Min(1, level) * 30)).PadRight(30, '·');
            var limit = seconds > 0 ? $" / {seconds:0}" : "";
            Console.Write($"\r  {elapsed,6:0.0}{limit} 秒  [{bar}]");
            if (seconds > 0 && elapsed >= seconds)
            {
                capture.Stop();
            }
        }
        Console.WriteLine();

        if (failure is not null)
        {
            Console.Error.WriteLine($"錄音中斷：{failure.Message}");
            return 1;
        }
        Console.WriteLine($"已存：{path}（{total / (double)AudioFile.SampleRate:0.0} 秒）");
        if (peak >= 0.99f)
        {
            Console.Error.WriteLine("警告：有爆音（峰值碰頂），把介面卡的增益調低一點再錄。");
        }
        else if (peak < 0.05f)
        {
            Console.Error.WriteLine($"警告：音量很小（峰值 {peak:0.000}），確認選對麥克風、增益夠不夠。");
        }
        return 0;
    }

    // ── transcribe ─────────────────────────────────────────────────────────

    public static int Transcribe(CommandLine cl)
    {
        var model = SpeechModel.Resolve(cl.Arg(0, "模型"), preferFp32: cl.Flag("fp32"));
        var wav = cl.Arg(1, "音檔.wav");
        var threads = cl.Int("threads", DefaultThreads);
        cl.RejectUnknown();

        var samples = AudioFile.Load16kMono(wav);
        var duration = samples.Length / (double)AudioFile.SampleRate;
        Console.WriteLine($"模型：{model}");
        Console.WriteLine($"音檔：{wav}（{duration:0.00} 秒）");

        using var session = new StreamingSession(model, threads);
        session.SegmentFinished += s =>
        {
            Console.WriteLine();
            Console.WriteLine($"── 第 {s.Index + 1} 句（從 {s.Start:0.00} 秒起）");
            Console.WriteLine(s.Text);
            if (s.TokenTimes.Length == s.Tokens.Length && s.Tokens.Length > 0)
            {
                Console.WriteLine(string.Join(" ", s.Tokens.Zip(s.TokenTimes, (t, at) => $"{t}@{at:0.00}")));
            }
            else
            {
                Console.WriteLine("（這個模型不提供每個字的時間）");
            }
        };

        var chunk = AudioFile.SampleRate / 10;
        for (var offset = 0; offset < samples.Length; offset += chunk)
        {
            session.Feed(samples[offset..Math.Min(offset + chunk, samples.Length)]);
        }
        session.Finish();

        Console.WriteLine();
        Console.WriteLine($"全文：{session.Transcript}");
        Console.WriteLine($"解碼 {session.DecodeTime.TotalSeconds:0.00} 秒，RTF {session.DecodeTime.TotalSeconds / duration:0.000}（{threads} 執行緒）");
        return 0;
    }

    // ── live ───────────────────────────────────────────────────────────────

    public static int Live(CommandLine cl)
    {
        var model = SpeechModel.Resolve(cl.Arg(0, "模型"), preferFp32: cl.Flag("fp32"));
        var threads = cl.Int("threads", DefaultThreads);
        var seconds = cl.Double("seconds", 0);
        var mic = PickMicrophone(cl);
        cl.RejectUnknown();

        Console.WriteLine($"模型：{model}");
        using var session = new StreamingSession(model, threads);
        using var capture = new MicrophoneCapture(mic);
        var queue = new BlockingCollection<float[]>();
        Exception? failure = null;
        capture.ChunkAvailable += chunk => queue.Add(chunk.Samples);
        capture.Stopped += ex => { failure = ex; queue.CompleteAdding(); };
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; capture.Stop(); };

        var width = ConsoleWidth();
        session.Updated += s => Console.Write("\r" + FitRight("> " + s.Text, width));
        session.SegmentFinished += s => Console.WriteLine("\r" + FitRight("✓ " + s.Text, width));

        Console.WriteLine(seconds > 0 ? $"對著麥克風講話，{seconds:0} 秒後自動結束（Ctrl+C 可提早）。" : "對著麥克風講話，Ctrl+C 結束。");
        Console.WriteLine();
        var clock = Stopwatch.StartNew();
        capture.Start();
        foreach (var samples in queue.GetConsumingEnumerable())
        {
            session.Feed(samples);
            if (seconds > 0 && clock.Elapsed.TotalSeconds >= seconds)
            {
                capture.Stop();
            }
        }
        session.Finish();
        Console.WriteLine();

        if (failure is not null)
        {
            Console.Error.WriteLine($"麥克風中斷：{failure.Message}");
            return 1;
        }
        var cpuShare = session.DecodeTime.TotalSeconds / Math.Max(0.001, session.FedSeconds);
        Console.WriteLine($"講了 {session.FedSeconds:0.0} 秒，RTF {cpuShare:0.000}");
        return 0;
    }

    private static int ConsoleWidth()
    {
        try
        {
            return Math.Max(40, Console.WindowWidth);
        }
        catch (IOException)
        {
            return 100;
        }
    }

    /// <summary>只留最後能放進一行的部分，再補空白蓋掉上一次的字。中日韓文字佔兩格。</summary>
    private static string FitRight(string text, int width)
    {
        static int W(char c) => c >= '⺀' ? 2 : 1;
        var max = width - 2;
        var start = text.Length;
        var used = 0;
        while (start > 0 && used + W(text[start - 1]) <= max)
        {
            used += W(text[--start]);
        }
        return text[start..] + new string(' ', max - used);
    }

    // ── bench ──────────────────────────────────────────────────────────────

    public static int Bench(CommandLine cl)
    {
        var model = SpeechModel.Resolve(cl.Arg(0, "模型"), preferFp32: cl.Flag("fp32"));
        var wav = cl.Arg(1, "音檔.wav");
        var threads = cl.Int("threads", DefaultThreads);
        var chunkMs = cl.Int("chunk-ms", 50);
        var fast = cl.Flag("fast");
        var jsonPath = cl.Get("json");
        var from = cl.Double("from", 0);
        var to = cl.Double("to", 0);
        cl.RejectUnknown();

        var samples = AudioFile.Load16kMono(wav);
        if (from > 0 || to > 0)
        {
            // 只量其中一段：長的 Podcast 照真實速度跑完整集太久
            var start = Math.Min(samples.Length, (int)(from * AudioFile.SampleRate));
            var end = to > 0 ? Math.Min(samples.Length, (int)(to * AudioFile.SampleRate)) : samples.Length;
            if (end <= start)
            {
                throw new ArgumentException($"--from {from} 到 --to {to} 之間沒有聲音（音檔長 {samples.Length / (double)AudioFile.SampleRate:0.0} 秒）");
            }
            samples = samples[start..end];
            Console.WriteLine($"只量 {from:0.0}～{end / (double)AudioFile.SampleRate:0.0} 秒這一段");
        }
        var duration = samples.Length / (double)AudioFile.SampleRate;
        var span = SpeechSpan.Find(samples, AudioFile.SampleRate);
        if (span is null)
        {
            Console.Error.WriteLine("這個音檔聽起來是一片安靜，沒辦法量。");
            return 1;
        }
        Console.WriteLine($"模型：{model}");
        Console.WriteLine($"音檔：{wav}（{duration:0.00} 秒，有聲段 {span.Value.Onset:0.00}～{span.Value.Offset:0.00} 秒）");
        Console.WriteLine(fast
            ? "模式：全速（延遲只算演算法本身，不含運算時間）"
            : $"模式：照真實速度播放，每 {chunkMs} ms 送一次（約需 {duration + 1:0} 秒）");

        using var session = new StreamingSession(model, threads);
        var wall = new Stopwatch();
        double Now() => fast ? session.FedSeconds : wall.Elapsed.TotalSeconds;

        // 每個字（第幾句、第幾個 token）第一次出現在辨識結果裡的時間
        var firstSeen = new Dictionary<(int Segment, int Token), double>();
        double? firstToken = null;
        session.Updated += s =>
        {
            var now = Now();
            for (var i = 0; i < s.Tokens.Length; i++)
            {
                firstSeen.TryAdd((s.Index, i), now);
            }
            if (s.Tokens.Length > 0)
            {
                firstToken ??= now;
            }
        };

        var chunk = AudioFile.SampleRate * chunkMs / 1000;
        var withTail = samples.Concat(new float[AudioFile.SampleRate]).ToArray(); // 講完後再給 1 秒安靜，像真的麥克風
        var process = Process.GetCurrentProcess();
        var cpuStart = process.TotalProcessorTime;
        wall.Start();
        for (var offset = 0; offset < withTail.Length; offset += chunk)
        {
            var end = Math.Min(offset + chunk, withTail.Length);
            if (!fast)
            {
                // 這一段聲音要等它「講完」才拿得到，跟麥克風一樣
                var wait = end / (double)AudioFile.SampleRate - wall.Elapsed.TotalSeconds;
                if (wait > 0)
                {
                    Thread.Sleep(TimeSpan.FromSeconds(wait));
                }
            }
            session.Feed(withTail[offset..end]);
        }
        session.Finish();
        wall.Stop();
        process.Refresh();
        var cpuSeconds = (process.TotalProcessorTime - cpuStart).TotalSeconds;

        var segments = session.Finished;
        var follow = new List<double>();
        foreach (var s in segments)
        {
            for (var i = 0; i < Math.Min(s.Tokens.Length, s.TokenTimes.Length); i++)
            {
                if (firstSeen.TryGetValue((s.Index, i), out var seen))
                {
                    follow.Add(seen - s.TokenTimes[i]);
                }
            }
        }
        var last = segments.LastOrDefault();
        double? tail = last is { Tokens.Length: > 0 } && firstSeen.TryGetValue((last.Index, last.Tokens.Length - 1), out var lastSeen)
            ? lastSeen - span.Value.Offset
            : null;

        var report = new BenchReport(
            Model: model.Code,
            ModelFiles: model.ToString(),
            Wav: Path.GetFullPath(wav),
            Mode: fast ? "fast" : "realtime",
            Threads: threads,
            ChunkMs: chunkMs,
            AudioSeconds: Math.Round(duration, 3),
            SpeechOnset: span.Value.Onset,
            SpeechOffset: span.Value.Offset,
            FirstTokenLatency: Round(firstToken - span.Value.Onset),
            FollowLatencyMedian: Round(Stats.Median(follow)),
            FollowLatencyP90: Round(Percentile(follow, 0.9)),
            FollowLatencySamples: follow.Count,
            TailLatency: Round(tail),
            Rtf: Math.Round(session.DecodeTime.TotalSeconds / session.FedSeconds, 4),
            CpuPercentOfMachine: fast ? null : Math.Round(cpuSeconds / wall.Elapsed.TotalSeconds / Environment.ProcessorCount * 100, 1),
            CpuCores: fast ? null : Math.Round(cpuSeconds / wall.Elapsed.TotalSeconds, 2),
            Transcript: session.Transcript,
            Segments: segments.Select(s => s.Text).ToArray(),
            MeasuredAt: DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture));

        Console.WriteLine();
        Console.WriteLine($"全文：{report.Transcript}");
        Console.WriteLine();
        Console.WriteLine($"首字延遲        {Show(report.FirstTokenLatency)}    （開口到第一個字出現，門檻 ≤ 0.5 秒）");
        Console.WriteLine($"追隨延遲中位數  {Show(report.FollowLatencyMedian)}    （講到某個字到它出現，門檻 ≤ 0.7 秒；{follow.Count} 個字，P90 {Show(report.FollowLatencyP90)}）");
        Console.WriteLine($"句尾延遲        {Show(report.TailLatency)}    （最後一個字的聲音結束到它出現）");
        Console.WriteLine($"RTF             {report.Rtf:0.000}      （處理 1 秒音訊花幾秒，門檻 ≤ 0.3）");
        Console.WriteLine(fast
            ? "CPU             —          （全速模式不量 CPU）"
            : $"CPU             {report.CpuPercentOfMachine:0.0}%      （整台電腦 {Environment.ProcessorCount} 個執行緒的佔比，門檻 ≤ 30%；約等於 {report.CpuCores:0.00} 個核心）");
        if (follow.Count == 0)
        {
            Console.WriteLine("註：這個模型不提供每個字的時間，追隨延遲量不到，看首字延遲與句尾延遲。");
        }

        if (jsonPath is not null)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 中文直接存，不要變成 \uXXXX
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, options));
            Console.WriteLine($"已存：{jsonPath}");
        }
        return 0;
    }

    private static double? Round(double? v) => v is null ? null : Math.Round(v.Value, 3);

    private static string Show(double? seconds) => seconds is null ? "—      " : $"{seconds:0.000} 秒";

    private static double? Percentile(List<double> values, double p)
    {
        if (values.Count == 0)
        {
            return null;
        }
        var sorted = values.Order().ToArray();
        return sorted[(int)Math.Min(sorted.Length - 1, Math.Ceiling(p * sorted.Length) - 1)];
    }
}

public sealed record BenchReport(
    string Model,
    string ModelFiles,
    string Wav,
    string Mode,
    int Threads,
    int ChunkMs,
    double AudioSeconds,
    double SpeechOnset,
    double SpeechOffset,
    double? FirstTokenLatency,
    double? FollowLatencyMedian,
    double? FollowLatencyP90,
    int FollowLatencySamples,
    double? TailLatency,
    double Rtf,
    double? CpuPercentOfMachine,
    double? CpuCores,
    string Transcript,
    string[] Segments,
    string MeasuredAt);
