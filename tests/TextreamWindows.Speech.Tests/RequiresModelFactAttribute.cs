using TextreamWindows.Speech.Models;

namespace TextreamWindows.Speech.Tests;

/// <summary>
/// 需要真的語音模型的測試：本機找得到預設模型 A（<c>python scripts/fetch_models.py A</c>）才跑，找不到就跳過、不算失敗。
/// CI 目前沒有下載模型，這類測試在 CI 會顯示為「略過」。
/// </summary>
public sealed class RequiresModelFactAttribute : FactAttribute
{
    public RequiresModelFactAttribute()
    {
        if (TestModels.DefaultModel is null)
        {
            Skip = $"找不到模型 A（{SpeechModel.DefaultModelsRoot}），先執行 python scripts/fetch_models.py A";
        }
    }
}

public static class TestModels
{
    private static readonly Lazy<SpeechModel?> Lazy = new(() =>
    {
        try
        {
            return SpeechModel.Resolve("A", preferFp32: true); // 第 1 節：預設模型是 A 的 fp32
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    });

    /// <summary>預設模型 A（fp32）；本機沒有就是 null。</summary>
    public static SpeechModel? DefaultModel => Lazy.Value;

    /// <summary>模型附帶的範例音檔（不是瓦基的聲音）。</summary>
    public static string SampleWav(string name) => Path.Combine(DefaultModel!.Directory, "test_wavs", name);
}
