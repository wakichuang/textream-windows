using SherpaOnnx;

namespace TextreamWindows.Speech.Models;

public enum SpeechModelKind { Transducer, Paraformer }

/// <summary>
/// 一個下載好的 sherpa-onnx 串流模型：在哪個資料夾、是哪一種架構、要載入哪幾個檔案。
/// </summary>
public sealed record SpeechModel(
    string Code,
    string Directory,
    SpeechModelKind Kind,
    string Tokens,
    string Encoder,
    string Decoder,
    string? Joiner)
{
    // 跟 scripts/fetch_models.py 的 MODELS 表同一份清單（計畫書第 5.1 節）
    private static readonly Dictionary<string, string> KnownModels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = "sherpa-onnx-streaming-zipformer-bilingual-zh-en-2023-02-20",
        ["B"] = "sherpa-onnx-streaming-paraformer-bilingual-zh-en",
        ["C"] = "sherpa-onnx-streaming-zipformer-zh-int8-2025-06-30",
        ["D"] = "sherpa-onnx-streaming-zipformer-zh-xlarge-int8-2025-06-30",
    };

    /// <summary>
    /// 依序找模型的地方（瓦基 2026-10-08：要做成免安裝版公開給一般人，下載解壓縮就能用）：
    /// 環境變數 TEXTREAM_MODELS_DIR（開發、測試用）→ 執行檔旁邊的 models（免安裝版內附）→ %LOCALAPPDATA%\Textream\models（開發機下載的位置）。
    /// </summary>
    public static IReadOnlyList<string> DefaultSearchRoots
    {
        get
        {
            var roots = new List<string>();
            if (Environment.GetEnvironmentVariable("TEXTREAM_MODELS_DIR") is { Length: > 0 } env)
            {
                roots.Add(env);
            }
            roots.Add(Path.Combine(AppContext.BaseDirectory, "models"));
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Textream", "models"));
            return roots;
        }
    }

    /// <summary>開發機下載模型的位置（<c>scripts/fetch_models.py</c> 預設放這裡），也是 <see cref="DefaultSearchRoots"/> 的最後一站。</summary>
    public static string DefaultModelsRoot => DefaultSearchRoots[^1];

    public string Name => Path.GetFileName(Directory);

    /// <param name="codeOrPath">模型代號（A～D）或模型資料夾路徑</param>
    /// <param name="modelsRoot">只在這個資料夾找；沒給就依 <see cref="DefaultSearchRoots"/> 依序找</param>
    /// <param name="preferFp32">true 時優先載入 fp32 檔，沒有才退回 int8；預設反過來</param>
    public static SpeechModel Resolve(string codeOrPath, string? modelsRoot = null, bool preferFp32 = false) =>
        Resolve(codeOrPath, modelsRoot is null ? DefaultSearchRoots : [modelsRoot], preferFp32);

    /// <summary>依序在 <paramref name="searchRoots"/> 找模型，第一個找得到的勝出。</summary>
    public static SpeechModel Resolve(string codeOrPath, IReadOnlyList<string> searchRoots, bool preferFp32 = false)
    {
        string code;
        string dir;
        if (System.IO.Directory.Exists(codeOrPath))
        {
            dir = Path.GetFullPath(codeOrPath);
            code = Path.GetFileName(dir);
        }
        else if (KnownModels.TryGetValue(codeOrPath, out var name))
        {
            code = codeOrPath.ToUpperInvariant();
            var found = searchRoots.Select(root => Path.Combine(root, name)).FirstOrDefault(d => File.Exists(Path.Combine(d, "tokens.txt")));
            dir = found ?? throw new FileNotFoundException(
                $"找不到模型 {code}（{name}）。找過這些地方：{string.Join("、", searchRoots)}。" +
                $"免安裝版請確認 models 資料夾跟 TextreamWindows.exe 放在一起；開發時請執行：python scripts/fetch_models.py {code}");
        }
        else
        {
            throw new ArgumentException(
                $"不認得的模型「{codeOrPath}」，請給代號（{string.Join("、", KnownModels.Keys)}）或模型資料夾路徑");
        }

        var joiner = PickFile(dir, "joiner", preferFp32);
        return new SpeechModel(
            code,
            dir,
            joiner is null ? SpeechModelKind.Paraformer : SpeechModelKind.Transducer,
            Path.Combine(dir, "tokens.txt"),
            PickFile(dir, "encoder", preferFp32) ?? throw new FileNotFoundException($"{dir} 裡沒有 encoder 的 .onnx 檔"),
            PickFile(dir, "decoder", preferFp32) ?? throw new FileNotFoundException($"{dir} 裡沒有 decoder 的 .onnx 檔"),
            joiner);
    }

    private static string? PickFile(string dir, string role, bool preferFp32)
    {
        var candidates = System.IO.Directory.GetFiles(dir, $"{role}*.onnx");
        var int8 = candidates.Where(f => f.EndsWith(".int8.onnx", StringComparison.OrdinalIgnoreCase)).Order().FirstOrDefault();
        var fp32 = candidates.Where(f => !f.EndsWith(".int8.onnx", StringComparison.OrdinalIgnoreCase)).Order().FirstOrDefault();
        return preferFp32 ? fp32 ?? int8 : int8 ?? fp32;
    }

    /// <summary>組出 sherpa-onnx 的串流辨識設定。</summary>
    /// <param name="enableEndpoint">開啟句尾偵測：偵測到一句講完時，呼叫端要 Reset 串流（計畫書第 6.2 節）</param>
    public OnlineRecognizerConfig ToRecognizerConfig(int numThreads = 2, bool enableEndpoint = true)
    {
        var config = new OnlineRecognizerConfig();
        config.FeatConfig.SampleRate = 16000;
        config.FeatConfig.FeatureDim = 80;
        config.ModelConfig.Tokens = Tokens;
        config.ModelConfig.NumThreads = numThreads;
        config.ModelConfig.Provider = "cpu";
        if (Kind == SpeechModelKind.Transducer)
        {
            config.ModelConfig.Transducer.Encoder = Encoder;
            config.ModelConfig.Transducer.Decoder = Decoder;
            config.ModelConfig.Transducer.Joiner = Joiner!;
        }
        else
        {
            config.ModelConfig.Paraformer.Encoder = Encoder;
            config.ModelConfig.Paraformer.Decoder = Decoder;
        }
        config.DecodingMethod = "greedy_search";
        config.EnableEndpoint = enableEndpoint ? 1 : 0;
        return config;
    }

    public override string ToString() =>
        $"{Code}（{Kind}）{Path.GetFileName(Encoder)} / {Path.GetFileName(Decoder)}" +
        (Joiner is null ? "" : $" / {Path.GetFileName(Joiner)}");
}
