using Microsoft.EntityFrameworkCore.Storage;
using PharmaX.Application.Abstractions;

namespace PharmaX.Persistence;

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly PharmaXDbContext _db;

    public UnitOfWork(PharmaXDbContext db) => _db = db;

    public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);

    public async Task<IAsyncDisposable> BeginTransactionAsync(
        CancellationToken ct = default)
    {
        // A service that opens a transaction inside one already running
        // gets the ambient one back, so a nested call cannot commit the
        // outer caller's work early.
        if (_db.Database.CurrentTransaction is not null)
            return new NestedScope();

        var tx = await _db.Database.BeginTransactionAsync(ct);
        return new TransactionScope(tx);
    }

    private sealed class TransactionScope : IAsyncDisposable
    {
        private readonly IDbContextTransaction _tx;
        private bool _committed;

        public TransactionScope(IDbContextTransaction tx) => _tx = tx;

        /// <summary>
        /// Idempotent, which IAsyncDisposable requires and which this
        /// codebase relies on: a service that commits explicitly before
        /// building its result and also holds the scope in an
        /// `await using` will dispose it twice, and the second commit
        /// would otherwise throw on a transaction already completed.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (_committed) return;

            _committed = true;
            await _tx.CommitAsync();
            await _tx.DisposeAsync();
        }
    }

    private sealed class NestedScope : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
