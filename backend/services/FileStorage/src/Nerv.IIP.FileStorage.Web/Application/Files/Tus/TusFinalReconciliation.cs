using Microsoft.EntityFrameworkCore;
using Nerv.IIP.FileStorage.Infrastructure;

namespace Nerv.IIP.FileStorage.Web.Application.Files.Tus;

public sealed record TusFinalReconciliationResult(
    IReadOnlyList<string> MissingFinalFiles,
    IReadOnlyList<string> OrphanFinalFiles);

public sealed class TusFinalReconciliation(
    ApplicationDbContext database,
    ILocalTusFileStoreAccessor stores,
    IWebHostEnvironment environment)
{
    public async Task<TusFinalReconciliationResult> ScanAsync(CancellationToken cancellationToken)
    {
        if (!stores.TryGet(out var store))
        {
            throw new InvalidOperationException("The tus volume is unavailable.");
        }

        store.VerifyVolume(required: !environment.IsDevelopment());
        var files = await database.StoredFiles
            .Select(file => new FinalFileRecord(file.FileId, file.ObjectKey))
            .ToListAsync(cancellationToken);
        return Compare(store, files, cancellationToken);
    }

    internal static TusFinalReconciliationResult Compare(
        LocalUploadByteStore store,
        IEnumerable<FinalFileRecord> files,
        CancellationToken cancellationToken)
    {
        var unmatched = store.EnumerateFinalFileNames().ToHashSet(StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!unmatched.Remove(LocalUploadByteStore.GetFinalFileName(file.ObjectKey)))
            {
                missing.Add(file.FileId);
            }
        }

        return new(missing.Order(StringComparer.Ordinal).ToArray(), unmatched.Order(StringComparer.Ordinal).ToArray());
    }

    internal sealed record FinalFileRecord(string FileId, string ObjectKey);
}
