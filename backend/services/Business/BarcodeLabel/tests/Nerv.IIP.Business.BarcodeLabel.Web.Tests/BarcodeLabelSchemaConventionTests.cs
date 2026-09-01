using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Business.BarcodeLabel.Domain;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.ScanRecordAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.TraceabilityAggregate;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure;
using Nerv.IIP.Testing.EntityFramework;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public sealed class BarcodeLabelSchemaConventionTests
{
    [Fact]
    public void BarcodeLabel_schema_tables_columns_and_migrations_history_follow_database_conventions()
    {
        using var fixture = CreateFixture();
        var businessEntities = new[]
        {
            typeof(BarcodeRule),
            typeof(LabelTemplate),
            typeof(LabelPrintBatch),
            typeof(LabelPrintItem),
            typeof(ScanRecord),
            typeof(EpcisEvent),
        };
        var failures = new List<string>();

        failures.AddRange(SchemaConventionAssertions.BusinessTablesHaveComments(fixture.DbContext, BarcodeLabelFacts.ServiceName, businessEntities));
        failures.AddRange(SchemaConventionAssertions.BusinessColumnsHaveComments(fixture.DbContext, BarcodeLabelFacts.ServiceName, businessEntities));
        failures.AddRange(SchemaConventionAssertions.MigrationsHistoryTableIsInSchema(fixture.DbContext, BarcodeLabelFacts.ServiceName, BarcodeLabelFacts.Schema));

        Assert.Empty(failures);
    }

    [Fact]
    public void Replay_snapshot_migration_keeps_legacy_rows_nullable_and_drops_the_constraint_before_columns()
    {
        using var fixture = CreateFixture();
        var migrations = fixture.DbContext.GetService<IMigrationsAssembly>();
        var migrationType = migrations.Migrations.Single(entry =>
            entry.Key.EndsWith("_AddLabelPrintBatchReplaySnapshots", StringComparison.Ordinal)).Value;
        var migration = migrations.CreateMigration(migrationType, fixture.DbContext.Database.ProviderName!);

        var addedColumns = migration.UpOperations
            .OfType<AddColumnOperation>()
            .Where(operation => operation.Table == "label_print_batches")
            .ToArray();
        Assert.Equal(5, addedColumns.Length);
        Assert.All(addedColumns, operation => Assert.True(operation.IsNullable));
        var constraint = Assert.IsType<AddCheckConstraintOperation>(Assert.Single(
            migration.UpOperations,
            operation => operation is AddCheckConstraintOperation));
        Assert.Equal("ck_label_print_batches_replay_snapshot_complete", constraint.Name);
        Assert.Contains("IS NULL", constraint.Sql, StringComparison.Ordinal);
        Assert.Contains("IS NOT NULL", constraint.Sql, StringComparison.Ordinal);

        Assert.IsType<DropCheckConstraintOperation>(migration.DownOperations[0]);
        Assert.Equal(5, migration.DownOperations.OfType<DropColumnOperation>().Count());
    }

    private static BarcodeLabelSchemaFixture CreateFixture()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddBarcodeLabelPostgreSqlPersistence("Host=localhost;Database=nerv_iip_barcode_schema;Username=nerv;Password=nerv");
        return new BarcodeLabelSchemaFixture(services.BuildServiceProvider());
    }

    private sealed class BarcodeLabelSchemaFixture : IDisposable
    {
        private readonly ServiceProvider serviceProvider;
        private readonly IServiceScope scope;

        public BarcodeLabelSchemaFixture(ServiceProvider serviceProvider)
        {
            this.serviceProvider = serviceProvider;
            scope = serviceProvider.CreateScope();
            DbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        }

        public ApplicationDbContext DbContext { get; }

        public void Dispose()
        {
            DbContext.Dispose();
            scope.Dispose();
            serviceProvider.Dispose();
        }
    }
}
