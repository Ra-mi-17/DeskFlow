using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace DeskFlow;

public static class App
{
    public static readonly string UserDataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeskFlow");
    public static readonly string WebViewDataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskFlow", "WebView2");
    /// <summary>icon.ico extracted from the embedded resources (used by tray / windows / shortcuts).</summary>
    public static readonly string IconPath = Path.Combine(UserDataDir, "icon.ico");
    public static readonly string ConhostPath = Path.Combine(Environment.SystemDirectory, "conhost.exe");

    public static bool Debug { get; private set; }

    private static string ProcessPath => Environment.ProcessPath ?? "dotnet.exe";

    /// <summary>
    /// True when running as DeskFlow.exe (single-file / self-contained publish),
    /// false when hosted by dotnet.exe ("dotnet DeskFlow.dll").
    /// </summary>
    public static bool IsStandaloneExe =>
        !Path.GetFileNameWithoutExtension(ProcessPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase);

    /// <summary>Program and arguments that start DeskFlow without a console window.</summary>
    public static (string Target, string Arguments) Launcher
    {
        get
        {
            if (IsStandaloneExe) return (ProcessPath, "");
            var dll = Path.Combine(AppContext.BaseDirectory, "DeskFlow.dll");
            return (ConhostPath, $"--headless \"{ProcessPath}\" \"{dll}\"");
        }
    }

    public static string LaunchCommand
    {
        get
        {
            var (t, a) = Launcher;
            return a.Length == 0 ? $"\"{t}\"" : $"\"{t}\" {a}";
        }
    }

    public static Stream? Resource(string name) => typeof(App).Assembly.GetManifestResourceStream(name);

    private static void ExtractIcon()
    {
        try
        {
            using var s = Resource("assets/icon.ico");
            if (s == null) return;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            var bytes = ms.ToArray();
            if (!File.Exists(IconPath) || new FileInfo(IconPath).Length != bytes.Length) File.WriteAllBytes(IconPath, bytes);
        }
        catch (Exception ex) { Log.Write($"icon extract failed: {ex.Message}"); }
    }

    private static Controller? _controller;

    [STAThread]
    public static int Main(string[] args)
    {
        // dotnet.exe has no DPI manifest; opt into per-monitor DPI before any window exists.
        Native.SetProcessDpiAwarenessContext(Native.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        // When started directly as "dotnet DeskFlow.dll" a console window appears: hide it.
        var console = Native.GetConsoleWindow();
        if (console != IntPtr.Zero) { Native.ShowWindow(console, 0); Native.FreeConsole(); }

        Debug = args.Contains("--debug") || Environment.GetEnvironmentVariable("DESKFLOW_DEBUG") == "1";
        Directory.CreateDirectory(UserDataDir);
        Log.FilePath = Path.Combine(UserDataDir, "deskflow.log");
        ExtractIcon();

        using var single = new SingleInstance();
        if (!single.IsFirst)
        {
            SingleInstance.Forward(args.Length > 0 ? args : new[] { "--second-instance" });
            return 0;
        }

        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) =>
        {
            Log.Write($"unhandled: {e.Exception}");
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write($"fatal: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Write($"task: {e.Exception}"); e.SetObserved(); };

        app.Startup += async (_, _) =>
        {
            try
            {
                var store = new SettingsStore(Path.Combine(UserDataDir, "settings.json"));
                var server = new LocalServer();
                await server.StartAsync();
                _controller = new Controller(store, server);
                _controller.Start(args);
                single.Listen(a => app.Dispatcher.BeginInvoke(() => _controller.HandleArgs(a)));
                Log.Write("started");
            }
            catch (Exception ex)
            {
                Log.Write($"startup failed: {ex}");
                MessageBox.Show($"DeskFlow を起動できませんでした。\n\n{ex.Message}", "DeskFlow", MessageBoxButton.OK, MessageBoxImage.Error);
                app.Shutdown(1);
            }
        };
        app.SessionEnding += (_, _) => _controller?.Quit();
        return app.Run();
    }
}
