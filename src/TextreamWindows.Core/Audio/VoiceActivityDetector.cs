namespace TextreamWindows.Core.Audio;

/// <summary>
/// 語音活動偵測（有沒有在講話），給「有講話才捲」模式用。移植 Textream <c>VoiceActivityDetector.swift</c>：
/// 音量過 <see cref="ActivationLevel"/> 要連續 <see cref="RequiredActiveFrames"/> 格才算開口，過 <see cref="ImmediateActivationLevel"/> 立刻算；
/// 算開口之後持續 <see cref="HangoverSeconds"/> 秒，句子中間換氣不會停。時間由呼叫端給（秒），方便測試。
/// </summary>
public sealed class VoiceActivityDetector(
    float activationLevel = 0.012f,
    float immediateActivationLevel = 0.04f,
    int requiredActiveFrames = 2,
    double hangoverSeconds = 0.75)
{
    private int _consecutiveActiveFrames;
    private double _activeUntil = double.NegativeInfinity;

    public float ActivationLevel { get; } = activationLevel;

    public float ImmediateActivationLevel { get; } = immediateActivationLevel;

    public int RequiredActiveFrames { get; } = requiredActiveFrames;

    public double HangoverSeconds { get; } = hangoverSeconds;

    /// <summary>送進一格的音量（<see cref="AudioLevelMeter.NormalizedRms"/>）與它的時間。</summary>
    public void Process(float level, double timestamp)
    {
        if (level >= ImmediateActivationLevel)
        {
            _consecutiveActiveFrames = RequiredActiveFrames;
            ExtendActivity(timestamp);
        }
        else if (level >= ActivationLevel)
        {
            _consecutiveActiveFrames++;
            if (_consecutiveActiveFrames >= RequiredActiveFrames)
            {
                ExtendActivity(timestamp);
            }
        }
        else
        {
            _consecutiveActiveFrames = 0;
        }
    }

    public bool IsActive(double timestamp) => timestamp < _activeUntil;

    public void Reset()
    {
        _consecutiveActiveFrames = 0;
        _activeUntil = double.NegativeInfinity;
    }

    private void ExtendActivity(double timestamp) => _activeUntil = Math.Max(_activeUntil, timestamp + HangoverSeconds);
}
