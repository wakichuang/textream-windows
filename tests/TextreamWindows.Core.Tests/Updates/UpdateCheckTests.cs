using TextreamWindows.Core.Updates;

namespace TextreamWindows.Core.Tests.Updates;

/// <summary>
/// 檢查更新（瓦基 2026-10-08，照 Mac 版 textream-zh 的 UpdateChecker）：查 GitHub 這個 repo 的最新 Release，比目前版本新就問要不要去下載。
/// 這裡只測不用連網的部分：比版本、讀 GitHub 回的 JSON。
/// </summary>
public class UpdateCheckTests
{
    private const string ReleasePage = "https://github.com/wakichuang/textream-windows/releases/tag/v0.10.0";

    private static string Json(string tag, string url = ReleasePage) =>
        $$"""{"tag_name": "{{tag}}", "html_url": "{{url}}", "name": "Textream for Windows {{tag}}", "assets": []}""";

    [Theory]
    [InlineData("0.10.0", "0.9.0", true)] // 照字串比會錯
    [InlineData("v0.9.1", "0.9.0", true)]
    [InlineData("1.0", "0.9.9", true)]
    [InlineData("0.9.0", "0.9.0", false)]
    [InlineData("0.9", "0.9.0", false)]
    [InlineData("0.8.5", "0.9.0", false)]
    [InlineData("V0.9.0", "0.9.0", false)]
    public void ComparesVersionsNumerically(string remote, string local, bool newer)
    {
        Assert.Equal(newer, UpdateCheck.IsNewer(remote, local));
    }

    [Fact]
    public void ANewerReleaseIsAnUpdate()
    {
        var result = UpdateCheck.Evaluate("0.9.0", Json("v0.10.0"));

        Assert.Equal(UpdateStatus.UpdateAvailable, result.Status);
        Assert.Equal("0.10.0", result.LatestVersion);
        Assert.Equal(ReleasePage, result.ReleaseUrl);
    }

    [Fact]
    public void TheSameVersionIsUpToDate()
    {
        var result = UpdateCheck.Evaluate("0.10.0", Json("v0.10.0"));

        Assert.Equal(UpdateStatus.UpToDate, result.Status);
        Assert.Equal("0.10.0", result.LatestVersion);
    }

    [Theory]
    [InlineData("不是 JSON")]
    [InlineData("""{"message": "Not Found"}""")] // 還沒有任何 Release
    [InlineData("""{"tag_name": "v1.0.0"}""")]
    [InlineData("")]
    public void AnUnreadableAnswerIsReportedAsSuch(string json)
    {
        Assert.Equal(UpdateStatus.Unreadable, UpdateCheck.Evaluate("0.9.0", json).Status);
    }

    /// <summary>按「前往下載」會用瀏覽器開這個網址：只開這個 repo 在 GitHub 上的頁面，回應被竄改也不會被帶去別的網站。</summary>
    [Theory]
    [InlineData("https://evil.example.com/textream-windows/releases/tag/v1.0.0")]
    [InlineData("https://github.com/someone-else/textream-windows/releases/tag/v1.0.0")]
    [InlineData("http://github.com/wakichuang/textream-windows/releases/tag/v1.0.0")]
    [InlineData("https://github.com.evil.example/wakichuang/textream-windows/releases")]
    public void OnlyThisRepositorysPagesAreOpened(string url)
    {
        var result = UpdateCheck.Evaluate("0.9.0", Json("v1.0.0", url));

        Assert.Equal(UpdateStatus.UpdateAvailable, result.Status);
        Assert.Equal(UpdateCheck.ReleasesPage, result.ReleaseUrl);
    }

    [Fact]
    public void TheApiAddressIsThisRepositorysLatestRelease()
    {
        Assert.Equal("https://api.github.com/repos/wakichuang/textream-windows/releases/latest", UpdateCheck.LatestReleaseApi);
        Assert.Equal("https://github.com/wakichuang/textream-windows/releases/latest", UpdateCheck.ReleasesPage);
    }
}
