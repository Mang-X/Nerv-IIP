using Microsoft.EntityFrameworkCore;
using Nerv.IIP.FileStorage.Domain;
using Nerv.IIP.FileStorage.Infrastructure.Records;
using Npgsql;

namespace Nerv.IIP.FileStorage.Infrastructure;

public sealed record RetirementAcceptance(TemplateAssetRetirementRecord? Receipt, bool Conflict);

public sealed class TemplateAssetRetirementStore(ApplicationDbContext db, TimeProvider clock)
{
    public async Task<RetirementAcceptance> AcceptAsync(RetirementCapability request,
        RetirementStorageInputs storage, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // A database row lock, not a process-local gate, serializes all retirements for this file.
        var file = await db.StoredFiles.FromSqlInterpolated(
            $"SELECT * FROM filestorage.stored_files WHERE file_id = {request.FileId} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        var existing = await db.TemplateAssetRetirements.AsNoTracking()
            .SingleOrDefaultAsync(x => x.DecisionId == request.DecisionId, ct);
        if (existing is not null)
            return new(existing.Matches(request) ? existing : null, !existing.Matches(request));

        if (file is null) return new(null, false);
        if (file.Status != FileStorageFileStatus.Available
            || file.OrganizationId != request.OrganizationId || file.EnvironmentId != request.EnvironmentId
            || file.OwnerService != request.OwnerService || file.OwnerType != request.OwnerType
            || file.OwnerId != request.OwnerId || file.Checksum != request.Checksum || file.FilePurpose != request.Purpose)
            return new(null, true);

        var receipt = TemplateAssetRetirementRecord.Accept(request, storage, file.SizeBytes, clock.GetUtcNow());
        file.HoldForTemplateAssetRetirement();
        db.TemplateAssetRetirements.Add(receipt);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new(receipt, false);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Concurrent reuse of a decision for a different file loses without leaking SQL details.
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return new(null, true);
        }
    }
}
