namespace TextreamWindows.Core.Session;

/// <summary>三種模式（計畫書第 3 節）。</summary>
public enum FollowMode
{
    /// <summary>逐字追蹤：聽講到哪個字，高亮就到哪。</summary>
    WordTracking,

    /// <summary>定速捲動：照設定的速度一直往前，不聽聲音。</summary>
    Classic,

    /// <summary>有講話才捲：照設定的速度往前，但只在偵測到講話時前進。</summary>
    VoiceActivated,
}
