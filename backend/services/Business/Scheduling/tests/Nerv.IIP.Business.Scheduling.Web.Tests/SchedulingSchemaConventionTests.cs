using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Business.Scheduling.Domain;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.ScheduleOperationOverrideAggregate;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.OrderUrgencyAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Testing.EntityFramework;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class SchedulingSchemaConventionTests
{
    [Fact]
    public void Runtime_PostgreSQL_profile_configures_migrations_history_schema()
    {
        using var fixture = CreateFixture();
        var failures = SchemaConventionAssertions.MigrationsHistoryTableIsInSchema(
            fixture.DbContext,
            SchedulingFacts.ServiceName,
            SchedulingFacts.Schema);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Scheduling_schema_metadata_follows_database_conventions()
    {
        using var fixture = CreateFixture();
        var businessEntities = new[]
        {
            typeof(ScheduleProblemSnapshot),
            typeof(SchedulePlan),
            typeof(SchedulePlanAssignment),
            typeof(SchedulePlanResourceLoad),
            typeof(SchedulePlanConflict),
            typeof(SchedulePlanUnscheduledOperation),
            typeof(SchedulePlanInvalidation),
            typeof(ScheduleOperationOverride),
            typeof(OrderUrgencyBusinessPriority),
            typeof(OrderUrgencyBusinessPriorityChange),
            typeof(OrderUrgencySnapshot),
        };

        var failures = new List<string>();
        Assert.Equal(SchedulingFacts.Schema, fixture.DbContext.Model.GetDefaultSchema());
        failures.AddRange(SchemaConventionAssertions.BusinessTablesHaveComments(fixture.DbContext, SchedulingFacts.ServiceName, businessEntities));
        failures.AddRange(SchemaConventionAssertions.BusinessColumnsHaveComments(fixture.DbContext, SchedulingFacts.ServiceName, businessEntities));
        failures.AddRange(SchemaConventionAssertions.MigrationsHistoryTableIsInSchema(fixture.DbContext, SchedulingFacts.ServiceName, SchedulingFacts.Schema));

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Schedule_plan_release_governance_columns_and_indexes_are_explicit()
    {
        using var fixture = CreateFixture();
        var entity = fixture.DbContext.Model.FindEntityType(typeof(SchedulePlan))!;

        Assert.Equal("release_revision", entity.FindProperty(nameof(SchedulePlan.ReleaseRevision))!.GetColumnName());
        Assert.Equal("revoked_at_utc", entity.FindProperty(nameof(SchedulePlan.RevokedAtUtc))!.GetColumnName());
        Assert.Equal("superseded_by_plan_id", entity.FindProperty(nameof(SchedulePlan.SupersededByPlanId))!.GetColumnName());
        Assert.Equal("revocation_reason", entity.FindProperty(nameof(SchedulePlan.RevocationReason))!.GetColumnName());
        Assert.Contains(entity.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(x => x.Name).SequenceEqual(
                [nameof(SchedulePlan.OrganizationId), nameof(SchedulePlan.EnvironmentId)]) &&
            index.GetFilter() == "status = 'Released'");
        Assert.Contains(entity.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(x => x.Name).SequenceEqual(
                [nameof(SchedulePlan.OrganizationId), nameof(SchedulePlan.EnvironmentId), nameof(SchedulePlan.ReleaseRevision)]) &&
            index.GetFilter() == "release_revision IS NOT NULL");
    }

    private static SchemaFixture CreateFixture()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddSchedulingPostgreSqlPersistence("Host=localhost;Database=nerv_iip_schema_conventions;Username=nerv;Password=nerv");
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
