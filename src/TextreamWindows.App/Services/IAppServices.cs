using TextreamWindows.Core.Overlay;
using TextreamWindows.Core.Session;
using TextreamWindows.Core.Text;
using TextreamWindows.Speech.Audio;

namespace TextreamWindows.App.Services;

/// <summary>ViewModel 對外的一切：對話框、麥克風清單、開始跟讀。測試換成假的，正式版用 WPF。</summary>
public interface IAppServices
{
    IReadOnlyList<MicrophoneInfo> ListMicrophones();

    /// <summary>「開啟」對話框，取消回 null。</summary>
    string? AskOpenPath();

    /// <summary>「另存新檔」對話框，取消回 null。</summary>
    string? AskSavePath(string suggestedName);

    /// <summary>講稿有沒存的修改時問：true 存檔、false 不存、null 取消（不要繼續原本的動作）。</summary>
    bool? AskSaveChanges(string displayName);

    void ShowError(string message);

    /// <summary>
    /// 開始跟讀（逐字追蹤要載入模型，會花 1～2 秒），從講稿位置 <paramref name="startAt"/>（文字元素）開始。
    /// 失敗丟例外，訊息給瓦基看。
    /// </summary>
    Task<IFollowRunner> StartFollowAsync(PromptScript prompt, FollowMode mode, double scrollSpeed, MicrophoneInfo? microphone, int startAt, SpeechLanguage language);

    /// <summary>這個程式的版本（csproj 的 Version）。</summary>
    string CurrentVersion { get; }

    /// <summary>向 GitHub 要最新 Release 的資訊（UpdateCheck.LatestReleaseApi 的回應內容）。連不上丟例外。</summary>
    Task<string> FetchLatestReleaseAsync();

    /// <summary>「有新版，要前往下載嗎？」：true 前往下載、false 稍後。</summary>
    bool AskOpenUpdate(string latestVersion, string currentVersion);

    /// <summary>用預設瀏覽器開網頁。</summary>
    void OpenUrl(string url);

    void ShowInfo(string message);

    /// <summary>目前接著的螢幕（DIP）。</summary>
    IReadOnlyList<DisplayInfo> ListDisplays();

    /// <summary>開浮層（計畫書第 5 階段步驟 2）。模式決定捲動方式與狀態文字；<paramref name="hideFromCapture"/> 時錄影、分享畫面看不到它（步驟 5）。</summary>
    IOverlay ShowOverlay(OverlayStyle style, ScreenRect bounds, PromptScript prompt, FollowMode mode, bool hideFromCapture, double fontSize);
}

/// <summary>浮層每次更新要顯示的：高亮位置、經過時間、麥克風音量（0～1 的 RMS）、是否在講話。</summary>
public readonly record struct OverlayStatus(int Progress, TimeSpan Elapsed, float AudioLevel, bool IsSpeaking, bool IsPaused = false, string Heard = "");

/// <summary>開著的浮層。由 UI 計時器每 50 毫秒更新一次位置，狀態在那時讀。</summary>
public interface IOverlay
{
    /// <summary>瓦基在浮層上按 Esc 或關掉它：要停止跟讀。</summary>
    bool WasClosedByUser { get; }

    /// <summary>「讀完了」畫面上按了「重來」。</summary>
    bool RestartRequested { get; }

    /// <summary>浮層現在的位置與大小（浮動視窗被拖過、拉過之後要記下來）。</summary>
    ScreenRect Bounds { get; }

    void Update(OverlayStatus status);

    /// <summary>瓦基在浮層上點了某個字、或轉了滾輪：要跳去的講稿位置（文字元素）。拿了就清掉，沒有回 null。</summary>
    int? TakeJumpRequest();

    /// <summary>讀完了：浮層顯示「讀完了」，ViewModel 過一秒再關。</summary>
    void ShowDone();

    void Close();
}

/// <summary>正在跑的一次跟讀。由 UI 計時器每 50 毫秒呼叫 <see cref="Tick"/>，狀態在那時讀。</summary>
public interface IFollowRunner : IDisposable
{
    /// <summary>高亮位置（文字元素）。</summary>
    int Progress { get; }

    bool IsSpeaking { get; }

    /// <summary>麥克風目前的音量（最後一批樣本的 RMS，0～1）；定速捲動沒有麥克風，永遠是 0。</summary>
    float AudioLevel { get; }

    /// <summary>已經停了（讀完、被停止、或麥克風出錯）。</summary>
    bool HasStopped { get; }

    /// <summary>停下來的原因是錯誤時，帶例外。</summary>
    Exception? Error { get; }

    /// <summary>剛剛聽到的話（辨識引擎的原文，簡體）；沒有辨識的模式是空字串。</summary>
    string Heard { get; }

    /// <summary>暫停中（Ctrl+Alt+L）：浮層留著、高亮不動。</summary>
    bool IsPaused { get; }

    void Tick();

    void Pause();

    void Resume();

    /// <summary>點字跳轉、滾輪追趕、跳一句：跳到講稿位置（文字元素），可以往回。</summary>
    void JumpTo(int characterOffset);

    void Stop();
}
