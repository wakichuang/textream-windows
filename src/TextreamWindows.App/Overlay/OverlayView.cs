using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using TextreamWindows.App.Services;
using TextreamWindows.Core.Overlay;
using TextreamWindows.Core.Session;
using TextreamWindows.Core.Text;

namespace TextreamWindows.App.Overlay;

/// <summary>
/// 浮層的內容（計畫書第 5 階段步驟 2、3、6；2026-10-08 瓦基要求參考 Mac 版調整）：
/// <list type="bullet">
/// <item>上方：狀態（● 跟著你念、○ 等你開口、定速捲動）與經過時間。</item>
/// <item>中間：整篇講稿排成一長條往上平移（<see cref="ReadingScroll"/>），目前那行停在上方三分之一；
/// 讀過的變淡、目前要念的 2～4 個字黃色、沒讀的亮白。
/// 不做上下邊緣淡出：第一版有，還沒讀的第一行被淡出蓋成灰色，瓦基 2026-10-08 要拿掉，三種顏色就夠清楚。</item>
/// <item>下方：麥克風音量條（讀過的比例那段是黃的）與 ✕ 結束鍵；頂端膠囊再加一條「可以拉」的提示線。</item>
/// <item>讀完：中間換成「✓ 讀完了」。</item>
/// </list>
/// 浮層一律深色（跟 Mac 版一樣），不跟系統深淺色；配色要套品牌色時讀 DESIGN.md（第 5 階段步驟 8）。
/// </summary>
public sealed class OverlayView : Border
{
    private const int MeterBars = 30;

    // 閱讀前哨站配色（瓦基 2026-10-08）：暖深色底、米白字、品牌黃；三種字色的分工照舊
    private static readonly Color Ink = BrandPalette.OverlayText;
    private static readonly Brush AheadBrush = Frozen(Ink);
    private static readonly Brush ReadBrush = Frozen(Color.FromArgb(0x5C, Ink.R, Ink.G, Ink.B)); // 約 35%
    private static readonly Brush CurrentBrush = Frozen(BrandPalette.Sun); // 品牌黃 --sun（原本是 Mac 版的 #FFD60A）
    private static readonly Brush MutedBrush = Frozen(Color.FromArgb(0x8C, Ink.R, Ink.G, Ink.B)); // 約 55%
    private static readonly Brush MeterIdleBrush = Frozen(Color.FromArgb(0x33, Ink.R, Ink.G, Ink.B));
    private static readonly Brush DoneBrush = Frozen(Color.FromRgb(0x34, 0xC7, 0x59));

    private readonly TextBlock _text;
    private readonly Run _read = new() { Foreground = ReadBrush };
    private readonly Run _current = new() { Foreground = CurrentBrush };
    private readonly Run _ahead = new() { Foreground = AheadBrush };
    private readonly TranslateTransform _shift = new();
    private readonly Canvas _viewport;
    private readonly TextBlock _status;
    private readonly TextBlock _elapsed;
    private readonly TextBlock _heard;
    private string _heardFull = "";
    private readonly Rectangle[] _bars = new Rectangle[MeterBars];
    private readonly float[] _levels = new float[MeterBars];
    private readonly UIElement _reading;
    private readonly UIElement _done;
    private PromptScript? _prompt;
    private PromptDisplay? _display;
    private FollowMode _mode;
    private int _shownProgress = -1;
    private double _target;
    private int _currentStart;
    private int _currentEnd;
    private Point? _pressedAt;
    private int _wheelDelta;

