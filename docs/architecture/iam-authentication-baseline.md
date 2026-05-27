# IAM 认证与授权基线

本文档冻结 Nerv-IIP 首批 IAM 认证与授权实现方向。目标是在不照搬通用 Admin 模板的前提下，复用 ASP.NET Core 的安全组件，并让平台自己的 IAM 领域模型成为身份、组织、环境、权限和授权事实源。

参考来源：

- ASP.NET Core Security：https://learn.microsoft.com/aspnet/core/security/
- ASP.NET Core Identity：https://learn.microsoft.com/aspnet/core/security/authentication/identity
- ASP.NET Core Authorization：https://learn.microsoft.com/aspnet/core/security/authorization/introduction

## 当前实现状态

IAM Persistent Auth Foundation 已覆盖后端持久化登录基线：PostgreSQL iam schema、初始 admin seed、JWT access token、refresh token hash + rotation、session revoke、/me 和 Connector Host credential validation。PostgreSQL profile 下 IAM 管理端点会先执行 bearer + permission 检查：用户读需要 `iam.users.read`，用户写需要 `iam.users.manage`，角色读需要 `iam.roles.read`，角色写需要 `iam.roles.manage`，会话读/撤销分别需要 `iam.sessions.read` 和 `iam.sessions.revoke`。Phase 8 已交付用户创建/编辑/禁用/重置密码、角色创建、角色权限 patch、权限 catalog、会话列表/撤销和 Console admin facade，不再以 501 作为管理写入口占位。

Gateway-wide permission enforcement 已覆盖现有 Console API：PlatformGateway 通过 ASP.NET Core JWT bearer authentication middleware 验证控制台 access token，不直接读取 IAM persistence；通过标准 authorization policy 进入受保护 endpoint 后，再把认证结果中的 bearer token 和所需 permission/context 转发给 IAM 的 internal authorization check endpoint，由 IAM 基于 session、security stamp、permission version、organization、environment、permission code 和可选 resource type/resource id 判断是否放行。当前已保护实例列表、实例详情、restart 运维任务创建和 operation task detail 查询。P2 已补 OIDC callback 入口、SSO session binding 字段、MFA challenge hook 和 ExternalClient resource-scope ABAC grant enforcement；完整 OAuth/OIDC 授权码服务器、consent 页面、WebAuthn 和复杂策略语言仍属于后续阶段。

Console login UI now consumes IAM through PlatformGateway Console Auth facade. The browser keeps a single Gateway API base URL; Gateway forwards login, refresh, logout and current-principal requests to IAM without owning identity facts.

Console IAM Admin UI now consumes IAM through PlatformGateway Console IAM Admin facade. The browser continues to call only `/api/console/v1/**`; Gateway checks IAM-backed permissions in the current organization/environment context, forwards the original bearer token to IAM, and returns Gateway OpenAPI response envelopes consumed by the generated `@nerv-iip/api-client`.

Ops connector endpoints remain on the existing `X-Connector-*` header credential validator as a transitional boundary until the Connector Host standard authentication pipeline from #17 is present in code. That boundary is intentionally kept separate from the Gateway console JWT pipeline to avoid a parallel rewrite of connector machine identity in this phase.

## 决策

1. Nerv-IIP 不直接采用 NetCorePal.Template 的 Admin 模板作为最终 IAM 实现。
2. Nerv-IIP 不把 ASP.NET Core Identity 默认表作为平台 IAM 主模型。
3. IAM 服务自行拥有 User、Role、Permission、Membership、UserSession、ExternalClient、ConnectorHostCredential、AuthorizationGrant 等领域事实。
4. 密码哈希、认证管线、JWT Bearer、授权策略、Data Protection 等底层安全能力优先复用 ASP.NET Core 官方组件。
5. 首批用户登录采用短期 access token 加 refresh token 轮换机制。
6. 首批服务端授权采用 policy/permission code/context scope 组合，而不是只依赖角色名。
7. IAM 不设计独立的 session 认证码作为常规认证机制；首批以 JWT Bearer access token、UserSession 和 refresh token rotation 组合完成认证与会话管理。
8. P2 企业身份入口采用配置驱动的外部 OIDC callback 和已有 IAM User/Membership 映射，不把 IAM 升级为完整 OAuth2/OIDC 授权码服务器；后续若需要标准授权服务器、consent 页面或第三方应用市场，再评估 OpenIddict 或等价方案。

