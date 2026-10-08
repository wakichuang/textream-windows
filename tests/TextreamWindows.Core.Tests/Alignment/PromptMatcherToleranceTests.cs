using TextreamWindows.Core.Alignment;
using TextreamWindows.Core.Text;

namespace TextreamWindows.Core.Tests.Alignment;

/// <summary>
/// 容錯：瓦基用提詞機大多照稿念，但會即興插話、跳過子標題、跳過不想念的句子、臨時改講法再接回原稿
/// （2026-10-08 瓦基說明，記憶卡 user_teleprompter_reading_style）。原版只有前後 5 個單位的容錯，接不住這些。
/// 這些情境依計畫書第 7.3 節的編號接著編 ZH-15 起。
/// </summary>
public class PromptMatcherToleranceTests
{
    /// <summary>
    /// 模擬 sherpa-onnx 串流：每一句的辨識結果每次多 2 個字（部分結果），一句講完就 <see cref="PromptMatcher.RestartFromCurrentProgress"/>。
    /// 回傳每一句講完時的位置；過程中只要倒退一次就失敗。
    /// </summary>
    private static List<int> Feed(PromptMatcher matcher, params string[] utterances)
    {
        var afterEach = new List<int>();
        var last = matcher.RecognizedCharacterCount;
        foreach (var utterance in utterances)
        {
            var elements = TextElements.Split(utterance);
            for (var k = Math.Min(2, elements.Length); k <= elements.Length; k = k == elements.Length ? k + 1 : Math.Min(k + 2, elements.Length))
            {
                var progress = matcher.Match(string.Concat(elements[..k]));
                Assert.True(progress >= last, $"倒退了：{last} → {progress}（聽到「{string.Concat(elements[..k])}」）");
                last = progress;
            }
            matcher.RestartFromCurrentProgress();
            afterEach.Add(last);
        }
        return afterEach;
    }

    /// <summary>位置換成「讀到第幾個可讀字」（不算空白、標點），比較好講「動了幾個字」。</summary>
    private static int Readable(PromptScript prompt, int offset) =>
        prompt.Elements.Take(offset).Count(TextElements.IsLetterOrNumber);

    private static void AssertReachesEnd(PromptScript prompt, int progress, string id) =>
        Assert.True(prompt.CharacterCount == progress,
            $"{id}：高亮停在可讀字 {Readable(prompt, progress)}／{Readable(prompt, prompt.CharacterCount)}，停在「{string.Concat(prompt.Elements.Skip(progress).Take(12))}」之前");

    [Fact]
    public void ZH15_SkippingASubheadingFindsTheBodyText()
    {
        var prompt = new PromptScript("所以這本書最後想說的是，好好照顧自己。\n\n## 四個步驟，照顧你的陰鬱小孩\n\n第一步是認出你的保護策略，看見自己在逃避什麼。");

        var progress = Feed(new PromptMatcher(prompt),
            "所以这本书最后想说的是好好照顾自己",
            "第一步是认出你的保护策略看见自己在逃避什么");

        AssertReachesEnd(prompt, progress[^1], "ZH-15");
    }

    [Fact]
    public void ZH16_SkippingASentenceFindsTheNextOneRead()
    {
        var prompt = new PromptScript("今天要聊三件事。第一件事是閱讀的習慣，很多人覺得自己沒有時間讀書。第二件事是寫作，寫作其實是在整理思考。");

        var progress = Feed(new PromptMatcher(prompt),
            "今天要聊三件事",
            "第二件事是写作写作其实是在整理思考");

        AssertReachesEnd(prompt, progress[^1], "ZH-16");
    }

    [Fact]
    public void ZH17_AdLibBetweenSentencesDoesNotMoveThenRejoins()
    {
        var prompt = new PromptScript("我讀完這本書之後，最大的收穫是學會了覺察。覺察就是有意識地看見自己的反應，然後選擇怎麼回應。");

        var progress = Feed(new PromptMatcher(prompt),
            "我读完这本书之后最大的收获是学会了觉察",
            "对啊这个真的很重要我上礼拜还跟我太太聊到这件事情",
            "觉察就是有意识地看见自己的反应然后选择怎么回应");

        Assert.True(Readable(prompt, progress[1]) - Readable(prompt, progress[0]) <= 2,
            $"ZH-17：插話期間高亮從可讀字 {Readable(prompt, progress[0])} 動到 {Readable(prompt, progress[1])}");
        AssertReachesEnd(prompt, progress[^1], "ZH-17");
    }

    [Fact]
    public void ZH18_AdLibInsideOneUtteranceRejoins()
    {
        // 沒有停頓，插話跟原稿在同一句辨識結果裡
        var prompt = new PromptScript("我讀完這本書之後，最大的收穫是學會了覺察。覺察就是有意識地看見自己的反應，然後選擇怎麼回應。");

        var progress = Feed(new PromptMatcher(prompt),
            "我读完这本书之后最大的收获是学会了觉察对啊这个真的很重要我上礼拜还跟我太太聊到觉察就是有意识地看见自己的反应然后选择怎么回应");

        AssertReachesEnd(prompt, progress[^1], "ZH-18");
    }

    [Fact]
    public void ZH19_RewordedSentenceThenBackToTheScript()
    {
        var prompt = new PromptScript("這種習慣不是一天養成的，它需要時間慢慢累積。所以不要急，先給自己一點耐心，再一步一步往前走。");

        var progress = Feed(new PromptMatcher(prompt),
            "这种习惯不是一天养成的",
            "就是说你要花很多功夫去一点一点地堆起来",
            "所以不要急先给自己一点耐心再一步一步往前走");

        AssertReachesEnd(prompt, progress[^1], "ZH-19");
    }

