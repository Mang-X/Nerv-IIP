# 平台上下文地图

本文档定义 Nerv-IIP 平台的核心上下文、服务边界与交互方式，用于约束后端服务划分、前端聚合接口和 Connector Host 协议设计。

## 总体视角

平台由三层上下文构成：

1. 平台控制面：负责身份、权限、组织、对外授权、文件存储、应用目录、实例事实、运维动作、审计、通知、AI 治理。
2. 应用接入面：负责 Connector Host、Connector、本地资源探测、状态上报与动作执行。
3. 知识与 AI 面：负责知识引入与检索、MCP 工具、模型接入与执行治理。

## 主平台独立性

1. 主平台只拥有通用控制面能力，不拥有行业组织模型、具体业务应用模型或 Connector 实现细节。
2. 行业扩展、示例应用、Connector Host 和具体 Connector 都是主平台之外的独立演进单元。
3. 主平台通过 Platform SDK 向应用、Connector Host 和扩展模块提供契约、认证、授权上下文、客户端和扩展点能力。
4. 主平台与外部演进单元只通过 Platform SDK、版本化公开契约、公开 API、集成事件和 IAM 授权关系协作。
5. 主平台与应用、Connector Host、行业扩展采用主版本对齐策略：例如主平台 1.x 只承诺兼容 1.x 应用和 1.x SDK。
6. 同一主版本内，小版本应用可以低于主平台小版本；主平台 1.5 应尽量兼容基于 1.0 到 1.5 SDK 构建的应用。
7. 同一主版本内应避免破坏性 SDK、API、事件或协议变更；确需破坏性变更时必须提升主版本，并提供迁移窗口。
8. Connector Host、Connector 或行业扩展不得通过引用主平台内部 Domain、Infrastructure、数据库表或私有接口来获得能力。
9. Platform SDK 只提供模块化客户端能力，不拥有服务发现事实、权限事实、审计事实或文件事实。

## 服务上下文

### IAM

- 负责用户、角色、权限、组织、环境、访问策略和外部应用授权关系。
- 提供统一身份、权限与授权事实。
- 是平台内部访问控制、平台应用身份权限管理和对外授权的事实源。
- 不承载应用注册、实例状态或运维动作。
- 不内置工厂、产线、设备等行业组织模型；这些概念应由后续领域扩展、应用扩展或插件式业务模块承载。

### File Storage

- 负责文件元数据、上传会话、下载授权、对象存储 key、保留策略和归档状态。
- 是主平台关于“文件如何被保存、访问和治理”的事实源。
- 二进制内容默认落到 MinIO 或等价对象存储，但对象存储内部 key 不作为公开业务契约。
- tus、S3 multipart 和平台中转上传通过 Upload Provider 抽象接入，不成为业务服务依赖的领域模型。
- filePurpose、大小限制、content type allowlist、scanStatus、保留策略和配额口径由 File Storage 统一治理。
- 不解释文件的业务语义；KnowledgeSource、OperationTask、Application 等业务对象只通过 fileId 或 FileReference 关联文件。

### AppHub

- 负责应用目录、版本、节点、能力声明、实例事实、实例存活与 reported state。
- 是平台关于“当前管理了哪些应用和实例”的事实源。
- 不负责任务执行、审批与审计闭环。

### Ops

- 负责动作任务、执行尝试、审计记录、失败分类、审批挂点与结果回传。
- 所有会改变目标系统状态的动作都应进入 Ops 的任务闭环。
- 不成为实例最终状态的真相源。

### Notification

- 负责站内通知、待办入口、接收人解析、通知偏好、去重合并、已读未读、投递状态和通道适配边界。
- 消费 AppHub、Ops、AI Integration、Knowledge 等服务发布的平台事实或明确通知意图，把它们转换为用户可见消息、待办或外部通道投递。
- 不拥有业务触发规则，不替代 Ops 审计、Observability 告警，也不把 RabbitMQ/CAP 集成消息直接暴露给最终用户。

### AI Integration

- 负责模型提供方配置、MCP Server、Skill 注册、工具授权、执行审批与人机确认编排。
- 不托管模型，不拥有知识索引。

### Knowledge

- 负责知识源接入、文档抽取、分块、嵌入、索引、检索、权限过滤和引用回显。
- 为 AI Integration、Gateway 和其他平台服务提供统一检索能力。

### PlatformGateway

- 负责前端聚合查询、上下文透传、页面级 BFF 接口。
- 不沉淀领域规则，不直接依赖任何服务的 Domain 或 Infrastructure。

### Connector Host

- 负责本地资源发现、能力探测、命令执行、日志采集、备份入口与本地状态上报。
- Connector 是 Connector Host 的适配层，而不是平台服务的一部分。

## 边界关系

### Platform SDK 与服务边界

1. Platform SDK 是公开客户端能力集合，不是新的运行时中心。
2. `Sdk.Auth` 可以处理 token、client credential 和认证头，但最终授权判断和会话事实仍归 IAM。
3. `Sdk.ConnectorProtocol` 可以发送注册、心跳和状态快照，但本地资源发现仍归 Connector Host 与 Connector，应用与实例事实仍归 AppHub。
4. `Sdk.FileStorage` 可以创建上传会话、获取上传指令和下载授权，但文件元数据与对象存储定位事实仍归 File Storage。
5. `Sdk.Ops` 可以创建任务、查询任务和回传动作结果，但 OperationTask、OperationAttempt 与 AuditRecord 仍归 Ops。
6. `Sdk.Notification` 可以提交通知意图、查询通知和标记已读，但 NotificationIntent、NotificationMessage、偏好和投递状态仍归 Notification。
7. `Sdk.Observability` 可以提供 correlationId、trace context 和标准日志字段，但不替代平台日志采集、保留策略或审计落库。
8. SDK 模块之间只通过公开 DTO 和 `Sdk.Core` 协作，不通过服务端内部项目、数据库表或私有接口协作。

