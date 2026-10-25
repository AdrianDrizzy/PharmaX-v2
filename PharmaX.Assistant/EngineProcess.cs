using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace PharmaX.Assistant;

/// <summary>
/// One running engine: a second PharmaX process (see
/// <see cref="AssistantHost"/>), and the conversation with it over its
/// standard input and output.
/// </summary>
/// <remarks>
/// It runs below normal priority, so the till stays quick to answer while
/// the model works. It is stopped, not asked to stop: the engine can be in
/// the middle of a minute's reading, and nothing it is doing needs to
/// finish.
/// </remarks>
internal sealed class EngineProcess : IDisposable
{
    private const string ReplyPrefix = "PXA ";
    private const int KeptErrorLines = 30;

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly Process _process;
    private readonly Queue<string> _errors = new();

    private EngineProcess(Process process) => _process = process;

    public static EngineProcess Start()
    {
        var self = Environment.ProcessPath
                   ?? throw new InvalidOperationException("PharmaX could not find its own program file.");

        var start = new ProcessStartInfo(self)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
            WorkingDirectory = AppContext.BaseDirectory
        };
        start.ArgumentList.Add(AssistantHost.Switch);

        var process = Process.Start(start)
                      ?? throw new InvalidOperationException("The assistant could not be started.");
        var engine = new EngineProcess(process);

        process.ErrorDataReceived += engine.Keep;
        process.BeginErrorReadLine();

        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Already gone, or not allowed: it still works at normal priority.
        }

        return engine;
    }

    public bool HasExited => _process.HasExited;

    /// <summary>The engine's last words on standard error, for the log.</summary>
    public string RecentErrors
    {
        get
        {
            lock (_errors) return string.Join(Environment.NewLine, _errors);
        }
    }

    /// <summary>
    /// Sends one request and waits for its reply, handing any pieces of a
    /// chat answer that come first to <paramref name="partial"/>.
    /// Cancelling stops the engine: a pipe read cannot be interrupted any
    /// other way.
    /// </summary>
    public async Task<JsonElement> SendAsync(object request, CancellationToken ct,
        Action<string>? partial = null)
    {
        using var stop = ct.Register(Stop);

        try
        {
            await _process.StandardInput.WriteLineAsync(
                JsonSerializer.Serialize(request).AsMemory(), ct).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(ct).ConfigureAwait(false);

            while (true)
            {
                var line = await _process.StandardOutput.ReadLineAsync(CancellationToken.None)
                    .ConfigureAwait(false);

                if (line is null)
                {
                    ct.ThrowIfCancellationRequested();
                    throw Stopped();
                }

                if (!line.StartsWith(ReplyPrefix, StringComparison.Ordinal)) continue;

                using var doc = JsonDocument.Parse(line.AsMemory(ReplyPrefix.Length));
                var reply = doc.RootElement.Clone();

                if (reply.TryGetProperty("partial", out var piece))
                {
                    partial?.Invoke(piece.GetString() ?? "");
                    continue;
                }

                if (reply.GetProperty("ok").GetBoolean()) return reply;

                throw new InvalidOperationException(
                    "The assistant could not answer: " + reply.GetProperty("error").GetString());
            }
        }
        catch (IOException) when (!ct.IsCancellationRequested)
        {
            // Writing to an engine that has already died.
            throw Stopped();
        }
    }

    private InvalidOperationException Stopped()
    {
        var code = _process.WaitForExit(2000) ? $" (exit code {_process.ExitCode})" : "";

        return new InvalidOperationException(
            $"The assistant stopped unexpectedly{code}. PharmaX itself is unaffected; "
            + "ask again to restart it.");
    }

    private void Keep(object sender, DataReceivedEventArgs e)
    {
        if (e.Data is null) return;

        lock (_errors)
        {
            _errors.Enqueue(e.Data);
            while (_errors.Count > KeptErrorLines) _errors.Dequeue();
        }
    }

    private void Stop()
    {
        try
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Already gone.
        }
    }

    public void Dispose()
    {
        Stop();
        _process.Dispose();
    }
}