## 认证模型

### 后台用户

后台用户由 IAM 的 User 聚合管理。首批至少支持：

1. 用户名或邮箱登录。
2. 密码哈希存储。
3. 用户启用、禁用和软删除。
4. 初始超级管理员 seed。
5. 最近登录时间和登录失败记录。
6. refresh token 轮换、撤销和强制下线。

密码哈希优先使用 `Microsoft.AspNetCore.Identity.PasswordHasher<TUser>` 或一个薄封装适配器，不复制模板自带的自定义 PasswordHasher 作为安全基线。

### 会话

UserSession 是 IAM 的一等事实，用于表达一次可撤销的用户登录会话。首批字段建议包括：

1. sessionId
2. userId
3. refreshTokenHash
4. issuedAtUtc
5. expiresAtUtc
6. revokedAtUtc
7. revokedReason
8. securityStamp
9. permissionVersion
10. clientInfo
11. ipAddress
12. authenticationMethod
13. externalProvider
14. externalSubject
15. mfaVerifiedAtUtc

refresh token 只保存 hash，不保存明文。刷新时进行 token rotation，旧 token 失效，新 token 重新落库。

首批不引入独立 session 认证码。`sessionId` 是服务端会话事实标识，access token 只携带 `sessionId` claim；服务端通过 UserSession、securityStamp、permissionVersion、refreshTokenHash 和撤销状态判断会话是否仍有效。

### 外部客户端与 Connector Host

ExternalClient 和 ConnectorHostCredential 不与后台用户登录混用。

1. ExternalClient 表达第三方系统或平台应用的受控访问身份。
2. ConnectorHostCredential 表达 Connector Host 调用平台 API 的机器身份。
3. 两者都必须绑定 organizationId、environmentId、资源范围、能力范围和有效期。
4. Connector Host 调 AppHub 的注册、心跳、状态同步接口必须经过 IAM 授权，不能长期使用匿名入口。
5. Sdk.Auth 只能封装 token 获取、刷新、凭证注入和认证错误归一化，不保存 IAM 授权事实，也不在客户端做最终授权判断。

Connector Host 机器身份认证终态见 [Connector Host 机器身份认证终态](connector-host-machine-auth.md)：ConnectorHostCredential validation 只作为换发短期 access token 的前置校验，AppHub、Ops 和 Gateway 生产入口统一使用 bearer token，旧版 header-secret 只保留迁移窗口。

## Token 与 Claims

Access token 首批采用 JWT Bearer。JWT 中只放稳定且短期可接受的信息：

1. sub
2. sessionId 或 clientId
3. principalType：user、external-client、connector-host
4. organizationId
5. environmentId
6. securityStamp
7. permissionVersion
8. issuedAtUtc
9. correlationId 或 trace context 透传字段

JWT 可以携带少量权限码用于前端体验优化，但服务端执行类接口不能只相信 token 中的静态权限码。服务端必须根据 sessionId、securityStamp、permissionVersion 和 IAM 权限快照完成授权判断。

### 独立安全机制

以下能力不混入 UserSession 核心模型：

1. MFA、短信码、邮箱码或 WebAuthn 是登录因子，不是 session 认证码。P2 的 MFA challenge hook 只在 OIDC callback 后换发会话前使用，并把验证时间写入 UserSession。
2. CSRF token 仅在采用 Cookie 认证的浏览器场景中启用；若控制台统一使用 `Authorization: Bearer`，首批不引入 CSRF token。
3. 高风险操作确认码属于 Ops/Approval 的动作确认机制，不作为普通 API 认证凭据。
4. 设备绑定、DPoP、mTLS 或 token binding 属于后续高安全客户场景，不进入第一阶段基线。

## 授权模型

权限判断由三层组成：

1. Authentication：确认调用方是谁。
2. Authorization：确认调用方是否具备权限码、组织环境范围和资源范围。
3. Domain guard：命令处理器或领域服务校验业务不变式。

