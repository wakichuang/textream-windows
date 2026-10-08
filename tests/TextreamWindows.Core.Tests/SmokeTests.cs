namespace TextreamWindows.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void CoreAssemblyLoads()
    {
        Assert.Equal("TextreamWindows.Core", typeof(AssemblyMarker).Assembly.GetName().Name);
    }
}
