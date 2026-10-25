using FluentAssertions;
using NSubstitute;
using PharmaX.Application.Reporting;
using PharmaX.Application.Security;
using PharmaX.Desktop.Features.Reports;
using PharmaX.Domain.Security;

namespace PharmaX.Tests.Unit.Presenters;

/// <summary>
/// The Ask box suggests and the operator confirms: a request fills the
/// screen in, and nothing runs until Preview.
/// </summary>
public class ReportsPresenterTests
{
    private readonly IReportRunner _runner = Substitute.For<IReportRunner>();
    private readonly IPresetCatalogue _catalogue = Substitute.For<IPresetCatalogue>();
    private readonly IReportLookups _lookups = Substitute.For<IReportLookups>();
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly IReportSuggester _suggester = Substitute.For<IReportSuggester>();
    private readonly IReportsView _view = Substitute.For<IReportsView>();

    private static readonly ReportPresetDto Expiring = new("inv.stock.expiring",
        "Stock Expiring", "Inventory", PermissionCode.ReportOperational);
    private static readonly ReportPresetDto Takings = new("pos.sales.daily",
        "Daily Sales", "Point of sale", PermissionCode.ReportManagement);
    private static readonly SlicerDto WithinDays = new("withinDays",
        "Expiring within (days)", "number", Array.Empty<SlicerOptionDto>());

    public ReportsPresenterTests()
    {
        _catalogue.All().Returns(new[] { Expiring, Takings });
        _catalogue.SlicersFor(Expiring.Key).Returns(new[] { WithinDays });
        _catalogue.SlicersFor(Takings.Key).Returns(Array.Empty<SlicerDto>());

        // An operator, not a manager: may see stock, not the takings.
        _permissions.Check(Arg.Any<string>()).Returns(PermissionOutcome.Denied);
        _permissions.Check(PermissionCode.ReportOperational).Returns(PermissionOutcome.Granted);

        _suggester.IsAvailable.Returns(true);
        _suggester.IsReady.Returns(true);
    }

    [Fact]
    public async Task Without_a_model_there_is_no_Ask_box()
    {
        _suggester.IsAvailable.Returns(false);

        await OpenAsync();

        _view.Received().ShowAsk(false);
    }

