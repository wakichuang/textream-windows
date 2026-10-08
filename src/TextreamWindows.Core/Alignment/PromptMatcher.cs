using TextreamWindows.Core.Chinese;
using TextreamWindows.Core.Session;
using TextreamWindows.Core.Text;

namespace TextreamWindows.Core.Alignment;

/// <summary>
/// 把辨識結果對到講稿上，回報讀到第幾個文字元素。移植 Textream iOS 版的 <c>PromptMatcher.swift</c>：
/// 字元層＋詞層兩套比對、合併、2/3 投票、只進不退。
/// 跟原版不同的是「相等」的定義（計畫書第 6 節）：中文字比讀音集合有沒有交集，數字先統一成阿拉伯數字，英文照原版模糊比對。
/// </summary>
/// <remarks>
/// 容錯比原版多兩項（2026-10-08，瓦基照稿念但會插話、跳子標題、改講法再接回）：
/// 一、防拖走：跳過、漏掉之後才對上的字要「連續」對上才算數。中文同音字多，插話裡零星的「是」「作」「說」會對上講稿，原版會一格一格被拖著走。
/// 二、找回位置：在講稿後面約 400 個單位內找「現在正在念的那一串」，連續對上夠多就跳過去，接住跳標題、跳句、插話與改講法之後的接回。
/// <para>
/// 呼叫方式：<see cref="Match"/> 收的是「上次重新起算以來的完整辨識結果」，sherpa-onnx 的部分結果正好是這樣。
/// 偵測到一句講完、辨識結果清空時，呼叫 <see cref="RestartFromCurrentProgress"/>（計畫書第 6.2 節）。
/// </para>
/// </remarks>
public sealed class PromptMatcher
{
    /// <summary>對不上時，兩邊各往後最多找幾個單位。</summary>
    private const int MaxSkip = 5;

    /// <summary>找回位置：往後找多遠（可讀單位）。約一分半鐘的朗讀量，涵蓋跳過一個子標題或一兩段。</summary>
    private const int AnchorLookAhead = 400;

    /// <summary>找回位置：至少連續對上幾個單位才跳。跳得越遠要求越多，見 <see cref="RunNeededToJump"/>。</summary>
    private const int AnchorBaseRun = 5;

    /// <summary>找回位置：那一串要結束在辨識結果的最後幾個單位內，才代表「現在正在念這裡」。部分結果的最後一兩個字常常還會改。</summary>
    private const int AnchorTailSlack = 2;

    private readonly PinyinTable _pinyin;
    private readonly List<MatchUnit> _units;
    private readonly List<Slot> _slots;
    private readonly List<int> _recentMatchPositions = new(4);

    public PromptMatcher(PromptScript source, int startingAt = 0, PinyinTable? pinyin = null, SpeechLanguage language = SpeechLanguage.TraditionalChinese)
    {
        Source = source;
        Language = language;
        _pinyin = pinyin ?? PinyinTable.Default;
        _units = MatchUnit.FromScript(source, _pinyin);
        _slots = _units
            .SelectMany(u => u.IsAnnotation
                ? [new Slot(true, default, u.Source.Start)]
                : u.Characters.Select(c => new Slot(false, c, c.Start)))
            .ToList();

        var start = SpeechTextAlignment.AdvancePastAnnotations(source, startingAt);
        RecognizedCharacterCount = start;
        MatchStartOffset = start;
    }

    public PromptScript Source { get; }

    /// <summary>比對規則用哪一套（瓦基 2026-10-08）。繁體中文是原本的規則；English 給純英文講稿用。</summary>
    public SpeechLanguage Language { get; }

    /// <summary>讀到第幾個文字元素（高亮的位置）。只會變大。</summary>
    public int RecognizedCharacterCount { get; private set; }

    /// <summary>這一句的辨識結果從講稿的哪裡開始比。</summary>
    public int MatchStartOffset { get; private set; }

