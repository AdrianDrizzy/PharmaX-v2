using System.Globalization;
using System.Text;
using PharmaX.Application.Assistant;
using PharmaX.Application.Inventory;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Application.Security;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Assistant;

/// <summary>
/// Ask PharmaX: a question in, an answer out, from the offline model, with
/// the disclaimer under every answer. Open from any screen with
/// <see cref="PhxFormBase.AssistantKey"/>.
/// </summary>
public sealed class AssistantPresenter : PresenterBase<IAssistantView>
{
    private readonly IChatAssistant _chat;
    private readonly IProductService? _products;

    /// The conversation so far, so a follow-up question has its context.
    private readonly List<ChatTurn> _turns = new();

    /// <remarks>
    /// The products are looked up only for someone who may read the
    /// product list: asked of anyone else, the search would be refused
    /// and the refusal written to the audit trail for every question.
    /// </remarks>
    public AssistantPresenter(IChatAssistant chat, IProductService products,
                              IPermissionService permissions, AssistantTasks tasks)
        : this(chat)
    {
        _products = products;
        _permissions = permissions;
        _tasks = tasks;
    }

    /// <remarks>
    /// With the guided jobs: a job that has one is talked through here,
    /// question by question, and saved through its service once the
    /// operator says yes. The others open their screen as before.
    /// </remarks>
    public AssistantPresenter(IChatAssistant chat, IProductService products,
                              IPermissionService permissions, AssistantTasks tasks,
                              Application.Assistant.Guided.IGuidedFlows guided)
        : this(chat, products, permissions, tasks) => _guided = guided;

    /// <remarks>
    /// With files: a question can come with pictures, PDFs, workbooks and
    /// documents attached, and any answer can be saved as one.
    /// </remarks>
    public AssistantPresenter(IChatAssistant chat, IProductService products,
                              IPermissionService permissions, AssistantTasks tasks,
                              Application.Assistant.Guided.IGuidedFlows guided,
                              IAttachmentReader reader, IAnswerWriter writer)
        : this(chat, products, permissions, tasks, guided)
    {
        _reader = reader;
        _writer = writer;
    }

    /// <remarks>
    /// With reports: "sales for March 2026" runs the report here, asking
    /// for the period first where the request named none.
    /// </remarks>
    public AssistantPresenter(IChatAssistant chat, IProductService products,
                              IPermissionService permissions, AssistantTasks tasks,
                              Application.Assistant.Guided.IGuidedFlows guided,
                              IAttachmentReader reader, IAnswerWriter writer, AssistantReports reports)
        : this(chat, products, permissions, tasks, guided, reader, writer) => _reports = reports;

    private readonly IAttachmentReader? _reader;
    private readonly IAnswerWriter? _writer;
    private readonly AssistantReports? _reports;

    /// The last answer's report, saved as the reports screen saves one.
    private Application.Reporting.ReportResult? _report;

    /// A report asked for with no period, waiting for the next answer to name one.
    private string? _reportAsked;

    /// What the last answer was to, as the title of a saved one.
    private string _asked = "";

    private readonly AssistantTasks? _tasks;
    private readonly IPermissionService? _permissions;
    private readonly Application.Assistant.Guided.IGuidedFlows? _guided;

    /// The guided job under way, if any: every answer goes to it until it
    /// finishes or is cancelled.
    private Application.Assistant.Guided.GuidedFlow? _flow;

    public AssistantPresenter(IChatAssistant chat) => _chat = chat;

    protected override void Subscribe()
    {
        View.AskRequested += (_, _) => Enqueue(AskAsync);
        View.CloseRequested += (_, _) => Enqueue(CloseAsync);
        View.OpenRequested += key => _tasks?.OpenScreen(key);
        View.SaveRequested += format => Enqueue(() => GuardAsync(() =>
        {
            View.ShowStatus(Save(format, _asked, View.LastAnswer));
            return Task.CompletedTask;
        }));
    }

