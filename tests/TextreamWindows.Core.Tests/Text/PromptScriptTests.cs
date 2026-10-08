using TextreamWindows.Core.Text;

namespace TextreamWindows.Core.Tests.Text;

/// <summary>移植自 Textream <c>TextreamiOSTests/PromptCoreTests.swift</c>（commit 2c02f3e），方法名照原名轉 PascalCase。</summary>
public class PromptScriptTests
{
    [Fact]
    public void TokenizerCollapsesWhitespaceAndSplitsCJKCharacters()
    {
        var script = new PromptScript("Hello\n\n世界  again");

        Assert.Equal(["Hello", "世", "界", "again"], script.Words.Select(w => w.Text));
        Assert.Equal("Hello 世 界 again", script.Text);
    }

    [Fact]
    public void BalancedCuesAreAnnotationsButUnmatchedOpeningBracketIsReadable()
    {
        var balanced = new PromptScript("Hello [look at camera] world");
        Assert.Equal(["[look", "at", "camera]"], balanced.Words.Where(w => w.IsAnnotation).Select(w => w.Text));

        var unmatched = new PromptScript("Hello [unfinished world");
        Assert.False(unmatched.Words[1].IsAnnotation);
        Assert.False(unmatched.Words[2].IsAnnotation);
    }

    [Fact]
    public void SpeechAnnotationRangesMatchMacIncludingEmptyCues()
    {
        var prompt = new PromptScript("Lead [look at camera] middle [] stray] [unfinished");

        Assert.Equal(["[look at camera]", "[]"], prompt.AnnotationRanges.Select(r => Slice(prompt, r)));
    }

    [Fact]
    public void AnnotationRangesFindEmbeddedAndAdjacentCuesExactly()
    {
        var prompt = new PromptScript("hello[pause]world [one][two]");

        Assert.Equal(["[pause]", "[one]", "[two]"], prompt.AnnotationRanges.Select(r => Slice(prompt, r)));
        Assert.Equal(new CharRange(5, 12), prompt.AnnotationRanges[0]);
    }

    [Fact]
    public void AnnotationRangesConvertToUTF16SafelyAfterEmoji()
    {
        var prompt = new PromptScript("hello🙂 [gülümse] now");
        var range = Assert.Single(prompt.AnnotationRanges);

        Assert.Equal("[gülümse]", Slice(prompt, range));
        Assert.Equal((prompt.Text.IndexOf("[gülümse]", StringComparison.Ordinal), "[gülümse]".Length), prompt.ToUtf16(range));
    }

    /// <summary>
    /// 不是移植的，是 C# 才有的風險（計畫書第 10 節）：組合附加符號（e + U+0301）與擴充 B 區的漢字（𠀋，U+2000B）
    /// 在 UTF-16 都佔兩格，但各自只算一個字。中文標註會被斷字拆開，所以全文裡是「[ 笑 ]」，這也是原版的行為。
    /// </summary>
    [Fact]
    public void CombiningMarksAndAstralHanCountAsOneCharacter()
    {
        var prompt = new PromptScript("café [笑] 𠀋字");

        Assert.Equal("café [ 笑 ] 𠀋 字", prompt.Text);
        var range = Assert.Single(prompt.AnnotationRanges);
        Assert.Equal(new CharRange(5, 10), range);
        Assert.Equal((6, 5), prompt.ToUtf16(range));
        Assert.Equal(["[", "笑", "]"], prompt.Words.Where(w => w.IsAnnotation).Select(w => w.Text));
        Assert.Equal(new CharRange(11, 12), prompt.Words[^2].CharacterRange);
        Assert.Equal(new CharRange(13, 14), prompt.Words[^1].CharacterRange);
    }

    [Fact]
    public void WordProgressRoundTripsCharacterOffsets()
    {
        var script = new PromptScript("alpha beta gamma");
        var offset = script.CharacterOffset(1.5);

        Assert.Equal(8, offset);
        Assert.Equal(1.5, script.WordProgress(offset), 0.01);
    }

    /// <summary>中文一字一詞：第 k 個詞就是第 k 個字，全文「字 空格 字」裡在第 2k 格；超出範圍就夾在頭尾。</summary>
    [Fact]
    public void WordProgressOnChineseLandsOnCharacters()
    {
        var script = new PromptScript("我讀書");

        Assert.Equal(2, script.CharacterOffset(1));
        Assert.Equal(2, script.CharacterOffset(1.5));
        Assert.Equal(script.CharacterCount, script.CharacterOffset(3));
        Assert.Equal(script.CharacterCount, script.CharacterOffset(99));
        Assert.Equal(0, script.CharacterOffset(-1));
        Assert.Equal(2.0, script.WordProgress(4), 0.01);
        Assert.Equal(3.0, script.WordProgress(script.CharacterCount + 10), 0.01);
    }

    private static string Slice(PromptScript prompt, CharRange range) =>
        string.Concat(TextElements.Split(prompt.Text)[range.Start..range.End]);
}
