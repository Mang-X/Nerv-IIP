# 持久化启动治理与 PostgreSQL 测试基建

本文档记录 issue #1075 的跨服务持久化启动治理入口、仓库现状盘点和测试数据库生命周期约束。它不改变服务 schema、迁移所有权或业务 API；服务仍拥有自己的 DbContext、migration runner、schema 和发布脚本。

## 仓库启动面盘点

2026-07-22 盘点到 18 个具有持久化启动判断的 Web 服务，分为三组：

| 分组 | 服务 | Provider 现状 | 连接串 | AutoMigrate 现状 |
| --- | --- | --- | --- | --- |
| 本次统一入口 | FileStorage、AppHub、Ops、Notification | Development 必须显式选择 `InMemory` 或 `PostgreSQL`；非 Development 只允许 `PostgreSQL` | 服务专名，部分兼容 `PostgreSQL` 别名，见下表 | 只允许 Development；InMemory 必须为 false |
| 仍待后续迁移 | IAM | Infrastructure 仍解析 `Persistence:Provider`，缺省为 `InMemory` | `IamDb` | Web 层已有 Development-only 判断 |
| PostgreSQL-only 业务服务 | Approval、BarcodeLabel、DemandPlanning、ERP、IndustrialTelemetry、Inventory、Maintenance、MasterData、MES、ProductEngineering、Quality、Scheduling、WMS | 不提供 InMemory 运行 profile，直接要求 PostgreSQL | `PostgreSQL` | 各服务 Web 层仍保留等价 Development-only 判断 |

本次只迁移 issue 指定的四个代表服务。IAM 和 13 个 PostgreSQL-only 业务服务的等价判断属于后续收敛范围；本文档不把它们声明为已迁移。

## 统一运行时入口

`backend/common/Persistence/Nerv.IIP.Persistence` 只依赖 ASP.NET Core 配置与环境抽象，不依赖 EF Core、Npgsql 或服务领域类型。Web 启动调用：

```csharp
var persistence = PersistenceStartupGovernance.Resolve(
    builder.Configuration,
    builder.Environment,
    new PersistenceStartupRequirements("AppHub", ["AppHubDb", "PostgreSQL"]));
```

返回值包含 `UsePostgreSql`、`AutoMigrate` 和治理入口按声明顺序选出的首个非空 PostgreSQL 连接串别名。服务把所选别名传入 Infrastructure DI extension，把 `AutoMigrate` 用于调用自己的 migration runner；Infrastructure 不再重复别名列表、解析 provider 或决定环境策略。

统一规则：

1. `Persistence:Provider` 先 trim，再以不区分大小写方式匹配 `InMemory` 或 `PostgreSQL`；缺失值和未知值均拒绝。
2. Development 可显式使用 InMemory，但 `Persistence:AutoMigrate` 必须为 false。
3. PostgreSQL 必须配置至少一个服务登记的非空连接串别名；前置别名为空白时继续检查后续别名。
4. 非 Development 只允许 PostgreSQL，且 Web-host `AutoMigrate` 必须为 false。
5. 错误只报告服务名、环境、规范化 provider 状态、是否存在连接配置、AutoMigrate 状态和修复建议；不输出连接串、用户名或密码。

代表服务矩阵：

| 服务 | Development 默认配置 | PostgreSQL 连接串查找顺序 | 非 Development 迁移建议 |
| --- | --- | --- | --- |
| FileStorage | `Persistence:Provider=InMemory` | `FileStorageDb` → `PostgreSQL` | `scripts/install/migrate-file-storage.ps1` 或 migration bundle |
| AppHub | `Persistence:Provider=InMemory` | `AppHubDb` → `PostgreSQL` | 显式 migrator、release script 或 migration bundle |
| Ops | `Persistence:Provider=InMemory` | `OpsDb` | 显式 migrator、release script 或 migration bundle |
| Notification | `Persistence:Provider=InMemory` | `NotificationDb` → `PostgreSQL` | 显式 migrator、release script 或 migration bundle |

Development 的 InMemory 值写入各服务 `appsettings.Development.json`，因此是显式 profile，而不是代码内静默回退。AppHost 仍可通过环境配置显式覆盖为 PostgreSQL。

## 独立 PostgreSQL 测试包

Npgsql 生命周期能力放在 `backend/common/Testing/Nerv.IIP.Testing.PostgreSql`，不放入通用 `Nerv.IIP.Testing`。这样只做 GUID、异常和关系模型断言的测试项目不会传递依赖 Npgsql。

基本用法：

```csharp
await using var database = await PostgreSqlTestDatabase.CreateAsync(
    baseConnectionString,
    "nerv_scheduling_test",
    async (connectionString, cancellationToken) =>
    {
        await using var context = CreateDbContext(connectionString);
        await context.Database.MigrateAsync(cancellationToken);
    },
    cancellationToken);
```

生命周期保证：

1. 每次创建使用规范化前缀和 `Guid.CreateVersion7()` 生成不超过 PostgreSQL 63 字符限制的唯一 database 名，支持 xUnit 并行隔离。
2. 通过 `postgres` admin database 创建测试 database，再把唯一连接串交给可选初始化/迁移回调。
3. 初始化失败或取消后尝试强制清理；CREATE 在 admin 连接打开后失败时也尝试清理，连接尚未打开时不再发起第二次连接。正常 `DisposeAsync` 或显式 `DropAsync` 使用 `DROP DATABASE ... WITH (FORCE)`。
4. `CreateAsync`、初始化回调和 `DropAsync` 接受 `CancellationToken`；已经取消的创建或 Drop 不会连接数据库。物理 DROP 一旦启动便由内部生命周期继续，`DropAsync` token 只取消该调用方的等待，`DisposeAsync` 仍会等待同一个 cleanup task 完成。
5. `DisposeAsync` 通过单一可等待生命周期任务执行 best-effort 清理，重复或并发 Dispose 保持幂等；并发 `DropAsync` 共享同一个严格清理结果，需要清理失败证据的调用方仍显式使用 `DropAsync`。
6. 失败诊断保留 operation、host、port、database 和 `usernameConfigured` 状态，但移除原始连接串、用户名和密码，不附带可能泄密的 inner exception；凭据子串不会误改非敏感 database 名。
7. FileStorage 重启持久化测试和 Scheduling PostgreSQL profile tests 已改用该包，证明两个服务不再复制 database create/drop 代码。

从仓库根目录对 `CREATE DATABASE`、`DROP DATABASE` 和 `pg_terminate_backend` 做未过滤搜索，并排除本包实现及其自身测试后，仍有 20 个手写 PostgreSQL 测试数据库 helper/测试文件：AppHub 1 个、Notification 2 个、业务服务测试树 14 个、跨服务业务验收测试 3 个。它们迁移到 `Nerv.IIP.Testing.PostgreSql` 属于后续范围，本 PR 不扩展迁移面。

真实 PostgreSQL 生命周期测试由 `NERV_IIP_TEST_POSTGRES` opt-in，覆盖两库并行隔离、初始化回调、失败清理和凭据脱敏。未设置变量时，普通 solution 测试保留确定性契约覆盖并明确跳过真实 provider 用例。
