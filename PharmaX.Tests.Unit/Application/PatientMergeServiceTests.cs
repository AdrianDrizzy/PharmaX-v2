using FluentAssertions;
using NSubstitute;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Patients;
using PharmaX.Application.Repositories;
using PharmaX.Application.Security;
using PharmaX.Domain.Patients;

namespace PharmaX.Tests.Unit.Application;

/// <summary>
/// Merging rewrites clinical history across two files and cannot be
/// undone from the screen that did it, so the refusals matter as much as
/// the happy path.
/// </summary>
public class PatientMergeServiceTests
{
    private readonly IPatientRepository _patients =
        Substitute.For<IPatientRepository>();

    private readonly IPatientMergeRepository _merge =
        Substitute.For<IPatientMergeRepository>();

    private readonly IPermissionService _permissions =
        Substitute.For<IPermissionService>();

    private readonly IAuditWriter _audit = Substitute.For<IAuditWriter>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private PatientMergeService CreateSut() =>
        new(_patients, _merge, _permissions, _audit, _uow);

    private Patient Given(string fileNumber, string surname,
                          bool active = true)
    {
        var patient = Patient.Create(fileNumber, surname, "Test");
        if (!active) patient.Deactivate();

        _patients.GetAsync(patient.Id).Returns(patient);
        return patient;
    }

    [Fact]
    public async Task A_file_cannot_be_merged_into_itself()
    {
        var patient = Given("P001", "Ndlovu");

        var result = await CreateSut()
            .PreviewAsync(patient.Id, patient.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("two different files");
    }

    [Fact]
    public async Task A_file_already_retired_cannot_be_merged_again()
    {
        var keep = Given("P001", "Ndlovu");
        var gone = Given("P002", "Ndlovu", active: false);

        var result = await CreateSut().PreviewAsync(keep.Id, gone.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("already been merged");
    }

    [Fact]
    public async Task A_merge_without_a_reason_is_refused()
    {
        var keep = Given("P001", "Ndlovu");
        var duplicate = Given("P002", "Ndlovu");

        var result = await CreateSut().MergeAsync(keep.Id, duplicate.Id, "  ");

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("requires a reason");

        await _merge.DidNotReceive().ReassignAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>());
    }

    [Fact]
    public async Task The_preview_counts_what_would_move()
    {
        var keep = Given("P001", "Ndlovu");
        var duplicate = Given("P002", "Ndlovu");

        _merge.CountScriptsAsync(duplicate.Id).Returns(4);
        _merge.CountDispensingsAsync(duplicate.Id).Returns(7);

        var result = await CreateSut().PreviewAsync(keep.Id, duplicate.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Scripts.Should().Be(4);
        result.Value.Dispensings.Should().Be(7);
        result.Value.KeepFileNumber.Should().Be("P001");
        result.Value.MergeFileNumber.Should().Be("P002");
    }

    [Fact]
    public async Task Merging_moves_the_history_and_retires_the_duplicate()
    {
        var keep = Given("P001", "Ndlovu");
        var duplicate = Given("P002", "Ndlovu");

        _merge.ReassignAsync(duplicate.Id, keep.Id).Returns((3, 5));

        var result = await CreateSut()
            .MergeAsync(keep.Id, duplicate.Id, "Same person, two files.");

        result.IsSuccess.Should().BeTrue();
        result.Value.Scripts.Should().Be(3);
        result.Value.Dispensings.Should().Be(5);

        duplicate.IsActive.Should().BeFalse();
        keep.IsActive.Should().BeTrue();

        await _uow.Received().SaveChangesAsync();
    }

    /// <summary>
    /// The account follows only into a file that has none. Two accounts
    /// are two balances, and silently choosing one loses money somebody
    /// is owed.
    /// </summary>
    [Fact]
    public async Task The_account_follows_only_where_there_is_room_for_it()
    {
        var keep = Given("P001", "Ndlovu");
        var duplicate = Given("P002", "Ndlovu");

        var account = Guid.NewGuid();
        duplicate.LinkAccount(account);

        _merge.ReassignAsync(duplicate.Id, keep.Id).Returns((0, 0));

        await CreateSut().MergeAsync(keep.Id, duplicate.Id, "Duplicate.");

        keep.AccountId.Should().Be(account);
    }

    [Fact]
    public async Task An_existing_account_on_the_kept_file_is_left_alone()
    {
        var keep = Given("P001", "Ndlovu");
        var duplicate = Given("P002", "Ndlovu");

        var kept = Guid.NewGuid();
        keep.LinkAccount(kept);
        duplicate.LinkAccount(Guid.NewGuid());

        _merge.ReassignAsync(duplicate.Id, keep.Id).Returns((0, 0));

        var result = await CreateSut()
            .MergeAsync(keep.Id, duplicate.Id, "Duplicate.");

        keep.AccountId.Should().Be(kept);
        result.Value.KeepAlreadyHasAccount.Should().BeTrue();
    }

    /// <summary>
    /// Both files get an entry: looking up the retired file number has
    /// to explain where its history went.
    /// </summary>
    [Fact]
    public async Task Both_files_are_audited()
    {
        var keep = Given("P001", "Ndlovu");
        var duplicate = Given("P002", "Ndlovu");

        _merge.ReassignAsync(duplicate.Id, keep.Id).Returns((1, 1));

        await CreateSut().MergeAsync(keep.Id, duplicate.Id, "Duplicate.");

        _audit.Received().Applied("pat.merge", "Patient", keep.Id,
            Arg.Any<string>());

        _audit.Received().Applied("pat.merged-away", "Patient", duplicate.Id,
            Arg.Any<string>());
    }
}
