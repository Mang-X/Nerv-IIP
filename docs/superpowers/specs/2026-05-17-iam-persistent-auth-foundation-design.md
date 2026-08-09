# IAM 持久化认证基础设计

## 背景

第六阶段已将 AppHub 和 Ops 建立为 schema 治理的参考服务：服务自有 schema、EF migrations、metadata 注释、schema catalog 条目、PostgreSQL profile 测试，以及可复用的 schema 约定测试。IAM 是下一个拥有长期安全数据的平台能力，因此在加入 Gateway 全局授权、File Storage 授权、Ops 审批或 Console 登录 UI 之前，它应先进入同一持久化基线。

IAM 已具备用于登录、刷新、退出、`/me`、用户、角色、会话和 Connector Host 凭据验证的早期内存骨架。该骨架适合探索契约，但并非持久的安全基础：密码仅通过简单的 SHA-256 helper 进行哈希，access token 是 base64 会话字符串，refresh token 状态保存在内存中，占位管理 endpoint 不写入真实事实，并且 IAM 表没有 migration、catalog 或 schema 约定门禁。

本阶段将 IAM 升级为持久化后端基础，同时有意将范围控制在完整产品授权落地之前。不得引入新的 Console 页面、Gateway 全局 bearer policy、OAuth/OIDC、SSO、MFA、复杂 ABAC、私有化部署 bundle 或客户发布脚本。

## 推荐方案

采用仅后端的 IAM 持久化切片：保持现有 IAM HTTP 表面可辨识，但以 CleanDDD aggregate、PostgreSQL 持久化和显式 seed 行为替换内存安全事实。保留供早期纵切脚本使用的 InMemory profile，并新增遵循 AppHub/Ops 约定的 PostgreSQL profile。

已考虑的备选方案：

1. 只增加 IAM schema 和 migrations。这可以满足一部分持久化基线，但无法证明登录、refresh token rotation、撤销和 seed 行为可基于持久状态运行。
2. 立即构建完整授权链：IAM 持久化、Gateway bearer policies、权限检查、Connector Host token exchange 和 Console 登录。这更接近最终产品，但会合并多个相互独立的风险领域，并过早强制开展前端 Design System 工作。
3. 先构建 IAM 持久化认证基础。这会产出持久的安全后端以及可供 FileStorage 和后续服务复用的模式，同时把 Gateway 和前端变更留给独立 spec。

本设计选择第三种方案。

## 范围

范围内：

1. 将 IAM 保持为 `backend/services/Iam` 下的三项目服务。
2. 为用户、角色、成员资格、用户会话和 Connector Host 凭据增加 IAM Domain aggregates 或聚焦的 aggregate roots。
3. 增加 IAM Infrastructure PostgreSQL profile，其中包含 `ApplicationDbContext`、entity configurations、repositories、migration runner，以及 `iam` schema 下的 EF migrations。
4. 将 PostgreSQL `__EFMigrationsHistory` 配置在 `iam` schema 内。
5. 将现有 InMemory profile 保持为当前早期纵切脚本的默认 profile。
6. 在 PostgreSQL 模式下，以持久化用户、密码哈希、用户会话和 refresh token rotation 替换内存登录路径。
7. 通过轻量 IAM 专用 adapter，使用 ASP.NET Core security primitives 完成密码哈希以及 JWT bearer token 的创建/验证。
8. 为初始平台管理员、平台管理员角色、seed permissions 和本地 Connector Host 凭据增加幂等的 local/dev seed 行为。
9. 使用 `Nerv.IIP.Testing.EntityFramework` 增加 IAM schema 约定测试。
10. 增加 PostgreSQL profile 测试，证明 IAM 能迁移空数据库，并基于持久状态运行登录、刷新、撤销和 Connector Host 凭据验证。
11. 更新 IAM 架构文档、schema catalog、implementation readiness 和 README 状态。

范围外：

1. Gateway 全局认证和权限 policy。
2. Console 登录 UI、导航、页面、design token 或组件变更。
3. OAuth2/OIDC authorization server 行为、consent 页面或第三方应用市场。
4. SSO、MFA、WebAuthn、device binding、DPoP 或 mTLS。
5. 复杂 ABAC、跨组织委派和临时授权。
6. 超出证明持久事实所需最小 endpoint 的完整用户/角色管理工作流。
7. 客户发布 migration bundle、installer 集成、backup/restore 演练或生产 seed 操作员 UI。
8. GaussDB、DMDB 或其他数据库 profile 验证。

