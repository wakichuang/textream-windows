using TextreamWindows.Core.Text;

namespace TextreamWindows.Core.Tests.Text;

/// <summary>
/// 浮層上黃色的那一段（瓦基 2026-10-08：只標一個中文字太少，一次標 2～4 個字比較跟得上）。
/// 從目前位置往後最多 4 個字；遇到標點、標註、換行就停；不到 2 個字時往前補這一句裡剛念的字。
/// </summary>
public class ReadingHighlightTests
{
    /// <summary>黃色那段在畫面上的文字。</summary>
    private static string Highlight(string script, int progress)
    {
        var prompt = new PromptScript(script);
        var display = new PromptDisplay(prompt);
        var range = ReadingHighlight.Range(prompt, display, progress);
        return display.Text[display.ToDisplayOffset(range.Start)..display.ToDisplayOffset(range.End)];
    }

    /// <summary>某個詞的開頭位置（照 PromptScript 的詞序）。</summary>
    private static int At(string script, int wordIndex) => new PromptScript(script).Words[wordIndex].CharacterRange.Start;

    [Fact]
    public void InTheMiddleOfAClauseFourCharactersAreHighlighted()
    {
        Assert.Equal("我已經讀", Highlight("我已經讀完了。今天天氣很好。", 0));
    }

    [Fact]
    public void ItStopsAtPunctuation()
    {
        const string Script = "我已經讀完了。今天天氣很好。";

        Assert.Equal("讀完了", Highlight(Script, At(Script, 3)));
    }

    [Fact]
    public void TheLastCharacterOfAClauseTakesThePreviousOneAlong()
    {
        // 只剩一個字就到句號：往前補剛念的「完」，不要只標一個字
        const string Script = "我已經讀完了。今天天氣很好。";

        Assert.Equal("完了", Highlight(Script, At(Script, 5)));
    }

    [Fact]
    public void AfterPunctuationTheNextClauseStartsFresh()
    {
        const string Script = "我已經讀完了。今天天氣很好。";

        Assert.Equal("今天天氣", Highlight(Script, At(Script, 6))); // 詞 6 是「。」，目前的字跳過標點
    }

    [Fact]
    public void ItNeverCrossesALineBreak()
    {
        const string Script = "## 開場\n大家好";

        Assert.Equal("開場", Highlight(Script, At(Script, 2))); // 「場」是這行最後一個字，往前補「開」；不能跨到下一行
        Assert.Equal("大家好", Highlight(Script, At(Script, 3)));
    }

    [Fact]
    public void AnEnglishWordIsKeptWhole()
    {
        const string Script = "我讀了 Atomic Habits 這本書";

        Assert.Equal("Atomic", Highlight(Script, At(Script, 3)));
    }

    [Fact]
    public void ShortEnglishWordsCanShareTheHighlight()
    {
        const string Script = "我用 AI 做 PPT";

        Assert.Equal("用 AI 做", Highlight(Script, At(Script, 1)));
    }

    [Fact]
    public void ALongEnglishWordAheadDoesNotGetPulledIn()
    {
        // 「了」後面是 Atomic：不要標成「了 Atomic」，往前補「讀」
        const string Script = "我讀了 Atomic Habits 這本書";

        Assert.Equal("讀了", Highlight(Script, At(Script, 2)));
    }

    [Fact]
    public void StoppingInsideTheLastLettersOfAnEnglishWordKeepsTheWholeWord()
    {
        const string Script = "我讀了 Atomic";
        var atomic = At(Script, 3);

        Assert.Equal("Atomic", Highlight(Script, atomic + 5)); // 只剩最後的 c 沒念：不要往前抓到「了」
    }

    [Fact]
    public void AnnotationsAreNotHighlighted()
    {
        const string Script = "大家好[停頓]今天";

        Assert.Equal("大家好", Highlight(Script, 0));
    }

    [Fact]
    public void AOneCharacterLineStaysOneCharacter()
    {
        Assert.Equal("好", Highlight("好\n今天", 0));
    }

    [Fact]
    public void AtTheEndNothingIsHighlighted()
    {
        const string Script = "我讀書";

        Assert.Equal("", Highlight(Script, new PromptScript(Script).CharacterCount));
    }

    [Fact]
    public void ItCountsTextElementsNotUtf16Units()
    {
        // 擴充 B 的「𠀋」在 UTF-16 是兩個單位，還是算一個字
        Assert.Equal("𠀋一二三", Highlight("𠀋一二三四", 0));
    }
}
