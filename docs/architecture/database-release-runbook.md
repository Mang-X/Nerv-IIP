# 数据库发布检查表与 Runbook

本文档把 ADR 0009 和 schema conventions 落成发布执行口径。它不是当前仓库已经具备完整私有化安装包的声明；当前第五阶段验证了 AppHub/Ops 可以通过 migrations 从空 PostgreSQL 数据库建表，第六阶段进一步把 AppHub/Ops 的 schema governance metadata 和 service-schema migrations history 配置固化为门禁，第七阶段补齐 IAM `iam` schema、初始 migration、seed/auth profile 验证和持久化登录基线。真正 PoC 或私有化交付前，必须把本文档中的 release gate 补进安装脚本、migration bundle 或专用 migrator；承载这些动作的脚本还必须满足 docs/architecture/script-automation-governance.md。

## 当前支持状态

| Profile | Current status | Release-supported? | Evidence |
| --- | --- | --- | --- |
| PostgreSQL | AppHub/Ops/IAM 已有初始 migrations 和 schema governance metadata/profile 门禁，并通过第五/第六/第七阶段本地验证。 | Not yet for customer release. 需要安装脚本、备份恢复演练、seed 清单和诊断输出契约。 | `scripts/verify-fifth-slice-persistence-foundation.ps1`、`scripts/verify-iam-persistent-auth-foundation.ps1` |
| GaussDB | Candidate only. | No. | 需要 provider、CAP storage/outbox、migration、JSON、时间、事务和集成测试证据。 |
| DMDB | Candidate only. | No. | 需要 provider、CAP storage/outbox、migration、JSON、时间、事务和集成测试证据。 |
| Other databases | Evaluation only. | No. | 不在 NetCorePal.Template 当前公开 profile 基线内。 |

## AutoMigrate 环境矩阵

`Persistence:AutoMigrate=true` 只用于受控本地或开发验证。任何面向客户数据的环境都必须走显式发布入口。

| Environment | AutoMigrate | Required migration path |
| --- | --- | --- |
| Unit/integration tests | Allowed when test owns disposable database. | Test setup or verification script。 |
| Local development | Allowed for disposable local database. | `Persistence:AutoMigrate=true` 或手动 `dotnet-ef database update`。 |
| Shared dev / team environment | Discouraged; requires explicit owner approval. | Service-owned migrator or release script。 |
| PoC with customer data | Forbidden. | Release script with backup, migration log and health check。 |
| Private deployment / production | Forbidden. | Migration bundle or dedicated migrator step, executed before service rollout。 |

## 发布前 Preflight

1. 确认 release id、git commit、服务版本、目标 environment、数据库 profile 和连接串来源。
2. 确认目标数据库是预期库，不是开发默认库、共享验证库或误连客户生产库。
3. 确认 PostgreSQL、RabbitMQ、Redis、对象存储和观测依赖版本满足当前 release 要求。
4. 确认安装脚本不会直接拼 SQL 写业务表，不会调用 `EnsureCreated()`。
5. 确认待执行服务清单和顺序。当前 AppHub/Ops/IAM 可独立迁移；后续 FileStorage、Notification、Knowledge、AI Integration、Observability 必须在各自 catalog 和迁移准备完成后加入顺序。
6. 确认备份或快照已完成，并记录备份位置、时间、校验方式和恢复负责人。
7. 确认本次 release 的 seed 清单、幂等键、默认管理员/凭据处理方式和重复执行语义。
8. 确认失败停止条件：任一服务 migration 或 seed 失败时，不继续启动新版本业务服务。
9. 从第五阶段旧库升级到第六阶段及以后时，先执行“迁移历史表 schema 搬迁”前置步骤；否则 EF 会在新的 service schema history table 中看不到已应用的 `InitialCreate`，从而尝试重复建表。
10. 确认执行脚本分类为 `release-install` 或受控 release migrator，并通过脚本治理门禁；不得用 `verify` 脚本直接处理客户数据环境。

## 第六阶段迁移历史表 schema 搬迁

第五阶段 AppHub/Ops 的 `__EFMigrationsHistory` 使用 provider 默认 schema。第六阶段开始，AppHub/Ops 显式使用 service schema 中的 history table：`apphub.__EFMigrationsHistory` 与 `ops.__EFMigrationsHistory`。

从已有第五阶段数据库升级时，必须在执行 `dotnet-ef database update`、migration bundle 或专用 migrator 之前，把旧 history rows 复制到 service schema。这个步骤只搬迁 EF migration history，不修改业务表。

如果目标库是一次性本地验证库，可以直接删除并重建库；如果目标库保留任何需要延续的数据，必须执行下面的前置 SQL 并保留备份。

AppHub:

