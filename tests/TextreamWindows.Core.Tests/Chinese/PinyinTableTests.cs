using TextreamWindows.Core.Chinese;

namespace TextreamWindows.Core.Tests.Chinese;

public class PinyinTableTests
{
    private static readonly PinyinTable Table = PinyinTable.Default;

    [Fact]
    public void DefaultTableIsEmbeddedAndComplete()
    {
        // Unicode 18.0 的 Unihan 有讀音的字數（計畫書第 12 節 3.2）；重產拼音表時跟著改
        Assert.Equal(44_364, Table.Count);
    }

    [Fact]
    public void TraditionalAndSimplifiedShareTheSameReading()
    {
        Assert.Equal(["yue"], Table.SoundKeys("閱"));
        Assert.Equal(["yue"], Table.SoundKeys("阅"));
        Assert.True(Table.SoundsAlike("閱", "阅"));
    }

    [Fact]
    public void PolyphonicCharacterKeepsEveryReading()
    {
        var keys = Table.SoundKeys("行");

        Assert.Contains("xing", keys);
        Assert.Contains("hang", keys);
        Assert.True(Table.SoundsAlike("行", "形"));
        Assert.True(Table.SoundsAlike("行", "航"));
    }

    [Theory]
    [InlineData("已", "以")] // ZH-01 已經／以經
    [InlineData("瓦", "哇")] // ZH-11 瓦基／哇基
    [InlineData("垃", "拉")] // 台灣讀 lè、中國讀 lā：表裡有 la 就對得上（計畫書第 10 節）
    public void HomophonesSoundAlike(string a, string b)
    {
        Assert.True(Table.SoundsAlike(a, b));
    }

    [Fact]
    public void DifferentSoundsDoNotMatch()
    {
        Assert.False(Table.SoundsAlike("閱", "讀"));
    }

    [Theory]
    [InlineData("🙂")]
    [InlineData("a")]
    [InlineData("，")]
    public void UnknownElementIsItsOwnKey(string element)
    {
        Assert.Equal([element], Table.SoundKeys(element));
        Assert.True(Table.SoundsAlike(element, element));
        Assert.False(Table.SoundsAlike(element, "閱"));
    }

    [Fact]
    public void VariationSelectorDoesNotHideTheCharacter()
    {
        // 葛 + U+E0100（異體字選擇符）是一個文字元素，讀音照基底字查
        Assert.Equal(Table.SoundKeys("葛"), Table.SoundKeys("葛\U000E0100"));
    }

    [Fact]
    public void ParseSkipsCommentsAndSplitsReadings()
    {
        var table = PinyinTable.Parse(new StringReader("# 註解\n行\thang heng xing\n閱\tyue\n"));

        Assert.Equal(2, table.Count);
        Assert.Equal(["hang", "heng", "xing"], table.SoundKeys("行"));
    }
}
