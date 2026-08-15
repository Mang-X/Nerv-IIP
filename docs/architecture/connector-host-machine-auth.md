# Connector Host 机器身份认证终态

本文档冻结 Connector Host 调用平台 API 的机器身份认证终态和迁移路径。它承接 [ADR 0002](../adr/0002-connector-host-and-app-integration-contract.md) 中“Connector Host 是外部接入客户端”的决策，并补齐 [IAM 认证与授权基线](iam-authentication-baseline.md) 与 [Connector Host 与平台协议 V1](connector-platform-protocol-v1.md) 中刻意留给 IAM 边界的认证细节。

## 终态决策

1. Connector Host 不再直接使用长期机器 secret（密钥）调用 AppHub、Ops 或 PlatformGateway 的业务接口。
2. Connector Host 先用 `ConnectorHostCredential` 向 IAM 完成机器凭证校验，并换发短期 JWT Bearer access token（访问令牌）。
3. AppHub、Ops、PlatformGateway 和后续平台服务统一只接受 `Authorization: Bearer <accessToken>` 作为 Connector Host 的生产环境认证入口。
4. Connector Host 的 principalType 固定为 `connector-host`，不与后台用户和 `external-client` 混用。
5. `ConnectorHostCredential` 是机器身份和初始凭证事实；能力范围（capability scope）是该机器身份可声明、可领取和可回传的能力边界；权限码（permission code）是服务端 API 的授权边界。
6. 旧版 `X-Connector-Host-Id`、`X-Connector-Secret`、`X-Organization-Id` 和 `X-Environment-Id` header-secret（请求头密钥）只保留为迁移兼容机制，不能作为最终生产方案。

## 认证流程

终态链路：

```text
Connector Host
  -> IAM credential token endpoint
     with connectorHostId, credentialId, secret proof, organizationId, environmentId
  <- short-lived access token
Connector Host
  -> AppHub / Ops / PlatformGateway
     Authorization: Bearer <accessToken>
  -> IAM internal authorization check
     principalType + permission code + organization/environment/resource context
```

凭证校验（credential validation）是令牌交换（token exchange）的前置校验，不是业务接口的认证方式。`credentialId` 是 `ConnectorHostCredential` 的稳定标识；若后续支持同一凭证下存在多把轮换密钥，`keyId` 只能作为 secret key 的子标识，用于选择待验证密钥，不能替代 `credentialId` 成为令牌主体或审计主体。IAM 校验 secret hash、凭证有效期、撤销状态、organizationId、environmentId、connectorHostId 和 capability scope 后，才签发 access token。业务服务不得重复实现机器 secret 校验；它们只校验 bearer token，并把所需的 permission/context 交给 IAM 作授权判断。

首批可以继续保留 `POST /api/iam/v1/connectors/credentials/validate` 作为内部校验能力或兼容入口，但面向 Connector Host SDK 的稳定入口应表达为“获取机器 access token（访问令牌）”，而不是“验证后由客户端自行拼接请求头”。

## 令牌生命周期

Connector Host access token 使用短生命周期，建议首批默认 10 分钟，最长不超过 15 分钟。令牌的 `exp` 不得超过 `ConnectorHostCredential.ValidToUtc`；凭证即将过期时，IAM 按两者中较早的时间计算令牌过期时间。

Connector Host 机器身份首批不签发长期 refresh token（刷新令牌）。刷新策略是由 `Sdk.Auth` 在 access token 到期前重新向 IAM token endpoint（令牌端点）提交机器凭证并换取新的 access token。这样可以避免在 Connector Host 本地额外保存可被离线滥用的长期 refresh token，同时保证凭证轮换、撤销和有效期仍由 IAM 的 `ConnectorHostCredential` 控制。

access token 建议携带以下最小声明（claims）：

1. `sub`：Connector Host 主体标识，通常为 connectorHostId。
2. `credentialId`：签发本 token 的 ConnectorHostCredential 标识。
3. `principalType`：固定为 `connector-host`。
4. `organizationId`
5. `environmentId`
6. `permissionVersion` 或等价的凭证授权版本（credential authorization version）。
7. `iat`：标准 JWT issued-at 时间。
8. `jti`：标准 JWT 令牌 ID（token id），用于审计、诊断和撤销关联。

`X-Correlation-Id` 请求头与 W3C trace context（追踪上下文）由请求链路透传，不写入 access token。access token 不承载单次请求的 correlation/trace 字段，避免把一次调用的上下文固化到可复用令牌中。

令牌可以携带 capability scope 或 permission 摘要用于诊断和 SDK 上下文，但服务端执行类接口不能只信任令牌中的静态 scope。最终授权仍由 IAM 基于凭证、授权授予、permission version、组织环境和资源上下文判断。

