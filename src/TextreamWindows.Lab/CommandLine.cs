namespace TextreamWindows.Lab;

/// <summary>極簡的參數解析：位置參數＋ --名稱 值 ＋ --旗標。不為了一個開發工具多裝套件。</summary>
public sealed class CommandLine
{
    private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _used = new(StringComparer.OrdinalIgnoreCase);

    public CommandLine(IEnumerable<string> args, params string[] flags)
    {
        var list = args.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            var a = list[i];
            if (!a.StartsWith("--", StringComparison.Ordinal))
            {
                Positional.Add(a);
                continue;
            }
            var name = a[2..];
            if (flags.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                _options[name] = null;
            }
            else if (i + 1 < list.Count)
            {
                _options[name] = list[++i];
            }
            else
            {
                throw new ArgumentException($"--{name} 後面要接一個值");
            }
        }
    }

    public List<string> Positional { get; } = [];

    public string Arg(int index, string name) =>
        index < Positional.Count ? Positional[index] : throw new ArgumentException($"少了參數 <{name}>");

    public bool Flag(string name)
    {
        _used.Add(name);
        return _options.ContainsKey(name);
    }

    public string? Get(string name)
    {
        _used.Add(name);
        return _options.GetValueOrDefault(name);
    }

    public int Int(string name, int fallback) =>
        Get(name) is { } v ? int.Parse(v, System.Globalization.CultureInfo.InvariantCulture) : fallback;

    public double Double(string name, double fallback) =>
        Get(name) is { } v ? double.Parse(v, System.Globalization.CultureInfo.InvariantCulture) : fallback;

    /// <summary>打錯選項名稱時要講，不要默默忽略。</summary>
    public void RejectUnknown()
    {
        var unknown = _options.Keys.Where(k => !_used.Contains(k)).ToList();
        if (unknown.Count > 0)
        {
            throw new ArgumentException($"不認得的選項：{string.Join(" ", unknown.Select(u => "--" + u))}");
        }
    }
}
