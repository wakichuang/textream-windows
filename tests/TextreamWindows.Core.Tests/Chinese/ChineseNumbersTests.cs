using TextreamWindows.Core.Chinese;
using TextreamWindows.Core.Text;

namespace TextreamWindows.Core.Tests.Chinese;

public class ChineseNumbersTests
{
    /// <summary>斷字 → 正規化，詞與詞之間用「|」接起來，看得出切在哪裡。</summary>
    private static string Normalize(string text) =>
        string.Join('|', ChineseNumbers.Normalize(PromptTokenizer.SplitIntoWords(text)).Select(w => w.Text));

    [Theory]
    [InlineData("我在 2026 年", "我|在|2026|年")] // ZH-04 原稿
    [InlineData("我在二零二六年", "我|在|2026|年")] // ZH-04 辨識結果
    [InlineData("二〇二六", "2026")]
    [InlineData("零九一二", "0912")] // 逐字念的數字保留開頭的零
    [InlineData("兩千零二十六年", "2026|年")]
    public void YearsAndDigitByDigitReadingsBecomeArabic(string text, string expected)
    {
        Assert.Equal(expected, Normalize(text));
    }

    [Theory]
    [InlineData("一共兩千元", "1|共|2000|元")] // ZH-05 原稿（「一共」的一也會變 1，兩邊一致就沒關係）
    [InlineData("一共2000元", "1|共|2000|元")] // ZH-05 辨識結果
    [InlineData("两千", "2000")]
    [InlineData("十", "10")]
    [InlineData("十五", "15")]
    [InlineData("二十", "20")]
    [InlineData("二十五", "25")]
    [InlineData("一百零五", "105")]
    [InlineData("一千二百三十四", "1234")]
    [InlineData("三萬五千", "35000")]
    [InlineData("三万五千", "35000")]
    [InlineData("一億兩千萬", "120000000")]
    [InlineData("廿五", "25")]
    [InlineData("卅", "30")]
    public void PositionalNumbersAreEvaluated(string text, string expected)
    {
        Assert.Equal(expected, Normalize(text));
    }

    [Theory]
    [InlineData("一千五", "1500")]
    [InlineData("三百五", "350")]
    [InlineData("兩萬三", "23000")]
    public void ColloquialTrailingDigitTakesTheNextLowerUnit(string text, string expected)
    {
        Assert.Equal(expected, Normalize(text));
    }

    [Theory]
    [InlineData("2,000 元", "2000|元")]
    [InlineData("２０２６", "2026")]
    [InlineData("3萬", "30000")]
    [InlineData("1.5萬", "15000")]
    [InlineData("一點五萬", "15000")]
    [InlineData("三點一四", "3.14")]
    [InlineData("3.14", "3.14")]
    public void ArabicFormsAndDecimalsMatchTheirSpokenForms(string text, string expected)
    {
        Assert.Equal(expected, Normalize(text));
    }

    [Theory]
    [InlineData("三點開會", "3|點|開|會")] // 三點鐘：點後面不是數字，就不是小數點
    [InlineData("讀了 Atomic Habits", "讀|了|Atomic|Habits")]
    [InlineData("COVID-19", "COVID-19")]
    [InlineData("參加大陸", "參|加|大|陸")] // 大寫數字（參、陸、伍、拾）是常用字，不轉，免得蓋掉同音比對
    public void NonNumbersAreLeftAlone(string text, string expected)
    {
        Assert.Equal(expected, Normalize(text));
    }

    /// <summary>
    /// Podcast 講稿裡的「信任一點一點累積」會被當成 1.1。不修：辨識結果的「一点一点」照同一套規則也是 1.1，
    /// 兩邊一致就對得上（點／点 再交給拼音比對）。這條測試防的是哪天只改了一邊。
    /// </summary>
    [Theory]
    [InlineData("一點一點", "1.1|點")]
    [InlineData("一点一点", "1.1|点")]
    public void IdiomsWithNumeralsConvertTheSameWayOnBothSides(string text, string expected)
    {
        Assert.Equal(expected, Normalize(text));
    }

    [Fact]
    public void MergedNumberRemembersWhichWordsItCameFrom()
    {
        var words = PromptTokenizer.SplitIntoWords("我在二零二六年");

        var normalized = ChineseNumbers.Normalize(words);

        Assert.Equal(
            [new("我", 0, 1), new("在", 1, 1), new("2026", 2, 4), new("年", 6, 1)],
            normalized);
    }
}
