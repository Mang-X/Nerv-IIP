# 数据库发布、迁移与恢复 Runbook

本文把 ADR 0009 的迁移/发布决策落实为当前操作者步骤。它不声明 Nerv-IIP 已具备完整客户安装包，也不保存第五/六/七阶段等形成历史；历史形成过程见 [`../reports/audits/database-release-stage-history.md`](../reports/audits/database-release-stage-history.md)。

命令事实以 `scripts/install/migrate-platform-databases.ps1`、`scripts/install/migrate-business-databases.ps1`、`scripts/install/migrate-file-storage.ps1`、两份 release database manifest、当前 EF migrations 和脚本帮助为准。执行前若本文与脚本参数不一致，**停止并先修正文档**。承载发布动作的脚本还必须满足 `docs/governance/script-automation.md`。

## 1. 当前执行边界

| Profile | 当前可执行边界 | 客户发布结论 | 权威入口 |
| --- | --- | --- | --- |
| PostgreSQL | AppHub、IAM、Ops、Notification 与 13 个业务数据库分别进入显式 migration manifest；FileStorage 使用独立受治理 migrator。 | 尚不等于完整客户发布；仍需备份恢复演练、seed 清单和现场诊断契约。 | `scripts/install/migrate-platform-databases.ps1`、`scripts/install/migrate-business-databases.ps1`、`scripts/install/migrate-file-storage.ps1` |
| GaussDB | 候选项。 | 不支持。 | 需要 provider、CAP storage/outbox、migration、JSON、时间、事务和集成测试证据。 |
| DMDB | 候选项。 | 不支持。 | 同上。 |
| 其他数据库 | 评估阶段。 | 不支持。 | 不属于当前公开 profile 基线。 |

`Persistence:AutoMigrate=true` 只用于受控本地或一次性开发验证。共享环境不建议使用；使用客户数据的 PoC、私有化部署和生产环境必须关闭，并走显式 migrator / release-install 入口。

## 2. 发布前置与停止条件

执行任何 migration 前确认：

1. 发布 ID、Git commit、服务版本、目标环境和数据库 profile 已冻结。
2. 连接串只来自受控输入，目标 database 名称与 manifest / `-ExpectedDatabase` 完全匹配；不得误连开发默认库、共享验证库或相邻客户库。
3. PostgreSQL、Redis、对象存储和观测依赖满足当前发布 profile；消息 provider 为 Redis 时核对持久化与恢复策略，为 RabbitMQ 时才核对 RabbitMQ。
4. 备份/快照已成功并记录位置、时间、校验方式和恢复负责人。**备份失败或无法确认位置时停止发布。**
5. 本次服务清单、执行顺序、seed 清单、幂等规则和初始凭据处理方式已批准。
6. 任一 migration 或 seed 失败时停止后续服务启动；结果不确定时先核对 migration history、公开健康状态和脚本日志，不盲重放。
7. 发布脚本必须是 `release-install` 或受控 migrator；不得用临时 SQL、`verify` 脚本或 Web 启动时 AutoMigrate 处理客户数据。
8. 执行 Quality migration `20260629074947_AddQualityLongtailReviewFixes` 前，先按 `docs/reports/remediation/business-quality-inspection-duplicates.md` 完成历史重复组检查/清理；不得静默删除或改写 NCR、事件或审计证据。
9. 如果旧库仍把 AppHub/Ops `__EFMigrationsHistory` 放在 `public`，先执行第 3 节升级前置；服务 schema 已存在正确历史记录的库不得重复把这一条件当作必经“阶段”。

## 3. 旧版 `public.__EFMigrationsHistory` 升级前置

仅当**保留数据的旧库**仍把 AppHub/Ops EF migration history 放在 provider 默认 `public` schema 时执行。一次性本地验证库可直接删除重建；当前已使用服务 schema 的数据库跳过本节。

AppHub：

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

Ops：

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

核对：

```sql
SELECT * FROM apphub."__EFMigrationsHistory" ORDER BY "MigrationId";
SELECT * FROM ops."__EFMigrationsHistory" ORDER BY "MigrationId";
```

