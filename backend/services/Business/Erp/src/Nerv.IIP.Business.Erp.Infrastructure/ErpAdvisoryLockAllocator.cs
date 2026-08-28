using System.Buffers.Binary;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore.Storage;

namespace Nerv.IIP.Business.Erp.Infrastructure;

public enum ErpAdvisoryLockDomain
{
    WorkCenterLaborCostRate,
    WorkCenterMachineOverheadRate,
}

public interface IErpAdvisoryLockAllocator
{
    long GetLockKey(
        ErpAdvisoryLockDomain domain,
        string organizationId,
        string environmentId,
        string workCenterId);

    Task AcquireAsync(
        ErpAdvisoryLockDomain domain,
        string organizationId,
        string environmentId,
        string workCenterId,
        CancellationToken cancellationToken);
}

public sealed class PostgreSqlErpAdvisoryLockAllocator(ApplicationDbContext dbContext)
    : IErpAdvisoryLockAllocator
{
    public long GetLockKey(
        ErpAdvisoryLockDomain domain,
        string organizationId,
        string environmentId,
        string workCenterId)
    {
        var canonicalScope = new StringBuilder();
        AppendScopePart(canonicalScope, GetDomainKey(domain));
        AppendScopePart(canonicalScope, organizationId);
        AppendScopePart(canonicalScope, environmentId);
        AppendScopePart(canonicalScope, workCenterId);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalScope.ToString()));
        return BinaryPrimitives.ReadInt64BigEndian(hash);
    }

    public async Task AcquireAsync(
        ErpAdvisoryLockDomain domain,
        string organizationId,
        string environmentId,
        string workCenterId,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational()) return;
        if (!string.Equals(dbContext.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            throw new NotSupportedException("ERP advisory-lock allocation requires PostgreSQL.");

        var currentTransaction = dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("ERP advisory-lock allocation requires a current EF transaction.");
        var connection = dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = currentTransaction.GetDbTransaction();
        command.CommandText = "SELECT pg_advisory_xact_lock(@key)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "key";
        parameter.DbType = DbType.Int64;
        parameter.Value = GetLockKey(domain, organizationId, environmentId, workCenterId);
        command.Parameters.Add(parameter);
        _ = await command.ExecuteScalarAsync(cancellationToken);
    }

    private static string GetDomainKey(ErpAdvisoryLockDomain domain) => domain switch
    {
        ErpAdvisoryLockDomain.WorkCenterLaborCostRate => "work-center-labor-cost-rate",
        ErpAdvisoryLockDomain.WorkCenterMachineOverheadRate => "work-center-machine-overhead-rate",
        _ => throw new ArgumentOutOfRangeException(nameof(domain), domain, "Unknown ERP advisory-lock domain."),
    };

    private static void AppendScopePart(StringBuilder builder, string value)
    {
        var normalized = value.Trim();
        builder
            .Append(normalized.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(normalized);
    }
}
