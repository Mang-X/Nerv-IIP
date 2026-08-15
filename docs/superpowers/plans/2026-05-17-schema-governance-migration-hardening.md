# Schema 治理与迁移加固实施计划

> **面向智能体执行者：** 必须使用子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐项实施本计划。各步骤使用复选框（`- [ ]`）语法跟踪进度。

**目标：** 在新的持久化服务添加数据表之前，将 AppHub/Ops schema 治理规则转化为 EF 元数据和可复用测试。

**架构：** 在 `Nerv.IIP.Testing` 中添加一个小型 EF Core 元数据断言辅助库，然后通过 AppHub/Ops 服务测试强制执行表注释、列注释、JSON 兼容性注释、字符串强类型 ID 规则和服务 schema 迁移历史配置。本阶段不包含客户发布包、IAM、FileStorage 和前端工作。

**技术栈：** .NET 10、EF Core 10.0.8、Npgsql.EntityFrameworkCore.PostgreSQL 10.0.1、xUnit、PowerShell、EF Core 本地工具清单、现有 AppHub/Ops CleanDDD Infrastructure 项目。

---

## 完成记录

本计划从提交 `39d6917 docs: plan schema governance hardening` 开始，该提交位于分支 `codex/schema-governance-hardening` 上。

已知交接说明：本计划开始前 `skills-lock.json` 已处于脏状态，先前审核未报告文本差异。除非用户明确要求，否则不得暂存或修改该文件。

## 边界

1. 不得实施 IAM、FileStorage、Notification、Knowledge、AI Integration 或 Observability 数据表。
2. 不得创建客户发布迁移包、安装程序、备份脚本或恢复演练。
3. 不得添加前端路由、页面、样式、组件库或设计系统决策。
4. 本计划不得验证 GaussDB、DMDB 或其他 provider profile。
5. schema 约定测试不得依赖 Docker 或运行中的 PostgreSQL 数据库。
6. 不得暂存或还原无关的 `skills-lock.json` 变更。

## 文件结构图

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

## 任务 1：添加预期失败的 AppHub Schema 约定测试

**文件：**

- 创建：`backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/AppHubSchemaConventionTests.cs`
- 修改：`backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj`

- [ ] **步骤 1：为 AppHub 测试项目添加共享测试引用**

将此引用添加到 `backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj`：

```xml
  <ItemGroup>
    <ProjectReference Include="..\..\..\..\common\Testing\Nerv.IIP.Testing\Nerv.IIP.Testing.csproj" />
    <ProjectReference Include="..\..\src\Nerv.IIP.AppHub.Web\Nerv.IIP.AppHub.Web.csproj" />
  </ItemGroup>
```

如果现有 Web 项目引用已存在于 `ItemGroup` 中，只需在其旁边添加 `Nerv.IIP.Testing` 引用。

- [ ] **步骤 2：编写预期失败的 AppHub schema 约定测试**

创建 `AppHubSchemaConventionTests.cs`：

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

- [ ] **步骤 3：运行 AppHub schema 测试并验证其失败**

运行：

```powershell
dotnet test backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj --filter FullyQualifiedName~AppHubSchemaConventionTests
```

预期结果：编译时失败，因为 `Nerv.IIP.Testing.EntityFramework.SchemaConventionAssertions` 和 `JsonColumnRule` 尚不存在。

## 任务 2：添加可复用的 EF Schema 约定断言

**文件：**

- 修改：`backend/common/Testing/Nerv.IIP.Testing/Nerv.IIP.Testing.csproj`
- 删除：`backend/common/Testing/Nerv.IIP.Testing/Class1.cs`
- 创建：`backend/common/Testing/Nerv.IIP.Testing/EntityFramework/SchemaConventionAssertions.cs`

- [ ] **步骤 1：为共享测试添加 EF Core relational 引用**

修改 `Nerv.IIP.Testing.csproj`：

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

- [ ] **步骤 2：移除空的初始类**

删除 `backend/common/Testing/Nerv.IIP.Testing/Class1.cs`。

- [ ] **步骤 3：添加 schema 约定辅助库**

创建 `EntityFramework/SchemaConventionAssertions.cs`：

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

- [ ] **步骤 4：再次运行 AppHub schema 测试**

运行：

```powershell
dotnet test backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj --filter FullyQualifiedName~AppHubSchemaConventionTests
```

