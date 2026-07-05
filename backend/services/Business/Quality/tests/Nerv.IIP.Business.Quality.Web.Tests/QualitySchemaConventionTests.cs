using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Business.Quality.Domain;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.QualityReasonAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Testing.EntityFramework;

namespace Nerv.IIP.Business.Quality.Web.Tests;

public sealed class QualitySchemaConventionTests
{
    [Fact]
    public void Quality_schema_metadata_follows_database_conventions()
    {
        using var fixture = CreateFixture();
        var businessEntities = new[]
        {
            typeof(NonconformanceReport),
            typeof(InspectionPlan),
            typeof(InspectionPlanCharacteristic),
            typeof(InspectionRecord),
            typeof(InspectionResultLine),
            typeof(InspectionTask),
            typeof(QualityReason),
        };

        var failures = new List<string>();
        Assert.Equal(QualityFacts.Schema, fixture.DbContext.Model.GetDefaultSchema());
        failures.AddRange(SchemaConventionAssertions.BusinessTablesHaveComments(fixture.DbContext, QualityFacts.ServiceName, businessEntities));
        failures.AddRange(SchemaConventionAssertions.BusinessColumnsHaveComments(fixture.DbContext, QualityFacts.ServiceName, businessEntities));
        failures.AddRange(SchemaConventionAssertions.MigrationsHistoryTableIsInSchema(fixture.DbContext, QualityFacts.ServiceName, QualityFacts.Schema));

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Quality_inspection_models_include_query_indexes()
    {
        using var fixture = CreateFixture();

        AssertEntityHasIndex<InspectionPlan>(
            fixture.DbContext,
            [nameof(InspectionPlan.OrganizationId), nameof(InspectionPlan.EnvironmentId), nameof(InspectionPlan.Status)]);
        AssertEntityHasIndex<InspectionRecord>(
            fixture.DbContext,
            [nameof(InspectionRecord.OrganizationId), nameof(InspectionRecord.EnvironmentId), nameof(InspectionRecord.Result)]);
        AssertEntityHasIndex<QualityReason>(
            fixture.DbContext,
            [nameof(QualityReason.OrganizationId), nameof(QualityReason.EnvironmentId), nameof(QualityReason.GroupName), nameof(QualityReason.Enabled)]);
        AssertEntityHasIndex<InspectionTask>(
            fixture.DbContext,
            [nameof(InspectionTask.OrganizationId), nameof(InspectionTask.EnvironmentId), nameof(InspectionTask.Status), nameof(InspectionTask.DueAtUtc)]);
    }

    [Fact]
    public void Mes_defect_source_unique_index_is_scoped_to_auto_created_mes_ncrs()
    {
        using var fixture = CreateFixture();

        var script = fixture.DbContext.GetService<IMigrator>().GenerateScript(
            "20260616013940_AddQualityBusinessGap415",
            "20260619051226_AddQualityDefectConsumerReliability");

        Assert.Contains("CREATE UNIQUE INDEX ux_ncr_mes_defect_source", script, StringComparison.Ordinal);
        Assert.Contains(
            "WHERE source_type = 'in-process' AND sku_code = 'MES-SKU-UNRESOLVED';",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Inspection_source_sku_unique_index_precheck_points_to_duplicate_remediation_runbook()
    {
        using var fixture = CreateFixture();

        var script = fixture.DbContext.GetService<IMigrator>().GenerateScript(
            "20260626170759_AddQualityNcrInventoryDispositionRouting",
            "20260629074947_AddQualityLongtailReviewFixes");

        Assert.Contains(
            "Cannot add unique inspection source/SKU index because duplicate Quality inspection records already exist",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "docs/architecture/business-quality-inspection-duplicate-remediation.md",
            script,
            StringComparison.Ordinal);
    }

    private static SchemaFixture CreateFixture()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly);
        });
        services.AddQualityPostgreSqlPersistence("Host=unused;Database=nerv_iip_schema_conventions;Username=nerv;Password=nerv");

        return new SchemaFixture(services.BuildServiceProvider());
    }

    private static void AssertEntityHasIndex<TEntity>(ApplicationDbContext dbContext, IReadOnlyCollection<string> propertyNames)
    {
        var entityType = dbContext.Model.FindEntityType(typeof(TEntity));
        Assert.NotNull(entityType);
        Assert.Contains(entityType.GetIndexes(), index =>
            index.Properties.Select(property => property.Name).SequenceEqual(propertyNames, StringComparer.Ordinal));
    }

    private sealed class SchemaFixture : IDisposable
    {
        private readonly ServiceProvider serviceProvider;
        private readonly IServiceScope scope;

        public SchemaFixture(ServiceProvider serviceProvider)
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
