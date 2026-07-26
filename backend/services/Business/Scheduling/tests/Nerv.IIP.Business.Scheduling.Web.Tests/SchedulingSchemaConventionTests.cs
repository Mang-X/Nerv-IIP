using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Business.Scheduling.Domain;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.ScheduleOperationOverrideAggregate;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.OrderUrgencyAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Infrastructure.Urgency;
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
            typeof(OrderUrgencyArchiveBatch),
            typeof(OrderUrgencyArchiveBatchSnapshot),
            typeof(OrderUrgencyRetentionLease),
            typeof(OrderUrgencyRestoreAudit),
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

    [Fact]
    public void Scheduling_engine_trace_and_dual_input_columns_are_explicit()
    {
        using var fixture = CreateFixture();
        var plan = fixture.DbContext.Model.FindEntityType(typeof(SchedulePlan))!;
        var problem = fixture.DbContext.Model.FindEntityType(typeof(ScheduleProblemSnapshot))!;

        AssertProperty(plan, nameof(SchedulePlan.AlgorithmVersion), "algorithm_version", "character varying(64)", false, 64);
        Assert.DoesNotContain(plan.GetProperties(), property => property.GetColumnName() == "engine_version");
        AssertProperty(plan, nameof(SchedulePlan.EngineId), "engine_id", "character varying(64)", false, 64);
        AssertProperty(plan, nameof(SchedulePlan.RuleProviderId), "rule_provider_id", "character varying(96)", false, 96);
        AssertProperty(plan, nameof(SchedulePlan.RuleProfileId), "rule_profile_id", "character varying(96)", false, 96);
        AssertProperty(plan, nameof(SchedulePlan.RuleProfileVersion), "rule_profile_version", "character varying(64)", false, 64);
        AssertProperty(plan, nameof(SchedulePlan.ConstraintSourcesJson), "constraint_sources_json", "jsonb", false, null);
        AssertProperty(plan, nameof(SchedulePlan.TraceSchemaVersion), "trace_schema_version", "integer", false, null);
        AssertProperty(plan, nameof(SchedulePlan.ReplayStatus), "replay_status", "character varying(32)", false, 32);
        AssertProperty(problem, nameof(ScheduleProblemSnapshot.EngineInputJson), "engine_input_json", "jsonb", true, null);
        AssertProperty(problem, nameof(ScheduleProblemSnapshot.EngineInputFingerprint), "engine_input_fingerprint", "character varying(128)", true, 128);
    }

    [Fact]
    public void Order_urgency_archive_membership_is_scope_isolated_and_indexed()
    {
        using var fixture = CreateFixture();
        var entity = fixture.DbContext.Model.FindEntityType(typeof(OrderUrgencyArchiveBatchSnapshot))!;

        Assert.Contains(entity.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(x => x.Name).SequenceEqual(
                [nameof(OrderUrgencyArchiveBatchSnapshot.ArchiveBatchId), nameof(OrderUrgencyArchiveBatchSnapshot.Sequence)]));
        Assert.Contains(entity.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(x => x.Name).SequenceEqual(
                [nameof(OrderUrgencyArchiveBatchSnapshot.OrganizationId), nameof(OrderUrgencyArchiveBatchSnapshot.EnvironmentId), nameof(OrderUrgencyArchiveBatchSnapshot.SnapshotId)]));
    }

    private static SchemaFixture CreateFixture()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddSchedulingPostgreSqlPersistence("Host=localhost;Database=nerv_iip_schema_conventions;Username=nerv;Password=nerv");
        return new SchemaFixture(services.BuildServiceProvider());
    }

    private static void AssertProperty(
        Microsoft.EntityFrameworkCore.Metadata.IEntityType entity,
        string propertyName,
        string columnName,
        string columnType,
        bool nullable,
        int? maxLength)
    {
        var property = entity.FindProperty(propertyName)
            ?? throw new Xunit.Sdk.XunitException($"{entity.DisplayName()}.{propertyName} metadata was not found.");

        Assert.Equal(columnName, property.GetColumnName());
        Assert.Equal(columnType, property.GetColumnType());
        Assert.Equal(nullable, property.IsNullable);
        Assert.Equal(maxLength, property.GetMaxLength());
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
