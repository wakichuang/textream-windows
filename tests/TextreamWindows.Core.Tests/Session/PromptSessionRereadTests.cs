using TextreamWindows.Core.Session;
using TextreamWindows.Core.Text;

namespace TextreamWindows.Core.Tests.Session;

/// <summary>
/// 講到一半往回滾、往回點，要能停在前面重講（移植 Mac 繁中版 1.7.1.4，wakichuang/textream-zh issue #1，MAC-04～08）。
/// 往回跳之後，跳轉前講出口的話、辨識引擎改寫的尾巴、停一下又接著講的原本後面的話，都會被「找回位置」在後面找到，把高亮拉回原處；
/// 往下跳不受影響，因為那些話落在新位置的前面，比對只往後找。
/// </summary>
/// <remarks>
/// 每個情境用兩種餵法跑：串流＝sherpa-onnx 的部分結果每次多 2 個字；逐句＝一句只送一次完整結果。
/// 時間用音訊時間（秒）：每 2 個字 0.4 秒，跟 <see cref="PromptSession.ProcessTranscript"/> 收到的 <c>result.Time</c> 同一個時鐘。
/// </remarks>
public class PromptSessionRereadTests
{
    private const double SecondsPerTwoCharacters = 0.4;

    private static readonly string[] Sentences =
    [
        "卡片盒筆記的核心是用自己的話重寫。", "每一張卡片只寫一個觀點，而且要能獨立看懂。", "寫完之後要跟舊的卡片建立連結。",
        "索引筆記就像一張地圖，帶你找到入口。", "真正的思考一定要由自己完成。", "工具不重要，重要的是持續寫下去。",
    ];

    /// <summary>辨識引擎吐出來的樣子：簡體、沒有標點。</summary>
    private static readonly string[] Spoken =
    [
        "卡片盒笔记的核心是用自己的话重写", "每一张卡片只写一个观点而且要能独立看懂", "写完之后要跟旧的卡片建立连结",
        "索引笔记就像一张地图带你找到入口", "真正的思考一定要由自己完成", "工具不重要重要的是持续写下去",
    ];

    private static readonly PromptScript Prompt = new(string.Concat(Sentences));

    /// <summary>第 <paramref name="index"/> 句開頭的位置（文字元素）：前面幾句的可讀字數過完之後的第一個可讀字。</summary>
    private static int SentenceStart(int index)
    {
        var target = Sentences[..index].Sum(s => TextElements.Split(s).Count(TextElements.IsLetterOrNumber));
        var seen = 0;
        for (var i = 0; i < Prompt.Elements.Count; i++)
        {
            if (!TextElements.IsLetterOrNumber(Prompt.Elements[i]))
            {
                continue;
            }
            if (seen == target)
            {
                return i;
            }
            seen++;
        }
        return Prompt.CharacterCount;
    }

    /// <summary>第 <paramref name="index"/> 句結尾（下一句開頭）；最後一句是講稿結尾。</summary>
    private static int SentenceEnd(int index) => index + 1 < Sentences.Length ? SentenceStart(index + 1) : Prompt.CharacterCount;

    /// <summary>第 <paramref name="sentence"/> 句裡，第 <paramref name="nth"/> 個可讀字的位置。</summary>
    private static int CharacterIn(int sentence, int nth)
    {
        var offset = SentenceStart(sentence);
        for (var seen = 0; ; offset++)
        {
            if (TextElements.IsLetterOrNumber(Prompt.Elements[offset]) && seen++ == nth)
            {
                return offset;
            }
        }
    }

    private static string Describe(int offset) =>
        $"停在可讀字 {Prompt.Elements.Take(offset).Count(TextElements.IsLetterOrNumber)}「{string.Concat(Prompt.Elements.Skip(offset).Take(8))}」";

    /// <summary>一段話的辨識結果：已經在這一句裡聽到的 <paramref name="heard"/> 後面接 <paramref name="words"/>。串流每次多 2 個字，逐句只送最後一次。</summary>
    private static void Say(PromptSession session, ref double now, string heard, string words, bool streaming, Action<int>? afterEach = null)
    {
        var elements = TextElements.Split(words);
        for (var k = Math.Min(2, elements.Length); k <= elements.Length; k = k == elements.Length ? k + 1 : Math.Min(k + 2, elements.Length))
        {
            now += SecondsPerTwoCharacters;
            if (!streaming && k < elements.Length)
            {
                continue;
            }
            session.ProcessTranscript(heard + string.Concat(elements[..k]), now);
            afterEach?.Invoke(k);
        }
    }

