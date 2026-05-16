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
| Control Plane | 平台控制面 | 平台整体 | 管理身份、权限、组织、对外授权、应用目录、实例事实、运维动作、审计、通知与 AI 治理的后台能力集合。 |
| IAM | 身份权限服务 | IAM | 统一管理用户、组织、角色、权限、外部客户端与授权事实，是平台内部鉴权和对外授权的事实源。 |
| Application Onboarding Plane | 应用接入面 | Connector Host | 通过 Connector Host 与 Connector 发现、观测和控制受管目标的接入能力集合。 |
| Knowledge and AI Plane | 知识与 AI 面 | Knowledge、AI Integration | 管理知识引入、检索、模型接入、工具治理与人机确认的能力集合。 |
| Platform SDK | 平台 SDK | 主平台 | 主平台提供给应用、Connector Host、行业扩展和前端包的模块化客户端能力集合，包括公开 DTO、生成客户端、认证上下文、文件存储、运维调用、通知意图、观测上下文、错误模型、事件契约和 Connector Protocol。 |
| Platform SDK Compatibility Version | 平台 SDK 兼容版本 | 主平台 | 应用、Connector Host 或扩展声明其兼容的主平台 SDK 版本；主版本必须与主平台对齐，小版本允许低于主平台小版本。 |
| File Storage | 文件存储服务 | File Storage | 主平台通用文件能力，管理文件元数据、上传下载授权、对象存储定位、保留策略和归档状态。 |
| Notification | 通知服务 | Notification | 主平台通用通知能力，管理通知意图、站内通知、待办、接收人解析、偏好、去重、已读未读和投递状态。 |
| Organization | 组织 | IAM | 权限、环境、用户与资源隔离的上层边界。 |
| Environment | 环境 | IAM | dev、test、prod、customer-hosted 等运行边界，所有跨环境操作必须显式携带。 |
| ExternalClient | 外部客户端 | IAM | 需要被主平台授权以访问平台 API、MCP 工具或受管应用能力的外部应用或系统，不等同于 AppHub 的 Application。 |
| AuthorizationGrant | 授权授予 | IAM | 记录某个用户、角色、组织或服务主体对外部客户端、资源范围或能力范围的授权关系。 |
| PermissionScope | 权限范围 | IAM | 可被授予给用户、角色或外部客户端的资源或能力范围，用于约束跨服务访问。 |
| PlatformGateway | 平台网关/BFF | PlatformGateway | 面向前端聚合查询、上下文透传和页面级接口，不沉淀领域规则。 |
| Aspire AppHost | Aspire 编排宿主 | infra | 平台级分布式拓扑模型入口，用于本地联调、Dashboard、服务发现和生成部署产物；不属于任何单个领域服务。 |
| Docker Compose Deployment | Compose 部署 | infra | 面向 PoC、小规模私有化和容器化单机部署的目标形态，完整平台 Compose 优先从 Aspire AppHost 生成。 |
| Install Package | 安装包 | 发布制品 | 面向无容器或传统运维环境的发布制品，Windows 默认注册为 Windows Service，Linux 默认注册为 systemd service。 |
| Integrated Install Script | 整合安装脚本 | scripts | 面向实施交付的 PowerShell 或 Bash 入口，负责检查、配置、初始化、服务注册、启动和诊断。 |

## Platform SDK

