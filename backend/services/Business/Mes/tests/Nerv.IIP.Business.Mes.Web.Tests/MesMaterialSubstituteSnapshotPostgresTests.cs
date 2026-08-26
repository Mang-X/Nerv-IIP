using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Npgsql;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(MesPostgresLaneDatabase.CollectionName)]
public sealed class MesMaterialSubstituteSnapshotPostgresTests
{
    private const string TargetMigrationName = "AddMesMaterialSubstituteSnapshotFoundation";

    // Contract: ProviderBehavior + Regression. Authority: Issue #2247 acceptance and the target MES migration/schema catalog.
    // Removing the target migration columns must fail this real PostgreSQL proof before persistence can be mistaken for green.
    [MesRealPostgresFact]
    public async Task Substitute_snapshot_migration_and_cross_scope_readback_hold_on_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var options = MesPostgresLaneDatabase.CreateOptions();
        var capturedAtUtc = DateTimeOffset.Parse("2026-08-25T13:00:00Z");

        await using (var setup = new ApplicationDbContext(options, new NoopMediator()))
        {
            MesPostgresLaneDatabase.AssertUsesGovernedDatabase(setup);
            await setup.Database.MigrateAsync();

            setup.WorkOrders.Add(WorkOrder.Create(
                "org-001", "env-dev", "WO-SUBSTITUTE-PG-001", "FG-001", "PV-001", 10m, 10, capturedAtUtc));
            setup.MaterialRequirements.Add(MaterialRequirement.Capture(
                "org-001", "env-dev", "WO-SUBSTITUTE-PG-001", null, "MAT-PRIMARY", null,
                10m, 2m, 0m, "product-engineering-http", "MBOM-001:A:MAT-PRIMARY", capturedAtUtc,
                ["MAT-ALT-A", "MAT-ALT-B"]));
            setup.MaterialIssueRequests.Add(MaterialIssueRequest.Create(
                "org-001", "env-dev", "MIR-SUBSTITUTE-PG-001", "WO-SUBSTITUTE-PG-001", null,
                "MAT-PRIMARY", "PCS", 1m, capturedAtUtc));
            await setup.SaveChangesAsync();
        }

        await using (var connection = new NpgsqlConnection(MesPostgresLaneDatabase.ConnectionString))
        {
            await connection.OpenAsync();
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
            Assert.Equal(1L, (long)(await historyCommand.ExecuteScalarAsync())!);

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
            await using var reader = await columnsCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns.Add(
                    $"{reader.GetString(0)}.{reader.GetString(1)}",
                    new ColumnDefinition(
                        reader.GetString(2),
                        await reader.IsDBNullAsync(3) ? null : reader.GetInt32(3),
                        reader.GetString(4),
                        await reader.IsDBNullAsync(5) ? null : reader.GetString(5)));
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
        var requirement = await assertion.MaterialRequirements.SingleAsync();
        var issue = await assertion.MaterialIssueRequests.SingleAsync();
        Assert.Equal(["MAT-ALT-A", "MAT-ALT-B"], requirement.GetSubstituteMaterialIds());
        Assert.Null(issue.SubstitutedMaterialId);
    }

    private sealed record ColumnDefinition(
        string DataType,
        int? MaximumLength,
        string IsNullable,
        string? DefaultValue);

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