```sql
DO $$
BEGIN
    CREATE SCHEMA IF NOT EXISTS apphub;
    CREATE TABLE IF NOT EXISTS apphub."__EFMigrationsHistory" (
        "MigrationId" varchar(150) NOT NULL,
        "ProductVersion" varchar(32) NOT NULL,
        CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
    );

    IF to_regclass('public."__EFMigrationsHistory"') IS NOT NULL THEN
        INSERT INTO apphub."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
        SELECT "MigrationId", "ProductVersion"
        FROM public."__EFMigrationsHistory"
        ON CONFLICT ("MigrationId") DO NOTHING;
    END IF;
END $$;
```

Ops:

```sql
DO $$
BEGIN
    CREATE SCHEMA IF NOT EXISTS ops;
    CREATE TABLE IF NOT EXISTS ops."__EFMigrationsHistory" (
        "MigrationId" varchar(150) NOT NULL,
        "ProductVersion" varchar(32) NOT NULL,
        CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
    );

    IF to_regclass('public."__EFMigrationsHistory"') IS NOT NULL THEN
        INSERT INTO ops."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
        SELECT "MigrationId", "ProductVersion"
        FROM public."__EFMigrationsHistory"
        ON CONFLICT ("MigrationId") DO NOTHING;
    END IF;
END $$;
```

执行后确认：

```sql
SELECT * FROM apphub."__EFMigrationsHistory" ORDER BY "MigrationId";
SELECT * FROM ops."__EFMigrationsHistory" ORDER BY "MigrationId";
```

确认目标 service schema 已包含旧库已应用的 `InitialCreate` migration 后，才可以执行下面的 AppHub/Ops/IAM 手动迁移命令。不要在同一个 release 中删除 `public.__EFMigrationsHistory`；等备份、迁移和服务健康验证都完成后，再单独评估清理。

## 当前 AppHub/Ops/IAM 手动迁移命令

第五阶段已经为 AppHub/Ops 提供 migration runner 和 migrations，第七阶段已经为 IAM 提供 migration runner 和初始 persistent auth migration，但尚未提供最终发布用 bundle。当前只允许开发者或 CI 在受控环境中使用以下手动命令；客户交付前必须封装为安装脚本或 migration bundle。

AppHub:

```powershell
dotnet tool restore
$env:Persistence__Provider = "PostgreSQL"
$env:ConnectionStrings__AppHubDb = "<apphub-postgres-connection-string>"
dotnet tool run dotnet-ef database update `
  --project backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/Nerv.IIP.AppHub.Infrastructure.csproj `
  --startup-project backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Nerv.IIP.AppHub.Web.csproj `
  --context Nerv.IIP.AppHub.Infrastructure.ApplicationDbContext
Remove-Item Env:\Persistence__Provider -ErrorAction SilentlyContinue
Remove-Item Env:\ConnectionStrings__AppHubDb -ErrorAction SilentlyContinue
```

Ops:

```powershell
dotnet tool restore
$env:Persistence__Provider = "PostgreSQL"
$env:ConnectionStrings__OpsDb = "<ops-postgres-connection-string>"
dotnet tool run dotnet-ef database update `
  --project backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/Nerv.IIP.Ops.Infrastructure.csproj `
  --startup-project backend/services/Ops/src/Nerv.IIP.Ops.Web/Nerv.IIP.Ops.Web.csproj `
  --context Nerv.IIP.Ops.Infrastructure.ApplicationDbContext
Remove-Item Env:\Persistence__Provider -ErrorAction SilentlyContinue
Remove-Item Env:\ConnectionStrings__OpsDb -ErrorAction SilentlyContinue
```

IAM:

```powershell
dotnet tool restore
$env:Persistence__Provider = "PostgreSQL"
$env:ConnectionStrings__IamDb = "<iam-postgres-connection-string>"
dotnet tool run dotnet-ef database update `
  --project backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/Nerv.IIP.Iam.Infrastructure.csproj `
  --startup-project backend/services/Iam/src/Nerv.IIP.Iam.Web/Nerv.IIP.Iam.Web.csproj `
  --context Nerv.IIP.Iam.Infrastructure.ApplicationDbContext
