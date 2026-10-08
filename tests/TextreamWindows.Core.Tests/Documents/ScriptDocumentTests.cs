using System.Text;
using TextreamWindows.Core.Documents;

namespace TextreamWindows.Core.Tests.Documents;

public sealed class ScriptDocumentTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("textream-doc-");

    public void Dispose() => _dir.Delete(recursive: true);

    private string PathOf(string name) => Path.Combine(_dir.FullName, name);

    [Fact]
    public void ANewDocumentIsUntitledAndClean()
    {
        var doc = new ScriptDocument();

        Assert.Null(doc.FilePath);
        Assert.False(doc.IsDirty);
        Assert.Equal("未命名", doc.DisplayName);
    }

    [Fact]
    public void EditingMarksItDirtyAndTheNameGetsAStar()
    {
        var doc = new ScriptDocument();

        doc.Text = "我已經讀完了";

        Assert.True(doc.IsDirty);
        Assert.Equal("未命名*", doc.DisplayName);
    }

    [Fact]
    public void TypingBackTheSavedTextIsNotDirty()
    {
        var path = PathOf("稿.txt");
        File.WriteAllText(path, "原本的稿");
        var doc = ScriptDocument.Load(path);

        doc.Text = "改過";
        doc.Text = "原本的稿";

        Assert.False(doc.IsDirty);
    }

    [Theory]
    [InlineData("utf8-bom")]
    [InlineData("utf8")]
    [InlineData("utf16")]
    public void LoadsCommonEncodings(string encoding)
    {
        // 記事本存 UTF-8（新版不帶 BOM、舊版帶 BOM）；Word「另存純文字」可能是 UTF-16
        var path = PathOf($"{encoding}.md");
        var text = "## 開場\r\n「內在小孩」聽起來很像心靈雞湯 🙂";
        var bytes = encoding switch
        {
            "utf8-bom" => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)],
            "utf8" => Encoding.UTF8.GetBytes(text),
            _ => [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(text)],
        };
        File.WriteAllBytes(path, bytes);

        var doc = ScriptDocument.Load(path);

        Assert.Equal(text, doc.Text);
        Assert.Equal(path, doc.FilePath);
        Assert.False(doc.IsDirty);
        Assert.Equal($"{encoding}.md", doc.DisplayName);
    }

    [Fact]
    public void SaveWritesUtf8WithoutBomAndCleansTheDocument()
    {
        var path = PathOf("稿.txt");
        File.WriteAllText(path, "舊");
        var doc = ScriptDocument.Load(path);
        doc.Text = "新的稿";

        doc.Save();

        Assert.Equal(Encoding.UTF8.GetBytes("新的稿"), File.ReadAllBytes(path));
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void SaveAsMovesTheDocumentToTheNewPath()
    {
        var doc = new ScriptDocument { Text = "第一版" };
        var path = PathOf("新稿.md");

        doc.SaveAs(path);

        Assert.Equal(path, doc.FilePath);
        Assert.Equal("新稿.md", doc.DisplayName);
        Assert.False(doc.IsDirty);
        Assert.Equal("第一版", File.ReadAllText(path));
    }

    [Fact]
    public void SavingAnUntitledDocumentNeedsAPath()
    {
        var doc = new ScriptDocument { Text = "還沒存過" };

        Assert.Throws<InvalidOperationException>(doc.Save);
    }

    [Fact]
    public void LoadingAMissingFileThrows()
    {
        Assert.Throws<FileNotFoundException>(() => ScriptDocument.Load(PathOf("不存在.txt")));
    }
}
