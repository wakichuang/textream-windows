using TextreamWindows.Core.Text;

namespace TextreamWindows.Core.Tests.Text;

/// <summary>Ctrl+Alt+↑ ／ ↓ 前後跳一句（計畫書第 5 階段步驟 4）。句子以 。！？；… 與換行分開。</summary>
public class SentenceNavigationTests
{
    private const string Script = "第一句話。第二句話！\n## 小標題\n第三句話？最後一句";

    private static readonly PromptScript Prompt = new(Script);
    private static readonly PromptDisplay Display = new(Prompt);

    /// <summary>某個字（第 n 次出現）在講稿裡的位置。</summary>
    private static int At(char c, int occurrence = 1) =>
        Prompt.Words.Where(w => w.Text == c.ToString()).ElementAt(occurrence - 1).CharacterRange.Start;

    [Fact]
    public void SentencesStartAfterEndPunctuationAndLineBreaks()
    {
        var starts = SentenceNavigation.Starts(Prompt, Display);

        Assert.Equal([At('第', 1), At('第', 2), At('小'), At('第', 3), At('最')], starts);
    }

    [Fact]
    public void CommasDoNotSplitSentences()
    {
        var prompt = new PromptScript("簡單來說，這是工具。下一句");

        var starts = SentenceNavigation.Starts(prompt, new PromptDisplay(prompt));

        Assert.Equal(2, starts.Count);
    }

    [Fact]
    public void NextGoesToTheStartOfTheFollowingSentence()
    {
        Assert.Equal(At('第', 2), SentenceNavigation.Next(Prompt, Display, 0));
        Assert.Equal(At('小'), SentenceNavigation.Next(Prompt, Display, At('句', 2)));
    }

    [Fact]
    public void NextInTheLastSentenceStaysPut()
    {
        // 跳到結尾會被當成讀完、浮層收掉，不要這樣
        var inLast = At('一', 2);

        Assert.Equal(inLast, SentenceNavigation.Next(Prompt, Display, inLast));
    }

    [Fact]
    public void PreviousGoesBackToTheStartOfThisSentenceFirst()
    {
        // 這一句已經念了幾個字：先回到這一句的開頭重念（像音樂播放器的「上一首」）
        Assert.Equal(At('第', 3), SentenceNavigation.Previous(Prompt, Display, At('話', 3)));
    }

    [Fact]
    public void PreviousRightAtASentenceStartGoesToTheSentenceBefore()
    {
        Assert.Equal(At('小'), SentenceNavigation.Previous(Prompt, Display, At('第', 3)));
        Assert.Equal(At('小'), SentenceNavigation.Previous(Prompt, Display, At('三'))); // 才念一個字也算剛開始
    }

    [Fact]
    public void PreviousAtTheTopStaysAtTheTop()
    {
        Assert.Equal(0, SentenceNavigation.Previous(Prompt, Display, 0));
    }
}
