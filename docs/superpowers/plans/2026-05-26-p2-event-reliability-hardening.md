# P2 事件可靠性强化实施计划

> **面向代理执行者：**必须使用子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项实施本计划。步骤使用复选框（`- [ ]`）语法进行跟踪。

**目标：**通过持久化 Notification、AppHub 和 MES 的 DLQ 事实，并增加按需启用的跨服务 CAP 强化门禁，使 #170/#171 的事件可靠性达到足以用于 P2 生产环境的水平。

**架构：**复用现有 Maintenance 持久化 DLQ 形态作为表契约，但基础 `Nerv.IIP.Messaging.CAP` 包仅保留契约、守卫逻辑和内存 DLQ。可复用的 EF 存储位于 `Nerv.IIP.Messaging.CAP.EntityFrameworkCore`，使 PostgreSQL 支持的服务无需复制粘贴即可按需启用，同时避免非持久化消费者引入 EF Core 传递依赖。每个服务在自己的 schema 内拥有独立的 `integration_event_dead_letters` 表。CAP `received` 仍作为消息代理级 inbox；服务自有的已处理事件表仍作为业务 inbox，并将逐步扩展。

**实施状态（2026-05-26）：**首个 PR 完成共享 CAP 存储、Notification、AppHub 和 MES 的持久化 DLQ 切片。按需启用的跨服务多进程 CAP 门禁仍留待下一个事件可靠性 PR，以便独立审核其 Docker/PostgreSQL/RabbitMQ 的搭建与清理过程。

**技术栈：**.NET 10、EF Core 10、PostgreSQL、CAP、RabbitMQ profile、xUnit、受治理的 PowerShell 脚本。

---

### Task 1：可复用的持久化 DLQ 存储

**文件：**
- 修改：`backend/common/Messaging/Nerv.IIP.Messaging.CAP/Nerv.IIP.Messaging.CAP.csproj`
- 修改：`backend/common/Messaging/Nerv.IIP.Messaging.CAP/IntegrationEventReliability.cs`
- 创建：`backend/common/Messaging/Nerv.IIP.Messaging.CAP.EntityFrameworkCore/**`
- 测试：`backend/tests/Nerv.IIP.Messaging.CAP.Tests/IntegrationEventReliabilityTests.cs`

- [ ] **步骤 1：增加一个会失败的 EF 支持 DLQ 存储测试**

增加一个测试：创建关系型 EF 测试 `DbContext`（CI 使用内存 SQLite 即可），配置共享死信实体，验证表名、`event_json` 列类型和索引等关系映射元数据；通过持久化存储写入被拒消息、列出该消息、将其标记为已重放，并验证状态变化。

运行：

```powershell
dotnet test backend/tests/Nerv.IIP.Messaging.CAP.Tests/Nerv.IIP.Messaging.CAP.Tests.csproj --no-restore --filter FullyQualifiedName~Persistent_dead_letter_store
```

实施前预期：编译失败，因为 `PersistentIntegrationEventDeadLetterStore<TDbContext>` 尚不存在。

- [ ] **步骤 2：增加 EF 扩展包**

创建 `Nerv.IIP.Messaging.CAP.EntityFrameworkCore`，并在其中增加 `Microsoft.EntityFrameworkCore` 和 `Microsoft.EntityFrameworkCore.Relational`。不得向基础 `Nerv.IIP.Messaging.CAP.csproj` 增加 EF Core 包引用。

- [ ] **步骤 3：实现共享持久化 DLQ 实体与存储**

在 EF 扩展包中增加：

```csharp
public sealed class IntegrationEventDeadLetter
{
    private IntegrationEventDeadLetter() { }

    public IntegrationEventDeadLetter(IntegrationEventDeadLetterMessage message) { ... }

    public Guid Id { get; private set; }
    public string ConsumerName { get; private set; } = string.Empty;
    public string? EventId { get; private set; }
    public string? EventType { get; private set; }
    public int? EventVersion { get; private set; }
    public string? SourceService { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public string EventClrType { get; private set; } = string.Empty;
    public string EventJson { get; private set; } = string.Empty;
    public string FailureCode { get; private set; } = string.Empty;
    public string FailureMessage { get; private set; } = string.Empty;
    public IntegrationEventDeadLetterStatus Status { get; private set; }
    public DateTimeOffset DeadLetteredAtUtc { get; private set; }
    public DateTimeOffset? ReplayedAtUtc { get; private set; }
}
```

