using TextreamWindows.Core.Text;

namespace TextreamWindows.Core.Alignment;

/// <summary>
/// 比對結果的後處理。移植 Textream <c>SpeechTextAlignment.swift</c> 的 <c>advancePastAnnotations</c>、<c>bestOffset</c>、<c>shouldCommit</c>
/// （標註的判斷在 <see cref="Annotations"/>）。
/// </summary>
public static class SpeechTextAlignment
{
    /// <summary>位置落在 <c>[...]</c> 裡、或前面只隔著空白就是 <c>[...]</c>，就跳到標註後面。</summary>
    public static int AdvancePastAnnotations(PromptScript script, int offset)
    {
        var elements = script.Elements;
        var ranges = script.AnnotationRanges;
        var current = Math.Clamp(offset, 0, elements.Count);
        var skippedAnnotation = false;

        while (current < elements.Count)
        {
            var inside = ranges.FirstOrDefault(r => r.Contains(current));
            if (!inside.IsEmpty)
            {
                current = inside.End;
                skippedAnnotation = true;
                continue;
            }

            var next = current;
            while (next < elements.Count && TextElements.IsWhitespace(elements[next]))
            {
                next++;
            }

            var following = ranges.FirstOrDefault(r => r.Start == next);
            if (!following.IsEmpty)
            {
                current = following.End;
                skippedAnnotation = true;
                continue;
            }

            return skippedAnnotation ? next : current;
        }
        return current;
    }

    /// <summary>
    /// 合併字元層與詞層的結果（都是從比對起點算起的字數）。差不多就取平均；差很多就信走得遠的那個：
    /// 詞層是依序、只進不退地掃，快讀時靠它追上，不被長距離容易失準的字元層拖回來。
    /// </summary>
    public static int BestOffset(int characterResult, int wordResult, int agreementTolerance = 20)
    {
        if (Math.Abs(characterResult - wordResult) <= agreementTolerance)
        {
            return (characterResult + wordResult) / 2;
        }
        return Math.Max(characterResult, wordResult);
    }

    /// <summary>
    /// 要不要採用這次的候選位置。兩層都各自往前走 → 採用（兩層同時幻覺出一樣的前進不太可能）；
    /// 只有一層往前走風險較高，要嘛步子小（15 字內）、要嘛最近三次結果有兩次落在附近（2/3 投票）才採用。
    /// </summary>
    public static bool ShouldCommit(int characterResult, int wordResult, int current, int rawCandidate, int candidate, bool confirmed)
    {
        var bothProgressed = Math.Min(characterResult, wordResult) > 0;
        var skippedAnnotation = candidate > rawCandidate;
        var smallStep = candidate - current <= 15;
        return bothProgressed || skippedAnnotation || confirmed || smallStep;
    }
}
