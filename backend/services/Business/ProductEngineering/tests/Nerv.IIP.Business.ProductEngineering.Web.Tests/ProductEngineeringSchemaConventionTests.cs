using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Business.ProductEngineering.Domain;
using Nerv.IIP.Business.ProductEngineering.Infrastructure;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringChangeAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringDocumentAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringItemAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ManufacturingBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.RoutingAggregate;
using Nerv.IIP.Testing.EntityFramework;

namespace Nerv.IIP.Business.ProductEngineering.Web.Tests;

public sealed class ProductEngineeringSchemaConventionTests
{
    [Fact]
    public void Design_time_PostgreSQL_factory_configures_migrations_history_schema_and_assembly()
    {
        using var dbContext = new DesignTimeApplicationDbContextFactory().CreateDbContext([]);
        var failures = SchemaConventionAssertions.MigrationsHistoryTableIsInSchema(
            dbContext,
            ProductEngineeringFacts.ServiceName,
            ProductEngineeringFacts.Schema);
        var relationalOptions = dbContext.GetService<IDbContextOptions>()
            .Extensions
            .OfType<RelationalOptionsExtension>()
            .LastOrDefault();

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Assert.Equal(typeof(DesignTimeApplicationDbContextFactory).Assembly.FullName, relationalOptions?.MigrationsAssembly);
    }

    [Fact]
    public void Runtime_PostgreSQL_profile_configures_migrations_history_schema()
    {
        using var fixture = CreateFixture();
        var failures = SchemaConventionAssertions.MigrationsHistoryTableIsInSchema(
            fixture.DbContext,
            ProductEngineeringFacts.ServiceName,
            ProductEngineeringFacts.Schema);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void ProductEngineering_schema_metadata_follows_database_conventions()
    {
        using var fixture = CreateFixture();
        var businessEntities = new[]
        {
            typeof(EngineeringDocument),
            typeof(EngineeringItem),
            typeof(EngineeringBom),
            typeof(EngineeringBomLine),
            typeof(ManufacturingBom),
            typeof(ManufacturingBomMaterialLine),
            typeof(ManufacturingBomRecipeLine),
            typeof(Routing),
            typeof(RoutingOperation),
            typeof(EngineeringChange),
            typeof(EngineeringChangeAffectedVersion),
            typeof(ProductionVersion),
        };

        var failures = new List<string>();
        Assert.Equal(ProductEngineeringFacts.Schema, fixture.DbContext.Model.GetDefaultSchema());
        failures.AddRange(SchemaConventionAssertions.BusinessTablesHaveComments(fixture.DbContext, ProductEngineeringFacts.ServiceName, businessEntities));
        failures.AddRange(SchemaConventionAssertions.BusinessColumnsHaveComments(fixture.DbContext, ProductEngineeringFacts.ServiceName, businessEntities));
        failures.AddRange(SchemaConventionAssertions.MigrationsHistoryTableIsInSchema(fixture.DbContext, ProductEngineeringFacts.ServiceName, ProductEngineeringFacts.Schema));

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static SchemaFixture CreateFixture()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddProductEngineeringPostgreSqlPersistence("Host=localhost;Database=nerv_iip_schema_conventions;Username=nerv;Password=nerv");
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