### IAM 与其它服务及外部应用

1. IAM 拥有用户、角色、权限、组织、环境、外部客户端和授权授予事实。
2. AppHub、Ops、Notification、Knowledge、AI Integration 和 Gateway 只能消费 IAM 的身份、权限和授权判断，不各自维护平行权限模型。
3. 平台应用对外授权时，应显式绑定组织、环境、资源范围、能力范围和有效期；外部应用获得的是受约束访问能力，不获得跨服务内部事实所有权。
4. 对外授权、工具授权和运维动作审批是不同概念：IAM 提供身份与权限授予事实，AI Integration 负责工具治理，Ops 负责动作任务与审计闭环。

### File Storage 与其它服务

1. File Storage 拥有文件元数据、上传下载授权、对象存储 key 和保留策略。
2. Knowledge、Ops、AppHub 和行业扩展只能通过 fileId、FileReference、File Storage API 或 Platform SDK 使用文件能力。
3. Knowledge 拥有知识源、解析、分块、嵌入和索引事实；File Storage 只管理原始文件和派生附件的存储治理。
4. Ops 拥有动作任务和审计事实；File Storage 只保存日志包、诊断包、备份包或审计附件的文件事实。
5. UI、外部应用和 Connector Host 不直接访问 MinIO；需要上传或下载时必须由 File Storage 发放受控入口或短期授权。
6. UI、外部应用和 Connector Host 可以根据 UploadInstructions 使用 tus 或 S3 multipart 上传，但不能绕过 UploadSession 完成校验与提交。

### AppHub 与 Ops

1. AppHub 拥有应用、版本、节点、能力、实例、存活和状态事实。
2. Ops 拥有动作任务、执行结果、审计与审批挂点。
3. restart 一类动作由 Ops 创建任务并记录结果。
4. 实例最终状态是否变化，由 Connector Host 后续状态同步驱动 AppHub 更新，而不是由 Ops 直接改写。

### Notification 与其它服务

1. Notification 消费跨服务集成事件或明确的通知意图，不直接进入其它服务的命令事务做外部通道投递。
2. AppHub、Ops、AI Integration、Knowledge 等服务只表达已发生事实、待处理事项或建议接收范围，不各自实现站内通知、邮件、短信、企业 IM 或 Webhook 投递。
3. Notification 可通过 IAM 解析用户、角色、组织、环境和授权范围，但不维护平行权限模型。
4. Notification 记录投递、已读未读、偏好和去重事实；审计事实仍归 Ops 或对应领域服务，日志与指标仍归 Observability。

### AI Integration 与 Knowledge

1. AI Integration 负责工具治理与模型接入。
2. Knowledge 负责文档与检索数据处理。
3. Knowledge 不是 MCP 执行编排器。
4. AI Integration 不是知识索引服务。

### Gateway 与领域服务

1. Gateway 只能聚合查询和透传上下文。
2. Gateway 不接管领域规则。
3. 前端不应直连多个服务，优先通过 Gateway 或明确服务 API。

## 交互方式

### 同步调用

- 登录、鉴权、页面查询聚合、必要的强一致入口可走同步 HTTP API。
- 同步 API 应优先依赖服务 Contracts，而不是服务内部实现。

### 异步传播

- 注册后产生的目录更新、状态变化、任务结果、不可达标记等优先通过集成事件传播。
- 禁止通过共享表实现跨服务协作。

## 首批纵切

### 应用注册到实例可见

1. Connector Host 发送注册契约。
2. AppHub 创建或更新 Application、ApplicationVersion、ManagedNode、ApplicationInstance。
3. Gateway 查询到新增实例事实。

### 心跳与状态同步

1. Connector Host 上报心跳。
2. AppHub 更新实例存活投影。
3. Connector Host 上报实例状态。
4. AppHub 更新 ApplicationInstance.reportedStatus 与状态历史。

### 低风险动作闭环

1. 控制台或 Gateway 发起重启动作。
2. Ops 创建 OperationTask 与 AuditRecord。
3. Connector Host 执行动作并回传结果。
4. Ops 记录 OperationCompleted 或 OperationFailed。
5. AppHub 通过后续状态同步确认最终实例状态。

## 不允许的耦合

1. 服务之间直接共享数据库表。
2. Gateway 直接依赖服务 Domain 或 Infrastructure。
3. Connector 直接依赖 AppHub、Ops、IAM 的内部实现。
4. AI Integration 直接维护知识索引。
5. Ops 成为实例状态真相源。
6. 业务服务、前端、外部应用或 Connector Host 绕过 File Storage 直接使用对象存储 key 作为长期业务契约。
7. Platform SDK 反向引用主平台服务 Domain、Infrastructure、数据库表或私有接口。
8. Platform SDK 直接写入最终 AuditRecord、权限授予、会话撤销或应用实例事实。
9. AppHub、Ops、AI Integration、Knowledge 或行业扩展各自直连外部通知通道，绕过 Notification 的接收人解析、偏好、去重、投递状态和审计挂点。
