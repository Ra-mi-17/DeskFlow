using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskFlow;

/// <summary>Regular (framed) window hosting one of the HTML pages: playlist / URL dialog.</summary>
public sealed class AuxWindow : Window
{
    private readonly string _page;
    private readonly Controller _c;

    public WebHost Web { get; }

    public AuxWindow(Controller c, string page, string title, double width, double height, bool resizable = true)
    {
        _c = c;
        _page = page;
        Title = title;
        Width = width;
        Height = height;
        MinWidth = Math.Min(width, 440);
        MinHeight = Math.Min(height, 320);
        ResizeMode = resizable ? ResizeMode.CanResize : ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0x15, 0x16, 0x1c));
        var ico = App.IconPath;
        if (File.Exists(ico)) Icon = BitmapFrame.Create(new Uri(ico));

        Web = new WebHost(c.Server.Origin, System.Drawing.Color.FromArgb(0x15, 0x16, 0x1c));
        Content = Web.View;
        SourceInitialized += (_, _) => Native.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
    }

    public async Task InitAsync() => await Web.InitAsync(_c.Server.Url(_page), contextMenus: true);

    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
        Topmost = true;  // reliably surface above other apps
        Topmost = false;
        Focus();
    }
}
