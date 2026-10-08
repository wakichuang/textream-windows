using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TextreamWindows.Core.Chinese;

/// <summary>正規化後的一個詞：<see cref="Text"/> 是比對用的文字，來自輸入的第 <see cref="Start"/> 個詞起連續 <see cref="Count"/> 個詞。</summary>
public readonly record struct NormalizedWord(string Text, int Start, int Count);

/// <summary>
/// 數字正規化（計畫書第 6 節）：國字數字與阿拉伯數字都統一成阿拉伯數字，原稿與辨識結果兩邊套同一套。
/// 輸入是 <see cref="Text.PromptTokenizer"/> 切好的詞；連續的數字詞併成一個，記住它從哪幾個詞來，高亮才放得回原稿。
/// </summary>
/// <remarks>
/// 「一起」會變成「1起」也沒關係，兩邊一致就對得上。
/// 大寫數字（壹貳參肆伍陸柒捌玖拾佰仟）刻意不轉：參、陸、伍、拾是常用字，轉了反而蓋掉同音比對（陸／路）。
/// </remarks>
public static partial class ChineseNumbers
{
    private static readonly Dictionary<char, int> Digits = new()
    {
        ['零'] = 0, ['〇'] = 0, ['○'] = 0,
        ['一'] = 1, ['二'] = 2, ['兩'] = 2, ['两'] = 2, ['三'] = 3, ['四'] = 4,
        ['五'] = 5, ['六'] = 6, ['七'] = 7, ['八'] = 8, ['九'] = 9,
    };

    private static readonly Dictionary<char, int> SmallUnits = new() { ['十'] = 10, ['百'] = 100, ['千'] = 1000 };

    private static readonly Dictionary<char, decimal> BigUnits = new()
    {
        ['萬'] = 10_000m, ['万'] = 10_000m, ['億'] = 100_000_000m, ['亿'] = 100_000_000m,
    };

    /// <summary>廿＝二十、卅＝三十、卌＝四十。</summary>
    private static readonly Dictionary<char, int> Tens = new() { ['廿'] = 20, ['卅'] = 30, ['卌'] = 40 };

    public static IReadOnlyList<NormalizedWord> Normalize(IReadOnlyList<string> words)
    {
        var result = new List<NormalizedWord>(words.Count);
        var i = 0;
        while (i < words.Count)
        {
            if (!IsNumberWord(words[i]))
            {
                result.Add(new NormalizedWord(words[i], i, 1));
                i++;
                continue;
            }

            var end = FindRunEnd(words, i);
            result.Add(new NormalizedWord(Evaluate(words, i, end), i, end - i));
            i = end;
        }
        return result;
    }

    /// <summary>從 <paramref name="start"/> 起的數字連續到哪裡（不含）。小數點只認一個，而且後面要接數字；小數部分後面可以再接萬、億（一點五萬）。</summary>
    private static int FindRunEnd(IReadOnlyList<string> words, int start)
    {
        var end = start + 1;
        var afterPoint = false;
        while (end < words.Count)
        {
            var word = words[end];
            if (!afterPoint && IsPoint(word) && end + 1 < words.Count && IsDigitWord(words[end + 1]))
            {
                afterPoint = true;
                end++;
            }
            else if (afterPoint)
            {
                if (IsDigitWord(word))
                {
                    end++;
                    continue;
                }
                if (IsBigUnit(word))
                {
                    end++;
                }
                break;
            }
            else if (IsNumberWord(word))
            {
                end++;
            }
            else
            {
                break;
            }
        }
        return end;
    }

    private static string Evaluate(IReadOnlyList<string> words, int start, int end)
    {
        var run = new List<string>(end - start);
        for (var i = start; i < end; i++)
        {
            run.Add(words[i]);
        }

        var point = run.FindIndex(IsPoint);
        var integerPart = point < 0 ? run : run[..point];
        var fractionPart = point < 0 ? [] : run[(point + 1)..];

        decimal? multiplier = null;
        if (fractionPart.Count > 0 && IsBigUnit(fractionPart[^1]))
        {
            multiplier = BigUnits[fractionPart[^1][0]];
            fractionPart = fractionPart[..^1];
        }
        var fractionDigits = string.Concat(fractionPart.Select(DigitsOf));

        var positional = integerPart.Any(w => w.Length == 1 && (SmallUnits.ContainsKey(w[0]) || BigUnits.ContainsKey(w[0]) || Tens.ContainsKey(w[0])));
        if (!positional && multiplier is null)
        {
            // 逐字念（二零二六、零九一二）或純阿拉伯數字：照字面接起來，保留開頭與結尾的零
            var integerDigits = string.Concat(integerPart.Select(DigitsOf));
            return point < 0 ? integerDigits : $"{integerDigits}.{fractionDigits}";
        }

        var value = positional ? EvaluatePositional(integerPart) : decimal.Parse(string.Concat(integerPart.Select(DigitsOf)), CultureInfo.InvariantCulture);
        if (fractionDigits.Length > 0)
        {
            value += decimal.Parse($"0.{fractionDigits}", CultureInfo.InvariantCulture);
        }
        if (multiplier is { } m)
        {
            value *= m;
        }
        return value.ToString("0.############", CultureInfo.InvariantCulture);
    }

