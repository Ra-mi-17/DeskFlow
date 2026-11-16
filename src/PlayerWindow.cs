using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace DeskFlow;

/// <summary>The frameless video window (also used embedded in the desktop for wallpaper mode).</summary>
public sealed class PlayerWindow : Window
{
    private readonly Controller _c;
    private readonly DispatcherTimer _dragTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(8) };
    private Native.POINT _dragStart;
    private Native.RECT _dragRect;
    private string _dragEdge = "";
    private int _dragTicks;

    public bool IsWallpaper { get; }
    public bool IntentionalClose { get; set; }
    public WebHost Web { get; }
    public IntPtr Handle { get; private set; }

    public PlayerWindow(Controller c, bool wallpaper)
    {
        _c = c;
        IsWallpaper = wallpaper;
        Title = "DeskFlow";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Background = Brushes.Black;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000; Top = -32000; Width = 320; Height = 180;

        Web = new WebHost(c.Server.Origin, System.Drawing.Color.Black);
        Content = Web.View;
        _dragTimer.Tick += (_, _) => DragTick();

        Handle = new WindowInteropHelper(this).EnsureHandle();
        HwndSource.FromHwnd(Handle)!.AddHook(WndProc);
        // Hide from Alt+Tab
        Native.SetStyle(Handle, Native.GWL_EXSTYLE,
            (Native.GetStyle(Handle, Native.GWL_EXSTYLE) | Native.WS_EX_TOOLWINDOW) & ~Native.WS_EX_APPWINDOW);
    }

    public async Task InitAsync() => await Web.InitAsync(_c.Server.Url("player.html"), contextMenus: false);

    public void SetBoundsPx(PixelRect r) =>
        Native.SetWindowPos(Handle, IntPtr.Zero, r.X, r.Y, r.Width, r.Height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);

    public PixelRect GetBoundsPx()
    {
        Native.GetWindowRect(Handle, out var r);
        return new PixelRect { X = r.Left, Y = r.Top, Width = r.Width, Height = r.Height };
    }

    public double Scale => Math.Max(1, Native.GetDpiForWindow(Handle)) / 96.0;

    /// <summary>Apply z-order, opacity and click-through (window mode only).</summary>
    public void ApplyWindowProps(Settings s)
    {
        if (IsWallpaper) return;
        var ex = Native.GetStyle(Handle, Native.GWL_EXSTYLE);
        var needLayered = s.Opacity < 0.999 || s.ClickThrough;
        ex = needLayered ? ex | Native.WS_EX_LAYERED : ex & ~Native.WS_EX_LAYERED;
        ex = s.ClickThrough ? ex | Native.WS_EX_TRANSPARENT : ex & ~Native.WS_EX_TRANSPARENT;
        Native.SetStyle(Handle, Native.GWL_EXSTYLE, ex);
        if (needLayered) Native.SetLayeredWindowAttributes(Handle, 0, (byte)Math.Round(Math.Clamp(s.Opacity, 0.1, 1) * 255), Native.LWA_ALPHA);

        var after = s.ZOrder switch { "top" => Native.HWND_TOPMOST, "bottom" => Native.HWND_BOTTOM, _ => Native.HWND_NOTOPMOST };
        Native.SetWindowPos(Handle, after, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    public void PushToBottom()
    {
        if (!IsWallpaper && _c.S.ZOrder == "bottom") Native.PushToBottom(Handle);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Keep the window pinned to the bottom of the z-order (just above the desktop).
        if (msg == Native.WM_WINDOWPOSCHANGING && !IsWallpaper && _c.S.ZOrder == "bottom")
        {
            var wp = Marshal.PtrToStructure<Native.WINDOWPOS>(lParam);
            if ((wp.flags & Native.SWP_NOZORDER) == 0 && wp.hwndInsertAfter != Native.HWND_BOTTOM)
            {
                wp.hwndInsertAfter = Native.HWND_BOTTOM;
                Marshal.StructureToPtr(wp, lParam, false);
            }
        }
        return IntPtr.Zero;
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        PushToBottom();
    }

    // ------------------------------------------------------------------ move / resize driven by the page
    /// <summary>edge: "" = move, or any combination of l/r/t/b.</summary>
    public void BeginDrag(string edge)
    {
        if (IsWallpaper || _c.S.Locked) return;
        Native.GetCursorPos(out _dragStart);
        Native.GetWindowRect(Handle, out _dragRect);
        _dragEdge = edge ?? "";
        _dragTicks = 0;
        _dragTimer.Start();
    }

    public void EndDrag()
    {
        if (!_dragTimer.IsEnabled) return;
        _dragTimer.Stop();
        _c.SaveBounds();
        PushToBottom();
    }

    private void DragTick()
    {
        _dragTicks++;
        if (_dragTicks > 2 && !Native.IsLeftButtonDown()) { EndDrag(); return; }
        Native.GetCursorPos(out var p);
        int dx = p.X - _dragStart.X, dy = p.Y - _dragStart.Y;
        var r = _dragRect;
        int x = r.Left, y = r.Top, w = r.Width, h = r.Height;
        var work = Native.MonitorWorkArea(Native.MonitorFromPoint(p, 2));
        var snap = (int)(14 * Scale);

        if (_dragEdge.Length == 0)
        {
            x += dx; y += dy;
            if (Math.Abs(x - work.Left) < snap) x = work.Left;
            if (Math.Abs(y - work.Top) < snap) y = work.Top;
            if (Math.Abs(x + w - work.Right) < snap) x = work.Right - w;
            if (Math.Abs(y + h - work.Bottom) < snap) y = work.Bottom - h;
        }
        else
        {
            var (nx, ny, nw, nh) = ComputeResize(r, _dragEdge, dx, dy, (int)(160 * Scale), (int)(90 * Scale),
                _c.S.KeepAspect && !Native.IsShiftDown());
            x = nx; y = ny; w = nw; h = nh;
        }
        Native.SetWindowPos(Handle, IntPtr.Zero, x, y, w, h, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    /// <summary>
    /// New bounds when dragging <paramref name="edge"/> (l/r/t/b combination) by (dx, dy).
    /// With keepAspect the window keeps the aspect ratio it had when the drag started;
    /// the opposite edge / corner stays anchored.
    /// </summary>
    internal static (int X, int Y, int W, int H) ComputeResize(Native.RECT r, string edge, int dx, int dy, int minW, int minH, bool keepAspect)
    {
        bool L = edge.Contains('l'), R = edge.Contains('r'), T = edge.Contains('t'), B = edge.Contains('b');
        double w = r.Width, h = r.Height;
        if (L) w -= dx; if (R) w += dx;
        if (T) h -= dy; if (B) h += dy;

        if (keepAspect && r.Height > 0)
        {
            var a = (double)r.Width / r.Height;
            bool useWidth;
            if ((L || R) && (T || B)) useWidth = Math.Abs(w - r.Width) / r.Width >= Math.Abs(h - r.Height) / r.Height;
            else useWidth = L || R;
            if (useWidth) h = w / a; else w = h * a;
            // minimum size while keeping the ratio
            if (w < minW) { w = minW; h = w / a; }
            if (h < minH) { h = minH; w = h * a; }
        }
        else
        {
            w = Math.Max(minW, w);
            h = Math.Max(minH, h);
        }

        int iw = (int)Math.Round(w), ih = (int)Math.Round(h);
        int x = r.Left, y = r.Top;
        if (L) x = r.Right - iw;
        if (T) y = r.Bottom - ih;
        // Dragging only a side edge with a locked ratio: grow the other axis around its center.
        if (keepAspect && (L || R) && !(T || B)) y = r.Top + (r.Height - ih) / 2;
        if (keepAspect && (T || B) && !(L || R)) x = r.Left + (r.Width - iw) / 2;
        return (x, y, iw, ih);
    }
}
