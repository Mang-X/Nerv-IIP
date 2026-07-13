using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Business.MasterData.Domain;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.BusinessPartnerAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.CodeRuleAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DepartmentAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DeviceAssetAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.LifecycleAuditAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.PersonnelSkillAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ProductCategoryAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ProductionLineAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ReferenceDataAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ShiftAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SiteAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkuAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkillAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.TeamAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.TeamMemberAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.UnitOfMeasureAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.UomConversionAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCalendarAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCenterAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkshopAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Coding;
using Nerv.IIP.Testing.EntityFramework;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

public sealed class MasterDataSchemaConventionTests
{
    [Fact]
    public void Runtime_PostgreSQL_profile_configures_migrations_history_schema()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly);
        });
        services.AddMasterDataPostgreSqlPersistence("Host=localhost;Database=nerv_iip_schema_conventions;Username=nerv;Password=nerv");

        using var fixture = new SchemaFixture(services.BuildServiceProvider());
        var failures = SchemaConventionAssertions.MigrationsHistoryTableIsInSchema(
            fixture.DbContext,
            MasterDataFacts.ServiceName,
            MasterDataFacts.Schema);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void MasterData_schema_metadata_follows_database_conventions()
    {
        using var fixture = CreateFixture();
        var businessEntities = new[]
        {
            typeof(Sku),
            typeof(BusinessPartner),
            typeof(Department),
            typeof(Team),
            typeof(TeamMember),
            typeof(PersonnelSkill),
            typeof(ProductCategory),
            typeof(Skill),
            typeof(UnitOfMeasure),
            typeof(UomConversion),
            typeof(Site),
            typeof(Workshop),
            typeof(ProductionLine),
            typeof(Shift),
            typeof(ReferenceDataCode),
            typeof(WorkCenter),
            typeof(WorkCalendar),
            typeof(WorkCalendarWorkingTime),
            typeof(DeviceAsset),
            typeof(CodeRule),
            typeof(CodeRuleVersion),
            typeof(CodeCounter),
            typeof(CodeIdempotencyKey),
            typeof(MasterDataLifecycleAuditEntry),
        };

        var failures = new List<string>();
        Assert.Equal(MasterDataFacts.Schema, fixture.DbContext.Model.GetDefaultSchema());
        failures.AddRange(SchemaConventionAssertions.BusinessTablesHaveComments(fixture.DbContext, MasterDataFacts.ServiceName, businessEntities));
        failures.AddRange(SchemaConventionAssertions.BusinessColumnsHaveComments(fixture.DbContext, MasterDataFacts.ServiceName, businessEntities));
        failures.AddRange(SchemaConventionAssertions.MigrationsHistoryTableIsInSchema(fixture.DbContext, MasterDataFacts.ServiceName, MasterDataFacts.Schema));

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Team_member_unique_index_matches_active_membership_lookup()
    {
        using var fixture = CreateFixture();
        var entityType = fixture.DbContext.Model.FindEntityType(typeof(TeamMember));
        Assert.NotNull(entityType);

        var index = Assert.Single(entityType.GetIndexes(), candidate =>
            candidate.IsUnique &&
            candidate.Properties.Select(property => property.Name)
                .SequenceEqual(["OrganizationId", "EnvironmentId", "TeamCode", "UserId"]));

        Assert.Equal("disabled = false", index.GetFilter());
        Assert.DoesNotContain(entityType.GetIndexes(), candidate =>
            candidate.IsUnique &&
            candidate.Properties.Select(property => property.Name)
                .SequenceEqual(["OrganizationId", "EnvironmentId", "TeamCode", "UserId", "EffectiveFrom"]));
    }

    private static SchemaFixture CreateFixture()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly);
        });
        services.AddMasterDataPostgreSqlPersistence("Host=localhost;Database=nerv_iip_schema_conventions;Username=nerv;Password=nerv");

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
