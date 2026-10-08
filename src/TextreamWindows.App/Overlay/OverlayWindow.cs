using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TextreamWindows.App.Services;
using TextreamWindows.Core.Overlay;
using TextreamWindows.Core.Session;
using TextreamWindows.Core.Text;

namespace TextreamWindows.App.Overlay;

/// <summary>
/// 浮層視窗：無邊框、永遠在最上層、不出現在工作列，開啟時淡入。
/// 頂端膠囊：不搶焦點（錄影軟體照常操作），拖左右邊框變寬（保持置中）、拖下緣或下面兩角變高（瓦基 2026-10-08 要求）。
/// 浮動視窗：拖中間移動，四邊四角都能拉大小。全螢幕：不能拉。
/// ✕ 鍵、Esc、Alt+F4 都算瓦基要停，ViewModel 下一次 Tick 會停止跟讀。
/// </summary>
public sealed class OverlayWindow : Window, IOverlay
{
    /// <summary>邊框可以抓的寬度。</summary>
    private const double Grip = 8;

    private const uint WdaMonitor = 0x1;
    private const uint WdaExcludeFromCapture = 0x11;

    private readonly OverlayView _view;
    private readonly OverlayStyle _style;
    private bool _closingFromCode;
    private int? _jumpRequest;
    private Point? _dragFrom;

