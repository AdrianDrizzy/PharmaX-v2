namespace PharmaX.Application.Assistant.Guided;

/// <summary>
/// What a guided job left behind once it finished: the record it saved,
/// for the shell to open. Null where nothing was saved.
/// </summary>
public sealed record GuidedResult(AppTaskKind Kind, Guid Id, string Reference);

/// <summary>
/// What an answer did to the conversation.
/// </summary>
public abstract record Reply
{
    private Reply() { }

    /// Taken. The note, if any, is said before the next question.
    public sealed record Taken(string? Note = null) : Reply;

    /// Refused: the reason, then the same question again.
    public sealed record Again(string Why) : Reply;

    /// A yes or no that has to be answered before anything moves on: a
    /// duplicate, an allergy, a warning that needs saying yes to.
    public sealed record Check(string Question, Func<CancellationToken, Task<Reply>> OnYes,
                               Func<CancellationToken, Task<Reply>> OnNo) : Reply;

    /// Back or on to another question, by its key.
    public sealed record Jump(string Key, string? Note = null, bool Change = false) : Reply;

    /// The end: saved, or not.
    public sealed record Finished(string Message) : Reply;

    /// <summary>
    /// Another flow takes the conversation - a new patient from inside
    /// dispensing - and this one carries on with <paramref name="Done"/>
    /// once it finishes.
    /// </summary>
    public sealed record Handover(GuidedFlow Child, Func<GuidedFlow, CancellationToken, Task<Reply>> Done) : Reply;

    public static Reply Ok(string? note = null) => new Taken(note);
    public static Reply Retry(string why) => new Again(why);
    public static Reply Finish(string message) => new Finished(message);

    public static Reply YesNo(string question, Func<CancellationToken, Task<Reply>> onYes,
                             Func<CancellationToken, Task<Reply>> onNo) =>
        new Check(question, onYes, onNo);

    /// A yes or no where only yes needs doing anything.
    public static Reply YesNo(string question, Func<CancellationToken, Task<Reply>> onYes,
                             string? whenNo = null) =>
        new Check(question, onYes, _ => Task.FromResult<Reply>(new Again(whenNo ?? "Then let's try that again.")));
}

/// <summary>
/// One question of a guided job.
/// </summary>
/// <param name="Key">What it is called, for "change the price" at the end.</param>
/// <param name="Prompt">The question, worked out when it is reached so it can
/// offer what the answers so far suggest.</param>
/// <param name="Answer">Reads the answer; an empty answer is pressing Enter.</param>
/// <param name="Applies">False skips it: no batch number for no stock.</param>
public sealed record Question(
    string Key,
    Func<CancellationToken, Task<string>> Prompt,
    Func<string, CancellationToken, Task<Reply>> Answer,
    Func<bool>? Applies = null);

/// <summary>
/// A job done in conversation: questions asked one at a time in a fixed
/// order, each answer checked, then a summary and a yes before anything
/// is saved.
/// </summary>
/// <remarks>
/// The flows never write to the database. They gather the answers into
/// the same edit the screen would send and hand it to the same service,
/// so every permission and domain rule behind the screen still holds;
/// a refusal from the service is said in the chat and the question that
/// caused it asked again.
/// </remarks>
public abstract class GuidedFlow
{
    /// The key of the closing "Save it?" question every flow ends with.
    public const string ConfirmKey = "confirm";

    private readonly List<Question> _questions = new();
    private int _at = -1;
    private Reply.Check? _pending;

    /// Set while a single answer is being changed from the summary, so the
    /// flow goes straight back to the summary rather than asking the rest
    /// again.
    private bool _changing;

    public bool IsFinished { get; private set; }

    /// What was saved, once it was.
    public GuidedResult? Result { get; protected set; }

    /// The key of the question waiting for an answer, for the tests.
    public string? Waiting => _child is not null ? "child:" + _child.Waiting
        : _pending is not null ? "check"
        : _at >= 0 && _at < _questions.Count ? _questions[_at].Key : null;

