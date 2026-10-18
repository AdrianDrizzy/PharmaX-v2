using NSubstitute;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Claims;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Dispensing.Dtos;
using PharmaX.Application.Patients;
using PharmaX.Application.Patients.Dtos;
using PharmaX.Application.Results;
using PharmaX.Application.Sales;
using PharmaX.Desktop.Features.Dispensing;

namespace PharmaX.Tests.Unit.Presenters;

/// <summary>
/// Issuing prints labels only when the dispensing screen says to.
/// </summary>
public class DispensePresenterTests
{
    private readonly IDispensingService _dispensing =
        Substitute.For<IDispensingService>();
    private readonly IScriptService _scripts = Substitute.For<IScriptService>();
    private readonly IPatientService _patients = Substitute.For<IPatientService>();
    private readonly ILabelPrinter _printer = Substitute.For<ILabelPrinter>();
    private readonly IDispenseView _view = Substitute.For<IDispenseView>();

    private readonly Guid _patientId = Guid.NewGuid();
    private readonly Guid _scriptId = Guid.NewGuid();
    private readonly Guid _dispensingId = Guid.NewGuid();

    [Fact]
    public async Task Issuing_with_labels_off_prints_nothing()
    {
        await IssueAsync(labels: false, trailer: true);

        await _printer.DidNotReceive().PrintAsync(
            Arg.Any<LabelDto>(), Arg.Any<CancellationToken>());
        await _printer.DidNotReceive().PrintTrailerAsync(
            Arg.Any<TrailerLabelDto>(), Arg.Any<CancellationToken>());
        _view.Received().ShowInfo(Arg.Is<string>(s =>
            s.Contains("No labels printed", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Issuing_with_labels_on_prints_the_item_and_not_a_cleared_trailer()
    {
        await IssueAsync(labels: true, trailer: false);

        await _printer.Received(1).PrintAsync(
            Arg.Any<LabelDto>(), Arg.Any<CancellationToken>());
        await _printer.DidNotReceive().PrintTrailerAsync(
            Arg.Any<TrailerLabelDto>(), Arg.Any<CancellationToken>());
        _view.Received().ShowInfo(Arg.Is<string>(s =>
            s.Contains("1 label(s) printed", StringComparison.Ordinal)));
    }

    private async Task IssueAsync(bool labels, bool trailer)
    {
        var line = new DispensingLineDto(1, Guid.NewGuid(), "Amoxicillin",
            20m, "One three times a day", null, null, 10m, 0m, 10m, false);
        var state = new DispensingStateDto(_dispensingId, new[] { line },
            Array.Empty<ClinicalWarningDto>(), 10m, 0m, 10m);

        _view.PromptPatientSearch().Returns(_patientId);
        _view.PromptScriptSearch(_patientId).Returns(_scriptId);
        _view.PrintLabels.Returns(labels);
        _view.PrintTrailer.Returns(trailer);

        _patients.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result<PatientDto>.Fail("t", "skip"));
        _scripts.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result<ScriptDetailDto>.Fail("t", "skip"));

        _dispensing.BeginAsync(_scriptId, Arg.Any<CancellationToken>())
            .Returns(Result<DispensingStateDto>.Ok(state));
        _dispensing.PendingWarningsAsync(_dispensingId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ClinicalWarningDto>());
        _dispensing.IssueAuthorityAsync(_dispensingId, Arg.Any<CancellationToken>())
            .Returns(Result<IssueAuthorityDto>.Ok(new IssueAuthorityDto(true, null)));

        var label = new LabelDto("Ada", "Amoxicillin", 20m, "tds", null, null,
            "Pharm", "42", new DateOnly(2026, 10, 18), Array.Empty<string>());
        var bag = new TrailerLabelDto("42", "Ada", 1, Array.Empty<TrailerItemDto>(),
            "Dr Adams", new DateOnly(2026, 10, 18));

        _dispensing.IssueAsync(_dispensingId, null, Arg.Any<CancellationToken>())
            .Returns(Result<IssueResultDto>.Ok(
                new IssueResultDto("42", "D-1", 10m, new[] { label }, null, bag)));

        _printer.PrintAsync(Arg.Any<LabelDto>(), Arg.Any<CancellationToken>())
            .Returns(PrintResult.Ok("LX-350"));
        _printer.PrintTrailerAsync(Arg.Any<TrailerLabelDto>(),
                Arg.Any<CancellationToken>())
            .Returns(PrintResult.Ok("LX-350"));

        var presenter = new DispensePresenter(_dispensing, _scripts, _patients,
            _printer, Substitute.For<IClaimFormService>(),
            Substitute.For<IClaimFormPrinter>(),
            Substitute.For<IWorkstationSettings>());
        presenter.Attach(_view);

        _view.ScriptRequested += Raise.EventWith(this, EventArgs.Empty);
        _view.IssueRequested += Raise.EventWith(this, EventArgs.Empty);

        await _dispensing.Received().IssueAsync(
            _dispensingId, null, Arg.Any<CancellationToken>());
    }
}
