# ADR 0015: 网关 HTTP 客户端弹性策略

- Status: Accepted
- Date: 2026-05-28

## Context

PlatformGateway 和 BusinessGateway 作为 BFF 层，聚合多个下游服务的 HTTP 调用。`Microsoft.Extensions.Http.Resilience` 提供了 `AddStandardResilienceHandler()`，包含 rate limiter → total request timeout → retry（默认 3 次指数退避）→ circuit breaker → attempt timeout 的完整管线。

对于幂等读操作（如 IAM 授权检查、AppHub 实例查询），自动重试是安全的——失败后重试不产生副作用。但对于非幂等写操作（如工单创建、生产报工、库存移动、用户管理），网络超时后重试可能导致下游服务执行两次相同的写入，产生重复记录。

PlatformGateway 已经通过 `GatewayHttpClientResilience.AddGatewayNonIdempotentSafeResilience()` 区分了两类操作。BusinessGateway 在 Business Console MVP 落地时，6 个 HTTP 客户端全部使用了 `AddStandardResilienceHandler()`，未区分读写操作的弹性需求。

## Decision

### 1. 按操作幂等性分类弹性策略

| 操作类别 | 弹性策略 | 策略内容 | 适用场景 |
| --- | --- | --- | --- |
| 幂等读操作 | `AddStandardResilienceHandler()` | retry + circuit breaker + timeout | 授权检查、列表查询、详情查询 |
| 非幂等写操作 | `AddGatewayNonIdempotentSafeResilience()` | timeout + circuit breaker（无 retry） | 创建、更新、删除、状态变更 |

### 2. 非幂等安全策略参数

```csharp
pipeline
    .AddTimeout(TimeSpan.FromSeconds(10))
    .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
    {
        FailureRatio = 0.5,
        MinimumThroughput = 10,
        SamplingDuration = TimeSpan.FromSeconds(30),
        BreakDuration = TimeSpan.FromSeconds(15)
    });
```

- **Timeout 10s**: 下游服务单次调用上限，超时直接失败返回，不重试。
- **Circuit breaker**: 30 秒窗口内至少 10 次请求，失败率超过 50% 时熔断 15 秒。熔断期间快速失败，避免雪崩。

### 3. 网关客户端策略分配

**PlatformGateway**（已实现）：

| HTTP 客户端 | 策略 | 理由 |
| --- | --- | --- |
| IAppHubClient | Standard | 实例查询，幂等 |
| IGatewayAuthorizationClient | Standard | 权限检查，幂等 |
| IGatewayOpsClient | NonIdempotentSafe | 运维任务创建，非幂等 |
| IGatewayIamAuthClient | NonIdempotentSafe | 登录/刷新/登出，非幂等 |
| IGatewayIamAdminClient | NonIdempotentSafe | 用户/角色 CRUD，非幂等 |
| IGatewayNotificationClient | NonIdempotentSafe | 通知标记已读等写操作，非幂等 |

**BusinessGateway**（需修复，见 #225）：

| HTTP 客户端 | 当前策略 | 应改为 | 理由 |
| --- | --- | --- | --- |
| IBusinessGatewayAuthorizationClient | Standard | Standard | IAM 授权检查，幂等 |
| IBusinessMasterDataClient | Standard | **NonIdempotentSafe** | SKU 创建等写操作 |
| IBusinessInventoryClient | Standard | **NonIdempotentSafe** | 库存移动、盘点等写操作 |
| IBusinessQualityClient | Standard | **NonIdempotentSafe** | 检验记录、NCR 创建等写操作 |
| IBusinessProductEngineeringClient | Standard | **NonIdempotentSafe** | ECO/ECN 创建等写操作 |
| IBusinessMesClient | Standard | **NonIdempotentSafe** | 工单创建、报工、完工入库等写操作 |

### 4. 弹性扩展点复用

`AddGatewayNonIdempotentSafeResilience()` 当前定义在 PlatformGateway 项目内。BusinessGateway 采用相同策略时，有两个选项：

- **复制**: 在 BusinessGateway 项目中创建相同的扩展方法。代码量极小（约 20 行），两个 Gateway 项目无直接引用关系，复制比共享依赖更简单。
- **共享**: 提取到 `backend/common` 下的共享包。当第三个 Gateway 出现时再考虑。

当前选择复制，保持 Gateway 项目独立。

### 5. 测试要求

每个 Gateway 必须有对应的弹性策略测试，验证非幂等客户端在收到 5xx 错误时不触发重试。参考 `GatewayHttpClientResilienceTests.Non_idempotent_gateway_clients_do_not_retry_server_errors`。

## Consequences

- BusinessGateway 的 5 个业务服务客户端改用 `NonIdempotentSafe` 策略后，写操作失败不再自动重试，避免重复记录风险。
- 写操作的可用性略有降低（单次失败即返回错误），但这是正确的权衡：重复数据比暂时失败危害更大，客户端（Business Console）可以提示用户重试。
- 后续新增 Gateway HTTP 客户端时，必须根据是否包含写操作选择对应策略，不得默认使用 `AddStandardResilienceHandler()`。
- 如果某个客户端同时包含读和写操作且需要读操作重试，应拆分为两个 HttpClient 注册（读客户端 + 写客户端），分别应用不同策略。
