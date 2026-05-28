# 代码审计报告 2026-05-28

本文档记录 2026-05-28 对 Nerv-IIP 后端代码的系统性审计结果，覆盖 IAM 认证安全、编号服务可靠性、网关弹性设计和架构合规性。所有缺陷均已在代码中确认，并创建对应 GitHub issue 跟踪修复。

## 审计范围

| 维度 | 覆盖范围 |
| --- | --- |
| IAM 认证 | IamAuthService、IamRepositories、InMemoryIamAuthService |
| 编号服务 | MasterDataNumberingService 及 5 个业务服务的复制实例 |
| 网关弹性 | PlatformGateway、BusinessGateway HTTP 客户端弹性策略 |
| 权限缓存 | GatewayAuthorizationClient 缓存键、InvalidateGatewayCacheEndpoint |
| 消息可靠性 | Messaging:Provider InMemory/RabbitMQ profile 切换 |
| 架构合规 | 业务平台分层、服务发现、Scheduling 依赖 |

## 缺陷总览

| # | Issue | 严重度 | 类别 | 简述 |
| --- | --- | --- | --- | --- |
| 1 | [#214](https://github.com/Mang-X/Nerv-IIP/issues/214) | Critical | 安全 | Refresh token 重放竞态条件 |
| 2 | [#215](https://github.com/Mang-X/Nerv-IIP/issues/215) | High | 正确性 | 多组织用户返回错误 membership |
| 3 | [#216](https://github.com/Mang-X/Nerv-IIP/issues/216) | Critical | 并发 | 编号服务跨进程竞态导致编号重复 |
| 4 | [#217](https://github.com/Mang-X/Nerv-IIP/issues/217) | Medium | 资源泄漏 | 编号计数器静态字典内存泄漏 |
| 5 | [#218](https://github.com/Mang-X/Nerv-IIP/issues/218) | High | 安全 | 登录失败计数无锁定逻辑 |
| 6 | [#219](https://github.com/Mang-X/Nerv-IIP/issues/219) | Medium | 性能 | LINQ ToLower() 导致索引失效 |
| 7 | [#220](https://github.com/Mang-X/Nerv-IIP/issues/220) | Medium | 可维护性 | 编号服务跨 5 个服务代码重复 |
| 8 | [#221](https://github.com/Mang-X/Nerv-IIP/issues/221) | High | 安全 | 权限缓存 key 缺少 version + 失效端点无鉴权 |
| 9 | [#222](https://github.com/Mang-X/Nerv-IIP/issues/222) | High | 可靠性 | InMemory 消息 provider 生产环境无强制检查 |
| 10 | [#223](https://github.com/Mang-X/Nerv-IIP/issues/223) | Medium | 架构 | Scheduling 服务违反分层原则 |
| 11 | [#224](https://github.com/Mang-X/Nerv-IIP/issues/224) | Low | 架构 | 服务间 HTTP 调用硬编码 BaseUrl |
| 12 | [#225](https://github.com/Mang-X/Nerv-IIP/issues/225) | High | 可靠性 | BusinessGateway 非幂等操作缺少安全弹性策略 |

## 详细发现

### 1. RefreshAsync refresh token 重放竞态条件 (#214)

**严重度**: Critical
**文件**: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Auth/IamAuthService.cs`

**问题**: `RefreshAsync` 方法中，读取 session (L59)、撤销旧 token (L71)、创建新 token 三步操作非原子执行。并发请求可在 revoke 前通过同一 refresh token 的有效性检查，导致一个已撤销的 token 被重复使用。

**修复方向**: 引入数据库级原子操作（如 `UPDATE ... WHERE refresh_token = @old AND is_revoked = false RETURNING *`），或使用乐观并发控制（ConcurrencyToken on refresh token column），确保只有第一个请求能成功消费 token。

---

### 2. GetCurrentPrincipalAsync 多组织用户返回错误 membership (#215)

**严重度**: High
**文件**: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Auth/IamAuthService.cs` (L138-156), `backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/Repositories/IamRepositories.cs` (L168-175)

**问题**: `GetCurrentPrincipalAsync` 忽略 JWT claims 中的 `organizationId` 和 `environmentId`，改为调用 `GetFirstByUserIdAsync`，该方法按 `OrganizationId` 升序返回第一条 membership 记录。多组织用户将始终返回按字母序最小的组织 membership，而非用户当前登录上下文所属的组织。

**修复方向**: 从 JWT claims 中提取 `organizationId` 和 `environmentId`，使用这两个值精确查询用户在当前上下文下的 membership。

---

### 3. NumberingService 跨进程竞态导致编号重复 (#216)

**严重度**: Critical
**文件**: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Commands/MasterData/MasterDataNumberingService.cs`

**问题**: `CounterScopeLocks` 使用 `static ConcurrentDictionary<string, SemaphoreSlim>` 作为进程本地锁。单实例部署时可用，但多实例部署时不同进程的 SemaphoreSlim 互不感知，可能同时分配相同编号。`NumberingCounter` 虽然通过 Fluent API 配置了 `IsConcurrencyToken()`，但缺少 `DbUpdateConcurrencyException` 的重试逻辑。

**修复方向**: 依赖数据库级 `IsConcurrencyToken()` 乐观并发控制，添加 `DbUpdateConcurrencyException` 捕获与重试逻辑（有限次数），移除进程本地锁或将其降级为本地防雷击群效应的辅助措施。

---

### 4. ReservedCounterValues 静态字典内存泄漏 (#217)

**严重度**: Medium
**文件**: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Commands/MasterData/MasterDataNumberingService.cs`

**问题**: `private static readonly ConcurrentDictionary<string, long> ReservedCounterValues` 以 `{orgId}:{envId}:{docType}:{dateSegment}` 为 key 缓存计数器值，但从不清理过期条目。随着日期滚动（如按天/月分段），旧的 dateSegment key 永远驻留内存，长时间运行后内存持续增长。

**修复方向**: 引入 TTL 淘汰机制（如定期扫描删除过期 dateSegment 的条目），或使用带过期策略的缓存（如 `MemoryCache`）替代裸 `ConcurrentDictionary`。

---

### 5. 登录失败计数有记录但缺少账户锁定逻辑 (#218)

**严重度**: High
**文件**: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Auth/IamAuthService.cs`

**问题**: `LoginAsync` 调用 `RecordFailedLogin()` 记录失败次数，但在校验密码前不检查是否已达锁定阈值。攻击者可无限次尝试密码暴力破解，失败计数只做记录，不产生任何限制效果。

**修复方向**: 在密码校验前添加 `IsLockedOut()` 检查；达到阈值后拒绝登录并返回限时锁定响应；考虑按 IP + 用户名组合限流。

---

### 6. GetByLoginNameAsync LINQ ToLower() 导致索引失效 (#219)

**严重度**: Medium
**文件**: `backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/Repositories/IamRepositories.cs` (L35-41)

**问题**: `GetByLoginNameAsync` 使用 `.Where(x => x.LoginName.ToLower() == loginName.ToLower())`。EF Core 翻译为 SQL `WHERE LOWER(login_name) = LOWER(@p)`，导致 `login_name` 列上的普通 B-tree 索引无法命中，退化为全表扫描。

**修复方向**: 使用 PostgreSQL `citext` 类型或在 `login_name` 列上创建 `LOWER()` 函数索引；LINQ 侧移除 `.ToLower()` 调用以匹配索引。

---

### 7. NumberingService 跨 5 个业务服务代码重复 (#220)

**严重度**: Medium
**文件**: MasterData、ProductEngineering、Inventory、MES、ERP 等 5 个服务中各自复制了完整的 NumberingService 实现

**问题**: 编号生成逻辑（包括计数器管理、编号格式化、幂等 key 绑定）在 5 个业务服务中完全复制。修复 #216/#217 等缺陷时需要同步修改 5 处，遗漏风险极高。

**修复方向**: 将 NumberingService 提取到 `backend/common` 下的共享包（如 `Nerv.IIP.Numbering`），各服务通过 DI 引用共享实现，仅保留服务特定的编号格式配置。

---

### 8. 权限缓存 key 缺少 permissionVersion + 失效端点无鉴权 (#221)

**严重度**: High
**文件**:
- `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Application/Auth/GatewayAuthorizationClient.cs` (L72-88)
- `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Endpoints/Cache/InvalidateGatewayCacheEndpoint.cs`

**问题**:
1. `BuildCacheKey` 中的 `"v1"` 是硬编码字面量，不是运行时从 JWT 读取的 `permissionVersion`。角色权限变更后，旧缓存条目不会因 version 变化而失效，用户继续使用已撤销的权限直到 TTL 过期。
2. `InvalidateGatewayCacheEndpoint` 标记为 `[AllowAnonymous]`，任何未认证请求都可清空 Gateway 权限缓存，构成 DoS 攻击面。
3. `cache.Clear()` 仅清除当前进程的 FusionCache L1 缓存，多实例部署下其他实例不受影响。

**修复方向**:
1. 将 `"v1"` 替换为从 JWT claims 读取的 `permissionVersion` 值。
2. 移除 `[AllowAnonymous]`，改用 `InternalServiceAuthorizationPolicy` 或等效内部认证。
3. 使用 FusionCache backplane 的 `RemoveByTag` 或事件总线广播失效到所有实例。

---

### 9. InMemory 消息 provider 在生产环境无强制检查 (#222)

**严重度**: High
**文件**: 各服务的 messaging provider 注册逻辑

**问题**: `Messaging:Provider` 默认为 `InMemory`，但在非 Development 环境下没有任何校验或警告。如果生产环境遗漏 `Messaging:Provider=RabbitMQ` 配置，所有集成事件将在 CAP InMemory transport 中传递，进程重启后未消费的事件丢失，跨服务事件完全不可达。

**修复方向**: 在非 Development 环境启动时，检测到 `Messaging:Provider=InMemory` 或未配置时，抛出 `InvalidOperationException` 阻止启动，或至少输出 `Warning` 级别日志。

---

### 10. Scheduling 服务依赖 9 个上游服务，违反分层原则 (#223)

**严重度**: Medium
**文件**: Scheduling/BusinessScheduling 服务的 HTTP 客户端注册

**问题**: Scheduling 服务作为业务平台 Layer 2（依赖 ADR 0014），但注册了 9 个上游服务的 HTTP 客户端，包括同层级或更高层级的服务。这违反了业务平台 4 层依赖模型（Layer 0 MasterData → Layer 1 → Layer 2 → Layer 3），形成循环依赖风险。

**修复方向**: 审查每个 HTTP 客户端的实际调用，将不必要的直接依赖改为通过事件消费或共享契约间接获取数据。

---

### 11. 服务间 HTTP 调用硬编码 BaseUrl (#224)

**严重度**: Low
**文件**: 各 Gateway 和服务的 `Program.cs`

**问题**: 所有服务间 HTTP 调用使用 `Configuration["ServiceName:BaseUrl"] ?? "http://localhost:port"` 模式，依赖静态配置。无服务发现机制意味着横向扩缩容需要手动更新配置和重启网关，且无法实现客户端负载均衡。

**修复方向**: 当前阶段作为已知技术债务记录。短期可引入基于 Aspire 的服务发现；长期可集成 Consul/etcd 等服务注册中心，或 Kubernetes DNS-based 服务发现。不阻塞 MVP。

---

### 12. BusinessGateway 非幂等操作缺少安全弹性策略 (#225)

**严重度**: High
**文件**: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Program.cs`

**问题**: BusinessGateway 的 6 个 HTTP 客户端全部使用 `AddStandardResilienceHandler()`。该策略包含自动重试（默认 3 次指数退避），对于非幂等写操作（如 MES 工单创建、生产报工、库存移动、质量检验记录创建）极其危险——网络超时后重试可能导致重复记录。

**对比**: PlatformGateway 已正确区分弹性策略：
- 幂等读操作（AppHub 查询、IAM 授权检查）→ `AddStandardResilienceHandler()`（含重试）
- 非幂等写操作（Ops 任务创建、IAM 登录/管理、通知发送）→ `AddGatewayNonIdempotentSafeResilience()`（仅 timeout + circuit breaker，无重试）

**BusinessGateway 当前状态**:
| HTTP 客户端 | 当前策略 | 包含写操作 | 风险 |
| --- | --- | --- | --- |
| IBusinessGatewayAuthorizationClient (IAM) | StandardResilience | 否（授权检查） | 低 |
| IBusinessMasterDataClient | StandardResilience | 是（SKU 创建等） | 高 |
| IBusinessInventoryClient | StandardResilience | 是（库存移动、盘点） | 高 |
| IBusinessQualityClient | StandardResilience | 是（检验记录、NCR） | 高 |
| IBusinessProductEngineeringClient | StandardResilience | 是（ECO/ECN 创建） | 高 |
| IBusinessMesClient | StandardResilience | 是（工单、报工、完工） | **极高** |

**修复方向**:
1. 将 PlatformGateway 的 `GatewayHttpClientResilience.cs` 提取到共享位置或在 BusinessGateway 中复制相同实现。
2. 对 5 个含写操作的业务服务客户端改用 `AddGatewayNonIdempotentSafeResilience()`。
3. `IBusinessGatewayAuthorizationClient` 保持 `AddStandardResilienceHandler()` 不变（纯读操作）。
4. 添加对应的单元测试（参考 PlatformGateway 的 `GatewayHttpClientResilienceTests.cs`）。

## 执行优先级建议

详见下方「issue 执行顺序」章节。

### Tier 1: 安全和数据完整性（立即修复）

修复后可防止数据损坏和安全漏洞，应在任何新功能开发前完成。

1. **#214** — Refresh token 竞态（安全漏洞，可被利用进行会话劫持）
2. **#221** — 权限缓存 key + 匿名失效端点（权限绕过 + DoS 攻击面）
3. **#216** — 编号重复竞态（多实例部署下数据完整性风险）
4. **#225** — BusinessGateway 非幂等重试（工单/报工重复风险）

### Tier 2: 正确性和可靠性（尽快修复）

修复后改善系统正确行为，应在当前迭代内完成。

5. **#215** — 多组织 membership 错误（功能正确性）
6. **#218** — 登录锁定缺失（暴力破解防护）
7. **#222** — InMemory 消息生产环境检查（事件丢失防护）

### Tier 3: 性能和可维护性（计划修复）

不阻塞当前功能，但应在下一个维护窗口安排。

8. **#219** — LINQ ToLower 索引失效（性能优化）
9. **#217** — 计数器内存泄漏（长期运行稳定性）
10. **#220** — 编号服务代码重复提取（可维护性，建议在修 #216/#217 后一并处理）

### Tier 4: 架构改进（长期规划）

11. **#223** — Scheduling 分层违规（依赖模型治理）
12. **#224** — 服务发现机制（部署运维改进，不阻塞 MVP）