    /// <param name="fontSize">講稿字級；沒給就用這種浮層的預設（瓦基 2026-10-08 起可在主視窗調）</param>
    public OverlayView(OverlayStyle style, double? fontSize = null)
    {
        Style = style;
        Background = Frozen(BrandPalette.OverlayBackground); // 不透明（瓦基 2026-10-08：不要半透明），閱讀前哨站的暖深色
        CornerRadius = style switch
        {
            OverlayStyle.Capsule => new CornerRadius(0, 0, 26, 26), // 貼著螢幕頂端，像瀏海一樣只有下面兩個角是圓的
            OverlayStyle.Floating => new CornerRadius(16),
            _ => new CornerRadius(0),
        };
        Padding = style switch
        {
            OverlayStyle.Capsule => new Thickness(20, 8, 20, 6),
            OverlayStyle.Floating => new Thickness(20, 10, 20, 10),
            _ => new Thickness(120, 40, 120, 40),
        };
        var textSize = OverlayLayout.ClampFontSize(fontSize ?? OverlayLayout.DefaultFontSize(style));

        // 上方：狀態與經過時間
        _status = SmallText(HorizontalAlignment.Left);
        _elapsed = SmallText(HorizontalAlignment.Right);
        _elapsed.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        var header = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        header.Children.Add(_status);
        header.Children.Add(_elapsed);

        // 中間：講稿，整條往上平移
        _text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Microsoft JhengHei UI"),
            FontSize = textSize,
            LineHeight = textSize * 1.45,
            VerticalAlignment = VerticalAlignment.Top,
            RenderTransform = _shift,
        };
        _text.Inlines.Add(_read);
        _text.Inlines.Add(_current);
        _text.Inlines.Add(_ahead);
        // 放在 Canvas 上：高度不受浮層限制，整條講稿都排得出來，再用平移決定看哪一段。
        // （第一版放在 Grid 裡，超出浮層高度的部分被 WPF 裁掉，往上平移後只剩空白，2026-10-08 測試抓到）
        _viewport = new Canvas
        {
            ClipToBounds = true,
            Background = Brushes.Transparent,
            Children = { _text },
        };
        _viewport.SizeChanged += (_, e) =>
        {
            _text.Width = e.NewSize.Width;
            _shownProgress = -1; // 寬度變了換行就變了，下次更新重算捲動
        };

        var doneText = new TextBlock
        {
            Text = "✓ 讀完了",
            Foreground = DoneBrush,
            FontSize = Math.Max(18, textSize * 0.9),
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // 讀完停在這裡，由瓦基自己選（2026-10-08）：重來＝從頭再念一次，結束＝回編輯器
        var again = PillButton("重來（Ctrl+Alt+L）"); // 瓦基 2026-10-08：讀完再按 Ctrl+Alt+L 就是重來
        again.Click += (_, _) => RestartRequested?.Invoke();
        var end = PillButton("結束（Esc）");
        end.Click += (_, _) => CloseRequested?.Invoke();
        // 頂端膠囊可能只有 100 高：字和鍵排成一列；其他浮層上下排
        var oneRow = style == OverlayStyle.Capsule;
        var choices = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = oneRow ? new Thickness(10, 0, 0, 0) : new Thickness(0, 12, 0, 0),
            Children = { again, end },
        };
        _done = new Grid
        {
            Visibility = Visibility.Collapsed,
            Children =
            {
                new StackPanel
                {
                    Orientation = oneRow ? Orientation.Horizontal : Orientation.Vertical,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = { doneText, choices },
                },
            },
        };
        _reading = _viewport;
        var middle = new Grid { Children = { _viewport, _done } };

