# 统一授权矩阵

本文档汇总 Nerv-IIP 平台权限码、调用主体类型和授权范围维度，作为 IAM 授权事实与各服务权限命名的统一入口。

上下文来源：

- [IAM 认证与授权基线](iam-authentication-baseline.md)
- [平台上下文地图](context-map.md)
- [Connector Host 机器身份认证终态](connector-host-machine-auth.md)

后续 Notification、Knowledge、AI Integration 与 Observability baseline 文档创建后，应反向链接本文档，并在对应服务实现时标明权限 seed 与 enforcement 状态。

## 命名规则

权限码采用 `{domain}.{resource}.{action}` 风格，全部小写，资源名默认使用复数。既有权限码在同一主版本内不得改变语义；如果必须改变权限语义或收窄/放宽授权边界，必须提升主版本并提供迁移说明。

授权判断由四类输入共同决定：

1. `principalType`：调用主体类型。
2. permission code：稳定权限码。
3. organization/environment scope：组织与环境边界。
4. resource/capability scope：具体资源或能力边界。

Endpoint 只声明需要的权限码与上下文；业务不变式仍由 Domain guard 或应用服务校验。

## principalType 维度

| principalType | 说明 | 典型入口 | 授权事实来源 |
| --- | --- | --- | --- |
| `user` | 平台控制台用户或运维管理员。 | Console/Gateway、IAM 管理接口、后续管理后台。 | IAM User、Role、Membership、UserSession。 |
| `connector-host` | Connector Host 机器身份。 | AppHub 注册/心跳/状态同步，Ops 任务领取与结果回传。 | IAM ConnectorHostCredential、organization/environment、capability scope。 |
| `external-client` | 外部系统、平台应用或受控第三方客户端。 | Platform SDK、公开 API、Webhook 回调、后续 OAuth/OIDC client credential。 | IAM ExternalClient、AuthorizationGrant、organization/environment、resource/capability scope。 |
| `internal-service` | 平台内部服务到服务调用主体。 | Gateway 到 IAM 授权检查，服务间后台任务或事件处理后的回查。 | 平台部署身份、服务账号或后续 workload identity；不得绕过 IAM 边界事实。 |

当前 access token 基线已覆盖 `user`、`external-client`、`connector-host` 的 token claim 形态；`internal-service` 是服务间授权命名维度，落地前不得作为匿名信任通道使用。

## resource scope 维度

| scope | 含义 | 适用示例 | 校验要求 |
| --- | --- | --- | --- |
| organization | 组织级授权边界。 | IAM 用户/角色管理、组织级通知偏好、组织级模型配置。 | 请求必须携带或解析 organizationId，授权事实必须匹配。 |
| environment | 环境级授权边界。 | 开发/生产环境实例查询、运维任务、日志查询、知识源检索。 | 请求必须携带或解析 environmentId，不能跨环境隐式复用授权。 |
| resource | 单个或一组资源边界。 | application instance、operation task、file、knowledge source、notification template。 | 权限码只表达动作类别；资源所有权、可见性和状态需单独校验。 |
| capability | 能力级边界。 | Connector Host 上报能力、AI 工具执行、外部客户端可调用能力。 | 必须和 permission code 同时匹配，不能只靠能力声明授权执行。 |

## 主体与范围矩阵

| principalType | organization | environment | resource | capability | 说明 |
| --- | --- | --- | --- | --- | --- |
| `user` | 必须 | 常规必须 | 按入口需要 | 按入口需要 | 控制台和管理后台的默认主体；角色权限只在组织/环境 membership 内生效。 |
| `connector-host` | 必须 | 必须 | 常规必须 | 必须 | 只能访问自身被授予的实例、任务和上报能力；不能获得 IAM 管理权限。 |
| `external-client` | 必须 | 常规必须 | 常规必须 | 常规必须 | 面向平台应用或第三方系统；授权必须有有效期和可撤销授予。 |
| `internal-service` | 必须 | 按调用需要 | 按调用需要 | 按调用需要 | 仅用于平台服务间调用；调用方身份不替代最终用户或机器主体授权。 |

## 已实现服务权限码表

下表来自当前 IAM seed 权限集和 Gateway 已接入的 permission enforcement。状态为“已 seed”表示 IAM 初始角色权限会包含该权限码；状态为“Gateway 已 enforcement”表示现有 Console API 已实际声明并转发到 IAM 授权检查。

