using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace DeskFlow;

/// <summary>Application state, playback logic, menus and window management.</summary>
public sealed class Controller
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly SettingsStore _store;
    private readonly Random _rng = new();
    private readonly List<int> _history = new();
    private readonly Dictionary<string, bool> _autoPause = new() { ["fullscreen"] = false, ["battery"] = false, ["lock"] = false };
    private readonly HashSet<string> _suppressed = new();
    private readonly Dictionary<string, (string Url, DateTime At)> _resolveCache = new();
    private readonly DispatcherTimer _watcher = new() { Interval = TimeSpan.FromSeconds(2) };
    private int _playToken;
    private bool _quitting;

    private AuxWindow? _playlistWin;
    private AuxWindow? _urlWin;
    private WinForms.NotifyIcon? _tray;
    private Hotkeys? _hotkeys;

    public Settings S => _store.Data;
    public LocalServer Server { get; }
    public PlayerWindow? Player { get; private set; }
    public double LastAspect { get; private set; } = 16.0 / 9;

    public Controller(SettingsStore store, LocalServer server)
    {
        _store = store;
        Server = server;
    }

    // ================================================================== startup / shutdown
    public void Start(string[] args)
    {
        Normalize();
        SyncMedia();
        CreateTray();
        _ = CreatePlayerAsync();
        _hotkeys = new Hotkeys();
        RegisterHotkeys();
        SetupSystemEvents();
        ApplyAutoStart();

        _watcher.Tick += (_, _) => Watch();
        _watcher.Start();

        HandleArgs(args);
        foreach (var it in S.Playlist.Where(i => !i.Enriched && i.Type is "youtube" or "vimeo").ToList()) _ = EnrichAsync(it);
    }

    private void Normalize()
    {
        S.Playlist.RemoveAll(i => i == null || string.IsNullOrEmpty(i.Id) || string.IsNullOrEmpty(i.Type) || string.IsNullOrEmpty(i.Src));
        if (S.CurrentIndex < 0 || S.CurrentIndex >= S.Playlist.Count) S.CurrentIndex = 0;
    }

    public void Quit()
    {
        if (_quitting) return;
        _quitting = true;
        _watcher.Stop();
        _store.Flush();
        _hotkeys?.Dispose();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        ClosePlayer();
        _playlistWin?.Close();
        _urlWin?.Close();
        Application.Current.Shutdown();
    }

    // ================================================================== state broadcast
    private PlaylistItem? Current => S.CurrentIndex >= 0 && S.CurrentIndex < S.Playlist.Count ? S.Playlist[S.CurrentIndex] : null;
    private bool EffectivePaused => S.Paused || _autoPause.Values.Any(v => v);

    private object ViewState()
    {
        var cur = Current;
        return new
        {
            playlist = S.Playlist.Select(it => new
            {
                it.Id, it.Type, it.Src, it.Title, it.Enabled, it.Start, it.Hash, it.Aspect, it.Thumb, it.Author,
                mediaUrl = it.Type == "file" ? Server.MediaUrl(it.Id) : null,
            }),
            currentIndex = S.CurrentIndex,
            currentId = cur?.Id,
            playToken = _playToken,
            volume = S.Volume,
            muted = S.Muted,
            paused = S.Paused,
            effectivePaused = EffectivePaused,
            autoPaused = _autoPause,
            repeat = S.Repeat,
            shuffle = S.Shuffle,
            rate = S.Rate,
            fit = S.Fit,
            brightness = S.Brightness,
            opacity = S.Opacity,
            mode = Player?.IsWallpaper == true ? "wallpaper" : "window",
            locked = S.Locked,
            showControls = S.ShowControls,
            captions = S.Captions,
        };
    }

    private void Broadcast()
    {
        var state = ViewState();
        Player?.Web.Post("state", state);
        _playlistWin?.Web.Post("state", state);
        UpdateTray();
    }

    private void Toast(string msg)
    {
        Player?.Web.Post("toast", msg);
        _playlistWin?.Web.Post("toast", msg);
    }

    private void SyncMedia() =>
        Server.SetMedia(S.Playlist.Where(i => i.Type == "file").ToDictionary(i => i.Id, i => i.Src));

    private void Changed(bool reload = false)
    {
        if (reload) _playToken++;
        SyncMedia();
        _store.Save();
        Broadcast();
    }

    // ================================================================== playback control
    private List<int> EnabledIndices() =>
        S.Playlist.Select((it, i) => it.Enabled ? i : -1).Where(i => i >= 0).ToList();

    private void Go(int index)
    {
        if (index < 0 || index >= S.Playlist.Count) return;
        S.CurrentIndex = index;
        S.Paused = false;
        Changed(reload: true);
    }

    public void Next()
    {
        var en = EnabledIndices();
        if (en.Count == 0) return;
        if (S.Shuffle && en.Count > 1)
        {
            var pool = en.Where(i => i != S.CurrentIndex).ToList();
            _history.Add(S.CurrentIndex);
            if (_history.Count > 200) _history.RemoveAt(0);
            Go(pool[_rng.Next(pool.Count)]);
            return;
        }
        var after = en.Where(i => i > S.CurrentIndex).DefaultIfEmpty(-1).First();
        Go(after >= 0 ? after : en[0]);
    }

    public void Prev()
    {
        var en = EnabledIndices();
        if (en.Count == 0) return;
        if (S.Shuffle && _history.Count > 0)
        {
            var h = _history[^1];
            _history.RemoveAt(_history.Count - 1);
            Go(h);
            return;
        }
        var before = en.Where(i => i < S.CurrentIndex).DefaultIfEmpty(-1).Last();
        Go(before >= 0 ? before : en[^1]);
    }

    public void TogglePause()
    {
        if (Current == null) return;
        if (EffectivePaused)
        {
            S.Paused = false;
            foreach (var k in _autoPause.Keys.ToList())
            {
                if (_autoPause[k]) _suppressed.Add(k); // user overrides until the condition clears
                _autoPause[k] = false;
            }
        }
        else S.Paused = true;
        Changed();
    }

    private void SetAutoPause(string key, bool condition)
    {
        if (!condition) _suppressed.Remove(key);
        var v = condition && !_suppressed.Contains(key);
        if (_autoPause[key] == v) return;
        _autoPause[key] = v;
        Broadcast();
    }

    public void SetOpt(string key, JsonElement value)
    {
        switch (key)
        {
            case "volume": SetVolume(value.GetDouble()); return;
            case "muted": S.Muted = value.GetBoolean(); break;
            case "repeat": S.Repeat = value.GetString() == "one" ? "one" : "all"; break;
            case "shuffle": S.Shuffle = value.GetBoolean(); break;
            case "rate": S.Rate = Math.Clamp(value.GetDouble(), 0.25, 4); break;
            case "fit": S.Fit = value.GetString() is "contain" or "fill" ? value.GetString()! : "cover"; break;
            case "showControls": S.ShowControls = value.GetBoolean(); break;
            case "captions": S.Captions = value.GetBoolean(); break;
            default: return;
        }
        Changed();
    }

    private void SetVolume(double v)
    {
        S.Volume = (int)Math.Clamp(Math.Round(v), 0, 100);
        if (S.Muted && S.Volume > 0) S.Muted = false;
        Changed();
    }

    private void Set(Action apply, bool windowProps = false)
    {
        apply();
        if (windowProps) Player?.ApplyWindowProps(S);
        Changed();
    }

    // ================================================================== playlist management
    private void AddItems(List<PlaylistItem> items, bool playNow = false)
    {
        if (items.Count == 0) { Toast("追加できる動画が見つかりませんでした"); return; }
        var wasEmpty = S.Playlist.Count == 0;
        var first = S.Playlist.Count;
        foreach (var it in items)
        {
            it.Id = Guid.NewGuid().ToString("N");
            it.Enabled = true;
            it.AddedAt = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            S.Playlist.Add(it);
        }
        Toast(items.Count == 1 ? $"追加: {Trunc(items[0].Title, 40)}" : $"{items.Count} 件追加しました");
        if (wasEmpty || playNow) Go(first); else Changed();
        foreach (var it in items) _ = EnrichAsync(it);
    }

    private static List<string> CollectFiles(IEnumerable<string> paths, int depth = 0)
    {
        var result = new List<string>();
        foreach (var p in paths)
        {
            try
            {
                if (Directory.Exists(p))
                {
                    if (depth >= 4) continue;
                    var entries = Directory.GetFileSystemEntries(p).OrderBy(e => e, new NaturalComparer());
                    result.AddRange(CollectFiles(entries, depth + 1));
                }
                else if (File.Exists(p) && MediaParser.IsVideoFile(p)) result.Add(p);
            }
            catch { /* skip */ }
        }
        return result;
    }

    public void AddFiles(IEnumerable<string> paths, bool playNow) =>
        AddItems(CollectFiles(paths).Select(MediaParser.FileItem).ToList(), playNow);

    public void AddUrls(string text, bool playNow)
    {
        var items = new List<PlaylistItem>();
        string? bad = null;
        foreach (var e in MediaParser.Split(text))
        {
            var it = MediaParser.Parse(e);
            if (it != null) items.Add(it); else bad ??= e;
        }
        if (items.Count == 0 && bad != null) { Toast($"認識できない入力です: {Trunc(bad, 50)}"); return; }
        AddItems(items, playNow);
    }

    private void RemoveItem(string id)
    {
        var idx = S.Playlist.FindIndex(i => i.Id == id);
        if (idx < 0) return;
        var wasCurrent = idx == S.CurrentIndex;
        S.Playlist.RemoveAt(idx);
        if (idx < S.CurrentIndex) S.CurrentIndex--;
        if (S.CurrentIndex >= S.Playlist.Count) S.CurrentIndex = 0;
        Changed(reload: wasCurrent);
    }

    private void MoveItem(int from, int to)
    {
        var n = S.Playlist.Count;
        if (from < 0 || from >= n) return;
        to = Math.Clamp(to, 0, n - 1);
        if (from == to) return;
        var curId = Current?.Id;
        var it = S.Playlist[from];
        S.Playlist.RemoveAt(from);
        S.Playlist.Insert(to, it);
        S.CurrentIndex = Math.Max(0, S.Playlist.FindIndex(i => i.Id == curId));
        Changed();
    }

    private void ClearPlaylist()
    {
        if (S.Playlist.Count == 0) return;
        if (MessageBox.Show("プレイリストをすべて削除しますか？", "DeskFlow", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        S.Playlist.Clear();
        S.CurrentIndex = 0;
        Changed(reload: true);
    }

    private static readonly string VideoFilter =
        "動画ファイル|" + string.Join(";", MediaParser.VideoExts.Select(e => "*." + e)) + "|すべてのファイル|*.*";

    public void PickFiles(bool playNow)
    {
        var dlg = new OpenFileDialog { Title = "動画ファイルを選択", Multiselect = true, Filter = VideoFilter };
        if (dlg.ShowDialog() == true) AddFiles(dlg.FileNames, playNow);
    }

    public void PickFolder(bool playNow)
    {
        var dlg = new OpenFolderDialog { Title = "動画フォルダを選択", Multiselect = true };
        if (dlg.ShowDialog() == true) AddFiles(dlg.FolderNames, playNow);
    }

    private void ExportPlaylist()
    {
        var dlg = new SaveFileDialog { Title = "プレイリストを書き出し", FileName = "DeskFlow-playlist.json", Filter = "DeskFlow プレイリスト|*.json" };
        if (dlg.ShowDialog() != true) return;
        var items = S.Playlist.Select(i => { var c = i.Clone(); c.Id = ""; return c; }).ToList();
        File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(new { app = "DeskFlow", version = 1, items }, SettingsStore.Json));
        Toast("プレイリストを書き出しました");
    }

    private void ImportPlaylist()
    {
        var dlg = new OpenFileDialog { Title = "プレイリストを読み込み", Filter = "プレイリスト|*.json;*.m3u;*.m3u8;*.txt" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var text = File.ReadAllText(dlg.FileName);
            if (dlg.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                using var doc = JsonDocument.Parse(text);
                var arr = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement : doc.RootElement.GetProperty("items");
                var items = arr.Deserialize<List<PlaylistItem>>(SettingsStore.Json) ?? new();
                AddItems(items.Where(i => !string.IsNullOrEmpty(i.Type) && !string.IsNullOrEmpty(i.Src)).ToList());
            }
            else
            {
                var dir = Path.GetDirectoryName(dlg.FileName)!;
                var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'))
                    .Select(l => Path.IsPathRooted(l) || l.Contains("://") ? l : Path.GetFullPath(Path.Combine(dir, l)));
                AddUrls(string.Join("\n", lines), false);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"読み込みに失敗しました: {ex.Message}", "DeskFlow", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Fetch title / thumbnail for online items via oEmbed.</summary>
    private async Task EnrichAsync(PlaylistItem item)
    {
        if (item.CustomTitle) return;
        string? endpoint = item.Type switch
        {
            "youtube" => "https://www.youtube.com/oembed?format=json&url=" + Uri.EscapeDataString($"https://www.youtube.com/watch?v={item.Src}"),
            "vimeo" => "https://vimeo.com/api/oembed.json?url=" + Uri.EscapeDataString(item.Hash != null ? $"https://vimeo.com/{item.Src}/{item.Hash}" : $"https://vimeo.com/{item.Src}"),
            _ => null,
        };
        if (endpoint == null) return;
        try
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(endpoint));
            var j = doc.RootElement;
            var live = S.Playlist.FirstOrDefault(i => i.Id == item.Id);
            if (live == null || live.CustomTitle) return;
            if (j.TryGetProperty("title", out var t)) live.Title = t.GetString() ?? live.Title;
            if (j.TryGetProperty("author_name", out var a)) live.Author = a.GetString();
            if (item.Type == "vimeo")
            {
                if (j.TryGetProperty("thumbnail_url", out var th)) live.Thumb = th.GetString();
                if (j.TryGetProperty("width", out var w) && j.TryGetProperty("height", out var h) && h.GetDouble() > 0)
                    live.Aspect = w.GetDouble() / h.GetDouble();
            }
            live.Enriched = true;
            Changed();
        }
        catch { /* offline, private video, … */ }
    }

    // ================================================================== yt-dlp fallback
    private async Task<object> ResolveAsync(string id)
    {
        var it = S.Playlist.FirstOrDefault(i => i.Id == id);
        if (it == null) return new { error = "項目が見つかりません" };
        var url = it.Type == "youtube" ? $"https://www.youtube.com/watch?v={it.Src}" : it.Src;
        if (_resolveCache.TryGetValue(url, out var c) && DateTime.Now - c.At < TimeSpan.FromHours(1)) return new { url = c.Url };

        var candidates = new[]
        {
            Path.Combine(App.UserDataDir, "yt-dlp.exe"),
            Path.Combine(AppContext.BaseDirectory, "yt-dlp.exe"),
            "yt-dlp",
        };
        foreach (var exe in candidates)
        {
            if (Path.IsPathRooted(exe) && !File.Exists(exe)) continue;
            try
            {
                var psi = new ProcessStartInfo(exe)
                {
                    RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
                };
                foreach (var a in new[] { "-f", "best[vcodec!=none][acodec!=none][ext=mp4]/best[vcodec!=none][acodec!=none]/best", "-g", "--no-playlist", "--no-warnings", url })
                    psi.ArgumentList.Add(a);
                using var p = Process.Start(psi)!;
                var stdout = p.StandardOutput.ReadToEndAsync();
                var stderr = p.StandardError.ReadToEndAsync();
                var done = await Task.WhenAny(p.WaitForExitAsync(), Task.Delay(60000));
                if (!p.HasExited) { try { p.Kill(true); } catch { } return new { error = "yt-dlp がタイムアウトしました" }; }
                var line = (await stdout).Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("http"));
                if (p.ExitCode == 0 && line != null)
                {
                    _resolveCache[url] = (line, DateTime.Now);
                    return new { url = line };
                }
                var err = (await stderr).Trim().Split('\n').LastOrDefault()?.Trim();
                return new { error = $"yt-dlp: {err ?? "URL を取得できませんでした"}" };
            }
            catch (System.ComponentModel.Win32Exception) { /* not found / blocked → try next */ }
        }
        return new { error = "このURLの再生には yt-dlp が必要です（yt-dlp.exe を PATH か設定フォルダに置いてください）" };
    }

    // ================================================================== player window
    private List<WinForms.Screen> WallpaperScreens()
    {
        var all = WinForms.Screen.AllScreens.ToList();
        if (S.WallpaperDisplay == "all") return all;
        var s = all.FirstOrDefault(x => x.DeviceName == S.WallpaperDisplay);
        return new List<WinForms.Screen> { s ?? WinForms.Screen.PrimaryScreen ?? all[0] };
    }

    private PixelRect WallpaperRect()
    {
        var rects = WallpaperScreens().Select(s => s.Bounds).ToList();
        int x = rects.Min(r => r.Left), y = rects.Min(r => r.Top);
        return new PixelRect { X = x, Y = y, Width = rects.Max(r => r.Right) - x, Height = rects.Max(r => r.Bottom) - y };
    }

    private PixelRect WindowRect()
    {
        var b = S.Bounds;
        if (b is { Width: >= 120, Height: >= 68 })
        {
            var visible = WinForms.Screen.AllScreens.Any(s =>
            {
                var wa = s.WorkingArea;
                return b.X < wa.Right - 40 && b.X + b.Width > wa.Left + 40 && b.Y < wa.Bottom - 40 && b.Y + b.Height > wa.Top;
            });
            if (visible) return b;
        }
        var scr = WinForms.Screen.PrimaryScreen!;
        var scale = scr.Bounds.Width >= 2560 ? 1.5 : 1.0;
        int w = (int)(640 * scale), h = (int)(360 * scale);
        return new PixelRect { X = scr.WorkingArea.Right - w - 40, Y = scr.WorkingArea.Top + 40, Width = w, Height = h };
    }

    private async Task CreatePlayerAsync()
    {
        var wall = S.Mode == "wallpaper";
        var w = new PlayerWindow(this, wall);
        Player = w;
        w.Web.Message += OnMessage;
        w.Closed += (_, _) =>
        {
            if (Player == w) Player = null;
            if (_quitting || w.IntentionalClose) return;
            // Explorer restarts destroy the embedded window: bring it back.
            Log.Write("player window lost; recreating");
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            t.Tick += (_, _) => { t.Stop(); if (Player == null && !_quitting) _ = CreatePlayerAsync(); };
            t.Start();
        };
        w.Closing += (_, e) => { if (!_quitting && !w.IntentionalClose) e.Cancel = true; };

        try
        {
            if (wall)
            {
                var kind = DesktopHost.Attach(w.Handle, WallpaperRect());
                Log.Write($"attached to desktop ({kind})");
            }
            else
            {
                w.SetBoundsPx(WindowRect());
            }
        }
        catch (Exception ex)
        {
            Log.Write($"wallpaper attach failed: {ex}");
            MessageBox.Show($"壁紙モードに切り替えられませんでした: {ex.Message}", "DeskFlow", MessageBoxButton.OK, MessageBoxImage.Warning);
            S.Mode = "window";
            _store.Save();
            w.SetBoundsPx(WindowRect());
            wall = false;
        }

        w.Show();
        if (!w.IsWallpaper) w.ApplyWindowProps(S);
        try { await w.InitAsync(); }
        catch (Exception ex)
        {
            Log.Write($"WebView2 init failed: {ex}");
            MessageBox.Show("WebView2 ランタイムを初期化できませんでした。\n" + ex.Message, "DeskFlow", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClosePlayer()
    {
        var old = Player;
        Player = null;
        if (old == null) return;
        old.IntentionalClose = true;
        var wasWall = old.IsWallpaper;
        old.Close();
        if (wasWall) DesktopHost.Cleanup();
    }

    private void RecreatePlayer()
    {
        ClosePlayer();
        _ = CreatePlayerAsync();
    }

    public void SaveBounds()
    {
        if (Player == null || Player.IsWallpaper) return;
        S.Bounds = Player.GetBoundsPx();
        _store.Save();
    }

    private void ResizePlayer(int dipW, int dipH)
    {
        if (Player == null || Player.IsWallpaper) return;
        var b = Player.GetBoundsPx();
        var scale = Player.Scale;
        var mon = Native.MonitorFromWindow(Player.Handle, 2);
        var wa = Native.MonitorWorkArea(mon);
        int w = Math.Min((int)(dipW * scale), wa.Width), h = Math.Min((int)(dipH * scale), wa.Height);
        int x = Math.Clamp(b.X, wa.Left, wa.Right - w), y = Math.Clamp(b.Y, wa.Top, wa.Bottom - h);
        Player.SetBoundsPx(new PixelRect { X = x, Y = y, Width = w, Height = h });
        SaveBounds();
    }

    private void FitToAspect()
    {
        if (Player == null || Player.IsWallpaper) return;
        var b = Player.GetBoundsPx();
        var s = Player.Scale;
        ResizePlayer((int)(b.Width / s), (int)(b.Width / s / LastAspect));
    }

    private void CenterPlayer()
    {
        if (Player == null || Player.IsWallpaper) return;
        var b = Player.GetBoundsPx();
        var wa = Native.MonitorWorkArea(Native.MonitorFromWindow(Player.Handle, 2));
        Player.SetBoundsPx(new PixelRect { X = wa.Left + (wa.Width - b.Width) / 2, Y = wa.Top + (wa.Height - b.Height) / 2, Width = b.Width, Height = b.Height });
        SaveBounds();
    }

    // ================================================================== aux windows
    public void OpenPlaylist()
    {
        if (_playlistWin != null) { _playlistWin.BringToFront(); return; }
        var w = new AuxWindow(this, "playlist.html", "DeskFlow プレイリスト", 600, 740);
        _playlistWin = w;
        w.Web.Message += OnMessage;
        w.Closed += (_, _) => { if (_playlistWin == w) _playlistWin = null; };
        w.Show();
        w.BringToFront();
        _ = w.InitAsync();
    }

    public void OpenUrlDialog()
    {
        if (_urlWin != null) { _urlWin.BringToFront(); return; }
        var w = new AuxWindow(this, "url.html", "動画URLを追加 - DeskFlow", 580, 390, resizable: false) { Topmost = true };
        _urlWin = w;
        w.Web.Message += OnMessage;
        w.Closed += (_, _) => { if (_urlWin == w) _urlWin = null; };
        w.Show();
        w.BringToFront();
        _ = w.InitAsync();
    }

    // ================================================================== messages from pages
    private void OnMessage(WebMessage m)
    {
        var d = m.Data;
        string Str(string p) => d.ValueKind == JsonValueKind.Object && d.TryGetProperty(p, out var v) ? v.ToString() : "";
        bool Bool(string p) => d.ValueKind == JsonValueKind.Object && d.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.True;

        switch (m.Channel)
        {
            case "ready": m.Source.Post("state", ViewState()); break;
            case "cmd":
            {
                var action = Str("action");
                var value = d.ValueKind == JsonValueKind.Object && d.TryGetProperty("value", out var vv) ? vv : default;
                HandleCommand(action, value);
                break;
            }
            case "ended":
                if (Current?.Id == d.ToString()) Next();
                break;
            case "aspect":
                if (d.ValueKind == JsonValueKind.Number && d.GetDouble() is > 0.1 and < 10) LastAspect = d.GetDouble();
                break;
            case "show-menu": ShowMenuAtCursor(); break;
            case "drag-start": Player?.BeginDrag(d.ValueKind == JsonValueKind.String ? d.GetString()! : ""); break;
            case "drag-end": Player?.EndDrag(); break;
            case "close-window":
                if (m.Source == _urlWin?.Web) _urlWin?.Close();
                else if (m.Source == _playlistWin?.Web) _playlistWin?.Close();
                break;
            case "drop-files":
                if (m.FilePaths.Count > 0) AddFiles(m.FilePaths, Bool("playNow"));
                break;
            case "resolve":
                if (m.ReplyId is int rid)
                {
                    var id = d.ValueKind == JsonValueKind.String ? d.GetString()! : "";
                    var src = m.Source;
                    _ = ResolveAsync(id).ContinueWith(t => src.Reply(rid, t.Result), TaskScheduler.FromCurrentSynchronizationContext());
                }
                break;
            case "clipboard-text":
                if (m.ReplyId is int cid) m.Source.Reply(cid, SafeClipboard());
                break;
        }
    }

    private void HandleCommand(string action, JsonElement v)
    {
        string Str(string p) => v.ValueKind == JsonValueKind.Object && v.TryGetProperty(p, out var x) ? x.ToString() : "";
        bool Bool(string p) => v.ValueKind == JsonValueKind.Object && v.TryGetProperty(p, out var x) && x.ValueKind == JsonValueKind.True;
        int Int(string p) => v.ValueKind == JsonValueKind.Object && v.TryGetProperty(p, out var x) && x.ValueKind == JsonValueKind.Number ? x.GetInt32() : -1;

        switch (action)
        {
            case "togglePause": TogglePause(); break;
            case "next": Next(); break;
            case "prev": Prev(); break;
            case "play": if (v.ValueKind == JsonValueKind.Number) Go(v.GetInt32()); break;
            case "remove": RemoveItem(v.ToString()); break;
            case "move": MoveItem(Int("from"), Int("to")); break;
            case "rename":
            {
                var it = S.Playlist.FirstOrDefault(i => i.Id == Str("id"));
                var title = Str("title").Trim();
                if (it != null && title.Length > 0) { it.Title = title; it.CustomTitle = true; Changed(); }
                break;
            }
            case "setEnabled":
            {
                var it = S.Playlist.FirstOrDefault(i => i.Id == Str("id"));
                if (it != null) { it.Enabled = Bool("enabled"); Changed(); }
                break;
            }
            case "set":
                if (v.ValueKind == JsonValueKind.Object && v.TryGetProperty("value", out var val)) SetOpt(Str("key"), val);
                break;
            case "pickFiles": PickFiles(Bool("playNow")); break;
            case "pickFolder": PickFolder(Bool("playNow")); break;
            case "addUrls": AddUrls(Str("text"), Bool("playNow")); break;
            case "openUrlDialog": OpenUrlDialog(); break;
            case "openPlaylist": OpenPlaylist(); break;
            case "export": ExportPlaylist(); break;
            case "import": ImportPlaylist(); break;
            case "clear": ClearPlaylist(); break;
            case "shortcuts": ShowShortcuts(); break;
        }
    }

    private static string SafeClipboard()
    {
        try { return Clipboard.ContainsText() ? Clipboard.GetText() : ""; } catch { return ""; }
    }

    // ================================================================== menus & tray
    private static string Trunc(string? t, int n) => t == null ? "" : t.Length > n ? t[..(n - 1)] + "…" : t;

    private WinForms.ContextMenuStrip? _menu;

    private void ShowMenuAtCursor()
    {
        _menu?.Dispose();
        _menu = DarkMenu.Create();
        FillMenu(_menu);
        _menu.Closed += (_, _) => Player?.PushToBottom();
        _menu.Show(WinForms.Cursor.Position);
    }

    private void CreateTray()
    {
        var ico = App.IconPath;
        var menu = DarkMenu.Create();
        menu.Opening += (_, e) => { menu.Items.Clear(); FillMenu(menu); e.Cancel = false; };
        menu.Items.Add("…"); // placeholder so the first Opening fires
        _tray = new WinForms.NotifyIcon
        {
            Icon = File.Exists(ico) ? new System.Drawing.Icon(ico, WinForms.SystemInformation.SmallIconSize) : System.Drawing.SystemIcons.Application,
            Text = "DeskFlow",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) => { if (e.Button == WinForms.MouseButtons.Left) OpenPlaylist(); };
    }

    private void UpdateTray()
    {
        if (_tray == null) return;
        var cur = Current;
        var tip = "DeskFlow";
        if (cur != null) tip += $"\n{(EffectivePaused ? "❚❚" : "▶")} {cur.Title}";
        _tray.Text = Trunc(tip, 127);
    }

    private void FillMenu(WinForms.ContextMenuStrip m)
    {
        var cur = Current;
        var wall = Player?.IsWallpaper == true;
        var multi = EnabledIndices().Count > 1;
        var clip = SafeClipboard().Trim();
        var I = m.Items;

        I.Add(DarkMenu.Item(cur != null ? "♪ " + Trunc(cur.Title, 44) : "（動画が登録されていません）", null, false));
        I.Add(DarkMenu.Sep());
        I.Add(DarkMenu.Item(EffectivePaused ? "▶  再生" : "❚❚  一時停止", TogglePause, cur != null));
        I.Add(DarkMenu.Item("次の動画", Next, multi));
        I.Add(DarkMenu.Item("前の動画", Prev, multi));
        I.Add(DarkMenu.Sep());
        I.Add(DarkMenu.Item("動画ファイルを追加…", () => PickFiles(true)));
        I.Add(DarkMenu.Item("フォルダから追加…", () => PickFolder(true)));
        I.Add(DarkMenu.Item("動画URLを追加…（YouTube など）", OpenUrlDialog));
        I.Add(DarkMenu.Item("クリップボードのURLを追加して再生", () => AddUrls(clip, true), clip.StartsWith("http", StringComparison.OrdinalIgnoreCase)));
        I.Add(DarkMenu.Item("プレイリストを開く…", OpenPlaylist));
        I.Add(DarkMenu.Sep());

        I.Add(DarkMenu.Sub($"音量（{(S.Muted ? "ミュート" : S.Volume + "%")}）", true,
            new WinForms.ToolStripItem[] { DarkMenu.Check("ミュート", S.Muted, () => Set(() => S.Muted = !S.Muted)), DarkMenu.Sep() }
                .Concat(new[] { 100, 90, 80, 70, 60, 50, 40, 30, 20, 10, 5 }.Select(v =>
                    (WinForms.ToolStripItem)DarkMenu.Radio($"{v}%", S.Volume == v, () => SetVolume(v)))).ToArray()));

        I.Add(DarkMenu.Sub("再生モード", true,
            DarkMenu.Radio("リスト全体をループ", S.Repeat == "all", () => Set(() => S.Repeat = "all")),
            DarkMenu.Radio("1本をくり返す", S.Repeat == "one", () => Set(() => S.Repeat = "one")),
            DarkMenu.Sep(),
            DarkMenu.Check("シャッフル", S.Shuffle, () => Set(() => S.Shuffle = !S.Shuffle))));

        I.Add(DarkMenu.Sub($"再生速度（{S.Rate}x）", true,
            new[] { 0.25, 0.5, 0.75, 1, 1.25, 1.5, 2 }.Select(r =>
                (WinForms.ToolStripItem)DarkMenu.Radio(r == 1 ? "標準 (1x)" : $"{r}x", Math.Abs(S.Rate - r) < 0.001, () => Set(() => S.Rate = r))).ToArray()));

        I.Add(DarkMenu.Sub("表示", true,
            DarkMenu.Radio("画面いっぱい（はみ出しをカット）", S.Fit == "cover", () => Set(() => S.Fit = "cover")),
            DarkMenu.Radio("全体を表示（余白あり）", S.Fit == "contain", () => Set(() => S.Fit = "contain")),
            DarkMenu.Radio("引き伸ばし", S.Fit == "fill", () => Set(() => S.Fit = "fill")),
            DarkMenu.Sep(),
            DarkMenu.Sub("明るさ", true, new[] { 1, 0.85, 0.7, 0.55, 0.4, 0.25 }.Select(v =>
                (WinForms.ToolStripItem)DarkMenu.Radio($"{v * 100:0}%", Math.Abs(S.Brightness - v) < 0.001, () => Set(() => S.Brightness = v))).ToArray()),
            DarkMenu.Sub("ウィンドウの不透明度", !wall, new[] { 1, 0.9, 0.8, 0.7, 0.6, 0.5, 0.4, 0.3 }.Select(v =>
                (WinForms.ToolStripItem)DarkMenu.Radio($"{v * 100:0}%", Math.Abs(S.Opacity - v) < 0.001, () => Set(() => S.Opacity = v, true))).ToArray()),
            DarkMenu.Check("マウスを乗せたら操作バーを表示", S.ShowControls, () => Set(() => S.ShowControls = !S.ShowControls)),
            DarkMenu.Check("YouTube の字幕を表示", S.Captions, () => Set(() => S.Captions = !S.Captions))));

        I.Add(DarkMenu.Sub("ウィンドウ", !wall,
            DarkMenu.Radio("最背面に固定（デスクトップ上）", S.ZOrder == "bottom", () => Set(() => S.ZOrder = "bottom", true)),
            DarkMenu.Radio("通常", S.ZOrder == "normal", () => Set(() => S.ZOrder = "normal", true)),
            DarkMenu.Radio("最前面に固定", S.ZOrder == "top", () => Set(() => S.ZOrder = "top", true)),
            DarkMenu.Sep(),
            DarkMenu.Check("位置とサイズをロック", S.Locked, () => Set(() => S.Locked = !S.Locked)),
            DarkMenu.Check("リサイズ時に縦横比を保つ（Shift で一時解除）", S.KeepAspect, () => Set(() => S.KeepAspect = !S.KeepAspect)),
            DarkMenu.Check("クリックを透過する（解除はトレイアイコンから）", S.ClickThrough, () => Set(() => S.ClickThrough = !S.ClickThrough, true)),
            DarkMenu.Sep(),
            DarkMenu.Item("動画の縦横比に合わせる", FitToAspect),
            DarkMenu.Sub("サイズ", true,
                DarkMenu.Item("小（480 × 270）", () => ResizePlayer(480, 270)),
                DarkMenu.Item("中（854 × 480）", () => ResizePlayer(854, 480)),
                DarkMenu.Item("大（1280 × 720）", () => ResizePlayer(1280, 720)),
                DarkMenu.Item("縦長（360 × 640）", () => ResizePlayer(360, 640))),
            DarkMenu.Item("画面の中央へ移動", CenterPlayer)));

        I.Add(DarkMenu.Sep());
        I.Add(DarkMenu.Check("壁紙モード（デスクトップアイコンの背面で再生）", wall, () => { S.Mode = wall ? "window" : "wallpaper"; _store.Save(); RecreatePlayer(); }));

        var screens = WinForms.Screen.AllScreens;
        var displayItems = screens.Select((s, i) => (WinForms.ToolStripItem)DarkMenu.Radio(
            $"ディスプレイ {i + 1}（{s.Bounds.Width} × {s.Bounds.Height}）{(s.Primary ? " メイン" : "")}",
            S.WallpaperDisplay == s.DeviceName || (S.WallpaperDisplay == "primary" && s.Primary),
            () => { S.WallpaperDisplay = s.DeviceName; _store.Save(); if (Player?.IsWallpaper == true) RecreatePlayer(); })).ToList();
        if (screens.Length > 1)
        {
            displayItems.Add(DarkMenu.Sep());
            displayItems.Add(DarkMenu.Radio("すべてのディスプレイにまたがる", S.WallpaperDisplay == "all",
                () => { S.WallpaperDisplay = "all"; _store.Save(); if (Player?.IsWallpaper == true) RecreatePlayer(); }));
        }
        I.Add(DarkMenu.Sub("壁紙にするディスプレイ", true, displayItems.ToArray()));

        I.Add(DarkMenu.Sub("設定", true,
            DarkMenu.Check("Windows の起動時に自動で開始", S.AutoStart, () => { S.AutoStart = !S.AutoStart; _store.Save(); ApplyAutoStart(); }),
            DarkMenu.Sep(),
            DarkMenu.Check("全画面アプリの使用中は一時停止", S.PauseOnFullscreen, () => { S.PauseOnFullscreen = !S.PauseOnFullscreen; _store.Save(); Watch(); }),
            DarkMenu.Check("バッテリー駆動中は一時停止", S.PauseOnBattery, () => { S.PauseOnBattery = !S.PauseOnBattery; _store.Save(); UpdateBattery(); }),
            DarkMenu.Check("画面ロック中は一時停止", S.PauseOnLock, () => { S.PauseOnLock = !S.PauseOnLock; _store.Save(); if (!S.PauseOnLock) SetAutoPause("lock", false); }),
            DarkMenu.Sep(),
            DarkMenu.Check("グローバルショートカットを使う", S.GlobalShortcuts, () => { S.GlobalShortcuts = !S.GlobalShortcuts; _store.Save(); RegisterHotkeys(); }),
            DarkMenu.Item("ショートカット一覧…", ShowShortcuts),
            DarkMenu.Sep(),
            DarkMenu.Item("デスクトップとスタートメニューにショートカットを作成", CreateShortcuts),
            DarkMenu.Item("設定フォルダを開く", () => WebHost.OpenExternal(App.UserDataDir))));

        I.Add(DarkMenu.Sep());
        I.Add(DarkMenu.Item("DeskFlow を終了", Quit));
    }

    // ================================================================== hotkeys
    private List<Hotkeys.Binding> HotkeyBindings()
    {
        const uint CA = Native.MOD_CONTROL | Native.MOD_ALT;
        return new()
        {
            new("再生 / 一時停止", "Ctrl + Alt + P", CA, 0x50, TogglePause),
            new("次の動画", "Ctrl + Alt + →", CA, 0x27, Next),
            new("前の動画", "Ctrl + Alt + ←", CA, 0x25, Prev),
            new("音量 +5", "Ctrl + Alt + ↑", CA, 0x26, () => SetVolume(S.Volume + 5)),
            new("音量 -5", "Ctrl + Alt + ↓", CA, 0x28, () => SetVolume(S.Volume - 5)),
            new("ミュート切り替え", "Ctrl + Alt + M", CA, 0x4D, () => Set(() => S.Muted = !S.Muted)),
            new("プレイリストを開く", "Ctrl + Alt + L", CA, 0x4C, OpenPlaylist),
        };
    }

    private void RegisterHotkeys()
    {
        if (_hotkeys == null) return;
        if (!S.GlobalShortcuts) { _hotkeys.Clear(); return; }
        var failed = _hotkeys.Register(HotkeyBindings());
        if (failed.Count > 0) Log.Write("hotkeys in use: " + string.Join(", ", failed));
    }

    private void ShowShortcuts()
    {
        var lines = HotkeyBindings().Select(b => $"{b.Display}　…　{b.Label}");
        MessageBox.Show(string.Join("\n", new[] { "【どこからでも】" }.Concat(lines).Concat(new[]
        {
            "", "【プレーヤー上で】",
            "Space … 再生 / 一時停止", "← / → … 5秒戻る / 進む", "N / P … 次 / 前の動画",
            "↑ / ↓ … 音量", "M … ミュート", "Ctrl + V … URLを貼り付けて追加・再生",
            "ダブルクリック … 再生 / 一時停止", "ドラッグ … 移動　／　端をドラッグ … リサイズ",
        })), "DeskFlow ショートカット", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ================================================================== system integration
    private void ApplyAutoStart()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (S.AutoStart) key.SetValue("DeskFlow", App.LaunchCommand);
            else if (key.GetValue("DeskFlow") != null) key.DeleteValue("DeskFlow");
        }
        catch (Exception ex) { Log.Write($"autostart: {ex.Message}"); }
    }

    private void CreateShortcuts()
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell")!;
            dynamic shell = Activator.CreateInstance(shellType)!;
            var targets = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            };
            foreach (var dir in targets)
            {
                dynamic lnk = shell.CreateShortcut(Path.Combine(dir, "DeskFlow.lnk"));
                var (target, arguments) = App.Launcher;
                lnk.TargetPath = target;
                lnk.Arguments = arguments;
                lnk.WorkingDirectory = AppContext.BaseDirectory;
                lnk.IconLocation = (App.IsStandaloneExe ? target : App.IconPath) + ",0";
                lnk.Description = "DeskFlow - デスクトップ動画プレーヤー";
                lnk.Save();
            }
            Toast("デスクトップとスタートメニューにショートカットを作成しました");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"ショートカットを作成できませんでした: {ex.Message}", "DeskFlow");
        }
    }

    private void SetupSystemEvents()
    {
        var d = Application.Current.Dispatcher;
        SystemEvents.SessionSwitch += (_, e) => d.BeginInvoke(() =>
        {
            if (e.Reason == SessionSwitchReason.SessionLock) SetAutoPause("lock", S.PauseOnLock);
            else if (e.Reason == SessionSwitchReason.SessionUnlock) SetAutoPause("lock", false);
        });
        SystemEvents.PowerModeChanged += (_, _) => d.BeginInvoke(UpdateBattery);
        SystemEvents.DisplaySettingsChanged += (_, _) => d.BeginInvoke(OnDisplaysChanged);
        UpdateBattery();
    }

    private void UpdateBattery() =>
        SetAutoPause("battery", S.PauseOnBattery && WinForms.SystemInformation.PowerStatus.PowerLineStatus == WinForms.PowerLineStatus.Offline);

    private void OnDisplaysChanged()
    {
        if (Player == null) return;
        if (Player.IsWallpaper) DesktopHost.Reposition(Player.Handle, WallpaperRect());
        else Player.SetBoundsPx(WindowRect());
    }

    private void Watch()
    {
        if (Player == null || _quitting) return;
        if (Player.IsWallpaper && !DesktopHost.IsAttached(Player.Handle))
        {
            Log.Write("desktop host lost; re-attaching");
            RecreatePlayer();
            return;
        }
        var own = Application.Current.Windows.OfType<Window>()
            .Select(w => new System.Windows.Interop.WindowInteropHelper(w).Handle).ToList();
        SetAutoPause("fullscreen", S.PauseOnFullscreen && Native.IsForegroundFullscreen(own, Player.Handle));
    }

    public void HandleArgs(string[] args)
    {
        var files = args.Where(a => !a.StartsWith('-') && (File.Exists(a) || Directory.Exists(a))).ToList();
        if (files.Count > 0) AddFiles(files, true);
        else if (args.Contains("--second-instance")) OpenPlaylist();
    }

    /// <summary>"file2" sorts before "file10".</summary>
    private sealed class NaturalComparer : IComparer<string>
    {
        [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string a, string b);
        public int Compare(string? x, string? y) => StrCmpLogicalW(x ?? "", y ?? "");
    }
}