    /// <summary>講完一整句，引擎偵測到句尾（停頓）。</summary>
    private static void SaySentence(PromptSession session, ref double now, string words, bool streaming)
    {
        Say(session, ref now, "", words, streaming);
        session.EndOfUtterance();
        now += 0.6;
    }

    private static PromptSession ReadFirstSentences(int count, bool streaming, out double now)
    {
        var session = new PromptSession(Prompt, FollowMode.WordTracking);
        now = 0;
        session.Start(now);
        for (var i = 0; i < count; i++)
        {
            SaySentence(session, ref now, Spoken[i], streaming);
        }
        Assert.Equal(SentenceEnd(count - 1), session.EffectiveCharacterCount);
        return session;
    }

    /// <summary>MAC-04：往回跳之後，引擎改寫了跳轉前那句的尾巴（句尾的最終結果晚一秒多才到），講者還沒開口。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RevisedTailAfterJumpingBackDoesNotPullTheHighlightAway(bool streaming)
    {
        var session = ReadFirstSentences(3, streaming, out var now);
        Say(session, ref now, "", Spoken[3], streaming); // 第 4 句講完，句尾還沒判定
        var back = SentenceStart(1);

        session.JumpTo(back, now);
        var revised = Spoken[3].Replace("带你", "代你"); // 「帶你找到入口」的「帶」改成同音的「代」
        session.ProcessTranscript(revised, now + 1.3);
        session.EndOfUtterance();
        Assert.True(session.EffectiveCharacterCount == back, $"MAC-04：被拉走了，{Describe(session.EffectiveCharacterCount)}");

        now += 2;
        SaySentence(session, ref now, Spoken[1], streaming);
        Assert.True(session.EffectiveCharacterCount == SentenceEnd(1), $"MAC-04：開口重念沒跟上，{Describe(session.EffectiveCharacterCount)}");
    }

    /// <summary>MAC-05：講到一半往回跳，把那句講完：留在跳到的位置；停一下再從那裡重念，高亮接得上。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FinishingTheSentenceAfterJumpingBackStaysThenRereadingFollows(bool streaming)
    {
        var session = ReadFirstSentences(4, streaming, out var now);
        var opening = string.Concat(TextElements.Split(Spoken[4])[..4]);
        Say(session, ref now, "", opening, streaming);
        var back = SentenceStart(1);

        session.JumpTo(back, now);
        Say(session, ref now, opening, Spoken[4][opening.Length..], streaming); // 一邊滾一邊把那句講完
        session.EndOfUtterance();
        Assert.True(session.EffectiveCharacterCount == back, $"MAC-05：講完那句就被拉走，{Describe(session.EffectiveCharacterCount)}");

        now += 0.6;
        SaySentence(session, ref now, Spoken[1], streaming);
        Assert.True(session.EffectiveCharacterCount == SentenceEnd(1), $"MAC-05：重念第 2 句沒跟上，{Describe(session.EffectiveCharacterCount)}");
    }

    /// <summary>
    /// MAC-06：往回跳之後停一下（引擎判定句尾），又接著講原本後面的話：留在前面。
    /// Mac 版第二輪就是讓「停頓就解除」才又失敗，停頓不能算數。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AfterJumpingBackAPauseThenTheOriginalNextWordsStayPut(bool streaming)
    {
        var session = ReadFirstSentences(4, streaming, out var now);
        var opening = string.Concat(TextElements.Split(Spoken[4])[..4]);
        Say(session, ref now, "", opening, streaming);
        var back = SentenceStart(1);

        session.JumpTo(back, now);
        session.ProcessTranscript(opening, now + 0.2); // 滾的時候就停嘴了：這一句到這裡結束
        session.EndOfUtterance();
        now += 1.5;
        SaySentence(session, ref now, Spoken[4][opening.Length..], streaming); // 停一下，又接著講原本後面的話
        SaySentence(session, ref now, Spoken[5], streaming);

        Assert.True(session.EffectiveCharacterCount == back, $"MAC-06：被拉回原本講的地方，{Describe(session.EffectiveCharacterCount)}");
    }

