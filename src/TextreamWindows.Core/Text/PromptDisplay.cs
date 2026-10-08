namespace TextreamWindows.Core.Text;

/// <summary>
/// 講稿在畫面上怎麼顯示：照瓦基原本的講稿（<see cref="PromptScript.RawText"/>），段落、空格都不動；
/// 比對用的 <see cref="PromptScript.Text"/> 合併了空白、在中文字之間補了空格，這裡負責把它的位置換回原稿的位置。
/// </summary>
/// <remarks>
/// 兩邊的「非空白文字元素」順序完全一樣（斷字只拆開、不改字），所以照順序一一對上；
/// 比對全文裡的空白（合併或補上的）對到前一個字的後面。
/// </remarks>
public sealed class PromptDisplay
{
    private readonly int[] _displayOffsets;
    private readonly IReadOnlyList<string> _elements;

    public PromptDisplay(PromptScript script)
    {
        Text = script.RawText;
        _elements = script.Elements;

        // 原稿每個非空白文字元素的 UTF-16 起點與長度
        var raw = new List<(int Start, int Length)>();
        var position = 0;
        foreach (var element in TextElements.Split(Text))
        {
            if (!TextElements.IsWhitespace(element))
            {
                raw.Add((position, element.Length));
            }
            position += element.Length;
        }

        var elements = script.Elements;
        _displayOffsets = new int[elements.Count + 1];
        var next = 0;
        var afterPrevious = 0;
        for (var i = 0; i < elements.Count; i++)
        {
            if (TextElements.IsWhitespace(elements[i]) || next >= raw.Count)
            {
                _displayOffsets[i] = afterPrevious;
                continue;
            }
            _displayOffsets[i] = raw[next].Start;
            afterPrevious = raw[next].Start + raw[next].Length;
            next++;
        }
        _displayOffsets[elements.Count] = Text.Length;
    }

    /// <summary>顯示用的全文（瓦基原本的講稿）。</summary>
    public string Text { get; }

    /// <summary>講稿位置（文字元素）→ 顯示文字的位置（UTF-16，可以直接拿去切 <see cref="Text"/>）。</summary>
    public int ToDisplayOffset(int characterOffset) =>
        _displayOffsets[Math.Clamp(characterOffset, 0, _displayOffsets.Length - 1)];

    /// <summary>兩個詞在原稿裡是不是隔了換行（比對用的全文把換行合併成空格了，要回原稿看）。</summary>
    public bool LineBreakBetween(PromptWord before, PromptWord after)
    {
        var from = ToDisplayOffset(before.CharacterRange.End);
        var to = ToDisplayOffset(after.CharacterRange.Start);
        return to > from && Text.AsSpan(from, to - from).ContainsAny('\n', '\r');
    }

    /// <summary>
    /// 反方向：顯示文字的位置（UTF-16，例如編輯器的游標）→ 講稿位置（文字元素）。
    /// 游標落在某個字上（含卡在代理對中間）就是那個字；落在空白、空行上就是後面第一個字；最後面是講稿結尾。
    /// </summary>
    public int ToCharacterOffset(int displayOffset)
    {
        for (var i = 0; i < _elements.Count; i++)
        {
            if (TextElements.IsWhitespace(_elements[i]))
            {
                continue;
            }
            if (_displayOffsets[i] + _elements[i].Length > displayOffset)
            {
                return i;
            }
        }
        return _elements.Count;
    }
}
