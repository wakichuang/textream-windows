using TextreamWindows.Speech.Audio;
using TextreamWindows.Speech.Recognition;

namespace TextreamWindows.Speech.Tests;

public class NullSpeechEngineTests
{
    [Fact]
    public async Task CompletesWhenToldAndNeverRaisesEvents()
    {
        using var engine = new NullSpeechEngine();
        var events = 0;
        engine.PartialResult += _ => events++;
        engine.EndOfUtterance += _ => events++;

        engine.Accept(new AudioChunk(new float[800], 0));
        Assert.False(engine.Completion.IsCompleted);
        engine.Complete();
        await engine.Completion.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(0, events);
    }
}
