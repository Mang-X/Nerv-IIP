# 缓存基线说明

本文档定义 Nerv-IIP 后端服务的应用级缓存基线，承接 ADR 0003 的 Redis 与 FusionCache 决策。目标是在不破坏领域事实边界的前提下，为高频读、聚合查询和权限快照提供统一、可观测、可失效的缓存能力。

参考来源：

- FusionCache：https://github.com/ZiggyCreatures/FusionCache
- FusionCache Redis integration：https://redis.io/docs/latest/integrate/fusioncache/
- ASP.NET Core HybridCache：https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid

## 定位

1. FusionCache 是 Nerv-IIP 的默认应用级缓存库。
2. Redis 是默认的 L2 分布式缓存，也是多实例部署时的 backplane（失效通知总线）基线。
3. 进程内 L1 缓存只用于提升读性能，不是跨实例一致性的事实来源。
4. 缓存只优化读取，不承载命令事务、审计、动作生命周期或实例最终状态。
5. 所有服务通过 backend/common/Caching 暴露的统一注册和策略使用 FusionCache，不在业务服务里各自散落配置。

## 当前 provider 实现

`AddNervIipCaching` 注册公共 `IAppCache`，由 `FusionAppCache` 封装 FusionCache L1 和按配置选择的 Redis L2/backplane。它使用独立的 `MemoryCache`，`Caching:L1MaxEntries` 默认 `10000`，必须大于零；预算单位为业务缓存项，每项 `Size=1`，库固定 tag 元数据 `Size=0`。容量准入与压缩由 MemoryCache 负责，满额时 factory 的结果仍正常交付；预算不表示字节数，也不承诺精确 LRU。

当前四个读取点为 PlatformGateway 实例列表/详情（5 秒）与双网关读授权（配置值，默认 10 秒）。授权键保留 token、组织、环境、权限、资源、主体 context 和 schema 版本的现有隔离；`RealtimeRequired` 始终实时向 IAM 校验。所有这些场景关闭 fail-safe、eager refresh 和超时 factory 后台完成，不配置 factory 软/硬超时。

PlatformGateway 的实例读取与读授权使用固定 `gateway` tag。内部失效端点调用 `RemoveByTag`，只使该家族下次读取重新加载，不清除其他家族。`Clear` 则使同一逻辑缓存身份下的 L1/L2 失效。库 tag 失效采用库自身时间戳语义，不另建前缀键索引或清理 worker。

`Caching:Provider` 允许 `L1` 或 `Redis`。省略时，有非空 `Caching:Redis` 就选择 Redis，否则为 L1；显式 L1 与非空 Redis 配置冲突会在注册时失败。Redis 必须提供有效连接参数和部署环境：优先 `Caching:Environment`，再按 .NET host 的优先序读取 `DOTNET_ENVIRONMENT`、`ASPNETCORE_ENVIRONMENT`。缺失参数、未知 provider、无效容量均在启动时失败，错误不回显配置值。

Redis 模式使用官方 `Microsoft.Extensions.Caching.StackExchangeRedis`、FusionCache `SystemTextJson` serializer 和 Redis backplane。逻辑身份为 `nerv-iip:<escaped-service>:<escaped-environment>:json-v1`，同时作为库 `CacheName`、业务及内部 tag/clear 的 `CacheKeyPrefix`、backplane channel 前缀。库自己的 wire-format 版本仍参与 L2 key 和 channel。租户隔离沿调用方的业务 key/tag；公共 provider 不从 key 推断租户。

host 启动会实例化所选 provider，并等待官方 Redis backplane 的实际初次订阅完成。FusionCache 2.9 会捕获初次订阅失败，公共边界通过轻量委托检查订阅是否返回成功，失败则终止启动。后续传播异步到达其它实例，发布返回不代表所有订阅者已经处理；测试在真实订阅边沿后有界观察远端行为。Redis Pub/Sub 不持久保存断连期间的消息；StackExchange.Redis 负责恢复连接和订阅，但本组件关闭 FusionCache 自动重放，不能声称断连期间即时一致。恢复后的 L2 读取或原业务 TTL 到期会重建缓存事实。

业务值及 tag/clear 元数据均关闭 fail-safe，分布式读写、序列化和 backplane 异常向调用方传播；分布式与发布操作不后台执行，circuit-breaker 不跳过失败请求。不新增 factory 超时、应用重试或 provider 降级。L2 提升到 L1 使用值原有逻辑到期，不按新读取的 TTL 延寿。分布式写与通知不构成事务：失败可能已有部分 L1/L2 副作用，调用方必须将失效异常视为失败；未过期的 L1 仍可命中，不承诺故障期间的线性一致性。

观测通过宿主现有 `ILogger<FusionAppCache>` 记录命中、未命中及错误类型；不把业务 key、tag、异常正文、token 或连接串交给日志。库的完整 key 级日志未接入，专门的 FusionCache OTel exporter 接线仍未实施。仅注册缓存而没有读调用的服务不会因此增加缓存场景，也不代表业务写入失效链已经闭合。

## 首批适用场景

优先用于：

1. PlatformGateway 的实例列表、实例详情和页面级聚合查询。
2. AppHub 的应用目录、能力清单和只读投影。
3. IAM 的权限码、角色权限快照、外部客户端只读信息和组织环境上下文。
4. FileStorage 的只读文件元数据和短 TTL（生存时间）下载授权校验辅助数据。
5. 配置字典、枚举映射、低频变化的系统元数据。
6. Knowledge 检索中的来源元数据、权限过滤辅助数据和引用回显元数据。

暂不用于：

