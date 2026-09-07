# IAM 认证与授权当前架构

本文描述 Nerv-IIP 当前 IAM 的身份事实、认证/授权边界与 Gateway 协作方式。M2-K 迁移前的混合基线包含阶段完成状态、密钥轮换操作、配置细节和后续能力规划，已按原 Git blob 冻结到 [`../../reports/iam-authentication-baseline-pre-m2-k-2026-09-07.md`](../../reports/iam-authentication-baseline-pre-m2-k-2026-09-07.md)。

## 事实所有权

IAM 拥有 User、Membership、Role、Permission、UserSession、ExternalClient、ConnectorHostCredential、AuthorizationGrant，以及组织、环境、主体、权限版本和授权范围等身份事实。ASP.NET Core 安全组件提供密码哈希、JWT Bearer、授权策略、Data Protection 等基础能力，但 ASP.NET Core Identity 默认表不是平台 IAM 的领域事实模型。

## 当前认证边界

1. 后台用户认证使用短期 JWT access token 与可撤销 UserSession / refresh rotation；会话、安全戳和权限版本仍由 IAM 裁决。
2. IAM 使用非对称 JWT 签名边界；签发私钥只属于 IAM，Gateway/下游按公开 JWKS 与 `kid` 验签，不共享签发私钥。
3. Platform Console 只调用 PlatformGateway facade；Gateway 可以验证 bearer token、透传当前组织/环境并调用 IAM internal authorization check，但不持有用户、角色、会话或授权事实。
4. 执行类授权不能只依赖 token 中的静态权限快照；当前服务端授权仍回到 IAM 的主体、会话、权限版本、组织/环境、permission code 与资源上下文判断。
5. `external-client` 与 `connector-host` 都是非用户主体，但凭据生命周期、capability scope 与审计身份保持分离。

## Connector Host 关系

Connector Host 的机器身份、短期 access token、capability scope / permission code 边界与旧 header-secret 兼容路线见 [`../integration/connector-host-machine-auth.md`](../integration/connector-host-machine-auth.md)。Connector Host 协议本身见 [`../integration/connector-protocol-v1.md`](../integration/connector-protocol-v1.md)。

## 权威路由

- 授权与权限治理：[`../../governance/security/authorization.md`](../../governance/security/authorization.md)。
- 当前 permission 人工目录：[`../../reference/security/permission-catalog.md`](../../reference/security/permission-catalog.md)。
- 部署、secret 与运行配置操作：[`../../runbooks/deployment.md`](../../runbooks/deployment.md)。
- 平台上下文所有权：[`../overview/context-map.md`](../overview/context-map.md)。

精确 endpoint、claim、配置键、seed、密码策略数值、密钥材料与阶段完成状态始终回到当前代码、配置、Contracts、迁移和测试核实。
