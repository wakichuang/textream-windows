using TextreamWindows.Speech.Models;

namespace TextreamWindows.Speech.Tests;

public sealed class SpeechModelTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("textream-models-");

    public void Dispose() => _root.Delete(recursive: true);

    private string MakeModel(string name, params string[] files)
    {
        var dir = Path.Combine(_root.FullName, name);
        Directory.CreateDirectory(dir);
        foreach (var f in files)
        {
            File.WriteAllText(Path.Combine(dir, f), "");
        }
        return dir;
    }

    [Fact]
    public void TransducerPrefersInt8FilesByDefault()
    {
        MakeModel(
            "sherpa-onnx-streaming-zipformer-bilingual-zh-en-2023-02-20",
            "tokens.txt",
            "encoder-epoch-99-avg-1.onnx", "encoder-epoch-99-avg-1.int8.onnx",
            "decoder-epoch-99-avg-1.onnx", "decoder-epoch-99-avg-1.int8.onnx",
            "joiner-epoch-99-avg-1.onnx", "joiner-epoch-99-avg-1.int8.onnx");

        var model = SpeechModel.Resolve("A", _root.FullName);

        Assert.Equal(SpeechModelKind.Transducer, model.Kind);
        Assert.Equal("encoder-epoch-99-avg-1.int8.onnx", Path.GetFileName(model.Encoder));
        Assert.Equal("decoder-epoch-99-avg-1.int8.onnx", Path.GetFileName(model.Decoder));
        Assert.Equal("joiner-epoch-99-avg-1.int8.onnx", Path.GetFileName(model.Joiner));
        Assert.Equal("tokens.txt", Path.GetFileName(model.Tokens));
    }

    [Fact]
    public void Fp32IsChosenWhenAskedAndFallsBackToInt8WhenMissing()
    {
        // C 只附 int8 的 encoder／joiner 與 fp32 的 decoder
        MakeModel(
            "sherpa-onnx-streaming-zipformer-zh-int8-2025-06-30",
            "tokens.txt", "encoder.int8.onnx", "decoder.onnx", "joiner.int8.onnx");

        var model = SpeechModel.Resolve("c", _root.FullName, preferFp32: true);

        Assert.Equal("encoder.int8.onnx", Path.GetFileName(model.Encoder));
        Assert.Equal("decoder.onnx", Path.GetFileName(model.Decoder));
        Assert.Equal("joiner.int8.onnx", Path.GetFileName(model.Joiner));
    }

    [Fact]
    public void ParaformerIsDetectedWhenThereIsNoJoiner()
    {
        MakeModel(
            "sherpa-onnx-streaming-paraformer-bilingual-zh-en",
            "tokens.txt", "encoder.onnx", "encoder.int8.onnx", "decoder.onnx", "decoder.int8.onnx");

        var model = SpeechModel.Resolve("B", _root.FullName);

        Assert.Equal(SpeechModelKind.Paraformer, model.Kind);
        Assert.Equal("encoder.int8.onnx", Path.GetFileName(model.Encoder));
        Assert.Equal("decoder.int8.onnx", Path.GetFileName(model.Decoder));
        Assert.Null(model.Joiner);
    }

    [Fact]
    public void AFolderPathWorksInPlaceOfACode()
    {
        var dir = MakeModel("my-model", "tokens.txt", "encoder.onnx", "decoder.onnx", "joiner.onnx");

        var model = SpeechModel.Resolve(dir, _root.FullName);

        Assert.Equal(dir, model.Directory);
        Assert.Equal(SpeechModelKind.Transducer, model.Kind);
    }

    [Fact]
    public void MissingModelExplainsHowToFetchIt()
    {
        var ex = Assert.Throws<FileNotFoundException>(() => SpeechModel.Resolve("C", _root.FullName));

        Assert.Contains("fetch_models.py", ex.Message);
    }

    // ── 免安裝版（瓦基 2026-10-08：要公開給一般人用，下載解壓縮就能用，不要再叫人另外下載模型） ──

    private const string ModelA = "sherpa-onnx-streaming-zipformer-bilingual-zh-en-2023-02-20";
    private static readonly string[] ModelAFiles =
        ["tokens.txt", "encoder-epoch-99-avg-1.onnx", "decoder-epoch-99-avg-1.onnx", "joiner-epoch-99-avg-1.onnx"];

    [Fact]
    public void TheFirstPlaceThatHasTheModelWins()
    {
        var empty = Path.Combine(_root.FullName, "empty");
        Directory.CreateDirectory(empty);
        MakeModel(Path.Combine("next-to-exe", ModelA), ModelAFiles);
        MakeModel(Path.Combine("localappdata", ModelA), ModelAFiles);

        var model = SpeechModel.Resolve("A", [empty, Path.Combine(_root.FullName, "next-to-exe"), Path.Combine(_root.FullName, "localappdata")]);

        Assert.StartsWith(Path.Combine(_root.FullName, "next-to-exe"), model.Directory);
    }

    [Fact]
    public void WhenNoPlaceHasTheModelTheMessageSaysWhereItLooked()
    {
        var a = Path.Combine(_root.FullName, "a");
        var b = Path.Combine(_root.FullName, "b");

        var ex = Assert.Throws<FileNotFoundException>(() => SpeechModel.Resolve("A", [a, b]));

        Assert.Contains(a, ex.Message);
        Assert.Contains(b, ex.Message);
        Assert.Contains("models", ex.Message);
    }

    [Fact]
    public void TheModelsFolderNextToTheProgramIsSearchedBeforeLocalAppData()
    {
        var roots = SpeechModel.DefaultSearchRoots;
        var nextToExe = Path.Combine(AppContext.BaseDirectory, "models");
        var localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Textream", "models");

        Assert.True(roots.ToList().IndexOf(nextToExe) >= 0, "要找執行檔旁邊的 models");
        Assert.True(roots.ToList().IndexOf(nextToExe) < roots.ToList().IndexOf(localAppData), "免安裝版的模型優先");
    }

    [Fact]
    public void UnknownCodeIsRejected()
    {
        Assert.Throws<ArgumentException>(() => SpeechModel.Resolve("Z", _root.FullName));
    }
}
