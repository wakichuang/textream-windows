using System.Text;
using TextreamWindows.Core.Chinese;
using TextreamWindows.Core.Text;

namespace TextreamWindows.Core.Alignment;

internal enum MatchUnitKind
{
    /// <summary>一個中日韓字，比讀音集合。</summary>
    Han,

    /// <summary>英文等以空白分隔的詞，比 NFKC＋小寫後的字母數字，允許模糊比對。</summary>
    Word,

    /// <summary>正規化成阿拉伯數字的數字（<see cref="ChineseNumbers"/>），要完全相同。</summary>
    Number,
}

/// <summary>
/// 字元層比對的一格。中文一字一格；英文一個字母一格；數字一位一格。
/// <see cref="Start"/>／<see cref="End"/> 是它在原稿的位置；數字只有最後一位的 <see cref="End"/> 到數字結尾，前面幾位都停在開頭，
/// 免得只聽到「二零」就把「2026」算成讀了一半。
/// </summary>
internal readonly record struct MatchChar(bool IsHan, string Key, IReadOnlyCollection<string> Sounds, int Start, int End);

/// <summary>
/// 詞層比對的一個單位（計畫書第 6.1 節）。原稿與辨識結果走同一條路：斷字 → 數字正規化 → 轉成單位，
/// 每個單位記住它在原稿的位置範圍，比完才放得回原稿。
/// </summary>
internal sealed record MatchUnit(
    MatchUnitKind Kind,
    string Key,
    IReadOnlyCollection<string> Sounds,
    CharRange Source,
    bool IsAnnotation,
    IReadOnlyList<MatchChar> Characters)
{
    /// <param name="splitHyphens">English：有連字號的詞拆成幾個單位（one-eighth → one、eighth），辨識結果不會有連字號（EN-03）</param>
    public static List<MatchUnit> FromScript(PromptScript script, PinyinTable pinyin, bool splitHyphens = false)
    {
        var words = script.Words;
        var units = new List<MatchUnit>(words.Count);
        foreach (var normalized in ChineseNumbers.Normalize(words.Select(w => w.Text).ToList()))
        {
            var first = words[normalized.Start];
            var last = words[normalized.Start + normalized.Count - 1];
            var range = new CharRange(first.CharacterRange.Start, last.CharacterRange.End);
            var unit = Create(normalized.Text, range, pinyin);
            // 標點、emoji 這類沒有字母數字的詞，PromptScript 已經標成標註
            if (first.IsAnnotation || unit.Key.Length == 0)
            {
                units.Add(unit with { IsAnnotation = true });
            }
            else if (splitHyphens && unit.Kind == MatchUnitKind.Word)
            {
                units.AddRange(SplitOnHyphens(normalized.Text, range, pinyin));
            }
            else
            {
                units.Add(unit);
            }
        }
        return units;
    }

    /// <summary>辨識結果沒有原稿位置，範圍一律填 0；沒有字母數字的詞（標點）直接丟掉。</summary>
    public static List<MatchUnit> FromTranscript(string transcript, PinyinTable pinyin, bool splitHyphens = false) =>
        ChineseNumbers.Normalize(PromptTokenizer.SplitIntoWords(transcript))
            .SelectMany(w =>
            {
                var range = new CharRange(0, TextElements.Split(w.Text).Length);
                var unit = Create(w.Text, range, pinyin);
                return splitHyphens && unit.Kind == MatchUnitKind.Word ? SplitOnHyphens(w.Text, range, pinyin) : [unit];
            })
            .Where(u => u.Key.Length > 0)
            .ToList();

    /// <summary>以連字號切開一個英文詞，每段一個單位、位置照原稿；沒有字母數字的段落不要。</summary>
    private static IEnumerable<MatchUnit> SplitOnHyphens(string text, CharRange range, PinyinTable pinyin)
    {
        var elements = TextElements.Split(text);
        var start = 0;
        for (var i = 0; i <= elements.Length; i++)
        {
            if (i < elements.Length && elements[i] is not ("-" or "\u2010" or "\u2011"))
            {
                continue;
            }
            if (i > start)
            {
                var piece = Create(string.Concat(elements[start..i]), new CharRange(range.Start + start, range.Start + i), pinyin);
                if (piece.Key.Length > 0)
                {
                    yield return piece;
                }
            }
            start = i + 1;
        }
    }

    private static MatchUnit Create(string text, CharRange range, PinyinTable pinyin)
    {
        if (IsNumber(text))
        {
            var digits = new MatchChar[text.Length];
            for (var i = 0; i < text.Length; i++)
            {
                var end = i == text.Length - 1 ? range.End : range.Start;
                digits[i] = new MatchChar(false, text[i].ToString(), [], range.Start, end);
            }
            return new MatchUnit(MatchUnitKind.Number, text, [], range, false, digits);
        }

        var elements = TextElements.Split(text);
        if (elements.Length == 1 && PromptTokenizer.IsCjk(Rune.GetRuneAt(text, 0)))
        {
            var folded = text.Normalize(NormalizationForm.FormKC); // 相容表意字（U+F900 區）換成一般字再查讀音
            var sounds = pinyin.SoundKeys(folded);
            return new MatchUnit(MatchUnitKind.Han, folded, sounds, range, false, [new MatchChar(true, folded, sounds, range.Start, range.End)]);
        }

        // 英文詞：每個文字元素先 NFKC（ＡＩ → AI）再轉小寫，只留字母數字；位置照原稿的文字元素算
        var characters = new List<MatchChar>(elements.Length);
        for (var i = 0; i < elements.Length; i++)
        {
            var folded = elements[i].Normalize(NormalizationForm.FormKC).ToLowerInvariant();
            if (!TextElements.IsLetterOrNumber(folded))
            {
                continue;
            }
            foreach (var piece in TextElements.Split(folded))
            {
                characters.Add(new MatchChar(false, piece, [], range.Start + i, range.Start + i + 1));
            }
        }
        var key = string.Concat(characters.Select(c => c.Key));
        return new MatchUnit(MatchUnitKind.Word, key, [], range, false, characters);
    }

    /// <summary><see cref="ChineseNumbers"/> 輸出的數字只會是 0～9 和小數點。</summary>
    private static bool IsNumber(string text) =>
        text.Length > 0 && text.Any(char.IsAsciiDigit) && text.All(c => char.IsAsciiDigit(c) || c == '.');
}