## 需求分析

### 干系人

| 角色 | 目标/痛点 | 权限/限制 | 备注 |
| --- | --- | --- | --- |
| 平台管理员 | 可以登录、刷新会话、检查基本 IAM 事实并撤销会话。 | 本阶段使用 seed 管理员账号；不包含完整角色管理 UI。 | 代表首个人类 principal。 |
| IAM 服务 | 拥有用户、角色、成员资格、会话和凭据安全事实。 | 不得依赖其他服务提供身份真相。 | 为后续授权提供持久化基线。 |
| Connector Host | 需要可按组织/环境范围验证的机器凭据。 | 本阶段只获得 connector principal 验证；完整 token exchange 后置。 | 保持当前 Connector Host 骨架兼容。 |
| 未来的 Gateway/Console | 需要后续可消费的稳定 IAM token 和权限事实。 | 本阶段不获得完整 enforcement。 | 本阶段准备后端契约和数据模型。 |
| 发布人员/操作员 | 需要后续可封装进发布脚本的 migration 和 seed 行为。 | 客户发布脚本后置。 | Local/dev seed 必须显式且可诊断。 |

### 需求条目

| ID | 场景 | 干系人/对象 | 业务实体 | 操作类型 | 约束/前置条件 | 备注 |
| --- | --- | --- | --- | --- | --- | --- |
| IAM-R1 | Seed 初始组织、环境、管理员角色、管理员用户和本地 Connector Host 凭据。 | 发布人员/操作员、IAM 服务 | User, Role, Membership, ConnectorHostCredential | 创建/seed | Seed 必须幂等，且不得记录 secrets。 | 只有使用清晰配置名称时，才可存在 Local/dev 默认值。 |
| IAM-R2 | 平台管理员使用登录名和密码登录。 | 平台管理员 | User, UserSession | 创建 | 已禁用或已删除的用户不能登录；密码哈希使用 ASP.NET Core hasher。 | 返回 JWT access token、refresh token 和 session id。 |
| IAM-R3 | 平台管理员刷新会话。 | 平台管理员 | UserSession | 修改/创建 | Refresh token 只以哈希存储；rotation 使先前的 refresh token 失效。 | security 或 permission version 变更时拒绝过期 access token。 |
| IAM-R4 | 平台管理员退出或会话被撤销。 | 平台管理员、IAM 服务 | UserSession | 修改/关闭 | 已撤销会话不能刷新；`/me` 拒绝已撤销的 session token。 | 在可用时记录撤销原因。 |
| IAM-R5 | 平台管理员查询自身 profile。 | 平台管理员 | User, Membership, Role | 查看 | 需要有效 bearer token。 | 响应包含用户身份和基本组织/环境上下文。 |
| IAM-R6 | 平台管理员列出用户、角色和会话。 | 平台管理员 | User, Role, UserSession | 查看 | 本阶段 endpoint 可以保持最小且面向管理员。 | 完整 permission policy enforcement 后置。 |
| IAM-R7 | 验证 Connector Host 凭据。 | Connector Host | ConnectorHostCredential | 查看/验证 | secret 只以哈希存储；检查有效窗和组织/环境范围。 | 完整 Connector Host token exchange 后置。 |
| IAM-R8 | 从空 PostgreSQL 数据库迁移 IAM 表。 | 发布人员/操作员 | 所有持久化 IAM 实体 | 异步/设置 | 使用 EF migrations，而不是 `EnsureCreated()`。 | 测试拥有一次性验证数据库。 |
| IAM-R9 | IAM schema 遵循数据库约定。 | IAM 服务、未来代理 | 所有持久化 IAM 实体 | 验证 | 业务表和列带有注释；字符串 ID 具备长度和生成规则；history table 位于 `iam`。 | 复用 schema convention helper。 |

### 业务实体视图