Remove-Item Env:\Persistence__Provider -ErrorAction SilentlyContinue
Remove-Item Env:\ConnectionStrings__IamDb -ErrorAction SilentlyContinue
```

重跑语义：

1. `database update` 对已应用 migration 应为 no-op。
2. 已失败的 migration 不允许靠手工改 `__EFMigrationsHistory` 修复；必须恢复备份或提交补救 migration。
3. 同一目标数据库同一时间只允许一个 migrator 运行。

## 备份与恢复

备份不是一句“请自行备份”。发布脚本必须能记录备份证据，并让操作者知道如何恢复。

Docker/local PostgreSQL 示例：

```powershell
New-Item -ItemType Directory -Force -Path .\artifacts\db-backups | Out-Null
docker compose -f infra/docker-compose.dev.yml exec -T postgres pg_dump -U nerv -d nerv_iip_apphub > .\artifacts\db-backups\apphub-before-release.sql
docker compose -f infra/docker-compose.dev.yml exec -T postgres pg_dump -U nerv -d nerv_iip_ops > .\artifacts\db-backups\ops-before-release.sql
```

外部 PostgreSQL 或客户托管数据库：

1. 优先使用客户平台的快照能力。
2. 无快照能力时使用 `pg_dump` 或等价受控备份工具。
3. 备份文件不得提交到仓库，不得写入公开日志。
4. 恢复演练至少在非生产库执行一次，确认备份可用。

停止条件：

1. 备份失败或无法确认备份位置时，停止发布。
2. migration 失败且原因不明确时，停止发布并保留日志。
3. seed 部分失败时，除非 seed 定义允许部分成功，否则停止发布。

回滚口径：

1. 默认采用向前修复，不依赖自动 downgrade。
2. 代码已发布但 migration 未完成时，停止新版本服务并回到旧版本服务。
3. migration 已完成但服务健康检查失败时，优先判断是否可用补救配置或补救 migration；涉及数据破坏风险时恢复备份。
4. 任何恢复都必须记录 release id、数据库、恢复点、执行人、开始/结束时间和结果。

## Seed 执行契约

Seed 必须是显式步骤，不混在普通 Web 启动里偷偷执行。

每个 seed step 必须声明：

1. `seedName`：例如 `iam-default-permissions`、`iam-initial-admin`。
2. `seedVersion`：用于判断 seed 逻辑是否升级。
3. `ownerService`：拥有该 seed 的服务。
4. `idempotencyKey` 或幂等规则。
5. 输入来源：配置文件、环境变量、安全输入、安装参数或内置常量。
6. 重复执行结果：created、updated、skipped 或 failed。
7. 敏感信息处理：初始管理员密码、client secret 和 connector credential 不写入日志。

安装脚本必须输出：

```text
releaseId=<id>
service=<service>
dbProfile=<profile>
targetDatabase=<database-or-alias>
migrationFrom=<migration-or-empty>
migrationTo=<migration>
seedName=<seed-or-empty>
seedVersion=<version-or-empty>
durationMs=<duration>
correlationId=<id>
logPath=<path>
exitCode=<code>
```

## CAP System Tables 运维

CAP tables 是 system-owned，不是业务表：

1. 不手工修改 `cap_published_messages`、`cap_received_messages`、`cap_locks`。
2. 不把 CAP 表作为业务查询接口的数据源。
3. 排障时只能读取消息状态、重试次数、时间戳、异常摘要和锁 key 等诊断信息。
4. 清理策略必须由 CAP 配置、服务 migrator 或受控运维任务执行，不用 ad hoc SQL 删除。
5. CAP storage/outbox 必须纳入 database profile 验证；只验证 EF business tables 不算 profile 支持完成。
6. CAP 锁异常时先确认是否有并发 migrator、异常退出服务实例或 RabbitMQ 连接故障，再决定是否需要人工介入。

## 发布后验证

1. 查询每个服务的 applied migrations，确认目标 migration 已应用。
2. 启动服务并通过 health endpoint 验证数据库、RabbitMQ、Redis 和外部依赖。
3. 对 AppHub 执行最小 registration/heartbeat/state-snapshot smoke test。
4. 对 Ops 执行最小 operation task create/pending/result smoke test。
5. 对 IAM 执行默认管理员登录、refresh、logout、`/me` 和 Connector Host credential validation smoke test。
6. 确认 CAP outbox/inbox 无持续增长的异常失败消息。
7. 确认日志包含 release id、service、profile、migration from/to、duration 和 correlation id。
8. 归档诊断日志位置，保留到当前 release 验收结束。

## Release Gate

面向 PoC 或私有化交付前，至少完成：

1. AppHub/Ops/IAM 发布脚本或 migration bundle。
2. FileStorage 等新增服务的 schema catalog、migration、seed 和 profile 测试。
3. PostgreSQL 备份/恢复演练记录。
4. seed 清单和初始凭据安全处理方案。
5. CAP system tables retention 和排障说明。
6. 安装脚本诊断输出契约。
7. `Persistence:AutoMigrate=true` 在 PoC/private/prod profile 下被禁止或 hard fail。
8. 发布安装脚本通过 ADR 0010 脚本治理门禁，包含超时、结构化日志、进程树清理、作用域环境变量、敏感信息脱敏和诊断包输出。
