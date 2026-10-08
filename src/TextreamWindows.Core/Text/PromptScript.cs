namespace TextreamWindows.Core.Text;

/// <summary>講稿裡的一個詞：中日韓一字一詞，其他語言以空白切開。位置以文字元素計。</summary>
public sealed record PromptWord(int Id, string Text, CharRange CharacterRange, bool IsAnnotation);

/// <summary>
/// 處理過的講稿。移植 Textream iOS 版的 <c>PromptScript</c>（<c>PromptTextProcessor.swift</c>）。
/// </summary>
public sealed class PromptScript
{
    private readonly int[] _utf16Offsets;

    public PromptScript(string rawText)
    {
        RawText = rawText;
        var rawWords = PromptTokenizer.SplitIntoWords(rawText);
        var flags = Annotations.Flags(rawWords);
        var words = new List<PromptWord>(rawWords.Count);
        var offset = 0;
        for (var index = 0; index < rawWords.Count; index++)
        {
            var elements = TextElements.Split(rawWords[index]);
            var range = new CharRange(offset, offset + elements.Length);
            var readable = elements.Any(TextElements.IsLetterOrNumber);
            words.Add(new PromptWord(index, rawWords[index], range, IsAnnotation: flags[index] || !readable));
            offset = range.End + 1;
        }

        Text = string.Join(' ', rawWords);
        Words = words;
        AnnotationRanges = Annotations.Ranges(Text);

        // 第 i 個文字元素在 Text 裡從第幾個 UTF-16 單位開始；多放一格給結尾
        var elementsOfText = TextElements.Split(Text);
        Elements = elementsOfText;
        _utf16Offsets = new int[elementsOfText.Length + 1];
        for (var i = 0; i < elementsOfText.Length; i++)
        {
            _utf16Offsets[i + 1] = _utf16Offsets[i] + elementsOfText[i].Length;
        }
    }

    /// <summary>瓦基原本的講稿（換行、空格都照原樣），畫面顯示用，見 <see cref="PromptDisplay"/>。</summary>
    public string RawText { get; }

    /// <summary>空白合併成單一空格、中日韓字之間補上空格之後的全文。所有位置都以它為準。</summary>
    public string Text { get; }

    public IReadOnlyList<PromptWord> Words { get; }

    /// <summary><see cref="Text"/> 切成文字元素，第 i 個就是位置 i 的字。</summary>
    public IReadOnlyList<string> Elements { get; }

    /// <summary>成對的 <c>[...]</c> 在 <see cref="Text"/> 裡的位置（含括號）。</summary>
    public IReadOnlyList<CharRange> AnnotationRanges { get; }

    /// <summary>全文有幾個文字元素。不要用 <c>Text.Length</c>，那是 UTF-16 單位數。</summary>
    public int CharacterCount => _utf16Offsets.Length - 1;

    /// <summary>
    /// 定速捲動用的「讀到第幾個詞（可以有小數）」換成文字元素位置。小數部分照詞的長度比例落在詞裡面；
    /// 中文一字一詞，所以小數部分不會落在字中間。對應 Swift 的 <c>characterOffset(forWordProgress:)</c>。
    /// </summary>
    public int CharacterOffset(double wordProgress)
    {
        if (Words.Count == 0)
        {
            return 0;
        }
        var clamped = Math.Clamp(wordProgress, 0, Words.Count);
        var wholeWord = Math.Min((int)clamped, Words.Count);
        if (wholeWord >= Words.Count)
        {
            return CharacterCount;
        }
        var word = Words[wholeWord];
        var fraction = clamped - wholeWord;
        return Math.Min(CharacterCount, word.CharacterRange.Start + (int)(word.CharacterRange.Length * fraction));
    }

    /// <summary><see cref="CharacterOffset"/> 的反方向。對應 Swift 的 <c>wordProgress(forCharacterOffset:)</c>。</summary>
    public double WordProgress(int characterOffset)
    {
        var clamped = Math.Clamp(characterOffset, 0, CharacterCount);
        foreach (var word in Words)
        {
            if (clamped <= word.CharacterRange.End)
            {
                var position = Math.Max(0, clamped - word.CharacterRange.Start);
                return word.Id + (double)position / Math.Max(1, word.CharacterRange.Length);
            }
        }
        return Words.Count;
    }

    /// <summary>把文字元素位置換成 UTF-16 位置，給 WPF 這類以 <see cref="string"/> 索引的 API 用。對應 Swift 的 <c>nsRange(forCharacterRange:)</c>。</summary>
    public (int Start, int Length) ToUtf16(CharRange range)
    {
        var lower = _utf16Offsets[Math.Clamp(range.Start, 0, CharacterCount)];
        var upper = _utf16Offsets[Math.Clamp(range.End, 0, CharacterCount)];
        return (lower, Math.Max(0, upper - lower));
    }
}
