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
    // Contract: ProviderBehavior + Regression. Authority: Issue #2222 acceptance 3 and the MES migration/schema catalog.
    [MesRealPostgresFact]
    public async Task Substitute_snapshot_migration_and_cross_scope_readback_hold_on_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var options = MesPostgresLaneDatabase.CreateOptions();
        var now = DateTimeOffset.Parse("2026-08-25T13:00:00Z");

        await using (var setup = new ApplicationDbContext(options, new NoopMediator()))
        {
            MesPostgresLaneDatabase.AssertUsesGovernedDatabase(setup);
            await setup.Database.MigrateAsync();
            setup.WorkOrders.Add(WorkOrder.Create(
                "org-001", "env-dev", "WO-PG-SUBSTITUTE-001", "FG-001", "PV-001", 10m, 10, now));
            setup.MaterialRequirements.Add(MaterialRequirement.Capture(
                "org-001", "env-dev", "WO-PG-SUBSTITUTE-001", null, "MAT-PRIMARY", null,
                10m, 2m, 0m, "product-engineering-http", "MBOM-PG:MAT-PRIMARY", now,
                ["MAT-ALT-A", "MAT-ALT-B"]));
            setup.MaterialIssueRequests.Add(MaterialIssueRequest.Create(
                "org-001", "env-dev", "MIR-PG-SUBSTITUTE-001", "WO-PG-SUBSTITUTE-001", null,
                "MAT-PRIMARY", "PCS", 1m, now));
            await setup.SaveChangesAsync();
        }

        await AssertPhysicalSchemaAsync();

        await using var readback = new ApplicationDbContext(options, new NoopMediator());
        var requirement = await readback.MaterialRequirements.SingleAsync();
        var issue = await readback.MaterialIssueRequests.SingleAsync();
        Assert.Equal(["MAT-ALT-A", "MAT-ALT-B"], requirement.GetSubstituteMaterialIds());
        Assert.Null(issue.SubstitutedMaterialId);
    }

    private static async Task AssertPhysicalSchemaAsync()
    {
        await using var connection = new NpgsqlConnection(MesPostgresLaneDatabase.ConnectionString);
        await connection.OpenAsync();
        await using (var migrationCommand = connection.CreateCommand())
        {
            migrationCommand.CommandText = """
                SELECT COUNT(*)
                FROM mes."__EFMigrationsHistory"
                WHERE "MigrationId" = '20260825145237_AddMesMaterialSubstituteSnapshotFoundation';
                """;
            Assert.Equal(1L, Assert.IsType<long>(await migrationCommand.ExecuteScalarAsync()));
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT column_name, data_type, is_nullable, character_maximum_length, column_default
            FROM information_schema.columns
            WHERE table_schema = 'mes'
              AND table_name IN ('material_requirements', 'material_issue_requests')
              AND column_name IN ('substitute_material_ids_json', 'substituted_material_id')
            ORDER BY column_name;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("substitute_material_ids_json", reader.GetString(0));
        Assert.Equal("text", reader.GetString(1));
        Assert.Equal("NO", reader.GetString(2));
        Assert.True(reader.IsDBNull(3));
        Assert.Contains("[]", reader.GetString(4), StringComparison.Ordinal);
        Assert.True(await reader.ReadAsync());
        Assert.Equal("substituted_material_id", reader.GetString(0));
        Assert.Equal("character varying", reader.GetString(1));
        Assert.Equal("YES", reader.GetString(2));
        Assert.Equal(100, reader.GetInt32(3));
        Assert.True(reader.IsDBNull(4));
        Assert.False(await reader.ReadAsync());
    }

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
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