增加带有 `AddAsync`、`ListAsync` 和 `MarkReplayedAsync` 的 `PersistentIntegrationEventDeadLetterStore<TDbContext>`，并使用 `dbContext.Set<IntegrationEventDeadLetter>()`。基础 CAP 包中仅保留 `IIntegrationEventDeadLetterStore`、`IntegrationEventDeadLetterMessage`、`IntegrationEventDeadLetterStatus`、`IntegrationEventConsumerGuard`、信封校验器和内存存储。

增加 `ModelBuilder.ConfigureIntegrationEventDeadLetters()` 扩展，用于映射 `integration_event_dead_letters` 表、全部注释、JSON 列类型、状态字符串转换和索引：

```csharp
builder.HasIndex(x => new { x.ConsumerName, x.Status, x.DeadLetteredAtUtc });
builder.HasIndex(x => new { x.ConsumerName, x.EventId });
```

- [ ] **步骤 4：运行聚焦测试**

运行：

```powershell
dotnet test backend/tests/Nerv.IIP.Messaging.CAP.Tests/Nerv.IIP.Messaging.CAP.Tests.csproj --no-restore
```

预期：所有 messaging CAP 测试通过。

### Task 2：Notification 持久化 DLQ

**文件：**
- 修改：`backend/services/Notification/src/Nerv.IIP.Notification.Infrastructure/ApplicationDbContext.cs`
- 修改：`backend/services/Notification/src/Nerv.IIP.Notification.Web/Program.cs`
- 创建：`backend/services/Notification/src/Nerv.IIP.Notification.Infrastructure/Migrations/*_AddNotificationIntegrationEventDeadLetters.cs`
- 测试：`backend/services/Notification/tests/Nerv.IIP.Notification.Web.Tests/OperationTaskFailedNotificationConsumerTests.cs`
- 文档：`docs/architecture/database-schema-catalog.md`

- [ ] **步骤 1：增加会失败的 Notification 持久化测试**

增加一个测试，使用 PostgreSQL profile 测试服务启动 Notification，并断言 `IIntegrationEventDeadLetterStore` 解析为 `PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>`。

实施前预期：解析出的存储为 `InMemoryIntegrationEventDeadLetterStore`。

- [ ] **步骤 2：仅为 PostgreSQL profile 注册持久化存储**

在 `Program.cs` 中为非 PostgreSQL 情况保留内存注册；在 PostgreSQL 分支中注册：

```csharp
builder.Services.AddScoped<IIntegrationEventDeadLetterStore, PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>>();
```

- [ ] **步骤 3：映射表并生成 migration**

在 `ApplicationDbContext.OnModelCreating` 中调用：

```csharp
modelBuilder.ConfigureIntegrationEventDeadLetters();
```

生成 migration：

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add AddNotificationIntegrationEventDeadLetters `
  --project backend/services/Notification/src/Nerv.IIP.Notification.Infrastructure `
  --startup-project backend/services/Notification/src/Nerv.IIP.Notification.Web
