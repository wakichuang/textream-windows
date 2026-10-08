using System.Globalization;
using System.Text;

namespace TextreamWindows.Core.Text;

/// <summary>
/// 文字元素（grapheme）工具。C# 字串是 UTF-16，emoji、罕用字、組合附加符號都可能佔兩格以上；
/// Swift 的 <c>Character</c> 天生是文字元素，所以移植 Textream 的索引一律先經過這裡。
/// </summary>
public static class TextElements
{
    public static string[] Split(string text)
    {
        var result = new List<string>(text.Length);
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            result.Add(enumerator.GetTextElement());
        }
        return result.ToArray();
    }

    /// <summary>對應 Swift <c>Character.isWhitespace</c>：例如 "\r\n" 是一個文字元素，也算空白。</summary>
    public static bool IsWhitespace(string element)
    {
        foreach (var rune in element.EnumerateRunes())
        {
            if (!Rune.IsWhiteSpace(rune))
            {
                return false;
            }
        }
        return element.Length > 0;
    }

    /// <summary>對應 Swift <c>$0.isLetter || $0.isNumber</c>：只要有一個字母或數字就算「讀得出來」。</summary>
    public static bool IsLetterOrNumber(string element)
    {
        foreach (var rune in element.EnumerateRunes())
        {
            if (Rune.IsLetter(rune) || Rune.IsNumber(rune))
            {
                return true;
            }
        }
        return false;
    }
}