1. OperationTask、OperationAttempt、AuditRecord 的真实状态。
2. refresh token（刷新令牌）、session revoke list（会话撤销列表）、一次性凭证和高风险授权结果的唯一存储。
3. ApplicationInstance 的最终上报状态（reported state）和状态历史（state history）事实来源。
4. StoredFile、FileVersion、UploadSession、DownloadGrant 的唯一事实来源。
5. 需要严格读己之写语义的命令侧校验。

## 配置基线

backend/common/Caching/Nerv.IIP.Caching 负责提供以下能力：

1. FusionCache 服务注册扩展方法。
2. Redis L2 分布式缓存配置。
3. Redis backplane（失效通知总线）配置，用于多实例 L1 失效同步。
4. System.Text.Json 序列化配置。
5. 缓存命中、未命中和错误的现有日志集成；FusionCache 专门的 OpenTelemetry 接线仍为后续目标。
6. 默认缓存项选项（entry options）：调用方 TTL、禁用 fail-safe 与后台完成、同步分布式操作及异常传播，不另设 factory 软/硬超时。
7. 缓存键和 tag（标签）的命名辅助方法。

首批服务不直接引用 StackExchange.Redis 或 FusionCache backplane（失效通知总线）的实现细节；除 common/Caching 外，业务服务只使用统一的缓存抽象或扩展。

## 键与标签规则

缓存键必须具备服务边界和租户上下文：

```text
{service}:{scope}:{organizationId}:{environmentId}:{resource}:{id-or-query-hash}:v{schemaVersion}
```

示例：

```text
apphub:instance-list:org-001:env-prod:query:8d3f:v1
iam:permission-snapshot:org-001:env-prod:user:user-123:v1
gateway:instance-detail:org-001:env-prod:instance:inst-456:v1
```

规则：

1. 不使用原始用户输入直接拼接缓存键；查询条件需要规范化后再哈希。
2. 缓存键必须包含 schemaVersion，缓存响应结构变更时通过版本号自然隔离旧数据。
3. tag（标签）按服务、组织、环境、资源类型和资源 ID 分层设计，例如 `apphub`、`org:org-001`、`env:env-prod`、`instance:inst-456`。
4. 权限相关缓存必须包含用户、角色或外部客户端范围，避免跨主体污染。

## 失效策略

1. 命令成功提交后，优先按 tag（标签）使相关读侧缓存失效。
2. AppHub 注册、心跳和状态快照写入后，失效对应实例、节点、应用和列表缓存。
3. IAM 用户、角色、权限或授权授予变化后，失效对应用户、角色、外部客户端和权限快照缓存。
4. Gateway 聚合缓存的 TTL 必须短于其聚合来源中最短的业务容忍时间。
5. 对安全敏感缓存，禁止长时间使用 fail-safe（故障安全）数据；权限变更后必须主动使其失效，不能只等待 TTL。
6. PlatformGateway 的 `/internal/gateway/cache/invalidate` 只允许 InternalService 调用，失效固定 `gateway` tag。L1 模式只作用于当前进程；Redis 模式更新同一服务/部署环境/序列化版本的 L2 tag 标记，并通过 backplane 通知其它实例。返回成功不代表每个订阅者已处理完成，也不代表 AppHub/IAM 的写入链已接线。
7. `/internal/gateway/cache/invalidate-scope` 是独立的精准操作，JSON body 必须携带非空、无首尾空白的 `organizationId` 与 `environmentId`。它复用共享 ScopedCallerAuthentication profile 和 `internal.gateway-cache.invalidate` permission policy，两个 body 字段必须分别匹配已认证 profile 的可信 claims；用户 JWT、旧 InternalService token 或裸 scope header 均不能扩大权限。未配置 scoped callers 时新操作拒绝，旧操作仍可使用。
8. PlatformGateway 实例列表、详情和读侧授权条目同时挂 `gateway` 与 `gateway:scope:<escaped-org>:<escaped-env>` tag。精准操作只移除一个复合 tag，避免分别移除 org/env tag 形成并集误删；公共 adapter 直接把多 tag 传给 FusionCache，不解析任意业务 key，也不重建广播协议。授权 TTL 与写侧实时 IAM 检查保持原有合同。

精准操作的配置与调用见 [`../../runbooks/local-development.md#platformgateway-精准缓存失效`](../../runbooks/local-development.md#platformgateway-精准缓存失效)。HTTP 204 只表示同步提交成功；Redis 写入或通知发布异常返回 503，部分提交失败不提供跨节点回滚保证。已在途 factory 可向原请求返回旧值，但失效边沿后的后续读取须重新加载；取消不撤销已提交的同步失效，也不承诺全节点瞬时完成。

## 一致性边界

1. 缓存命中不代表事实已永久成立，命令侧必须回到领域服务或数据库事实做最终判断。
2. 权限缓存只可作为读侧加速；执行类接口必须保留服务端授权校验。
3. 运维动作、审计记录和实例状态历史必须先写入真实存储，再触发缓存失效。
4. 不能通过共享 Redis 键实现跨服务业务协作；跨服务事实传播仍以集成事件或明确 API 为准。

## 首批验收标准

1. backend/common/Caching 可以被 AppHub、Iam、PlatformGateway 引用。
2. 本地或测试环境可使用同一 Redis 同时承担 L2 缓存和 backplane（失效通知总线）。
3. AppHub 或 Gateway 至少有一个读侧查询通过 FusionCache 缓存，并具备测试覆盖。
4. 对应写操作能主动失效缓存，重复查询能返回更新后的事实。
5. OpenTelemetry 中能观察到缓存命中、未命中、失败或降级行为。
