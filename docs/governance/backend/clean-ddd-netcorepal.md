# 后端 CleanDDD / NetCorePal 治理

本文规定 Nerv-IIP 平台 HTTP 服务采用 CleanDDD / NetCorePal 时的当前代码结构、依赖边界和实现约束。精确 .NET、框架、模板与包版本以 `Directory.Build.props`、`backend/Directory.Packages.props`、项目文件、模板帮助和 lock/restore 结果为准；本页不维护版本总账或阶段形成史。

## 适用范围

1. `backend/services/` 下拥有领域模型和持久化事实的平台/业务 HTTP 服务按本页治理。
2. PlatformGateway / BusinessGateway 采用 Web、Endpoint、响应、观测和契约消费约定；除非自身拥有持久化领域事实，不强制补 Domain/Infrastructure。
3. Connector Host 使用 `connector-hosts/` 的独立宿主模型，不套用服务三项目结构。
4. 与本页冲突的领域专属 ADR/Architecture 先判断是否为更窄、更新的规则；长期取舍变化必须通过 ADR。

## 模板与初始化

1. 新服务必须显式指定目标 framework、database、message queue 等会影响生成结构的模板参数，不依赖模板版本变化的默认值。
2. Nerv-IIP 自有 IAM、控制台和平台 AppHost 不得被模板附带的管理后台、局部 RBAC、局部 Aspire 等重复能力替代。
3. 每个服务不生成自己的完整 AppHost；统一拓扑仍由 `infra/aspire/` 管理。
4. 模板只提供起点。生成后必须按本仓库的包版本、观测、认证、provider、测试和目录边界裁剪。
5. 精确模板参数先从当前 `dotnet new` / 模板 metadata 核实；人工文档不得固定已经可能漂移的模板版本号。

## 数据库可替换性

1. 默认持久化 profile 由当前仓库配置决定；provider 选择只允许出现在 Infrastructure、DI/Program、部署配置、migration 和 provider 测试。
2. Domain、Application、Endpoint、SDK 和公开契约不得引用 provider 专有 API、专有 SQL 或把 PostgreSQL `jsonb`、array、函数、schema 等语义变成跨层契约。
3. 数据库替换目标是降低替换成本，不承诺零修改；migration、索引、SQL 方言、事务、CAP storage/outbox 与真实依赖测试必须按目标 profile 重新验证。
4. 每个服务拥有自己的 schema/数据库边界，不通过共享 DbContext 或跨 schema 表访问回退到大单体。
5. 候选 provider 在真实迁移、CAP、事务和测试未验证前不得写成生产支持能力。

## .NET 目标框架

1. 服务必须使用仓库统一目标框架，不允许单个服务自行漂移 major runtime。
2. runtime 升级必须集中评估 NetCorePal、EF Core provider、CAP storage、测试基础设施、CI/本地 SDK 和容器镜像兼容性。
3. 精确 TFM 从仓库构建配置读取；本页不复制当前/未来版本路线图。

## 服务结构

拥有领域与持久化事实的服务采用三层职责：

- `Domain`：聚合、实体、值对象、强类型 ID、领域不变式与领域事件；不得引用 Web/Infrastructure。
- `Infrastructure`：DbContext、EntityConfigurations、repository 实现、provider 接线、migrations 和外部基础设施适配。
- `Web`：Endpoint、command/query、validator、应用编排、领域事件 handler、integration event converter/consumer、DI/host。

Application 默认作为 Web 内部应用层目录，不因“Clean Architecture”名义机械拆第四项目。只有出现真实独立编译/依赖边界才拆项目。

## 聚合与强类型 ID

1. 聚合根继承框架 `Entity<TId>` 并实现 `IAggregateRoot`。
2. Guid 型领域 ID 优先使用 `IGuidStronglyTypedId`；确有有序长整型或可读协议 ID 需求时再选对应强类型 ID。
3. 持久化 ID 的生成权威必须唯一；不要同时在领域构造函数、EF generator 和数据库 default 各生成一遍。
4. 聚合内部状态只能通过领域行为维护；Endpoint/handler 不直接改内部集合绕过不变式。
5. 领域事件用过去式表达已经发生的事实，定义为实现 `IDomainEvent` 的记录；领域事件本身不执行外部 IO。

## Command、Query 与 Endpoint

