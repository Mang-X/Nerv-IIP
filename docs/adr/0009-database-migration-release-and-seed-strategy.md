# ADR 0009: 数据库迁移、发布与种子数据策略

- Status: Accepted
- Date: 2026-05-17

## Context

第四阶段已经为 AppHub 和 Ops 引入 PostgreSQL profile、EF Core `ApplicationDbContext`、netcorepal repository/unit of work 和平台级 Aspire AppHost；当时本地验证允许使用 `EnsureCreated()` 快速证明真实基础设施链路。第五阶段已把 AppHub/Ops 切换到 migration-based verification，并且 Web startup 只有在 dev/local 显式配置 `Persistence:AutoMigrate=true` 时才执行自动迁移。第七阶段 IAM 也已沿用 PostgreSQL migration、幂等 seed 和 schema convention baseline。

进入下一阶段后，IAM、FileStorage、Ops 审批、Notification、日志归档索引和部署脚本都会产生长期数据。平台必须先冻结迁移、初始化、seed 和回滚策略，避免各服务在启动时隐式改库，或由安装脚本直接绕过服务边界写业务数据。

## Decision

1. 生产、PoC 和可交付环境不得使用 `EnsureCreated()` 创建或升级业务库；当前 AppHub/Ops/IAM 的 PostgreSQL profile 以 EF Core migrations、受控 migrator/seed 和 dev-only `Persistence:AutoMigrate=true` 为基线。
2. 每个拥有持久化事实的服务拥有自己的 EF Core migrations。迁移文件放在该服务的 Infrastructure 项目或明确的 migrations 项目中，由该服务边界维护。
3. 普通 Web/Worker 服务启动时不得默认自动执行破坏性迁移。数据库升级由发布流程、安装脚本或专用 migrator step 显式执行。
4. Aspire、Docker Compose、Windows 安装脚本和 Linux 安装脚本都必须调用同一套迁移入口或等价 migrator 产物，不各自实现平行建表逻辑。
5. 首选迁移执行形态是 EF Core migration bundle 或专用 migrator 命令；如果某个服务需要其它迁移工具，必须在该服务架构文档中说明原因和兼容策略。
6. 种子数据必须幂等。系统内置权限码、初始角色、初始管理员、系统配置和 connector credential seed 通过受控 seed command 或 migrator step 写入，不通过脚本直接拼 SQL 写业务表。
7. 数据库升级默认采用向前修复策略。回滚重点是发布前备份、可重复迁移、兼容旧代码的扩展式 schema 变更和补救迁移；不得依赖不可验证的自动 downgrade。
8. 每个 release 在执行迁移前必须有备份或快照策略；安装脚本必须输出迁移版本、目标数据库、执行结果和诊断位置。
9. AppHub、Ops、Iam、FileStorage、Notification、Knowledge、AI Integration 和 Observability 索引各自维护数据边界；迁移不得跨服务写表或假设共享 DbContext。
10. database profile 替换必须覆盖迁移验证。PostgreSQL 是默认 profile；GaussDB、DMDB 等候选 profile 只有在 provider、迁移、CAP storage/outbox 和集成测试通过后才能进入支持矩阵。
11. 新增或变更业务表时必须同步维护建表注释和 schema catalog。具体规则见 `docs/architecture/database-schema-conventions.md` 与 `docs/architecture/database-schema-catalog.md`。
12. PoC、私有化和生产发布的数据库执行口径必须满足 `docs/architecture/database-release-runbook.md`；当前第五阶段迁移验证通过不等于完整客户发布 runbook 已完成。
13. 承载数据库迁移、seed、备份、验证或安装的脚本必须同时满足 ADR 0010 的脚本可信执行治理；本 ADR 只定义数据库发布边界。

## Rationale

1. `EnsureCreated()` 适合早期纵切，但绕过 migrations history，后续无法可靠升级、审计或回滚。
2. 显式 migrator step 能让部署、安装、备份和诊断拥有清晰边界，也避免服务启动时在并发实例中抢占 schema 变更。
3. 服务拥有自己的 migrations，可以保持数据边界，不把单仓早期便利演变成共享数据库模型。
4. 幂等 seed command 让安装脚本可以重复执行，也能复用服务端领域校验和审计挂点。
5. 向前修复比自动 downgrade 更符合私有化交付现实：客户数据不可随意丢弃，失败时需要可诊断、可补偿、可重跑。

## Consequences

1. 第五阶段已经为 AppHub/Ops 建立 migrations 与 migrator 入口；后续新增持久化服务继续沿用同一迁移、seed 和验证口径。
2. `scripts/verify-fourth-slice-real-infra.ps1` 可以继续作为本地门禁，但不能被解释为生产部署流程。
3. 部署脚本会变复杂，需要管理连接串、迁移执行顺序、备份提示、失败日志和重试语义。
4. 服务启动路径会更安全，但开发者需要显式运行迁移或使用验证脚本准备数据库。
5. 引入新的持久化服务时，必须同时补迁移、seed 和 profile 验证计划，不能只提交 DbContext 和实体。
6. schema 注释和 catalog 维护成为持久化变更的一部分；这会增加少量开发成本，但能支撑 ER 可视化、客户数据字典、部署审计和后续 agent 理解项目结构。
7. 数据库相关脚本必须显式声明目标库、profile、副作用和清理策略，避免把 disposable verification 习惯带入 PoC、私有化或生产发布。

## Alternatives Considered

1. 服务启动时自动 `Migrate()`：本地体验简单，但多实例和客户环境下容易产生并发迁移、权限过大和不可控停机窗口。
2. 长期保留 `EnsureCreated()`：实现最快，但没有 migrations history，无法支撑升级、回滚、审计和 profile 验证。
3. 安装脚本手写 SQL：对实施人员直观，但容易绕过服务边界、领域校验和跨 profile 兼容性。
4. 单一中央 migrations 项目管理所有服务：看似统一，但会削弱服务边界，后续更难独立发布和替换数据库 profile。
