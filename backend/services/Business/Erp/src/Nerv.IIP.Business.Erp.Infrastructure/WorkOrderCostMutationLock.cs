using System.Buffers.Binary;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore.Storage;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;

namespace Nerv.IIP.Business.Erp.Infrastructure;

public sealed class PostgreSqlWorkOrderCostMutationLock(ApplicationDbContext dbContext)
    : IWorkOrderCostMutationLock
{
    public Task AcquireReportAsync(string organizationId, string environmentId, string reportNo, CancellationToken cancellationToken)
        => AcquireLockAsync(GetLockKey("work-order-report-cost|", organizationId, environmentId, reportNo), cancellationToken);

    public Task AcquireAsync(
        string organizationId,
        string environmentId,
        string workOrderId,
        CancellationToken cancellationToken)
        => AcquireLockAsync(GetLockKey(organizationId, environmentId, workOrderId), cancellationToken);

    private async Task AcquireLockAsync(long key, CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational()) return;
        if (!string.Equals(dbContext.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            throw new NotSupportedException("Work-order cost mutation locking requires PostgreSQL advisory locks.");

        var currentTransaction = dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Work-order cost mutation locking requires a current EF transaction.");
        var connection = dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = currentTransaction.GetDbTransaction();
        command.CommandText = "SELECT pg_advisory_xact_lock(@key)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "key";
        parameter.DbType = DbType.Int64;
        parameter.Value = key;
        command.Parameters.Add(parameter);
        _ = await command.ExecuteScalarAsync(cancellationToken);
    }

    internal static long GetLockKey(string organizationId, string environmentId, string workOrderId)
    {
        return GetLockKey("work-order-cost|", organizationId, environmentId, workOrderId);
    }

    private static long GetLockKey(string domain, string organizationId, string environmentId, string documentId)
    {
        var canonicalScope = new StringBuilder(domain);
        AppendScopePart(canonicalScope, organizationId);
        AppendScopePart(canonicalScope, environmentId);
        AppendScopePart(canonicalScope, documentId);
        return BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalScope.ToString())));
    }

    private static void AppendScopePart(StringBuilder builder, string value)
    {
        var normalized = value.Trim();
        builder.Append(normalized.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(normalized);
    }
}
