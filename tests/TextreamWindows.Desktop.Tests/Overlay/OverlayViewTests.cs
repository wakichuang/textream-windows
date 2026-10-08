using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TextreamWindows.App.Overlay;
using TextreamWindows.App.Services;
using TextreamWindows.Core.Overlay;
using TextreamWindows.Core.Session;
using TextreamWindows.Core.Text;

namespace TextreamWindows.Desktop.Tests.Overlay;

/// <summary>
/// 浮層的樣子與行為：直接把 OverlayView 畫成圖（RenderTargetBitmap，STA 執行緒、不開視窗），不在瓦基的螢幕上跳東西。
/// 圖存到暫存資料夾（TEXTREAM_SNAPSHOT_DIR 可指定），開發時拿來看。
/// </summary>
public class OverlayViewTests
{
    private const string Script =
        "簡單來說，這是一個站在百年傳統上的簡化工具。作者希望透過一個簡單易懂的比喻，幫你看懂自己為什麼會那樣反應。\n" +
        "## 跟《蛤蟆先生去看心理師》有什麼不一樣？\n" +
        "以前聽過我分享《蛤蟆先生去看心理師》的朋友，讀到這裡應該會覺得很熟悉。\n" +
        "沒錯，兩本書是同一棵樹上的兩根分枝。\n" +
        "「兒童自我、成人自我、父母自我」正是溝通分析（Transactional Analysis）的用語，也就是蒼鷺幫蛤蟆諮商的那套方法。史塔爾在書中沒有特別點名，但兩本書的根是同一條。\n" +
        "兩本書的共同點很清楚：「童年經驗」會形塑我們今天的反應，而最終的出口都在「大人」這個狀態，也就是說，你要為自己負責。";

    private static readonly PromptScript Prompt = new(Script);

