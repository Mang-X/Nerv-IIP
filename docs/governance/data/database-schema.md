# 数据库 Schema 治理

本文规定 Nerv-IIP 后端服务的数据库 schema、迁移、注释、索引、seed 与 provider 边界。它补充 ADR 0009 的迁移发布策略；当前物理结构仍由代码 producer 决定，不以本页代替 migration 或 EntityConfiguration。

## 权威来源

按事实类型分别回到：

1. 领域模型与不变式：各服务 Domain；
2. 物理表结构与演进：各服务 EF Core migrations；
3. 表/列映射、长度、索引、外键、注释与 provider 映射：EntityConfigurations；
4. 人工 Schema 目录与业务解释：[`../../reference/data/database-schema-catalog.md`](../../reference/data/database-schema-catalog.md)；
5. 发布、迁移、seed、回滚与 `AutoMigrate` 操作边界：ADR 0009 + [`../../runbooks/database-release.md`](../../runbooks/database-release.md)。

Schema 变更必须让相关 producer 和人工 Reference 保持一致；不能只改 Domain，也不能只补一份 migration 文本。

## 服务边界

1. 每个拥有持久化事实的服务拥有自己的 schema、DbContext、EntityConfigurations 和 migrations。
2. 服务不得跨 schema 建外键，不得通过共享 DbContext 读写其他服务表。
3. 跨服务引用使用稳定业务标识、公开 API/contract、integration event 或专用投影，不用数据库外键表达。
4. CAP、EF migration history 和框架表归属于服务基础设施边界；PostgreSQL profile 必须显式把 `__EFMigrationsHistory` 放在服务 schema。

## 命名

1. schema 使用稳定小写服务名或明确缩写。
2. 表名使用小写 `snake_case` 复数或领域内稳定集合名。
3. 显式列名使用小写 `snake_case`；主键 Domain 属性默认为 `Id`，跨服务业务引用不得伪装成数据库外键。
4. 索引默认由 EF 生成；只有部署、排障或跨 profile 兼容确需稳定名称时显式命名。

## 新增业务表前置条件

新增业务表前必须同时具备：

1. 领域对象、强类型 ID 和聚合边界；
2. EntityConfiguration：主键、必填、长度、转换、索引、外键、删除行为和注释；
3. migration；
4. Schema Reference 条目：用途、owner、关键列、索引意图和生命周期；
5. 至少默认数据库 profile 的迁移/模型验证；
6. 需要基础数据时采用幂等 seed command/migrator，而不是安装脚本直接写业务表；
7. JSON/text 若进入 API、SDK、integration event 或外部协议，必须有版本/兼容说明。

## 数据库注释

1. 每张业务表必须有表注释；每个业务列必须有列注释。
2. 注释解释业务语义、单位、时间口径、JSON producer/consumer、软删除/并发含义，不复制属性名。
3. 时间字段必须说明时区，默认 UTC。
4. 软删除、并发版本等跨服务通用概念使用稳定统一语义；服务有不同长期规则时通过更窄 ADR/Governance 说明。
5. 物理注释默认使用简洁英文以便工具和多语言部署读取；中文业务解释放在 Schema Reference。
6. CAP、EF migration history 等系统表可不逐列补注释，但必须在 Reference 标记框架 owner。

## 类型与强类型 ID

1. Domain/Application/Endpoint/SDK 不暴露 provider 专有类型。
2. Guid 强类型 ID 使用仓库当前 NetCorePal/EF generator；持久化 ID 只允许一个生成权威。
3. Int64/string 强类型 ID 只有真实业务或协议需求时使用，并显式配置 generation/length。
4. string 业务自然键必须有长度和唯一范围说明。
5. JSON/text 只用于扩展或外部 payload；核心查询条件不得长期藏在 JSON。
6. 金额、容量、计数、耗时等字段必须明确单位，单位体现在命名、注释或稳定契约中。

## 索引与约束

