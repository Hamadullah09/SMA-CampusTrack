using Microsoft.EntityFrameworkCore;

namespace CampusTrack.Infrastructure.Persistence;

/// <summary>
/// Runs a multi-step write inside a transaction that survives a connection retry.
///
/// The database is configured with <c>EnableRetryOnFailure</c> so the RFID pipeline rides
/// out a brief failover. That resilience has a consequence EF enforces strictly: once a
/// retrying execution strategy is in play, opening a transaction by hand throws, because a
/// retry would otherwise resume mid-transaction against a fresh connection and commit half
/// the work.
///
/// The correct pattern is to hand the whole unit to the strategy, which replays it from the
/// beginning if the connection drops. Wrapping it here means every caller gets that right by
/// default rather than each remembering to.
/// </summary>
public static class TransactionRunner
{
    public static async Task<T> InTransactionAsync<T>(
        this CampusTrackDbContext db,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            var result = await work(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }

    public static async Task InTransactionAsync(
        this CampusTrackDbContext db,
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken = default)
    {
        await db.InTransactionAsync(async ct =>
        {
            await work(ct);
            return true;
        }, cancellationToken);
    }
}
