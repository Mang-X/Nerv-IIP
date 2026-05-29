# 缓存基线说明

本文档定义 Nerv-IIP 后端服务的应用级缓存基线，承接 ADR 0003 的 Redis 与 FusionCache 决策。目标是在不破坏领域事实边界的前提下，为高频读、聚合查询和权限快照提供统一、可观测、可失效的缓存能力。

参考来源：

- FusionCache：https://github.com/ZiggyCreatures/FusionCache
- FusionCache Redis integration：https://redis.io/docs/latest/integrate/fusioncache/
- ASP.NET Core HybridCache：https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid

## 定位

1. FusionCache 是 Nerv-IIP 的默认应用级缓存库。
2. Redis 是默认 L2 distributed cache，也是多实例部署时的 backplane 基线。
3. 进程内 L1 缓存只用于提升读性能，不是跨实例一致性的事实来源。
4. 缓存只优化读取，不承载命令事务、审计、动作生命周期或实例最终状态。
5. 所有服务通过 backend/common/Caching 暴露的统一注册和策略使用 FusionCache，不在业务服务里各自散落配置。

## 首批适用场景

优先用于：

1. PlatformGateway 的实例列表、实例详情和页面级聚合查询。
2. AppHub 的应用目录、能力清单和只读投影。
3. IAM 的权限码、角色权限快照、外部客户端只读信息和组织环境上下文。
4. FileStorage 的只读文件元数据和短 TTL 下载授权校验辅助数据。
5. 配置字典、枚举映射、低频变化的系统元数据。
6. Knowledge 检索中的来源元数据、权限过滤辅助数据和引用回显元数据。

暂不用于：

1. OperationTask、OperationAttempt、AuditRecord 的真实状态。
2. refresh token、session revoke list、一次性凭证和高风险授权结果的唯一存储。
3. ApplicationInstance 的最终 reported state 和 state history 真相源。
4. StoredFile、FileVersion、UploadSession、DownloadGrant 的唯一事实来源。
5. 需要严格读己之写语义的命令侧校验。

## 配置基线

backend/common/Caching/Nerv.IIP.Caching 负责提供以下能力：

1. FusionCache 服务注册扩展方法。
2. Redis L2 distributed cache 配置。
3. Redis backplane 配置，用于多实例 L1 失效同步。
4. System.Text.Json 序列化配置。
5. FusionCache OpenTelemetry 集成。
6. 默认 entry options，包括短 TTL、软超时、硬超时和 fail-safe 边界。
7. 缓存键和 tag 的命名辅助方法。

首批服务不直接引用 StackExchange.Redis 或 FusionCache backplane 细节；除 common/Caching 外，业务服务只消费统一的缓存抽象或扩展。

## 键与 Tag 规则

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
2. key 必须包含 schemaVersion，缓存响应结构变更时通过版本号自然隔离旧数据。
3. tag 按服务、组织、环境、资源类型和资源 ID 分层设计，例如 `apphub`、`org:org-001`、`env:env-prod`、`instance:inst-456`。
4. 权限相关缓存必须包含用户、角色或外部客户端范围，避免跨主体污染。

## 失效策略

1. 命令成功提交后，优先按 tag 失效相关读侧缓存。
2. AppHub 注册、心跳和状态快照写入后，失效对应实例、节点、应用和列表缓存。
3. IAM 用户、角色、权限或授权授予变化后，失效对应用户、角色、外部客户端和权限快照缓存。
4. Gateway 聚合缓存的 TTL 必须短于其聚合来源中最短的业务容忍时间。
5. 对安全敏感缓存，禁止长时间 fail-safe；权限变更后必须主动失效，不能只等待 TTL。
6. PlatformGateway 的 `/internal/gateway/cache/invalidate` 只允许 InternalService 调用，并只失效当前进程内 `gateway:` 前缀缓存；在 Redis L2/backplane 接入前，它不是多实例广播失效机制。

## 一致性边界

1. 缓存命中不代表事实已永久成立，命令侧必须回到领域服务或数据库事实做最终判断。
2. 权限缓存只可作为读侧加速；执行类接口必须保留服务端授权校验。
3. 运维动作、审计记录和实例状态历史必须先写入真实存储，再触发缓存失效。
4. 不能通过共享 Redis key 实现跨服务业务协作；跨服务事实传播仍以集成事件或明确 API 为准。

## 首批验收标准

1. backend/common/Caching 可以被 AppHub、Iam、PlatformGateway 引用。
2. 本地或测试环境可用同一 Redis 同时作为 L2 cache 和 backplane。
3. AppHub 或 Gateway 至少有一个读侧查询通过 FusionCache 缓存，并具备测试覆盖。
4. 对应写操作能主动失效缓存，重复查询能返回更新后的事实。
5. OpenTelemetry 中能观察到缓存命中、未命中、失败或降级行为。
