using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
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
    public void Print_batch_template_checksum_snapshot_fits_the_canonical_sha256_value()
    {
        using var fixture = CreateFixture();
        var property = fixture.DbContext.Model
            .FindEntityType(typeof(LabelPrintBatch))!
            .FindProperty(nameof(LabelPrintBatch.TemplateAssetSha256))!;

        Assert.Equal(71, property.GetMaxLength());
    }

    [Fact]
    public void Print_batch_status_comment_declares_delivery_unknown_truthfully()
    {
        using var fixture = CreateFixture();
        var property = fixture.DbContext.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(LabelPrintBatch))!
            .FindProperty(nameof(LabelPrintBatch.Status))!;

        Assert.Contains("delivery-unknown", property.GetComment(), StringComparison.Ordinal);
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
