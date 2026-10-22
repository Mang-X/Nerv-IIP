using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
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
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ToolingAssetAggregate;
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
            typeof(ToolingAuditEntry),
        };

        var failures = new List<string>();
        Assert.Equal(MasterDataFacts.Schema, fixture.DbContext.Model.GetDefaultSchema());
        failures.AddRange(SchemaConventionAssertions.BusinessTablesHaveComments(fixture.DbContext, MasterDataFacts.ServiceName, businessEntities));
        failures.AddRange(SchemaConventionAssertions.BusinessColumnsHaveComments(fixture.DbContext, MasterDataFacts.ServiceName, businessEntities));
        failures.AddRange(SchemaConventionAssertions.MigrationsHistoryTableIsInSchema(fixture.DbContext, MasterDataFacts.ServiceName, MasterDataFacts.Schema));

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Tooling_audit_schema_has_append_only_identity_and_target_indexes()
    {
        using var fixture = CreateFixture();
        var entityType = fixture.DbContext.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ToolingAuditEntry));
        Assert.NotNull(entityType);

        var operationIndex = Assert.Single(entityType.GetIndexes(), candidate =>
            candidate.IsUnique &&
            candidate.Properties.Select(property => property.Name)
                .SequenceEqual(["OrganizationId", "EnvironmentId", "OperationId"]));
        Assert.Equal("ux_tooling_audit_operation", operationIndex.GetDatabaseName());

        var targetIndex = Assert.Single(entityType.GetIndexes(), candidate =>
            !candidate.IsUnique &&
            candidate.Properties.Select(property => property.Name)
                .SequenceEqual(["OrganizationId", "EnvironmentId", "ToolingCode", "OccurredAtUtc"]));
        Assert.Equal("ix_tooling_audit_target_time", targetIndex.GetDatabaseName());
        var constraints = entityType.GetCheckConstraints().ToDictionary(constraint => constraint.Name!);
        Assert.Equal(
            "\"OperationKind\" IN ('tooling-register', 'tooling-status', 'tooling-usage')",
            constraints["ck_tooling_audit_operation_kind"].Sql);
        Assert.Equal(
            "(\"OperationKind\" = 'tooling-register' AND \"BeforeStatus\" IS NULL AND \"AfterStatus\" = 'Available' AND \"BeforeUsageCount\" IS NULL AND \"AfterUsageCount\" = 0 AND \"UsageDelta\" IS NULL AND \"Reason\" IS NULL) OR (\"OperationKind\" = 'tooling-status' AND \"BeforeStatus\" IS NOT NULL AND \"AfterStatus\" IS NOT NULL AND \"BeforeUsageCount\" IS NULL AND \"AfterUsageCount\" IS NULL AND \"UsageDelta\" IS NULL AND \"Reason\" IS NOT NULL) OR (\"OperationKind\" = 'tooling-usage' AND \"BeforeStatus\" IS NULL AND \"AfterStatus\" IS NULL AND \"BeforeUsageCount\" >= 0 AND \"AfterUsageCount\" = \"BeforeUsageCount\" + \"UsageDelta\" AND \"UsageDelta\" > 0 AND \"Reason\" IS NULL)",
            constraints["ck_tooling_audit_summary_shape"].Sql);
    }

    [Fact]
    public void Tooling_audit_migration_installs_database_append_only_trigger()
    {
        using var fixture = CreateFixture();
        var script = fixture.DbContext.GetService<IMigrator>().GenerateScript(
            "20260728232043_AddPrincipalScopeContextAudit",
            "20260825081539_AddToolingOperationAudit");

        Assert.Contains("CREATE TRIGGER trg_tooling_audit_append_only", script, StringComparison.Ordinal);
        Assert.Contains("RETURNS trigger", script, StringComparison.Ordinal);
        Assert.Contains("LANGUAGE plpgsql", script, StringComparison.Ordinal);
        Assert.Contains(
            "RAISE EXCEPTION 'business_masterdata.tooling_audit_entries is append-only'",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "BEFORE UPDATE OR DELETE ON business_masterdata.tooling_audit_entries",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "EXECUTE FUNCTION business_masterdata.reject_tooling_audit_mutation()",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Tooling_audit_down_script_rejects_existing_facts_before_destructive_statements()
    {
        using var fixture = CreateFixture();
        var script = fixture.DbContext.GetService<IMigrator>().GenerateScript(
            "20260825081539_AddToolingOperationAudit",
            "20260728232043_AddPrincipalScopeContextAudit");

        const string guard = "IF EXISTS (\n        SELECT 1\n        FROM business_masterdata.tooling_audit_entries\n    ) THEN";
        Assert.Contains(guard, script, StringComparison.Ordinal);
        Assert.Contains(
            "RAISE EXCEPTION\n            'Cannot downgrade AddToolingOperationAudit while tooling audit facts exist. Preserve the evidence and roll forward with a corrective migration.'",
            script,
            StringComparison.Ordinal);
        var guardPosition = script.IndexOf(guard, StringComparison.Ordinal);
        var dropTablePosition = script.IndexOf(
            "DROP TABLE business_masterdata.tooling_audit_entries",
            StringComparison.Ordinal);
        var dropFunctionPosition = script.IndexOf(
            "DROP FUNCTION IF EXISTS business_masterdata.reject_tooling_audit_mutation()",
            StringComparison.Ordinal);
        Assert.True(guardPosition >= 0 && guardPosition < dropTablePosition, script);
        Assert.True(dropTablePosition < dropFunctionPosition, script);
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