    /// Where a single changed answer goes back to: the summary, or the
    /// checks just before it, which the change may now trip.
    protected virtual string ResumeKey => ConfirmKey;

    /// The first thing said: what is being done and what was understood.
    protected abstract Task<string> IntroAsync(CancellationToken ct);

    protected void Add(string key, Func<CancellationToken, Task<string>> prompt,
                       Func<string, CancellationToken, Task<Reply>> answer, Func<bool>? applies = null) =>
        _questions.Add(new Question(key, prompt, answer, applies));

    protected void Add(string key, Func<string> prompt, Func<string, CancellationToken, Task<Reply>> answer,
                       Func<bool>? applies = null) =>
        Add(key, _ => Task.FromResult(prompt()), answer, applies);

    protected void Add(string key, Func<CancellationToken, Task<string>> prompt, Func<string, Reply> answer,
                       Func<bool>? applies = null) =>
        Add(key, prompt, (a, _) => Task.FromResult(answer(a)), applies);

    protected void Add(string key, Func<string> prompt, Func<string, Reply> answer, Func<bool>? applies = null) =>
        Add(key, _ => Task.FromResult(prompt()), (a, _) => Task.FromResult(answer(a)), applies);

    /// <summary>
    /// The closing question: the summary, then "Save it?". Yes runs
    /// <paramref name="save"/>; no stops with nothing saved; "change X"
    /// asks the question for X again and comes back here.
    /// </summary>
    /// <param name="changeable">False where answers repeat (one per line
    /// of a sale), so "change X" could not say which one: no then stops.</param>
    protected void AddConfirm(Func<CancellationToken, Task<string>> summary, string ask,
                              Func<CancellationToken, Task<Reply>> save, Func<bool>? applies = null,
                              bool changeable = true) =>
        Add(ConfirmKey, async ct => await summary(ct) + Environment.NewLine + Environment.NewLine + ask,
            async (answer, ct) =>
            {
                if (Answers.IsYes(answer)) return await save(ct);
                if (Answers.IsNo(answer))
                    return Reply.Finish(NothingDone);
                if (changeable && Changing(answer) is string key) return new Reply.Jump(key, Change: true);

                return Reply.Retry(changeable
                    ? "Say yes to go ahead, no to stop, or \"change\" and what to change, e.g. \"change the price\"."
                    : "Say yes to go ahead, or no to stop.");
            }, applies);

    /// What is said when the operator stops before anything was done.
    protected virtual string NothingDone => "Nothing was saved.";

    /// <summary>
    /// Undoes whatever the flow had to start before the summary - an open
    /// sale, a dispensing under way - when it ends without being done.
    /// </summary>
    protected virtual Task AbandonAsync(CancellationToken ct) => Task.CompletedTask;

    /// Which question "change the price" means, by any word of its key.
    private string? Changing(string answer)
    {
        var words = Answers.Words(answer);
        if (words.Count < 2 || words[0] is not ("change" or "edit" or "fix")) return null;

        return _questions
            .Where(q => q.Key != ConfirmKey && q.Applies?.Invoke() != false)
            .FirstOrDefault(q => words.Skip(1).Any(w => w.Length > 2
                && q.Key.Split('-').Any(k => k.StartsWith(w, StringComparison.OrdinalIgnoreCase))))?.Key;
    }

    /// <summary>
    /// Stops it where it stands, as "cancel" does: the window was closed
    /// part way, so whatever the flow had begun is undone.
    /// </summary>
    public Task<string> CancelAsync(CancellationToken ct = default) =>
        IsFinished ? Task.FromResult("") : ReplyAsync("cancel", ct);
    public async Task<string> StartAsync(CancellationToken ct = default)
    {
        var intro = await IntroAsync(ct);
        return Join(intro, await MoveAsync(0, ct));
    }

