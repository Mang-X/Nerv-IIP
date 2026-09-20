using Microsoft.EntityFrameworkCore;
using Nerv.IIP.FileStorage.Infrastructure.Records;

namespace Nerv.IIP.FileStorage.Infrastructure;

public sealed class UploadCommitExecutionLeaseStore(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    TimeProvider timeProvider)
{
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);

    public async Task<bool> TryClaimAsync(
        string uploadSessionId,
        string executionOwnerId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var leaseUntil = now.Add(LeaseDuration);
        if (dbContext.Database.IsRelational())
        {
            return await dbContext.UploadSessions
                .Where(x => x.UploadSessionId == uploadSessionId
                    && x.State == UploadSessionState.Committing
                    && (x.ExecutionLeaseUntilUtc == null || x.ExecutionLeaseUntilUtc <= now))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(x => x.ExecutionOwnerId, executionOwnerId)
                        .SetProperty(x => x.ExecutionLeaseUntilUtc, leaseUntil)
                        .SetProperty(x => x.ConcurrencyVersion, x => x.ConcurrencyVersion + 1),
                    cancellationToken) == 1;
        }

        var session = await dbContext.UploadSessions.SingleOrDefaultAsync(
            x => x.UploadSessionId == uploadSessionId,
            cancellationToken);
        if (session is null
            || !string.Equals(session.State, UploadSessionState.Committing, StringComparison.Ordinal)
            || session.ExecutionLeaseUntilUtc is { } existingLease && existingLease > now)
        {
            return false;
        }

        session.ClaimExecution(executionOwnerId, leaseUntil);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> TryRenewAsync(
        string uploadSessionId,
        string executionOwnerId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var leaseUntil = now.Add(LeaseDuration);
        if (dbContext.Database.IsRelational())
        {
            return await dbContext.UploadSessions
                .Where(x => x.UploadSessionId == uploadSessionId
                    && x.State == UploadSessionState.Committing
                    && x.ExecutionOwnerId == executionOwnerId
                    && x.ExecutionLeaseUntilUtc > now)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.ExecutionLeaseUntilUtc, leaseUntil),
                    cancellationToken) == 1;
        }

        var session = await dbContext.UploadSessions.SingleOrDefaultAsync(
            x => x.UploadSessionId == uploadSessionId,
            cancellationToken);
        if (session is null
            || !string.Equals(session.State, UploadSessionState.Committing, StringComparison.Ordinal)
            || !string.Equals(session.ExecutionOwnerId, executionOwnerId, StringComparison.Ordinal)
            || session.ExecutionLeaseUntilUtc <= now)
        {
            return false;
        }

        session.RenewExecutionLease(leaseUntil);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> StillOwnsAsync(
        string uploadSessionId,
        string executionOwnerId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.UploadSessions
            .AsNoTracking()
            .AnyAsync(
                x => x.UploadSessionId == uploadSessionId
                    && x.State == UploadSessionState.Committing
                    && x.ExecutionOwnerId == executionOwnerId
                    && x.ExecutionLeaseUntilUtc > timeProvider.GetUtcNow(),
                cancellationToken);
    }
}
