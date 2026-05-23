# 数据库 Schema、建表与注释规范

本文档定义 Nerv-IIP 后端服务的数据库 schema、迁移、建表注释和可视化元数据约定。它补充 ADR 0009 的迁移发布策略，已用于 AppHub/Ops/IAM，并作为后续 FileStorage、Notification、Knowledge、AI Integration、Observability 索引等持久化服务的落地规则。

目标不是提前设计所有表，而是把“以后每次建表必须做到什么程度”说清楚，避免后续功能推进时只留下 EF 模型和迁移，却缺少人能读、工具能展示、部署能审计的结构说明。

## 权威来源

1. 领域模型和不变式以各服务 Domain 项目为准。
2. 物理表结构以服务 Infrastructure 项目中的 EF Core migrations 为准。
3. 表名、列名、长度、索引、外键、注释和 provider 映射以 `{Entity}EntityTypeConfiguration` 为准。
4. 面向人和可视化工具的解释以 `docs/architecture/database-schema-catalog.md` 为准。
5. 迁移执行、发布、seed 和回滚策略以 `docs/adr/0009-database-migration-release-and-seed-strategy.md` 和 `docs/architecture/database-release-runbook.md` 为准。

任何 schema 变更都必须让这些来源保持一致。不能只改 Domain，也不能只改 migration 文件。

## 服务边界

1. 每个拥有持久化事实的服务拥有自己的 schema、DbContext、EntityConfigurations 和 migrations。
2. 服务不得跨 schema 建外键，不得通过共享 DbContext 读写其他服务表。
3. 跨服务引用使用稳定业务标识、IntegrationEvent、查询 API 或后续专用 projection，不用数据库外键表达。
4. 默认 PostgreSQL profile 下，每个服务使用独立默认 schema，已落地示例包括 `apphub`、`ops`、`iam`；后续示例包括 `filestorage`、`notification`、`knowledge`、`ai` 或 `observability`。
5. CAP、EF migrations history 和框架自带表仍放在服务 schema 内，归属于该服务的基础设施边界。
6. PostgreSQL profile 下必须显式配置 EF migrations history table，例如 `MigrationsHistoryTable("__EFMigrationsHistory", "apphub")`，避免历史表落到 provider 默认 schema 后削弱服务边界。

## 命名

1. Schema 名使用小写服务名或明确缩写，例如 `apphub`、`ops`。
2. 表名使用小写 snake_case 复数或领域内稳定集合名，例如 `applications`、`operation_tasks`。
3. 列名遵循 EF Core 默认映射；如显式命名，使用小写 snake_case，并在配置中说明原因。
4. 主键列默认为 `Id`，Domain 类型使用强类型 ID。
5. 外键列使用 `{ReferencedEntity}Id`，跨服务业务引用不命名为外键。
6. 索引名默认由 EF 生成；只有在部署脚本、排障或跨 profile 兼容需要稳定命名时才显式命名。

## 建表前置条件

新增业务表前必须同时准备：

1. 领域对象、强类型 ID 和聚合边界说明。
2. `{Entity}EntityTypeConfiguration`，包含主键、必填、长度、转换、索引、外键、删除行为和注释。
3. EF Core migration，且能从空库迁移到当前模型。
4. schema catalog 条目，说明表用途、所有者、关键列、索引意图和数据生命周期。
5. profile 验证入口，至少覆盖 PostgreSQL；候选信创 profile 进入支持矩阵前必须单独验证迁移。
6. 如果表需要初始化数据，必须设计幂等 seed command 或 migrator step，不允许安装脚本直接拼 SQL 写业务表。
7. 如果字段会以 JSON/text 形式进入 API、SDK、IntegrationEvent 或外部协议，必须先定义 schema/version/compat 说明。

## 注释规范

数据库注释是长期结构资产，不是装饰。后续做 ER 图、客户部署审计、数据字典、故障排查和 AI 辅助结构理解时都依赖它。

