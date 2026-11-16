using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace DeskFlow;

/// <summary>
/// Loopback HTTP server. Serving the UI over http:// gives YouTube embeds a valid origin/referrer,
/// and local video files are streamed with HTTP Range support.
/// </summary>
public sealed class LocalServer
{
    private WebApplication? _app;
    private readonly string _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    private volatile Dictionary<string, string> _media = new();

    public string Origin { get; private set; } = "";

    public string Url(string page) => $"{Origin}/ui/{page}";
    public string MediaUrl(string id) => $"{Origin}/media/{Uri.EscapeDataString(id)}?k={_token}";

    /// <summary>Replace the id → file path table (called whenever the playlist changes).</summary>
    public void SetMedia(Dictionary<string, string> map) => _media = map;

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(o => o.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();

        var types = new FileExtensionContentTypeProvider();

        // UI pages are embedded resources ("wwwroot/<name>"), so the app works as a single file.
        var ui = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        app.MapGet("/ui/{name}", (string name, HttpContext ctx) =>
        {
            if (!ui.TryGetValue(name, out var bytes))
            {
                using var s = App.Resource("wwwroot/" + name);
                if (s == null) return Results.NotFound();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                bytes = ms.ToArray();
                lock (ui) ui[name] = bytes;
            }
            if (!types.TryGetContentType(name, out var mime)) mime = "application/octet-stream";
            if (mime.StartsWith("text/") || mime.EndsWith("javascript")) mime += "; charset=utf-8";
            ctx.Response.Headers.CacheControl = "no-cache";
            return Results.Bytes(bytes, mime);
        });

        app.MapGet("/media/{id}", (string id, string? k) =>
        {
            if (k != _token) return Results.StatusCode(403);
            if (!_media.TryGetValue(id, out var path) || !File.Exists(path)) return Results.NotFound();
            if (!types.TryGetContentType(path, out var mime)) mime = "application/octet-stream";
            if (path.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase)) mime = "video/webm"; // lets Chromium try Matroska
            return Results.File(path, mime, enableRangeProcessing: true);
        });

        await app.StartAsync();
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
        Origin = addresses.First().Replace("[::1]", "127.0.0.1").TrimEnd('/');
        _app = app;
        Log.Write($"server listening on {Origin}");
    }

    public async Task StopAsync()
    {
        if (_app != null) await _app.StopAsync();
    }
}
