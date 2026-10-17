using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace Lignum.App;

/// <summary>
/// One Lignum per user and settings folder (ticket 07): a second start (e.g. double-clicking a file) hands its
/// file to the running one over a named pipe and exits; the running one opens it in a new tab. A named mutex
/// tells whether one is running. Both are scoped to the user, and to the settings folder so a test run with
/// <c>--settings-dir</c> never talks to your own Lignum.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stop = new();

    private SingleInstance(Mutex mutex, string name)
    {
        _mutex = mutex;
        Name = name;
    }

    public string Name { get; }

    /// <summary>The mutex and pipe name for this user and settings folder.</summary>
    public static string NameFor(string settingsDirectory)
    {
        var key = $"{Environment.UserDomainName}\\{Environment.UserName}|{Path.GetFullPath(settingsDirectory).ToUpperInvariant()}";
        return "Lignum-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..24];
    }

    /// <summary>Becomes the running instance for <paramref name="name"/>; null when another one already is.</summary>
    public static SingleInstance? TryAcquire(string name)
    {
        var mutex = new Mutex(initiallyOwned: true, @"Local\" + name, out var created);
        if (created) return new SingleInstance(mutex, name);
        mutex.Dispose();
        return null;
    }

    /// <summary>
    /// Hands <paramref name="path"/> (null: just bring the window forward) to the running instance. False when it
    /// does not answer in time (then this start carries on as a separate window).
    /// </summary>
    public static bool TrySend(string name, string? path, int timeoutMilliseconds = 3000)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(timeoutMilliseconds);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false));
            writer.WriteLine(path ?? "");
            writer.Flush();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Receives what later starts send, on a background thread, until disposed ("" means "come to the front").</summary>
    public void Listen(Action<string> onMessage) => _ = Task.Run(async () =>
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(Name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                using var reader = new StreamReader(server, Encoding.UTF8);
                if (await reader.ReadLineAsync(_stop.Token).ConfigureAwait(false) is { } message) onMessage(message);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                // A client that gave up half-way; wait for the next one.
            }
        }
    });

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not owned by this thread any more; disposing still frees it.
        }

        _mutex.Dispose();
    }
}
