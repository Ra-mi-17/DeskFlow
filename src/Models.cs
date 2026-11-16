using System.Collections.Generic;

namespace DeskFlow;

public sealed class PlaylistItem
{
    public string Id { get; set; } = "";
    /// <summary>file | youtube | vimeo | url | web</summary>
    public string Type { get; set; } = "";
    public string Src { get; set; } = "";
    public string Title { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public double? Start { get; set; }
    public string? Hash { get; set; }
    public double? Aspect { get; set; }
    public string? Thumb { get; set; }
    public string? Author { get; set; }
    public bool CustomTitle { get; set; }
    public bool Enriched { get; set; }
    public long AddedAt { get; set; }

    public PlaylistItem Clone() => (PlaylistItem)MemberwiseClone();
}

/// <summary>Window bounds in physical screen pixels.</summary>
public sealed class PixelRect
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed class Settings
{
    public List<PlaylistItem> Playlist { get; set; } = new();
    public int CurrentIndex { get; set; }
    public int Volume { get; set; } = 50;
    public bool Muted { get; set; }
    public bool Paused { get; set; }
    /// <summary>all | one</summary>
    public string Repeat { get; set; } = "all";
    public bool Shuffle { get; set; }
    public double Rate { get; set; } = 1;
    /// <summary>cover | contain | fill</summary>
    public string Fit { get; set; } = "cover";
    public double Opacity { get; set; } = 1;
    public double Brightness { get; set; } = 1;
    /// <summary>window | wallpaper</summary>
    public string Mode { get; set; } = "window";
    /// <summary>primary | all | device name</summary>
    public string WallpaperDisplay { get; set; } = "primary";
    /// <summary>bottom | normal | top</summary>
    public string ZOrder { get; set; } = "bottom";
    public bool Locked { get; set; }
    public bool ClickThrough { get; set; }
    public bool KeepAspect { get; set; } = true;
    public bool ShowControls { get; set; } = true;
    /// <summary>Show YouTube captions (off by default: YouTube enables auto-translated captions on its own).</summary>
    public bool Captions { get; set; }
    public PixelRect? Bounds { get; set; }
    public bool AutoStart { get; set; }
    public bool PauseOnFullscreen { get; set; } = true;
    public bool PauseOnBattery { get; set; }
    public bool PauseOnLock { get; set; } = true;
    public bool GlobalShortcuts { get; set; } = true;
}
