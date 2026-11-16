using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace DeskFlow;

internal static class Native
{
    // ------------------------------------------------------------------ structs & constants
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    public static readonly IntPtr HWND_TOP = IntPtr.Zero;
    public static readonly IntPtr HWND_BOTTOM = new(1);
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new(-2);

    public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010,
        SWP_FRAMECHANGED = 0x0020, SWP_SHOWWINDOW = 0x0040, SWP_NOOWNERZORDER = 0x0200;

    public const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    public const long WS_CHILD = 0x40000000L, WS_POPUP = 0x80000000L;
    public const long WS_EX_LAYERED = 0x00080000L, WS_EX_TRANSPARENT = 0x00000020L, WS_EX_TOOLWINDOW = 0x00000080L,
        WS_EX_NOACTIVATE = 0x08000000L, WS_EX_APPWINDOW = 0x00040000L;
    public const uint LWA_ALPHA = 0x2;

    public const int WM_WINDOWPOSCHANGING = 0x0046, WM_HOTKEY = 0x0312;
    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;

    // ------------------------------------------------------------------ imports
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string? cls, string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp, uint flags, uint timeout, out IntPtr result);
    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lp);
    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int idx);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int idx, IntPtr val);
    [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern int MapWindowPoints(IntPtr from, IntPtr to, ref POINT pt, uint n);
    [DllImport("user32.dll")] public static extern bool RedrawWindow(IntPtr hwnd, IntPtr rc, IntPtr rgn, uint flags);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int idx);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("kernel32.dll")] public static extern IntPtr GetConsoleWindow();
    [DllImport("kernel32.dll")] public static extern bool FreeConsole();
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);

    public static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);

    // ------------------------------------------------------------------ helpers
    public static long GetStyle(IntPtr h, int idx) => GetWindowLongPtr(h, idx).ToInt64();
    public static void SetStyle(IntPtr h, int idx, long v) => SetWindowLongPtr(h, idx, new IntPtr(v));

    public static string ClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        return GetClassName(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }

    public static bool IsLeftButtonDown()
    {
        var vk = GetSystemMetrics(23 /* SM_SWAPBUTTON */) != 0 ? 0x02 : 0x01;
        return (GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    public static bool IsShiftDown() => (GetAsyncKeyState(0x10) & 0x8000) != 0;

    public static RECT MonitorWorkArea(IntPtr mon)
    {
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(mon, ref mi);
        return mi.rcWork;
    }

    public static RECT MonitorBounds(IntPtr mon)
    {
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(mon, ref mi);
        return mi.rcMonitor;
    }

    public static void UseDarkTitleBar(IntPtr hwnd)
    {
        var on = 1;
        DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref on, sizeof(int));
    }

    public static void PushToBottom(IntPtr hwnd) =>
        SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    private static readonly HashSet<string> ShellClasses = new() { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

    /// <summary>True when a foreground window (not ours) fully covers the monitor our player is on.</summary>
    public static bool IsForegroundFullscreen(ICollection<IntPtr> own, IntPtr player)
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || own.Contains(fg)) return false;
        if (ShellClasses.Contains(ClassName(fg))) return false;
        if (!GetWindowRect(fg, out var r)) return false;
        var fgMon = MonitorFromWindow(fg, 2);
        if (player != IntPtr.Zero && MonitorFromWindow(player, 2) != fgMon) return false;
        var m = MonitorBounds(fgMon);
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }
}

/// <summary>Embeds a window between the wallpaper and the desktop icons.</summary>
internal static class DesktopHost
{
    private sealed record Host(string Kind, IntPtr Parent, IntPtr ShellView, IntPtr WorkerW);

    private static Host? _host;
    private static IntPtr _attached;

    private static Host? Find()
    {
        var progman = Native.FindWindow("Progman", null);
        if (progman == IntPtr.Zero) return null;
        // Ask Explorer to create the WorkerW that sits behind the icons.
        Native.SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(0x1), 0, 1000, out _);
        Native.SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out _);

        // Windows 11 24H2+: SHELLDLL_DefView and WorkerW are both children of Progman.
        var shell = Native.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        var worker = Native.FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
        if (shell != IntPtr.Zero && worker != IntPtr.Zero) return new Host("raised", progman, shell, worker);

        // Legacy: icons live in a top-level WorkerW; the wallpaper WorkerW is its next sibling.
        var legacy = IntPtr.Zero;
        Native.EnumWindows((h, _) =>
        {
            if (Native.FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                legacy = Native.FindWindowEx(IntPtr.Zero, h, "WorkerW", null);
            return true;
        }, IntPtr.Zero);
        if (legacy != IntPtr.Zero) return new Host("legacy", legacy, IntPtr.Zero, legacy);
        return new Host("progman", progman, IntPtr.Zero, IntPtr.Zero);
    }

    private static void Position(IntPtr hwnd, IntPtr parent, PixelRect r)
    {
        var pt = new Native.POINT { X = r.X, Y = r.Y };
        Native.MapWindowPoints(IntPtr.Zero, parent, ref pt, 1);
        Native.SetWindowPos(hwnd, IntPtr.Zero, pt.X, pt.Y, r.Width, r.Height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    public static string Attach(IntPtr hwnd, PixelRect rect)
    {
        var host = Find() ?? throw new InvalidOperationException("デスクトップ (Progman) が見つかりません");
        var style = Native.GetStyle(hwnd, Native.GWL_STYLE);
        Native.SetStyle(hwnd, Native.GWL_STYLE, (style & ~Native.WS_POPUP) | Native.WS_CHILD);
        Native.SetParent(hwnd, host.Parent);
        Native.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
        Position(hwnd, host.Parent, rect);
        if (host.Kind == "raised")
        {
            // icons (SHELLDLL_DefView) > our window > wallpaper (WorkerW)
            Native.SetWindowPos(hwnd, host.ShellView, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            Native.SetWindowPos(host.WorkerW, hwnd, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }
        _host = host;
        _attached = hwnd;
        return host.Kind;
    }

    public static void Reposition(IntPtr hwnd, PixelRect rect)
    {
        if (_host != null && _attached == hwnd) Position(hwnd, _host.Parent, rect);
    }

    public static bool IsAttached(IntPtr hwnd) =>
        _host != null && _attached == hwnd && Native.IsWindow(_host.Parent) && Native.GetParent(hwnd) == _host.Parent;

    /// <summary>Repaint the wallpaper after our window has gone.</summary>
    public static void Cleanup()
    {
        if (_host == null) return;
        var target = _host.WorkerW != IntPtr.Zero ? _host.WorkerW : _host.Parent;
        _host = null;
        _attached = IntPtr.Zero;
        // RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_UPDATENOW | RDW_FRAME
        Native.RedrawWindow(target, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0004 | 0x0080 | 0x0100 | 0x0400);
    }
}
