# Connector Host 机器身份认证终态

本文档冻结 Connector Host 调用平台 API 的机器身份认证终态和迁移路径。它承接 [ADR 0002](../adr/0002-connector-host-and-app-integration-contract.md) 中“Connector Host 是外部接入客户端”的决策，并补齐 [IAM 认证与授权基线](iam-authentication-baseline.md) 与 [Connector Host 与平台协议 V1](connector-platform-protocol-v1.md) 中刻意留给 IAM 边界的认证细节。

## 终态决策

1. Connector Host 不再直接拿长期机器 secret 调用 AppHub、Ops 或 PlatformGateway 的业务接口。
2. Connector Host 先用 `ConnectorHostCredential` 向 IAM 完成机器凭证校验，并换发短期 JWT Bearer access token。
3. AppHub、Ops、PlatformGateway 和后续平台服务统一只接受 `Authorization: Bearer <accessToken>` 作为 Connector Host 的生产认证入口。
4. Connector Host 的 principalType 固定为 `connector-host`，不与后台用户和 `external-client` 混用。
5. `ConnectorHostCredential` 是机器身份和初始凭证事实；Capability scope 是该机器身份可声明、可领取和可回传的能力边界；Permission code 是服务端 API 授权边界。
6. 旧版 `X-Connector-Host-Id`、`X-Connector-Secret`、`X-Organization-Id` 和 `X-Environment-Id` header-secret 只保留为迁移兼容机制，不能作为最终生产方案。

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

Credential validation 是 token exchange 的前置校验，不是业务接口认证方式。`credentialId` 是 `ConnectorHostCredential` 的稳定标识；若后续支持同一 credential 下多把轮换密钥，`keyId` 只能作为 secret key 的子标识，用于选择待验证密钥，不能替代 `credentialId` 成为 token 主体或审计主体。IAM 校验 secret hash、凭证有效期、撤销状态、organizationId、environmentId、connectorHostId 和 capability scope 后，才签发 access token。业务服务不得重复实现机器 secret 校验；它们只校验 bearer token，并把所需 permission/context 交给 IAM 授权判断。

首批可以继续保留 `POST /api/iam/v1/connectors/credentials/validate` 作为内部校验能力或兼容入口，但面向 Connector Host SDK 的稳定入口应表达为“获取机器 access token”，而不是“验证后由客户端自己拼 header”。

## Token 生命周期

Connector Host access token 使用短生命周期，建议首批默认 10 分钟，最长不超过 15 分钟。token 的 `exp` 不得超过 `ConnectorHostCredential.ValidToUtc`；凭证即将过期时，IAM 按两者更早者计算 token 过期时间。

Connector Host 机器身份首批不签发长期 refresh token。刷新策略是由 `Sdk.Auth` 在 access token 到期前重新向 IAM token endpoint 提交机器凭证并换取新 access token。这样可以避免在 Connector Host 本地额外保存一个可离线滥用的长期 refresh token，同时保持凭证轮换、撤销和有效期仍由 IAM 的 `ConnectorHostCredential` 控制。

access token 建议携带以下最小 claims：

1. `sub`：Connector Host 主体标识，通常为 connectorHostId。
2. `credentialId`：签发本 token 的 ConnectorHostCredential 标识。
3. `principalType`：固定为 `connector-host`。
4. `organizationId`
5. `environmentId`
6. `permissionVersion` 或等价的 credential authorization version。
7. `iat`：标准 JWT issued-at 时间。
8. `jti`：标准 JWT token id，用于审计、诊断和撤销关联。

`X-Correlation-Id` header 与 W3C trace context 由请求链路透传，不写入 access token。access token 不承载单次请求的 correlation/trace 字段，避免把一次调用上下文固化到可复用 token 中。

token 可以携带 capability scope 或 permission 摘要用于诊断和 SDK 上下文，但服务端执行类接口不能只相信 token 中的静态 scope。最终授权仍由 IAM 基于凭证、授权授予、permission version、组织环境和资源上下文判断。