    /// <summary>
    /// Saves an answer where the operator says, and says how that went.
    /// </summary>
    private string Save(AnswerFormat format, string asked, IReadOnlyList<AnswerPiece> answer)
    {
        if (_writer is null) return "Answers cannot be saved here.";
        if (answer.Count == 0) return "There is no answer to save yet - ask something first.";

        var title = asked.Length == 0 ? "Ask PharmaX" : asked;
        var name = string.Concat(title.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
        if (name.Length > 60) name = name[..60];

        if (View.AskSavePath(format, name.Trim()) is not { } path) return "Not saved.";

        // A report as the reports screen exports it: every row, the letterhead, the graph.
        if (_report is { } report && _reports is not null && format != AnswerFormat.Word)
            _reports.Save(report, format == AnswerFormat.Pdf, path);
        else
            _writer.WriteTo(title, answer, format, path);

        View.OfferToOpen(path);
        return $"Saved: {path}";
    }

    /// True while a guided job is part way: the window may not just close.
    public bool HasWorkUnderWay => _flow is { IsFinished: false };

    /// <summary>
    /// Another chat on show: what this one said is no longer the context
    /// for a follow-up. False, and nothing forgotten, while a guided job is part way.
    /// </summary>
    public bool StartOver()
    {
        if (HasWorkUnderWay) return false;

        _turns.Clear();
        _asked = "";
        _report = null;
        _reportAsked = null;
        return true;
    }

    /// <summary>
    /// Closes the window, stopping a guided job part way first so an open
    /// sale or a dispensing it began is set aside rather than left behind.
    /// </summary>
    public void RequestClose() => Enqueue(CloseAsync);

    private Task CloseAsync() => GuardAsync(async () =>
    {
        if (_flow is { IsFinished: false } flow)
        {
            _flow = null;
            await flow.CancelAsync();
        }

        View.CloseView(true);
    });

    public override Task InitialiseAsync()
    {
        if (!_chat.IsAvailable)
        {
            // How-to answers come from the guide, which needs no model.
            View.ShowStatus("Questions about how to use PharmaX work. Medicine questions need "
                            + "the model, and it is not installed on this computer.");
            return Task.CompletedTask;
        }

        // Loading the model takes seconds; begun now, it is usually done
        // by the time the question is typed.
        View.ShowStatus("Starting the assistant...");
        _ = WarmUpQuietlyAsync();
        return Task.CompletedTask;
    }

    private async Task WarmUpQuietlyAsync()
    {
        try
        {
            await _chat.WarmUpAsync();
            View.ShowStatus("");
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // The window closed first.
        }
#pragma warning disable CA1031 // Logged; the question itself reports any failure.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ErrorHandler.Log(ex);
            View.ShowStatus("");
        }
    }

