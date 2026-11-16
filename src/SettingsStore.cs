using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;

namespace DeskFlow;

public sealed class SettingsStore
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly string _file;
    private readonly DispatcherTimer _timer;

    public Settings Data { get; }

    public SettingsStore(string file)
    {
        _file = file;
        Settings? loaded = null;
        try
        {
            if (File.Exists(file)) loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(file), Json);
        }
        catch (Exception ex)
        {
            try { File.Copy(file, file + ".broken", true); } catch { /* ignore */ }
            Log.Write($"settings load failed: {ex.Message}");
        }
        Data = loaded ?? new Settings();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _timer.Tick += (_, _) => Flush();
    }

    public void Save()
    {
        _timer.Stop();
        _timer.Start();
    }

    public void Flush()
    {
        _timer.Stop();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var tmp = _file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Data, Json));
            File.Move(tmp, _file, true);
        }
        catch (Exception ex)
        {
            Log.Write($"settings save failed: {ex.Message}");
        }
    }
}