## 撤销与传播

撤销入口至少覆盖以下事实：

1. ConnectorHostCredential 被禁用、删除、过期或 secret 轮换。
2. capability scope 被收窄。
3. 映射到 Connector Host 的 permission grant 被撤销或 permission version 提升。
4. Connector Host 所属 organization/environment 范围被调整。

撤销传播采用“主动失效缓存 + 短 token TTL”组合：

1. IAM 修改 ConnectorHostCredential、AuthorizationGrant 或权限快照时，必须提升对应主体的 permission version 或 credential authorization version。
2. IAM 必须主动失效该 Connector Host 相关授权缓存；使用分布式缓存时要通过缓存 key 版本或事件驱动失效传播到 AppHub、Ops 和 Gateway 的 authorization check 调用路径。
3. 业务服务每次执行受保护接口时都通过 IAM internal authorization check 校验 token 对应的当前授权事实；不得只在本地按 JWT 过期时间放行执行类接口。
4. 在缓存失效传播延迟内，短期 access token 的剩余 TTL 是最大风险窗口；首批默认按 10 分钟控制，生产客户可下调到 5 分钟。
5. Connector Host 收到 401 或 403 后，`Sdk.Auth` 应立即丢弃本地 access token，重新取 token；重新取 token 仍失败时停止继续调用业务接口，并输出可诊断认证错误。

## Bearer Token 统一范围

AppHub、Ops 和 PlatformGateway 的 Connector Host 入口统一使用 bearer token：

| 服务 | 接口范围 | 终态权限 |
| --- | --- | --- |
| AppHub | registrations | `connectors.registrations.write` |
| AppHub | heartbeats | `connectors.heartbeats.write` |
| AppHub | state-snapshots | `connectors.state-snapshots.write` |
| Ops | pending operation tasks | `ops.tasks.read` 加 operation capability scope |
| Ops | operation results | `ops.results.write` 加 operation capability scope |

Gateway 可以作为外部统一入口转发 bearer token，但不能把 Connector Host 重新降级成 Gateway 自己的共享 secret。AppHub/Ops 直连和经 Gateway 转发时，IAM 看到的 principalType、organizationId、environmentId、connectorHostId 和 permission code 必须一致。

## Capability Scope 与 Permission Code 映射

Capability scope 和 permission code 不是同一种事实：

1. Capability scope 描述某个 Connector Host 被允许声明、领取或执行的本地能力，例如 `runtime.status`、`lifecycle.restart`、`log.read`、`backup.create`、`restore.execute`。
2. Permission code 描述某个 API 动作是否允许访问，例如 `connectors.state-snapshots.write` 或 `ops.results.write`。
3. Connector Protocol 中上报的 `CapabilityDescriptor` 是 AppHub 的实例能力事实，不自动授予 IAM 权限。
4. IAM 的 ConnectorHostCredential capability scope 是机器身份授权边界，用于约束该 Connector Host 能用哪些 capabilityCode/operationCode 调用平台接口。

首批映射规则：

| 场景 | 需要的 permission code | 需要的 capability scope |
| --- | --- | --- |
| 注册或更新实例能力清单 | `connectors.registrations.write` | 被上报的 capabilityCode 必须包含在凭证 scope 内。 |
| 上报心跳 | `connectors.heartbeats.write` | 不要求具体 operation capability，但必须在同一 organization/environment/connectorHostId 范围内。 |
| 上报状态快照 | `connectors.state-snapshots.write` | 状态来源对应的 runtime capability 必须包含在凭证 scope 内；首批可用 `runtime.status` 表达。 |
| 拉取 pending task | `ops.tasks.read` | 返回任务必须过滤到该凭证 scope 允许的 operationCode，例如 `lifecycle.restart`。 |
| 回传动作结果 | `ops.results.write` | result.operationCode 必须包含在凭证 scope 内，并且 task 的 connectorHostId、organizationId、environmentId 与 token 一致。 |

