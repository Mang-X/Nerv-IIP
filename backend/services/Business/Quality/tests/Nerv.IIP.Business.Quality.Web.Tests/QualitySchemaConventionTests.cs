using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Business.Quality.Domain;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
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
        };

        var failures = new List<string>();
        Assert.Equal(QualityFacts.Schema, fixture.DbContext.Model.GetDefaultSchema());
        failures.AddRange(SchemaConventionAssertions.BusinessTablesHaveComments(fixture.DbContext, QualityFacts.ServiceName, businessEntities));
        failures.AddRange(SchemaConventionAssertions.BusinessColumnsHaveComments(fixture.DbContext, QualityFacts.ServiceName, businessEntities));
        failures.AddRange(SchemaConventionAssertions.MigrationsHistoryTableIsInSchema(fixture.DbContext, QualityFacts.ServiceName, QualityFacts.Schema));

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static SchemaFixture CreateFixture()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly);
        });
        services.AddQualityPostgreSqlPersistence("Host=localhost;Database=nerv_iip_schema_conventions;Username=nerv;Password=nerv");

        return new SchemaFixture(services.BuildServiceProvider());
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
