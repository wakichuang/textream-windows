using System.Text;

namespace TextreamWindows.Core.Text;

/// <summary>
/// 斷字。移植 Textream iOS 版的 <c>splitPromptTextIntoWords</c>：
/// 換行與連續空白都當成一個分隔；含中日韓字的片段再拆成一字一詞，夾在中間的非中日韓字元黏成一詞。
/// </summary>
public static class PromptTokenizer
{
    public static IReadOnlyList<string> SplitIntoWords(string text)
    {
        var result = new List<string>();
        foreach (var token in SplitOnWhitespace(text))
        {
            if (!token.Any(e => e.EnumerateRunes().Any(IsCjk)))
            {
                result.Add(string.Concat(token));
                continue;
            }

            var buffer = new StringBuilder();
            foreach (var element in token)
            {
                if (IsCjk(element.EnumerateRunes().First()))
                {
                    if (buffer.Length > 0)
                    {
                        result.Add(buffer.ToString());
                        buffer.Clear();
                    }
                    result.Add(element);
                }
                else
                {
                    buffer.Append(element);
                }
            }
            if (buffer.Length > 0)
            {
                result.Add(buffer.ToString());
            }
        }
        return result;
    }

    /// <summary>對應 Swift <c>Unicode.Scalar.isPromptCJK</c>：中日韓統一表意文字、擴充 A／B、相容字、平假名、片假名、諺文。</summary>
    public static bool IsCjk(Rune rune)
    {
        var value = rune.Value;
        return (value >= 0x4E00 && value <= 0x9FFF)
            || (value >= 0x3400 && value <= 0x4DBF)
            || (value >= 0x20000 && value <= 0x2A6DF)
            || (value >= 0xF900 && value <= 0xFAFF)
            || (value >= 0x3040 && value <= 0x309F)
            || (value >= 0x30A0 && value <= 0x30FF)
            || (value >= 0xAC00 && value <= 0xD7AF);
    }

    private static IEnumerable<List<string>> SplitOnWhitespace(string text)
    {
        var token = new List<string>();
        foreach (var element in TextElements.Split(text))
        {
            if (TextElements.IsWhitespace(element))
            {
                if (token.Count > 0)
                {
                    yield return token;
                    token = [];
                }
            }
            else
            {
                token.Add(element);
            }
        }
        if (token.Count > 0)
        {
            yield return token;
        }
    }
}
