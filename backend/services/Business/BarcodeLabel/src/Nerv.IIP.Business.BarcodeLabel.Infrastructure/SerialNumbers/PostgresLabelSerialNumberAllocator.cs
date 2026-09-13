using System.Data;
using Microsoft.EntityFrameworkCore.Storage;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelSerialCounterAggregate;
using Npgsql;

namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure.SerialNumbers;

public sealed class PostgresLabelSerialNumberAllocator(ApplicationDbContext dbContext)
    : ILabelSerialNumberAllocator
{
    public async Task<IReadOnlyList<string>> AllocateAsync(
        string organizationId,
        string environmentId,
        BarcodeRuleId barcodeRuleId,
        int quantity,
        CancellationToken cancellationToken)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Serial allocation quantity must be positive.");
        }

        if (!dbContext.Database.IsNpgsql())
        {
            throw new InvalidOperationException("The label serial allocator requires the Npgsql PostgreSQL provider.");
        }

        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("The label serial allocator requires an active PostgreSQL transaction.");
        }

        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        var shouldClose = connection.State == ConnectionState.Closed;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (NpgsqlTransaction?)dbContext.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = """
                INSERT INTO barcode.label_serial_counters (id, organization_id, environment_id, barcode_rule_id, current_value)
                VALUES (@id, @organization_id, @environment_id, @barcode_rule_id, @quantity)
                ON CONFLICT (organization_id, environment_id, barcode_rule_id)
                DO UPDATE SET current_value = barcode.label_serial_counters.current_value + EXCLUDED.current_value
                RETURNING current_value - @quantity + 1;
                """;
            command.Parameters.AddWithValue("id", Guid.CreateVersion7());
            command.Parameters.AddWithValue("organization_id", organizationId);
            command.Parameters.AddWithValue("environment_id", environmentId);
            command.Parameters.AddWithValue("barcode_rule_id", barcodeRuleId.Id);
            command.Parameters.AddWithValue("quantity", quantity);
            var firstValue = (long)(await command.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("The label serial allocator did not return a reserved range."));

            return Enumerable.Range(0, quantity)
                .Select(offset => LabelSerialNumber.Format(barcodeRuleId, checked(firstValue + offset)))
                .ToArray();
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }
}
