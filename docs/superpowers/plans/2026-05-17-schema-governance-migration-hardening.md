# Schema Governance & Migration Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn AppHub/Ops schema governance rules into EF metadata and reusable tests before new persistent services add tables.

**Architecture:** Add a small EF Core metadata assertion helper in `Nerv.IIP.Testing`, then use AppHub/Ops service tests to enforce table comments, column comments, JSON compatibility comments, string strongly typed ID rules and service-schema migrations history configuration. Keep customer-release bundles, IAM, FileStorage and frontend work out of this stage.

**Tech Stack:** .NET 10, EF Core 10.0.8, Npgsql.EntityFrameworkCore.PostgreSQL 10.0.1, xUnit, PowerShell, EF Core local tool manifest, existing AppHub/Ops CleanDDD infrastructure projects.

---

## Completion Record

This plan starts from commit `39d6917 docs: plan schema governance hardening` on branch `codex/schema-governance-hardening`.

Known handoff note: `skills-lock.json` is dirty before this plan begins, with no text diff reported in the prior audit. Do not stage or modify it unless the user explicitly asks.

## Boundaries

1. Do not implement IAM, FileStorage, Notification, Knowledge, AI Integration or Observability tables.
2. Do not create customer-release migration bundles, installers, backup scripts or restore rehearsals.
3. Do not add frontend routes, pages, styling, component libraries or Design System decisions.
4. Do not validate GaussDB, DMDB or other provider profiles in this plan.
5. Do not require Docker or a live PostgreSQL database for schema convention tests.
6. Do not stage or revert unrelated `skills-lock.json` changes.

## File Structure Map

```text
backend/common/Testing/Nerv.IIP.Testing/
  Nerv.IIP.Testing.csproj
  EntityFramework/
    SchemaConventionAssertions.cs

backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/
  AppHubPersistenceServiceCollectionExtensions.cs
  EntityConfigurations/ApplicationEntityTypeConfiguration.cs
  EntityConfigurations/ApplicationInstanceEntityTypeConfiguration.cs
  EntityConfigurations/ManagedNodeEntityTypeConfiguration.cs
  Migrations/*

backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/
  Nerv.IIP.AppHub.Web.Tests.csproj
  AppHubSchemaConventionTests.cs

backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/
  OpsPersistenceServiceCollectionExtensions.cs
  EntityConfigurations/AuditRecordEntityTypeConfiguration.cs
  EntityConfigurations/OperationAttemptEntityTypeConfiguration.cs
  EntityConfigurations/OperationTaskEntityTypeConfiguration.cs
  Migrations/*

backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/
  Nerv.IIP.Ops.Web.Tests.csproj
  OpsSchemaConventionTests.cs

docs/architecture/
  database-schema-catalog.md
  database-schema-conventions.md
  implementation-readiness.md
  technology-stack-references.md

README.md
docs/superpowers/plans/2026-05-17-release-grade-persistence-foundation.md
```

## Task 1: Add AppHub Failing Schema Convention Test

**Files:**

- Create: `backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/AppHubSchemaConventionTests.cs`
- Modify: `backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj`

- [ ] **Step 1: Add the AppHub test project reference to shared testing**

Add this reference to `backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj`:

```xml
  <ItemGroup>
    <ProjectReference Include="..\..\..\..\common\Testing\Nerv.IIP.Testing\Nerv.IIP.Testing.csproj" />
    <ProjectReference Include="..\..\src\Nerv.IIP.AppHub.Web\Nerv.IIP.AppHub.Web.csproj" />
  </ItemGroup>
```

If the existing Web project reference already exists in an `ItemGroup`, add only the `Nerv.IIP.Testing` reference beside it.

- [ ] **Step 2: Write the failing AppHub schema convention test**

Create `AppHubSchemaConventionTests.cs`:

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationAggregate;
using Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationInstanceAggregate;
using Nerv.IIP.AppHub.Domain.AggregatesModel.ManagedNodeAggregate;
using Nerv.IIP.AppHub.Infrastructure;
using Nerv.IIP.Testing.EntityFramework;
using AppHubApplication = Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationAggregate.Application;

namespace Nerv.IIP.AppHub.Web.Tests;