预期结果：失败，并报告 AppHub 表注释、JSON 兼容性注释和 AppHub 迁移历史 schema 缺失的约定消息。

## 任务 3：添加预期失败的 Ops Schema 约定测试

**文件：**

- 创建：`backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/OpsSchemaConventionTests.cs`
- 修改：`backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj`

- [ ] **步骤 1：为 Ops 测试项目添加共享测试引用**

在现有 Ops 测试项目引用旁添加此引用：

```xml
    <ProjectReference Include="..\..\..\..\common\Testing\Nerv.IIP.Testing\Nerv.IIP.Testing.csproj" />
```

- [ ] **步骤 2：编写预期失败的 Ops schema 约定测试**

创建 `OpsSchemaConventionTests.cs`：

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

- [ ] **步骤 3：运行 Ops schema 测试并验证其失败**

运行：

```powershell
dotnet test backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj --filter FullyQualifiedName~OpsSchemaConventionTests
```

预期结果：失败，并报告 Ops 表注释缺失、JSON 注释不足和 Ops 迁移历史 schema 缺失的约定消息。

## 任务 4：加固 AppHub Schema 元数据

**文件：**

- 修改：`backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/AppHubPersistenceServiceCollectionExtensions.cs`
- 修改：`backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/EntityConfigurations/ApplicationEntityTypeConfiguration.cs`
- 修改：`backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/EntityConfigurations/ApplicationInstanceEntityTypeConfiguration.cs`
- 修改：`backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/EntityConfigurations/ManagedNodeEntityTypeConfiguration.cs`
- 创建：`backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/Migrations/*SchemaGovernanceMetadata*.cs`
- 修改：`backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`

- [ ] **步骤 1：配置 AppHub 迁移历史 schema**

将 PostgreSQL 注册更改为：

```csharp
services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql =>
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "apphub")));
```

- [ ] **步骤 2：添加 AppHub 表注释**

在 AppHub 实体配置中使用以下表注释：

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

- [ ] **步骤 3：添加 AppHub JSON 兼容性注释**

更改 `Metadata` 和 `Capabilities` 属性注释：

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

- [ ] **步骤 4：运行 AppHub schema 约定测试**

运行：

```powershell
dotnet test backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj --filter FullyQualifiedName~AppHubSchemaConventionTests
```

预期结果：通过。

- [ ] **步骤 5：生成 AppHub schema 治理迁移**

运行：

```powershell
$env:Persistence__Provider = "PostgreSQL"
$env:ConnectionStrings__AppHubDb = "Host=localhost;Port=15432;Database=nerv_iip_apphub_schema_governance_design;Username=nerv;Password=nerv"
dotnet tool restore
dotnet tool run dotnet-ef migrations add SchemaGovernanceMetadata --project backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/Nerv.IIP.AppHub.Infrastructure.csproj --startup-project backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Nerv.IIP.AppHub.Web.csproj --context Nerv.IIP.AppHub.Infrastructure.ApplicationDbContext --output-dir Migrations
Remove-Item Env:\Persistence__Provider -ErrorAction SilentlyContinue
Remove-Item Env:\ConnectionStrings__AppHubDb -ErrorAction SilentlyContinue
```

预期结果：AppHub 创建新迁移并更新 `ApplicationDbContextModelSnapshot.cs`。迁移应包含表/注释元数据变更，且不新增业务表。

## 任务 5：加固 Ops Schema 元数据

**文件：**

- 修改：`backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/OpsPersistenceServiceCollectionExtensions.cs`
- 修改：`backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/EntityConfigurations/AuditRecordEntityTypeConfiguration.cs`
- 修改：`backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/EntityConfigurations/OperationAttemptEntityTypeConfiguration.cs`
- 修改：`backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/EntityConfigurations/OperationTaskEntityTypeConfiguration.cs`
- 创建：`backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/Migrations/*SchemaGovernanceMetadata*.cs`
- 修改：`backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`

- [ ] **步骤 1：配置 Ops 迁移历史 schema**

将 PostgreSQL 注册更改为：

```csharp
services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql =>
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "ops")));
```

- [ ] **步骤 2：添加 Ops 表注释**

在 Ops 实体配置中使用以下表注释：

```csharp
builder.ToTable("operation_tasks", table => table.HasComment("Ops operation task aggregate roots requested through Gateway and executed by connector hosts."));
builder.ToTable("operation_attempts", table => table.HasComment("Ops operation execution attempts created when connector hosts claim operation tasks."));
builder.ToTable("audit_records", table => table.HasComment("Ops audit records for operation task lifecycle events and user-visible traceability."));
```

