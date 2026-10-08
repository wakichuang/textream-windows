namespace TextreamWindows.Core.Text;

/// <summary>
/// 原稿裡的一段位置，單位是文字元素（grapheme），左閉右開。對應 Swift 的 <c>Range&lt;Int&gt;</c>。
/// 不用 <see cref="System.Range"/>：它的端點是 <see cref="Index"/>，可以從尾端起算，這裡用不到也容易誤用。
/// </summary>
public readonly record struct CharRange(int Start, int End)
{
    public int Length => End - Start;

    public bool IsEmpty => End <= Start;

    public bool Contains(int offset) => offset >= Start && offset < End;

    public override string ToString() => $"{Start}..<{End}";
}