Endpoint 层只声明所需权限和上下文，不写领域规则。权限快照可以使用 FusionCache 缓存，但权限变更、角色变更、会话撤销和授权撤销必须主动失效相关缓存。

### 权限码命名基线

权限码采用 `{domain}.{resource}.{action}` 风格，全部小写，用复数资源名表达集合能力。首批建议冻结以下范围：

| 权限码 | 说明 |
| --- | --- |
| iam.users.read | 查看用户。 |
| iam.users.manage | 创建、禁用、重置用户。 |
| iam.roles.read | 查看角色与权限。 |
| iam.roles.manage | 创建角色、调整角色权限。 |
| iam.sessions.read | 查看会话。 |
| iam.sessions.revoke | 撤销会话。 |
| connectors.registrations.write | Connector Host 注册或更新应用实例事实。 |
| connectors.heartbeats.write | Connector Host 上报心跳。 |
| connectors.state-snapshots.write | Connector Host 上报实例状态快照。 |
| apphub.instances.read | 查看应用实例列表与详情。 |
| files.upload | 创建上传会话并完成文件上传。 |
| files.read | 查看文件元数据。 |
| files.download-grants.create | 创建短期下载授权。 |
| files.archive | 归档文件。 |
| ops.tasks.create | 创建运维任务。 |
| ops.tasks.read | 查看运维任务。 |
| ops.results.write | Connector Host 回传动作结果。 |
| ops.audit.read | 查看审计记录。 |

新增权限码必须保持向后兼容；同一主版本内不得改变既有权限码语义，确需改变时必须提升主版本。

## 首批接口范围

IAM 首批公开接口建议包括：

1. POST /api/iam/v1/auth/login
2. POST /api/iam/v1/auth/refresh
3. POST /api/iam/v1/auth/logout
4. GET /api/iam/v1/me
5. GET /api/iam/v1/users
6. POST /api/iam/v1/users
7. PATCH /api/iam/v1/users/{userId}
8. POST /api/iam/v1/users/{userId}/disable
9. POST /api/iam/v1/users/{userId}/reset-password
10. GET /api/iam/v1/roles
11. POST /api/iam/v1/roles
12. PATCH /api/iam/v1/roles/{roleId}/permissions
13. GET /api/iam/v1/sessions
14. POST /api/iam/v1/sessions/{sessionId}/revoke

ExternalClient、ConnectorHostCredential 和 AuthorizationGrant 可以先建领域骨架与内部命令，不要求第一批完成完整管理 UI。
P2 已新增 `POST /api/iam/v1/auth/oidc/callback` 和 `POST /api/iam/v1/auth/mfa/challenges/{challengeId}/verify` 作为企业身份入口。该 callback 用配置启用的 provider、callback secret、subject、email、organizationId 和 environmentId 映射既有 IAM 用户和 membership；RequireMfa=true 时先返回 challenge，不直接签发 session。callback secret 是当前最小可信入口，完整 id_token/JWKS 校验留给后续 IdP adapter 或标准授权服务器实现。

## 非目标

1. 首批不实现完整 OAuth2/OIDC 授权服务器。
2. 首批不实现第三方应用市场和 consent 页面。
3. 首批不实现复杂 ABAC 规则引擎；P2 只实现 organization/environment/resource type/resource id 范围匹配。
4. 首批不实现 WebAuthn 或完整企业 IdP 联邦登录；P2 只提供 OIDC callback、SSO session binding 和 MFA challenge hook。
5. 首批不让各服务各自维护用户、角色、权限或会话表。
6. 首批不引入独立 session 认证码；验证码、CSRF、高风险操作确认码按独立安全机制处理。

## 验收标准

1. IAM 能 seed 初始超级管理员。
2. 用户可以登录、刷新 token、退出并撤销 refresh token。
3. 被禁用用户不能继续刷新或访问受保护接口。
4. 权限变更后，相关用户的权限快照失效，后续请求使用新权限。
5. Connector Host 或外部客户端调用平台接口时能区分 principalType 和授权范围。
6. AppHub、Ops、Knowledge、AI Integration 和 Gateway 不维护平行身份权限模型。
