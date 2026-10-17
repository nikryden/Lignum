using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Lignum.App.Assistant;

/// <summary>One tool call over the AI pipe: one JSON line each way.</summary>
public sealed record AiRequest(string Tool, Dictionary<string, JsonElement>? Arguments);

/// <summary>The answer to an <see cref="AiRequest"/>.</summary>
public sealed record AiResponse(string Content, bool IsError);

/// <summary>
/// The running Lignum's side of AI access (ticket 05): serves tool calls from <c>Lignum.exe --mcp</c> processes
/// (one connection per AI tool) on a named pipe for this user only. Exists only while AI access is on.
/// </summary>
internal sealed class AiAccessServer : IDisposable
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly CancellationTokenSource _stop = new();

    public AiAccessServer(string pipeName, Func<ToolCall, CancellationToken, Task<ToolOutcome>> handle) =>
        _ = Task.Run(() => AcceptAsync(pipeName, handle));

    private async Task AcceptAsync(string pipeName, Func<ToolCall, CancellationToken, Task<ToolOutcome>> handle)
    {
        while (!_stop.IsCancellationRequested)
        {
            var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await server.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException)
            {
                await server.DisposeAsync();
                if (_stop.IsCancellationRequested) return;
                continue;
            }

            _ = ServeAsync(server, handle); // the next client may connect while this one is served
        }
    }

    private async Task ServeAsync(NamedPipeServerStream server, Func<ToolCall, CancellationToken, Task<ToolOutcome>> handle)
    {
        await using (server)
        {
            using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
            await using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            try
            {
                while (await reader.ReadLineAsync(_stop.Token).ConfigureAwait(false) is { } line)
                {
                    AiResponse response;
                    try
                    {
                        var request = JsonSerializer.Deserialize<AiRequest>(line, Json)
                                      ?? throw new JsonException("empty request");
                        var outcome = await handle(new ToolCall(request.Tool, request.Arguments ?? []), _stop.Token)
                            .ConfigureAwait(false);
                        response = new AiResponse(outcome.Content, outcome.IsError);
                    }
                    catch (JsonException ex)
                    {
                        response = new AiResponse($"Bad request: {ex.Message}", true);
                    }

                    await writer.WriteLineAsync(JsonSerializer.Serialize(response, Json)).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
            {
                // The AI tool went away, or AI access was turned off.
            }
        }
    }

    public void Dispose() => _stop.Cancel();
}

/// <summary>
/// The <c>Lignum.exe --mcp</c> side: sends tool calls to the running Lignum. One connection, reused; if Lignum
/// restarted in between, it reconnects once.
/// </summary>
public sealed class AiPipeClient(string pipeName, int connectTimeoutMilliseconds = 2000) : IDisposable
{
    public const string NotAvailableMessage =
        "Lignum is not running, or AI access is off. Open Lignum and turn on View > AI access.";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    public async Task<AiResponse> CallAsync(string tool, Dictionary<string, JsonElement> arguments,
                                            CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    await ConnectAsync(cancellationToken);
                    await _writer!.WriteLineAsync(JsonSerializer.Serialize(new AiRequest(tool, arguments), AiAccessServer.Json));
                    var line = await _reader!.ReadLineAsync(cancellationToken) ?? throw new IOException("Lignum closed the connection.");
                    return JsonSerializer.Deserialize<AiResponse>(line, AiAccessServer.Json)
                           ?? new AiResponse("Empty answer from Lignum.", true);
                }
                catch (Exception ex) when (ex is IOException or TimeoutException or ObjectDisposedException)
                {
                    Close(); // Lignum restarted or turned AI access off: try once more with a new connection
                }
            }

            return new AiResponse(NotAvailableMessage, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_pipe is { IsConnected: true }) return;
        Close();
        _pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await _pipe.ConnectAsync(connectTimeoutMilliseconds, cancellationToken);
        _reader = new StreamReader(_pipe, Encoding.UTF8, leaveOpen: true);
        _writer = new StreamWriter(_pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
    }

    private void Close()
    {
        foreach (IDisposable? part in new IDisposable?[] { _reader, _writer, _pipe })
        {
            try
            {
                part?.Dispose();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // The other end is gone already (the writer flushes on dispose).
            }
        }

        _pipe = null;
        _reader = null;
        _writer = null;
    }

    public void Dispose() => Close();
}