    private Task AskAsync() => GuardAsync(async () =>
    {
        var question = View.QuestionText.Trim();

        // Enter on its own is an answer here: "let PharmaX number it".
        if (_flow is { IsFinished: false })
        {
            await AnswerFlowAsync(question);
            return;
        }

        var files = View.AttachedFiles.ToList();
        if (question.Length == 0 && files.Count == 0) return;

        var wanted = AnswerExport.Requested(question);

        // "Save that as a PDF": the last answer, not a new one.
        if (wanted is { } format && files.Count == 0 && AnswerExport.OnlyAsksToSave(question))
        {
            var last = View.LastAnswer.ToList();
            var asked = _asked;

            View.ClearQuestion();
            View.ShowQuestion(question);
            View.BeginAnswer();
            View.AppendAnswer(Save(format, asked, last));
            View.EndAnswer("");
            return;
        }

        View.ClearQuestion();
        View.ShowQuestion(question);
        View.ClearAttachments();
        View.BeginAnswer();
        _report = null;
        _asked = question.Length > 0 ? question : string.Join(", ", files.Select(Path.GetFileName));

        if (files.Count > 0)
        {
            _reportAsked = null;
            await AnswerAboutFilesAsync(question, files);
            if (wanted is { } asFile) View.ShowStatus(Save(asFile, _asked, View.LastAnswer));
            return;
        }

        // "March 2026", answering "which period?" for the report asked for last.
        if (_reportAsked is { } pending)
        {
            _reportAsked = null;

            if (AssistantReports.NamesPeriod(question))
            {
                var request = $"{pending} for {question}";
                _asked = request;
                await AnswerReportAsync(request);
                if (AnswerExport.Requested(request) is { } reportAsFile && _report is not null)
                    View.ShowStatus(Save(reportAsFile, _asked, View.LastAnswer));
                return;
            }
        }

        if (Sums.Answer(question) is { } sum)
        {
            View.AppendAnswer(sum);
            View.EndAnswer("");
            _turns.Add(new ChatTurn(question, sum));
            return;
        }

        // A job rather than a question: talked through here where it has
        // a guided flow; otherwise the screen for it opens, filled in, and
        // this window gets out of its way.
        if (_tasks?.CanRun == true && AppTasks.Read(question) is AppTask task)
        {
            if (_guided?.FlowFor(task, new ViewPrompts(View)) is { } flow)
            {
                _flow = flow;
                View.AppendAnswer(await flow.StartAsync());
                View.EndAnswer("");
                return;
            }

            View.AppendAnswer(AppTasks.Describe(task));
            View.EndAnswer("");
            View.CloseView(true);
            _tasks.Run(task);
            return;
        }

        // A document made on its own screen: a quotation, labels, a receipt template.
        if (_tasks?.CanRun == true && AssistantOpens.ForDocument(question) is { } document)
        {
            View.AppendAnswer($"Opening **{document.Caption}** for you. Fill in what it asks for there, then save "
                              + "or print it - nothing is saved until you do. Ask here if anything on it is unclear.");
            View.ShowOpens(new[] { document });
            View.EndAnswer("");
            _tasks.OpenScreen(document.Key);
            return;
        }

        if (_reports?.IsAvailable == true && AssistantReports.Looks(question) && await AnswerReportAsync(question))
        {
            if (wanted is { } reportAsFile && _report is not null)
                View.ShowStatus(Save(reportAsFile, _asked, View.LastAnswer));
            return;
        }

        // A question about using the program is answered from the guide,
        // word for word, rather than by a model that has never seen it.
        if (HowTo.Answer(question) is { } steps)
        {
            View.NameChat(steps.Replace("\r", "", StringComparison.Ordinal).Split('\n')[0].Trim());
            View.AppendGuide(steps);
            View.ShowOpens(AssistantOpens.In(steps));
            View.EndAnswer("");
            _turns.Add(new ChatTurn(question, steps));
            if (wanted is { } guideAsFile) View.ShowStatus(Save(guideAsFile, _asked, View.LastAnswer));
            return;
        }

        if (!_chat.IsAvailable)
        {
            View.AppendAnswer("That is not a question about how to use PharmaX, and the model "
                              + "that answers the others is not installed on this computer.");
            View.EndAnswer("");
            return;
        }

        View.ShowStatus(_chat.IsReady
            ? "Thinking..."
            : "Starting the assistant - the first answer takes a little longer...");

        // Created here, on the screen's thread, so each piece of the
        // answer is written on it too.
        var writing = new Progress<string>(View.AppendAnswer);

        try
        {
            // "What do we have for a cough?" - the pharmacy's own products
            // first, as the counter would offer them, then the advice.
            var condition = StockForCondition.Condition(question);
            var stock = condition is null ? null : await InStockForAsync(condition);

            if (stock is not null)
            {
                stock += Environment.NewLine + Environment.NewLine;
                View.AppendAnswer(stock);
            }

            // Asked at the counter, so answered as counter advice, in the
            // pharmacy's OTC and dispensary terms.
            var asked = condition is null ? question : StockForCondition.CounterAdvice(question);

            var answer = await _chat.AskAsync(_turns.ToArray(), asked, writing);

            _turns.Add(new ChatTurn(question, (stock ?? "") + answer));
        }
        finally
        {
            View.EndAnswer(ChatDisclaimer.Text);
            View.ShowStatus("");
        }

        if (wanted is { } answerAsFile) View.ShowStatus(Save(answerAsFile, _asked, View.LastAnswer));
    });