| 业务实体 | 覆盖需求 | 主要职责/规则 | 关键输入/输出 |
| --- | --- | --- | --- |
| User | IAM-R1, IAM-R2, IAM-R3, IAM-R4, IAM-R5, IAM-R6 | 登录身份、密码哈希、启用/删除状态、security stamp 和 permission version。 | 输入登录名/密码；输出用户身份和 token claims。 |
| Role | IAM-R1, IAM-R5, IAM-R6 | 命名权限集。权限码来自已记录的 seed 基线。 | 角色 id/name 和 permission code 列表。 |
| Membership | IAM-R1, IAM-R5 | 用户在组织/环境中的范围以及分配的角色。 | 用户 id 加组织/环境范围。 |
| UserSession | IAM-R2, IAM-R3, IAM-R4, IAM-R5, IAM-R6 | Refresh token 哈希、access-token session anchor、过期、撤销和 permission-version snapshot。 | Session id、refresh token 哈希，以及 issued/expires/revoked 时间戳。 |
| ConnectorHostCredential | IAM-R1, IAM-R7 | 机器凭据哈希、组织/环境范围、capability scope 和有效窗。 | 输入 connector host id/secret；输出 connector principal。 |
| SeedManifest | IAM-R1, IAM-R8 | 记录 seed 名称/版本或等效幂等证据。 | Seed 执行结果和诊断 metadata。 |

### 触发条件与后续动作

| 触发条件 | 后续动作/影响 | 相关干系人 | 受影响实体 | 备注 |
| --- | --- | --- | --- | --- |
| 已 seed 管理员用户 | 创建角色、权限集、成员资格和独立于初始会话的用户事实。 | 发布人员/操作员、平台管理员 | User, Role, Membership | 可重新运行 Seed 而不产生重复行。 |
| 登录成功 | 创建用户会话并签发 token pair。 | 平台管理员 | UserSession | 本阶段 token 签发不是离开 IAM 的 domain event。 |
| 刷新成功 | 撤销或停用旧 refresh 状态、创建新的 refresh token 状态并签发新的 access token。 | 平台管理员 | UserSession | Rotation 失败不得泄露 token 的哪个部分有误。 |
| 会话被撤销 | 该会话后续的 refresh 和 `/me` 调用失败。 | 平台管理员、IAM 服务 | UserSession | 通过 session lookup 拒绝现有 JWT。 |
| 用户 security stamp 或 permission version 变更 | 现有 access token 变为 stale。 | IAM 服务、未来的 Gateway | User, UserSession | 完整 permission cache invalidation 后置。 |
| Connector 凭据被撤销或过期 | Connector 凭据验证失败。 | Connector Host | ConnectorHostCredential | Connector Host token exchange 仍后置。 |

## CleanDDD 模型

### 聚合

| 名称 | 职责摘要 | 关键不变式 |
| --- | --- | --- |
| User | 拥有登录身份、密码哈希、启用状态、security stamp 和 permission version。 | 登录名唯一；已禁用/已删除用户不能创建或刷新会话；密码哈希永不为空；security stamp 变更会使 access token 失效。 |
| Role | 拥有角色名和 permission codes。 | 角色名在平台范围内唯一；permission codes 必须是已知 seed codes，或由后续 migration 显式加入。 |
| Membership | 将用户关联到组织/环境角色。 | 用户在同一组织/环境中不能有重复 membership；role ids 必须非空。 |
| UserSession | 拥有 refresh token 状态和会话生命周期。 | Refresh token 哈希绝不以明文存储；已撤销会话不能刷新；过期时间使用 UTC；session id 由 IAM 生成。 |
| ConnectorHostCredential | 拥有机器凭据哈希、有效性和范围。 | Secret 哈希永不为空；凭据仅在有效窗内有效；connector host id 唯一。 |
| SeedManifest | 记录幂等 seed 执行。 | Seed 名称和版本唯一；重新运行相同 seed 是安全的。 |

实现可将某些小型 aggregate 组织到聚焦文件中，但不应继续把当前 `IamFacts.cs` 的纯 record 模型用作最终持久化领域模型。

### 命令

| 名称 | 聚合 | 输入 | 行为/事件 | 幂等性 |
| --- | --- | --- | --- | --- |
| SeedIamBaselineCommand | User, Role, Membership, ConnectorHostCredential, SeedManifest | Seed config、permission list、admin login/email/password、connector host id/secret | 创建或更新基线 IAM 事实。 | 按 seed 名称/版本和稳定业务键幂等。 |
| LoginUserCommand | User, UserSession | Login name、password、client info、ip address | 验证密码并创建会话。 | 不幂等；每次成功登录都创建一个会话。 |
| RefreshUserSessionCommand | UserSession, User | Refresh token、client info、ip address | 验证 refresh hash、撤销旧 refresh 状态并创建 rotated token。 | 单次使用的 token rotation。 |
| RevokeUserSessionCommand | UserSession | Session id、reason | 将会话标记为已撤销。 | 按 session id 幂等。 |
| ValidateConnectorHostCredentialCommand | ConnectorHostCredential | Connector host id、secret | 验证 secret hash 和 scope。 | 无变更、类似读取的命令。 |

