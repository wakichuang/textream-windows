namespace TextreamWindows.Core.Text;

/// <summary>
/// Ctrl+Alt+↑ ／ ↓ 前後跳一句（計畫書第 5 階段步驟 4）。句子以句末標點（。！？；…）與換行分開，逗號不算。
/// 「上一句」像音樂播放器的「上一首」：這一句已經念了兩個字以上，先回到這一句的開頭；剛開始念才回到上一句。
/// </summary>
public static class SentenceNavigation
{
    private const string SentenceEnds = "。！？；…!?;";

    /// <summary>每一句第一個要念的字的位置（文字元素），由前到後。</summary>
    public static IReadOnlyList<int> Starts(PromptScript script, PromptDisplay display)
    {
        var starts = new List<int>();
        var newSentence = true;
        PromptWord? previous = null;
        foreach (var word in script.Words)
        {
            if (previous is not null && display.LineBreakBetween(previous, word))
            {
                newSentence = true;
            }
            previous = word;
            if (word.IsAnnotation)
            {
                newSentence |= word.Text.AsSpan().ContainsAny(SentenceEnds);
                continue;
            }
            if (newSentence)
            {
                starts.Add(word.CharacterRange.Start);
                newSentence = false;
            }
        }
        return starts;
    }

    /// <summary>下一句的開頭；已經在最後一句就不動（跳到結尾會被當成讀完）。</summary>
    public static int Next(PromptScript script, PromptDisplay display, int progress)
    {
        foreach (var start in Starts(script, display))
        {
            if (start > progress)
            {
                return start;
            }
        }
        return progress;
    }

    /// <summary>這一句的開頭（已經念了兩個字以上），或上一句的開頭（剛開始念）。</summary>
    public static int Previous(PromptScript script, PromptDisplay display, int progress)
    {
        var starts = Starts(script, display);
        var current = -1;
        for (var i = 0; i < starts.Count && starts[i] <= progress; i++)
        {
            current = i;
        }
        if (current < 0)
        {
            return 0;
        }
        var readInThisSentence = script.Elements.Skip(starts[current]).Take(progress - starts[current]).Count(TextElements.IsLetterOrNumber);
        if (readInThisSentence > 1)
        {
            return starts[current];
        }
        return current > 0 ? starts[current - 1] : 0;
    }
}