| 术语 | 中文口径 | 归属 | 说明 |
| --- | --- | --- | --- |
| Sdk.Core | SDK 核心模块 | Platform SDK | 提供 transport、错误模型、correlationId、idempotencyKey、组织环境上下文和版本上报。 |
| Sdk.Auth | SDK 认证模块 | Platform SDK | 处理 token、client credential 和认证头，不做最终授权判断，不保存 IAM 事实。 |
| Sdk.ConnectorProtocol | SDK 接入协议模块 | Platform SDK | 提供注册、心跳、状态快照和动作结果回传客户端，不拥有 AppHub 事实。 |
| Sdk.FileStorage | SDK 文件存储模块 | Platform SDK | 提供上传会话、上传指令、完成上传、取消上传和下载授权客户端，不解释文件业务语义。 |
| Sdk.Ops | SDK 运维模块 | Platform SDK | 提供运维任务、任务查询、动作结果和审计意图提交客户端，不直接写最终 AuditRecord。 |
| Sdk.Notification | SDK 通知模块 | Platform SDK | 提供通知意图提交、通知查询和已读状态客户端，不直接调用外部通道 provider。 |
| Sdk.Observability | SDK 观测上下文模块 | Platform SDK | 提供 trace context、correlationId 和标准日志字段辅助，不替代日志采集或审计落库。 |
| AuditIntent | 审计意图 | Ops、Platform SDK | 客户端提交的审计上下文或动作说明，必须由 Ops 服务端校验后才能形成正式 AuditRecord。 |

## 应用与实例

| 术语 | 中文口径 | 归属 | 说明 |
| --- | --- | --- | --- |
| Application | 应用 | AppHub | 平台识别和管理的应用逻辑实体，例如业务服务、后台任务、Web/API 服务或第三方系统。 |
| ApplicationVersion | 应用版本 | AppHub | Application 的业务版本、镜像版本或发布版本事实，由 Connector Host 注册或后续发布流程观测；不等同于 Platform SDK 兼容版本。 |
| ManagedNode | 受管节点 | AppHub | 运行一个或多个应用实例的主机、容器宿主或其他节点。 |
| ApplicationInstance | 应用实例 | AppHub | 某个 ApplicationVersion 在某个 ManagedNode 上的运行实例，是状态查询与动作目标的主要对象。 |
| InstanceLiveness | 实例存活投影 | AppHub | 由心跳更新的可达性视图，只回答“最近是否可达”，不代表业务运行状态。 |
| reportedStatus | 上报状态 | AppHub | Connector Host 观察到的实例运行状态，例如 running、stopped、degraded 一类事实。 |
| healthStatus | 健康状态 | AppHub | Connector Host 或 Connector 汇总出的健康判断，可与 reportedStatus 分开表达。 |
| CapabilityManifest | 能力清单 | AppHub | 某实例声明的可观测、可查询、可执行能力集合。 |
| CapabilityDescriptor | 能力描述 | AppHub、Connector Protocol | 单项能力声明，包含能力编码、版本、分类、支持动作与扩展元数据。 |

## Connector Host 与 Connector

| 术语 | 中文口径 | 归属 | 说明 |
| --- | --- | --- | --- |
| Connector Host | 接入宿主 | connector-hosts | 在受管环境中运行的后台宿主，负责调度 Connector、上报状态和执行受控动作。 |
| Connector | 连接器 | connector-hosts | 适配具体宿主环境的插件式组件，例如 Docker、Windows Service、HTTP 服务。 |
| Docker Connector | Docker 连接器 | connector-hosts | 首批优先实现的 Connector，用于发现本地测试容器并上报实例事实。 |
| Connector Protocol | Connector Host 协议 | backend/common/Contracts | 平台与 Connector Host 共用的公开契约，首批固定放在 `backend/common/Contracts/Nerv.IIP.Contracts.ConnectorProtocol`。 |
| nodeKey | 节点键 | Connector Protocol | Connector Host 侧稳定识别 ManagedNode 的业务键。 |
| instanceKey | 实例键 | Connector Protocol | Connector Host 与平台共同识别 ApplicationInstance 的稳定业务键。 |

## 文件存储

