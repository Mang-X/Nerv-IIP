using Microsoft.EntityFrameworkCore;
using Nerv.IIP.FileStorage.Infrastructure;

namespace Nerv.IIP.FileStorage.Web.Application.Files.Tus;

public sealed record TusFinalReconciliationResult(
    IReadOnlyList<string> MissingFinalFiles,
    IReadOnlyList<string> OrphanFinalFiles,
    bool Truncated,
    int ScannedVolumeEntries,
    int ScannedStoredFiles,
    int MissingFinalFileCount,
    int? OrphanFinalFileCount);

public sealed class TusFinalReconciliation(
    ApplicationDbContext database,
    ILocalTusFileStoreAccessor stores,
    IWebHostEnvironment environment,
    TusFinalReconciliationGate gate)
{
    private const int MaxEntries = 10_000;
    private const int MaxReportItems = 500;

    public async Task<TusFinalReconciliationResult?> TryScanAsync(CancellationToken cancellationToken)
    {
        if (!await gate.Permit.WaitAsync(0, cancellationToken))
        {
            return null;
        }

        try
        {
            if (!stores.TryGet(out var store))
            {
                throw new InvalidOperationException("The tus volume is unavailable.");
            }

            store.VerifyVolume(required: !environment.IsDevelopment());
            var files = database.StoredFiles
                .OrderBy(file => file.FileId)
                .Take(MaxEntries + 1)
                .Select(file => new FinalFileRecord(file.FileId, file.ObjectKey))
                .AsAsyncEnumerable();
            return await CompareAsync(store, files, cancellationToken);
        }
        finally
        {
            gate.Permit.Release();
        }
    }

    internal static async Task<TusFinalReconciliationResult> CompareAsync(
        LocalUploadByteStore store,
        IAsyncEnumerable<FinalFileRecord> files,
        CancellationToken cancellationToken,
        int maxEntries = MaxEntries,
        int maxReportItems = MaxReportItems)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var unmatched = new HashSet<string>(StringComparer.Ordinal);
        var scannedVolumeEntries = 0;
        var volumeTruncated = false;
        foreach (var path in store.EnumerateVolumeEntries())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (scannedVolumeEntries == maxEntries)
            {
                volumeTruncated = true;
                break;
            }

            scannedVolumeEntries++;
            if (LocalUploadByteStore.IsFinalFile(path))
            {
                unmatched.Add(Path.GetFileName(path));
            }
        }

        var missing = new List<string>();
        var missingCount = 0;
        var scannedStoredFiles = 0;
        var recordsTruncated = false;
        await foreach (var file in files.WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (scannedStoredFiles == maxEntries)
            {
                recordsTruncated = true;
                break;
            }

            scannedStoredFiles++;
            if (!unmatched.Remove(LocalUploadByteStore.GetFinalFileName(file.ObjectKey))
                && (!volumeTruncated || !store.FinalExists(file.ObjectKey)))
            {
                missingCount++;
                if (missing.Count < maxReportItems)
                {
                    missing.Add(file.FileId);
                }
            }
        }

        // A final file may still be referenced by a database record beyond the scan limit.
        int? orphanCount = recordsTruncated ? null : unmatched.Count;
        string[] orphans = recordsTruncated
            ? []
            : unmatched.Order(StringComparer.Ordinal).Take(maxReportItems - missing.Count).ToArray();

        return new(
            missing.Order(StringComparer.Ordinal).ToArray(),
            orphans,
            volumeTruncated || recordsTruncated || missingCount > missing.Count || orphanCount > orphans.Length,
            scannedVolumeEntries,
            scannedStoredFiles,
            missingCount,
            orphanCount);
    }

    internal sealed record FinalFileRecord(string FileId, string ObjectKey);
}

public sealed class TusFinalReconciliationGate : IDisposable
{
    internal SemaphoreSlim Permit { get; } = new(1, 1);

    public void Dispose() => Permit.Dispose();
}