    /// <summary>
    /// The operator's answer to whatever was asked last, and what to say
    /// back: a note, the next question, or the end.
    /// </summary>
    public async Task<string> ReplyAsync(string answer, CancellationToken ct = default)
    {
        if (IsFinished) return "";

        answer = (answer ?? "").Trim();

        if (Answers.IsCancel(answer))
        {
            IsFinished = true;
            if (Result is not null) return "Stopped.";

            await AbandonAsync(ct);
            return "Stopped. " + NothingDone;
        }

        if (_child is { } child)
        {
            var said = await child.ReplyAsync(answer, ct);
            if (!child.IsFinished) return said;

            _child = null;
            return Join(said, await ApplyAsync(await _childDone!(child, ct), ct));
        }

        if (_pending is { } check)
        {
            if (Answers.IsYes(answer))
            {
                _pending = null;
                return await ApplyAsync(await check.OnYes(ct), ct);
            }

            if (Answers.IsNo(answer))
            {
                _pending = null;
                return await ApplyAsync(await check.OnNo(ct), ct);
            }

            return "Please answer yes or no." + Environment.NewLine + check.Question;
        }

        if (_at < 0 || _at >= _questions.Count) return "";

        return await ApplyAsync(await _questions[_at].Answer(answer, ct), ct);
    }

    private async Task<string> ApplyAsync(Reply reply, CancellationToken ct)
    {
        switch (reply)
        {
            case Reply.Taken next:
                if (_changing && _questions[_at].Key != ConfirmKey)
                {
                    _changing = false;
                    return Join(next.Note, await MoveAsync(IndexOf(ResumeKey), ct));
                }

                return Join(next.Note, await MoveAsync(_at + 1, ct));

            case Reply.Again again:
                return Said(Join(again.Why, await _questions[_at].Prompt(ct)));

            case Reply.Check check:
                _pending = check;
                return check.Question;

            case Reply.Jump go:
                if (go.Change) _changing = true;
                return Join(go.Note, await MoveAsync(IndexOf(go.Key), ct));

            case Reply.Handover handover:
                _child = handover.Child;
                _childDone = handover.Done;
                return Said(await handover.Child.StartAsync(ct));

            case Reply.Finished done:
                IsFinished = true;
                if (Result is null) await AbandonAsync(ct);
                return Said(done.Message);

            default:
                throw new InvalidOperationException($"Unknown reply {reply}.");
        }
    }

    private int IndexOf(string key)
    {
        var index = _questions.FindIndex(q => q.Key == key);
        return index >= 0 ? index : throw new InvalidOperationException($"No question {key}.");
    }

    /// <summary>
    /// Asks the first question from <paramref name="from"/> on that
    /// applies. A prompt that comes back empty had nothing to ask - a
    /// duplicate check that found none - and is passed over too.
    /// </summary>
    private async Task<string> MoveAsync(int from, CancellationToken ct)
    {
        for (_at = from; _at < _questions.Count; _at++)
        {
            if (_questions[_at].Applies?.Invoke() == false) continue;

            var prompt = await _questions[_at].Prompt(ct);
            if (prompt.Length > 0) return Said(prompt);
        }

        IsFinished = true;
        return Said("");
    }

    private readonly List<string> _said = new();
    private GuidedFlow? _child;
    private Func<GuidedFlow, CancellationToken, Task<Reply>>? _childDone;

    /// <summary>
    /// Something to say before the next question shown, from work done
    /// where no question was needed: one match chosen, a line rung up.
    /// </summary>
    protected void Say(string? note)
    {
        if (!string.IsNullOrWhiteSpace(note)) _said.Add(note);
    }

    private string Said(string text)
    {
        var all = Join(_said.Append(text).ToArray());
        _said.Clear();
        return all;
    }

    protected static string Join(params string?[] parts) =>
        string.Join(Environment.NewLine, parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}
