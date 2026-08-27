using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Testing;
using Npgsql;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(MesPostgresLaneDatabase.CollectionName)]
public sealed class MesMaterialSubstituteSnapshotPostgresTests
{
    private const string TargetMigrationName = "AddMesMaterialSubstituteSnapshotFoundation";
    private static readonly TimeSpan PostgresOperationTimeout = TimeSpan.FromSeconds(30);

    // Contract: ProviderBehavior + Regression. Authority: Issue #2247 acceptance and the target MES migration/schema catalog.
    // Removing the target migration columns must fail this real PostgreSQL proof before persistence can be mistaken for green.
    [MesRealPostgresFact]
    public async Task Substitute_snapshot_migration_and_cross_scope_readback_hold_on_postgres()
    {
        var cancellationToken = CancellationToken.None;
        await ResetSchemaAsync(cancellationToken);
        var options = MesPostgresLaneDatabase.CreateOptions();
        var capturedAtUtc = DateTimeOffset.Parse("2026-08-25T13:00:00Z");

        var setup = new ApplicationDbContext(options, new NoopMediator());
        try
        {
            MesPostgresLaneDatabase.AssertUsesGovernedDatabase(setup);
            await TestTimeout.RunAsync(
                operation: "apply the MES substitute snapshot migrations on PostgreSQL",
                action: async token => await setup.Database.MigrateAsync(token),
                timeout: PostgresOperationTimeout,
                cancellationToken);

            setup.WorkOrders.Add(WorkOrder.Create(
                "org-001", "env-dev", "WO-SUBSTITUTE-PG-001", "FG-001", "PV-001", 10m, 10, capturedAtUtc));
            setup.MaterialRequirements.Add(MaterialRequirement.Capture(
                "org-001", "env-dev", "WO-SUBSTITUTE-PG-001", null, "MAT-PRIMARY", null,
                10m, 2m, 0m, "product-engineering-http", "MBOM-001:A:MAT-PRIMARY", capturedAtUtc,
                ["MAT-ALT-A", "MAT-ALT-B"]));
            var substitutedIssue = MaterialIssueRequest.Create(
                "org-001", "env-dev", "MIR-SUBSTITUTE-PG-001", "WO-SUBSTITUTE-PG-001", null,
                "MAT-ALT-A", "PCS", 1m, capturedAtUtc,
                substitutedMaterialId: "MAT-PRIMARY");
            setup.MaterialIssueRequests.AddRange(
                substitutedIssue,
                MaterialIssueRequest.Create(
                    "org-001", "env-dev", "MIR-SUBSTITUTE-PG-NULL", "WO-SUBSTITUTE-PG-001", null,
                    "MAT-PRIMARY", "PCS", 1m, capturedAtUtc));
            await TestTimeout.RunAsync(
                operation: "persist the MES substitute snapshot provider fixture on PostgreSQL",
                action: async token => await setup.SaveChangesAsync(token),
                timeout: PostgresOperationTimeout,
                cancellationToken);
        }
        finally
        {
            await DisposeWithinPostgresOperationBudgetAsync(
                setup,
                "dispose the MES substitute snapshot PostgreSQL setup context",
                cancellationToken);
        }

        var connection = new NpgsqlConnection(MesPostgresLaneDatabase.ConnectionString);
        try
        {
            await TestTimeout.RunAsync(
                operation: "open the MES substitute snapshot PostgreSQL proof connection",
                action: async token => await connection.OpenAsync(token),
                timeout: PostgresOperationTimeout,
                cancellationToken,
                sensitiveValues: [MesPostgresLaneDatabase.ConnectionString]);
            var historyCommand = new NpgsqlCommand(
                """
                SELECT count(*)
                FROM mes."__EFMigrationsHistory"
                WHERE "MigrationId" ~ @migration_id_pattern
                """,
                connection);
            try
            {
                historyCommand.Parameters.AddWithValue(
                    "migration_id_pattern",
                    $"^[0-9]{{14}}_{TargetMigrationName}$");
                var migrationCount = await TestTimeout.RunAsync(
                    operation: "read the MES substitute snapshot migration history on PostgreSQL",
                    action: async token => (long)(await historyCommand.ExecuteScalarAsync(token))!,
                    timeout: PostgresOperationTimeout,
                    cancellationToken);
                Assert.Equal(1L, migrationCount);
            }
            finally
            {
                await DisposeWithinPostgresOperationBudgetAsync(
                    historyCommand,
                    "dispose the MES substitute snapshot PostgreSQL migration history command",
                    cancellationToken);
            }

            var columns = new Dictionary<string, ColumnDefinition>(StringComparer.Ordinal);
            var columnsCommand = new NpgsqlCommand(
                """
                SELECT table_name, column_name, data_type, character_maximum_length, is_nullable, column_default
                FROM information_schema.columns
                WHERE table_schema = 'mes'
                  AND (table_name, column_name) IN (
                      ('material_requirements', 'substitute_material_ids_json'),
                      ('material_issue_requests', 'substituted_material_id'))
                ORDER BY table_name, column_name
                """,
                connection);
            try
            {
                var reader = await TestTimeout.RunAsync(
                    operation: "query the MES substitute snapshot columns on PostgreSQL",
                    action: async token => await columnsCommand.ExecuteReaderAsync(token),
                    timeout: PostgresOperationTimeout,
                    cancellationToken);
                try
                {
                    while (await TestTimeout.RunAsync(
                               operation: "read the next MES substitute snapshot column on PostgreSQL",
                               action: async token => await reader.ReadAsync(token),
                               timeout: PostgresOperationTimeout,
                               cancellationToken))
                    {
                        var maximumLengthIsNull = await TestTimeout.RunAsync(
                            operation: "read the MES substitute snapshot column length nullability on PostgreSQL",
                            action: async token => await reader.IsDBNullAsync(3, token),
                            timeout: PostgresOperationTimeout,
                            cancellationToken);
                        var defaultValueIsNull = await TestTimeout.RunAsync(
                            operation: "read the MES substitute snapshot column default nullability on PostgreSQL",
                            action: async token => await reader.IsDBNullAsync(5, token),
                            timeout: PostgresOperationTimeout,
                            cancellationToken);
                        columns.Add(
                            $"{reader.GetString(0)}.{reader.GetString(1)}",
                            new ColumnDefinition(
                                reader.GetString(2),
                                maximumLengthIsNull ? null : reader.GetInt32(3),
                                reader.GetString(4),
                                defaultValueIsNull ? null : reader.GetString(5)));
                    }
                }
                finally
                {
                    await DisposeWithinPostgresOperationBudgetAsync(
                        reader,
                        "dispose the MES substitute snapshot PostgreSQL column reader",
                        cancellationToken);
                }
            }
            finally
            {
                await DisposeWithinPostgresOperationBudgetAsync(
                    columnsCommand,
                    "dispose the MES substitute snapshot PostgreSQL columns command",
                    cancellationToken);
            }

            Assert.Equal(2, columns.Count);
            var substitutes = columns["material_requirements.substitute_material_ids_json"];
            Assert.Equal("text", substitutes.DataType);
            Assert.Null(substitutes.MaximumLength);
            Assert.Equal("NO", substitutes.IsNullable);
            Assert.Contains("[]", substitutes.DefaultValue, StringComparison.Ordinal);

            var audit = columns["material_issue_requests.substituted_material_id"];
            Assert.Equal("character varying", audit.DataType);
            Assert.Equal(100, audit.MaximumLength);
            Assert.Equal("YES", audit.IsNullable);
            Assert.Null(audit.DefaultValue);
        }
        finally
        {
            await DisposeWithinPostgresOperationBudgetAsync(
                connection,
                "dispose the MES substitute snapshot PostgreSQL proof connection",
                cancellationToken,
                [MesPostgresLaneDatabase.ConnectionString]);
        }

        var assertion = new ApplicationDbContext(options, new NoopMediator());
        try
        {
            var requirement = await TestTimeout.RunAsync(
                operation: "read back the MES substitute candidate snapshot on PostgreSQL",
                action: async token => await assertion.MaterialRequirements.SingleAsync(token),
                timeout: PostgresOperationTimeout,
                cancellationToken);
            var issues = await TestTimeout.RunAsync(
                operation: "read back the MES substitute audit matrix on PostgreSQL",
                action: async token => await assertion.MaterialIssueRequests
                    .ToDictionaryAsync(x => x.RequestNo, StringComparer.Ordinal, token),
                timeout: PostgresOperationTimeout,
                cancellationToken);
            Assert.Equal(["MAT-ALT-A", "MAT-ALT-B"], requirement.GetSubstituteMaterialIds());
            Assert.Equal("MAT-ALT-A", issues["MIR-SUBSTITUTE-PG-001"].MaterialId);
            Assert.Equal("MAT-PRIMARY", issues["MIR-SUBSTITUTE-PG-001"].SubstitutedMaterialId);
            Assert.Null(issues["MIR-SUBSTITUTE-PG-NULL"].SubstitutedMaterialId);
        }
        finally
        {
            await DisposeWithinPostgresOperationBudgetAsync(
                assertion,
                "dispose the MES substitute snapshot PostgreSQL assertion context",
                cancellationToken);
        }
    }

