using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;

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

public interface ILabelPrintBatchActivationFence
{
    Task AcquireAsync(
        string organizationId,
        string environmentId,
        LabelPrintBatchId printBatchId,
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

        var keyBytes = Encoding.UTF8.GetBytes($"{organizationId.Length}:{organizationId}\n{environmentId.Length}:{environmentId}\n{fileId.Length}:{fileId}");
        var digest = SHA256.HashData(keyBytes);
        var lockId = BinaryPrimitives.ReadInt64BigEndian(digest);
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

internal sealed class PostgresLabelPrintBatchActivationFence(ApplicationDbContext dbContext)
    : ILabelPrintBatchActivationFence
{
    public async Task AcquireAsync(
        string organizationId,
        string environmentId,
        LabelPrintBatchId printBatchId,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(dbContext.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The label print batch activation fence requires the Npgsql PostgreSQL provider.");
        }

        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("The label print batch activation fence requires an active PostgreSQL transaction.");
        }

        var lockId = PostgresAdvisoryLockKey.Create(
            "label-print-batch-activation",
            organizationId,
            environmentId,
            printBatchId.ToString());
        _ = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockId})",
            cancellationToken);
    }
}

internal static class PostgresAdvisoryLockKey
{
    public static long Create(string domain, params string[] components)
    {
        var payload = string.Join(
            '\n',
            components.Prepend(domain).Select(component => $"{component.Length}:{component}"));
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return BinaryPrimitives.ReadInt64BigEndian(digest);
    }
}