public sealed class AppHubSchemaConventionTests
{
    [Fact]
    public void AppHub_schema_metadata_follows_database_conventions()
    {
        using var fixture = CreateFixture();
        var businessEntities = new[]
        {
            typeof(AppHubApplication),
            typeof(ApplicationVersion),
            typeof(ManagedNode),
            typeof(ApplicationInstance),
            typeof(InstanceHeartbeat),
            typeof(InstanceStateHistory),
            typeof(InstanceStatusChange),
            typeof(RegistrationIdempotency),
        };

        var jsonColumns = new[]
        {
            new JsonColumnRule(typeof(ApplicationInstance), nameof(ApplicationInstance.Metadata)),
            new JsonColumnRule(typeof(ApplicationInstance), nameof(ApplicationInstance.Capabilities)),
        };

        var failures = new List<string>();
        failures.AddRange(SchemaConventionAssertions.BusinessTablesHaveComments(fixture.DbContext, "AppHub", businessEntities));
        failures.AddRange(SchemaConventionAssertions.BusinessColumnsHaveComments(fixture.DbContext, "AppHub", businessEntities));
        failures.AddRange(SchemaConventionAssertions.JsonColumnsHaveCompatibilityComments(fixture.DbContext, "AppHub", jsonColumns));
        failures.AddRange(SchemaConventionAssertions.MigrationsHistoryTableIsInSchema(fixture.DbContext, "AppHub", "apphub"));

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
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql("Host=localhost;Database=nerv_iip_schema_conventions;Username=nerv;Password=nerv"));

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
```

- [ ] **Step 3: Run the AppHub schema test and verify it fails**

Run:

```powershell
dotnet test backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj --filter FullyQualifiedName~AppHubSchemaConventionTests
```

Expected: FAIL at compile time because `Nerv.IIP.Testing.EntityFramework.SchemaConventionAssertions` and `JsonColumnRule` do not exist yet.

## Task 2: Add Reusable EF Schema Convention Assertions

**Files:**

- Modify: `backend/common/Testing/Nerv.IIP.Testing/Nerv.IIP.Testing.csproj`
- Delete: `backend/common/Testing/Nerv.IIP.Testing/Class1.cs`
- Create: `backend/common/Testing/Nerv.IIP.Testing/EntityFramework/SchemaConventionAssertions.cs`

- [ ] **Step 1: Add EF Core relational reference to shared testing**

Modify `Nerv.IIP.Testing.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Remove the empty starter class**

Delete `backend/common/Testing/Nerv.IIP.Testing/Class1.cs`.

- [ ] **Step 3: Add schema convention helper**

Create `EntityFramework/SchemaConventionAssertions.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Nerv.IIP.Testing.EntityFramework;

public sealed record JsonColumnRule(Type EntityType, string PropertyName);

public sealed record StringKeyRule(Type EntityType, string PropertyName);

public static class SchemaConventionAssertions
{
    public static IReadOnlyList<string> BusinessTablesHaveComments(DbContext dbContext, string serviceName, IEnumerable<Type> businessEntityTypes)
    {
        var failures = new List<string>();
        foreach (var entityType in ResolveEntityTypes(dbContext, serviceName, businessEntityTypes))
        {
            if (string.IsNullOrWhiteSpace(entityType.GetComment()))
            {
                failures.Add($"{serviceName}: table '{FormatTable(entityType)}' mapped from '{entityType.ClrType.Name}' is missing a table comment.");
            }
        }

        return failures;
    }

    public static IReadOnlyList<string> BusinessColumnsHaveComments(DbContext dbContext, string serviceName, IEnumerable<Type> businessEntityTypes)
    {
        var failures = new List<string>();
        foreach (var entityType in ResolveEntityTypes(dbContext, serviceName, businessEntityTypes))
        {
            var storeObject = StoreObjectIdentifier.Table(entityType.GetTableName()!, entityType.GetSchema());
            foreach (var property in entityType.GetProperties())
            {
                if (property.IsShadowProperty() || property.GetColumnName(storeObject) is null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(property.GetComment()))
                {
                    failures.Add($"{serviceName}: column '{FormatTable(entityType)}.{property.GetColumnName(storeObject)}' mapped from '{entityType.ClrType.Name}.{property.Name}' is missing a column comment.");
                }
            }
        }

        return failures;
    }

    public static IReadOnlyList<string> JsonColumnsHaveCompatibilityComments(DbContext dbContext, string serviceName, IEnumerable<JsonColumnRule> rules)
    {
        var failures = new List<string>();
        foreach (var rule in rules)
        {
            var entityType = ResolveEntityType(dbContext, serviceName, rule.EntityType);
            var property = entityType.FindProperty(rule.PropertyName);
            if (property is null)
            {
                failures.Add($"{serviceName}: JSON rule references missing property '{rule.EntityType.Name}.{rule.PropertyName}'.");
                continue;
            }

            var comment = property.GetComment();
            var normalized = comment?.ToLowerInvariant() ?? string.Empty;
            var requiredTokens = new[] { "json", "producer", "consumer", "compatib" };
            foreach (var token in requiredTokens)
            {
                if (!normalized.Contains(token, StringComparison.Ordinal))
                {
                    failures.Add($"{serviceName}: JSON column '{rule.EntityType.Name}.{rule.PropertyName}' comment must mention JSON format, producer, consumer and compatibility. Current comment: '{comment ?? "<missing>"}'.");
                    break;
                }
            }
        }

        return failures;
    }

    public static IReadOnlyList<string> StringStronglyTypedKeysAreExplicit(DbContext dbContext, string serviceName, IEnumerable<StringKeyRule> rules)
    {
        var failures = new List<string>();
        foreach (var rule in rules)
        {
            var entityType = ResolveEntityType(dbContext, serviceName, rule.EntityType);
            var property = entityType.FindProperty(rule.PropertyName);
            if (property is null)
            {
                failures.Add($"{serviceName}: string key rule references missing property '{rule.EntityType.Name}.{rule.PropertyName}'.");
                continue;
            }

            if (property.ValueGenerated != ValueGenerated.Never)
            {
                failures.Add($"{serviceName}: string key '{rule.EntityType.Name}.{rule.PropertyName}' must use ValueGeneratedNever().");
            }

            if (property.GetMaxLength() is null or <= 0)
            {
                failures.Add($"{serviceName}: string key '{rule.EntityType.Name}.{rule.PropertyName}' must set HasMaxLength(...).");
            }
        }

        return failures;
    }

    public static IReadOnlyList<string> MigrationsHistoryTableIsInSchema(DbContext dbContext, string serviceName, string expectedSchema)
    {
        var options = dbContext.GetService<IDbContextOptions>();
        var relationalOptions = options.Extensions.OfType<RelationalOptionsExtension>().LastOrDefault();
        var failures = new List<string>();

        if (relationalOptions is null)
        {
            failures.Add($"{serviceName}: DbContext is missing relational options.");
            return failures;
        }

        if (!string.Equals(relationalOptions.MigrationsHistoryTableName, "__EFMigrationsHistory", StringComparison.Ordinal))
        {
            failures.Add($"{serviceName}: migrations history table must be '__EFMigrationsHistory' but was '{relationalOptions.MigrationsHistoryTableName ?? "<default>"}'.");
        }

        if (!string.Equals(relationalOptions.MigrationsHistoryTableSchema, expectedSchema, StringComparison.Ordinal))
        {
            failures.Add($"{serviceName}: migrations history schema must be '{expectedSchema}' but was '{relationalOptions.MigrationsHistoryTableSchema ?? "<default>"}'.");
        }

        return failures;
    }

    private static IEnumerable<IEntityType> ResolveEntityTypes(DbContext dbContext, string serviceName, IEnumerable<Type> entityTypes)
    {
        foreach (var entityType in entityTypes)
        {
            yield return ResolveEntityType(dbContext, serviceName, entityType);
        }
    }

    private static IEntityType ResolveEntityType(DbContext dbContext, string serviceName, Type entityType)
    {
        return dbContext.Model.FindEntityType(entityType)
            ?? throw new InvalidOperationException($"{serviceName}: entity type '{entityType.FullName}' is not part of the EF model.");
    }

    private static string FormatTable(IEntityType entityType)
    {
        var schema = entityType.GetSchema();
        var table = entityType.GetTableName();
        return string.IsNullOrWhiteSpace(schema) ? table ?? entityType.ClrType.Name : $"{schema}.{table}";
    }
}
```

- [ ] **Step 4: Run the AppHub schema test again**

Run:

```powershell
dotnet test backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj --filter FullyQualifiedName~AppHubSchemaConventionTests
```

Expected: FAIL with convention messages for missing AppHub table comments, JSON compatibility comments and AppHub migrations history schema.

## Task 3: Add Ops Failing Schema Convention Test

**Files:**

- Create: `backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/OpsSchemaConventionTests.cs`
- Modify: `backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj`

- [ ] **Step 1: Add the Ops test project reference to shared testing**

Add this reference beside the existing Ops test project references:

```xml
    <ProjectReference Include="..\..\..\..\common\Testing\Nerv.IIP.Testing\Nerv.IIP.Testing.csproj" />
```

- [ ] **Step 2: Write the failing Ops schema convention test**

Create `OpsSchemaConventionTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Ops.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Ops.Infrastructure;
using Nerv.IIP.Testing.EntityFramework;

namespace Nerv.IIP.Ops.Web.Tests;

public sealed class OpsSchemaConventionTests
{
    [Fact]
    public void Ops_schema_metadata_follows_database_conventions()
    {
        using var fixture = CreateFixture();
        var businessEntities = new[]
        {
            typeof(OperationTask),
            typeof(OperationAttempt),
            typeof(AuditRecord),
        };

        var jsonColumns = new[]
        {
            new JsonColumnRule(typeof(OperationTask), nameof(OperationTask.ParametersJson)),
            new JsonColumnRule(typeof(OperationAttempt), nameof(OperationAttempt.FailureJson)),
        };

        var stringKeys = new[]
        {
            new StringKeyRule(typeof(OperationTask), nameof(OperationTask.Id)),
            new StringKeyRule(typeof(OperationAttempt), nameof(OperationAttempt.Id)),
            new StringKeyRule(typeof(AuditRecord), nameof(AuditRecord.Id)),
        };

        var failures = new List<string>();
        failures.AddRange(SchemaConventionAssertions.BusinessTablesHaveComments(fixture.DbContext, "Ops", businessEntities));
        failures.AddRange(SchemaConventionAssertions.BusinessColumnsHaveComments(fixture.DbContext, "Ops", businessEntities));
        failures.AddRange(SchemaConventionAssertions.JsonColumnsHaveCompatibilityComments(fixture.DbContext, "Ops", jsonColumns));
        failures.AddRange(SchemaConventionAssertions.StringStronglyTypedKeysAreExplicit(fixture.DbContext, "Ops", stringKeys));
        failures.AddRange(SchemaConventionAssertions.MigrationsHistoryTableIsInSchema(fixture.DbContext, "Ops", "ops"));

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static SchemaFixture CreateFixture()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly);
        });
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql("Host=localhost;Database=nerv_iip_schema_conventions;Username=nerv;Password=nerv"));

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
```

- [ ] **Step 3: Run the Ops schema test and verify it fails**

Run:

```powershell
dotnet test backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj --filter FullyQualifiedName~OpsSchemaConventionTests
```

Expected: FAIL with convention messages for missing Ops table comments, insufficient JSON comments and Ops migrations history schema.

## Task 4: Harden AppHub Schema Metadata

**Files:**

- Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/AppHubPersistenceServiceCollectionExtensions.cs`
- Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/EntityConfigurations/ApplicationEntityTypeConfiguration.cs`
- Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/EntityConfigurations/ApplicationInstanceEntityTypeConfiguration.cs`
- Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/EntityConfigurations/ManagedNodeEntityTypeConfiguration.cs`
- Create: `backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/Migrations/*SchemaGovernanceMetadata*.cs`
- Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`

- [ ] **Step 1: Configure AppHub migrations history schema**

Change PostgreSQL registration to:

```csharp
services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql =>
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "apphub")));
```

- [ ] **Step 2: Add AppHub table comments**

Use these table comments in AppHub entity configurations:

```csharp
builder.ToTable("applications", table => table.HasComment("AppHub application catalog aggregate roots scoped by organization and environment."));
builder.ToTable("application_versions", table => table.HasComment("AppHub application versions owned by an application catalog aggregate."));
builder.ToTable("managed_nodes", table => table.HasComment("AppHub managed connector host or runtime node catalog entries."));
builder.ToTable("application_instances", table => table.HasComment("AppHub managed application instance aggregate roots reported by connector hosts."));
builder.ToTable("instance_heartbeat", table => table.HasComment("AppHub latest heartbeat facts for managed application instances."));
builder.ToTable("instance_state_history", table => table.HasComment("AppHub observed application instance state history for diagnostics and status timelines."));
builder.ToTable("instance_status_changes", table => table.HasComment("AppHub reported status transition history for managed application instances."));
builder.ToTable("registration_idempotency", table => table.HasComment("AppHub registration idempotency records used to deduplicate connector retries."));
```

- [ ] **Step 3: Add AppHub JSON compatibility comments**

Change `Metadata` and `Capabilities` property comments:

```csharp
builder.Property(x => x.Metadata)
    .HasConversion(value => EntityConfigurationJson.SerializeDictionary(value), value => EntityConfigurationJson.DeserializeDictionary(value))
    .HasComment("JSON dictionary produced by Connector Host registration and state reporting, consumed by AppHub and Gateway readers; additive optional keys are compatible, removing or changing key semantics requires Connector Protocol versioning.")
    .Metadata.SetValueComparer(EntityConfigurationJson.DictionaryComparer);

