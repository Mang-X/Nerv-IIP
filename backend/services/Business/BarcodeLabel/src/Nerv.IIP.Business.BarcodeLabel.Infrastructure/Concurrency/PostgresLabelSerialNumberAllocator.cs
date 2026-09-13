using Microsoft.EntityFrameworkCore.Storage;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelSerialCounterAggregate;

namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure.Concurrency;

internal sealed class PostgresLabelSerialNumberAllocator(ApplicationDbContext dbContext)
    : ILabelSerialNumberAllocator
{
    public async Task<IReadOnlyList<string>> AllocateAsync(
        string organizationId,
        string environmentId,
        BarcodeRuleId barcodeRuleId,
        int serialNumberLength,
        int quantity,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(dbContext.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The label serial number allocator requires the Npgsql PostgreSQL provider.");
        }

        var transaction = dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("The label serial number allocator requires an active PostgreSQL transaction.");
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Serial allocation quantity must be positive.");
        }

        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            INSERT INTO barcode.label_serial_counters
                (id, organization_id, environment_id, barcode_rule_id, current_value)
            VALUES
                (@id, @organization_id, @environment_id, @barcode_rule_id, @quantity)
            ON CONFLICT (organization_id, environment_id, barcode_rule_id)
            DO UPDATE SET current_value = barcode.label_serial_counters.current_value + EXCLUDED.current_value
            RETURNING current_value;
            """;
        AddParameter(command, "id", Guid.CreateVersion7());
        AddParameter(command, "organization_id", organizationId);
        AddParameter(command, "environment_id", environmentId);
        AddParameter(command, "barcode_rule_id", barcodeRuleId.Id);
        AddParameter(command, "quantity", quantity);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        var end = Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
        var start = checked(end - quantity + 1);
        return Enumerable.Range(0, quantity)
            .Select(offset => LabelSerialNumber.Format(checked(start + offset), serialNumberLength))
            .ToArray();
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