| 术语 | 中文口径 | 归属 | 说明 |
| --- | --- | --- | --- |
| StoredFile | 已存储文件 | File Storage | 主平台记录的文件元数据事实，包含组织、环境、文件名、内容类型、大小、校验和、状态和保留策略。 |
| FileVersion | 文件版本 | File Storage | 某个 StoredFile 的具体对象内容版本，内部保存 provider、bucket、objectKey、checksum 和 size。 |
| UploadSession | 上传会话 | File Storage | 一次受控上传过程，约束上传主体、用途、大小、content type、有效期、幂等键、uploadMode 和 provider。 |
| UploadInstructions | 上传指令 | File Storage | File Storage 为客户端生成的短期上传说明，例如 tus endpoint、S3 multipart presigned urls 或平台中转地址。 |
| UploadProvider | 上传实现策略 | File Storage | 屏蔽 tus、S3 multipart、server-proxy 等协议差异的基础设施扩展点，不拥有文件领域事实。 |
| tus | tus 断点续传协议 | File Storage | 可选上传 provider，适合大文件和断点续传场景；领域模型仍以 UploadSession 与 StoredFile 为准。 |
| S3MultipartUploadProvider | S3 分片上传策略 | File Storage | 对接 MinIO/S3 multipart upload 的 provider，负责生成短期分片上传指令和完成校验。 |
| FilePurposePolicy | 文件用途策略 | File Storage | 按文件用途定义大小限制、content type allowlist、扩展名、扫描要求、保留策略和配额口径。 |
| scanStatus | 扫描状态 | File Storage | 文件安全扫描状态；未扫描、扫描失败或隔离中的文件不能进入普通下载和 Knowledge ingestion。 |
| DownloadGrant | 下载授权 | File Storage | 一次短期下载许可，可映射为平台中转下载或对象存储预签名 URL。 |
| FileReference | 文件引用 | File Storage、业务服务 | 业务对象与 fileId 的绑定关系；File Storage 记录引用边界，业务服务解释业务语义。 |
| objectKey | 对象存储键 | File Storage | MinIO 或等价对象存储内部定位信息，不应作为前端、外部应用或业务服务的长期公开契约。 |

## 运维与审计

| 术语 | 中文口径 | 归属 | 说明 |
| --- | --- | --- | --- |
| OperationTask | 运维任务 | Ops | 一次受控动作请求的任务事实，例如 restart、backup、pull logs。 |
| OperationAttempt | 执行尝试 | Ops | OperationTask 的一次具体执行尝试，用于记录重试、耗时和结果。 |
| OperationResult | 动作结果 | Ops、Connector Protocol | Connector Host 回传给 Ops 的结构化执行结果。 |
| AuditRecord | 审计记录 | Ops | 对动作请求、执行、结果和确认过程的审计事实。 |
| ApprovalRequest | 审批请求 | Ops | 高风险动作或策略要求下的人机确认挂点，首批仅预留边界。 |
| FailureReason | 失败原因 | Ops、Connector Protocol | 标准化失败分类与可重试信息，首批分类覆盖 validation、timeout、unreachable、permission、runtime。 |
| Low-risk Operation | 低风险动作 | Ops、AI Integration | 可在授权与审计下执行的动作，例如重启、重新拉取状态、触发标准备份。 |
| High-risk Operation | 高风险动作 | Ops、AI Integration | 破坏性、跨环境批量或不可逆数据操作，必须预留审批与人工确认。 |

## 通知与待办

| 术语 | 中文口径 | 归属 | 说明 |
| --- | --- | --- | --- |
| NotificationIntent | 通知意图 | Notification | 某个服务希望把平台事实、任务结果或待处理事项通知给人的结构化意图，包含来源事件、资源引用、严重性、建议接收范围和去重键。 |
| NotificationMessage | 通知消息 | Notification | 面向单个用户或主体的用户可见消息，承载标题、摘要、资源跳转、已读未读和归档状态。 |
| NotificationSubscription | 通知订阅 | Notification | 用户、角色、组织或外部客户端对某类平台事件的订阅关系。 |
| NotificationPreference | 通知偏好 | Notification | 用户、角色或组织对站内、邮件、企业 IM、Webhook 等通道的偏好和静默设置。 |
| DeliveryAttempt | 投递尝试 | Notification | 对某个通道的一次投递记录，包含 provider、状态、失败原因、重试次数和最后错误摘要。 |
| In-app Notification | 站内通知 | Notification | 平台控制台内可见的通知，是首批默认通道和外部投递失败后的兜底视图。 |
| Todo Notification | 待办通知 | Notification、Ops、AI Integration | 需要用户动作的通知，例如审批、确认、失败处理或补充信息；动作事实仍归发起服务。 |
| Notification Provider | 通知通道提供方 | Notification Infrastructure | 邮件、短信、企业微信、钉钉、Webhook 等外部通道适配器，不属于其它领域服务依赖。 |

