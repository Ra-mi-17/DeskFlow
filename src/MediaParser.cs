using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;

namespace DeskFlow;

/// <summary>Turns user input (URLs / file paths) into playlist items.</summary>
public static class MediaParser
{
    public static readonly string[] VideoExts =
        { "mp4", "m4v", "webm", "mkv", "mov", "ogv", "ogg", "avi", "wmv", "mpg", "mpeg", "ts", "m2ts", "3gp", "flv" };

    private static readonly Regex DirectRe = new(@"\.(mp4|m4v|webm|mkv|mov|ogv|ogg|m3u8)$", RegexOptions.IgnoreCase);
    private static readonly Regex YtIdRe = new(@"^[\w-]{11}$");

    public static bool IsVideoFile(string path) =>
        VideoExts.Contains(Path.GetExtension(path).TrimStart('.').ToLowerInvariant());

    public static PlaylistItem FileItem(string path) => new()
    {
        Type = "file",
        Src = path,
        Title = Path.GetFileNameWithoutExtension(path),
    };

    public static double ParseTime(string? t)
    {
        if (string.IsNullOrWhiteSpace(t)) return 0;
        var m = Regex.Match(t, @"^(\d+(?:\.\d+)?)s?$");
        if (m.Success) return double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        m = Regex.Match(t, @"^(?:(\d+)h)?(?:(\d+)m)?(?:(\d+)s)?$");
        if (!m.Success) return 0;
        int G(int i) => m.Groups[i].Success ? int.Parse(m.Groups[i].Value) : 0;
        return G(1) * 3600 + G(2) * 60 + G(3);
    }

    public static PlaylistItem? Parse(string raw)
    {
        var s = (raw ?? "").Trim().Trim('"', '\'');
        if (s.Length == 0) return null;

        // Local paths
        if (s.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            try { s = new Uri(s).LocalPath; } catch { return null; }
        }
        if (Regex.IsMatch(s, @"^[a-zA-Z]:[\\/]") || s.StartsWith(@"\\"))
        {
            return File.Exists(s) ? FileItem(s) : null;
        }

        if (!Regex.IsMatch(s, "^https?://", RegexOptions.IgnoreCase))
        {
            if (!Regex.IsMatch(s, @"^[\w-]+(\.[\w-]+)+")) return null;
            s = "https://" + s;
        }
        if (!Uri.TryCreate(s, UriKind.Absolute, out var u)) return null;
        var host = Regex.Replace(u.Host.ToLowerInvariant(), @"^(www|m|music)\.", "");
        var query = HttpUtility.ParseQueryString(u.Query);

        // YouTube
        string? ytId = null;
        var shorts = false;
        if (host == "youtu.be")
        {
            ytId = u.AbsolutePath.TrimStart('/').Split('/')[0];
        }
        else if (host is "youtube.com" or "youtube-nocookie.com")
        {
            ytId = query["v"];
            if (ytId == null)
            {
                var m = Regex.Match(u.AbsolutePath, @"^/(shorts|embed|live|v)/([\w-]{11})");
                if (m.Success) { ytId = m.Groups[2].Value; shorts = m.Groups[1].Value == "shorts"; }
            }
        }
        if (ytId != null && YtIdRe.IsMatch(ytId))
        {
            var start = ParseTime(query["t"] ?? query["start"]);
            return new PlaylistItem
            {
                Type = "youtube",
                Src = ytId,
                Start = start > 0 ? start : null,
                Title = $"YouTube ({ytId})",
                Aspect = shorts ? 9.0 / 16 : 16.0 / 9,
                Thumb = $"https://i.ytimg.com/vi/{ytId}/mqdefault.jpg",
            };
        }

        // Vimeo
        if (host is "vimeo.com" or "player.vimeo.com")
        {
            var m = Regex.Match(u.AbsolutePath, @"/(?:video/)?(\d{5,})(?:/([0-9a-f]{6,}))?");
            if (m.Success)
            {
                return new PlaylistItem
                {
                    Type = "vimeo",
                    Src = m.Groups[1].Value,
                    Hash = m.Groups[2].Success ? m.Groups[2].Value : query["h"],
                    Title = $"Vimeo ({m.Groups[1].Value})",
                };
            }
        }

        // Direct video link
        if (DirectRe.IsMatch(u.AbsolutePath))
        {
            var name = Uri.UnescapeDataString(u.AbsolutePath.Split('/').LastOrDefault() ?? u.Host);
            return new PlaylistItem { Type = "url", Src = u.AbsoluteUri, Title = name };
        }

        // Other sites: resolved at play time through yt-dlp (if available)
        var path = u.AbsolutePath == "/" ? "" : u.AbsolutePath;
        return new PlaylistItem { Type = "web", Src = u.AbsoluteUri, Title = u.Host + path };
    }

    /// <summary>Split free text (multiple lines / URLs) into entries.</summary>
    public static IEnumerable<string> Split(string text)
    {
        foreach (var line in (text ?? "").Split('\n'))
        {
            var t = line.Trim();
            if (t.Length == 0) continue;
            if (Regex.Matches(t, "https?://").Count > 1)
            {
                foreach (var part in Regex.Split(t, @"\s+")) if (part.Length > 0) yield return part;
            }
            else yield return t;
        }
    }
}
