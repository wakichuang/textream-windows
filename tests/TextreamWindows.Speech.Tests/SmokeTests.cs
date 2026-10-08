namespace TextreamWindows.Speech.Tests;

public class SmokeTests
{
    [Fact]
    public void SpeechAssemblyLoads()
    {
        Assert.Equal("TextreamWindows.Speech", typeof(AssemblyMarker).Assembly.GetName().Name);
    }
}
