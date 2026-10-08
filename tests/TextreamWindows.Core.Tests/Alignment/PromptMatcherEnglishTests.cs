using TextreamWindows.Core.Alignment;
using TextreamWindows.Core.Session;
using TextreamWindows.Core.Text;

namespace TextreamWindows.Core.Tests.Alignment;

/// <summary>
/// 英文講稿（瓦基 2026-10-08：編輯介面切換「辨識語言」為 English）。模型照用 A（中英雙語），切換的是比對規則；
/// 繁體中文維持原本的規則，中文的 ZH 測試不受影響。
/// 講稿是林肯的〈蓋茲堡演說〉與〈第二次就職演說〉（公有領域），辨識結果是模型 A（fp32）聽 LibriVox 朗讀（公有領域）的真實輸出。
/// </summary>
public class PromptMatcherEnglishTests
{
    /// <summary>模擬 sherpa-onnx 串流：每一句的辨識結果每次多 2 個英文詞，一句講完就重新起算。回傳每一句講完時的位置；倒退一次就失敗。</summary>
    private static List<int> Feed(PromptMatcher matcher, params string[] utterances)
    {
        var afterEach = new List<int>();
        var last = matcher.RecognizedCharacterCount;
        foreach (var utterance in utterances)
        {
            var words = utterance.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var k = Math.Min(2, words.Length); k <= words.Length; k = k == words.Length ? k + 1 : Math.Min(k + 2, words.Length))
            {
                var partial = string.Join(' ', words[..k]);
                var progress = matcher.Match(partial);
                Assert.True(progress >= last, $"倒退了：{last} → {progress}（聽到「{partial}」）");
                last = progress;
            }
            matcher.RestartFromCurrentProgress();
            afterEach.Add(last);
        }
        return afterEach;
    }

    private static PromptMatcher English(PromptScript prompt) => new(prompt, language: SpeechLanguage.English);

    /// <summary>位置換成「讀到第幾個詞」（只算有字母數字的詞）。</summary>
    private static int WordsRead(PromptScript prompt, int offset) =>
        prompt.Words.Count(w => w.CharacterRange.End <= offset && w.Text.Any(char.IsLetterOrDigit));

    private static void AssertReachesEnd(PromptScript prompt, int progress, string id) =>
        Assert.True(prompt.CharacterCount == progress,
            $"{id}：高亮停在第 {WordsRead(prompt, progress)}／{WordsRead(prompt, prompt.CharacterCount)} 個詞，「{string.Concat(prompt.Elements.Skip(progress).Take(30))}」之前");

    [Fact]
    public void EN01_ReadingAsWrittenReachesTheEnd()
    {
        var prompt = new PromptScript(
            "At this second appearing to take the oath of the Presidential office there is less occasion for an extended address than there was at the first. " +
            "Then a statement somewhat in detail of a course to be pursued seemed fitting and proper.");

        var progress = Feed(English(prompt),
            "AT THIS SECOND APPEARING TO TAKE THE OATH OF THE PRESIDENTIAL OFFICE THERE IS LESS OCCASION FOR AN EXTENDED ADDRESS THAN THERE WAS AT THE FIRST",
            "THEN A STATEMENT SOMEWHAT IN DETAIL OF A COURSE TO BE PURSUED SEEMED FITTING AND PROPER");

        AssertReachesEnd(prompt, progress[^1], "EN-01");
    }

    /// <summary>
    /// 2026-10-08 實測：15 位朗讀者有 14 位念完〈蓋茲堡演說〉高亮卻停在最後一個詞前面，到不了「讀完了」。
    /// 結尾「from the earth」常被辨識成「FROM THIS EARTH」：the 對不上之後，earth 只對上一個詞，防拖走要連續對上才算數，而講稿已經沒有下一個詞了。
    /// </summary>
    [Fact]
    public void EN02_LastWordCountsEvenAfterAMisheardWord()
    {
        var prompt = new PromptScript("and that government of the people, by the people, for the people, shall not perish from the earth.");

        var progress = Feed(English(prompt),
            "AND THAT GOVERNMENT OF THE PEOPLE BY THE PEOPLE FOR THE PEOPLE SHALL NOT PERISH FROM THIS EARTH");

        AssertReachesEnd(prompt, progress[^1], "EN-02");
    }

    /// <summary>
    /// 念一段講稿裡沒有的話（約 50 秒）：英文的 the、of、were、it 到處都是，2026-10-08 實測高亮被拖著往前跑了 43 個詞（中文 g06 是 0 字）。
    /// 這裡的插話是〈第二次就職演說〉第二段，講稿把它拿掉了。
    /// </summary>
    [Fact]
    public void EN03_AdLibDoesNotDragTheHighlight()
    {
        var prompt = new PromptScript(
            "With high hope for the future, no prediction in regard to it is ventured.\n\n" +
            "One-eighth of the whole population were colored slaves, not distributed generally over the Union, but localized in the southern part of it. " +
            "These slaves constituted a peculiar and powerful interest.");
        var matcher = English(prompt);

        var progress = Feed(matcher,
            "WITH HIGH HOPE FOR THE FUTURE NO PREDICTION IN REGARD TO IT IS VENTURED",
            "ON THE OCCASION CORRESPONDING TO THIS FOUR YEARS AGO ALL THOUGHTS WERE ANXIOUSLY DIRECTED TO AN IMPENDING CIVIL WAR",
            "ALL DREADED IT ALL SOUGHT TO AVERTED WHILE THE INAUGURAL ADDRESS WAS BEING DELIVERED FROM THIS PLACE DEVOTED ALL TOGETHER TO SAVING THE UNION WITHOUT WAR",
            "INSURGENT AGENTS WERE IN THE CITY SEEKING TO DESTROY IT WITHOUT WAR SEEKING TO DISSOLVE THE UNION AND DIVIDE EFFECTS BY NEGOTIATION",
            "BOTH PARTIES DEPRECATED WAR BUT ONE OF THEM WOULD MAKE WAR RATHER THAN LET THE NATION SURVIVE AND THE OTHER WOULD ACCEPT WAR RATHER THAN LET IT PERISH AND THE WAR CAME");

        Assert.True(WordsRead(prompt, progress[^1]) - WordsRead(prompt, progress[0]) <= 2,
            $"EN-03：插話期間高亮從第 {WordsRead(prompt, progress[0])} 個詞跑到第 {WordsRead(prompt, progress[^1])} 個詞");

        var rejoined = Feed(matcher,
            "ONE EIGHTH OF THE WHOLE POPULATION WERE COLORED SLAVES NOT DISTRIBUTED GENERALLY OVER THE UNION BUT LOCALIZED IN THE SOUTHERN PART OF IT THESE SLAVES CONSTITUTED A PECULIAR AND POWERFUL INTEREST");
        AssertReachesEnd(prompt, rejoined[^1], "EN-03");
    }

    /// <summary>
    /// 跳過一整段（約 100 個詞，這裡是〈蓋茲堡演說〉第二段）：2026-10-08 實測要念 7 個詞（約 3.7 秒）才接回。
    /// 中文找回位置基本要連續 5 個字，英文一個詞比一個中文字長、也比較不會撞，5 個詞以內要接回。
    /// </summary>
    [Fact]
    public void EN04_SkippingAParagraphRejoinsWithinFiveWords()
    {
        var prompt = new PromptScript(
            "With high hope for the future, no prediction in regard to it is ventured.\n\n" +
            "Now we are engaged in a great civil war, testing whether that nation, or any nation so conceived and so dedicated, can long endure. " +
            "We are met on a great battle-field of that war. We have come to dedicate a portion of that field, as a final resting place for those who here gave their lives that that nation might live. " +
            "It is altogether fitting and proper that we should do this. But, in a larger sense, we can not dedicate -- we can not consecrate -- we can not hallow -- this ground.\n\n" +
            "One-eighth of the whole population were colored slaves, not distributed generally over the Union, but localized in the southern part of it.");
        var matcher = English(prompt);
        Feed(matcher, "WITH HIGH HOPE FOR THE FUTURE NO PREDICTION IN REGARD TO IT IS VENTURED");

        var target = prompt.Text.IndexOf("One-eighth", StringComparison.Ordinal);
        var words = "ONE EIGHTH OF THE WHOLE POPULATION WERE COLORED SLAVES NOT DISTRIBUTED GENERALLY".Split(' ');
        var needed = Enumerable.Range(1, words.Length).FirstOrDefault(k => matcher.Match(string.Join(' ', words[..k])) > target);

        Assert.True(needed is > 0 and <= 5, needed == 0 ? "EN-04：念完 12 個詞都沒接回" : $"EN-04：要念 {needed} 個詞才接回");
    }
}
