using System.Buffers.Binary;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore.Storage;

namespace Nerv.IIP.Business.Erp.Infrastructure;

public interface IWorkCenterCostRateRevisionLock
{
    long GetLockKey(string organizationId, string environmentId, string workCenterId);

    Task AcquireAsync(
        string organizationId,
        string environmentId,
        string workCenterId,
        CancellationToken cancellationToken);
}

public sealed class PostgreSqlWorkCenterCostRateRevisionLock(ApplicationDbContext dbContext)
    : IWorkCenterCostRateRevisionLock
{
    public long GetLockKey(string organizationId, string environmentId, string workCenterId)
    {
        var canonicalScope = new StringBuilder();
        AppendScopePart(canonicalScope, organizationId);
        AppendScopePart(canonicalScope, environmentId);
        AppendScopePart(canonicalScope, workCenterId);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalScope.ToString()));
        return BinaryPrimitives.ReadInt64BigEndian(hash);
    }

    public async Task AcquireAsync(
        string organizationId,
        string environmentId,
        string workCenterId,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational()) return;
        if (!string.Equals(dbContext.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            throw new NotSupportedException("Work-center cost-rate revision allocation requires PostgreSQL advisory locks.");

        var currentTransaction = dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Work-center cost-rate revision allocation requires a current EF transaction.");
        var connection = dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = currentTransaction.GetDbTransaction();
        command.CommandText = "SELECT pg_advisory_xact_lock(@key)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "key";
        parameter.DbType = DbType.Int64;
        parameter.Value = GetLockKey(organizationId, environmentId, workCenterId);
        command.Parameters.Add(parameter);
        _ = await command.ExecuteScalarAsync(cancellationToken);
    }

    private static void AppendScopePart(StringBuilder builder, string value)
    {
        var normalized = value.Trim();
        builder
            .Append(normalized.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(normalized);
    }
}
