using TextreamWindows.Core.Audio;

namespace TextreamWindows.Core.Tests.Audio;

public class VoiceActivityDetectorTests
{
    // ── 移植自 Textream PromptCoreTests.swift（commit 2c02f3e） ──

    [Fact]
    public void VoiceActivityUsesActivationFramesAndHangover()
    {
        var detector = new VoiceActivityDetector();
        detector.Process(0.02f, 10);
        Assert.False(detector.IsActive(10));

        detector.Process(0.02f, 10.05);
        Assert.True(detector.IsActive(10.5));
        Assert.False(detector.IsActive(10.81));
    }

    [Fact]
    public void AudioSampleMeterComputesNormalizedRMS()
    {
        var level = AudioLevelMeter.NormalizedRms([0.5f, -0.5f, 0.5f, -0.5f]);

        Assert.Equal(0.5f, level, 0.0001f);
    }

    // ── 本專案加的 ──

    [Fact]
    public void MeterSkipsNonFiniteSamplesAndClampsToOne()
    {
        Assert.Equal(0.5f, AudioLevelMeter.NormalizedRms([0.5f, float.NaN, -0.5f, float.PositiveInfinity]), 0.0001f);
        Assert.Equal(1f, AudioLevelMeter.NormalizedRms([3f, -3f]));
        Assert.Equal(0f, AudioLevelMeter.NormalizedRms([]));
    }

    /// <summary>
    /// 原版的門檻（0.012／0.04）是照 iPhone 麥克風調的，拿瓦基 Focusrite 的實測音量驗一次（第 13.2 節 mic01）：
    /// 按 Enter 的雜音約 0.008（-42 dBFS）不能算講話，開口約 0.12（-18 dBFS）要立刻算講話。
    /// </summary>
    [Fact]
    public void ThresholdsFitTheFocusriteLevelsMeasuredInMic01()
    {
        var detector = new VoiceActivityDetector();
        for (var t = 0.0; t < 0.5; t += 0.02)
        {
            detector.Process(0.008f, t);
        }
        Assert.False(detector.IsActive(0.5));

        detector.Process(0.12f, 2.2);
        Assert.True(detector.IsActive(2.2));
    }

    [Fact]
    public void ResetForgetsActivity()
    {
        var detector = new VoiceActivityDetector();
        detector.Process(0.12f, 1);

        detector.Reset();

        Assert.False(detector.IsActive(1.1));
    }
}