builder.Property(x => x.Capabilities)
    .HasConversion(value => EntityConfigurationJson.SerializeCapabilities(value), value => EntityConfigurationJson.DeserializeCapabilities(value))
    .HasComment("JSON capability descriptors produced by Connector Host discovery, consumed by Gateway and Ops action routing; additive capabilities are compatible, removing or changing action semantics requires Connector Protocol versioning.")
    .Metadata.SetValueComparer(EntityConfigurationJson.CapabilitiesComparer);
```

- [ ] **Step 4: Run AppHub schema convention test**

Run:

```powershell
dotnet test backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj --filter FullyQualifiedName~AppHubSchemaConventionTests
```

Expected: PASS.

- [ ] **Step 5: Generate AppHub schema governance migration**

Run:

```powershell
$env:Persistence__Provider = "PostgreSQL"
$env:ConnectionStrings__AppHubDb = "Host=localhost;Port=15432;Database=nerv_iip_apphub_schema_governance_design;Username=nerv;Password=nerv"
dotnet tool restore
dotnet tool run dotnet-ef migrations add SchemaGovernanceMetadata --project backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/Nerv.IIP.AppHub.Infrastructure.csproj --startup-project backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Nerv.IIP.AppHub.Web.csproj --context Nerv.IIP.AppHub.Infrastructure.ApplicationDbContext --output-dir Migrations
Remove-Item Env:\Persistence__Provider -ErrorAction SilentlyContinue
Remove-Item Env:\ConnectionStrings__AppHubDb -ErrorAction SilentlyContinue
```

Expected: AppHub creates a new migration and updates `ApplicationDbContextModelSnapshot.cs`. The migration should contain table/comment metadata changes and no new business tables.

## Task 5: Harden Ops Schema Metadata

**Files:**

- Modify: `backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/OpsPersistenceServiceCollectionExtensions.cs`
- Modify: `backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/EntityConfigurations/AuditRecordEntityTypeConfiguration.cs`
- Modify: `backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/EntityConfigurations/OperationAttemptEntityTypeConfiguration.cs`
- Modify: `backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/EntityConfigurations/OperationTaskEntityTypeConfiguration.cs`
- Create: `backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/Migrations/*SchemaGovernanceMetadata*.cs`
- Modify: `backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`

- [ ] **Step 1: Configure Ops migrations history schema**

Change PostgreSQL registration to:

```csharp
services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql =>
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "ops")));
```

- [ ] **Step 2: Add Ops table comments**

Use these table comments in Ops entity configurations:

```csharp
builder.ToTable("operation_tasks", table => table.HasComment("Ops operation task aggregate roots requested through Gateway and executed by connector hosts."));
builder.ToTable("operation_attempts", table => table.HasComment("Ops operation execution attempts created when connector hosts claim operation tasks."));
builder.ToTable("audit_records", table => table.HasComment("Ops audit records for operation task lifecycle events and user-visible traceability."));
```

- [ ] **Step 3: Add Ops JSON compatibility comments**

Change `ParametersJson` and `FailureJson` comments:

```csharp
builder.Property(x => x.ParametersJson)
    .IsRequired()
    .HasComment("JSON operation parameter dictionary produced by Gateway and Ops task creation, consumed by Connector Host execution; additive optional keys are compatible, required key or semantic changes require Ops contract versioning.");

builder.Property(x => x.FailureJson)
    .HasComment("JSON failure details produced by Connector Host execution, consumed by Ops and Gateway diagnostics; additive optional keys are compatible, removing or changing key semantics requires Ops contract versioning.");
```

- [ ] **Step 4: Run Ops schema convention test**

Run:

```powershell
dotnet test backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj --filter FullyQualifiedName~OpsSchemaConventionTests
```

Expected: PASS.

- [ ] **Step 5: Generate Ops schema governance migration**

Run:

```powershell
$env:Persistence__Provider = "PostgreSQL"
$env:ConnectionStrings__OpsDb = "Host=localhost;Port=15432;Database=nerv_iip_ops_schema_governance_design;Username=nerv;Password=nerv"
dotnet tool restore
dotnet tool run dotnet-ef migrations add SchemaGovernanceMetadata --project backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/Nerv.IIP.Ops.Infrastructure.csproj --startup-project backend/services/Ops/src/Nerv.IIP.Ops.Web/Nerv.IIP.Ops.Web.csproj --context Nerv.IIP.Ops.Infrastructure.ApplicationDbContext --output-dir Migrations
Remove-Item Env:\Persistence__Provider -ErrorAction SilentlyContinue
Remove-Item Env:\ConnectionStrings__OpsDb -ErrorAction SilentlyContinue
```

Expected: Ops creates a new migration and updates `ApplicationDbContextModelSnapshot.cs`. The migration should contain table/comment metadata changes and no new business tables.

## Task 6: Update Architecture And Handoff Documentation

**Files:**

- Modify: `README.md`
- Modify: `docs/architecture/database-schema-catalog.md`
- Modify: `docs/architecture/database-schema-conventions.md`
- Modify: `docs/architecture/implementation-readiness.md`
- Modify: `docs/architecture/technology-stack-references.md`
- Modify: `docs/superpowers/plans/2026-05-17-release-grade-persistence-foundation.md`

- [ ] **Step 1: Update README stage handoff**

Change the repository entry that currently says the current fourth-stage worktree to:

```markdown
- 当前工作树：`codex/schema-governance-hardening`，从第五阶段迁移发布底座之后继续推进 schema governance hardening。
```

Add the sixth-stage plan to the implementation plans list:

```markdown
6. docs/superpowers/plans/2026-05-17-schema-governance-migration-hardening.md
```

Add a current-status sentence:

```markdown
第六阶段 Schema Governance & Migration Hardening 规划已启动，目标是在 IAM、FileStorage 等新持久化服务开工前，把 AppHub/Ops 的表注释、JSON 兼容注释、migrations history schema 和 schema convention tests 固化为门禁。
```

- [ ] **Step 2: Update technology stack current baseline**

Change the repository table current baseline row to:

```markdown
| Current baseline | 第五阶段 Release-grade Persistence Foundation 已合入；本计划是历史执行记录，不是当前状态源。当前状态见 [implementation-readiness.md](../../architecture/implementation-readiness.md)，原始设计输入见 [schema-governance design](../specs/2026-05-17-schema-governance-migration-hardening-design.md)。 |
```

- [ ] **Step 3: Update schema catalog known gaps**

For AppHub and Ops, remove gaps that this plan closes:

```markdown
Known gaps:

1. CAP system tables 当前只在 catalog 中标记 system-owned，后续可补 table comment 便于数据库工具展示。
```

Keep future-service rows unchanged.

- [ ] **Step 4: Update schema conventions enforcement status**

In `Schema Convention Tests`, state that AppHub/Ops now enforce the first six checks and future persistent services must adopt the same helper:

```markdown
AppHub/Ops 已通过 `Nerv.IIP.Testing` 中的 schema convention helper 覆盖 business table comment、business column comment、JSON/text 兼容注释、string ID 约束和 service-schema `__EFMigrationsHistory`。后续 IAM、FileStorage、Notification、Knowledge、AI Integration 和 Observability 索引建表时必须复用同一类测试。
```

Update current known gaps so AppHub/Ops closed items are not listed as open after implementation:

```markdown
1. CAP system tables 当前只在 DbContext 中配置表名和主键，后续应至少补表注释或在 catalog 中保持 system-owned 标记。
2. IAM、FileStorage、Notification、Knowledge、AI Integration 和 Observability 索引尚未建表；首次建表前必须先补 catalog 草案和 schema convention tests。
```

- [ ] **Step 5: Update implementation readiness**

Add a sixth-stage current conclusion:

```markdown
18. 第六阶段 Schema Governance & Migration Hardening 用 AppHub/Ops 作为已迁移服务样本，把业务表注释、业务列注释、JSON/text 兼容注释、string ID 约束和 service-schema migrations history 配置固化为测试门禁；IAM/FileStorage 等新增持久化服务开工前必须沿用该门禁。
```

Add the new plan to the plan list and note that customer release bundles remain future work.

- [ ] **Step 6: Mark fifth-stage plan as historical completion**

At the top of `docs/superpowers/plans/2026-05-17-release-grade-persistence-foundation.md`, after the completion record intro, add:

```markdown
> Historical note: the unchecked task list below is preserved as the original execution plan. The stage is complete; use the Completion Record and git history as the source of truth for status.
```

Do not rewrite the whole historical task list.

## Task 7: Verification

**Files:**

- No new files unless a previous task uncovers a missing test or doc.

- [ ] **Step 1: Run targeted schema tests**

Run:

```powershell
dotnet test backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj --filter FullyQualifiedName~AppHubSchemaConventionTests
dotnet test backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj --filter FullyQualifiedName~OpsSchemaConventionTests
```

Expected: both exit `0`.

- [ ] **Step 2: Run full backend solution tests**

Run:

```powershell
dotnet test backend/Nerv.IIP.sln
```

Expected: exit `0`.

- [ ] **Step 3: Run fifth-stage persistence verification**

Run because migrations and PostgreSQL history configuration changed:

```powershell
pwsh scripts/verify-fifth-slice-persistence-foundation.ps1
```

Expected final line:

```text
Fifth slice release-grade persistence foundation verified.
```

- [ ] **Step 4: Run repository whitespace check**

Run:

```powershell
git diff --check
```

Expected: exit `0`.

- [ ] **Step 5: Confirm final git status only includes intended files plus pre-existing skills lock**

Run:

```powershell
git status --short
```

Expected: intended schema governance files are modified/added. `skills-lock.json` may still appear as a pre-existing unstaged modification; do not stage it.

## Execution Order

1. Task 1 first to establish AppHub red test.
2. Task 2 second because both service tests depend on shared assertions.
3. Task 3 third to establish Ops red test.
4. Tasks 4 and 5 can run independently after Task 2 if assigned to separate workers, because AppHub and Ops write sets are disjoint.
5. Task 6 runs after schema tests pass so documentation reflects the real closed gaps.
6. Task 7 runs last.

## Self Review

Spec coverage:

1. AppHub/Ops table comments are covered by Tasks 4 and 5.
2. JSON/text compatibility comments are covered by Tasks 4 and 5.
3. Migrations history schema configuration is covered by Tasks 4 and 5.
4. Reusable convention tests are covered by Tasks 1, 2 and 3.
5. Documentation and handoff drift are covered by Task 6.
6. Full verification is covered by Task 7.

Red-flag scan:

1. No red-flag markers or empty sections remain.
2. Every code-changing task names exact files and concrete snippets.
3. Every verification step has a concrete command and expected result.

Type consistency:

1. Helper type names are `SchemaConventionAssertions`, `JsonColumnRule` and `StringKeyRule` throughout.
2. AppHub schema name is `apphub` throughout.
3. Ops schema name is `ops` throughout.
4. New migration name is `SchemaGovernanceMetadata` for both services.