- [ ] **步骤 3：添加 Ops JSON 兼容性注释**

更改 `ParametersJson` 和 `FailureJson` 注释：

```csharp
builder.Property(x => x.ParametersJson)
    .IsRequired()
    .HasComment("JSON operation parameter dictionary produced by Gateway and Ops task creation, consumed by Connector Host execution; additive optional keys are compatible, required key or semantic changes require Ops contract versioning.");

builder.Property(x => x.FailureJson)
    .HasComment("JSON failure details produced by Connector Host execution, consumed by Ops and Gateway diagnostics; additive optional keys are compatible, removing or changing key semantics requires Ops contract versioning.");
```

- [ ] **步骤 4：运行 Ops schema 约定测试**

运行：

```powershell
dotnet test backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj --filter FullyQualifiedName~OpsSchemaConventionTests
```

预期结果：通过。

- [ ] **步骤 5：生成 Ops schema 治理迁移**

运行：

```powershell
$env:Persistence__Provider = "PostgreSQL"
$env:ConnectionStrings__OpsDb = "Host=localhost;Port=15432;Database=nerv_iip_ops_schema_governance_design;Username=nerv;Password=nerv"
dotnet tool restore
dotnet tool run dotnet-ef migrations add SchemaGovernanceMetadata --project backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/Nerv.IIP.Ops.Infrastructure.csproj --startup-project backend/services/Ops/src/Nerv.IIP.Ops.Web/Nerv.IIP.Ops.Web.csproj --context Nerv.IIP.Ops.Infrastructure.ApplicationDbContext --output-dir Migrations
Remove-Item Env:\Persistence__Provider -ErrorAction SilentlyContinue
Remove-Item Env:\ConnectionStrings__OpsDb -ErrorAction SilentlyContinue
```

预期结果：Ops 创建新迁移并更新 `ApplicationDbContextModelSnapshot.cs`。迁移应包含表/注释元数据变更，且不新增业务表。

## 任务 6：更新架构和交接文档

**文件：**

- 修改：`README.md`
- 修改：`docs/architecture/database-schema-catalog.md`
- 修改：`docs/architecture/database-schema-conventions.md`
- 修改：`docs/architecture/implementation-readiness.md`
- 修改：`docs/architecture/technology-stack-references.md`
- 修改：`docs/superpowers/plans/2026-05-17-release-grade-persistence-foundation.md`

- [ ] **步骤 1：更新 README 阶段交接说明**

将仓库中当前写作第四阶段工作树的条目更改为：

```markdown
- 当前工作树：`codex/schema-governance-hardening`，从第五阶段迁移发布底座之后继续推进 schema governance hardening。
```

将第六阶段计划添加到实施计划清单：

```markdown
6. docs/superpowers/plans/2026-05-17-schema-governance-migration-hardening.md
```

添加当前状态说明：

```markdown
第六阶段 Schema Governance & Migration Hardening 规划已启动，目标是在 IAM、FileStorage 等新持久化服务开工前，把 AppHub/Ops 的表注释、JSON 兼容注释、migrations history schema 和 schema convention tests 固化为门禁。
```

- [ ] **步骤 2：更新技术栈当前基线**

将仓库表格中的当前基线行更改为：

```markdown
| Current baseline | 第五阶段 Release-grade Persistence Foundation 已合入；本计划是历史执行记录，不是当前状态源。当前状态见 [implementation-readiness.md](../../architecture/implementation-readiness.md)，原始设计输入见 [schema-governance design](../specs/2026-05-17-schema-governance-migration-hardening-design.md)。 |
```

- [ ] **步骤 3：更新 schema 目录中的已知缺口**

对于 AppHub 和 Ops，移除本计划关闭的缺口：

```markdown
Known gaps:

1. CAP system tables 当前只在 catalog 中标记 system-owned，后续可补 table comment 便于数据库工具展示。
```

保持未来服务的行不变。

- [ ] **步骤 4：更新 schema 约定强制执行状态**

在 `Schema Convention Tests` 中说明 AppHub/Ops 现已强制执行前六项检查，未来的持久化服务必须采用同一辅助库：

