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

        await using (var setup = new ApplicationDbContext(options, new NoopMediator()))
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
                "MAT-PRIMARY", "PCS", 1m, capturedAtUtc);
            setup.Entry(substitutedIssue).Property(x => x.SubstitutedMaterialId).CurrentValue = "MAT-ALT-A";
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

        await using (var connection = new NpgsqlConnection(MesPostgresLaneDatabase.ConnectionString))
        {
            await TestTimeout.RunAsync(
                operation: "open the MES substitute snapshot PostgreSQL proof connection",
                action: async token => await connection.OpenAsync(token),
                timeout: PostgresOperationTimeout,
                cancellationToken,
                sensitiveValues: [MesPostgresLaneDatabase.ConnectionString]);
            await using var historyCommand = new NpgsqlCommand(
                """
                SELECT count(*)
                FROM mes."__EFMigrationsHistory"
                WHERE "MigrationId" ~ @migration_id_pattern
                """,
                connection);
            historyCommand.Parameters.AddWithValue(
                "migration_id_pattern",
                $"^[0-9]{{14}}_{TargetMigrationName}$");
            var migrationCount = await TestTimeout.RunAsync(
                operation: "read the MES substitute snapshot migration history on PostgreSQL",
                action: async token => (long)(await historyCommand.ExecuteScalarAsync(token))!,
                timeout: PostgresOperationTimeout,
                cancellationToken);
            Assert.Equal(1L, migrationCount);

            var columns = new Dictionary<string, ColumnDefinition>(StringComparer.Ordinal);
            await using var columnsCommand = new NpgsqlCommand(
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
            await using var reader = await TestTimeout.RunAsync(
                operation: "query the MES substitute snapshot columns on PostgreSQL",
                action: async token => await columnsCommand.ExecuteReaderAsync(token),
                timeout: PostgresOperationTimeout,
                cancellationToken);
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

        await using var assertion = new ApplicationDbContext(options, new NoopMediator());
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
        Assert.Equal("MAT-ALT-A", issues["MIR-SUBSTITUTE-PG-001"].SubstitutedMaterialId);
        Assert.Null(issues["MIR-SUBSTITUTE-PG-NULL"].SubstitutedMaterialId);
    }

    private static async Task ResetSchemaAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(MesPostgresLaneDatabase.ConnectionString);
        await TestTimeout.RunAsync(
            operation: "open the MES substitute snapshot PostgreSQL reset connection",
            action: async token => await connection.OpenAsync(token),
            timeout: PostgresOperationTimeout,
            cancellationToken,
            sensitiveValues: [MesPostgresLaneDatabase.ConnectionString]);

        foreach (var schema in new[] { MesFacts.Schema, "cap" })
        {
            var quotedSchema = new NpgsqlCommandBuilder().QuoteIdentifier(schema);
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP SCHEMA IF EXISTS {quotedSchema} CASCADE";
            await TestTimeout.RunAsync(
                operation: $"reset the {schema} schema for the MES substitute snapshot PostgreSQL proof",
                action: async token => await command.ExecuteNonQueryAsync(token),
                timeout: PostgresOperationTimeout,
                cancellationToken);
        }
    }

    private sealed record ColumnDefinition(
        string DataType,
        int? MaximumLength,
        string IsNullable,
        string? DefaultValue);
}