    private static T OnSta<T>(Func<T> work)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            throw new InvalidOperationException("STA 執行緒裡出錯", error);
        }
        return result;
    }

    private static OverlayView Create(OverlayStyle style, FollowMode mode, double width, double height)
    {
        var view = new OverlayView(style) { AnimateScroll = false };
        view.Load(Prompt, mode);
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();
        return view;
    }

    private static void Layout(OverlayView view, double width, double height)
    {
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();
    }

    /// <summary>畫出來，回傳像素（BGRA）並存一張 PNG。</summary>
    private static byte[] Render(OverlayView view, int width, int height, string name)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var dir = Environment.GetEnvironmentVariable("TEXTREAM_SNAPSHOT_DIR") is { Length: > 0 } d ? d : Path.GetTempPath();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(dir, $"overlay-{name}.png")))
        {
            encoder.Save(stream);
        }
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    private static bool Bright(byte b, byte g, byte r, byte a) => a > 200 && r > 220 && g > 220 && b > 220;

    private static bool Dim(byte b, byte g, byte r, byte a) => a > 200 && r is > 50 and < 120 && Math.Abs(r - b) < 20;

    /// <summary>黃色（目前的字、音量條讀過的那段）。</summary>
    private static bool Yellow(byte b, byte g, byte r, byte a) => a > 200 && r > 200 && g > 160 && b < 90;

    private static List<(int X, int Y)> Where(byte[] bgra, int width, Func<byte, byte, byte, byte, bool> match)
    {
        var result = new List<(int, int)>();
        for (var i = 0; i < bgra.Length; i += 4)
        {
            if (match(bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]))
            {
                result.Add((i / 4 % width, i / 4 / width));
            }
        }
        return result;
    }

    private static int WordStart(int index) => Prompt.Words[index].CharacterRange.Start;

    // ── 外觀 ──

    [Fact]
    public void CapsuleIsDarkWithRoundedBottomCornersOnly()
    {
        var pixels = OnSta(() =>
        {
            var view = Create(OverlayStyle.Capsule, FollowMode.WordTracking, 420, 180);
            view.Update(new OverlayStatus(0, TimeSpan.FromSeconds(12), 0.05f, true));
            Layout(view, 420, 180);
            return Render(view, 420, 180, "capsule");
        });

        Assert.True(Where(pixels, 420, Bright).Count > 500, "還沒讀的字要是亮白的");
        Assert.True(pixels[3] > 200, "上面兩個角貼著螢幕頂端，是方的（不透明）");
        Assert.True(pixels[((180 - 1) * 420) * 4 + 3] < 50, "下面兩個角是圓的（透明）");
    }

    [Fact]
    public void ReadTextIsDimTheCurrentCharacterIsYellowAndTheRestIsBright()
    {
        var pixels = OnSta(() =>
        {
            var view = Create(OverlayStyle.Floating, FollowMode.WordTracking, 700, 300);
            view.Update(new OverlayStatus(WordStart(30), TimeSpan.Zero, 0, false));
            Layout(view, 700, 300);
            return Render(view, 700, 300, "floating-halfway");
        });

        Assert.True(Where(pixels, 700, Dim).Count > 200, "讀過的字要變淡");
        Assert.True(Where(pixels, 700, Yellow).Count > 30, "目前的字是黃色");
        Assert.True(Where(pixels, 700, Bright).Count > 500, "後面還沒讀的亮白");
    }

    [Fact]
    public void TheYellowHighlightSpansSeveralCharacters()
    {
        // 瓦基 2026-10-08：只標一個中文字太少，改成一次 2～4 個字（ReadingHighlight）。開頭「簡單來說」四個字都是黃的
        var pixels = OnSta(() =>
        {
            var view = Create(OverlayStyle.Floating, FollowMode.WordTracking, 700, 300);
            return Render(view, 700, 300, "floating-highlight");
        });

        var yellow = Where(pixels, 700, Yellow);
        var width = yellow.Max(p => p.X) - yellow.Min(p => p.X);
        Assert.True(width > 28 * 3, $"黃色寬 {width} 像素，要超過三個 28 號字");
        Assert.True(width < 28 * 5, $"黃色寬 {width} 像素，不能超過四個字太多");
    }

    [Fact]
    public void TheScriptUsesTheChosenFontSize()
    {
        // 瓦基 2026-10-08：字級可以調
        var (chosen, clamped) = OnSta(() => (new OverlayView(OverlayStyle.Floating, 40).TextFontSize, new OverlayView(OverlayStyle.Floating, 4).TextFontSize));

        Assert.Equal(40, chosen);
        Assert.Equal(14, clamped);
    }

    [Fact]
    public void TheBackgroundIsFullyOpaque()
    {
        // 瓦基 2026-10-08：浮層不要半透明，反灰、黃、白三種顏色就夠清楚了
        var pixels = OnSta(() =>
        {
            var view = Create(OverlayStyle.Floating, FollowMode.WordTracking, 700, 300);
            return Render(view, 700, 300, "floating-opaque");
        });

        var middleLeft = (150 * 700 + 4) * 4; // 左邊內距裡，只有背景
        Assert.Equal(255, pixels[middleLeft + 3]);
        // 閱讀前哨站的暖深色（瓦基 2026-10-08），不是原本的冷黑 #0E0F11
        var expected = BrandPalette.OverlayBackground;
        Assert.Equal((expected.B, expected.G, expected.R), (pixels[middleLeft], pixels[middleLeft + 1], pixels[middleLeft + 2]));
    }

    [Fact]
    public void TheFirstLineIsAsBrightAsTheSecondBeforeReadingStarts()
    {
        // 瓦基 2026-10-08：一開始最上面灰濛濛的，要講到 5～10% 才變清楚（上緣淡出蓋在還沒讀的第一行上）
        var (pixels, top) = OnSta(() =>
        {
            var view = Create(OverlayStyle.Floating, FollowMode.WordTracking, 700, 300);
            var viewport = Find<Canvas>(view);
            var y = viewport.TranslatePoint(new Point(0, 0), view).Y;
            return (Render(view, 700, 300, "floating-start"), (int)y);
        });

        var line = (int)(28 * 1.45);
        // 第一行開頭是黃色的 2～4 個字，所以亮白與黃色一起算
        var lit = Where(pixels, 700, (b, g, r, a) => Bright(b, g, r, a) || Yellow(b, g, r, a));
        var first = lit.Count(p => p.Y >= top && p.Y < top + line);
        var second = lit.Count(p => p.Y >= top + line && p.Y < top + 2 * line);
        Assert.True(second > 300, $"第二行要有亮白的字（{second}）");
        Assert.True(first >= second * 0.8, $"第一行 {first} 個亮的像素，第二行 {second} 個：第一行不能被淡出蓋掉");
    }

    private static T Find<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }
            if (TryFind<T>(child) is { } found)
            {
                return found;
            }
        }
        throw new InvalidOperationException($"找不到 {typeof(T).Name}");
    }

    private static T? TryFind<T>(DependencyObject root) where T : DependencyObject
    {
        try
        {
            return Find<T>(root);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    // ── 捲動（第 5.3 步，瓦基 2026-10-08：跳著講要看得出來有跳過去） ──

    [Fact]
    public void FarIntoTheScriptTheCurrentLineStaysInTheUpperThird()
    {
        var (yellowY, height) = OnSta(() =>
        {
            var view = Create(OverlayStyle.Floating, FollowMode.WordTracking, 700, 320);
            view.Update(new OverlayStatus(WordStart(Prompt.Words.Count - 20), TimeSpan.Zero, 0, false));
            Layout(view, 700, 320);
            var pixels = Render(view, 700, 320, "floating-far");
            // 只看文字區（扣掉最下面的控制列），找黃色的字
            var text = Where(pixels, 700, Yellow).Where(p => p.Y < 320 - 50).ToList();
            return (text.Count == 0 ? -1 : text.Average(p => p.Y), 320.0);
        });

        Assert.True(yellowY > 0, "找不到目前的字（黃色），可能捲出畫面了");
        Assert.InRange(yellowY / height, 0.2, 0.5);
    }

    [Fact]
    public void AJumpAnimatesWithTheReadingScrollMotion()
    {
        var (small, big) = OnSta(() =>
        {
            var view = Create(OverlayStyle.Floating, FollowMode.WordTracking, 700, 320);
            view.AnimateScroll = true;
            view.Update(new OverlayStatus(WordStart(40), TimeSpan.Zero, 0, false));
            Layout(view, 700, 320);
            view.Update(new OverlayStatus(WordStart(70), TimeSpan.Zero, 0, false));
            Layout(view, 700, 320);
            var first = view.LastMotion;
            view.Update(new OverlayStatus(WordStart(Prompt.Words.Count - 5), TimeSpan.Zero, 0, false));
            Layout(view, 700, 320);
            return (first, view.LastMotion);
        });

        Assert.True(small?.EaseOut);
        Assert.True(big?.Seconds > small?.Seconds, $"跳得遠要滑得久：{small} → {big}");
    }

    // ── 資訊與控制 ──

    [Theory]
    [InlineData(FollowMode.WordTracking, true, "● 跟著你念")]
    [InlineData(FollowMode.WordTracking, false, "○ 等你開口")]
    [InlineData(FollowMode.VoiceActivated, true, "● 捲動中")]
    [InlineData(FollowMode.Classic, false, "定速捲動")]
    public void TheHeaderShowsElapsedTimeAndStatus(FollowMode mode, bool speaking, string expectedStatus)
    {
        var (elapsed, status) = OnSta(() =>
        {
            var view = Create(OverlayStyle.Capsule, mode, 420, 180);
            view.Update(new OverlayStatus(0, TimeSpan.FromSeconds(75), 0.05f, speaking));
            return (view.ElapsedText, view.StatusText);
        });

        Assert.Equal("01:15", elapsed);
        Assert.StartsWith(expectedStatus, status);
    }

    [Fact]
    public void PausedShowsInTheHeader()
    {
        var status = OnSta(() =>
        {
            var view = Create(OverlayStyle.Capsule, FollowMode.WordTracking, 420, 180);
            view.Update(new OverlayStatus(0, TimeSpan.Zero, 0.1f, true, IsPaused: true));
            return view.StatusText;
        });

        Assert.Contains("暫停", status);
    }

    // ── 即時辨識文字（瓦基 2026-10-08：講了什麼就顯示在下方中間，跟音量條、✕ 同一行） ──

    [Fact]
    public void WhatWasJustHeardShowsInTraditionalChinese()
    {
        // 模型吐出的是簡體，畫面上給瓦基看繁體（Windows 內建的轉換，一字一字轉）
        var heard = OnSta(() =>
        {
            var view = Create(OverlayStyle.Capsule, FollowMode.WordTracking, 520, 200);
            view.Update(new OverlayStatus(0, TimeSpan.Zero, 0.1f, true, Heard: "阅读前哨站的瓦基 Schema"));
            Layout(view, 520, 200);
            Render(view, 520, 200, "capsule-heard");
            return view.HeardText;
        });

        Assert.Equal("閱讀前哨站的瓦基 Schema", heard);
    }

    [Fact]
    public void ALongUtteranceKeepsTheNewestWordsVisible()
    {
        const string Long = "今天想跟大家聊聊閱讀這件事情我們先從一本書開始講起最後一句";
        var shown = OnSta(() =>
        {
            var view = Create(OverlayStyle.Capsule, FollowMode.WordTracking, 420, 180);
            view.Update(new OverlayStatus(0, TimeSpan.Zero, 0.1f, true, Heard: Long));
            Layout(view, 420, 180);
            view.Update(new OverlayStatus(0, TimeSpan.Zero, 0.1f, true, Heard: Long + "了"));
            Layout(view, 420, 180);
            Render(view, 420, 180, "capsule-heard-long");
            return (view.HeardShownText, view.HeardFits);
        });

        Assert.True(shown.HeardFits, "顯示的那段要整段放得下，不能被右邊切掉");
        var text = shown.HeardShownText;
        Assert.StartsWith("…", text);
        Assert.EndsWith("最後一句了", text); // 最新講的在右邊，一定看得到
    }

    [Fact]
    public void NothingHeardShowsNothing()
    {
        var heard = OnSta(() =>
        {
            var view = Create(OverlayStyle.Capsule, FollowMode.Classic, 520, 200);
            view.Update(new OverlayStatus(0, TimeSpan.Zero, 0, false));
            return view.HeardText;
        });

        Assert.Equal("", heard);
    }

    // ── 第 5.4 步：點字跳轉、滾輪追趕 ──

    [Fact]
    public void ClickingACharacterGivesItsPosition()
    {
        // 拿黃色那段當靶：它的第一個字在畫面上哪裡是已知的，點它的左半邊、右半邊都要回那個字
        var display = new PromptDisplay(Prompt);
        var highlight = ReadingHighlight.Range(Prompt, display, WordStart(30));
        var firstWord = Prompt.Words.First(w => w.CharacterRange.Start == highlight.Start);
        var secondWord = Prompt.Words[firstWord.Id + 1];

        var (left, right, next, header) = OnSta(() =>
        {
            var view = Create(OverlayStyle.Floating, FollowMode.WordTracking, 700, 300);
            view.Update(new OverlayStatus(WordStart(30), TimeSpan.Zero, 0, false));
            Layout(view, 700, 300);
            var yellow = Where(Render(view, 700, 300, "floating-click"), 700, Yellow).Where(p => p.Y < 300 - 50).ToList();
            var x = yellow.Min(p => p.X);
            var y = yellow.Average(p => p.Y);
            const double Glyph = 28;
            return (view.CharacterOffsetAt(new Point(x + Glyph * 0.2, y)),
                view.CharacterOffsetAt(new Point(x + Glyph * 0.8, y)),
                view.CharacterOffsetAt(new Point(x + Glyph * 1.2, y)),
                view.CharacterOffsetAt(new Point(350, 8)));
        });

        Assert.Equal(firstWord.CharacterRange.Start, left);
        Assert.Equal(firstWord.CharacterRange.Start, right);
        Assert.Equal(secondWord.CharacterRange.Start, next);
        Assert.Null(header); // 點到上方狀態列不算
    }

    [Fact]
    public void TheWheelMovesALineAtATime()
    {
        var (down1, down2, upFromTop, upFromLine3) = OnSta(() =>
        {
            var view = Create(OverlayStyle.Floating, FollowMode.WordTracking, 700, 300);
            var d1 = view.LineJumpTarget(1);
            var d2 = view.LineJumpTarget(2);
            var up = view.LineJumpTarget(-1);
            view.Update(new OverlayStatus(d2, TimeSpan.Zero, 0, false));
            Layout(view, 700, 300);
            return (d1, d2, up, view.LineJumpTarget(-1));
        });

        // 700 寬、28 號字一行約 23 個字（比對全文中文字之間有空格，一個字佔 2 格）
        Assert.InRange(down1, 30, 60);
        Assert.InRange(down2 - down1, 30, 60);
        Assert.Equal(0, upFromTop);
        Assert.Equal(down1, upFromLine3);
    }

    [Fact]
    public void TheMeterShowsProgressInYellow()
    {
        var footerYellow = OnSta(() =>
        {
            var view = Create(OverlayStyle.Floating, FollowMode.WordTracking, 700, 300);
            for (var i = 0; i < 30; i++)
            {
                view.Update(new OverlayStatus(WordStart(Prompt.Words.Count / 2), TimeSpan.Zero, 0.12f, true));
            }
            Layout(view, 700, 300);
            var pixels = Render(view, 700, 300, "floating-meter");
            return Where(pixels, 700, Yellow).Count(p => p.Y > 300 - 50);
        });

        Assert.True(footerYellow > 20, "音量條讀過的那段是黃的");
    }

    [Fact]
    public void TheCloseButtonAsksToStop()
    {
        var asked = OnSta(() =>
        {
            var view = Create(OverlayStyle.Capsule, FollowMode.WordTracking, 420, 180);
            var requested = false;
            view.CloseRequested += () => requested = true;
            var button = FindChild<Button>(view, b => b.ToolTip is string tip && tip.Contains("結束"));
            // 直接發 Click：UI Automation 的 Invoke 會排到 Dispatcher 之後才跑，這條測試執行緒沒有在處理排程
            button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            return requested;
        });

        Assert.True(asked);
    }

    [Fact]
    public void TheDoneScreenOffersRestartAndClose()
    {
        // 瓦基 2026-10-08：讀完停在「讀完了」，讓使用者自己選重來或結束
        var (restarts, closes, shown) = OnSta(() =>
        {
            var view = Create(OverlayStyle.Floating, FollowMode.WordTracking, 700, 300);
            var restart = 0;
            var close = 0;
            view.RestartRequested += () => restart++;
            view.CloseRequested += () => close++;
            view.ShowDone();
            Layout(view, 700, 300);
            Render(view, 700, 300, "floating-done-buttons");
            Assert.Contains("Ctrl+Alt+L", (string)FindChild<Button>(view, b => b.Content is string t && t.StartsWith("重來")).Content);
            var again = FindChild<Button>(view, b => b.Content is string text && text.StartsWith("重來"));
            var end = FindChild<Button>(view, b => b.Content is string text && text.StartsWith("結束"));
            again.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            end.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            return (restart, close, ShownInTree(again) && ShownInTree(end));
        });

        Assert.True(shown, "兩顆鍵都要看得到");
        Assert.Equal(1, restarts);
        Assert.Equal(1, closes);
    }

    [Fact]
    public void TheCloseButtonShowsItsEscShortcut()
    {
        // 瓦基 2026-10-08：✕ 旁邊提示可以按 Esc
        var shown = OnSta(() =>
        {
            var view = Create(OverlayStyle.Capsule, FollowMode.WordTracking, 450, 180);
            view.ShowDone();
            Layout(view, 450, 160);
            Render(view, 450, 160, "capsule-done");
            return ShownInTree(FindChild<TextBlock>(view, t => t.Text == "Esc"));
        });

        Assert.True(shown);
    }

    [Fact]
    public void DoneShowsAFinishedMessage()
    {
        var (before, after) = OnSta(() =>
        {
            var view = Create(OverlayStyle.Floating, FollowMode.WordTracking, 700, 300);
            var done = FindChild<TextBlock>(view, t => t.Text.Contains("讀完了"));
            var hiddenBefore = !ShownInTree(done);
            view.ShowDone();
            Layout(view, 700, 300);
            Render(view, 700, 300, "floating-done");
            return (hiddenBefore, ShownInTree(done));
        });

        Assert.True(before, "還沒讀完時不顯示「讀完了」");
        Assert.True(after, "讀完要顯示「讀完了」");
    }

    /// <summary>自己跟所有上層都不是 Collapsed／Hidden、而且不透明。</summary>
    private static bool ShownInTree(FrameworkElement element)
    {
        for (DependencyObject? node = element; node is not null; node = LogicalTreeHelper.GetParent(node) ?? VisualTreeHelper.GetParent(node))
        {
            if (node is UIElement ui && (ui.Visibility != Visibility.Visible || ui.Opacity < 0.5))
            {
                return false;
            }
        }
        return true;
    }

    private static T FindChild<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject
    {
        var queue = new Queue<DependencyObject>([root]);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node is T hit && match(hit))
            {
                return hit;
            }
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            {
                queue.Enqueue(child);
            }
        }
        throw new InvalidOperationException($"找不到符合條件的 {typeof(T).Name}");
    }

    [Fact]
    public void DisplaysFindsThisComputersPrimaryScreen()
    {
        var displays = Displays.List();

        var primary = Assert.Single(displays, d => d.IsPrimary);
        Assert.StartsWith(@"\\.\DISPLAY", primary.Name);
        Assert.True(primary.Bounds.Width >= 800 && primary.Bounds.Height >= 600, $"{primary.Bounds}");
        Assert.True(primary.WorkArea.Height <= primary.Bounds.Height);
    }
}
