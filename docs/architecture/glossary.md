# 核心术语表

本文档统一 Nerv-IIP 首批架构文档中的核心术语。术语定义优先服务于工程沟通、契约命名、代码目录命名和验收口径；若后续 ADR 调整了边界，应同步更新本文档。

## 使用规则

1. 文档、代码、接口与数据库命名应优先使用本文档中的英文术语。
2. 同一概念不要在不同服务中换名，例如 ApplicationInstance 不应在 Ops 中被改称为 Target、Device 或 Runtime。
3. 中文说明可以按语境表达，但首次出现关键概念时建议同时带上英文术语。
4. 当术语含义跨越服务边界时，以拥有该事实的服务为准。

## 平台上下文

| 术语 | 中文口径 | 归属 | 说明 |
| --- | --- | --- | --- |
| Control Plane | 平台控制面 | 平台整体 | 管理身份、权限、组织、对外授权、应用目录、实例事实、运维动作、审计与 AI 治理的后台能力集合。 |
| IAM | 身份权限服务 | IAM | 统一管理用户、组织、角色、权限、外部客户端与授权事实，是平台内部鉴权和对外授权的事实源。 |
| Application Onboarding Plane | 应用接入面 | Agent Host | 通过 Agent Host 与 Connector 发现、观测和控制受管目标的接入能力集合。 |
| Knowledge and AI Plane | 知识与 AI 面 | Knowledge、AI Integration | 管理知识引入、检索、模型接入、工具治理与人机确认的能力集合。 |
| Organization | 组织 | IAM | 权限、环境、用户与资源隔离的上层边界。 |
| Plant | 工厂 | IAM | 组织下的工业现场或生产单位，可用于环境、用户与资产归属。 |
| Environment | 环境 | IAM | dev、test、prod 或客户现场等运行边界，所有跨环境操作必须显式携带。 |
| ExternalClient | 外部客户端 | IAM | 需要被底座授权以访问平台 API、MCP 工具或受管应用能力的外部应用或系统，不等同于 AppHub 的 Application。 |
| AuthorizationGrant | 授权授予 | IAM | 记录某个用户、角色、组织或服务主体对外部客户端、资源范围或能力范围的授权关系。 |
| PermissionScope | 权限范围 | IAM | 可被授予给用户、角色或外部客户端的资源或能力范围，用于约束跨服务访问。 |
| PlatformGateway | 平台网关/BFF | PlatformGateway | 面向前端聚合查询、上下文透传和页面级接口，不沉淀领域规则。 |

## 应用与实例

| 术语 | 中文口径 | 归属 | 说明 |
| --- | --- | --- | --- |
| Application | 应用 | AppHub | 平台识别和管理的应用逻辑实体，例如某个业务服务或工业应用。 |
| ApplicationVersion | 应用版本 | AppHub | Application 的版本事实，由 Agent 注册或后续发布流程观测。 |
| ManagedNode | 受管节点 | AppHub | 运行一个或多个应用实例的主机、容器宿主或其他节点。 |
| ApplicationInstance | 应用实例 | AppHub | 某个 ApplicationVersion 在某个 ManagedNode 上的运行实例，是状态查询与动作目标的主要对象。 |
| InstanceLiveness | 实例存活投影 | AppHub | 由心跳更新的可达性视图，只回答“最近是否可达”，不代表业务运行状态。 |
| reportedStatus | 上报状态 | AppHub | Agent 观察到的实例运行状态，例如 running、stopped、degraded 一类事实。 |
| healthStatus | 健康状态 | AppHub | Agent 或 Connector 汇总出的健康判断，可与 reportedStatus 分开表达。 |
| CapabilityManifest | 能力清单 | AppHub | 某实例声明的可观测、可查询、可执行能力集合。 |
| CapabilityDescriptor | 能力描述 | AppHub、Agent Protocol | 单项能力声明，包含能力编码、版本、分类、支持动作与扩展元数据。 |

## Agent 与 Connector

| 术语 | 中文口径 | 归属 | 说明 |
| --- | --- | --- | --- |
| Agent Host | Agent 宿主 | agents | 在受管环境中运行的后台宿主，负责调度 Connector、上报状态和执行受控动作。 |
| Connector | 连接器 | agents | 适配具体宿主环境的插件式组件，例如 Docker、Windows Service、HTTP 服务。 |
| Docker Connector | Docker 连接器 | agents | 首批优先实现的 Connector，用于发现本地测试容器并上报实例事实。 |
| Agent Protocol | Agent 协议 | backend/common/Contracts | 平台与 Agent 共用的公开契约，首批固定放在 `backend/common/Contracts/Nerv.IIP.Contracts.AgentProtocol`。 |
| nodeKey | 节点键 | Agent Protocol | Agent 侧稳定识别 ManagedNode 的业务键。 |
| instanceKey | 实例键 | Agent Protocol | Agent 与平台共同识别 ApplicationInstance 的稳定业务键。 |