## 知识与 AI

| 术语 | 中文口径 | 归属 | 说明 |
| --- | --- | --- | --- |
| AI Integration | AI 集成服务 | AI Integration | 管理模型提供方、MCP Server、Skill、工具授权、执行审批与审计。 |
| Knowledge | 知识服务 | Knowledge | 管理知识源、文档抽取、分块、嵌入、索引、检索、权限过滤与引用回显。 |
| KnowledgeSource | 知识源 | Knowledge | 可被引入和检索的文档来源，例如手册、规范、运维记录、File Storage 文件集合或受控外部来源。 |
| IngestionJob | 引入任务 | Knowledge | 负责首次导入、增量更新、重建索引或删除同步的异步任务。 |
| Chunk | 分块 | Knowledge | 文档经抽取后用于嵌入和检索的最小文本片段。 |
| Embedding | 嵌入向量 | Knowledge | 由模型生成、写入向量索引并用于相似检索的向量表示。 |
| Citation | 引用 | Knowledge | 检索结果必须携带的来源信息，用于回溯原始文档、位置与权限上下文。 |
| MCP Tool | MCP 工具 | AI Integration | 暴露给 AI 客户端或外部应用的受治理工具。 |
| Skill | 技能 | AI Integration | 可注册、授权和审计的 AI 能力封装，不等同于领域服务本身。 |

## 跟踪与一致性

| 术语 | 中文口径 | 归属 | 说明 |
| --- | --- | --- | --- |
| correlationId | 关联 ID | 全平台 | 串联一次请求、事件、日志和追踪的业务关联标识。 |
| idempotencyKey | 幂等键 | Connector Protocol、Ops | 防止注册或动作结果重复提交造成重复事实或重复任务结果。 |
| Integration Event | 集成事件 | 后端服务 | 跨服务传播状态变化的事件，不用于替代服务内部领域事件。 |
| W3C trace context | 标准追踪上下文 | Observability | 平台采用的链路追踪上下文，不自造第二套追踪协议。 |

## 命名边界

1. `ApplicationInstance` 是 AppHub 的实例事实，不应被 Ops 直接改写。
2. `OperationTask` 是 Ops 的动作事实，不应被 AppHub 当作实例状态保存。
3. `Heartbeat` 与 `StateSnapshot` 是两类契约：前者更新存活投影，后者更新上报状态。
4. `CapabilityDescriptor` 描述可用能力，不代表平台已经授权某用户或 AI 工具调用该能力。
5. `KnowledgeSource` 管理知识来源和生命周期，`AI Integration` 只消费检索或工具能力，不维护私有向量索引。
6. `StoredFile` 管理文件存储事实，不解释文件在 Knowledge、Ops、AppHub 或行业扩展中的业务含义。
7. `Platform SDK` 是客户端能力集合，不是服务发现中心、权限事实源、审计事实源、通知事实源或领域模型副本。
8. `ExternalClient` 是 IAM 管理的授权对象，不应与 AppHub 管理的 `Application` 混用。
9. `NotificationMessage` 是用户可见通知事实，不应替代 `AuditRecord`、`OperationTask`、`ApplicationInstance` 或 `KnowledgeSource`。
10. 工厂、产线、设备、站点等行业概念不属于主平台核心术语；需要时由领域扩展定义，不能反向污染 IAM、FileStorage、AppHub、Ops、Notification 的通用模型。
