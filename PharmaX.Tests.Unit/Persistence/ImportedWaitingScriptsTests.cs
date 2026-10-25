using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Abstractions;
using PharmaX.Domain.Dispensing;
using PharmaX.Domain.Primitives;
using PharmaX.Persistence;
using PharmaX.Persistence.Import;
using PharmaX.Persistence.Interceptors;

namespace PharmaX.Tests.Unit.Persistence;

/// <summary>
/// An upgrade takes imported scripts off Scripts to Collect and leaves
/// a script PharmaX itself issued, and the dispensing record, alone.
/// </summary>
public sealed class ImportedWaitingScriptsTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly DateTime _noon = new(2026, 10, 16, 10, 0, 0, DateTimeKind.Utc);

    public ImportedWaitingScriptsTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Old_waiting_scripts_leave_the_queue_and_the_record_stays()
    {
        await using var db = Database();
        var settings = new PharmacySettingStore(db, new FixedClock(_noon));

        var old = Issued(db, "14568", tariff: 12m, scheme: 2m);
        var live = Issued(db, "D-001-000003", tariff: 8m, scheme: 0m);
        var paid = Issued(db, "835", tariff: 5m, scheme: 0m);
        paid.RecordCollection(Guid.NewGuid(), _noon);
        var covered = Issued(db, "900", tariff: 20m, scheme: 20m);
        var unfinished = Dispensing.Begin(Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), 0);
        unfinished.AddLine(DispensingLine.Create(Guid.NewGuid(), "Saline",
            1m, "As directed", Money.Of(3m)));
        db.Dispensings.Add(unfinished);
        await db.SaveChangesAsync();

        var cleared = await ImportedWaitingScripts.ClearOnceAsync(db, settings);

        cleared.Should().Be(1);
        db.ChangeTracker.Clear();
        var rows = await db.Dispensings.AsNoTracking().Include(d => d.Lines)
            .ToListAsync();

        rows.Should().HaveCount(5);
        rows.Single(d => d.Id == old.Id).Status.Should().Be(DispensingStatus.Issued);
        rows.Single(d => d.Id == old.Id).CollectedAtUtc.Should().Be(_noon);
        rows.Single(d => d.Id == old.Id).AwaitsCollection.Should().BeFalse();
        rows.Single(d => d.Id == old.Id).Lines.Should().NotBeEmpty();
        rows.Single(d => d.Id == live.Id).AwaitsCollection.Should().BeTrue();
        rows.Single(d => d.Id == paid.Id).SaleId.Should().NotBeNull();
        rows.Single(d => d.Id == covered.Id).CollectedAtUtc.Should().BeNull();
        rows.Single(d => d.Id == unfinished.Id).Status
            .Should().Be(DispensingStatus.InProgress);

        (await ImportedWaitingScripts.ClearOnceAsync(db, settings))
            .Should().Be(0, "a second start does not read the queue again");
    }

    [Fact]
    public async Task A_second_till_that_loses_the_save_still_starts()
    {
        await using var db = Database();
        var settings = new LosingSettings();

        var act = () => ImportedWaitingScripts.ClearOnceAsync(db, settings);

        await act.Should().NotThrowAsync();
        (await act()).Should().Be(0);
    }

    [Fact]
    public async Task A_failed_save_with_the_flag_still_unset_is_not_hidden()
    {
        await using var db = Database();

        var act = () => ImportedWaitingScripts.ClearOnceAsync(db, new BrokenSettings());

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    private Dispensing Issued(PharmaXDbContext db, string number,
        decimal tariff, decimal scheme)
    {
        var dispensing = Dispensing.Begin(Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), 0);
        var line = dispensing.AddLine(DispensingLine.Create(Guid.NewGuid(),
            "Amoxicillin 500mg", 21m, "One three times a day", Money.Of(tariff)));
        line.SetSchemeContribution(Money.Of(scheme));
        dispensing.Issue(Guid.NewGuid(), number, _noon);
        db.Dispensings.Add(dispensing);
        return dispensing;
    }

    private PharmaXDbContext Database()
    {
        var db = new PharmaXDbContext(new DbContextOptionsBuilder<PharmaXDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new RowVersionInterceptor())
            .Options);

        db.Database.EnsureCreated();
        return db;
    }

    private sealed class FixedClock(DateTime utc) : IClock
    {
        public DateTime UtcNow => utc;
        public DateOnly Today => DateOnly.FromDateTime(utc);
    }

    /// The other till inserts the flag as this one saves.
    private sealed class LosingSettings : IPharmacySettings
    {
        private int _writes;

        public Task<string?> GetAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(_writes > 0 ? "1" : null);

        public Task SetAsync(string key, string? value, CancellationToken ct = default)
        {
            _writes++;
            throw new DbUpdateException("The flag was inserted by the other till.");
        }
    }

    /// The save failed and nobody else finished the job.
    private sealed class BrokenSettings : IPharmacySettings
    {
        public Task<string?> GetAsync(string key, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);

        public Task SetAsync(string key, string? value, CancellationToken ct = default) =>
            throw new DbUpdateException("The database did not accept the save.");
    }
}