1. Command 使用 `{Action}{Entity}Command`；有返回值用 `ICommand<TResponse>`，无返回值用 `ICommand`。
2. command、validator、handler 保持就近组织；领域规则放聚合/领域服务，handler 只做应用编排。
3. Query 不改变业务状态；读取模型应显式表达过滤、分页、scope 和稳定排序。
4. FastEndpoints 路由、鉴权和 metadata 必须在同一 endpoint 的声明方式中保持一致；一旦切换到 `Configure()`，不要把路由与鉴权拆散到第二处。
5. HTTP 响应沿用仓库统一 `ResponseData<T>` / `.AsResponseData()` 等当前契约基线；强类型 ID 可直接作为请求/响应契约时不要无意义解包再包装。
6. 新增或修改业务服务公开 HTTP endpoint 时，必须按当前 facade coverage 治理声明 `exposed` / `deferred` / `internal`，并同步对应机器伴随物；M2-I 完成前从 `docs/architecture/api-contract-and-codegen.md` 与 `facade-coverage-matrix.*` 路由。
7. `KnownException` 的用户可见性与 Gateway 传输遵循 [`../errors/user-visibility.md`](../errors/user-visibility.md)。

## 事务、领域事件与集成事件

1. CommandHandler 与其同一 UnitOfWork 内触发的 DomainEventHandler 共享事务；任一失败时业务事务回滚。
2. 领域事件用于同服务内的领域事实传播；跨服务事实必须通过明确的 integration event / contract 边界。
3. integration event 必须遵循当前公开契约、信封、幂等与消费治理；不得让消费者跨 schema 查询生产方数据库。
4. 外部副作用必须考虑 outbox/inbox、重复投递、乱序和失败重放，不把“handler 被调用一次”当作可靠性假设。

## Repository、DbContext 与 EF

1. repository 接口/实现与聚合所有权一致，不为简单查询制造跨域通用仓储。
2. DbContext、EntityConfigurations、migration 和 provider 配置留在 Infrastructure。
3. PostgreSQL 等具体 profile 的 schema、`__EFMigrationsHistory`、注释、索引、强类型 ID 与 migration 规则遵循 [`../data/database-schema.md`](../data/database-schema.md)。
4. 新增/删除/改变业务表时同步 [`../../reference/data/database-schema-catalog.md`](../../reference/data/database-schema-catalog.md)；物理结构仍以 migration/configuration 为准。
5. `RowVersion` 等并发模型使用仓库/框架当前约定，不另造第二套并发字段。

## Program 与基础设施注册

每个服务按其能力确认：DbContext/provider、repository、command/query、FastEndpoints、认证授权、CAP/消息、OpenTelemetry/共享 Observability、健康检查及必要 hosted service 已通过统一扩展注册。不要在各服务复制一套与共享基础设施平行的日志、认证、序列化或消息管线。

## 日志与观测

1. 业务代码只依赖 `ILogger<T>`，不直接依赖 Serilog 静态 API、具体 sink 或日志后端 SDK。
2. 宿主级日志、OpenTelemetry 与 correlation 由共享 Observability 库和当前宿主接线统一提供。
3. 跨服务请求、Connector 生命周期、任务创建/领取/回传等链路必须保留稳定 correlation/trace 上下文。
4. 稳定字段按当前 Observability 契约输出；没有上下文时不伪造 organizationId、actor 等字段。
5. 不记录 access/refresh token、密码、密钥、完整连接串、个人敏感信息、文件内容或大体积 payload。
6. 日志不是审计。用户动作、运维动作、审批、文件授权等需要长期追溯的事实必须写入对应领域或审计模型。

## 测试与验收

1. 聚合测试覆盖正常路径、拒绝路径、不变式和领域事件，不只测 getter/setter。
2. command/query/endpoint 测试验证真实授权、验证器、事务与公开错误契约；不要用 mock 成功代替关键领域结果。
3. provider、CAP、migration、真实数据库和跨服务闭环按测试治理进入相应 real-dependency lane；测试分层规则由测试 Governance 管理。
4. 新服务或大规模结构变更应通过受影响 solution/project、当前脚本入口和 CI impact plan 验证；只报告实际执行的 lane。

## 变更纪律

- 本页只随当前工程规则变化而更新；框架版本、模板版本和“已迁移多少服务”不在此记录。
- 历史调研、阶段形成史和迁移批次通过 Git/Reports 追溯。
- 当现有实现需要违反本页时，先写明更窄的例外及 owner；不能在业务代码里静默形成第二套结构。