    public int Match(string transcript)
    {
        if (string.IsNullOrEmpty(transcript) || MatchStartOffset >= Source.CharacterCount)
        {
            return RecognizedCharacterCount;
        }

        var spokenUnits = MatchUnit.FromTranscript(transcript, _pinyin);
        var spokenCharacters = spokenUnits.SelectMany(u => u.Characters).ToList();

        var firstSlot = _slots.FindIndex(s => s.Start >= MatchStartOffset);
        var characterResult = Progress(
            Scan(_slots, firstSlot, spokenCharacters, s => s.IsSkip, (s, p) => CharactersMatch(s.Character, p), s => s.Character.IsHan ? 2 : 3, AcceptsRunAtScriptEnd),
            firstSlot, _slots.Count, i => _slots[i].Start);

        var firstUnit = _units.FindIndex(u => u.Source.Start >= MatchStartOffset);
        var wordResult = Progress(
            Scan(_units, firstUnit, spokenUnits, u => u.IsAnnotation, UnitsMatch, _ => 2, AcceptsRunAtScriptEnd),
            firstUnit, _units.Count, i => _units[i].Source.Start);

        var best = SpeechTextAlignment.BestOffset(characterResult, wordResult);
        var rawCandidate = Math.Min(MatchStartOffset + best, Source.CharacterCount);
        var candidate = SpeechTextAlignment.AdvancePastAnnotations(Source, rawCandidate);
        if (candidate > RecognizedCharacterCount)
        {
            _recentMatchPositions.Add(candidate);
            if (_recentMatchPositions.Count > 3)
            {
                _recentMatchPositions.RemoveAt(0);
            }
            var confirmed = _recentMatchPositions.Count(p => Math.Abs(p - candidate) <= 10) >= 2;

            if (SpeechTextAlignment.ShouldCommit(characterResult, wordResult, RecognizedCharacterCount, rawCandidate, candidate, confirmed))
            {
                RecognizedCharacterCount = candidate;
            }
        }

        if (FindAnchor(firstUnit, spokenUnits) is { } anchor && anchor > RecognizedCharacterCount)
        {
            RecognizedCharacterCount = anchor;
            _recentMatchPositions.Clear();
        }
        return RecognizedCharacterCount;
    }

    /// <summary>
    /// 使用者點字、滾輪追趕時跳到指定位置（可以往回），跳過標註，並從那裡重新比。
    /// 這是唯一會讓 <see cref="RecognizedCharacterCount"/> 變小的路。對應原版 <c>jump(to:)</c>。
    /// </summary>
    public int Jump(int offset)
    {
        var target = SpeechTextAlignment.AdvancePastAnnotations(Source, Math.Clamp(offset, 0, Source.CharacterCount));
        RecognizedCharacterCount = target;
        MatchStartOffset = target;
        _recentMatchPositions.Clear();
        return target;
    }

    /// <summary>一句講完、辨識結果要清空重來時呼叫：下一句從目前讀到的位置開始比。</summary>
    public void RestartFromCurrentProgress()
    {
        MatchStartOffset = RecognizedCharacterCount;
        _recentMatchPositions.Clear();
    }

    /// <summary>英文詞的模糊比對，照原版：前綴相同、共同前綴夠長、或編輯距離夠小。長度以文字元素計。</summary>
    public static bool IsFuzzyMatch(string first, string second)
    {
        if (first.Length == 0 || second.Length == 0)
        {
            return false;
        }
        if (first == second)
        {
            return true;
        }
        var a = TextElements.Split(first);
        var b = TextElements.Split(second);
        var shorter = Math.Min(a.Length, b.Length);
        if (shorter >= 3 && (a.AsSpan().StartsWith(b) || b.AsSpan().StartsWith(a)))
        {
            return true;
        }
        var sharedPrefix = 0;
        while (sharedPrefix < shorter && a[sharedPrefix] == b[sharedPrefix])
        {
            sharedPrefix++;
        }
        if (shorter >= 3 && sharedPrefix >= Math.Max(3, shorter * 3 / 5))
        {
            return true;
        }
        var distance = EditDistance(a, b);
        if (shorter <= 2)
        {
            return false;
        }
        if (shorter <= 4)
        {
            return distance <= 1;
        }
        if (shorter <= 8)
        {
            return distance <= 2;
        }
        return distance <= Math.Max(a.Length, b.Length) / 3;
    }

