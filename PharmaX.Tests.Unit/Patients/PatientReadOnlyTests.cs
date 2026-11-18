using FluentAssertions;
using NSubstitute;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Licensing;
using PharmaX.Application.Patients;
using PharmaX.Application.Patients.Dtos;
using PharmaX.Application.Repositories;
using PharmaX.Application.Security;
using PharmaX.Tests.Unit.Fakes;

namespace PharmaX.Tests.Unit.Patients;

public sealed class PatientReadOnlyTests
{
    private static readonly PatientEditDto Edit = new("Moyo", "Tendai", null, null,
        null, null, null, false, false, null, [], []);

    private static PatientService Service(LicenceStatus status)
    {
        var gate = new LicenceGate();
        gate.Update(new(status, null, null, false));

        var permissions = new PermissionService(new FakeCurrentUser(),
            new FakePermissionRepository(), new FakeAuditWriter(), gate);

        return new PatientService(Substitute.For<IPatientRepository>(),
            Substitute.For<IDocumentNumbering>(), permissions, new FakeAuditWriter(),
            new FakeOutbox(), new FakeUnitOfWork(), new FakeClock(), gate);
    }

    [Fact]
    public async Task Create_is_refused_when_read_only() =>
        await FluentActions.Awaiting(() =>
                Service(LicenceStatus.ReadOnly).CreateAsync(Edit))
            .Should().ThrowAsync<PermissionDeniedException>()
            .WithMessage(PermissionService.ReadOnlyMessage);

    [Fact]
    public async Task Update_is_refused_when_read_only() =>
        await FluentActions.Awaiting(() =>
                Service(LicenceStatus.ClockWrong).UpdateAsync(Guid.NewGuid(), Edit))
            .Should().ThrowAsync<PermissionDeniedException>()
            .WithMessage(PermissionService.ReadOnlyMessage);

    [Fact]
    public async Task Create_goes_ahead_in_grace() =>
        await FluentActions.Awaiting(() => Service(LicenceStatus.Grace).CreateAsync(Edit))
            .Should().NotThrowAsync<PermissionDeniedException>();
}