```markdown
AppHub/Ops 已通过 `Nerv.IIP.Testing` 中的 schema convention helper 覆盖 business table comment、business column comment、JSON/text 兼容注释、string ID 约束和 service-schema `__EFMigrationsHistory`。后续 IAM、FileStorage、Notification、Knowledge、AI Integration 和 Observability 索引建表时必须复用同一类测试。
```

更新当前已知缺口，确保实施后 AppHub/Ops 已关闭项不会继续列为开放项：

```markdown
1. CAP system tables 当前只在 DbContext 中配置表名和主键，后续应至少补表注释或在 catalog 中保持 system-owned 标记。
2. IAM、FileStorage、Notification、Knowledge、AI Integration 和 Observability 索引尚未建表；首次建表前必须先补 catalog 草案和 schema convention tests。
```

- [ ] **步骤 5：更新实施就绪状态**

添加第六阶段当前结论：

```markdown
18. 第六阶段 Schema Governance & Migration Hardening 用 AppHub/Ops 作为已迁移服务样本，把业务表注释、业务列注释、JSON/text 兼容注释、string ID 约束和 service-schema migrations history 配置固化为测试门禁；IAM/FileStorage 等新增持久化服务开工前必须沿用该门禁。
```

将新计划添加到计划清单，并说明客户发布包仍属于未来工作。

- [ ] **步骤 6：将第五阶段计划标记为历史完成状态**

在 `docs/superpowers/plans/2026-05-17-release-grade-persistence-foundation.md` 顶部的完成记录引言之后添加：

```markdown
> Historical note: the unchecked task list below is preserved as the original execution plan. The stage is complete; use the Completion Record and git history as the source of truth for status.
```

不得重写整个历史任务清单。

## 任务 7：验证

**文件：**

- 除非前一任务发现缺失的测试或文档，否则不新增文件。

- [ ] **步骤 1：运行针对性的 schema 测试**

运行：

```powershell
dotnet test backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj --filter FullyQualifiedName~AppHubSchemaConventionTests
dotnet test backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj --filter FullyQualifiedName~OpsSchemaConventionTests
```

预期结果：两者都以 `0` 退出。

- [ ] **步骤 2：运行完整后端解决方案测试**

运行：

```powershell
dotnet test backend/Nerv.IIP.sln
```

预期结果：以 `0` 退出。

- [ ] **步骤 3：运行第五阶段持久化验证**

由于迁移和 PostgreSQL 历史配置发生变化，运行：

```powershell
pwsh scripts/verify-fifth-slice-persistence-foundation.ps1
```

预期最后一行：

```text
Fifth slice release-grade persistence foundation verified.
```

- [ ] **步骤 4：运行仓库空白检查**

运行：

```powershell
git diff --check
```

预期结果：以 `0` 退出。

- [ ] **步骤 5：确认最终 git 状态只包含预期文件和预先存在的 skills lock**

运行：

```powershell
git status --short
```

预期结果：预期的 schema 治理文件已修改/添加。`skills-lock.json` 仍可能显示为预先存在的未暂存修改；不得暂存它。

## 执行顺序

1. 首先执行任务 1，建立 AppHub 红灯测试。
2. 接着执行任务 2，因为两个服务测试都依赖共享断言。
3. 然后执行任务 3，建立 Ops 红灯测试。
4. 如果分配给不同执行者，任务 4 和任务 5 可在任务 2 后独立运行，因为 AppHub 和 Ops 的写入集合互不相交。
5. schema 测试通过后运行任务 6，使文档反映真实关闭的缺口。
6. 最后运行任务 7。

## 自检

规范覆盖：

1. 任务 4 和任务 5 覆盖 AppHub/Ops 表注释。
2. 任务 4 和任务 5 覆盖 JSON/text 兼容性注释。
3. 任务 4 和任务 5 覆盖迁移历史 schema 配置。
4. 任务 1、2 和 3 覆盖可复用的约定测试。
5. 任务 6 覆盖文档和交接偏差。
6. 任务 7 覆盖完整验证。

风险标记扫描：

1. 不保留风险标记或空章节。
2. 每个代码变更任务都指定精确文件和具体片段。
3. 每个验证步骤都有具体命令和预期结果。

类型一致性：

1. 辅助类型始终命名为 `SchemaConventionAssertions`、`JsonColumnRule` 和 `StringKeyRule`。
2. AppHub schema 始终命名为 `apphub`。
3. Ops schema 始终命名为 `ops`。
4. 两个服务的新迁移都命名为 `SchemaGovernanceMetadata`。