### 查询

| 名称 | 聚合 | 过滤/排序/分页 | 输出 |
| --- | --- | --- | --- |
| GetCurrentPrincipalQuery | User, Membership, Role, UserSession | bearer token 中的 Session id。 | User id、login name、email、principal type、organization/environment scopes 和 permission version。 |
| ListUsersQuery | User | 可选 search/status、page number、page size。 | 最小 user list DTO。 |
| ListRolesQuery | Role | 可选 search、page number、page size。 | Role id/name 和 permission codes。 |
| ListSessionsQuery | UserSession | 可选 user id/revoked status、page number、page size。 | Session id、user id、issued/expires/revoked 时间戳。 |

### 领域事件

| 领域事件 | 发布方 | 处理动作 | 外部副作用 |
| --- | --- | --- | --- |
| UserLoggedInDomainEvent | UserSession | 如有需要，在 IAM 内记录 session-created diagnostics。 | 本阶段无。 |
| UserSessionRefreshedDomainEvent | UserSession | 如有需要，在 IAM 内记录 rotation diagnostics。 | 本阶段无。 |
| UserSessionRevokedDomainEvent | UserSession | 未来的 cache invalidation hook。 | 本阶段无。 |
| PermissionSetChangedDomainEvent | Role or User | 未来的 permission cache invalidation hook。 | 本阶段不连接到 IAM 外部。 |
| ConnectorHostCredentialValidatedDomainEvent | ConnectorHostCredential | 仅作为可选 diagnostic event。 | 本阶段无。 |

### API 端点

| 方法/路径 | 命令/查询 | 认证/鉴权 | 一致性 |
| --- | --- | --- | --- |
| `POST /api/iam/v1/auth/login` | LoginUserCommand | 匿名。 | 创建持久会话并返回 token pair。 |
| `POST /api/iam/v1/auth/refresh` | RefreshUserSessionCommand | 携带 refresh token 的匿名请求。 | 原子旋转 refresh token。 |
| `POST /api/iam/v1/auth/logout` | RevokeUserSessionCommand | 优先使用 Bearer token；仅当现有契约要求时保留 session id fallback。 | 幂等撤销会话。 |
| `GET /api/iam/v1/me` | GetCurrentPrincipalQuery | Bearer token。 | 读取持久化用户/会话状态。 |
| `GET /api/iam/v1/users` | ListUsersQuery | 面向管理员；完整 permission enforcement 后置。 | 只读。 |
| `GET /api/iam/v1/roles` | ListRolesQuery | 面向管理员；完整 permission enforcement 后置。 | 只读。 |
| `GET /api/iam/v1/sessions` | ListSessionsQuery | 面向管理员；完整 permission enforcement 后置。 | 只读。 |
| `POST /api/iam/v1/sessions/{sessionId}/revoke` | RevokeUserSessionCommand | 面向管理员；完整 permission enforcement 后置。 | 幂等。 |
| `POST /api/iam/v1/connectors/credentials/validate` | ValidateConnectorHostCredentialCommand | 匿名机器 secret 验证。 | 只返回 connector principal。 |

除非实现需要用创建/更新用户和角色 endpoint 证明该切片，否则这些 endpoint 可以继续作为 placeholder 或延后。如果保留，则在能够持久化真实事实并验证输入之前，不得声称支持生产管理。

## 架构

目标结构与 AppHub/Ops 一致：

```text
backend/services/Iam/
  src/
    Nerv.IIP.Iam.Domain/
      AggregatesModel/
        UserAggregate/
        RoleAggregate/
        MembershipAggregate/
        UserSessionAggregate/
        ConnectorHostCredentialAggregate/
      DomainEvents/
    Nerv.IIP.Iam.Infrastructure/
      ApplicationDbContext.cs
      IamPersistenceServiceCollectionExtensions.cs
      IamDatabaseMigrationRunner.cs
      EntityConfigurations/
      Repositories/
      Migrations/
    Nerv.IIP.Iam.Web/
      Application/
        Commands/
        Queries/
        Auth/
        Seed/
      Endpoints/
  tests/
    Nerv.IIP.Iam.Web.Tests/
```