| 权限码 | 服务域 | 建议 principalType | 建议 scope | 当前状态 | 说明 |
| --- | --- | --- | --- | --- | --- |
| `iam.users.read` | IAM | `user` | organization | 已 seed；IAM 管理端点已检查 | 查看用户。 |
| `iam.users.manage` | IAM | `user` | organization | 已 seed；IAM 管理端点已检查 | 创建、禁用、重置用户；当前写入口授权后仍可能返回未实现。 |
| `iam.roles.read` | IAM | `user` | organization | 已 seed；IAM 管理端点已检查 | 查看角色与权限。 |
| `iam.roles.manage` | IAM | `user` | organization | 已 seed；IAM 管理端点已检查 | 创建角色、调整角色权限；当前写入口授权后仍可能返回未实现。 |
| `iam.sessions.read` | IAM | `user` | organization | 已 seed；IAM 管理端点已检查 | 查看会话。 |
| `iam.sessions.revoke` | IAM | `user` | organization | 已 seed；IAM 管理端点已检查 | 撤销会话。 |
| `connectors.registrations.write` | Connectors/AppHub | `connector-host` | environment + capability | 已 seed | Connector Host 注册或更新应用实例事实。 |
| `connectors.heartbeats.write` | Connectors/AppHub | `connector-host` | environment + resource + capability | 已 seed | Connector Host 上报心跳。 |
| `connectors.state-snapshots.write` | Connectors/AppHub | `connector-host` | environment + resource + capability | 已 seed | Connector Host 上报实例状态快照。 |
| `apphub.instances.read` | AppHub | `user` / `external-client` / `internal-service` | environment + resource | 已 seed；Gateway 已 enforcement | 查看应用实例列表与详情。 |
| `files.upload` | File Storage | `user` / `connector-host` / `external-client` | environment + resource | 已 seed | 创建上传会话并完成文件上传。 |
| `files.read` | File Storage | `user` / `connector-host` / `external-client` / `internal-service` | environment + resource | 已 seed | 查看文件元数据。 |
| `files.download-grants.create` | File Storage | `user` / `external-client` / `internal-service` | environment + resource | 已 seed | 创建短期下载授权。 |
| `files.archive` | File Storage | `user` / `internal-service` | environment + resource | 已 seed | 归档文件。 |
| `ops.tasks.create` | Ops | `user` / `external-client` / `internal-service` | environment + resource + capability | 已 seed；Gateway 已 enforcement | 创建运维任务。 |
| `ops.tasks.read` | Ops | `user` / `connector-host` / `external-client` / `internal-service` | environment + resource | 已 seed；Gateway 已 enforcement | 查看运维任务。 |
| `ops.results.write` | Ops | `connector-host` | environment + resource + capability | 已 seed | Connector Host 回传动作结果。 |
| `ops.audit.read` | Ops | `user` / `internal-service` | organization / environment + resource | 已 seed | 查看审计记录。 |

## 待落地服务权限命名

以下权限码用于冻结后续服务的命名口径。它们尚未进入当前 `NervIipSeedPermissions.All`，后续实现对应服务时必须通过迁移、seed 和端点授权检查一起落地。

### Notification

| 权限码 | 建议 principalType | 建议 scope | 说明 |
| --- | --- | --- | --- |
| `notifications.messages.read` | `user` / `external-client` | organization / environment + resource | 查询站内通知、待办、已读未读和资源相关消息。 |
| `notifications.subscriptions.manage` | `user` / `external-client` | organization / environment + resource | 管理事件订阅、通知偏好和订阅过滤条件。 |
| `notifications.templates.manage` | `user` / `internal-service` | organization + resource | 管理通知模板、模板版本和多语言文本。 |
| `notifications.deliveries.manage` | `user` / `internal-service` | organization / environment + resource | 管理投递尝试、重试、失败诊断和外部通道投递状态。 |

### Knowledge

| 权限码 | 建议 principalType | 建议 scope | 说明 |
| --- | --- | --- | --- |
| `knowledge.retrievals.query` | `user` / `external-client` / `internal-service` | environment + resource | 执行知识检索并返回带引用的片段；必须经过权限过滤。 |
| `knowledge.sources.manage` | `user` / `external-client` | environment + resource | 创建、配置、暂停、归档知识源和同步策略。 |
| `knowledge.indexes.rebuild` | `user` / `internal-service` | environment + resource | 触发索引重建、权限同步后重建或策略变更后的重建。 |

### AI Integration

| 权限码 | 建议 principalType | 建议 scope | 说明 |
| --- | --- | --- | --- |
| `ai.models.configure` | `user` / `internal-service` | organization / environment | 管理模型提供方配置、模型参数和可用范围。 |
| `ai.tools.register` | `user` / `external-client` / `internal-service` | organization / environment + capability | 注册 MCP Server、Skill 或平台工具。 |
| `ai.tools.execute` | `user` / `external-client` / `internal-service` | environment + resource + capability | 执行已授权工具；执行类工具仍需 Ops 或目标服务的动作授权。 |
| `ai.approvals.manage` | `user` / `internal-service` | environment + resource | 管理工具执行审批、人机确认和高风险动作批准。 |
| `ai.prompts.manage` | `user` / `internal-service` | organization / environment + resource | 管理 prompt 模板、版本、启停和适用范围。 |

### Observability

| 权限码 | 建议 principalType | 建议 scope | 说明 |
| --- | --- | --- | --- |
| `observability.logs.query` | `user` / `external-client` / `internal-service` | environment + resource | 查询日志 chunk、日志条目索引和关联上下文；Gateway 不暴露底层查询语言。 |
| `observability.diagnostics.read` | `user` / `internal-service` | environment + resource | 查看诊断包、日志包和归档元数据。 |
| `observability.retention.manage` | `user` / `internal-service` | organization / environment | 管理日志 retention、清理任务和归档策略。 |

## 落地要求

1. 新增权限码必须先更新本文档，再进入 IAM seed、迁移、端点授权检查和测试。
2. 对外 API、Gateway facade、SDK 和服务间调用不得各自发明未登记权限码。
3. `connector-host` 和 `external-client` 必须同时校验 organization、environment、resource 或 capability scope；不得只校验权限码。
4. `internal-service` 只能表达服务到服务调用身份，不得作为绕过最终用户、外部客户端或 Connector Host 授权的后门。
5. Notification、Knowledge、AI Integration 和 Observability 首次实现时，应在对应服务文档中链接本文档，并标明实际 seed 状态。