        // 下方：音量條與 ✕
        var meter = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < MeterBars; i++)
        {
            _bars[i] = new Rectangle
            {
                Width = 3,
                Height = 3,
                RadiusX = 1.5,
                RadiusY = 1.5,
                Margin = new Thickness(0, 0, 2, 0),
                Fill = MeterIdleBrush,
                VerticalAlignment = VerticalAlignment.Center,
            };
            meter.Children.Add(_bars[i]);
        }
        var closeButton = CloseButton();
        closeButton.Click += (_, _) => CloseRequested?.Invoke();
        // ✕ 旁邊提示 Esc（瓦基 2026-10-08）
        var escHint = new TextBlock { Text = "Esc", FontSize = 11, Foreground = MutedBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        var close = new StackPanel { Orientation = Orientation.Horizontal, Children = { escHint, closeButton } };
        // 中間：剛剛聽到的話（瓦基 2026-10-08：讓人感覺講的東西真的有被辨認到），只留最新的一段
        _heard = new TextBlock
        {
            FontSize = 13,
            Foreground = MutedBrush,
            FontFamily = new FontFamily("Microsoft JhengHei UI"),
            TextWrapping = TextWrapping.NoWrap,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0),
        };
        var heardBox = new Border { ClipToBounds = true, Child = _heard };
        heardBox.SizeChanged += (_, _) => ShowHeard();
        var footer = new Grid { Height = 28, Margin = new Thickness(0, 4, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(meter, 0);
        Grid.SetColumn(heardBox, 1);
        Grid.SetColumn(close, 2);
        footer.Children.Add(meter);
        footer.Children.Add(heardBox);
        footer.Children.Add(close);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(header, 0);
        Grid.SetRow(middle, 1);
        Grid.SetRow(footer, 2);
        layout.Children.Add(header);
        layout.Children.Add(middle);
        layout.Children.Add(footer);
        if (style == OverlayStyle.Capsule)
        {
            // 下緣中間一條短線：提示這裡可以往下拉（Mac 版的 grabber）
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var grabber = new Rectangle { Width = 36, Height = 4, RadiusX = 2, RadiusY = 2, Fill = MeterIdleBrush, Margin = new Thickness(0, 4, 0, 0) };
            Grid.SetRow(grabber, 3);
            layout.Children.Add(grabber);
        }
        Child = layout;

        // 第 5.4 步：點任何一個字就從那裡開始、滾輪一格跳一行（往下是往後）
        _viewport.Cursor = Cursors.Hand;
        MouseLeftButtonDown += (_, e) => _pressedAt = e.GetPosition(this);
        MouseLeftButtonUp += (_, e) =>
        {
            var position = e.GetPosition(this);
            var wasClick = _pressedAt is { } pressed && (position - pressed).Length < ClickSlop;
            _pressedAt = null;
            if (wasClick && CharacterOffsetAt(position) is { } offset)
            {
                JumpRequested?.Invoke(offset);
            }
        };
        MouseWheel += (_, e) =>
        {
            _wheelDelta += e.Delta;
            var notches = _wheelDelta / Mouse.MouseWheelDeltaForOneLine;
            _wheelDelta -= notches * Mouse.MouseWheelDeltaForOneLine;
            if (notches != 0)
            {
                JumpRequested?.Invoke(LineJumpTarget(-notches));
            }
            e.Handled = true;
        };
    }

    public new OverlayStyle Style { get; }

    /// <summary>捲動要不要動畫（測試關掉，直接跳到位）。</summary>
    public bool AnimateScroll { get; set; } = true;

    /// <summary>最後一次捲動用的動作（測試看「跳越遠滑越久」）。</summary>
    public ScrollMotion? LastMotion { get; private set; }

    public string ElapsedText => _elapsed.Text;

    public string StatusText => _status.Text;

    /// <summary>講稿的字級。</summary>
    public double TextFontSize => _text.FontSize;

    /// <summary>浮層下方顯示的「剛剛聽到的話」（轉成繁體的全文；畫面上放不下時只顯示最後一段）。</summary>
    public string HeardText => _heardFull;

    /// <summary>畫面上實際顯示的那一段（測試用）。</summary>
    public string HeardShownText => _heard.Text;

    /// <summary>顯示的那段整段放得下（測試用）。</summary>
    public bool HeardFits => TextWidth(_heard.Text) <= HeardRoom() + 0.5;

    /// <summary>中間那格扣掉左右留白，還有多寬可以放字。</summary>
    private double HeardRoom() =>
        ((_heard.Parent as FrameworkElement)?.ActualWidth ?? 0) - _heard.Margin.Left - _heard.Margin.Right;

    private double TextWidth(string text) =>
        new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(_heard.FontFamily, _heard.FontStyle, _heard.FontWeight, _heard.FontStretch),
            _heard.FontSize, _heard.Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace;

    /// <summary>瓦基按了 ✕。</summary>
    public event Action? CloseRequested;

    public event Action? RestartRequested;

    /// <summary>瓦基點了某個字、或轉了滾輪：要跳去的講稿位置（文字元素）。</summary>
    public event Action<int>? JumpRequested;

    /// <summary>按下到放開移動不到這麼多（DIP）才算點一下；浮動視窗拖曳移動時不要誤跳。</summary>
    public const double ClickSlop = 4;

    public void Load(PromptScript prompt, FollowMode mode)
    {
        _prompt = prompt;
        _display = new PromptDisplay(prompt);
        _mode = mode;
        _shownProgress = -1;
        _target = 0;
        _shift.BeginAnimation(TranslateTransform.YProperty, null);
        _shift.Y = 0;
        Update(new OverlayStatus(0, TimeSpan.Zero, 0, false));
    }

    public void Update(OverlayStatus status)
    {
        _elapsed.Text = $"{(int)status.Elapsed.TotalMinutes:00}:{status.Elapsed.Seconds:00}";
        _status.Text = status.IsPaused ? "❚❚ 暫停中" : _mode switch
        {
            FollowMode.WordTracking => status.IsSpeaking ? "● 跟著你念" : "○ 等你開口",
            FollowMode.VoiceActivated => status.IsSpeaking ? "● 捲動中" : "○ 等你開口",
            _ => "定速捲動",
        };
        UpdateMeter(status);
        var heard = TraditionalChinese.From(status.Heard);
        if (heard != _heardFull)
        {
            _heardFull = heard;
            ShowHeard();
        }
        ShowProgress(status.Progress);
    }

    /// <summary>浮層上 <paramref name="point"/>（本元件座標）那個字的講稿位置；不在講稿區（狀態列、音量條）回 null。</summary>
    public int? CharacterOffsetAt(Point point)
    {
        if (_display is null)
        {
            return null;
        }
        var inViewport = TranslatePoint(point, _viewport);
        if (inViewport.X < 0 || inViewport.Y < 0 || inViewport.X > _viewport.ActualWidth || inViewport.Y > _viewport.ActualHeight)
        {
            return null;
        }
        var inText = TranslatePoint(point, _text);
        if (_text.GetPositionFromPoint(inText, snapToText: true) is not { } pointer || DisplayOffsetOf(pointer) is not { } offset)
        {
            return null;
        }
        // 點在字的右半邊時，位置會靠到字的後面（或下一行開頭）：退回被點的那個字
        var after = pointer.GetCharacterRect(LogicalDirection.Forward);
        if (offset > 0 && (after.IsEmpty || inText.X < after.Left || inText.Y < after.Top))
        {
            offset--;
        }
        return _display.ToCharacterOffset(offset);
    }

    /// <summary>滾輪：從目前那一行往下（正）或往上（負）數 <paramref name="lines"/> 行，那一行開頭的講稿位置。</summary>
    public int LineJumpTarget(int lines)
    {
        if (_display is null || _prompt is null)
        {
            return 0;
        }
        _text.UpdateLayout();
        var anchor = _current.Text.Length > 0 ? _current.ContentStart : _ahead.ContentStart;
        var rect = anchor.GetCharacterRect(LogicalDirection.Forward);
        if (rect.IsEmpty)
        {
            return Math.Max(0, _shownProgress);
        }
        var y = rect.Top + rect.Height / 2 + lines * _text.LineHeight;
        if (y < 0)
        {
            return 0;
        }
        // 不捲過最後一行：跳到結尾會被當成讀完
        y = Math.Min(y, _text.ActualHeight - _text.LineHeight / 2);
        if (_text.GetPositionFromPoint(new Point(0, y), snapToText: true) is not { } pointer || DisplayOffsetOf(pointer) is not { } offset)
        {
            return Math.Max(0, _shownProgress);
        }
        return _display.ToCharacterOffset(offset);
    }

    /// <summary>TextBlock 裡的位置 → 顯示文字的位置（三段 Run：讀過、黃色、還沒讀）。</summary>
    private int? DisplayOffsetOf(TextPointer pointer)
    {
        foreach (var (run, start) in new[] { (_read, 0), (_current, _currentStart), (_ahead, _currentEnd) })
        {
            if (pointer.CompareTo(run.ContentStart) >= 0 && pointer.CompareTo(run.ContentEnd) <= 0)
            {
                return start + run.ContentStart.GetOffsetToPosition(pointer);
            }
        }
        return null;
    }

    public void ShowDone()
    {
        _reading.Visibility = Visibility.Collapsed;
        _done.Visibility = Visibility.Visible;
    }

    /// <summary>高亮位置（講稿的文字元素位置）變了才重排文字、重算捲動。</summary>
    private void ShowProgress(int progress)
    {
        if (_display is null || _prompt is null || progress == _shownProgress)
        {
            return;
        }
        _shownProgress = progress;

        // 黃色那段：從目前要念的字起 2～4 個字，不跨標點與換行（ReadingHighlight）
        var text = _display.Text;
        var highlight = ReadingHighlight.Range(_prompt, _display, progress);
        var currentStart = _display.ToDisplayOffset(highlight.Start);
        var currentEnd = _display.ToDisplayOffset(highlight.End);
        _read.Text = text[..currentStart];
        _current.Text = text[currentStart..currentEnd];
        _ahead.Text = text[currentEnd..];
        _currentStart = currentStart;
        _currentEnd = currentEnd;

        ScrollToCurrent();
    }

    private void ScrollToCurrent()
    {
        var viewportHeight = _viewport.ActualHeight;
        if (viewportHeight <= 0)
        {
            return; // 還沒排版（剛開），下一次更新再捲
        }
        _text.UpdateLayout();
        var anchor = _current.Text.Length > 0 ? _current.ContentStart : _ahead.ContentStart;
        var rect = anchor.GetCharacterRect(LogicalDirection.Forward);
        if (rect.IsEmpty)
        {
            return;
        }

        // 計時器模式：照目前的字在這一行走了多遠，往下多捲一點，看起來是連續往上流（Mac 版的做法）
        var lineTop = rect.Top;
        if (_mode != FollowMode.WordTracking && _text.ActualWidth > 0)
        {
            lineTop += rect.Height * Math.Clamp(rect.Left / _text.ActualWidth, 0, 1);
        }
        var target = ReadingScroll.Target(lineTop, viewportHeight);
        if (Math.Abs(target - _target) < 0.5)
        {
            return;
        }
        var motion = ReadingScroll.Motion(target - _target, rect.Height, _mode);
        LastMotion = motion;
        _target = target;
        if (!AnimateScroll)
        {
            _shift.BeginAnimation(TranslateTransform.YProperty, null);
            _shift.Y = -target;
            return;
        }
        var animation = new DoubleAnimation(-target, TimeSpan.FromSeconds(motion.Seconds))
        {
            EasingFunction = motion.EaseOut ? new CubicEase { EasingMode = EasingMode.EaseOut } : null,
        };
        _shift.BeginAnimation(TranslateTransform.YProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>放不下時只留最後幾個字（最新講的在右邊），前面補「…」；照實際字寬算，中英文都剛好放滿。</summary>
    private void ShowHeard()
    {
        var room = HeardRoom();
        if (room <= 0 || TextWidth(_heardFull) <= room)
        {
            _heard.Text = _heardFull;
            return;
        }
        var elements = TextElements.Split(_heardFull);
        var start = elements.Length;
        while (start > 0 && TextWidth("…" + string.Concat(elements[(start - 1)..])) <= room)
        {
            start--;
        }
        _heard.Text = "…" + string.Concat(elements[start..]);
    }

    /// <summary>最近 30 次的音量畫成直條，讀過的比例那段是黃色。</summary>
    private void UpdateMeter(OverlayStatus status)
    {
        Array.Copy(_levels, 1, _levels, 0, MeterBars - 1);
        _levels[^1] = status.AudioLevel;
        var fraction = _prompt is null || _prompt.CharacterCount == 0 ? 0 : (double)status.Progress / _prompt.CharacterCount;
        var lit = (int)Math.Round(MeterBars * Math.Clamp(fraction, 0, 1));
        for (var i = 0; i < MeterBars; i++)
        {
            // 開口約 0.12（-18 dBFS）就接近滿格
            _bars[i].Height = Math.Max(3, Math.Min(1, _levels[i] * 7) * 22);
            _bars[i].Fill = i < lit ? CurrentBrush : MeterIdleBrush;
        }
    }

    private static TextBlock SmallText(HorizontalAlignment alignment) => new()
    {
        FontSize = 12,
        Foreground = MutedBrush,
        HorizontalAlignment = alignment,
        FontFamily = new FontFamily("Microsoft JhengHei UI"),
    };

    /// <summary>「讀完了」底下的膠囊鈕：淡白底、白字。</summary>
    private static Button PillButton(string text)
    {
        var pill = new FrameworkElementFactory(typeof(Border));
        pill.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
        pill.SetValue(Border.BackgroundProperty, Frozen(Color.FromArgb(0x26, Ink.R, Ink.G, Ink.B)));
        pill.SetValue(Border.PaddingProperty, new Thickness(16, 5, 16, 5));
        var label = new FrameworkElementFactory(typeof(ContentPresenter));
        label.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        pill.AppendChild(label);
        return new Button
        {
            Content = text,
            Foreground = AheadBrush,
            FontSize = 14,
            FontFamily = new FontFamily("Microsoft JhengHei UI"),
            Margin = new Thickness(6, 0, 6, 0),
            Cursor = Cursors.Hand,
            Focusable = false,
            Template = new ControlTemplate(typeof(Button)) { VisualTree = pill },
        };
    }

    /// <summary>✕：24×24 圓鈕，淡白底（Mac 版的樣子）。</summary>
    private static Button CloseButton()
    {
        var circle = new FrameworkElementFactory(typeof(Border));
        circle.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
        circle.SetValue(Border.BackgroundProperty, Frozen(Color.FromArgb(0x26, Ink.R, Ink.G, Ink.B)));
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetValue(TextBlock.TextProperty, "✕");
        label.SetValue(TextBlock.FontSizeProperty, 11.0);
        label.SetValue(TextBlock.ForegroundProperty, Frozen(Color.FromArgb(0x99, Ink.R, Ink.G, Ink.B)));
        label.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        label.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        circle.AppendChild(label);
        return new Button
        {
            Width = 24,
            Height = 24,
            HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = Cursors.Hand,
            ToolTip = "結束（Esc）",
            Focusable = false,
            Template = new ControlTemplate(typeof(Button)) { VisualTree = circle },
        };
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
