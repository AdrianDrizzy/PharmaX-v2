using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Reporting;

namespace PharmaX.Assistant;

/// <summary>
/// The reports assistant, with its model running in a second PharmaX
/// process (see <see cref="AssistantHost"/>).
/// </summary>
/// <remarks>
/// <para>
/// One per reports screen, and disposed with it, which stops the engine
/// and gives its two gigabytes back. Nothing starts until the first
/// warm-up or question.
/// </para>
/// <para>
/// Everything PharmaX-specific happens here - the instructions, the
/// grammar, reading the answer - and the engine only runs the model. If
/// the engine dies, the question in hand fails with a message and the next
/// one starts a new engine, which reads its saved instructions in seconds.
/// </para>
/// </remarks>
public sealed class EngineReportSuggester : IReportSuggester, IDisposable
{
    /// <summary>A whole answer is about forty tokens; this is room to spare.</summary>
    private const int MaxAnswerTokens = 160;

    /// <summary>
    /// The longest a request may take, the first reading of the
    /// instructions included, before the engine is stopped.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(5);

    private readonly AssistantOptions _options;
    private readonly ILogger<EngineReportSuggester> _log;

    // Not disposed: a request finishing after the screen has closed still
    // releases and checks them. They hold no native resources.
#pragma warning disable CA2213
    private readonly SemaphoreSlim _turn = new(1, 1);
    private readonly CancellationTokenSource _closing = new();
#pragma warning restore CA2213

    private EngineProcess? _engine;
    private ReportPrompt? _prompt;
    private ReportSuggestionContext? _preparedFor;
    private volatile bool _ready;
    private volatile bool _disposed;

    public EngineReportSuggester(PharmaXOptions options, ILogger<EngineReportSuggester> log)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Assistant;
        _log = log;
    }

    public bool IsAvailable =>
        _options.Enabled && AssistantHost.IsInstalled && File.Exists(_options.ResolvedModelPath);

    public bool IsReady => _ready;

    public Task WarmUpAsync(ReportSuggestionContext context, CancellationToken ct = default) =>
        RunAsync(async token =>
        {
            await PrepareAsync(context, token).ConfigureAwait(false);
            return true;
        }, ct);

    public Task<ReportSuggestion> SuggestAsync(string request, ReportSuggestionContext context,
        CancellationToken ct = default) =>
        RunAsync(async token =>
        {
            var prompt = await PrepareAsync(context, token).ConfigureAwait(false);
            var clock = Stopwatch.StartNew();

            var reply = await _engine!.SendAsync(new
            {
                op = "ask",
                turn = prompt.Turn(request),
                grammar = prompt.GrammarFor(request),
                maxTokens = MaxAnswerTokens
            }, token).ConfigureAwait(false);

            var suggestion = prompt.Read(reply.GetProperty("text").GetString() ?? "");

            // The report chosen, not the words typed: a request can name a
            // patient, and the log is no place for that.
            _log.LogInformation("Reports assistant chose {Report} in {Ms} ms",
                suggestion.PresetKey ?? (suggestion.Understood ? "no report" : "nothing readable"),
                clock.ElapsedMilliseconds);

            return suggestion;
        }, ct);

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _closing.Cancel();

        // A request in hand stops its own engine as it is cancelled; with
        // none in hand, stop it here.
        if (_turn.Wait(0))
        {
            try { Stop(); }
            finally { _turn.Release(); }
        }
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
            _log.LogWarning("Reports assistant engine stopped unexpectedly: {Errors}",
                _engine.RecentErrors);
            Stop();
            throw;
        }
        finally
        {
            _turn.Release();
        }
    }

    /// <summary>
    /// Starts the engine if it is not running, and has it read the
    /// instructions for this context, from its saved reading where it has one.
    /// </summary>
    private async Task<ReportPrompt> PrepareAsync(ReportSuggestionContext context,
        CancellationToken ct)
    {
        if (_engine is { HasExited: false } && _prompt is not null
            && ReferenceEquals(_preparedFor, context))
            return _prompt;

        var prompt = new ReportPrompt(context);

        // A grammar the engine cannot parse would kill it; this says why.
        Gbnf.Validate(prompt.Grammar);
        Gbnf.Validate(prompt.NamedPeriodGrammar);

        if (_engine is null || _engine.HasExited)
        {
            Stop();
            _engine = EngineProcess.Start();
        }

        var clock = Stopwatch.StartNew();
        var reply = await _engine.SendAsync(new
        {
            op = "load",
            model = _options.ResolvedModelPath,
            cache = _options.ResolvedCacheDirectory,
            threads = _options.Threads > 0
                ? _options.Threads
                : Math.Max(1, Environment.ProcessorCount / 2),
            instructions = prompt.SystemTurn
        }, ct).ConfigureAwait(false);

        _prompt = prompt;
        _preparedFor = context;
        _ready = true;

        _log.LogInformation(
            "Reports assistant ready in {Ms} ms ({Tokens} tokens of instructions, {Source})",
            clock.ElapsedMilliseconds, reply.GetProperty("tokens").GetInt32(),
            reply.GetProperty("reused").GetBoolean() ? "saved reading" : "read afresh");

        return prompt;
    }

    private void Stop()
    {
        _ready = false;
        _prompt = null;
        _preparedFor = null;
        _engine?.Dispose();
        _engine = null;
    }
}