`Program.cs` 应注册 FastEndpoints、caching、observability 和所选 persistence profile。默认 profile 仍为 `InMemory`；通过 `Persistence:Provider=PostgreSQL` 和 `ConnectionStrings:IamDb` 选择 PostgreSQL。

PostgreSQL profile 注册：

1. `ApplicationDbContext`，使用 `UseNpgsql(..., npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "iam"))`；
2. repositories 和 unit of work；
3. 密码哈希 adapter；
4. JWT token 签发器/验证器；
5. 在 local/dev 测试中显式使用的 seed runner 或 seed command handler。

InMemory profile 应继续服务现有早期骨架。可将其重构到接口之后以保持 endpoint 行为一致，但不得扩展为第二套生产实现。

## 数据模型

数据库 schema：`iam`

初始业务表：

| 表 | 类型 | 用途 |
| --- | --- | --- |
| `organizations` | business | IAM seed 和 membership scope 所需的最小平台组织事实。 |
| `environments` | business | 组织下的最小环境事实。 |
| `users` | business | 登录身份、密码哈希、状态、security stamp 和 permission version。 |
| `roles` | business | Role metadata。 |
| `role_permissions` | business | 分配给角色的 permission codes。 |
| `memberships` | business | 按组织/环境划分范围的用户角色分配。 |
| `user_sessions` | business | Refresh token 哈希、会话状态、过期和撤销事实。 |
| `connector_host_credentials` | business | 机器凭据哈希、组织/环境范围、capability scope 和有效性。 |
| `seed_manifests` | business | 幂等 seed 执行记录。 |
| `cap_published_messages` | system | 如果 IAM 在本阶段或之后接入 CAP，则作为 CAP published message outbox。 |
| `cap_received_messages` | system | 如果 IAM 在本阶段或之后接入 CAP，则作为 CAP received message inbox。 |
| `cap_locks` | system | 如果 IAM 在本阶段或之后接入 CAP，则作为 CAP distributed lock table。 |
| `__EFMigrationsHistory` | system | `iam` schema 中的 EF migration history。 |

如果本阶段未为 IAM 接入 CAP，则不应提前创建 CAP 表。catalog 应只反映 migration 实际创建的表。

重要数据规则：

1. 永不记录 password hash 和 refresh token hash，也永不通过 API 返回。
2. 永不记录 Connector Host secret hash，也永不通过 API 返回。
3. Login name 和 email 具有稳定的最大长度和唯一约束。
4. 字符串标识符具有 `ValueGeneratedNever()` 和显式最大长度。
5. 首个 IAM schema 应避免 JSON/text 列，除非记录了明确的兼容性原因。
6. 所有时间戳都使用 UTC，且注释必须说明这一点。

## Token 与安全流程

访问 token：

1. 使用 JWT Bearer。
2. Claims 包含 `sub`、`sessionId`、`principalType=user`、`organizationId`、`environmentId`、`securityStamp`、`permissionVersion`、`iat` 和 `jti`。
3. 为保障开发安全，JWT lifetime 应足够短；可配置具体默认值，例如 15 分钟。
4. `/me` 和 IAM endpoints 的服务端验证必须检查持久化会话，而不只是 JWT signature。

刷新 token：

1. 从 cryptographically strong random bytes 生成。
2. 只向调用方返回一次。
3. 只以哈希存储。
4. 刷新时轮换。
5. rotation 成功后，旧 refresh token 失败。
6. 已禁用用户和已撤销会话不能刷新。

密码：

1. 使用 `Microsoft.AspNetCore.Identity.PasswordHasher<TUser>` 或其轻量 wrapper。
2. 存储 hasher 输出，而不是 SHA-256。
3. 如果 hasher 报告需要 rehash 的结果，则支持未来的 rehash detection。

Seed secrets（种子敏感信息）：

1. 为支持可重复测试和脚本，可以存在 Local/dev 默认值。
2. 非本地发布流程必须通过配置或安全输入提供 admin password 和 connector secret。
3. Secrets 绝不得写入日志、schema catalog 或 migration 文件。

## 错误处理

认证错误返回具有稳定 problem shape 的 401，且不泄露凭据细节：

