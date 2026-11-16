using System;
using System.Collections.Generic;
using System.Windows.Interop;

namespace DeskFlow;

/// <summary>System-wide hotkeys through RegisterHotKey on a message-only window.</summary>
public sealed class Hotkeys : IDisposable
{
    public sealed record Binding(string Label, string Display, uint Mods, uint Vk, Action Action);

    private readonly HwndSource _src;
    private readonly Dictionary<int, Binding> _registered = new();

    public Hotkeys()
    {
        var p = new HwndSourceParameters("DeskFlowHotkeys") { ParentWindow = new IntPtr(-3) /* HWND_MESSAGE */, Width = 0, Height = 0 };
        _src = new HwndSource(p);
        _src.AddHook(WndProc);
    }

    public List<string> Register(IEnumerable<Binding> bindings)
    {
        Clear();
        var failed = new List<string>();
        var id = 1;
        foreach (var b in bindings)
        {
            if (Native.RegisterHotKey(_src.Handle, id, b.Mods | Native.MOD_NOREPEAT, b.Vk)) _registered[id] = b;
            else failed.Add(b.Display);
            id++;
        }
        return failed;
    }

    public void Clear()
    {
        foreach (var id in _registered.Keys) Native.UnregisterHotKey(_src.Handle, id);
        _registered.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && _registered.TryGetValue(wParam.ToInt32(), out var b))
        {
            handled = true;
            b.Action();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Clear();
        _src.Dispose();
    }
}