    private static async Task ResetSchemaAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(MesPostgresLaneDatabase.ConnectionString);
        try
        {
            await TestTimeout.RunAsync(
                operation: "open the MES substitute snapshot PostgreSQL reset connection",
                action: async token => await connection.OpenAsync(token),
                timeout: PostgresOperationTimeout,
                cancellationToken,
                sensitiveValues: [MesPostgresLaneDatabase.ConnectionString]);

            foreach (var schema in new[] { MesFacts.Schema, "cap" })
            {
                var quotedSchema = new NpgsqlCommandBuilder().QuoteIdentifier(schema);
                var command = connection.CreateCommand();
                try
                {
                    command.CommandText = $"DROP SCHEMA IF EXISTS {quotedSchema} CASCADE";
                    await TestTimeout.RunAsync(
                        operation: $"reset the {schema} schema for the MES substitute snapshot PostgreSQL proof",
                        action: async token => await command.ExecuteNonQueryAsync(token),
                        timeout: PostgresOperationTimeout,
                        cancellationToken);
                }
                finally
                {
                    await DisposeWithinPostgresOperationBudgetAsync(
                        command,
                        $"dispose the {schema} schema reset command for the MES substitute snapshot PostgreSQL proof",
                        cancellationToken);
                }
            }
        }
        finally
        {
            await DisposeWithinPostgresOperationBudgetAsync(
                connection,
                "dispose the MES substitute snapshot PostgreSQL reset connection",
                cancellationToken,
                [MesPostgresLaneDatabase.ConnectionString]);
        }
    }

    private static ValueTask DisposeWithinPostgresOperationBudgetAsync(
        IAsyncDisposable disposable,
        string operation,
        CancellationToken cancellationToken,
        IReadOnlyCollection<string?>? sensitiveValues = null) =>
        TestTimeout.RunAsync(
            operation,
            token => new ValueTask(disposable.DisposeAsync().AsTask().WaitAsync(token)),
            PostgresOperationTimeout,
            cancellationToken,
            sensitiveValues: sensitiveValues);

    private sealed record ColumnDefinition(
        string DataType,
        int? MaximumLength,
        string IsNullable,
        string? DefaultValue);
}