1. 每张业务表必须有表注释。EF 配置应使用 `ToTable("table_name", table => table.HasComment("..."))` 或等价写法。
2. 每个业务列必须有列注释。包括 ID、业务键、状态、时间、软删除、行版本、JSON/text 序列化字段。
3. 注释描述字段语义，不重复类型。例如写“Connector host 上报的能力集合 JSON”，不要只写“string column”。
4. 序列化字段必须说明格式、生产方、消费方和兼容策略。至少写清是 JSON、是否由平台生成、是否允许 connector 扩展。
5. 状态字段必须在 catalog 中链接或列出允许值来源，避免状态枚举只散落在代码里。
6. 时间字段必须说明时区，默认使用 UTC。
7. 软删除字段统一说明为“Soft delete flag”，RowVersion 统一说明为“Optimistic row version”，除非该服务另有 ADR。
8. 注释语言默认使用简洁英文，便于数据库工具、OpenAPI 和多语言交付统一展示；如客户交付需要中文数据字典，在 catalog 中补中文解释，不改物理注释口径。
9. CAP、EF migrations history 等框架表可以不逐列补注释，但必须在 catalog 中标记为 system-owned，并说明由哪个框架维护。

## 类型与强类型 ID

1. Domain、Application、Endpoint 和 SDK 不暴露 provider 专有类型。
2. `IGuidStronglyTypedId` 使用 NetCorePal EF `UseGuidVersion7ValueGenerator()`，领域构造函数不手动生成持久化 ID。
3. `IInt64StronglyTypedId` 使用 Snowflake 值生成器。
4. `IStringStronglyTypedId` 只在 ID 需要成为外部协议可读、调试友好或由业务命令显式生成时使用；否则默认优先 Guid v7。
5. String 强类型 ID 必须 `ValueGeneratedNever()`，必须设置 `HasMaxLength`，必须在 Domain 或 Application 层有唯一生成权威，并通过幂等键、前缀、随机段或序列规则说明碰撞规避方式。
6. 业务自然键可使用 string，但必须设置 `HasMaxLength`，并在 catalog 中说明来源和唯一范围。
7. JSON/text 字段只作为跨版本扩展或外部 payload 存储；核心查询条件不得长期藏在 JSON 内。
8. JSON/text 字段如进入 API/SDK，必须有可版本化 schema 或兼容说明；新增可选字段默认兼容，删除字段、改变必填性或改变语义属于契约变更。
9. 金额、容量、计数、耗时等数值字段必须明确单位，单位写进属性名或注释。

## 索引与约束

1. 每个唯一业务规则必须落到唯一索引或唯一约束，不能只靠应用层判断。
2. 每个列表查询、调度领取、幂等检查和状态扫描都必须有对应索引。
3. 每个索引都要在 entity configuration 附近或 schema catalog 中说明服务的查询/约束意图。
4. 外键删除行为必须显式配置。聚合内子实体通常 `Cascade`；跨聚合和跨服务不得用级联删除隐藏业务影响。
5. 可空列必须有明确原因。业务必填字段应配置 `IsRequired()`。
6. 字符串列必须配置长度。除非有明确大文本语义，不允许无边界 string。
7. 默认唯一索引不包含 `Deleted`，表示软删除后的业务键不自动释放。若某服务允许软删除后复用业务键，必须使用明确的 filtered/partial unique index 或等价机制，并说明恢复、审计和重复注册语义。

## 迁移与发布

1. 生产、PoC 和可交付环境不得使用绕过 EF migrations history 的建表路径创建或升级业务库。
2. Web/Worker 默认启动不得自动迁移；本地/dev 验证可通过 `Persistence:AutoMigrate=true` 显式开启。
3. migration 文件属于服务 Infrastructure 项目或明确 migrations 项目，不放在 Web 项目。
4. 修改实体配置后必须检查 pending model changes，并生成/调整 migration。
5. 每个 PostgreSQL profile DbContext 必须显式配置 `__EFMigrationsHistory` 所在 schema。
6. 不手写与 EF migration 平行的建表 SQL，除非该 provider 无法可靠由 EF 表达；这种例外必须写入服务架构文档。
7. 删除列、改类型、改唯一范围、拆表和迁移数据都要采用可前滚的兼容策略，并在 release notes 或服务计划中说明。
8. 每次 release 执行迁移前必须有备份/快照策略，并记录迁移版本、目标数据库和日志位置。
9. `Persistence:AutoMigrate=true` 的环境边界以 `docs/architecture/database-release-runbook.md` 的矩阵为准。

