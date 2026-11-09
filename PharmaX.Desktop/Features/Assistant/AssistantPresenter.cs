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
    }

    /// True while a guided job is part way: the window may not just close.
    public bool HasWorkUnderWay => _flow is { IsFinished: false };

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

        if (question.Length == 0) return;

        View.ClearQuestion();
        View.ShowQuestion(question);
        View.BeginAnswer();

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

        // A question about using the program is answered from the guide,
        // word for word, rather than by a model that has never seen it.
        if (HowTo.Answer(question) is { } steps)
        {
            View.AppendAnswer(steps);
            View.ShowOpens(AssistantOpens.In(steps));
            View.EndAnswer("");
            _turns.Add(new ChatTurn(question, steps));
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
    });

    private async Task AnswerFlowAsync(string answer)
    {
        var flow = _flow!;

        View.ClearQuestion();
        View.ShowQuestion(answer.Length == 0 ? "(Enter)" : answer);
        View.BeginAnswer();

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
