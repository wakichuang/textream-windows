using System.Runtime.InteropServices;
using System.Text;
using TextreamWindows.Core.Text;

namespace TextreamWindows.Lab;

/// <summary>
/// follow 指令在終端機上的畫面：講稿的一行窗口，讀過的變灰、下一個字反白、後面的亮白。
/// 講稿的全文（<see cref="PromptScript.Text"/>）在中文字之間補了空格，顯示時拿掉，英文單字之間的空格留著。
/// </summary>
internal sealed class FollowView
{
    private readonly PromptScript _prompt;
    private readonly List<int> _shown = [];

    public FollowView(PromptScript prompt)
    {
        _prompt = prompt;
        var elements = prompt.Elements;
        for (var i = 0; i < elements.Count; i++)
        {
            if (TextElements.IsWhitespace(elements[i]))
            {
                var bothSidesWide = i > 0 && i + 1 < elements.Count && IsWide(elements[i - 1]) && IsWide(elements[i + 1]);
                if (bothSidesWide)
                {
                    continue;
                }
            }
            _shown.Add(i);
        }
    }

    /// <summary>窗口：讀過的部分最多佔四分之一寬，其餘給還沒讀的。<paramref name="ansi"/> 為 false 時用「▌」標出目前位置。</summary>
    public string Frame(int progress, int width, bool ansi)
    {
        var elements = _prompt.Elements;
        var next = _shown.FindIndex(i => i >= progress);
        if (next < 0)
        {
            next = _shown.Count;
        }

        var before = new StringBuilder();
        var used = 0;
        for (var k = next - 1; k >= 0 && used + Columns(elements[_shown[k]]) <= width / 4; k--)
        {
            before.Insert(0, elements[_shown[k]]);
            used += Columns(elements[_shown[k]]);
        }

        var ahead = new StringBuilder();
        var current = next < _shown.Count ? elements[_shown[next]] : "";
        used += Columns(current) + (ansi ? 0 : 1);
        for (var k = next + 1; k < _shown.Count && used + Columns(elements[_shown[k]]) <= width; k++)
        {
            ahead.Append(elements[_shown[k]]);
            used += Columns(elements[_shown[k]]);
        }

        return ansi
            ? $"\u001b[90m{before}\u001b[30;43m{current}\u001b[0m\u001b[97m{ahead}\u001b[0m"
            : $"{before}▌{current}{ahead}";
    }

    /// <summary>終端機上佔兩格的字（中日韓、全形標點）。</summary>
    public static int Columns(string element) => IsWide(element) ? 2 : 1;

    private static bool IsWide(string element)
    {
        if (element.Length == 0)
        {
            return false;
        }
        var value = Rune.GetRuneAt(element, 0).Value;
        return PromptTokenizer.IsCjk(new Rune(value))
            || (value >= 0x3000 && value <= 0x303F)  // 中日韓標點（「」、。）
            || (value >= 0xFF00 && value <= 0xFFEF); // 全形字元（，：！）
    }

    /// <summary>開啟主控台的 ANSI 色碼（Windows Terminal 本來就支援，舊的主控台視窗要開這個旗標）。失敗就回 false，改用純文字。</summary>
    public static bool TryEnableAnsi()
    {
        if (Console.IsOutputRedirected)
        {
            return false;
        }
        const int StdOutputHandle = -11;
        const uint EnableVirtualTerminalProcessing = 0x0004;
        var handle = GetStdHandle(StdOutputHandle);
        return GetConsoleMode(handle, out var mode) && SetConsoleMode(handle, mode | EnableVirtualTerminalProcessing);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
}
