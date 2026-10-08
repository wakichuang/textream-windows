namespace TextreamWindows.Core.Text;

/// <summary>
/// 浮層上黃色的那一段（瓦基 2026-10-08：只標一個中文字太少，一次標 2～4 個字比較容易跟上）。
/// 從目前要念的字往後標，最多 <see cref="MaxCharacters"/> 個字；遇到標點、標註、換行就停，不跨到下一句或下一行。
/// 只剩一個字就到句尾時，往前補這一句裡剛念的那個字，湊滿 <see cref="MinCharacters"/> 個。
/// 英文詞不拆開：一個長的英文詞自己就是一段。字數以文字元素計。
/// </summary>
public static class ReadingHighlight
{
    public const int MinCharacters = 2;

    public const int MaxCharacters = 4;

    /// <summary>位置 <paramref name="progress"/>（文字元素）時要標黃的範圍；念完了回傳結尾的空範圍。</summary>
    public static CharRange Range(PromptScript script, PromptDisplay display, int progress)
    {
        var words = script.Words;
        var first = words.FirstOrDefault(w => w.CharacterRange.End > progress && !w.IsAnnotation);
        if (first is null)
        {
            return new CharRange(script.CharacterCount, script.CharacterCount);
        }

        var start = Math.Max(progress, first.CharacterRange.Start);
        var end = first.CharacterRange.End;
        var count = end - start;
        for (var i = first.Id + 1; i < words.Count && count < MaxCharacters; i++)
        {
            var next = words[i];
            if (next.IsAnnotation || display.LineBreakBetween(words[i - 1], next) || count + next.CharacterRange.Length > MaxCharacters)
            {
                break;
            }
            end = next.CharacterRange.End;
            count += next.CharacterRange.Length;
        }

        if (count < MinCharacters && start > first.CharacterRange.Start)
        {
            start = first.CharacterRange.Start; // 停在英文詞中間：整個詞標起來就好
        }
        else if (count < MinCharacters && first.Id > 0)
        {
            var previous = words[first.Id - 1];
            if (!previous.IsAnnotation && !display.LineBreakBetween(previous, first))
            {
                start = previous.CharacterRange.Start;
            }
        }
        return new CharRange(start, end);
    }
}