    public OverlayWindow(OverlayStyle style, ScreenRect bounds, PromptScript prompt, FollowMode mode, double fontSize)
    {
        _style = style;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true; // 圓角外面要透明
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = style != OverlayStyle.Capsule;
        ResizeMode = ResizeMode.NoResize; // 自己做拉大小的邊框，系統的只有右下角一個點
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = bounds.X;
        Top = bounds.Y;
        Width = bounds.Width;
        Height = bounds.Height;
        Title = "Textream for Windows 浮層";

        _view = new OverlayView(style, fontSize);
        _view.Load(prompt, mode);
        _view.CloseRequested += () => WasClosedByUser = true;
        _view.RestartRequested += () => RestartRequested = true;
        _view.JumpRequested += offset => _jumpRequest = offset;
        var root = new Grid { Children = { _view } };
        AddResizeGrips(root);
        Content = root;

        if (style == OverlayStyle.Floating)
        {
            // 拖中間移動視窗；只點一下（移動不到 ClickSlop）是點字跳轉（第 5.4 步），交給 OverlayView
            _view.PreviewMouseLeftButtonDown += (_, e) => _dragFrom = e.GetPosition(this);
            _view.PreviewMouseLeftButtonUp += (_, _) => _dragFrom = null;
            _view.PreviewMouseMove += (_, e) =>
            {
                if (_dragFrom is { } from && e.LeftButton == MouseButtonState.Pressed && (e.GetPosition(this) - from).Length >= OverlayView.ClickSlop)
                {
                    _dragFrom = null;
                    DragMove();
                }
            };
        }
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                WasClosedByUser = true;
            }
        };

        SourceInitialized += (_, _) => ApplyCaptureExclusion();

        Opacity = 0;
        Loaded += (_, _) => BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromSeconds(0.25)));
    }

    public bool WasClosedByUser { get; private set; }

    public bool RestartRequested { get; private set; }

    /// <summary>錄影、分享畫面時看不到浮層（計畫書第 5 階段步驟 5）。要在視窗顯示前設定。</summary>
    public bool HideFromCapture { get; init; }

    public ScreenRect Bounds => new(Left, Top, ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height);

    public void Update(OverlayStatus status) => _view.Update(status);

    public int? TakeJumpRequest()
    {
        var jump = _jumpRequest;
        _jumpRequest = null;
        return jump;
    }

    /// <summary>讀完了：停在「讀完了」畫面等瓦基選；把浮層叫到前面拿焦點，按 Esc 才有作用（頂端膠囊平常不搶焦點）。</summary>
    public void ShowDone()
    {
        _view.ShowDone();
        Activate();
    }

    void IOverlay.Close()
    {
        _closingFromCode = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_closingFromCode)
        {
            WasClosedByUser = true; // Alt+F4 之類：當成瓦基要停
        }
        base.OnClosing(e);
    }

    /// <summary>
    /// WDA_EXCLUDEFROMCAPTURE（Windows 10 2004 起）：錄影、截圖、分享畫面裡完全沒有這個視窗，自己的螢幕照常看得到。
    /// 舊版 Windows 不支援時退回 WDA_MONITOR（錄影裡變成一塊黑）。
    /// </summary>
    private void ApplyCaptureExclusion()
    {
        if (!HideFromCapture)
        {
            return;
        }
        var hwnd = new WindowInteropHelper(this).Handle;
        if (!SetWindowDisplayAffinity(hwnd, WdaExcludeFromCapture))
        {
            SetWindowDisplayAffinity(hwnd, WdaMonitor);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    private void AddResizeGrips(Grid root)
    {
        switch (_style)
        {
            case OverlayStyle.Capsule:
                Add(root, HorizontalAlignment.Left, VerticalAlignment.Stretch, Cursors.SizeWE, (dx, _) => ResizeCapsule(-dx, 0));
                Add(root, HorizontalAlignment.Right, VerticalAlignment.Stretch, Cursors.SizeWE, (dx, _) => ResizeCapsule(dx, 0));
                Add(root, HorizontalAlignment.Stretch, VerticalAlignment.Bottom, Cursors.SizeNS, (_, dy) => ResizeCapsule(0, dy));
                Add(root, HorizontalAlignment.Left, VerticalAlignment.Bottom, Cursors.SizeNESW, (dx, dy) => ResizeCapsule(-dx, dy));
                Add(root, HorizontalAlignment.Right, VerticalAlignment.Bottom, Cursors.SizeNWSE, (dx, dy) => ResizeCapsule(dx, dy));
                break;
            case OverlayStyle.Floating:
                Add(root, HorizontalAlignment.Left, VerticalAlignment.Stretch, Cursors.SizeWE, (dx, _) => ResizeFloating(dx, 0, 0, 0));
                Add(root, HorizontalAlignment.Right, VerticalAlignment.Stretch, Cursors.SizeWE, (dx, _) => ResizeFloating(0, 0, dx, 0));
                Add(root, HorizontalAlignment.Stretch, VerticalAlignment.Top, Cursors.SizeNS, (_, dy) => ResizeFloating(0, dy, 0, 0));
                Add(root, HorizontalAlignment.Stretch, VerticalAlignment.Bottom, Cursors.SizeNS, (_, dy) => ResizeFloating(0, 0, 0, dy));
                Add(root, HorizontalAlignment.Left, VerticalAlignment.Top, Cursors.SizeNWSE, (dx, dy) => ResizeFloating(dx, dy, 0, 0));
                Add(root, HorizontalAlignment.Right, VerticalAlignment.Top, Cursors.SizeNESW, (dx, dy) => ResizeFloating(0, dy, dx, 0));
                Add(root, HorizontalAlignment.Left, VerticalAlignment.Bottom, Cursors.SizeNESW, (dx, dy) => ResizeFloating(dx, 0, 0, dy));
                Add(root, HorizontalAlignment.Right, VerticalAlignment.Bottom, Cursors.SizeNWSE, (dx, dy) => ResizeFloating(0, 0, dx, dy));
                break;
        }
    }

    /// <summary>一條透明但抓得到的邊框：邊用 Grip 寬的長條，角用 Grip×2 的方塊（疊在邊上面）。</summary>
    private static void Add(Grid root, HorizontalAlignment h, VerticalAlignment v, Cursor cursor, Action<double, double> onDrag)
    {
        var corner = h != HorizontalAlignment.Stretch && v != VerticalAlignment.Stretch;
        var surface = new FrameworkElementFactory(typeof(Border));
        surface.SetValue(Border.BackgroundProperty, Brushes.Transparent); // 透明但不是 null，滑鼠才點得到
        var thumb = new Thumb
        {
            HorizontalAlignment = h,
            VerticalAlignment = v,
            Width = h == HorizontalAlignment.Stretch ? double.NaN : (corner ? Grip * 2 : Grip),
            Height = v == VerticalAlignment.Stretch ? double.NaN : (corner ? Grip * 2 : Grip),
            Cursor = cursor,
            Template = new ControlTemplate(typeof(Thumb)) { VisualTree = surface },
        };
        thumb.DragDelta += (_, e) => onDrag(e.HorizontalChange, e.VerticalChange);
        if (corner)
        {
            Panel.SetZIndex(thumb, 1);
        }
        root.Children.Add(thumb);
    }

    /// <summary>膠囊：寬度兩邊一起變（中心不動，維持在鏡頭正下方），高度往下長；範圍跟 OverlayLayout 一樣。</summary>
    private void ResizeCapsule(double widthChangePerSide, double heightChange)
    {
        var center = Left + ActualWidth / 2;
        var width = Math.Clamp(ActualWidth + 2 * widthChangePerSide, OverlayLayout.CapsuleMinWidth, OverlayLayout.CapsuleMaxWidth);
        var height = Math.Clamp(ActualHeight + heightChange, OverlayLayout.CapsuleMinHeight, OverlayLayout.CapsuleMaxHeight);
        Width = width;
        Height = height;
        Left = center - width / 2;
    }

    /// <summary>浮動視窗：哪一邊被拉就動哪一邊，最小 200×80。</summary>
    private void ResizeFloating(double left, double top, double right, double bottom)
    {
        const double MinimumWidth = 200, MinimumHeight = 80;
        var newLeft = Left + left;
        var newTop = Top + top;
        var width = ActualWidth - left + right;
        var height = ActualHeight - top + bottom;
        if (width < MinimumWidth)
        {
            if (left != 0)
            {
                newLeft -= MinimumWidth - width;
            }
            width = MinimumWidth;
        }
        if (height < MinimumHeight)
        {
            if (top != 0)
            {
                newTop -= MinimumHeight - height;
            }
            height = MinimumHeight;
        }
        Left = newLeft;
        Top = newTop;
        Width = width;
        Height = height;
    }
}
