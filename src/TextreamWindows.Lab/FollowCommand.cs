using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using NAudio.Wave;
using TextreamWindows.Core.Session;
using TextreamWindows.Core.Text;
using TextreamWindows.Speech.Audio;
using TextreamWindows.Speech.Follow;
using TextreamWindows.Speech.Models;
using TextreamWindows.Speech.Recognition;

namespace TextreamWindows.Lab;

/// <summary>
/// follow：對著麥克風念講稿，終端機即時顯示讀到哪裡（計畫書第 4.3 步，瓦基第一次看到「跟著聲音走」）。
/// 走的是正式的 FollowPipeline，不是 Lab 的簡易版。
/// </summary>
public static class FollowCommand
{
    private sealed record LogEntry(double Seconds, int Progress, int Readable, string Heard);

    public static int Run(CommandLine cl)
    {
        var scriptPath = Path.GetFullPath(cl.Arg(0, "講稿.txt"));
        var model = SpeechModel.Resolve(cl.Get("model") ?? "A", preferFp32: !cl.Flag("int8")); // 第 1 節：預設 A 的 fp32
        var threads = cl.Int("threads", 2);
        var seconds = cl.Double("seconds", 0);
        var wav = cl.Get("wav");
        var speed = cl.Double("speed", 1);
        var recordPath = cl.Get("record") is { } r ? Path.GetFullPath(r) : null;
        var logPath = cl.Get("log") is { } l ? Path.GetFullPath(l) : null;
        var showRaw = cl.Flag("raw");
        var language = cl.Get("lang") switch
        {
            null or "zh" => SpeechLanguage.TraditionalChinese,
            "en" => SpeechLanguage.English,
            var other => throw new ArgumentException($"--lang 只能是 zh 或 en，收到「{other}」"),
        };
        var mic = wav is null ? Microphone.Find(cl.Get("device")) : null;
        cl.RejectUnknown();

        var prompt = new PromptScript(File.ReadAllText(scriptPath));
        if (prompt.CharacterCount == 0)
        {
            Console.Error.WriteLine($"{scriptPath} 是空的。");
            return 2;
        }
        var totalReadable = Readable(prompt, prompt.CharacterCount);
        Console.WriteLine($"講稿：{Path.GetFileName(scriptPath)}（{totalReadable} 個字）");
        Console.WriteLine($"模型：{model}");
        Console.WriteLine($"辨識語言：{(language == SpeechLanguage.English ? "English" : "繁體中文")}");
        Console.WriteLine(mic is not null ? $"麥克風：{mic.Name}" : $"錄音檔：{wav}（{speed:0.##} 倍速）");

        IAudioSource source = mic is not null ? new MicrophoneCapture(mic) : WavFileSource.FromFile(Path.GetFullPath(wav!), speed);
        using var _ = source;
        using var engine = new SpeechEngine(model, threads);
        using var pipeline = new FollowPipeline(source, engine, new PromptSession(prompt, FollowMode.WordTracking, language: language));

        var gate = new object();
        var heard = "";
        var log = new List<LogEntry>();
        var clock = new Stopwatch();
        engine.PartialResult += result => { lock (gate) { heard = result.Text; } };
        pipeline.ProgressChanged += progress =>
        {
            lock (gate)
            {
                log.Add(new LogEntry(Math.Round(clock.Elapsed.TotalSeconds, 2), progress, Readable(prompt, progress), heard));
            }
        };

        WaveFileWriter? recorder = recordPath is null ? null : AudioFile.Create16kMonoWriter(recordPath);
        if (recorder is not null)
        {
            source.ChunkAvailable += chunk => { lock (gate) { recorder.WriteSamples(chunk.Samples, 0, chunk.Samples.Length); } };
        }

        Console.CancelKeyPress += (_, e) => { e.Cancel = true; pipeline.Stop(); };
        var ansi = FollowView.TryEnableAnsi();
        var view = new FollowView(prompt);
        var width = ConsoleWidth();
        Console.WriteLine(seconds > 0 ? $"開始念稿，{seconds:0} 秒後自動結束（Ctrl+C 可提早）。" : "開始念稿，念完自動結束，Ctrl+C 可提早。");
        Console.WriteLine();

        clock.Start();
        pipeline.Start();
        var lines = showRaw ? 3 : 2;
        var lastPlain = -1;
        var finishedAt = TimeSpan.Zero;
        while (!pipeline.Completion.Wait(100))
        {
            var progress = pipeline.EffectiveCharacterCount;
            string raw;
            lock (gate)
            {
                raw = heard;
            }
            var status = $"{(pipeline.IsSpeaking ? "● 講話中" : "○ 安靜  ")}  {Readable(prompt, progress)}／{totalReadable} 字（{(double)Readable(prompt, progress) / totalReadable:P0}）  {clock.Elapsed:mm\\:ss}";
            if (ansi)
            {
                var frame = new List<string> { view.Frame(progress, width - 2, ansi: true), "\u001b[90m" + status + "\u001b[0m" };
                if (showRaw)
                {
                    frame.Add("\u001b[90m聽到：" + Tail(raw, width - 8) + "\u001b[0m");
                }
                if (lastPlain >= 0)
                {
                    Console.Write($"\u001b[{lines}F");
                }
                foreach (var line in frame)
                {
                    Console.Write("\u001b[2K" + line + "\n");
                }
                lastPlain = progress;
            }
            else if (progress != lastPlain)
            {
                Console.WriteLine($"{clock.Elapsed.TotalSeconds,6:0.0} 秒 {(double)Readable(prompt, progress) / totalReadable,4:P0}  {view.Frame(progress, width - 22, ansi: false)}");
                lastPlain = progress;
            }

            if (progress >= prompt.CharacterCount && finishedAt == TimeSpan.Zero)
            {
                finishedAt = clock.Elapsed;
                if (mic is not null)
                {
                    pipeline.Stop(); // 讀完了，麥克風不用再開著
                }
            }
            if (seconds > 0 && clock.Elapsed.TotalSeconds >= seconds)
            {
                pipeline.Stop();
            }
        }

        recorder?.Dispose();
        var final = pipeline.EffectiveCharacterCount;
        Console.WriteLine();
        Console.WriteLine(final >= prompt.CharacterCount
            ? $"讀完了：{finishedAt:mm\\:ss} 追到講稿結尾。"
            : $"停在 {Readable(prompt, final)}／{totalReadable} 字（{(double)Readable(prompt, final) / totalReadable:P0}），「{Around(prompt, final)}」附近。");
        if (recordPath is not null)
        {
            Console.WriteLine($"錄音存在：{recordPath}（不要放進 repo；要當黃金錄音就移到 TEXTREAM_GOLDEN_DIR 指的資料夾）");
        }
        if (logPath is not null)
        {
            var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            File.WriteAllText(logPath, JsonSerializer.Serialize(log, options));
            Console.WriteLine($"過程紀錄：{logPath}");
        }
        return 0;
    }

    private static int Readable(PromptScript prompt, int offset) => prompt.Elements.Take(offset).Count(TextElements.IsLetterOrNumber);

    private static string Around(PromptScript prompt, int offset) =>
        string.Concat(prompt.Elements.Skip(Math.Max(0, offset - 6)).Take(14)).Replace(" ", "");

    private static string Tail(string text, int columns)
    {
        var elements = TextElements.Split(text);
        var used = 0;
        var start = elements.Length;
        while (start > 0 && used + FollowView.Columns(elements[start - 1]) <= columns)
        {
            used += FollowView.Columns(elements[--start]);
        }
        return string.Concat(elements[start..]);
    }

    private static int ConsoleWidth()
    {
        try
        {
            return Console.IsOutputRedirected ? 100 : Math.Max(40, Console.WindowWidth);
        }
        catch (IOException)
        {
            return 100;
        }
    }
}
