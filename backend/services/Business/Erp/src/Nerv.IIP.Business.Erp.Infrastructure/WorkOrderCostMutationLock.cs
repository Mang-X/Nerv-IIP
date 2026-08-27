using System.Buffers.Binary;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore.Storage;

namespace Nerv.IIP.Business.Erp.Infrastructure;

public interface IWorkOrderCostMutationLock
{
    Task AcquireAsync(
        string organizationId,
        string environmentId,
        string workOrderId,
        CancellationToken cancellationToken);
}

public sealed class PostgreSqlWorkOrderCostMutationLock(ApplicationDbContext dbContext)
    : IWorkOrderCostMutationLock
{
    public async Task AcquireAsync(
        string organizationId,
        string environmentId,
        string workOrderId,
        CancellationToken cancellationToken)
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
        parameter.Value = GetLockKey(organizationId, environmentId, workOrderId);
        command.Parameters.Add(parameter);
        _ = await command.ExecuteScalarAsync(cancellationToken);
    }

    internal static long GetLockKey(string organizationId, string environmentId, string workOrderId)
    {
        var canonicalScope = new StringBuilder("work-order-cost|");
        AppendScopePart(canonicalScope, organizationId);
        AppendScopePart(canonicalScope, environmentId);
        AppendScopePart(canonicalScope, workOrderId);
        return BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalScope.ToString())));
    }

    private static void AppendScopePart(StringBuilder builder, string value)
    {
        var normalized = value.Trim();
        builder.Append(normalized.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(normalized);
    }
}
