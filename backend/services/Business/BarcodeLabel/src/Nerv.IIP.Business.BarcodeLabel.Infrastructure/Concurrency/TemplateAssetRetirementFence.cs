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

public interface ILabelPrintBatchReservationFence
{
    Task AcquireAsync(
        string organizationId,
        string environmentId,
        string idempotencyKey,
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
            throw new InvalidOperationException("The template asset retirement fence requires the Npgsql PostgreSQL provider.");
        }

        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("The template asset retirement fence requires an active PostgreSQL transaction.");
        }

        var lockId = PostgresAdvisoryLockKey.Create(
            "template-asset-retirement",
            organizationId,
            environmentId,
            fileId);
        _ = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockId})",
            cancellationToken);
    }
}

internal sealed class PostgresLabelPrintBatchReservationFence(ApplicationDbContext dbContext)
    : ILabelPrintBatchReservationFence
{
    public async Task AcquireAsync(
        string organizationId,
        string environmentId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(dbContext.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The label print batch reservation fence requires the Npgsql PostgreSQL provider.");
        }

        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("The label print batch reservation fence requires an active PostgreSQL transaction.");
        }

        var lockId = PostgresAdvisoryLockKey.Create(
            "label-print-batch-reservation",
            organizationId,
            environmentId,
            idempotencyKey);
        _ = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockId})",
            cancellationToken);
    }
}

internal static class PostgresAdvisoryLockKey
{
    public static long Create(string domain, params string[] components)
    {
        var framed = string.Join('\n', new[] { domain }.Concat(components).Select(value => $"{value.Length}:{value}"));
        return BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(framed)));
    }
}