## 运维与审计

| 术语 | 中文口径 | 归属 | 说明 |
| --- | --- | --- | --- |
| OperationTask | 运维任务 | Ops | 一次受控动作请求的任务事实，例如 restart、backup、pull logs。 |
| OperationAttempt | 执行尝试 | Ops | OperationTask 的一次具体执行尝试，用于记录重试、耗时和结果。 |
| OperationResult | 动作结果 | Ops、Agent Protocol | Agent 回传给 Ops 的结构化执行结果。 |
| AuditRecord | 审计记录 | Ops | 对动作请求、执行、结果和确认过程的审计事实。 |
| ApprovalRequest | 审批请求 | Ops | 高风险动作或策略要求下的人机确认挂点，首批仅预留边界。 |
| FailureReason | 失败原因 | Ops、Agent Protocol | 标准化失败分类与可重试信息，首批分类覆盖 validation、timeout、unreachable、permission、runtime。 |
| Low-risk Operation | 低风险动作 | Ops、AI Integration | 可在授权与审计下执行的动作，例如重启、重新拉取状态、触发标准备份。 |
| High-risk Operation | 高风险动作 | Ops、AI Integration | 破坏性、跨环境批量或不可逆数据操作，必须预留审批与人工确认。 |

## 知识与 AI

| 术语 | 中文口径 | 归属 | 说明 |
| --- | --- | --- | --- |
| AI Integration | AI 集成服务 | AI Integration | 管理模型提供方、MCP Server、Skill、工具授权、执行审批与审计。 |
| Knowledge | 知识服务 | Knowledge | 管理知识源、文档抽取、分块、嵌入、索引、检索、权限过滤与引用回显。 |
| KnowledgeSource | 知识源 | Knowledge | 可被引入和检索的文档来源，例如手册、规范、运维记录或对象存储路径。 |
| IngestionJob | 引入任务 | Knowledge | 负责首次导入、增量更新、重建索引或删除同步的异步任务。 |
| Chunk | 分块 | Knowledge | 文档经抽取后用于嵌入和检索的最小文本片段。 |
| Embedding | 嵌入向量 | Knowledge | 由模型生成、写入向量索引并用于相似检索的向量表示。 |
| Citation | 引用 | Knowledge | 检索结果必须携带的来源信息，用于回溯原始文档、位置与权限上下文。 |
| MCP Tool | MCP 工具 | AI Integration | 暴露给 AI 或外部 Agent 客户端的受治理工具。 |
| Skill | 技能 | AI Integration | 可注册、授权和审计的 AI 能力封装，不等同于领域服务本身。 |

## 跟踪与一致性

| 术语 | 中文口径 | 归属 | 说明 |
| --- | --- | --- | --- |
| correlationId | 关联 ID | 全平台 | 串联一次请求、事件、日志和追踪的业务关联标识。 |
| idempotencyKey | 幂等键 | Agent Protocol、Ops | 防止注册或动作结果重复提交造成重复事实或重复任务结果。 |
| Integration Event | 集成事件 | 后端服务 | 跨服务传播状态变化的事件，不用于替代服务内部领域事件。 |
| W3C trace context | 标准追踪上下文 | Observability | 平台采用的链路追踪上下文，不自造第二套追踪协议。 |

## 命名边界

1. `ApplicationInstance` 是 AppHub 的实例事实，不应被 Ops 直接改写。
2. `OperationTask` 是 Ops 的动作事实，不应被 AppHub 当作实例状态保存。
3. `Heartbeat` 与 `StateSnapshot` 是两类契约：前者更新存活投影，后者更新上报状态。
4. `CapabilityDescriptor` 描述可用能力，不代表平台已经授权某用户或 AI 工具调用该能力。
5. `KnowledgeSource` 管理知识来源和生命周期，`AI Integration` 只消费检索或工具能力，不维护私有向量索引。
6. `ExternalClient` 是 IAM 管理的授权对象，不应与 AppHub 管理的 `Application` 混用。
