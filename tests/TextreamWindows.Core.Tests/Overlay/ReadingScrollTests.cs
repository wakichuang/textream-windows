using TextreamWindows.Core.Overlay;
using TextreamWindows.Core.Session;

namespace TextreamWindows.Core.Tests.Overlay;

public class ReadingScrollTests
{
    [Fact]
    public void TheCurrentLineSitsAtTheUpperThird()
    {
        // 浮層 300 高，目前那行在講稿第 500 的位置：往上捲 400，那行就停在 100（300 的三分之一）
        Assert.Equal(400, ReadingScroll.Target(lineTop: 500, viewportHeight: 300), 6);
    }

    [Fact]
    public void TheBeginningIsNotPushedDown()
    {
        // 第一行還在三分之一以上：不往下捲出空白
        Assert.Equal(0, ReadingScroll.Target(lineTop: 40, viewportHeight: 300));
    }

    [Fact]
    public void ASmallStepIsShortAndEasesOut()
    {
        var motion = ReadingScroll.Motion(distance: 34, lineHeight: 34, FollowMode.WordTracking);

        Assert.True(motion.EaseOut);
        Assert.InRange(motion.Seconds, 0.3, 0.4);
    }

    [Fact]
    public void ABigJumpTakesLongerSoTheEyeCanFollowButIsCapped()
    {
        // 瓦基 2026-10-08：跳過子標題或一句時，畫面快速切換會跟不上，要有往上拉的感覺
        var oneLine = ReadingScroll.Motion(34, 34, FollowMode.WordTracking);
        var fourLines = ReadingScroll.Motion(4 * 34, 34, FollowMode.WordTracking);
        var twentyLines = ReadingScroll.Motion(20 * 34, 34, FollowMode.WordTracking);

        Assert.True(fourLines.Seconds > oneLine.Seconds);
        Assert.Equal(0.7, twentyLines.Seconds, 6);
    }

    [Theory]
    [InlineData(FollowMode.Classic)]
    [InlineData(FollowMode.VoiceActivated)]
    public void TimerModesMoveInShortLinearSteps(FollowMode mode)
    {
        // 計時器每 50 毫秒推一次，每步 60 毫秒等速：看起來是連續往上流（Mac 版的做法）
        var motion = ReadingScroll.Motion(3, 34, mode);

        Assert.False(motion.EaseOut);
        Assert.Equal(0.06, motion.Seconds, 6);
    }
}