## 撤销与传播

撤销入口至少覆盖以下事实：

1. ConnectorHostCredential 被禁用、删除、过期或其 secret 发生轮换。
2. capability scope 被收窄。
3. 映射到 Connector Host 的 permission grant（权限授予）被撤销或 permission version 提升。
4. Connector Host 所属 organization/environment 范围被调整。

撤销传播采用“主动使缓存失效 + 短令牌 TTL”组合：

1. IAM 修改 ConnectorHostCredential、AuthorizationGrant 或权限快照时，必须提升对应主体的 permission version 或 credential authorization version。
2. IAM 必须主动使该 Connector Host 的相关授权缓存失效；使用分布式缓存时，要通过缓存键版本或事件驱动，将失效传播到 AppHub、Ops 和 Gateway 的 authorization check（授权检查）调用路径。
3. 业务服务每次执行受保护接口时，都通过 IAM internal authorization check 校验令牌对应的当前授权事实；不得只在本地依据 JWT 过期时间放行执行类接口。
4. 在缓存失效传播延迟内，短期 access token 的剩余 TTL 是最大风险窗口；首批默认按 10 分钟控制，生产客户可下调到 5 分钟。
5. Connector Host 收到 401 或 403 后，`Sdk.Auth` 应立即丢弃本地 access token 并重新获取令牌；重新获取仍失败时停止继续调用业务接口，并输出可诊断的认证错误。

## Bearer Token 的统一范围

AppHub、Ops 和 PlatformGateway 的 Connector Host 入口统一使用 bearer token：

| 服务 | 接口范围 | 终态权限 |
| --- | --- | --- |
| AppHub | 注册（registrations） | `connectors.registrations.write` |
| AppHub | 心跳（heartbeats） | `connectors.heartbeats.write` |
| AppHub | 状态快照（state-snapshots） | `connectors.state-snapshots.write` |
| Ops | 待处理动作任务（pending operation tasks） | `ops.tasks.read` 加 operation capability scope |
| Ops | 动作结果（operation results） | `ops.results.write` 加 operation capability scope |

Gateway 可以作为外部统一入口转发 bearer token，但不能把 Connector Host 重新降级为使用 Gateway 自身的共享 secret。无论 AppHub/Ops 直连还是经 Gateway 转发，IAM 看到的 principalType、organizationId、environmentId、connectorHostId 和 permission code 都必须一致。

## Capability Scope 与 Permission Code 的映射

Capability scope 和 permission code 不是同一种事实：

1. Capability scope（能力范围）描述某个 Connector Host 被允许声明、领取或执行的本地能力，例如 `runtime.status`、`lifecycle.restart`、`log.read`、`backup.create`、`restore.execute`。
2. Permission code（权限码）描述某个 API 动作是否允许访问，例如 `connectors.state-snapshots.write` 或 `ops.results.write`。
3. Connector Protocol 中上报的 `CapabilityDescriptor` 是 AppHub 的实例能力事实，不自动授予 IAM 权限。
4. IAM 的 ConnectorHostCredential capability scope 是机器身份授权边界，用于约束该 Connector Host 能用哪些 capabilityCode/operationCode 调用平台接口。

首批映射规则：

| 场景 | 所需 permission code | 所需 capability scope |
| --- | --- | --- |
| 注册或更新实例能力清单 | `connectors.registrations.write` | 被上报的 capabilityCode 必须包含在凭证 scope 内。 |
| 上报心跳 | `connectors.heartbeats.write` | 不要求具体 operation capability，但必须在同一 organization/environment/connectorHostId 范围内。 |
| 上报状态快照 | `connectors.state-snapshots.write` | 状态来源对应的 runtime capability 必须包含在凭证 scope 内；首批可用 `runtime.status` 表达。 |
| 拉取待处理任务（pending task） | `ops.tasks.read` | 返回任务必须过滤到该凭证 scope 允许的 operationCode，例如 `lifecycle.restart`。 |
| 回传动作结果 | `ops.results.write` | result.operationCode 必须包含在凭证 scope 内，并且 task 的 connectorHostId、organizationId、environmentId 与 token 一致。 |

新增 operationCode 时必须同时评估两件事：是否需要新增 permission code，以及该 operationCode 是否进入 ConnectorHostCredential capability scope。单纯新增本地 Connector 能力不等于开放平台 API 权限。

## 旧版 Header-Secret 迁移

第二阶段低风险动作闭环中的 `X-Connector-Host-Id`、`X-Connector-Secret`、`X-Organization-Id` 和 `X-Environment-Id` 是本地纵切验证机制，迁移策略如下：

