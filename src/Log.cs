using System;
using System.IO;

namespace DeskFlow;

public static class Log
{
    private static readonly object Gate = new();
    public static string? FilePath { get; set; }

    public static void Write(string msg)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {msg}";
        System.Diagnostics.Debug.WriteLine(line);
        if (FilePath == null) return;
        lock (Gate)
        {
            try
            {
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > 512 * 1024) fi.Delete();
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
            catch { /* ignore */ }
        }
    }
}
