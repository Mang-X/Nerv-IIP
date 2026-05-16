# 后端 CleanDDD 与 netcorepal 落地规范

本文档定义 Nerv-IIP 后端平台服务在 CleanDDD 与 netcorepal-cloud-framework 上的落地约定。它承接 ADR 0001 的 solution 边界、ADR 0003 的基础设施基线，以及首批纵切中的 AppHub、PlatformGateway、Ops、Connector Protocol 实现要求。

参考来源：

- netcorepal-cloud-framework 官方文档：https://netcorepal.github.io/netcorepal-cloud-framework/en/
- Project Structure：https://netcorepal.github.io/netcorepal-cloud-framework/en/getting-started/project-structure/
- Development Process：https://netcorepal.github.io/netcorepal-cloud-framework/en/getting-started/development-process/
- Transactions：https://netcorepal.github.io/netcorepal-cloud-framework/en/transactions/transactions/
- OpenTelemetry Diagnostics：https://netcorepal.github.io/netcorepal-cloud-framework/en/observability/opentelemetry-diagnostics/
- NetCorePal.Template 当前公开参数说明：https://www.nuget.org/packages/NetCorePal.Template
- 本地 CleanDDD 技能：cleanddd-modeling、cleanddd-dotnet-coding、cleanddd-dotnet-init。

## 适用范围

1. 本文档适用于 backend 下的平台 HTTP 服务：Iam、FileStorage、AppHub、Ops、Notification、AI Integration、Knowledge。
2. PlatformGateway 只采用其中的 Web、Endpoint、响应、观测与契约消费约定；默认不强制创建 Domain 与 Infrastructure。
3. Connector Host 不适用平台 HTTP 服务的三项目约定，仍按 connector-hosts 独立宿主模型实现。
4. 当前仓库已经落地第一迭代纵切骨架；本文档继续约束后续平台服务、Endpoint、领域模型和基础设施实现。

## 模板使用基线

netcorepal-web 模板的默认参数会随模板版本变化。Nerv-IIP 不依赖默认值，所有平台领域服务创建时必须显式传入目标框架、数据库和消息队列。

首批领域服务建议命令：

```powershell
dotnet new install NetCorePal.Template

dotnet new netcorepal-web `
  -n Nerv.IIP.AppHub `
  -o backend/services/AppHub `
  --Framework net10.0 `
  --Database PostgreSQL `
  --MessageQueue RabbitMQ `
  --UseAspire false `
  --IncludeCopilotInstructions false `
  --UseAdmin false
```

Iam 与 Ops 使用同样参数，仅替换 `-n` 与 `-o`：

```powershell
dotnet new netcorepal-web -n Nerv.IIP.Iam -o backend/services/Iam --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new netcorepal-web -n Nerv.IIP.Ops -o backend/services/Ops --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
```

约束：

1. 创建前先运行 `dotnet new netcorepal-web --help`，确认本机模板支持的参数名。
2. 当前项目目标框架固定显式传 `net10.0`，除非 ADR 更新。
3. `--Database PostgreSQL` 与 `--MessageQueue RabbitMQ` 必须显式传入，避免落到模板默认 MySQL 或其他消息队列。
4. `--UseAdmin false` 必须显式传入，避免把模板内置 Admin、RBAC 或前端后台与 Nerv-IIP 自有 IAM、console 规划混在一起。
5. `--IncludeCopilotInstructions false` 保持协作指引由仓库根统一维护，不让每个服务生成一份局部指令。
6. `--UseAspire false` 是每个平台领域服务的默认值，含义是不让每个服务各自生成局部 AppHost；平台统一 Aspire AppHost 由 ADR 0008 冻结，后续落点在 `infra/aspire`。

## .NET 版本策略

1. 当前后端目标框架采用 .NET 10，对应模板参数固定为 `--Framework net10.0`。
2. 不再以 .NET 9 作为首批 scaffold 基线。
3. .NET 11 作为下一次目标升级方向；只有在 netcorepal-cloud-framework、NetCorePal.Template、EF Core provider、CAP 存储和测试基础设施明确适配后，才统一升级。
4. 升级到 .NET 11 时必须集中修改 Directory.Build.props、模板创建命令、CI/本地 SDK 前置、Docker/测试镜像和本文档，不允许服务间分批漂移目标框架。

## 服务内结构

平台领域服务采用 netcorepal 推荐的三项目结构：

```text
backend/services/AppHub/
  src/
    Nerv.IIP.AppHub.Web/
      Application/
        Commands/
        Queries/
        DomainEventHandlers/
        IntegrationEvents/
        IntegrationEventConverters/
        IntegrationEventHandlers/
      Endpoints/
      Program.cs
    Nerv.IIP.AppHub.Domain/
      AggregatesModel/
      DomainEvents/
    Nerv.IIP.AppHub.Infrastructure/
      EntityConfigurations/
      Repositories/
      ApplicationDbContext.cs
  tests/
    Nerv.IIP.AppHub.Web.Tests/
    Nerv.IIP.AppHub.Domain.Tests/
    Nerv.IIP.AppHub.Infrastructure.Tests/
```