    /// <summary>MAC-08：往回跳、重念前面那句之後，直接跳念後面的段落：找回位置仍然有效，跟過去。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AfterRereadingTheJumpedToSentenceSkippingAheadIsFollowedAgain(bool streaming)
    {
        var session = ReadFirstSentences(4, streaming, out var now);

        session.JumpTo(SentenceStart(1), now);
        now += 1.5;
        SaySentence(session, ref now, Spoken[1], streaming);
        Assert.True(session.EffectiveCharacterCount == SentenceEnd(1), $"MAC-08：重念那句沒跟上，{Describe(session.EffectiveCharacterCount)}");

        SaySentence(session, ref now, Spoken[4], streaming);
        Assert.True(session.EffectiveCharacterCount == SentenceEnd(4), $"MAC-08：沒有找回位置，{Describe(session.EffectiveCharacterCount)}");
    }

    /// <summary>MAC-07（對照組）：往後跳，跳轉前那句的尾巴被改寫，接著念跳到的地方：跟現在一樣跟得上。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void JumpingAheadWithARevisedTailStillFollows(bool streaming)
    {
        var session = ReadFirstSentences(3, streaming, out var now);
        Say(session, ref now, "", Spoken[3], streaming);

        session.JumpTo(SentenceStart(5), now);
        session.ProcessTranscript(Spoken[3].Replace("带你", "代你"), now + 1.3);
        session.EndOfUtterance();
        now += 2;
        SaySentence(session, ref now, Spoken[5], streaming);

        Assert.True(session.EffectiveCharacterCount == Prompt.CharacterCount, $"MAC-07：往後跳之後沒跟上，{Describe(session.EffectiveCharacterCount)}");
    }

    /// <summary>對照組：往後跳不會暫停找回位置，跳到之後再跳念更後面的句子，照樣跟過去。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void JumpingAheadKeepsFindingThePlace(bool streaming)
    {
        var session = ReadFirstSentences(1, streaming, out var now);

        session.JumpTo(SentenceStart(2), now);
        now += 1.5;
        SaySentence(session, ref now, Spoken[4], streaming);

        Assert.True(session.EffectiveCharacterCount == SentenceEnd(4), $"往後跳之後找不回位置，{Describe(session.EffectiveCharacterCount)}");
    }

    /// <summary>
    /// 就定位時間：往回點前面兩個字想重講，嘴巴卻還把那句多講了一小段（引擎約慢半秒才吐出來），這些字不能在新位置被對上、把高亮往後拉；
    /// 同一句接著重念時，那些字也要當成已經講過剪掉，高亮只跟著重念的字走。
    /// 多講的要超過 5 個字：比對在辨識結果裡往後找 5 個單位，少於這個數會被直接跳過，測不出有沒有剪掉。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WordsSpokenWhileClickingBackAreNotMatched(bool streaming)
    {
        var session = ReadFirstSentences(1, streaming, out var now);
        var said = "每一张卡片只写"; // 第 2 句念到「寫」
        Say(session, ref now, "", said, streaming);
        var target = CharacterIn(1, 5); // 點回「只寫」重講

        session.JumpTo(target, now);
        string[] late = ["一个", "一个观点", "一个观点而且要"]; // 點下去之後，嘴巴還多講了「一個觀點而且要」
        for (var i = streaming ? 0 : late.Length - 1; i < late.Length; i++)
        {
            session.ProcessTranscript(said + late[i], now + 0.3 * (i + 1));
            Assert.True(session.EffectiveCharacterCount == target, $"點下去之後講的字被對上了，{Describe(session.EffectiveCharacterCount)}");
        }

        now += 1.2;
        var reread = "只写一个观点而且要能独立看懂";
        Say(session, ref now, said + late[^1], reread, streaming, afterEach: k =>
        {
            var limit = CharacterIn(1, 5 + k); // 重念到第 k 個字，高亮最多到下一個字的開頭
            Assert.True(session.EffectiveCharacterCount <= limit,
                $"重念到第 {k} 個字，高亮跑到前面去了，{Describe(session.EffectiveCharacterCount)}");
        });
        session.EndOfUtterance();
        Assert.True(session.EffectiveCharacterCount == SentenceEnd(1), $"重念沒跟上，{Describe(session.EffectiveCharacterCount)}");
    }
}
