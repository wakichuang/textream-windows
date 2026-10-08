using TextreamWindows.Core.Alignment;
using TextreamWindows.Core.Text;

namespace TextreamWindows.Core.Tests.Alignment;

public class PromptMatcherTests
{
    // ── 移植自 Textream PromptCoreTests.swift（commit 2c02f3e） ──

    [Fact]
    public void MatcherSkipsCueAndMovesForwardWithFuzzyTranscript()
    {
        var matcher = new PromptMatcher(new PromptScript("Welcome [smile] to the presentation today"));

        var first = matcher.Match("welcome to the");
        var second = matcher.Match("welcome to the presentation");

        Assert.True(first > "Welcome".Length, $"first = {first}");
        Assert.True(second >= first, $"first = {first}, second = {second}");
        Assert.True(second > "Welcome [smile] to the".Length, $"second = {second}");
    }

    [Fact]
    public void MatcherNeverMovesBackward()
    {
        var matcher = new PromptMatcher(new PromptScript("one two three four five six"));
        var forward = matcher.Match("one two three four");
        var stale = matcher.Match("one");

        Assert.True(forward > 0, $"forward = {forward}");
        Assert.Equal(forward, stale);
    }

    // ── 計畫書第 7.3 節的中文測試 ──

    /// <summary>辨識結果一次給完，高亮要到講稿結尾。模型輸出的是簡體字，這裡模擬的就是簡體。</summary>
    [Theory]
    [InlineData("ZH-01", "我已經讀完了", "我以经读完了")]
    [InlineData("ZH-02", "閱讀前哨站", "阅读前哨站")]
    [InlineData("ZH-03", "銀行和行動", "银行和行动")]
    [InlineData("ZH-04", "我在 2026 年寫這本書", "我在二零二六年写这本书")]
    [InlineData("ZH-05", "一共兩千元", "一共2000元")]
    [InlineData("ZH-06", "我讀了 Atomic Habits 這本書", "我读了 ATOMIC HABITS 这本书")] // 模型 A 吐的英文是大寫
    [InlineData("ZH-07", "「卡片盒」筆記法。", "卡片盒笔记法")] // 標點跳過但算位置：高亮到句號之後
    [InlineData("ZH-08", "開場 [看鏡頭] 大家好", "开场大家好")]
    [InlineData("ZH-09", "第一段……第二段……第三段", "第一段第三段")] // 跳過第二段
    [InlineData("ZH-11", "瓦基", "哇基")]
    [InlineData("ZH-13", "ＡＩ工具", "ai工具")]
    public void TranscriptReachesTheEndOfTheScript(string id, string script, string transcript)
    {
        var prompt = new PromptScript(script);
        var matcher = new PromptMatcher(prompt);

        var progress = matcher.Match(transcript);

        Assert.True(prompt.CharacterCount == progress, $"{id}：高亮停在 {progress}，講稿共 {prompt.CharacterCount} 個字");
    }

    /// <summary>
    /// 上一條只看終點不夠嚴：比對會跳過一兩個對不上的字，後面對上了照樣到得了結尾。
    /// 2026-10-08 把拼音比對換成「字完全相同」做突變測試，ZH-01、02、11 居然還是綠的。
    /// 這裡改成一個字一個字餵：同音字、簡體字本身就要讓高亮前進一格，不能靠後面的字撿回來。
    /// </summary>
    [Theory]
    [InlineData("ZH-01", "我已經讀完了", "我以经读完了")]
    [InlineData("ZH-02", "閱讀前哨站", "阅读前哨站")]
    [InlineData("ZH-03", "銀行和行動", "银行和行动")]
    [InlineData("ZH-11", "瓦基", "哇基")]
    public void HomophonesAndSimplifiedCharactersAdvanceOneCharacterAtATime(string id, string script, string transcript)
    {
        var prompt = new PromptScript(script); // 純中文：全文是「字 空格 字 空格 …」，讀完第 k 個字就停在第 2k 格
        var characters = TextElements.Split(transcript);

        for (var k = 1; k <= characters.Length; k++)
        {
            var matcher = new PromptMatcher(prompt);
            var progress = matcher.Match(string.Concat(characters[..k]));

            var expected = k < characters.Length ? 2 * k : prompt.CharacterCount;
            Assert.True(expected == progress, $"{id}：聽到「{string.Concat(characters[..k])}」，高亮應該在 {expected}，實際在 {progress}");
        }
    }