确认服务 schema 已包含旧库已应用的 migration 后才继续。不要在同一次发布中删除 `public.__EFMigrationsHistory`；等备份、迁移和健康验证完成后再单独评估清理。

## 4. 平台数据库 migrator

当前 manifest 明确列出 `apphub`、`iam`、`ops`、`notification`。连接串只放在**当前 PowerShell 进程环境变量**中；脚本会验证目标 database、串行执行选中服务，任一服务失败即停止，不建库、不 seed、不删除或回滚已成功 migration。

```powershell
$env:NERV_IIP_APPHUB_DB = "<apphub-postgres-connection-string>"
$env:NERV_IIP_IAM_DB = "<iam-postgres-connection-string>"
$env:NERV_IIP_OPS_DB = "<ops-postgres-connection-string>"
$env:NERV_IIP_NOTIFICATION_DB = "<notification-postgres-connection-string>"

pwsh scripts/install/migrate-platform-databases.ps1 -ValidateOnly -ReleaseId "<release-id>"
pwsh scripts/install/migrate-platform-databases.ps1 -ReleaseId "<release-id>"

Remove-Item Env:\NERV_IIP_APPHUB_DB,Env:\NERV_IIP_IAM_DB,Env:\NERV_IIP_OPS_DB,Env:\NERV_IIP_NOTIFICATION_DB -ErrorAction SilentlyContinue
```

经过发布计划明确批准时可传 `-Service apphub,iam` 等子集；未传 `-Service` 时覆盖 manifest 全部服务。`-ValidateOnly` 只验证配置、目标 database 和仓库输入，不执行数据库 migration。

直接 `dotnet-ef database update` 只用于实现原理核查或开发排障，不是客户 release-install 入口。

### 4.1 业务数据库 migrator

13 个业务数据库由 `scripts/install/business-release-database-migrations.json` 显式登记 service、当前进程连接变量、expected database、Infrastructure project、startup project 与 DbContext，并通过固定选择该 manifest 的受治理包装入口执行。目标 database 必须预先存在；实际 apply 会在任何 restore 或 EF migration 前使用 `psql` 逐库执行只读存在性检查，全部选中目标通过后才串行迁移，首个失败立即停止。

```powershell
# 按 business-release-database-migrations.json 的
# connectionEnvironmentVariable 字段设置 13 个当前进程连接变量。
pwsh scripts/install/migrate-business-databases.ps1 -ValidateOnly -ReleaseId "<release-id>"
pwsh scripts/install/migrate-business-databases.ps1 -ReleaseId "<release-id>"

Remove-Item Env:\NERV_IIP_BUSINESS_MASTER_DATA_DB,Env:\NERV_IIP_BUSINESS_PRODUCT_ENGINEERING_DB,Env:\NERV_IIP_BUSINESS_INVENTORY_DB,Env:\NERV_IIP_BUSINESS_QUALITY_DB,Env:\NERV_IIP_BUSINESS_MES_DB,Env:\NERV_IIP_BUSINESS_DEMAND_PLANNING_DB,Env:\NERV_IIP_BUSINESS_BARCODE_LABEL_DB,Env:\NERV_IIP_BUSINESS_APPROVAL_DB,Env:\NERV_IIP_BUSINESS_WMS_DB,Env:\NERV_IIP_BUSINESS_INDUSTRIAL_TELEMETRY_DB,Env:\NERV_IIP_BUSINESS_MAINTENANCE_DB,Env:\NERV_IIP_BUSINESS_ERP_DB,Env:\NERV_IIP_BUSINESS_SCHEDULING_DB -ErrorAction SilentlyContinue
```

经过发布计划明确批准时可传 `-Service business-quality,business-mes` 等子集；未传 `-Service` 时覆盖全部 13 项。`-ValidateOnly` 只验证配置、目标 database 名和仓库中的 project/context/factory 配对，不连接数据库。入口不执行 seed、建库、备份、删除或回滚；连接串不得复制进命令行、仓库文件或日志。

## 5. FileStorage migrator

```powershell
$env:NERV_IIP_FILE_STORAGE_DB = "<file-storage-postgres-connection-string>"

pwsh scripts/install/migrate-file-storage.ps1 -ValidateOnly -ReleaseId "<release-id>"
pwsh scripts/install/migrate-file-storage.ps1 -ReleaseId "<release-id>"

Remove-Item Env:\NERV_IIP_FILE_STORAGE_DB -ErrorAction SilentlyContinue
```