    /// <summary>有位數的念法：一千二百三十四、兩千零二十六、三萬五千、一千五（＝1500）。</summary>
    private static decimal EvaluatePositional(IReadOnlyList<string> words)
    {
        decimal total = 0, section = 0;
        decimal? number = null;
        decimal lastUnit = 1;
        var zeroSinceUnit = false;

        foreach (var word in words)
        {
            if (TryParseArabic(word, out var arabic))
            {
                number = arabic;
                continue;
            }

            var c = word[0];
            if (Digits.TryGetValue(c, out var digit))
            {
                if (digit == 0)
                {
                    zeroSinceUnit = true;
                }
                else
                {
                    number = number is { } n ? n * 10 + digit : digit;
                }
            }
            else if (SmallUnits.TryGetValue(c, out var small))
            {
                section += (number ?? 1) * small;
                (number, lastUnit, zeroSinceUnit) = (null, small, false);
            }
            else if (Tens.TryGetValue(c, out var tens))
            {
                section += tens;
                (number, lastUnit, zeroSinceUnit) = (null, 10, false);
            }
            else if (BigUnits.TryGetValue(c, out var big))
            {
                if (section == 0 && number is null)
                {
                    total = total > 0 ? total * big : big; // 萬一、一萬億
                }
                else
                {
                    total += (section + (number ?? 0)) * big;
                }
                (section, number, lastUnit, zeroSinceUnit) = (0, null, big, false);
            }
        }

        if (number is { } last)
        {
            // 單位後面直接接一個數字、中間沒有零：一千五＝1500、兩萬三＝23000、二十五＝25
            section += !zeroSinceUnit && lastUnit >= 10 ? last * (lastUnit / 10) : last;
        }
        return total + section;
    }

    private static bool IsNumberWord(string word) =>
        TryParseArabic(word, out _)
        || (word.Length == 1 && (Digits.ContainsKey(word[0]) || SmallUnits.ContainsKey(word[0]) || BigUnits.ContainsKey(word[0]) || Tens.ContainsKey(word[0])));

    /// <summary>小數點後面能接的：國字的零到九，或不帶逗號、小數點的阿拉伯整數。</summary>
    private static bool IsDigitWord(string word) =>
        (word.Length == 1 && Digits.ContainsKey(word[0])) || ArabicInteger().IsMatch(Fold(word));

    private static bool IsPoint(string word) => word is "點" or "点";

    private static bool IsBigUnit(string word) => word.Length == 1 && BigUnits.ContainsKey(word[0]);

    /// <summary>一個數字詞照字面換成阿拉伯數字字串：國字一個字一位，阿拉伯數字去掉千分位逗號。</summary>
    private static string DigitsOf(string word) =>
        word.Length == 1 && Digits.TryGetValue(word[0], out var digit)
            ? digit.ToString(CultureInfo.InvariantCulture)
            : Fold(word).Replace(",", "", StringComparison.Ordinal);

    private static bool TryParseArabic(string word, out decimal value)
    {
        var folded = Fold(word);
        if (ArabicNumber().IsMatch(folded))
        {
            value = decimal.Parse(folded.Replace(",", "", StringComparison.Ordinal), CultureInfo.InvariantCulture);
            return true;
        }
        value = 0;
        return false;
    }

    /// <summary>全形數字、逗號、句點（２，０００．５）先轉半形。</summary>
    private static string Fold(string word) => word.Normalize(NormalizationForm.FormKC);

    [GeneratedRegex(@"^[0-9]+(,[0-9]{3})*(\.[0-9]+)?$")]
    private static partial Regex ArabicNumber();

    [GeneratedRegex(@"^[0-9]+$")]
    private static partial Regex ArabicInteger();
}
