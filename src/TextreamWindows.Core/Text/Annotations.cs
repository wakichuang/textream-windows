namespace TextreamWindows.Core.Text;

/// <summary>
/// 講稿裡的 <c>[...]</c> 標註（例如「[看鏡頭]」）：給講者看、不用念出來。
/// 移植 Textream <c>SpeechTextAlignment.swift</c> 的 <c>annotationFlags</c> 與 <c>annotationRanges</c>。
/// </summary>
public static class Annotations
{
    /// <summary>
    /// 每個詞是不是落在標註裡。以「[」開頭、而且後面某處有「]」的詞開啟標註，含「]」的詞結束；
    /// 後面沒有「]」的「[」當一般文字，免得一個打錯的括號把後面整篇吃掉。
    /// </summary>
    public static bool[] Flags(IReadOnlyList<string> words)
    {
        var closingAtOrAfter = new bool[words.Count];
        var hasClosing = false;
        for (var index = words.Count - 1; index >= 0; index--)
        {
            hasClosing |= words[index].Contains(']');
            closingAtOrAfter[index] = hasClosing;
        }

        var flags = new bool[words.Count];
        var isInsideAnnotation = false;
        for (var index = 0; index < words.Count; index++)
        {
            var beginsAnnotation = words[index].StartsWith('[') && closingAtOrAfter[index];
            flags[index] = isInsideAnnotation || beginsAnnotation;
            if (beginsAnnotation)
            {
                isInsideAnnotation = true;
            }
            if (isInsideAnnotation && words[index].Contains(']'))
            {
                isInsideAnnotation = false;
            }
        }
        return flags;
    }

    /// <summary>成對括號的位置（含括號，以文字元素計）。不處理巢狀：第一個「[」配上它之後第一個「]」，落單的「]」忽略。</summary>
    public static IReadOnlyList<CharRange> Ranges(string text)
    {
        var ranges = new List<CharRange>();
        int? openingIndex = null;
        var elements = TextElements.Split(text);
        for (var index = 0; index < elements.Length; index++)
        {
            if (elements[index] == "[" && openingIndex is null)
            {
                openingIndex = index;
            }
            else if (elements[index] == "]" && openingIndex is int start)
            {
                ranges.Add(new CharRange(start, index + 1));
                openingIndex = null;
            }
        }
        return ranges;
    }
}
