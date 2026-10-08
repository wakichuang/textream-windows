using TextreamWindows.Speech.Audio;

namespace TextreamWindows.Speech.Tests.Golden;

/// <summary>
/// 黃金錄音在哪。錄音與它們的講稿是維護者本人的聲音與文章，不進 repo：
/// <list type="bullet">
/// <item>黃金錄音資料夾（mic01.wav、mic01.md、podcast.md…）：環境變數 TEXTREAM_GOLDEN_DIR，或 repo 根目錄 <c>golden.local</c> 的 <c>golden_dir=</c>。</item>
/// <item>整集 Podcast 的音檔（看稿講，約 30 分鐘）：環境變數 TEXTREAM_PODCAST，或 <c>golden.local</c> 的 <c>podcast=</c>。</item>
/// </list>
/// <c>golden.local</c> 不進 repo（.gitignore 擋掉），一行一個 <c>名稱=路徑</c>。都沒有就略過這些測試（CI 一律略過）。
/// </summary>
public static class GoldenAudio
{
    public static string? GoldenDir => Setting("TEXTREAM_GOLDEN_DIR", "golden_dir");

    public static string? PodcastPath => Setting("TEXTREAM_PODCAST", "podcast");

    public static string Mic01Path => Path.Combine(GoldenDir ?? "", "mic01.wav");

    public static bool HasMic01 => GoldenDir is not null && File.Exists(Mic01Path);

    public static bool HasPodcast => PodcastPath is not null && File.Exists(PodcastPath);

    private static readonly Lazy<float[]> Mic01Samples = new(() => AudioFile.Load16kMono(Mic01Path));

    private static readonly Lazy<float[]> PodcastSamples = new(() => AudioFile.Load16kMono(PodcastPath!));

    public static float[] Mic01 => Mic01Samples.Value;

    public static float[] Podcast => PodcastSamples.Value;

    /// <summary>黃金錄音的講稿，跟錄音放在一起（<see cref="GoldenDir"/>）。</summary>
    public static string Script(string name) => File.ReadAllText(Path.Combine(GoldenDir!, name + ".md"));

    /// <summary>環境變數優先，其次是 repo 根目錄的 golden.local。</summary>
    private static string? Setting(string environmentVariable, string key)
    {
        if (Environment.GetEnvironmentVariable(environmentVariable) is { Length: > 0 } env)
        {
            return env;
        }
        if (LocalFile() is not { } file)
        {
            return null;
        }
        var prefix = key + "=";
        return File.ReadLines(file).Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..].Trim() is { Length: > 0 } value
            ? value
            : null;
    }

    /// <summary>從測試組件的位置往上找到 repo 根目錄（有 TextreamWindows.slnx 的地方），再看有沒有 golden.local。</summary>
    private static string? LocalFile()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TextreamWindows.slnx")))
            {
                var file = Path.Combine(dir.FullName, "golden.local");
                return File.Exists(file) ? file : null;
            }
        }
        return null;
    }
}

/// <summary>需要模型 A 與 mic01.wav 才跑；任一個找不到就略過（CI 一律略過）。</summary>
public sealed class RequiresGoldenFactAttribute : FactAttribute
{
    public RequiresGoldenFactAttribute(bool needsPodcast = false)
    {
        if (TestModels.DefaultModel is null)
        {
            Skip = "找不到模型 A，先執行 python scripts/fetch_models.py A";
        }
        else if (!GoldenAudio.HasMic01)
        {
            Skip = "找不到黃金錄音 mic01.wav（設環境變數 TEXTREAM_GOLDEN_DIR，或在 repo 根目錄的 golden.local 寫 golden_dir=資料夾）";
        }
        else if (needsPodcast && !GoldenAudio.HasPodcast)
        {
            Skip = "找不到整集 Podcast 音檔（設環境變數 TEXTREAM_PODCAST，或在 golden.local 寫 podcast=音檔路徑）";
        }
    }
}