    [Fact]
    public void ZH20_LongAdLibDoesNotDrift()
    {
        // 計畫書第 7.4 節第 5 條：閒聊期間高亮往前跳的距離不超過 10 個字
        var prompt = new PromptScript(
            "很多人一輩子都在別人身上找安全感，找伴侶、找朋友、找一份穩定的工作。" +
            "可是作者說，真正的安全感是一個內在的家，要靠我們自己一磚一瓦蓋起來。" +
            "這個家蓋好了，外面再怎麼風吹雨打，你都有地方可以回去。");
        var matcher = new PromptMatcher(prompt);

        var progress = Feed(matcher,
            "很多人一辈子都在别人身上找安全感找伴侣找朋友找一份稳定的工作",
            "好那我们先休息一下喝口水",
            "其实我昨天晚上睡得不太好所以今天声音有点哑",
            "等一下录完我还要去接小孩放学我们继续");

        Assert.True(Readable(prompt, progress[^1]) - Readable(prompt, progress[0]) <= 10,
            $"ZH-20：閒聊三句之後高亮從可讀字 {Readable(prompt, progress[0])} 跑到 {Readable(prompt, progress[^1])}");

        var rejoined = Feed(matcher, "可是作者说真正的安全感是一个内在的家要靠我们自己一砖一瓦盖起来");
        Assert.True(Readable(prompt, rejoined[^1]) >= Readable(prompt, progress[0]) + 30, "ZH-20：閒聊完回到原稿要接得上");
    }

    [Fact]
    public void ZH21_CommonPhrasesInAnAdLibDoNotJumpFarAhead()
    {
        // 插話裡的「我覺得」「這本書」在後面的原稿各出現一次，但不是連在一起：不能跳過去
        var prompt = new PromptScript(
            "第一段先講背景，作者是一位心理治療師，在德國執業超過二十年。" +
            "第二段講方法，她把人的內心分成兩個小孩和一個大人，用很生活化的例子說明。" +
            "第三段是心得，我覺得很多人都需要這樣的語言。最後推薦這本書給正在育兒的朋友。");
        var matcher = new PromptMatcher(prompt);

        var progress = Feed(matcher,
            "第一段先讲背景作者是一位心理治疗师在德国执业超过二十年",
            "我觉得这本书不错啦");

        Assert.True(Readable(prompt, progress[1]) - Readable(prompt, progress[0]) <= 2,
            $"ZH-21：插話讓高亮從可讀字 {Readable(prompt, progress[0])} 跳到 {Readable(prompt, progress[1])}");
    }

    /// <summary>
    /// 真實案例（一集看稿講的 Podcast 講稿第 14～20 行、模型 A 的第 18～20 句辨識結果）：瓦基念子標題時說了「有什麼不一樣的地方」，
    /// 2026-10-08 第一版的找回位置被常用詞組「不一樣的地方」騙去 200 字後的「有三個不一樣的地方」，高亮領先講者約 30 秒。
    /// 他接著把中間那段都念了，所以高亮不能跳過「沒錯，兩本書是同一棵樹」。
    /// </summary>
    [Fact]
    public void ZH23_CommonPhraseDoesNotLureTheAnchorFarAhead()
    {
        var prompt = new PromptScript(
            "簡單來說，這是一個站在百年傳統上的簡化工具。作者希望透過一個簡單易懂的比喻，幫你看懂自己為什麼會那樣反應。\n" +
            "## 跟《蛤蟆先生去看心理師》有什麼不一樣？\n" +
            "以前聽過我分享《蛤蟆先生去看心理師》的朋友，讀到這裡應該會覺得很熟悉。\n" +
            "沒錯，兩本書是同一棵樹上的兩根分枝。\n" +
            "「兒童自我、成人自我、父母自我」正是溝通分析（Transactional Analysis）的用語，也就是蒼鷺幫蛤蟆諮商的那套方法。史塔爾在書中沒有特別點名，但兩本書的根是同一條。\n" +
            "兩本書的共同點很清楚：「童年經驗」會形塑我們今天的反應，而最終的出口都在「大人」這個狀態，也就是說，你要為自己負責。\n" +
            "這兩本書之間，我認為有三個不一樣的地方。");
        var matcher = new PromptMatcher(prompt);

        var progress = Feed(matcher,
            "所以简单来讲这是一个站在百年传统上的一个简化的一个方法然后作者他希望可以透过一个",
            "简单好懂的比喻来帮你去看懂自己为什么会做出那些反应那接下来我们来看一下这本书跟蛤蟆先生去看心理师有什么不一样的地方因为以前很多朋友可能听过我分享蛤蟆先生去看心理师那本书嘛很多朋友都很喜欢这一本那读到这里的时候应该会");

        var notYetRead = prompt.Text.IndexOf("沒", StringComparison.Ordinal); // 「沒錯」：第 19 句講完時還沒念到
        var limit = prompt.Elements.Count(e => e.Length > 0) - TextElements.Split(prompt.Text[notYetRead..]).Length;
        Assert.True(progress[^1] <= limit,
            $"ZH-23：第 19 句講完，高亮跑到「{string.Concat(prompt.Elements.Skip(Math.Max(0, progress[^1] - 12)).Take(12))}」之後，超過了還沒念的「沒錯」");
    }

    [Fact]
    public void ZH22_SkippingAFewWordsMidSentenceKeepsUp()
    {
        var prompt = new PromptScript("我覺得這本書最大的價值，在於它給了我們一套很好懂的語言。");

        var progress = Feed(new PromptMatcher(prompt), "我觉得这本书最大的价值在于给了我们好懂的语言");

        AssertReachesEnd(prompt, progress[^1], "ZH-22");
    }
}