当前 AppHub 的接入加固（ingestion hardening）已先把注册后的心跳与状态同步，从共享 header-secret 切换到注册返回的逐实例 `X-Connector-Ingestion-Token`。注册阶段必须校验 header-secret 携带的 organizationId、environmentId、connectorHostId 与注册请求体一致；当 AppHub 配置了 `ConnectorHostCredential:*` 范围时，还必须同时与服务端配置绑定，避免只依靠请求体自证租户/环境。该令牌由 AppHub 签发，并绑定 registrationId、organizationId、environmentId、connectorHostId、instanceKey、issuedAtUtc 和 expiresAtUtc，用于阻断跨实例或跨租户伪造上报；它不是终态 IAM access token，也不替代下述 bearer token 迁移目标。非 Development 环境必须配置 `ConnectorIngestionToken:SigningKey`，不得依赖本地回退值；`ConnectorIngestionToken:LifetimeMinutes` 默认 10 分钟，过期后 Connector Host 通过重新注册刷新本实例令牌。

1. 当前阶段：保留 header-secret 入口供本地验证脚本和旧 Connector Host 使用，新增 bearer token 链路和 SDK token provider（令牌提供程序）。
2. 兼容阶段：AppHub/Ops 同时接受 bearer token 与 header-secret，但生产 profile 默认要求 bearer token；命中 header-secret 时必须输出结构化警告（structured warning），并在响应中返回弃用信号（deprecation signal）。
3. 下线阶段：当 Connector Host SDK、验证脚本、AppHost/Compose 配置和 Ops/AppHub 测试全部切换到 bearer token 后，在下一个次版本（minor release）移除生产 profile 的 header-secret 支持，只允许显式 `Development` profile 或一次性迁移工具使用。
4. 主版本边界：同一主版本内不得长期保留生产 header-secret。若已发布给外部客户，最多保留一个次版本的迁移窗口；下一个主版本必须完全删除业务接口的 header-secret 认证。

header-secret 下线不影响 IAM 内部保存 secret hash。secret 仍作为换发短期 access token 的机器凭证，只是不再直接出现在 AppHub/Ops 业务请求中。

## 与 ExternalClient Principal 的关系

`connector-host` 和 `external-client` 都是非用户主体，但边界不同：

| 项目 | connector-host | external-client |
| --- | --- | --- |
| 身份来源 | ConnectorHostCredential | ExternalClient |
| 主要用途 | 受管环境接入、实例事实上报、pending task 领取、动作结果回传 | 第三方系统、平台应用或行业扩展访问公开 API |
| capability scope | 必须存在，用于约束 Connector 能力和 operationCode | 可选；通常使用 OAuth/OIDC scope 或 AuthorizationGrant 表达 API 范围 |
| permission code | 由 IAM grant（授权）映射到 connectors/ops 等权限 | 由 IAM grant 映射到被开放的业务权限 |
| 用户关系 | 不代表后台用户，不继承用户角色 | 不代表后台用户；若未来支持 delegated access（委托访问），必须显式记录授权用户和 consent（同意记录） |
| 审计主体 | connectorHostId + credentialId | externalClientId + grantId |

Connector Host 不能注册成 ExternalClient 来绕过 capability scope；ExternalClient 也不能通过伪造 connectorHostId 来领取 Ops 待处理任务（pending task）。两类 principal（主体）可以复用 IAM 的 JWT、permission code、AuthorizationGrant 和 internal authorization check 机制，但领域事实、凭证生命周期、审计字段和授权语义必须分开。

## SDK 与服务边界

`Nerv.IIP.Sdk.Auth` 负责：

1. 调用 IAM token endpoint（令牌端点）。
2. 缓存短期 access token。
3. 在请求中注入 `Authorization: Bearer`。
4. 在收到 401/403 时清理令牌、重新认证，或向调用方返回统一认证错误。

`Sdk.Auth` 不保存 IAM 授权事实，不在客户端作最终授权判断，也不把 capability scope 解释成服务端 permission code。AppHub、Ops 和 PlatformGateway 的 endpoint 仍按各自业务接口声明 permission/context，并由 IAM 完成最终授权。

## 非目标

1. 不在本文档中引入完整 OAuth2/OIDC 授权服务器、consent 页面或第三方应用市场。
2. 不在本文档中要求 Connector Host 首批使用 mTLS、DPoP、token binding 或硬件密钥。
3. 不在本文档中定义所有 capabilityCode/operationCode 的完整字典；协议字典继续由 Connector Protocol 和 Ops 契约演进。
4. 不在本文档中改变 AppHub 是实例事实来源、Ops 是动作事实来源的边界。