    /// <summary>
    /// A report asked for: run and shown here, or the period asked for
    /// where it named none. False where the request is not for a report.
    /// </summary>
    private async Task<bool> AnswerReportAsync(string request)
    {
        View.ShowStatus(_reports!.IsReady
            ? "Finding the report..."
            : "Starting the report assistant - the first report takes a little longer...");

        ReportAnswer answer;
        try
        {
            answer = await _reports.AskAsync(request);
        }
        finally
        {
            View.ShowStatus("");
        }

        var reports = new[] { ("reports", "Reports") };

        switch (answer.Outcome)
        {
            case ReportOutcome.NotAReport:
                return false;

            case ReportOutcome.Unclear:
                View.AppendAnswer("I could not work out which report that is. Say it another way, for example "
                                  + "\"sales for March 2026\", or choose it on Reports.");
                break;

            case ReportOutcome.NeedsPeriod:
                _reportAsked = request;
                View.AppendAnswer($"Which period should **{answer.Name}** cover? For example \"yesterday\", "
                                  + "\"last week\", \"March 2026\" or \"2025\".");
                View.EndAnswer("");
                return true;

            case ReportOutcome.Ran when answer.Result!.RowCount == 0:
                View.AppendAnswer($"**{answer.Name}** has nothing for {answer.Result.Provenance.ParameterSummary}.");
                break;

            default:
                var result = answer.Result!;

                // "Sales Summary | Branch 001 | 01/03/2026 to 31/03/2026 | no filters"
                var parts = result.Provenance.ParameterSummary.Split(" | ");
                View.NameChat(parts.Length > 2 ? $"{answer.Name}, {parts[2]}" : answer.Name);
                View.AppendAnswer($"**{result.Title}**{Environment.NewLine}");
                if (_reports.Chart(result) is { } chart) View.AppendPicture(chart);
                View.AppendAnswer(AssistantReports.Describe(result));
                View.AppendAnswer(Environment.NewLine + "Save answer saves the whole report as a PDF or Excel, "
                                  + "with the letterhead and every row. Reports opens it to change the filters.");
                _report = result;
                _turns.Add(new ChatTurn(request, $"Ran {answer.Name}: {result.Provenance.ParameterSummary}"));
                break;
        }

        View.ShowOpens(reports);
        View.EndAnswer("");
        return true;
    }

