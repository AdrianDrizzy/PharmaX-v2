using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Assistant;

namespace PharmaX.Assistant;

/// <summary>
/// The chat assistant, with its model in a second PharmaX process (see
/// <see cref="AssistantHost"/>), as the reports assistant's is.
/// </summary>
/// <remarks>
/// One per chat window, disposed with it, which stops the engine and gives
/// its memory back. If the engine dies, the question in hand fails with a
/// message and the next one starts a new engine.
/// </remarks>
public sealed class EngineChatAssistant : IChatAssistant, IDisposable
{
    /// <summary>About three hundred words: enough for an answer, short enough to wait for.</summary>
    private const int MaxAnswerTokens = 400;

    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(5);

    private readonly AssistantOptions _options;
    private readonly ILogger<EngineChatAssistant> _log;

    // Not disposed: a question finishing after the window has closed still
    // releases and checks them. They hold no native resources.
#pragma warning disable CA2213
    private readonly SemaphoreSlim _turn = new(1, 1);
    private readonly CancellationTokenSource _closing = new();
#pragma warning restore CA2213

    private EngineProcess? _engine;
    private volatile bool _ready;
    private volatile bool _disposed;

    public EngineChatAssistant(PharmaXOptions options, ILogger<EngineChatAssistant> log)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Assistant;
        _log = log;
    }

    public bool IsAvailable =>
        _options.Enabled && AssistantHost.IsInstalled && File.Exists(_options.ResolvedModelPath);

    public bool IsReady => _ready;

    /// <remarks>A one-word answer to nothing, which is what loads the model.</remarks>
    public Task WarmUpAsync(CancellationToken ct = default) =>
        RunAsync(token => ChatAsync(ChatPrompt.Build(Array.Empty<ChatTurn>(), "Hello"), 1,
            new Progress<string>(), token), ct);

    public Task<string> AskAsync(IReadOnlyList<ChatTurn> earlier, string question,
        IProgress<string> writing, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(writing);

        return RunAsync(async token =>
        {
            var clock = Stopwatch.StartNew();
            var answer = await ChatAsync(ChatPrompt.Build(earlier, question), MaxAnswerTokens,
                writing, token).ConfigureAwait(false);

            // How long, not what: a question can name a patient.
            _log.LogInformation("Chat assistant answered in {Ms} ms", clock.ElapsedMilliseconds);
            return answer.Trim();
        }, ct);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _closing.Cancel();

        if (_turn.Wait(0))
        {
            try { Stop(); }
            finally { _turn.Release(); }
        }
    }

    private async Task<string> ChatAsync(string prompt, int maxTokens, IProgress<string> writing,
        CancellationToken ct)
    {
        if (_engine is null || _engine.HasExited)
        {
            Stop();
            _engine = EngineProcess.Start();
        }

        var answer = new StringBuilder();
        var plain = new PlainTextFilter();

        void Write(string text)
        {
            if (text.Length == 0) return;
            answer.Append(text);
            writing.Report(text);
        }

        await _engine.SendAsync(new
        {
            op = "chat",
            model = _options.ResolvedModelPath,
            threads = _options.Threads > 0
                ? _options.Threads
                : Math.Max(1, Environment.ProcessorCount / 2),
            prompt,
            maxTokens
        }, ct, piece => Write(plain.Push(piece))).ConfigureAwait(false);

        Write(plain.Flush());
        _ready = true;
        return answer.ToString();
    }

    private async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _closing.Token);
        linked.CancelAfter(Patience);

        await _turn.WaitAsync(linked.Token).ConfigureAwait(false);

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return await work(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested
                                                && !_closing.IsCancellationRequested)
        {
            Stop();
            throw new TimeoutException(
                "The assistant took too long to answer, so it was stopped. Try again.");
        }
        catch (OperationCanceledException)
        {
            Stop();
            throw;
        }
        catch (InvalidOperationException) when (_engine?.HasExited == true)
        {
            _log.LogWarning("Chat assistant engine stopped unexpectedly: {Errors}",
                _engine.RecentErrors);
            Stop();
            throw;
        }
        finally
        {
            _turn.Release();
        }
    }

    private void Stop()
    {
        _ready = false;
        _engine?.Dispose();
        _engine = null;
    }
}