    /// <summary>
    /// 同樣的理由（2026-10-08 拿掉數字正規化做突變測試，ZH-04、05 還是綠的）：只聽到數字為止，高亮就要越過整個數字，
    /// 停在數字後面那個字的開頭，不能靠「年」「元」把漏掉的數字撿回來。
    /// </summary>
    [Theory]
    [InlineData("ZH-04", "我在 2026 年寫這本書", "我在二零二六", "年")]
    [InlineData("ZH-04", "我在二〇二六年寫這本書", "我在2026", "年")]
    [InlineData("ZH-05", "一共兩千元", "一共2000", "元")]
    [InlineData("ZH-05", "一共兩千元", "一共两千", "元")]
    public void SpokenNumberAdvancesPastTheWholeWrittenNumber(string id, string script, string transcript, string nextCharacter)
    {
        var prompt = new PromptScript(script);
        var expected = prompt.Elements.ToList().IndexOf(nextCharacter);
        var matcher = new PromptMatcher(prompt);

        var progress = matcher.Match(transcript);

        Assert.True(expected == progress, $"{id}：聽到「{transcript}」，高亮應該停在「{nextCharacter}」（{expected}），實際在 {progress}");
    }

    [Fact]
    public void ZH10_CorrectedTranscriptNeverMovesBackward()
    {
        var matcher = new PromptMatcher(new PromptScript("我已經讀完了，今天天氣很好"));

        var first = matcher.Match("我以经读完了");
        var corrected = matcher.Match("我已"); // 辨識引擎改口：前面的字被改掉、變短
        var later = matcher.Match("我已经读完了今天天气");

        Assert.True(first > 0, $"first = {first}");
        Assert.Equal(first, corrected);
        Assert.True(later > first, $"first = {first}, later = {later}");
    }

    [Fact]
    public void ZH12_RareCharactersAndEmojiDoNotShiftPositions()
    {
        // 𠀋（U+2000B）與 🙂 在 UTF-16 各佔兩格，位置一律以文字元素計
        var prompt = new PromptScript("我愛\U0002000B🙂讀書");
        var matcher = new PromptMatcher(prompt);

        var progress = matcher.Match("我爱\U0002000B读书");

        Assert.NotEqual(prompt.Text.Length, prompt.CharacterCount);
        Assert.Equal(prompt.CharacterCount, progress);
    }

    [Fact]
    public void ZH14_ASingleCharacterFromTheMiddleDoesNotJump()
    {
        var matcher = new PromptMatcher(new PromptScript("今天想跟大家聊聊閱讀這件事，我們從某本書開始講起，然後再談到卡片盒筆記"));

        var progress = matcher.Match("盒");

        Assert.Equal(0, progress);
    }

    // ── 句尾偵測的接法（計畫書第 6.2 節） ──

    [Fact]
    public void RestartLetsTheNextUtteranceStartFromCurrentProgress()
    {
        // 第一句超過比對的跳躍範圍（往後最多看 5 個單位），不重新起算的話，第二句的辨識結果對不到「今」。
        // 第二句刻意只有 4 個字，短於「找回位置」要求的 5 個字，才測得出是靠重新起算（2026-10-08 加找回位置時改）
        var prompt = new PromptScript("我已經把這本書讀完了。今天很好。");
        var matcher = new PromptMatcher(prompt);
        var firstSentence = matcher.Match("我已经把这本书读完了");

        matcher.RestartFromCurrentProgress();
        var secondSentence = matcher.Match("今天很好");

        Assert.True(firstSentence > 0, $"firstSentence = {firstSentence}");
        Assert.Equal(prompt.CharacterCount, secondSentence);
    }

    [Fact]
    public void WithoutRestartTheNextUtteranceIsMatchedFromTheOldStart()
    {
        // 上一條的對照組：證明上一條真的是靠 RestartFromCurrentProgress 才過的
        var prompt = new PromptScript("我已經把這本書讀完了。今天很好。");
        var matcher = new PromptMatcher(prompt);
        var firstSentence = matcher.Match("我已经把这本书读完了");

        var secondSentence = matcher.Match("今天很好");

        Assert.Equal(firstSentence, secondSentence);
    }
}