    /// <summary>
    /// A question about attached files: each read here, then put to the
    /// model with the question. Without the model, what was read is shown.
    /// </summary>
    private async Task AnswerAboutFilesAsync(string question, IReadOnlyList<string> files)
    {
        if (_reader is null)
        {
            View.AppendAnswer("Attached files cannot be read here.");
            View.EndAnswer("");
            return;
        }

        var read = new List<(string Name, string Text)>();

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            View.ShowStatus($"Reading {name}...");

            try
            {
                read.Add((name, await _reader.ReadAsync(file)));
            }
#pragma warning disable CA1031 // A file that will not open is said so; the others are still read.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                ErrorHandler.Log(ex);
                View.AppendAnswer($"{name} could not be read: {ex.Message}{Environment.NewLine}{Environment.NewLine}");
            }
        }

        if (read.Count == 0)
        {
            View.EndAnswer("");
            View.ShowStatus("");
            return;
        }

        if (!_chat.IsAvailable)
        {
            View.AppendAnswer("The model that answers questions about a file is not installed on this computer. "
                              + "This is what the file says:" + Environment.NewLine + Environment.NewLine);
            foreach (var (name, text) in read)
                View.AppendAnswer($"**{name}**{Environment.NewLine}{text.Trim()}{Environment.NewLine}{Environment.NewLine}");
            View.EndAnswer("");
            View.ShowStatus("");
            return;
        }

        View.ShowStatus(_chat.IsReady ? "Thinking..." : "Starting the assistant - the first answer takes a little longer...");

        try
        {
            var answer = await _chat.AskAsync(_turns.ToArray(), Attachments.Question(question, read),
                                              new Progress<string>(View.AppendAnswer));

            // The files' text is not kept: a follow-up would not fit beside it.
            _turns.Add(new ChatTurn($"{question} (attached: {string.Join(", ", read.Select(r => r.Name))})", answer));
        }
        finally
        {
            View.EndAnswer(ChatDisclaimer.Text);
            View.ShowStatus("");
        }
    }

    private async Task AnswerFlowAsync(string answer)
    {
        var flow = _flow!;

        View.ClearQuestion();
        View.ShowQuestion(answer.Length == 0 ? "(Enter)" : answer);
        View.BeginAnswer();
        _report = null;

        try
        {
            View.AppendAnswer(await flow.ReplyAsync(answer));
        }
        finally
        {
            View.EndAnswer("");
        }

        if (!flow.IsFinished) return;

        _flow = null;

        if (flow.Result is not { } result || _tasks is null) return;

        // A product or patient file opens on its own screen once this
        // window is out of the way. The rest stay in the conversation,
        // which says what was done; a sale's receipt prints from the shell.
        if (result.Kind is AppTaskKind.NewProduct or AppTaskKind.NewPatient)
            View.CloseView(true);

        _tasks.Open(result);
    }

    /// <summary>
    /// A second person's authority, on the sign-in box the till and the
    /// dispensary use - never typed into the conversation.
    /// </summary>
    private sealed class ViewPrompts : Application.Assistant.Guided.IGuidedPrompts
    {
        private readonly IAssistantView _view;

        public ViewPrompts(IAssistantView view) => _view = view;

        public Task<Guid?> AuthoriseAsync(string permissionCode, string reason, CancellationToken ct = default) =>
            Task.FromResult(_view.PromptAuthorisation(reason, permissionCode));
    }

    /// Most products listed under an answer.
    private const int MostProducts = 15;

    /// Most products listed for any one ingredient or search word.
    private const int PerIngredient = 3;

    /// <summary>
    /// What this pharmacy has on the shelf for a condition, as text to put
    /// under the answer; null where it cannot look.
    /// </summary>
    /// <remarks>
    /// The model names the ingredients; the products, quantities and
    /// prices come from the product list, active and in stock only.
    /// </remarks>
    private async Task<string?> InStockForAsync(string condition)
    {
        if (_products is null
            || _permissions?.Has(Domain.Security.PermissionCode.ProductRead) != true)
            return null;

        View.ShowStatus($"Checking what we stock for {condition}...");

        var named = await _chat.AskAsync(Array.Empty<ChatTurn>(),
            StockForCondition.IngredientsQuestion(condition), Silent.Instance);

        var found = new List<ProductListItemDto>();

        foreach (var term in StockForCondition.SearchTerms(condition, named))
        {
            var page = await _products.SearchAsync(term, take: 20, includeInactive: false);
            if (!page.IsSuccess) return null;

            // A few of each, so twenty paracetamols do not crowd out
            // everything else the model named: in stock first, then one
            // the pharmacy lists but has run out of.
            found.AddRange(page.Value.Items
                .Where(p => found.All(f => f.Id != p.Id))
                .OrderByDescending(p => p.QuantityOnHand > 0m)
                .ThenByDescending(p => p.QuantityOnHand)
                .Take(PerIngredient));
            if (found.Count >= MostProducts) break;
        }

        View.ShowStatus("Thinking...");

        return StockForCondition.Listing(condition, found.Take(MostProducts).ToList(), MostProducts);
    }

    private sealed class Silent : IProgress<string>
    {
        public static Silent Instance { get; } = new();
        public void Report(string value) { }
    }
}
