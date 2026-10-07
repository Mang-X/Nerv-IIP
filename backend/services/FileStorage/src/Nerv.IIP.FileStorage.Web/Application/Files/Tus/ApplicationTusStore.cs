using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.FileStorage.Infrastructure;
using Nerv.IIP.FileStorage.Infrastructure.Records;
using tusdotnet.Interfaces;

namespace Nerv.IIP.FileStorage.Web.Application.Files.Tus;

// Request-scoped transport adapter. PostgreSQL owns metadata; file length is the durable offset.
// tusdotnet's file lock serializes PATCH offsets. The application gate alone owns PATCH/complete ordering.
internal sealed class ApplicationTusStore(
    ApplicationDbContext db,
    ILocalTusFileStoreAccessor accessor,
    IUploadSessionMutationGate gate,
    TimeProvider clock,
    HttpContext context) : ITusStore, ITusChecksumStore, ITusExpirationStore, ITusTerminationStore
{
    private bool checksumMatches;

    public async Task<long> AppendDataAsync(string fileId, Stream stream, CancellationToken cancellationToken)
    {
        accessor.TryGet(out var bytes);
        long written = 0;
        var result = await gate.ExecutePatchMutationAsync(fileId, async token =>
        {
            // The application gate has just re-read durable open. Hold it through writes, rollback and fsync.
            await using var file = bytes.OpenWrite(fileId);
            var offset = file.Length;
            if (offset != long.Parse(context.Request.Headers["Upload-Offset"].ToString(), CultureInfo.InvariantCulture))
                throw new TusMutationRejectedException(StatusCodes.Status409Conflict);
            file.Position = offset;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[64 * 1024];
            try
            {
                int read;
                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, token)) != 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), token);
                    hash.AppendData(buffer, 0, read);
                    written += read;
                }
                var checksumHeader = context.Request.Headers["Upload-Checksum"].ToString();
                // tusdotnet has validated the algorithm and base64 before calling this method.
                checksumMatches = checksumHeader.Length == 0 || CryptographicOperations.FixedTimeEquals(
                    hash.GetHashAndReset(), Convert.FromBase64String(checksumHeader.Split(' ', 2)[1]));
                if (!checksumMatches) { file.SetLength(offset); written = 0; }
                file.Flush(flushToDisk: true);
            }
            catch
            {
                file.SetLength(offset);
                file.Flush(flushToDisk: true);
                throw;
            }
        }, cancellationToken);
        if (result != UploadSessionMutationResult.Mutated)
            throw new TusMutationRejectedException(result == UploadSessionMutationResult.NotFound
                ? StatusCodes.Status404NotFound : StatusCodes.Status409Conflict);
        return written;
    }

    public Task<bool> FileExistAsync(string fileId, CancellationToken cancellationToken) =>
        db.UploadSessions.AnyAsync(x => x.UploadSessionId == fileId && x.Provider == "tus", cancellationToken);

    public async Task<long?> GetUploadLengthAsync(string fileId, CancellationToken cancellationToken) =>
        await db.UploadSessions.Where(x => x.UploadSessionId == fileId)
            .Select(x => (long?)x.ExpectedSizeBytes).SingleOrDefaultAsync(cancellationToken);

    public Task<long> GetUploadOffsetAsync(string fileId, CancellationToken cancellationToken)
    {
        accessor.TryGet(out var bytes);
        return Task.FromResult(bytes.GetOffset(fileId));
    }

    public Task<IEnumerable<string>> GetSupportedAlgorithmsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IEnumerable<string>>(["sha256"]);

    // Verification and any rollback already completed inside the application-owned mutation gate.
    public Task<bool> VerifyChecksumAsync(string fileId, string algorithm, byte[] checksum, CancellationToken cancellationToken) =>
        Task.FromResult(checksumMatches);

    public Task SetExpirationAsync(string fileId, DateTimeOffset expires, CancellationToken cancellationToken) =>
        Task.CompletedTask; // Application creation exclusively fixes TTL; transport never extends it.

    public async Task<DateTimeOffset?> GetExpirationAsync(string fileId, CancellationToken cancellationToken) =>
        await db.UploadSessions.Where(x => x.UploadSessionId == fileId)
            .Select(x => (DateTimeOffset?)x.ExpiresAtUtc).SingleOrDefaultAsync(cancellationToken);

    public async Task<IEnumerable<string>> GetExpiredFilesAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        return await db.UploadSessions.Where(x => x.Provider == "tus" && x.State == UploadSessionState.Open
            && !x.LegacyCompleted && x.ExpiresAtUtc <= now).Select(x => x.UploadSessionId).ToArrayAsync(cancellationToken);
    }

    public async Task<int> RemoveExpiredFilesAsync(CancellationToken cancellationToken)
    {
        var expired = await GetExpiredFilesAsync(cancellationToken);
        var removed = 0;
        foreach (var id in expired) { await DeleteFileAsync(id, cancellationToken); removed++; }
        return removed;
    }

    public async Task DeleteFileAsync(string fileId, CancellationToken cancellationToken)
    {
        var result = await gate.ExecutePatchMutationAsync(fileId, async token =>
        {
            var session = await db.UploadSessions.SingleAsync(x => x.UploadSessionId == fileId, token);
            accessor.TryGet(out var bytes);
            bytes.Delete(fileId);
            db.UploadSessions.Remove(session);
            await db.SaveChangesAsync(token);
        }, cancellationToken);
        if (result == UploadSessionMutationResult.NotOpen)
            throw new TusMutationRejectedException(StatusCodes.Status409Conflict);
    }
}
