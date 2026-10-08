namespace TextreamWindows.Desktop.Tests;

/// <summary>
/// 視窗行為測試（第 6 階段）只在有真實桌面的 Windows 本機跑，CI 不跑這個專案。
/// </summary>
public class SmokeTests
{
    [Fact]
    public void AppAssemblyLoads()
    {
        Assert.Equal("TextreamWindows", typeof(TextreamWindows.App.App).Assembly.GetName().Name);
    }
}