```

- [ ] **步骤 4：运行聚焦的 Notification 测试**

运行：

```powershell
dotnet test backend/services/Notification/tests/Nerv.IIP.Notification.Web.Tests/Nerv.IIP.Notification.Web.Tests.csproj --no-restore
```

预期：Notification 测试通过。

### Task 3：AppHub 持久化 DLQ

**文件：**
- 修改：`backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/ApplicationDbContext.cs`
- 修改：`backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Program.cs`
- 创建：`backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/Migrations/*_AddAppHubIntegrationEventDeadLetters.cs`
- 测试：`backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/AppHubIntegrationEventTests.cs`
- 文档：`docs/architecture/database-schema-catalog.md`

- [ ] **步骤 1：增加会失败的 AppHub 持久化测试**

增加一个测试，使用 PostgreSQL profile 测试服务启动 AppHub，并断言 `IIntegrationEventDeadLetterStore` 解析为 `PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>`。
由于该测试仅验证 DI 注册，测试工厂必须在服务注册后将 EF Core 数据库 provider 替换为内存 provider。测试不得依赖可访问的 PostgreSQL 实例。

- [ ] **步骤 2：为 PostgreSQL profile 注册持久化存储**

当 `usePostgreSql` 为 true 时使用 scoped 持久化存储，否则保留 singleton 内存存储。

- [ ] **步骤 3：映射表并生成 migration**

从 AppHub `ApplicationDbContext` 调用 `modelBuilder.ConfigureIntegrationEventDeadLetters()`，然后生成 `AddAppHubIntegrationEventDeadLetters`。

- [ ] **步骤 4：运行聚焦的 AppHub 测试**

运行：

```powershell
dotnet test backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj --no-restore
```

预期：AppHub 测试通过。

### Task 4：MES 持久化 DLQ

**文件：**
- 修改：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/ApplicationDbContext.cs`
- 修改：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Program.cs`
- 创建：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/Migrations/*_AddMesIntegrationEventDeadLetters.cs`
- 测试：`backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/MaintenanceEventHandlerTests.cs`
- 文档：`docs/architecture/database-schema-catalog.md`

- [ ] **步骤 1：增加会失败的 MES 持久化测试**

增加一个测试，断言 MES PostgreSQL 服务注册将 `IIntegrationEventDeadLetterStore` 解析为 `PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>`。

- [ ] **步骤 2：替换 singleton 内存注册**

当前运行时中的 MES 由 PostgreSQL 支持，因此注册：

```csharp
builder.Services.AddScoped<IIntegrationEventDeadLetterStore, PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>>();
```

- [ ] **步骤 3：映射表并生成 migration**

从 MES `ApplicationDbContext` 调用 `modelBuilder.ConfigureIntegrationEventDeadLetters()`，然后生成 `AddMesIntegrationEventDeadLetters`。

- [ ] **步骤 4：运行聚焦的 MES 测试**

运行：

```powershell
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj --no-restore
```

预期：MES 测试通过。

### Task 5：跨服务 CAP 强化门禁

**文件：**
- 创建或修改：`backend/tests/Nerv.IIP.Infra.Cap.Tests/**`
- 创建：`scripts/verify-infra-cross-service-cap.ps1`
- 文档：`docs/architecture/implementation-readiness.md`
- 文档：`docs/architecture/script-automation-governance.md`

- [ ] **步骤 1：增加按需启用的测试类别**

创建带有 `Category=cap-cross-service` 标签、且需要 `NERV_IIP_TEST_POSTGRES` 的测试。仅 `Profile=rabbitmq` 需要 RabbitMQ。

- [ ] **步骤 2：覆盖 Ops 到 Notification/AppHub 的链路**

通过 Ops/Notification CAP 契约发布 `OperationTaskFailedIntegrationEvent`，并验证在版本受支持时，Notification 创建意图，AppHub 刷新 handler 记录或安全忽略事件，且不进入 DLQ。

- [ ] **步骤 3：增加受治理脚本**

创建 `scripts/verify-infra-cross-service-cap.ps1`，其中包含 Script-Governance 文件头、限定作用域的环境变量、`Invoke-DotNet`、明确的 PostgreSQL 要求、可选的 RabbitMQ 要求，以及输出至 `artifacts/script-logs/**` 的日志。

- [ ] **步骤 4：运行脚本治理与聚焦门禁**

运行：

```powershell
scripts/check-script-governance.ps1
dotnet test backend/tests/Nerv.IIP.Messaging.CAP.Tests/Nerv.IIP.Messaging.CAP.Tests.csproj --no-restore
```

如果 PostgreSQL 可用，还需运行：

```powershell
pwsh scripts/verify-infra-cross-service-cap.ps1 -PostgresConnectionString $env:NERV_IIP_TEST_POSTGRES -Profile inmemory
```

### Task 6：文档与就绪状态

**文件：**
- 修改：`docs/architecture/implementation-readiness.md`
- 修改：`docs/architecture/database-schema-catalog.md`
- 修改：`docs/architecture/project-status-dashboard.html`

- [ ] **步骤 1：更新就绪状态**

修改 #170/#171 所在行，说明 Notification/AppHub/MES 在 PostgreSQL profile 下具备持久化 DLQ，而跨服务 CAP 仍是按需启用的强化项。

- [ ] **步骤 2：更新 schema 目录**

为 notification、apphub 和 mes schema 增加 `integration_event_dead_letters` 条目，并说明索引和所有权。

- [ ] **步骤 3：最终验证**

运行：

```powershell
dotnet test backend/tests/Nerv.IIP.Messaging.CAP.Tests/Nerv.IIP.Messaging.CAP.Tests.csproj --no-restore
dotnet test backend/services/Notification/tests/Nerv.IIP.Notification.Web.Tests/Nerv.IIP.Notification.Web.Tests.csproj --no-restore
dotnet test backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj --no-restore
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj --no-restore
scripts/check-script-governance.ps1
git diff --check
```

预期：所有命令均通过；仅当所需基础设施不可用时，按需启用的 PostgreSQL/RabbitMQ 强化脚本报告为已跳过。