默认目标 database 必须精确匹配 `nerv_iip_filestorage`；受控环境使用其它库名时显式传 `-ExpectedDatabase <database-name>`。`-ValidateOnly` 不执行数据库命令。PoC、私有化部署和生产环境保持 `Persistence:AutoMigrate=false`。

重跑规则：

1. 已应用 migration 的 `database update` 应为 no-op。
2. 失败后不得手工伪造 `__EFMigrationsHistory`；恢复备份或提交补救 migration。
3. 同一目标 database 同一时间只允许一个 migrator。

## 6. 备份、恢复与回滚

Docker/本地 PostgreSQL 示例仅用于受控环境；真实客户环境优先使用平台快照，没有快照时才使用 `pg_dump` 或等价受控工具：

```powershell
New-Item -ItemType Directory -Force -Path .\artifacts\db-backups | Out-Null
docker compose -f infra/docker-compose.dev.yml exec -T postgres pg_dump -U nerv -d nerv_iip_apphub > .\artifacts\db-backups\apphub-before-release.sql
docker compose -f infra/docker-compose.dev.yml exec -T postgres pg_dump -U nerv -d nerv_iip_ops > .\artifacts\db-backups\ops-before-release.sql
```

- 备份文件不得提交仓库或写入公开日志；恢复能力至少在非生产库演练一次。
- migration 未完成：停止新版本服务并回到旧版本服务。
- migration 已完成但健康检查失败：优先前滚修复；存在数据破坏风险时按批准恢复点恢复备份。
- seed 部分失败且未声明允许部分成功：停止发布。
- 每次恢复记录 release ID、数据库、恢复点、执行人、开始/结束时间和结果。

### 6.1 Quality 数量巡检 migrations

Quality 数量巡检链路依次引入 `AddPeriodicInspectionQuantityWatermark`、`AddPeriodicInspectionQuantityContinuationInbox` 与 `AddPeriodicInspectionQuantityContinuationFairness`。执行前除本节外仍须满足第 2 节的备份、版本冻结与失败停止条件。

1. 开始生成数量任务后，不执行 `AddPeriodicInspectionQuantityWatermark.Down`。该降级会丢失数量水位，再升级时既有任务唯一键会使后续消费者事务失败；使用补救 migration 前滚修复。
2. 开始写入 `processed_integration_events`、数量续批锚点或恢复进度后，不执行 `AddPeriodicInspectionQuantityContinuationInbox.Down`。该降级会丢失消费去重事实与恢复进度；使用补救 migration 前滚修复。
3. 开始写入 `quantity_continuation_next_attempt_at_utc` 或出现 closed + pending 上下文后，不执行 `AddPeriodicInspectionQuantityContinuationFairness.Down`。旧约束不能表达终态欠桶，且降级会丢失公平游标；使用补救 migration 前滚修复。
4. 如果上述 migration 已应用但新版本健康检查失败，保留现有 schema 和数据，停止新版本服务，按第 6 节从批准恢复点恢复，或发布包含补救 migration 的前滚版本；不得手工删除水位、inbox、锚点或公平游标。

### 6.2 BusinessMasterData 工装审计 migration

`20260825081539_AddToolingOperationAudit` 是纯新增 migration，不回填既有 `tooling_assets` / `tooling_applicability` 的伪历史审计。发布或恢复仍使用第 4.1 节的业务数据库 migrator 与第 6 节的批准备份/恢复入口，不用测试 runner、临时 SQL 或 Web `AutoMigrate` 代替客户发布流程。

1. migration 已应用但尚未产生工装审计事实时，旧版本服务可以忽略新增表继续运行；这不是执行 `Down` 的授权。
2. `business_masterdata.tooling_audit_entries` 一旦存在事实，不执行会删除该证据的 `Down`；发布失败时停止新版本服务并优先前滚补救。
3. 确需恢复备份时，记录 `releaseId`、目标数据库、批准恢复点、执行人、开始时间、结束时间和结果；恢复后重新核对 migration history 与工装业务/审计事实。
4. CI 或本地一次性 PostgreSQL profile 只验证 runner 自有数据库中的 migration、事务、并发与隔离行为，不构成客户生产迁移、备份或恢复演练，也不能据此宣称 BusinessMasterData 已具备完整客户 migrator。