放置规则：

1. Domain 只放聚合、实体、值对象、强类型 ID 与领域事件。
2. Infrastructure 放 EF Core DbContext、实体配置、仓储接口与实现。
3. Web 放 Endpoint、命令、查询、验证器、领域事件处理器、集成事件、集成事件转换器和集成事件处理器。
4. Application 作为 Web 项目内部目录，不默认拆成独立项目。
5. PlatformGateway 默认只保留 `Nerv.IIP.PlatformGateway.Web`；只有当它需要自身持久化模型时，才允许补充 Infrastructure。

## 聚合与领域模型

聚合根约定：

1. 聚合根继承 `Entity<TId>` 并实现 `IAggregateRoot`。
2. 强类型 ID 使用 `public partial record {Entity}Id`，优先采用 `IGuidStronglyTypedId`；需要有序长整型时采用 `IInt64StronglyTypedId`。
3. 聚合根提供 `protected` 无参构造，供 EF Core 使用。
4. 状态属性默认 `private set`，通过聚合行为修改，不从命令处理器直接改属性。
5. 首批持久聚合默认包含 `Deleted` 与 `RowVersion`，用于软删除和乐观并发。
6. 状态变化通过 `this.AddDomainEvent(...)` 发布领域事件。
7. 聚合之间不直接引用其他聚合根实例；跨聚合影响通过领域事件、集成事件或明确的查询接口实现。

子实体和值对象：

1. 子实体不跨聚合共享。
2. 子实体如需要独立主键，也使用强类型 ID。
3. 值对象可在服务内部复用，但不得把跨服务共享值对象变成隐形 SharedKernel。

领域事件：

1. 领域事件使用过去式命名，例如 `ApplicationRegisteredDomainEvent`。
2. 领域事件定义为 `record` 并实现 `IDomainEvent`。
3. 同一聚合的领域事件可以合并在 `{Aggregate}DomainEvents.cs`。
4. 领域事件只表达已经发生的领域事实，不放外部 IO 逻辑。

## 命令、查询与 Endpoint

命令：

1. 命令使用 `{Action}{Entity}Command` 命名，返回值使用 `ICommand<TResponse>`，无返回值使用 `ICommand`。
2. 命令、验证器、处理器放在同一文件。
3. 命令处理器通过仓储读取和持久化聚合。
4. 命令处理器不显式调用 `SaveChanges`、`SaveEntitiesAsync` 或 `UpdateAsync`，默认交给 netcorepal 的 UnitOfWork 管线处理。
5. 业务异常使用 `KnownException`，不要返回半成功对象来表达业务错误。

查询：

1. 查询使用 `{Action}{Entity}Query` 命名，返回 `IQuery<T>` 或 `IPagedQuery<T>`。
2. 查询处理器可直接使用 `ApplicationDbContext` 做只读投影。
3. 查询无副作用，不通过仓储修改聚合。
4. 列表查询必须有明确分页、排序和过滤口径。

Endpoint：

1. HTTP Endpoint 使用 FastEndpoints。
2. Endpoint 只做请求绑定、鉴权声明、mediator 调度和响应包装，不写领域规则。
3. 本项目采用 CleanDDD 技能里的属性路由风格，优先使用 `[HttpGet]`、`[HttpPost]`、`[Tags]`、`[AllowAnonymous]` 等特性，不在新代码中默认使用 `Configure()`。
4. 响应统一使用 `ResponseData<T>` 和 `.AsResponseData()`。
5. 请求/响应类型可以直接使用强类型 ID，不解包 `.Value`。

## 事务、领域事件与集成事件

事务边界：

1. CommandHandler 与其触发的 DomainEventHandler 运行在同一数据库事务中。
2. 如果 CommandHandler 或 DomainEventHandler 抛出异常，当前事务回滚。
3. 手动使用 `IUnitOfWork` 或 `ITransactionUnitOfWork` 只作为例外场景；常规命令处理依赖框架管线。

领域事件处理器：

1. 领域事件处理器实现 `IDomainEventHandler<TDomainEvent>`。
2. 处理器可以通过 mediator 发送命令，不直接跨聚合改库。
3. 处理器内避免不可回滚的外部副作用；需要跨服务传播时转为集成事件。

集成事件：

1. 跨服务传播使用 IntegrationEvent，不直接把 DomainEvent 当作外部契约。
2. 集成事件使用 `record`，不携带聚合对象引用，不暴露敏感字段。
3. 领域事件到集成事件通过 `IIntegrationEventConverter<TDomainEvent, TIntegrationEvent>` 转换。
4. netcorepal 会通过生成器为 converter 生成发布处理器；新代码不要默认手写 `IIntegrationEventPublisher` 发布逻辑。
5. IntegrationEvent 与命令数据修改通过 CAP outbox 保存在同一事务中；事务提交后再由 CAP 发布到 RabbitMQ。
6. IntegrationEventHandler 与触发它的原命令不在同一事务中，必须按最终一致性和可重试语义设计。
7. 集成事件处理器应具备幂等性，至少能处理 CAP 重试或重复投递。
8. 不是每个 DomainEvent 都需要转换成 IntegrationEvent；只有跨服务传播、跨进程审计或异步投影需要外发时才转换。

