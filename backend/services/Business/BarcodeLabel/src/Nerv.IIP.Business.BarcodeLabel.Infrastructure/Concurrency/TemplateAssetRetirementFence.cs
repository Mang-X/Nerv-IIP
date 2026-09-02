using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure.Concurrency;

public interface ITemplateAssetRetirementFence
{
    Task AcquireAsync(
        string organizationId,
        string environmentId,
        string fileId,
        CancellationToken cancellationToken);
}

internal sealed class PostgresTemplateAssetRetirementFence(ApplicationDbContext dbContext)
    : ITemplateAssetRetirementFence
{
    public async Task AcquireAsync(
        string organizationId,
        string environmentId,
        string fileId,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(dbContext.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
        {
            return;
        }

        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("The template asset retirement fence requires an active PostgreSQL transaction.");
        }

        var keyBytes = Encoding.UTF8.GetBytes($"{organizationId.Length}:{organizationId}\n{environmentId.Length}:{environmentId}\n{fileId.Length}:{fileId}");
        var digest = SHA256.HashData(keyBytes);
        var lockId = BinaryPrimitives.ReadInt64BigEndian(digest);
        _ = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockId})",
            cancellationToken);
    }
}