## DbContext 配置顺序

新服务默认使用统一顺序：

1. 调用 `base.OnModelCreating(modelBuilder)`。
2. 调用 `modelBuilder.HasDefaultSchema("<service-schema>")`。
3. 调用 `modelBuilder.ApplyConfigurationsFromAssembly(...)`。
4. 配置 CAP/system tables。

若框架升级或 provider 要求调整顺序，必须在服务架构文档或 migration hardening 记录中说明原因；同一服务内不要混用多套顺序。

## Seed 与基础数据

1. Seed 数据必须幂等，可重复执行。
2. 系统权限码、初始角色、初始管理员、系统配置、connector credential seed 通过服务内 command 或 migrator step 写入。
3. Seed 不绕过领域校验，不直接访问其他服务 schema。
4. Seed 结果需要可诊断：失败时输出 correlation id、服务名、seed 名称、seed 版本、数据范围、结果和日志位置。
5. 默认管理员密码、client secret、connector credential 等敏感 seed 输入不得写入日志或 catalog。

## Database Profile 兼容

1. PostgreSQL 是当前默认并已落地 profile。
2. GaussDB、DMDB 等候选 profile 必须覆盖 EF provider、CAP storage/outbox、migration、索引、时间、JSON 和事务验证。
3. 不把 PostgreSQL `jsonb`、array、函数、schema 行为写进 Domain/Application/Endpoint/SDK 契约。
4. profile 差异只能出现在 Infrastructure、DI、部署配置、migration 和 profile 测试中。
5. 新增 provider 前先完成最小 schema 迁移验证，再扩展业务功能。
6. 未通过 `implemented`、`validated` 和 `release-supported` 三个状态的 profile，不得写成客户交付支持项。

## 可视化与数据字典

1. `database-schema-catalog.md` 必须能独立回答“这个服务有哪些表、每张表干什么、关键关系是什么、哪些表由框架维护”。
2. catalog 不是 migration 的复制品。它记录业务语义、索引意图、生命周期和后续注意事项。
3. 每个服务新增表、删除表、改变关键关系时都必须更新 catalog。
4. 可视化工具需要的稳定信息优先来自数据库注释和 catalog，不依赖个人口头解释。
5. 后续如果引入自动 ER 图或数据字典生成器，生成产物必须以 migrations 和注释为输入，不反向成为 schema 权威。

## Schema Convention Tests

AppHub/Ops/IAM 已通过 `Nerv.IIP.Testing` 中的 schema convention helper 覆盖 business table comment、business column comment、JSON/text 兼容注释（在相关字段存在时）、string ID 约束和 service-schema `__EFMigrationsHistory`。后续 FileStorage、Notification、Knowledge、AI Integration 和 Observability 索引建表时必须复用同一类测试。

自动化测试至少检查：

1. 每张 business table 有 table comment。
2. 每个 business property 有 column comment。
3. JSON/text property 的注释包含 JSON 格式、生产方或消费方、兼容策略。
4. system-owned tables 在 catalog 中有条目。
5. `IStringStronglyTypedId` 主键使用 `ValueGeneratedNever()` 和长度限制。
6. PostgreSQL profile 显式配置 service schema 的 `__EFMigrationsHistory`。

## 当前必须补齐的已知差距

1. CAP system tables 当前只在 DbContext 中配置表名和主键，后续应至少补表注释或在 catalog 中保持 system-owned 标记。
2. FileStorage、Notification、Knowledge、AI Integration 和 Observability 索引尚未建表；首次建表前必须先补 catalog 草案和 schema convention tests。