1. 登录名或密码无效：通用 unauthorized 响应。
2. Refresh token 无效：通用 unauthorized 响应。
3. 会话已撤销：unauthorized 响应。
4. 会话或 access token 已过期：unauthorized 响应。
5. 用户已禁用：unauthorized 响应。
6. Connector Host 凭据无效：unauthorized 响应。

当输入不是 secret 时，validation error 返回带字段级信息的 400，例如 paging value 格式错误。与 secret 相关的错误应保持通用。

Persistence error 应在测试中明确失败，并在运行时日志中随 correlation id 一起记录。日志必须包含 service name、correlation id 和 operation name，但不得包含 tokens、password、refresh token、connector secret 或完整 connection string。

Seed error 会使 local/dev 验证中的启动或显式 seed command 失败。seed 步骤应报告 seed name、seed version、owner service、result 和 correlation id。

## 测试

实现应采用 test-first：

1. 使用一次性 IAM 数据库，为 PostgreSQL 登录、refresh token rotation、退出/撤销、`/me` 和 Connector Host 凭据验证增加失败测试。
2. 在增加 metadata 前，先增加失败的 IAM schema 约定测试。
3. 增加 IAM `ApplicationDbContext`、entity configurations、migration runner 和 migration。
4. 增加幂等 seed 行为，并验证运行两次不会产生重复用户、角色或凭据。
5. 验证 rotation 后旧 refresh token 失败。
6. 验证已撤销会话无法 refresh 和调用 `/me`。
7. 验证已禁用用户不能登录或 refresh。
8. 验证 password hashes 不是 SHA-256 plain hashes，且永不返回。
9. 运行 targeted IAM tests。
10. 运行 `dotnet test backend/Nerv.IIP.sln --no-restore`。
11. 如果 Connector Host contracts 或 SDK auth 行为发生变化，运行 `dotnet test connector-hosts/Nerv.IIP.ConnectorHost.sln`。
12. 运行 `git diff --check`。

如果 PostgreSQL 测试设置需要遵循第五阶段持久化脚本模式进行 Docker orchestration，则 `scripts/verify-iam-persistent-auth-foundation.ps1` 之类的新脚本会很有用。如果 targeted IAM PostgreSQL tests 可通过现有本地基础设施拥有自己的一次性数据库，则可在 implementation plan 中增加该脚本，而不由本设计强制要求。

除非 Gateway OpenAPI 或生成的前端 client inputs 发生变化，否则不要求 frontend gates。本阶段不应变更 Console 页面或设计资产。

## 文档

实现时更新以下文档：

1. `README.md`：当前状态和下一阶段摘要。
2. `docs/architecture/implementation-readiness.md`：IAM 持久化认证基础状态和用法。
3. `docs/architecture/iam-authentication-baseline.md`：说明当前已实现内容与仍属未来的内容。
4. `docs/architecture/database-schema-catalog.md`：增加 IAM schema 表和已知缺口。
5. `docs/architecture/database-schema-conventions.md`：不做大范围重写，但实现后将 IAM 加入 convention tests 覆盖的服务列表。
6. `docs/architecture/technology-stack-references.md`：仅在引入新的长期依赖时更新。

不需要新的 ADR。ADR 0009 已覆盖服务自有 migrations、release/seed 策略和 schema catalog 义务。本阶段为 IAM 落实这一已接受决策。

## 完成定义

满足以下条件时，本阶段可关闭：

1. IAM 具有 PostgreSQL `ApplicationDbContext`、entity configurations，以及 `iam` schema 中已提交的 EF migrations。
2. IAM 将 `__EFMigrationsHistory` 配置在 `iam` schema 内。
3. IAM 业务表和列具有能通过 schema convention tests 的注释。
4. IAM schema catalog 条目与 migration 和 entity configurations 一致。
5. 初始 admin、role、permission set、membership 和本地 Connector Host 凭据可以幂等 seed。
6. 持久化登录返回 JWT access token、refresh token 和 session id。
7. Refresh token rotation 只持久化哈希，并拒绝旧 refresh token。
8. 退出/会话撤销会阻止 refresh 和 `/me`。
9. 已禁用用户不能登录或 refresh。
10. Connector Host 凭据验证可基于已持久化的哈希凭据工作。
11. Targeted IAM PostgreSQL tests 通过。
12. Backend solution tests 通过。
13. 现有 InMemory 行为继续可供早期脚本使用。
14. 不引入 Gateway 全局授权、Console 登录 UI 或 Design System 工作。
