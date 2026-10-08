using System.Text;

namespace TextreamWindows.Core.Chinese;

/// <summary>
/// 漢字 → 所有無聲調讀音。資料是 <c>data/pinyin/unihan-pinyin.tsv</c>（由 <c>scripts/gen_pinyin_table.py</c> 從 Unicode Unihan 產生），
/// 以內嵌資源編進組件，打包成單一執行檔也不必另外找檔案。
/// 計畫書第 6 節：比對時兩邊都轉成讀音集合，有交集就算同一個字，簡繁、同音字、多音字一次吸收。
/// </summary>
public sealed class PinyinTable
{
    private const string ResourceName = "TextreamWindows.Core.unihan-pinyin.tsv";

    private static readonly Lazy<PinyinTable> DefaultTable = new(LoadEmbedded);

    private readonly Dictionary<int, string[]> _readings;

    private PinyinTable(Dictionary<int, string[]> readings)
    {
        _readings = readings;
    }

    /// <summary>組件內嵌的 Unihan 拼音表，第一次用到才載入（約 4 萬 4 千字）。</summary>
    public static PinyinTable Default => DefaultTable.Value;

    /// <summary>每行「字&lt;TAB&gt;讀音 讀音…」，<c>#</c> 開頭是註解。</summary>
    public static PinyinTable Parse(TextReader reader)
    {
        var readings = new Dictionary<int, string[]>();
        var syllables = new Dictionary<string, string>(StringComparer.Ordinal); // 同一個音節共用一個字串
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }
            var tab = line.IndexOf('\t');
            var character = Rune.GetRuneAt(line, 0);
            var keys = line[(tab + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < keys.Length; i++)
            {
                keys[i] = syllables.TryGetValue(keys[i], out var shared) ? shared : syllables[keys[i]] = keys[i];
            }
            readings[character.Value] = keys;
        }
        return new PinyinTable(readings);
    }

    public int Count => _readings.Count;

    /// <summary>
    /// 一個文字元素的比對鍵：漢字是它所有的讀音；查不到的（英文、標點、emoji、表裡沒有的罕用字）就是它自己。
    /// 只看第一個碼位，後面的異體字選擇符之類不影響讀音。
    /// </summary>
    public IReadOnlyCollection<string> SoundKeys(string element)
    {
        if (element.Length > 0 && _readings.TryGetValue(Rune.GetRuneAt(element, 0).Value, out var keys))
        {
            return keys;
        }
        return [element];
    }

    public bool SoundsAlike(string a, string b)
    {
        var keysOfB = SoundKeys(b);
        foreach (var key in SoundKeys(a))
        {
            if (keysOfB.Contains(key))
            {
                return true;
            }
        }
        return false;
    }

    private static PinyinTable LoadEmbedded()
    {
        using var stream = typeof(PinyinTable).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"組件裡找不到內嵌資源 {ResourceName}，檢查 TextreamWindows.Core.csproj 的 EmbeddedResource。");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return Parse(reader);
    }
}
