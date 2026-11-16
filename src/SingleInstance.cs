using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DeskFlow;

/// <summary>Ensures one running instance; later launches forward their arguments through a named pipe.</summary>
public sealed class SingleInstance : IDisposable
{
    private static readonly string Id = $"DeskFlow-{Environment.UserName}";
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cts = new();

    public bool IsFirst { get; }

    public SingleInstance()
    {
        _mutex = new Mutex(true, $"Local\\{Id}", out var created);
        IsFirst = created;
    }

    public static bool Forward(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", Id, PipeDirection.Out);
            client.Connect(1500);
            using var w = new StreamWriter(client);
            w.WriteLine(JsonSerializer.Serialize(args));
            w.Flush();
            return true;
        }
        catch { return false; }
    }

    public void Listen(Action<string[]> onArgs)
    {
        Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(Id, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(_cts.Token);
                    using var r = new StreamReader(server);
                    var line = await r.ReadLineAsync();
                    var args = line == null ? Array.Empty<string>() : JsonSerializer.Deserialize<string[]>(line) ?? Array.Empty<string>();
                    onArgs(args);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { Log.Write($"pipe error: {ex.Message}"); await Task.Delay(500); }
            }
        });
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _mutex.ReleaseMutex(); } catch { /* not owner */ }
        _mutex.Dispose();
    }
}