    private static int EditDistance(string[] first, string[] second)
    {
        if (first.Length == 0)
        {
            return second.Length;
        }
        if (second.Length == 0)
        {
            return first.Length;
        }
        var row = Enumerable.Range(0, second.Length + 1).ToArray();
        for (var i = 1; i <= first.Length; i++)
        {
            var previous = row[0];
            row[0] = i;
            for (var j = 1; j <= second.Length; j++)
            {
                var temporary = row[j];
                row[j] = first[i - 1] == second[j - 1] ? previous : Math.Min(previous, Math.Min(row[j], row[j - 1])) + 1;
                previous = temporary;
            }
        }
        return row[second.Length];
    }

    /// <summary>
    /// 兩層共用的掃描：原稿從 <paramref name="first"/> 起、辨識結果從頭，對得上就一起前進；
    /// 對不上先在辨識結果往後找（模型多吐了字），再在原稿往後找（講者跳過幾個字），都找不到就丟掉這個辨識單位。
    /// </summary>
    /// <returns>
    /// 原稿確定讀到哪一格（不含）。一句話開頭第一個單位就對上講稿下一格，直接算數（照稿念的常態）；
    /// 其餘要連續直接對上 <paramref name="runNeeded"/> 個單位才算數，跳過或丟掉單位都會讓連續中斷。
    /// 這是防拖走：插話裡零星的同音字會對上講稿，但很少連著對上。
    /// <paramref name="acceptRunAtScriptEnd"/> 時，還沒連續到的那串若已經對到講稿最後一格，也算數：後面沒有字可以再連下去了。
    /// </returns>
    private static int Scan<TSource, TSpoken>(
        IReadOnlyList<TSource> source,
        int first,
        IReadOnlyList<TSpoken> spoken,
        Func<TSource, bool> isSkip,
        Func<TSource, TSpoken, bool> matches,
        Func<TSource, int> runNeeded,
        bool acceptRunAtScriptEnd = false)
    {
        if (first < 0)
        {
            return first;
        }
        var si = first;
        var pi = 0;
        var confirmedEnd = first;
        var tentative = false; // confirmedEnd 之後有對上、但還沒連續到算數的格子
        var tentativeEnd = first;
        var run = 0;
        var atStart = true; // 還沒對上任何單位，也還沒跳過或丟掉任何單位
        while (si < source.Count && pi < spoken.Count)
        {
            if (isSkip(source[si]))
            {
                si++;
                if (!tentative)
                {
                    confirmedEnd = si;
                }
                continue;
            }
            if (matches(source[si], spoken[pi]))
            {
                run++;
                if (atStart || run >= runNeeded(source[si]))
                {
                    confirmedEnd = si + 1;
                    tentative = false;
                }
                else
                {
                    tentative = true;
                    tentativeEnd = si + 1;
                }
                atStart = false;
                si++;
                pi++;
                continue;
            }

            atStart = false;
            run = 0;
            var found = false;
            for (var skip = 1; skip <= Math.Min(MaxSkip, spoken.Count - pi - 1); skip++)
            {
                if (matches(source[si], spoken[pi + skip]))
                {
                    pi += skip;
                    found = true;
                    break;
                }
            }
            if (found)
            {
                continue;
            }

            for (var skip = 1; skip <= Math.Min(MaxSkip, source.Count - si - 1); skip++)
            {
                if (!isSkip(source[si + skip]) && matches(source[si + skip], spoken[pi]))
                {
                    si += skip;
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                pi++;
            }
        }

        if (tentative && acceptRunAtScriptEnd && Enumerable.Range(tentativeEnd, source.Count - tentativeEnd).All(i => isSkip(source[i])))
        {
            confirmedEnd = tentativeEnd;
            tentative = false;
        }
        if (!tentative)
        {
            while (confirmedEnd < source.Count && isSkip(source[confirmedEnd]))
            {
                confirmedEnd++;
            }
        }
        return confirmedEnd;
    }

    /// <summary>
    /// 找回位置：在講稿「這一句的起點」到「目前位置往後 <see cref="AnchorLookAhead"/> 個可讀單位」之間，
    /// 找跟辨識結果結尾連續對上最長的一串（最長共同子字串）。夠長就回傳那一串之後的位置；一樣長取最近的。
    /// 從這一句的起點找，是因為跳過去之後，同一句的部分結果還會繼續變長，要能一直找到同一串、跟著往前。
    /// </summary>
    private int? FindAnchor(int firstUnit, IReadOnlyList<MatchUnit> spoken)
    {
        if (firstUnit < 0 || spoken.Count < AnchorBaseRun)
        {
            return null;
        }

        var currentUnit = _units.FindIndex(u => u.Source.Start >= RecognizedCharacterCount);
        var readable = new List<int>();
        var readableBeforeCurrent = 0;
        for (var i = firstUnit; i < _units.Count; i++)
        {
            if (_units[i].IsAnnotation)
            {
                continue;
            }
            if (currentUnit >= 0 && i < currentUnit)
            {
                readableBeforeCurrent++;
            }
            else if (readable.Count - readableBeforeCurrent >= AnchorLookAhead)
            {
                break;
            }
            readable.Add(i);
        }

        // run[j]：原稿目前這個可讀單位與辨識第 j 個單位「結尾在這裡」的連續對上長度，逐列更新
        var run = new int[spoken.Count + 1];
        var bestLength = 0;
        var bestEnd = -1;
        for (var i = 0; i < readable.Count; i++)
        {
            var unit = _units[readable[i]];
            for (var j = spoken.Count; j >= 1; j--)
            {
                run[j] = UnitsMatch(unit, spoken[j - 1]) ? run[j - 1] + 1 : 0;
                if (j < spoken.Count - AnchorTailSlack || run[j] <= bestLength)
                {
                    continue;
                }
                var distance = Math.Max(0, i - run[j] + 1 - readableBeforeCurrent);
                if (run[j] >= RunNeededToJump(distance))
                {
                    bestLength = run[j];
                    bestEnd = readable[i];
                }
            }
        }
        if (bestEnd < 0)
        {
            return null;
        }

        var next = bestEnd + 1;
        while (next < _units.Count && _units[next].IsAnnotation)
        {
            next++;
        }
        return next < _units.Count ? _units[next].Source.Start : Source.CharacterCount;
    }

    /// <summary>
    /// 往前跳 <paramref name="distance"/> 個可讀單位，要連續對上幾個：基本 5 個，每遠 50 個多要 1 個。
    /// 原本是每 100 個多 1 個，一集看稿講的 Podcast 被「不一樣的地方」（6 個）騙去 200 字後（測試 ZH-23），2026-10-08 改陡。
    /// </summary>
    private static int RunNeededToJump(int distance) => AnchorBaseRun + distance / 50;

    /// <summary>掃到第 <paramref name="end"/> 格 → 從比對起點算前進了幾個字：停在下一格的開頭，掃完了就是講稿結尾。</summary>
    private int Progress(int end, int first, int count, Func<int, int> startOf)
    {
        if (first < 0 || end <= first)
        {
            return 0;
        }
        var position = end < count ? startOf(end) : Source.CharacterCount;
        return Math.Max(0, position - MatchStartOffset);
    }

    /// <summary>
    /// English：講稿最後幾個詞前面有一個詞沒聽清楚（「from the earth」辨識成「FROM THIS EARTH」）時，最後一個詞照樣算數，否則永遠到不了「讀完了」。
    /// 2026-10-08 實測 15 位朗讀者有 14 位卡在這裡。繁體中文維持原本的規則。
    /// </summary>
    private bool AcceptsRunAtScriptEnd => Language == SpeechLanguage.English;

    private static bool UnitsMatch(MatchUnit source, MatchUnit spoken) =>
        source.Kind == spoken.Kind && source.Kind switch
        {
            MatchUnitKind.Han => SoundsOverlap(source.Sounds, spoken.Sounds),
            MatchUnitKind.Word => source.Key == spoken.Key || IsFuzzyMatch(source.Key, spoken.Key),
            _ => source.Key == spoken.Key,
        };

    private static bool CharactersMatch(MatchChar source, MatchChar spoken) =>
        source.IsHan == spoken.IsHan && (source.IsHan ? SoundsOverlap(source.Sounds, spoken.Sounds) : source.Key == spoken.Key);

    private static bool SoundsOverlap(IReadOnlyCollection<string> first, IReadOnlyCollection<string> second)
    {
        foreach (var sound in first)
        {
            if (second.Contains(sound))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>字元層的一格：一般字元，或整個標註（跳過）。</summary>
    private readonly record struct Slot(bool IsSkip, MatchChar Character, int Start);
}
