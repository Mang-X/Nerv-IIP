# ADR 0015：网关 HTTP 客户端弹性策略

- 状态：已接受
- 日期：2026-05-28

## 背景

PlatformGateway 和 BusinessGateway 作为 BFF 层，会聚合多个下游服务的 HTTP 调用。`Microsoft.Extensions.Http.Resilience` 提供的标准弹性管线包含限流、总请求超时、自动重试、熔断器和单次尝试超时。

对于幂等读操作，自动重试通常不会产生新的业务副作用；对于创建、更新、删除、状态变更、登录或刷新等非幂等写操作，网络超时后的透明重试可能让下游重复执行，形成重复记录或不可判定结果。

本决策形成时，两个 Gateway 的注册方式并不一致，这暴露出需要以操作幂等性而不是项目默认值选择弹性策略。具体客户端清单、当前注册状态和修复进度会随实现变化，不属于本 ADR 的长期事实。

## 决策

### 1. 按操作幂等性分类弹性策略

| 操作类别 | 弹性策略 | 策略内容 | 适用场景 |
| --- | --- | --- | --- |
| 幂等读操作 | `AddStandardResilienceHandler()` | 重试 + 熔断器 + 超时 | 授权检查、列表查询、详情查询 |
| 非幂等写操作 | `AddGatewayNonIdempotentSafeResilience()` | 超时 + 熔断器，不自动重试 | 创建、更新、删除、状态变更、登录、刷新等 |

策略选择以客户端实际可发起的操作为准，不以客户端名称、所属 Gateway 或页面来源推断。

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

- 单次调用超时为 10 秒；超时直接失败返回，不自动重试。
- 30 秒窗口内至少 10 次请求且失败率超过 50% 时熔断 15 秒；熔断期间快速失败。

参数变化会改变可用性与重复执行风险之间的权衡，必须作为本决策的修订处理，不能仅在某个 Gateway 中静默漂移。

### 3. 客户端分配原则

1. 只包含授权检查、列表或详情查询等幂等读操作的客户端使用标准弹性策略。
2. 只要客户端能够发起非幂等写操作，就必须使用无自动重试的非幂等安全策略。
3. 同一客户端同时包含读写操作、且读操作确需自动重试时，应拆分为独立的读客户端与写客户端，分别注册策略。
4. 当前客户端到策略的完整映射由 Gateway 注册代码和对应测试证明；ADR 不维护会随实现增长的客户端清单，也不记录某项修复是否已经交付。

### 4. 弹性扩展点复用

`AddGatewayNonIdempotentSafeResilience()` 在各 Gateway 内保持本地扩展实现。该实现代码量小，两个 Gateway 无直接项目引用关系；为共享这段代码引入公共依赖的收益不足。

出现第三个 Gateway 时，必须复评是否将策略构造器提取到 `backend/common`。在复评完成前，各 Gateway 的本地实现必须由同型契约测试锁定，不能因复制而形成参数或重试语义漂移。

### 5. 测试要求

每个 Gateway 必须有弹性策略契约测试，至少证明：

1. 非幂等客户端收到 5xx 或等价可重试失败时不会自动发起第二次调用；
2. 策略参数与本 ADR 一致；
3. 新增或拆分客户端后，策略分配符合第 3 节原则。

## 当前实现入口

本节只提供导航，不声明交付状态：

- PlatformGateway producer：[`../../backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/GatewayHttpClientResilience.cs`](../../backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/GatewayHttpClientResilience.cs)
- BusinessGateway producer：[`../../backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/BusinessGatewayHttpClientResilience.cs`](../../backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/BusinessGatewayHttpClientResilience.cs)
- PlatformGateway 契约测试：[`../../backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/GatewayHttpClientResilienceTests.cs`](../../backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/GatewayHttpClientResilienceTests.cs)
- BusinessGateway 契约测试：[`../../backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayHttpClientResilienceTests.cs`](../../backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayHttpClientResilienceTests.cs)
- 当前完成度、缺陷与验证证据以关联 Issue、PR 和 CI 为准。

## 已考虑的替代方案

1. **所有网关 HTTP 客户端统一使用标准弹性管线。** 否决。非幂等写操作在超时或服务端错误后透明重试，可能让下游重复执行；对写操作而言，明确失败并由调用方基于业务幂等信息决定后续动作，比无法判定的自动重试更安全。
2. **把非幂等安全策略立即提取到共享包。** 暂不采用。当前只有两个 Gateway，代码量小且没有直接引用关系；新增共享依赖的成本高于复制。第三个 Gateway 是重新评估共享扩展点的明确触发条件。
3. **混合读写客户端统一使用无重试策略。** 允许作为保守默认，但若读操作确需重试，应按第 3 节拆分客户端，而不是为获得读重试而让写操作重新进入自动重试管线。

## 后果

1. 包含写操作的 Gateway 客户端在单次失败后直接返回错误，降低透明重复执行的风险。
2. 写操作的瞬时可用性低于自动重试方案；调用方需要展示可理解的失败，并依赖业务幂等键、查询结果或人工确认决定是否重试。
3. 新增 Gateway HTTP 客户端时必须显式判断操作幂等性，不能无条件套用标准弹性管线。
4. 本地复制的扩展实现需要同型测试约束；出现第三个 Gateway 时承担一次公共抽取复评成本。
