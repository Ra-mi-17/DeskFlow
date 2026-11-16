using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DeskFlow;

/// <summary>A message posted from a page through window.deskflow (see wwwroot/bridge.js).</summary>
public sealed record WebMessage(WebHost Source, string Channel, JsonElement Data, int? ReplyId, IReadOnlyList<string> FilePaths);

/// <summary>WebView2 control + JSON message bridge.</summary>
public sealed class WebHost
{
    private static CoreWebView2Environment? _env;
    private readonly string _origin;

    public WebView2 View { get; } = new();
    public event Action<WebMessage>? Message;

    public WebHost(string origin, System.Drawing.Color background)
    {
        _origin = origin;
        View.DefaultBackgroundColor = background;
    }

    public static async Task<CoreWebView2Environment> EnvironmentAsync()
    {
        if (_env != null) return _env;
        var args = "--autoplay-policy=no-user-gesture-required " +
                   "--disable-features=CalculateNativeWinOcclusion,HardwareMediaKeyHandling " +
                   "--disable-background-timer-throttling --disable-renderer-backgrounding " +
                   "--disable-backgrounding-occluded-windows";
        if (App.Debug) args += " --remote-debugging-port=9333";
        var opts = new CoreWebView2EnvironmentOptions(args) { Language = "ja" };
        _env = await CoreWebView2Environment.CreateAsync(null, App.WebViewDataDir, opts);
        return _env;
    }

    public async Task InitAsync(string url, bool contextMenus)
    {
        await View.EnsureCoreWebView2Async(await EnvironmentAsync());
        var core = View.CoreWebView2;
        var s = core.Settings;
        s.AreDefaultContextMenusEnabled = contextMenus;
        s.AreDevToolsEnabled = App.Debug;
        s.IsStatusBarEnabled = false;
        s.IsZoomControlEnabled = false;
        s.IsPinchZoomEnabled = false;
        s.IsSwipeNavigationEnabled = false;
        s.AreBrowserAcceleratorKeysEnabled = App.Debug;
        s.IsGeneralAutofillEnabled = false;
        s.IsPasswordAutosaveEnabled = false;

        core.WebMessageReceived += OnWebMessage;
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (e.Uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) OpenExternal(e.Uri);
        };
        core.NavigationStarting += (_, e) =>
        {
            // The top-level document must stay on our own pages (dropped files would otherwise navigate away).
            if (!e.Uri.StartsWith(_origin, StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
        };
        core.Navigate(url);
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            var ch = root.TryGetProperty("ch", out var c) ? c.GetString() ?? "" : "";
            var data = root.TryGetProperty("data", out var d) ? d.Clone() : default;
            int? id = root.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.Number ? i.GetInt32() : null;
            var files = new List<string>();
            if (e.AdditionalObjects != null)
            {
                foreach (var o in e.AdditionalObjects)
                    if (o is CoreWebView2File f && !string.IsNullOrEmpty(f.Path)) files.Add(f.Path);
            }
            Message?.Invoke(new WebMessage(this, ch, data, id, files));
        }
        catch (Exception ex)
        {
            Log.Write($"bad web message: {ex.Message}");
        }
    }

    public void Post(string channel, object? data)
    {
        if (View.CoreWebView2 == null) return;
        View.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { ch = channel, data }, SettingsStore.Json));
    }

    public void Reply(int id, object? data)
    {
        if (View.CoreWebView2 == null) return;
        View.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { reply = id, data }, SettingsStore.Json));
    }

    public static void OpenExternal(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { /* ignore */ }
    }
}