### 6.3 BusinessMES 停机事件收件箱 migration

`AddMesProcessedEventInstanceIdentity` 为 `mes.processed_integration_events` 补回 `(ConsumerName, EventId)` 唯一索引，让 Maintenance AssetUnavailable 的 v1/v2 跨版本双投同时受事件实例身份与 `IdempotencyKey` 业务身份约束。该索引曾被 `UseIdempotencyKeyForProcessedIntegrationEvents` 移除，因此既有库里可能存在同 consumer、同 `EventId` 的多行历史。同一条历史 migration 在移除它的同时建立了 `(ConsumerName, IdempotencyKey)` 唯一索引，所以这些多行的 `IdempotencyKey` 必然两两不同：MES 不存在「同 `EventId` 且同 `IdempotencyKey`」的真重复分支，也就没有可自动清理的行。migration 按以下策略处理，不做静默清理：

1. 同 consumer、同 `EventId` 但 `IdempotencyKey` 不同的行是语义歧义：migration **fail-closed 中止**（PostgreSQL `RAISE EXCEPTION`，SQLSTATE `23000`），错误信息逐行列出 `ConsumerName / EventId / IdempotencyKey / ProcessedAtUtc`，由运维按发布说明显式裁决（确认为同一事实的重复登记后保留一行，或确认为不同事实后重赋 `EventId`）再重跑 migration；migration 不会替运维选择保留哪一行。
2. 中止时索引未创建、数据未改动、迁移历史未写入，旧版本服务可继续运行；这不是执行 `Down` 的授权。`Down` 只删除索引。
3. 索引创建后，旧版本 MES（只按 `IdempotencyKey` 去重）仍可运行；新版本消费者在同一事务内先赢得两项身份再登记停机与重排，重放死信不会形成第二条停机事实。

## 7. Seed 契约

Seed 是显式步骤，不混入普通 Web 启动。每个 seed 至少声明 `seedName`、`seedVersion`、`ownerService`、幂等规则、输入来源、重复执行结果和敏感信息处理。初始管理员密码、客户端密钥、Connector 凭据不得写入日志。

诊断输出至少能关联：

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

## 8. CAP 系统表运维

- `cap_published_messages`、`cap_received_messages`、`cap_locks` 由系统所有，不手工修改，也不作为业务读面。
- 清理通过 CAP 配置、服务 migrator 或受控运维任务执行，不用临时 SQL。
- CAP storage/outbox 必须进入数据库 profile 验证。
- 锁或消息异常先核对并发 migrator、异常退出实例及当前消息 provider；Redis 与 RabbitMQ 按实际 profile 分别排障。

## 9. 发布后验证与证据

1. 核对每个服务实际已应用 migrations。
2. 启动服务并验证数据库、Redis 与当前 profile 的外部依赖健康；验证消息消费在重启后可恢复。
3. AppHub 至少做 registration/heartbeat/state-snapshot 冒烟；Ops 至少做操作任务创建/待处理/结果冒烟；IAM 至少做登录、refresh、logout、`/me` 与 Connector Host 凭据冒烟。
4. CAP outbox/inbox 不应持续增长异常失败消息。
5. 保存 release ID、commit、service/profile、target database、migration 起止、duration、correlation ID、脚本 logPath、备份位置和最终健康结论。
6. 诊断日志保留到当前发布验收结束；敏感连接串和凭据不得进入证据包。

## 10. 面向客户交付前的门禁

至少具备：受治理的平台/业务数据库迁移编排、FileStorage migrator、PostgreSQL 备份恢复演练、seed/初始凭据安全方案、CAP 保留与排障策略、结构化诊断输出，以及 `Persistence:AutoMigrate=true` 在客户数据环境的失败关闭。发布安装脚本还必须满足 ADR 0010 的超时、结构化日志、进程树清理、作用域环境变量、敏感信息脱敏和诊断包要求。
