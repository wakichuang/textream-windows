using System.Text;

namespace TextreamWindows.Core.Documents;

/// <summary>編輯器裡的講稿：文字、檔案路徑、有沒有沒存的修改。開檔、存檔支援 .txt／.md（計畫書第 5 階段步驟 1）。</summary>
public sealed class ScriptDocument
{
    /// <summary>存檔一律 UTF-8 不帶 BOM（新版記事本、VS Code 的預設），Heptabase 匯出的 .md 也是這樣。</summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>上次開檔或存檔時的內容，用來判斷有沒有沒存的修改（改回原樣就不算）。</summary>
    private string _savedText = "";

    public string Text { get; set; } = "";

    public string? FilePath { get; private set; }

    public bool IsDirty => Text != _savedText;

    /// <summary>標題列顯示用：檔名，沒存過是「未命名」，有沒存的修改加「*」。</summary>
    public string DisplayName => (FilePath is null ? "未命名" : Path.GetFileName(FilePath)) + (IsDirty ? "*" : "");

    /// <summary>開檔。看 BOM 判斷 UTF-8／UTF-16，沒有 BOM 當 UTF-8。</summary>
    public static ScriptDocument Load(string path)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        return new ScriptDocument { Text = text, FilePath = path, _savedText = text };
    }

    /// <summary>存回原本的檔案。沒存過的講稿要用 <see cref="SaveAs"/>。</summary>
    public void Save()
    {
        if (FilePath is null)
        {
            throw new InvalidOperationException("這份講稿還沒存過，要用「另存新檔」選位置。");
        }
        SaveAs(FilePath);
    }

    public void SaveAs(string path)
    {
        File.WriteAllText(path, Text, Utf8NoBom);
        FilePath = path;
        _savedText = Text;
    }
}