新增 operationCode 时必须同时评估两件事：是否需要新增 permission code，以及该 operationCode 是否进入 ConnectorHostCredential capability scope。单纯新增本地 Connector 能力不等于开放平台 API 权限。

## 旧版 Header-Secret 迁移

第二阶段低风险动作闭环中的 `X-Connector-Host-Id`、`X-Connector-Secret`、`X-Organization-Id` 和 `X-Environment-Id` 是本地纵切验证机制，迁移策略如下：

1. 当前阶段：保留 header-secret 入口用于本地验证脚本和旧 Connector Host，新增 bearer token 链路和 SDK token provider。
2. 兼容阶段：AppHub/Ops 同时接受 bearer token 与 header-secret，但生产 profile 默认要求 bearer token；header-secret 命中时必须输出 structured warning，并在响应中返回 deprecation signal。
3. 下线阶段：当 Connector Host SDK、验证脚本、AppHost/Compose 配置和 Ops/AppHub 测试全部切到 bearer token 后，在下一个 minor release 移除生产 profile 的 header-secret 支持，只允许显式 `Development` profile 或一次性迁移工具使用。
4. 主版本边界：同一主版本内不得长期保留生产 header-secret。若已经发布给外部客户，最多保留一个 minor release 的迁移窗口；下一个主版本必须完全删除业务接口 header-secret 认证。

header-secret 下线不影响 IAM 内部保存 secret hash。secret 仍作为换发短期 access token 的机器凭证，只是不再直接出现在 AppHub/Ops 业务请求中。

## 与 ExternalClient Principal 的关系

`connector-host` 和 `external-client` 都是非用户主体，但边界不同：

| 项目 | connector-host | external-client |
| --- | --- | --- |
| 身份来源 | ConnectorHostCredential | ExternalClient |
| 主要用途 | 受管环境接入、实例事实上报、pending task 领取、动作结果回传 | 第三方系统、平台应用或行业扩展访问公开 API |
| capability scope | 必须有，约束 Connector 能力和 operationCode | 可选；通常使用 OAuth/OIDC scope 或 AuthorizationGrant 表达 API 范围 |
| permission code | 由 IAM grant 映射到 connectors/ops 等权限 | 由 IAM grant 映射到被开放的业务权限 |
| 用户关系 | 不代表后台用户，不继承用户角色 | 不代表后台用户；若未来支持 delegated access，必须显式记录授权用户和 consent |
| 审计主体 | connectorHostId + credentialId | externalClientId + grantId |

Connector Host 不能注册成 ExternalClient 来绕过 capability scope；ExternalClient 也不能通过伪造 connectorHostId 来领取 Ops pending task。两类 principal 可以复用 IAM 的 JWT、permission code、AuthorizationGrant 和 internal authorization check 机制，但领域事实、凭证生命周期、审计字段和授权语义必须分开。

## SDK 与服务边界

`Nerv.IIP.Sdk.Auth` 负责：

1. 调用 IAM token endpoint。
2. 缓存短期 access token。
3. 在请求中注入 `Authorization: Bearer`。
4. 在 401/403 时清理 token、重新认证或向调用方返回统一认证错误。

`Sdk.Auth` 不保存 IAM 授权事实，不在客户端做最终授权判断，也不把 capability scope 解释成服务端 permission code。AppHub、Ops 和 PlatformGateway 的 endpoint 仍按各自业务接口声明 permission/context，并由 IAM 完成最终授权。

## 非目标

1. 不在本文档中引入完整 OAuth2/OIDC 授权服务器、consent 页面或第三方应用市场。
2. 不在本文档中要求 Connector Host 首批使用 mTLS、DPoP、token binding 或硬件密钥。
3. 不在本文档中定义所有 capabilityCode/operationCode 的完整字典；协议字典继续由 Connector Protocol 和 Ops 契约演进。
4. 不在本文档中改变 AppHub 是实例事实来源、Ops 是动作事实来源的边界。