    [Fact]
    public async Task A_request_fills_the_screen_in_and_runs_nothing()
    {
        _view.AskText.Returns("stock expiring in the next 3 months");
        _suggester.SuggestAsync(Arg.Any<string>(), Arg.Any<ReportSuggestionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(new ReportSuggestion(Expiring.Key, null, null,
                new Dictionary<string, string> { ["withinDays"] = "90" }));
        _view.When(v => v.SelectPreset(Expiring.Key))
             .Do(_ => _view.SelectedPresetKey.Returns(Expiring.Key));

        await OpenAsync();
        _view.AskRequested += Raise.EventWith(this, EventArgs.Empty);

        _view.Received().SelectPreset(Expiring.Key);
        _view.Received().ShowSlicers(Arg.Is<IReadOnlyList<SlicerDto>>(s =>
            s.Count == 1 && s[0].Key == "withinDays"));
        _view.Received().ApplySlicerValues(Arg.Is<IReadOnlyDictionary<string, string>>(v =>
            v["withinDays"] == "90"));
        _view.Received().ShowAskResult(Arg.Is<string>(m =>
            m.StartsWith("Chose Stock Expiring, Expiring within (days): 90.", StringComparison.Ordinal)));

        await _runner.DidNotReceive().RunAsync(Arg.Any<ReportRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_assistant_is_offered_only_the_reports_this_operator_may_run()
    {
        _view.AskText.Returns("daily takings");
        _suggester.SuggestAsync(Arg.Any<string>(), Arg.Any<ReportSuggestionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(ReportSuggestion.NotAReport);

        await OpenAsync();
        _view.AskRequested += Raise.EventWith(this, EventArgs.Empty);

        await _suggester.Received().SuggestAsync("daily takings",
            Arg.Is<ReportSuggestionContext>(c =>
                c.Presets.Count == 1 && c.Presets[0].Key == Expiring.Key),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_question_that_is_not_for_a_report_chooses_nothing()
    {
        _view.AskText.Returns("What is the dose of amoxicillin for a child?");
        _suggester.SuggestAsync(Arg.Any<string>(), Arg.Any<ReportSuggestionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(ReportSuggestion.NotAReport);

        await OpenAsync();
        _view.AskRequested += Raise.EventWith(this, EventArgs.Empty);

        _view.DidNotReceive().SelectPreset(Arg.Any<string>());
        _view.Received().ShowAskResult(Arg.Is<string>(m =>
            m.Contains("does not look like a request for a report", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Typing_starts_the_model_loading_once()
    {
        await OpenAsync();

        _view.AskStarted += Raise.EventWith(this, EventArgs.Empty);
        _view.AskStarted += Raise.EventWith(this, EventArgs.Empty);

        await _suggester.Received(1).WarmUpAsync(Arg.Any<ReportSuggestionContext>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_empty_request_asks_for_words_rather_than_guessing()
    {
        _view.AskText.Returns("   ");

        await OpenAsync();
        _view.AskRequested += Raise.EventWith(this, EventArgs.Empty);

        await _suggester.DidNotReceive().SuggestAsync(Arg.Any<string>(),
            Arg.Any<ReportSuggestionContext>(), Arg.Any<CancellationToken>());
        _view.Received().ShowAskResult(Arg.Is<string>(m =>
            m.StartsWith("Type the report you want", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task The_graph_toggle_decides_whether_an_export_carries_the_graph(
        bool graph, bool pdf)
    {
        var result = new ReportResult("Sales summary",
            new[] { new ReportColumn("Tender", "Tender") },
            new[] { (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["Tender"] = "Cash" } },
            new Dictionary<string, object?>(),
            new ReportProvenance("admin", DateTime.Now, "", ""))
        {
            Chart = new ReportChart("Net by tender", ChartKind.Bar, "Net",
                new[] { new ChartPoint("Cash", 1m) })
        };

        _runner.RunAsync(Arg.Any<ReportRequest>(), Arg.Any<CancellationToken>()).Returns(result);
        _view.SelectedPresetKey.Returns(Expiring.Key);
        _view.SlicerValues.Returns(new Dictionary<string, string?>());
        _view.IncludeGraph.Returns(graph);
        _view.PromptExportPath(Arg.Any<string>(), pdf).Returns("out");

        await OpenAsync();
        if (pdf) _view.ExportPdfRequested += Raise.EventWith(this, EventArgs.Empty);
        else _view.ExportRequested += Raise.EventWith(this, EventArgs.Empty);

        var written = pdf
            ? _pdfs.ReceivedCalls().Single().GetArguments()[0]
            : _workbooks.ReceivedCalls().Single().GetArguments()[0];

        ((ReportResult)written!).Chart.Should().Match(c => (c != null) == graph);
    }

    [Fact]
    public async Task Enlarge_opens_the_graph_drawn_large_or_says_there_is_none()
    {
        await OpenAsync();
        _view.EnlargeGraphRequested += Raise.EventWith(this, EventArgs.Empty);
        _view.Received().ShowInfo(Arg.Is<string>(m => m.Contains("has a graph")));

        var result = new ReportResult("Sales summary",
            new[] { new ReportColumn("Tender", "Tender") },
            new[] { (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["Tender"] = "Cash" } },
            new Dictionary<string, object?>(), new ReportProvenance("admin", DateTime.Now, "", ""))
        {
            Chart = new ReportChart("Net by tender", ChartKind.Bar, "Net",
                new[] { new ChartPoint("Cash", 1m) })
        };
        _runner.RunAsync(Arg.Any<ReportRequest>(), Arg.Any<CancellationToken>()).Returns(result);
        _view.SelectedPresetKey.Returns(Expiring.Key);
        _view.SlicerValues.Returns(new Dictionary<string, string?>());
        _charts.Png(Arg.Any<ReportChart>(), Arg.Any<int>(), Arg.Any<int>()).Returns(new byte[] { 1 });

        _view.PreviewRequested += Raise.EventWith(this, EventArgs.Empty);
        _view.EnlargeGraphRequested += Raise.EventWith(this, EventArgs.Empty);

        _charts.Received().Png(result.Chart, 1280, 640);
        _view.Received().ShowGraphWindow("Net by tender", Arg.Any<byte[]>());
    }

    private readonly IChartRenderer _charts = Substitute.For<IChartRenderer>();
    private readonly IWorkbookWriter _workbooks = Substitute.For<IWorkbookWriter>();
    private readonly IPdfWriter _pdfs = Substitute.For<IPdfWriter>();

    private async Task OpenAsync()
    {
        var presenter = new ReportsPresenter(_runner, _catalogue, _lookups, _permissions,
            _workbooks, _suggester, _pdfs, _charts);
        presenter.Attach(_view);
        await presenter.InitialiseAsync();
    }
}