首批事件映射建议：

| 领域事件 | 集成事件 | 发布方 | 消费方 |
| --- | --- | --- | --- |
| ApplicationRegisteredDomainEvent | ApplicationRegisteredIntegrationEvent | AppHub | 后续异步订阅者或查询投影 |
| InstanceHeartbeatReceivedDomainEvent | InstanceHeartbeatReceivedIntegrationEvent | AppHub | 观测、告警或后续订阅者 |
| InstanceStatusChangedDomainEvent | InstanceStatusChangedIntegrationEvent | AppHub | Ops、Notification、观测告警或后续订阅者 |
| OperationCompletedDomainEvent | OperationCompletedIntegrationEvent | Ops | AppHub 不直接改状态，仅可用于审计或通知；Notification 可生成用户可见结果消息 |
| OperationFailedDomainEvent | OperationFailedIntegrationEvent | Ops | 审计、告警或 Notification |

## 仓储、DbContext 与 EF 配置

仓储：

1. 仓储接口命名为 `I{Aggregate}Repository`，实现命名为 `{Aggregate}Repository`。
2. 接口继承 `IRepository<TEntity, TKey>`。
3. 实现继承 `RepositoryBase<TEntity, TKey, ApplicationDbContext>`。
4. 仓储接口与实现同文件，放在 Infrastructure 的 `Repositories` 目录。
5. 仓储只服务命令侧聚合持久化，不作为复杂查询层。

DbContext：

1. 每个领域服务拥有自己的 `ApplicationDbContext`。
2. `ApplicationDbContext` 继承 netcorepal 的 `AppDbContextBase`。
3. DbSet 使用 `public DbSet<T> Name => Set<T>();` 风格。
4. 通过 `ApplyConfigurationsFromAssembly` 应用实体配置。

实体配置：

1. 每个持久实体提供 `{Entity}EntityTypeConfiguration`。
2. 必须配置主键、必填、长度、索引和字段注释。
3. `IGuidStronglyTypedId` 使用 Guid v7 值生成器。
4. `IInt64StronglyTypedId` 使用 Snowflake 值生成器。
5. `RowVersion` 采用框架约定，不自行实现第二套并发字段。

## Program 与基础注册

每个领域服务的 `Program.cs` 至少需要确认以下注册存在：

1. DbContext 使用 PostgreSQL provider。
2. Repositories 通过 `AddRepositories(...)` 注册。
3. UnitOfWork 通过 `AddUnitOfWork<ApplicationDbContext>()` 注册。
4. MediatR 注册命令、查询、验证、命令锁和 UnitOfWork 行为。
5. CAP 配置 RabbitMQ，并使用 netcorepal storage 绑定当前 `ApplicationDbContext`。
6. FastEndpoints、KnownException 处理中间件、ResponseData、OpenAPI 生成正常启用。
7. OpenTelemetry 接入 ASP.NET Core、HTTP、CAP 和 netcorepal instrumentation。

这些注册原则先由模板生成，后续只做与 Nerv-IIP 基线一致的裁剪，避免手写一套与框架管线平行的基础设施。

## 测试与验收

领域模型测试：

1. 聚合构造与行为必须覆盖正常路径、异常路径、不变式和领域事件。
2. 领域事件测试使用 `GetDomainEvents()` 校验事件类型与数量。
3. 乐观并发、软删除、状态转换需要独立测试。

Web 集成测试：

1. 使用模板生成的 `MyWebApplicationFactory` 或等价测试工厂。
2. 使用 Testcontainers 或本地开发编排启动 PostgreSQL、RabbitMQ、Redis 等依赖。
3. Endpoint 测试覆盖请求、响应、KnownException、权限上下文和幂等行为。

事件测试：

1. DomainEventHandler 异常应导致命令事务回滚。
2. IntegrationEventConverter 应只输出外部契约需要的字段。
3. IntegrationEventHandler 按重复投递测试幂等性。

首批提交前至少运行：

```powershell
dotnet restore backend/Nerv.IIP.sln
dotnet build backend/Nerv.IIP.sln
dotnet test backend/Nerv.IIP.sln
```

## 反模式

1. 让 CommandHandler 显式 `SaveChanges` 或手动控制普通事务。
2. 在 Endpoint 中直接访问 DbContext 或写领域规则。
3. 在 QueryHandler 中修改聚合或发布事件。
4. 让 Domain 引用 Infrastructure、Web 或其他服务项目。
5. 把 DomainEvent 直接作为跨服务消息。
6. 手写 `IIntegrationEventPublisher` 替代 converter + 生成器默认链路。
7. 让集成事件携带聚合实例、数据库实体或敏感字段。
8. 在 common 中创建 SharedKernel、Utils、Helpers 一类无边界聚合库。
9. PlatformGateway 直接引用服务 Domain 或 Infrastructure。
10. Connector Host 引用平台服务实现项目。
