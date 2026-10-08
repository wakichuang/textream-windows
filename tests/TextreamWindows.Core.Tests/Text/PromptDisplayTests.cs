using TextreamWindows.Core.Text;

namespace TextreamWindows.Core.Tests.Text;

public class PromptDisplayTests
{
    [Fact]
    public void ChineseHasNoSpacesBetweenCharacters()
    {
        var display = new PromptDisplay(new PromptScript("我已經讀完了。今天天氣很好。"));

        Assert.Equal("我已經讀完了。今天天氣很好。", display.Text);
    }

    [Fact]
    public void SpacesBetweenEnglishWordsAndAroundThemStay()
    {
        var display = new PromptDisplay(new PromptScript("我讀了 Atomic Habits 這本書"));

        Assert.Equal("我讀了 Atomic Habits 這本書", display.Text);
    }

    [Fact]
    public void LineBreaksAndOriginalSpacingAreKept()
    {
        // 顯示的是瓦基原本的講稿：段落、空格都照原樣，比對用的 PromptScript.Text 才會合併
        var display = new PromptDisplay(new PromptScript("## 開場\n\n第一段  兩個空格\n第二段 🙂"));

        Assert.Equal("## 開場\n\n第一段  兩個空格\n第二段 🙂", display.Text);
    }

    [Fact]
    public void OffsetsMapToTheSameCharacterInTheDisplayText()
    {
        var script = new PromptScript("我讀了 Atomic Habits 這本書");
        var display = new PromptDisplay(script);

        foreach (var word in script.Words)
        {
            var start = display.ToDisplayOffset(word.CharacterRange.Start);
            Assert.Equal(word.Text, display.Text.Substring(start, word.Text.Length));
        }
        Assert.Equal(display.Text.Length, display.ToDisplayOffset(script.CharacterCount));
    }

    [Fact]
    public void OffsetsAreUtf16SafeAfterEmojiAndRareCharacters()
    {
        var script = new PromptScript("我愛\U0002000B🙂讀書");
        var display = new PromptDisplay(script);
        var book = script.Words.Single(w => w.Text == "書");

        var start = display.ToDisplayOffset(book.CharacterRange.Start);

        Assert.Equal("書", display.Text.Substring(start, 1));
    }

    [Fact]
    public void OffsetsOutsideTheScriptAreClamped()
    {
        var script = new PromptScript("我讀書");
        var display = new PromptDisplay(script);

        Assert.Equal(0, display.ToDisplayOffset(-5));
        Assert.Equal(display.Text.Length, display.ToDisplayOffset(script.CharacterCount + 10));
    }

    // ── 反方向：編輯器的游標（原稿 UTF-16 位置）→ 講稿位置（瓦基 2026-10-08：從游標處開始講） ──

    [Fact]
    public void ACaretBeforeACharacterMapsToThatCharacter()
    {
        var script = new PromptScript("我已經讀完了。今天天氣很好。");
        var display = new PromptDisplay(script);
        var today = script.Words.First(w => w.Text == "今");

        Assert.Equal(today.CharacterRange.Start, display.ToCharacterOffset(display.Text.IndexOf('今')));
        Assert.Equal(0, display.ToCharacterOffset(0));
    }

    [Fact]
    public void ACaretInSpacesOrOnABlankLineMapsToTheNextWord()
    {
        var script = new PromptScript("## 開場\n\n大家好  Atomic Habits");
        var display = new PromptDisplay(script);
        var big = script.Words.First(w => w.Text == "大");
        var habits = script.Words.First(w => w.Text == "Habits");

        Assert.Equal(big.CharacterRange.Start, display.ToCharacterOffset(display.Text.IndexOf('\n') + 1)); // 空行上
        Assert.Equal(habits.CharacterRange.Start, display.ToCharacterOffset(display.Text.IndexOf("Habits") - 1)); // 空格後面
    }

    [Fact]
    public void ACaretAtTheEndMapsToTheEnd()
    {
        var script = new PromptScript("我讀書\n");
        var display = new PromptDisplay(script);

        Assert.Equal(script.CharacterCount, display.ToCharacterOffset(display.Text.Length));
        Assert.Equal(script.CharacterCount, display.ToCharacterOffset(display.Text.Length + 5)); // 超出範圍也不出錯
    }

    [Fact]
    public void ACaretInsideASurrogatePairStaysOnThatCharacter()
    {
        var script = new PromptScript("一𠀋二");
        var display = new PromptDisplay(script);
        var rare = script.Words[1].CharacterRange.Start;

        Assert.Equal(rare, display.ToCharacterOffset(1));
        Assert.Equal(rare, display.ToCharacterOffset(2)); // 卡在兩個 UTF-16 單位中間：還是那個字
    }

    [Fact]
    public void BothDirectionsAgreeOnEveryWord()
    {
        var script = new PromptScript("我讀了 Atomic Habits 這本書，\n第二段 🙂 好");
        var display = new PromptDisplay(script);

        foreach (var word in script.Words)
        {
            Assert.Equal(word.CharacterRange.Start, display.ToCharacterOffset(display.ToDisplayOffset(word.CharacterRange.Start)));
        }
    }
}
