using FluentAssertions;
using NSubstitute;
using PharmaX.Application.Assistant;
using PharmaX.Desktop.Features.Assistant;

namespace PharmaX.Tests.Unit.Presenters;

/// <summary>
/// Ask PharmaX answers whatever is asked, and every answer carries the
/// disclaimer.
/// </summary>
public class AssistantPresenterTests
{
    private readonly IChatAssistant _chat = Substitute.For<IChatAssistant>();
    private readonly IAssistantView _view = Substitute.For<IAssistantView>();

    public AssistantPresenterTests()
    {
        _chat.IsAvailable.Returns(true);
        _chat.IsReady.Returns(true);
    }

    [Fact]
    public async Task Every_answer_ends_with_the_disclaimer()
    {
        Answers("Common side effects of amlodipine include ankle swelling.");
        _view.QuestionText.Returns("What are the side effects of amlodipine?");

        await OpenAsync();
        _view.AskRequested += Raise.EventWith(this, EventArgs.Empty);

        Received.InOrder(() =>
        {
            _view.ShowQuestion("What are the side effects of amlodipine?");
            _view.BeginAnswer();
            _view.EndAnswer(ChatDisclaimer.Text);
        });
        ChatDisclaimer.Text.Should().Contain("can be wrong");
    }

    [Fact]
    public async Task A_failed_answer_still_gets_the_disclaimer()
    {
        _chat.AskAsync(Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(),
                Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<string>(new InvalidOperationException("The assistant stopped unexpectedly.")));
        _view.QuestionText.Returns("Anything");

        await OpenAsync();
        _view.AskRequested += Raise.EventWith(this, EventArgs.Empty);

        _view.Received().EndAnswer(ChatDisclaimer.Text);
        _view.Received().ShowError(Arg.Any<string>());
    }