1. 唯一业务规则落到数据库唯一索引/约束，不只靠应用层判断。
2. 列表查询、调度领取、幂等检查和状态扫描要有对应索引及意图说明。
3. 外键删除行为显式配置；跨聚合/跨服务不得用级联删除隐藏业务影响。
4. 可空字段必须有明确原因；业务必填字段配置为 required。
5. string 默认有长度；无明确大文本语义不允许无边界 string。
6. 软删除后业务键是否释放必须显式设计；需要复用时使用明确的过滤/部分唯一索引或等价机制，并说明恢复和审计语义。

## Migration 与发布

1. 生产、PoC 和交付环境不得绕过 EF migration history 创建/升级业务库。
2. Web/Worker 默认启动不得自动迁移；允许自动迁移的环境必须显式配置并符合 [`../../runbooks/database-release.md`](../../runbooks/database-release.md)。
3. migration 位于 Infrastructure 或明确 migrations 项目，不放 Web。
4. EntityConfiguration 变化后检查 pending model changes，并同步生成/审核 migration。
5. 不维护一套与 EF migration 平行的手写建表 SQL；provider 确实无法可靠表达时必须登记窄例外及验证。
6. 删除列、改类型、改唯一范围、拆表或数据迁移采用可前滚的兼容策略，并进入发布说明/计划。
7. release migration 前按 Runbook 执行备份/快照、目标确认、版本和日志记录。

## DbContext 顺序

默认顺序：

1. `base.OnModelCreating(modelBuilder)`；
2. `HasDefaultSchema("<service-schema>")`；
3. `ApplyConfigurationsFromAssembly(...)`；
4. 配置 CAP/系统表。

provider/框架确需不同顺序时，要在对应服务当前文档或 migration 说明原因；同一服务不要混用两套。

## Seed

1. seed 必须幂等、可重复执行，不覆盖租户已维护事实，除非该项明确由平台拥有并有兼容策略。
2. 系统权限、初始角色/管理员、系统配置和机器凭据由服务内 seed/migrator 写入，不跨 schema 直接插表。
3. seed 不绕过领域校验。
4. 失败诊断记录 correlation、服务、seed 名、范围和结果，但不得记录密码、token、client secret、完整连接串等敏感输入。

## Provider Profile

1. 默认 profile 以当前部署配置为准；候选 profile 必须独立验证 EF provider、CAP storage/outbox、migration、索引、时间、JSON 和事务。
2. provider 差异只出现在 Infrastructure、DI、部署、migration 与 profile 测试。
3. 未验证的 profile 不得写成生产/客户支持能力。
4. 新 provider 先完成最小 migration 与真实数据库验证，再扩大业务支持。

## Schema Reference 与可视化

1. [`../../reference/data/database-schema-catalog.md`](../../reference/data/database-schema-catalog.md) 回答“有哪些表、做什么、关键关系/索引/生命周期是什么”，不复制 migration 逐行结构。
2. 新增/删除表或改变关键关系时同步 Reference。
3. 自动 ER 图/数据字典若引入，必须以 migration/注释等 producer 为输入；生成物不能反向成为 schema 权威。

## Schema 约定测试

适用服务的自动化至少验证：

- 业务表/列注释；
- JSON/text 兼容语义（相关字段存在时）；
- string 强类型 ID generation/length；
- 服务 schema 的 `__EFMigrationsHistory`；
- 系统表能在 Reference 中识别其框架 owner。

具体测试 helper、lane 和适用服务集合以当前测试代码与测试 Governance 为准，本页不维护“哪些服务已覆盖/尚未覆盖”的状态表。

## 变更纪律

- 本页只写现态约束；历史服务批次、CAP 注释欠账、尚未建表服务和时点盘点留在 Issue/Reports/Git。
- Schema/Provider 行为变化必须先改 producer 与测试，再同步本页和 Reference；不能通过改文档让未验证行为变成受支持。