    [Fact]
    public async Task A_follow_up_question_carries_the_conversation_so_far()
    {
        Answers("Paracetamol is an analgesic.");
        _view.QuestionText.Returns("What is paracetamol?", "And the usual adult dose?");

        await OpenAsync();
        _view.AskRequested += Raise.EventWith(this, EventArgs.Empty);
        _view.AskRequested += Raise.EventWith(this, EventArgs.Empty);

        await _chat.Received().AskAsync(
            Arg.Is<IReadOnlyList<ChatTurn>>(t => t.Count == 1 && t[0].Question == "What is paracetamol?"),
            "And the usual adult dose?", Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Without_a_model_a_how_to_question_is_still_answered()
    {
        _chat.IsAvailable.Returns(false);
        _view.QuestionText.Returns("How do I cash up?");

        await OpenAsync();
        _view.AskRequested += Raise.EventWith(this, EventArgs.Empty);

        _view.DidNotReceive().ShowUnavailable(Arg.Any<string>());
        await _chat.DidNotReceive().AskAsync(Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(),
            Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
        _view.Received().AppendAnswer(Arg.Is<string>(a => a.Contains("Count cash and close", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task An_empty_question_is_not_sent()
    {
        _view.QuestionText.Returns("   ");

        await OpenAsync();
        _view.AskRequested += Raise.EventWith(this, EventArgs.Empty);

        await _chat.DidNotReceive().AskAsync(Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(),
            Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("What do we have for a headache?", "headache")]
    [InlineData("suggest something for hay fever", "hay fever")]
    [InlineData("Which products do we stock for diarrhoea", "diarrhoea")]
    [InlineData("What are the side effects of amlodipine?", null)]
    public void A_question_about_a_condition_is_recognised(string question, string? condition) =>
        StockForCondition.Condition(question).Should().Be(condition);

    [Fact]
    public void Another_name_for_a_medicine_also_searches_the_shelf_name() =>
        StockForCondition.SearchTerms("fever", "Paracetamol (Acetaminophen), Acetaminophen, albuterol")
            .Should().Equal("fever", "paracetamol", "acetaminophen", "albuterol", "salbutamol");

    [Fact]
    public void The_listing_puts_OTC_before_the_dispensary_and_out_of_stock_last()
    {
        var listing = StockForCondition.Listing("cough", new[]
        {
            Product("AMOX", "Amoxicillin 500mg", 20m) with { Schedule = 3, PackSize = "CAP 21" },
            Product("4CS", "4CS Cough Syrup", 10m) with { PackSize = "SYR 100" },
            Product("OLD", "Old linctus", 0m)
        }, 15);

        listing.Should().Be(string.Join(Environment.NewLine,
            "In the product list for cough:",
            "OTC (over the counter):",
            "- 4CS Cough Syrup (4CS): 10 on hand, 25.00 for SYR 100",
            "Dispensary (prescription, pharmacist only):",
            "- Amoxicillin 500mg (AMOX): 20 on hand, 25.00 for CAP 21",
            "On file but out of stock:",
            "- Old linctus (OLD): 0 on hand, 25.00"));
    }

    [Fact]
    public void The_search_words_are_the_condition_and_each_named_ingredient() =>
        StockForCondition.SearchTerms("headache", "1. Paracetamol, ibuprofen,\naspirin.")
            .Should().Equal("headache", "paracetamol", "ibuprofen", "aspirin");

    [Fact]
    public async Task The_pharmacys_own_products_come_before_the_advice()
    {
        var products = Substitute.For<PharmaX.Application.Inventory.IProductService>();
        var permissions = Substitute.For<PharmaX.Application.Security.IPermissionService>();
        permissions.Check(Arg.Any<string>()).Returns(PharmaX.Application.Security.PermissionOutcome.Granted);

        _chat.AskAsync(Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Is<string>(q => q.StartsWith("List the", StringComparison.Ordinal)),
                Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>())
            .Returns("paracetamol");
        _chat.AskAsync(Arg.Any<IReadOnlyList<ChatTurn>>(),
                Arg.Is<string>(q => q.StartsWith("What do we have for a headache?", StringComparison.Ordinal)
                                    && q.Contains("OTC", StringComparison.Ordinal)
                                    && q.Contains("Dispensary", StringComparison.Ordinal)
                                    && q.Contains("Paracetamol (Acetaminophen)", StringComparison.Ordinal)),
                Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>())
            .Returns("Rest and fluids help.");
        products.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(ci => PharmaX.Application.Results.Result<PharmaX.Application.Inventory.Dtos.ProductPageDto>.Ok(new(
                (string)ci[0] == "paracetamol"
                    ? new[]
                    {
                        Product("PAN500", "Panado 500mg", 40m),
                        Product("PARA0", "Paracetamol syrup", 0m)
                    }
                    : Array.Empty<PharmaX.Application.Inventory.Dtos.ProductListItemDto>(), 2)));
        _view.QuestionText.Returns("What do we have for a headache?");

        var presenter = new AssistantPresenter(_chat, products, permissions, new AssistantTasks());
        presenter.Attach(_view);
        await presenter.InitialiseAsync();
        _view.AskRequested += Raise.EventWith(this, EventArgs.Empty);

        // Listed first - on the shelf, then on file but out of stock - and
        // only then is the model asked for its advice.
        Received.InOrder(() =>
        {
            _view.AppendAnswer(Arg.Is<string>(s =>
                s.Contains("OTC (over the counter):", StringComparison.Ordinal)
                && s.Contains("Panado 500mg (PAN500): 40 on hand", StringComparison.Ordinal)
                && s.IndexOf("On file but out of stock:", StringComparison.Ordinal)
                   < s.IndexOf("Paracetamol syrup", StringComparison.Ordinal)));
            _chat.AskAsync(Arg.Any<IReadOnlyList<ChatTurn>>(),
                Arg.Is<string>(q => q.StartsWith("What do we have for a headache?", StringComparison.Ordinal)),
                Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
        });

        // The conversation keeps the question as typed, not the counter-advice wording.
        _view.Received().ShowQuestion("What do we have for a headache?");
    }

    [Fact]
    public async Task One_ingredient_does_not_crowd_out_the_others()
    {
        var products = Substitute.For<PharmaX.Application.Inventory.IProductService>();
        var permissions = Substitute.For<PharmaX.Application.Security.IPermissionService>();
        permissions.Check(Arg.Any<string>()).Returns(PharmaX.Application.Security.PermissionOutcome.Granted);

        _chat.AskAsync(Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Is<string>(q => q.StartsWith("List the", StringComparison.Ordinal)),
                Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>())
            .Returns("paracetamol, aspirin");
        products.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(ci => PharmaX.Application.Results.Result<PharmaX.Application.Inventory.Dtos.ProductPageDto>.Ok(new(
                (string)ci[0] switch
                {
                    "paracetamol" => Enumerable.Range(1, 10).Select(i => Product($"P{i}", $"Paracetamol {i}", i)).ToArray(),
                    "aspirin" => new[] { Product("ASP", "Aspirin 300mg", 5m) },
                    _ => Array.Empty<PharmaX.Application.Inventory.Dtos.ProductListItemDto>()
                }, 10)));
        _view.QuestionText.Returns("What do we have for a headache?");

        var presenter = new AssistantPresenter(_chat, products, permissions, new AssistantTasks());
        presenter.Attach(_view);
        await presenter.InitialiseAsync();
        _view.AskRequested += Raise.EventWith(this, EventArgs.Empty);

        _view.Received().AppendAnswer(Arg.Is<string>(s =>
            s.Contains("Aspirin 300mg", StringComparison.Ordinal)
            && Lines(s, "Paracetamol") == 3));
    }

    private static int Lines(string text, string word)
    {
        return text.Split('\n').Count(l => l.Contains(word, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Sell 2 panado", AppTaskKind.Sell, "panado", 2, null)]
    [InlineData("create a sale for 3 x ibuprofen 400mg to John Moyo", AppTaskKind.Sell, "ibuprofen 400mg", 3, "John Moyo")]
    [InlineData("Add a new product called Vitamin C 1000mg", AppTaskKind.NewProduct, "Vitamin C 1000mg", null, null)]
    [InlineData("please add a patient called Mary Banda", AppTaskKind.NewPatient, null, null, "Mary Banda")]
    [InlineData("Receive stock from Pharmanova", AppTaskKind.ReceiveStock, null, null, "Pharmanova")]
    [InlineData("create a purchase order for 20 panado", AppTaskKind.PurchaseOrder, "panado", 20, null)]
    [InlineData("Dispense amoxicillin 500mg for John Moyo", AppTaskKind.Dispense, "amoxicillin 500mg", null, "John Moyo")]
    public void A_job_is_read_from_plain_words(string request, AppTaskKind kind, string? item,
                                               int? quantity, string? person) =>
        AppTasks.Read(request).Should().Be(new AppTask(kind, item, quantity, person));

    [Theory]
    [InlineData("What do we have for a headache?")]
    [InlineData("What are the side effects of amlodipine?")]
    public void A_question_is_not_a_job(string question) =>
        AppTasks.Read(question).Should().BeNull();

    [Fact]
    public async Task A_job_opens_its_screen_instead_of_asking_the_model()
    {
        var tasks = new AssistantTasks();
        AppTask? ran = null;
        tasks.HandleWith(t => ran = t);
        _view.QuestionText.Returns("Sell 2 panado");

        var presenter = new AssistantPresenter(_chat, Substitute.For<PharmaX.Application.Inventory.IProductService>(),
            Substitute.For<PharmaX.Application.Security.IPermissionService>(), tasks);
        presenter.Attach(_view);
        await presenter.InitialiseAsync();
        _view.AskRequested += Raise.EventWith(this, EventArgs.Empty);

        ran.Should().Be(new AppTask(AppTaskKind.Sell, "panado", 2m));
        _view.Received().CloseView(true);
        await _chat.DidNotReceive().AskAsync(Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(),
            Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
    }

    /// A flow that asks one question and saves on any answer, Enter included.
    private sealed class OneQuestion : PharmaX.Application.Assistant.Guided.GuidedFlow
    {
        public static readonly Guid Saved = Guid.NewGuid();

        public bool Abandoned { get; private set; }

        public OneQuestion(AppTaskKind kind = AppTaskKind.NewProduct) =>
            Add("code", () => "Product code?", answer =>
            {
                Result = new PharmaX.Application.Assistant.Guided.GuidedResult(kind, Saved, answer);
                return PharmaX.Application.Assistant.Guided.Reply.Finish("Saved.");
            });

        protected override Task<string> IntroAsync(CancellationToken ct) => Task.FromResult("New product.");

        protected override Task AbandonAsync(CancellationToken ct)
        {
            Abandoned = true;
            return Task.CompletedTask;
        }
    }

    private (AssistantPresenter Presenter, AssistantTasks Tasks) Guided(OneQuestion flow, params string[] said)
    {
        var tasks = new AssistantTasks();
        tasks.HandleWith(_ => { });

        var guided = Substitute.For<PharmaX.Application.Assistant.Guided.IGuidedFlows>();
        guided.FlowFor(Arg.Any<AppTask>(), Arg.Any<PharmaX.Application.Assistant.Guided.IGuidedPrompts?>())
            .Returns(flow);
        _view.QuestionText.Returns(said[0], said[1..]);

        var presenter = new AssistantPresenter(_chat, Substitute.For<PharmaX.Application.Inventory.IProductService>(),
            Substitute.For<PharmaX.Application.Security.IPermissionService>(), tasks, guided);
        presenter.Attach(_view);
        return (presenter, tasks);
    }

    [Fact]
    public async Task A_finished_sale_stays_in_the_conversation_and_prints_from_the_shell()
    {
        var (presenter, tasks) = Guided(new OneQuestion(AppTaskKind.Sell), "Sell 2 panado", "yes");
        PharmaX.Application.Assistant.Guided.GuidedResult? opened = null;
        tasks.OpenWith(r => opened = r);
        await presenter.InitialiseAsync();

        _view.AskRequested += Raise.EventWith(this, EventArgs.Empty);
        _view.AskRequested += Raise.EventWith(this, EventArgs.Empty);

        opened!.Kind.Should().Be(AppTaskKind.Sell);
        _view.DidNotReceive().CloseView(Arg.Any<bool>());
    }

    [Fact]
    public async Task Closing_part_way_stops_the_job_and_undoes_what_it_began()
    {
        var flow = new OneQuestion(AppTaskKind.Sell);
        var (presenter, _) = Guided(flow, "Sell 2 panado");
        await presenter.InitialiseAsync();

        _view.AskRequested += Raise.EventWith(this, EventArgs.Empty);
        presenter.HasWorkUnderWay.Should().BeTrue();

        _view.CloseRequested += Raise.EventWith(this, EventArgs.Empty);

        flow.Abandoned.Should().BeTrue();
        presenter.HasWorkUnderWay.Should().BeFalse();
        _view.Received().CloseView(true);
    }

    [Fact]
    public async Task A_guided_job_is_talked_through_here_and_opens_what_it_saved()
    {
        var tasks = new AssistantTasks();
        AppTask? ran = null;
        PharmaX.Application.Assistant.Guided.GuidedResult? opened = null;
        tasks.HandleWith(t => ran = t);
        tasks.OpenWith(r => opened = r);

        var guided = Substitute.For<PharmaX.Application.Assistant.Guided.IGuidedFlows>();
        guided.FlowFor(Arg.Any<AppTask>(), Arg.Any<PharmaX.Application.Assistant.Guided.IGuidedPrompts?>())
            .Returns(new OneQuestion());
        _view.QuestionText.Returns("Add a new product called Vitamin C", "");

        var presenter = new AssistantPresenter(_chat, Substitute.For<PharmaX.Application.Inventory.IProductService>(),
            Substitute.For<PharmaX.Application.Security.IPermissionService>(), tasks, guided);
        presenter.Attach(_view);
        await presenter.InitialiseAsync();

        _view.AskRequested += Raise.EventWith(this, EventArgs.Empty);
        _view.Received().AppendAnswer(Arg.Is<string>(s => s.Contains("Product code?", StringComparison.Ordinal)));
        _view.DidNotReceive().CloseView(Arg.Any<bool>());

        // Enter on its own answers the question rather than being ignored.
        _view.AskRequested += Raise.EventWith(this, EventArgs.Empty);

        _view.Received().ShowQuestion("(Enter)");
        opened.Should().Be(new PharmaX.Application.Assistant.Guided.GuidedResult(AppTaskKind.NewProduct, OneQuestion.Saved, ""));
        _view.Received().CloseView(true);
        ran.Should().BeNull();
        await _chat.DidNotReceive().AskAsync(Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(),
            Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
    }

    private static PharmaX.Application.Inventory.Dtos.ProductListItemDto Product(string code, string name, decimal onHand) =>
        new(Guid.NewGuid(), code, name, 25m, onHand, 0, false, true, false);

    private void Answers(string answer) =>
        _chat.AskAsync(Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(),
                Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>())
            .Returns(answer);

    private async Task OpenAsync()
    {
        var presenter = new AssistantPresenter(_chat);
        presenter.Attach(_view);
        await presenter.InitialiseAsync();
    }
}
