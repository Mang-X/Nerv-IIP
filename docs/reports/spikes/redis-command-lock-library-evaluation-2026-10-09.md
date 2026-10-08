# Redis 命令锁库净替代评估（2026-10-09）

## 结论与边界

**no-go：本次保留现有实现。** `DistributedLock.Redis` 是可行的 Redis 锁机制候选，NetCorePal 也提供匹配现有接口的 wrapper；阻断本次迁移的是可控时间合同，不能归因为没有成熟库。固定版本的获取超时、续期和丢锁监测使用系统 timer / `Stopwatch`，没有公开时钟或等价调度 seam。薄 adapter 无法让现有确定性时间测试验证候选实际循环；用 fake 替代整个候选只证明 fake，复制旧循环或 fork 上游则重新承担要删除的责任。

本报告依据 [#4164 获批规格 2026-10-07-r2](https://github.com/Mang-X/Nerv-IIP/issues/4164)，以 `ae45b4cd07da1f051b201b1ea7f742f36a730ca2` 为仓库调查基线。Scope M，单票单 PR，产物只有调查报告及报告入口。该判断只约束本次 Spike，不建立永久选型禁令；本票不发布后续迁移票。

实际证据是官方固定源码与 NuGet 包 metadata 阅读，没有候选运行实验。no-go 已有直接源码证据，按规格在此结束调查，不为了得到 go 扩展真实 Redis 或生产原型。真实 Redis 正确性、真实命令管道兼容和性能均未验证。

## 固定版本与来源

以 NuGet `.nuspec` 的 `repository commit` 固定各包源码，避免把仓库最新 release 的源码误称为旧包实现。此次下载并读取三个固定包的 `.nuspec`；未向生产项目添加引用或更改 lockfile。

| 对象 | 固定版本 / 源码 | 维护、许可证与兼容范围 |
| --- | --- | --- |
| 当前实现 | [CommandLocking.cs](https://github.com/Mang-X/Nerv-IIP/blob/ae45b4cd07da1f051b201b1ea7f742f36a730ca2/backend/common/DistributedLocking/Nerv.IIP.DistributedLocking/CommandLocking.cs)，[项目](https://github.com/Mang-X/Nerv-IIP/blob/ae45b4cd07da1f051b201b1ea7f742f36a730ca2/backend/common/DistributedLocking/Nerv.IIP.DistributedLocking/Nerv.IIP.DistributedLocking.csproj)、[版本 producer](https://github.com/Mang-X/Nerv-IIP/blob/ae45b4cd07da1f051b201b1ea7f742f36a730ca2/backend/Directory.Packages.props)、[TFM producer](https://github.com/Mang-X/Nerv-IIP/blob/ae45b4cd07da1f051b201b1ea7f742f36a730ca2/backend/Directory.Build.props) | net10.0，NetCorePal abstractions 3.3.0，StackExchange.Redis 2.10.1；自维护通用机制 |
| NetCorePal 候选 | [NetCorePal.Extensions.DistributedLocks.Redis 3.3.0](https://www.nuget.org/packages/NetCorePal.Extensions.DistributedLocks.Redis/3.3.0)，源码 `79e7768ba414e3a720b7d2255d95a5fea1f42d75` | MIT；包直接提供 net10.0，abstractions >=3.3.0；Redis 底层依赖 >=1.0.3，不代表已固定为最新版。为了和直接候选比较，本报告考察显式固定 Redis 1.1.1 / Core 1.0.9 的组合。3.3.0 发布于 2026-03-25；查询时已有 [3.4.0 release](https://github.com/netcorepal/netcorepal-cloud-framework/releases/tag/v3.4.0)，本次不升级现有 NetCorePal 消费边界 |
| 直接候选 | [DistributedLock.Redis 1.1.1](https://www.nuget.org/packages/DistributedLock.Redis/1.1.1)，源码 `338025c469c13ba6b4d569344956752707b35a8c`（tag 2.7.1） | MIT；提供 netstandard2.0 / 2.1 和 net462。net10.0 的兼容来自 TFM 兼容性，不是本次构建/运行证明；依赖 StackExchange.Redis >=2.7.33，现有 2.10.1 满足版本下界；Core 范围为 >=1.0.8 且 <1.1.0。Redis 1.1.1 发布于 2025-10-26；库家族仍有 [2.8.3 release](https://github.com/madelson/DistributedLock/releases/tag/2.8.3)（2026-07-15），不是停更证据 |
| 共同底层 | [DistributedLock.Core 1.0.9](https://www.nuget.org/packages/DistributedLock.Core/1.0.9)，源码 `231a8c7821f6ec255e4c7ffc3bf2badb75166628`（tag 2.8） | MIT；net8.0 / netstandard2.0 / 2.1 / net462。1.0.9 发布于 2026-01-18；满足 Redis 1.1.1 的依赖范围。net10.0 可选 net8.0 asset，仍未实测 restore/build |

维护状态只说明这些日期可见的发布活动，不承诺未来支持。上述两个候选不是两个独立 Redis 算法：NetCorePal wrapper 委托给 DistributedLock。兼容信息属于 package / 源码层；未运行依赖恢复、编译、运行或漏洞审计，不把版本范围满足写成集成通过。

## 能力与默认值矩阵

| 能力 | 当前 | NetCorePal 3.3.0 wrapper | 直接 Redis 1.1.1 / Core 1.0.9 | 本次裁决 |
| --- | --- | --- | --- | --- |
| 消费边界 | NetCorePal `IDistributedLock` / `ILockSynchronizationHandler` | 原生匹配；转发 `HandleLostToken` 与 Dispose | Medallion 类型，需要接口 adapter | 接口桥接可行，不能据此推断语义等价 |
| 默认 acquire timeout | null → 30 秒 | null 原样传递，底层默认无限 | 默认无限；adapter 可显式 null → 30 秒 | 不采用库默认值 |
| 租约 / 续期 / 重试 | 2 分钟 / 30 秒 / 50 毫秒 | 每次直接构造锁，无 options 参数 | 可显式 `Expiry(2m)`、`ExtensionCadence(30s)`、`BusyWaitSleepTime(50ms,50ms)`；默认是 30s / 9s / 随机 10–800ms | wrapper 无法表达基线；直接候选可以配置数值，但不能据此宣称时序等价 |
| 最小有效租期 | 无候选式最小有效期预算 | 继承底层 | 默认租约的 90%；2m expiry 对应 108s min validity、单次 acquire/extend 预算 12s；可配置但必须显式裁决 | 新约束需独立说明，不静默继承 |
| 可控时间 | 注入 `TimeProvider`，获取重试和续期使用其 timer；fake store 使用其 UTC | 无时钟参数 | options 只有时长；内部 timer / Stopwatch 不可注入 | **阻断**：无法保留确定性 seam |
| token ownership | SET NX；续期/释放 Lua 比较 token | 继承底层 | SET NX；续期/释放 Lua 比较 lock ID | 源码机制匹配；过期换 owner 的真实反例未运行 |
| key | session → service → logical key | 原样 logical key，不加 namespace | `RedisKey` 原样使用，不散列/转义 mutex key | namespace 必须由本仓构造；不能直接接 `AddRedisLocks` |
| multiplexer / 缺 Redis | 重用已注册连接；生产 fail-fast；Testing / Development fallback | DI 可重用连接，但不拥有本仓环境策略 | 可接受已有 `IDatabase` | 注册与环境策略保持本仓责任 |
| 多键与异常 | 去重、最短 timeout、Ordinal 获取、逆序完整释放、聚合 | 单锁 wrapper，不接管本仓编排 | 不使用库的批量获取替换本仓编排 | 保留现有 behavior |

默认值与 key 依据：[NetCorePal RedisLock](https://github.com/netcorepal/netcorepal-cloud-framework/blob/79e7768ba414e3a720b7d2255d95a5fea1f42d75/src/NetCorePal.Extensions.DistributedLocks.Redis/RedisLock.cs)、[NetCorePal DI](https://github.com/netcorepal/netcorepal-cloud-framework/blob/79e7768ba414e3a720b7d2255d95a5fea1f42d75/src/NetCorePal.Extensions.DistributedLocks.Redis/ServiceCollectionExtensions.cs)、[Redis options](https://github.com/madelson/DistributedLock/blob/338025c469c13ba6b4d569344956752707b35a8c/src/DistributedLock.Redis/RedisDistributedSynchronizationOptionsBuilder.cs)、[Redis mutex](https://github.com/madelson/DistributedLock/blob/338025c469c13ba6b4d569344956752707b35a8c/src/DistributedLock.Redis/RedisDistributedLock.cs)、[默认 acquire API](https://github.com/madelson/DistributedLock/blob/338025c469c13ba6b4d569344956752707b35a8c/src/DistributedLock.Redis/RedisDistributedLock.IDistributedLock.cs)。

## 硬阻断的直接证据

固定 Core 1.0.9 的 [BusyWaitHelper](https://github.com/madelson/DistributedLock/blob/231a8c7821f6ec255e4c7ffc3bf2badb75166628/src/DistributedLock.Core/Internal/BusyWaitHelper.cs#L56) 用 `CancellationTokenSource` 定时构造和 `CancelAfter` 管获取预算，[SyncViaAsync](https://github.com/madelson/DistributedLock/blob/231a8c7821f6ec255e4c7ffc3bf2badb75166628/src/DistributedLock.Core/Internal/SyncViaAsync.cs#L74) 用系统 `Task.Delay` 管重试。[LeaseMonitor](https://github.com/madelson/DistributedLock/blob/231a8c7821f6ec255e4c7ffc3bf2badb75166628/src/DistributedLock.Core/Internal/LeaseMonitor.cs#L59) 以 `Task.Run` 启动监测，使用 `Stopwatch.StartNew` / `Elapsed` 和系统 `Task.Delay`；Redis 1.1.1 的 [TimeoutTask](https://github.com/madelson/DistributedLock/blob/338025c469c13ba6b4d569344956752707b35a8c/src/DistributedLock.Redis/TimeoutTask.cs#L14) 也使用系统 `Task.Delay`。公开构造器、options 和 Handle API 没有时钟、timer factory 或可替换 scheduler。

这已足以否决薄 adapter：现有 `Redis_distributed_lock_acquire_timeout_uses_controlled_time` 在目标 timer 注册后推进 fake 1 秒，能完成原实现的竞争超时；把同一个 fake 注入外围 adapter 不会推进候选的内部定时源。续期也没有可以由 fake Advance 驱动的内部 timer。将 timeout 改成外围 fake cancellation 还会把 helper timeout 变成候选 caller cancellation，并且仍不控制候选续期和 Stopwatch。把库 options 的时长设小只能缩短真实等待，不能形成可证明的确定性 seam。

以上是源码推导，**没有声称实际跑过该候选反例**。规格明确允许官方证据直接 no-go，因此未做真实 sleep、反射修改私有状态、全局 patch 或 fork。依据仍是本票必须保留的可测试性，而非给所有 Redis transport 注入假时钟的要求：真实外部可见性仍应使用真实时钟与有界 Eventually。

## 合同逐项映射

合同来源为 #4164 spec；当前 producer 为 [CommandLocking.cs](https://github.com/Mang-X/Nerv-IIP/blob/ae45b4cd07da1f051b201b1ea7f742f36a730ca2/backend/common/DistributedLocking/Nerv.IIP.DistributedLocking/CommandLocking.cs)，既有观察点为 [MaintenanceCommandLockTests](https://github.com/Mang-X/Nerv-IIP/blob/ae45b4cd07da1f051b201b1ea7f742f36a730ca2/backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/MaintenanceCommandLockTests.cs)。源码合同映射不等于测试已执行。

| 合同 / 输入状态 | 当前 observable / 既有 seam | 候选映射与显式裁决 |
| --- | --- | --- |
| 重复 key、同 key 不同 timeout | behavior 去重并取最短 timeout；去重由 `Command_lock_behavior_acquires_distinct_keys_in_ordinal_order_and_releases_in_reverse` 覆盖，最短 timeout 本次只读代码，未找到专门断言 | 保留 behavior；不交给库批量 API，不新写一套较弱排序测试 |
| key 顺序相反 | Ordinal 获取、逆序释放；`Command_lock_behavior_acquires_distinct_keys_in_ordinal_order_and_releases_in_reverse` | adapter 每次只获取一个完整 Redis key；本仓仍拥有多键顺序 |
| 后续获取失败 | 已有 handle 全部逆序 Dispose，handler 不运行；`Command_lock_behavior_releases_partially_acquired_keys_when_later_acquisition_fails` 同时覆盖失败与取消 | behavior 保持；候选内部单锁获取失败的释放另有 fire-and-forget 路径，不能把返回失败等同于 Redis cleanup 已读回 |
| 获取期间 caller cancellation | 当前重试 `Task.Delay` 接 caller token；**Redis store 忽略 token**，已发网络操作不因此中断 | 候选获取前检查 token，等待也接 token；[RedLockAcquire](https://github.com/madelson/DistributedLock/blob/338025c469c13ba6b4d569344956752707b35a8c/src/DistributedLock.Redis/RedLock/RedLockAcquire.cs#L34) 取消未完成 Redis 操作时安排完成后释放。不是底层 socket cancellation。已取消且 key 空闲时，当前 store 可能仍获取成功，候选则先抛取消；这是当前差异，不偷换为当前已完整支持取消 |
| caller token + 任一 HandleLostToken | behavior 组合所有 token 传 handler；`Maintenance_command_lock_behavior_cancels_handler_when_lease_is_lost` | 原样转发候选 `HandleLostToken`；取消原因仍由 caller / handle loss 拥有，不捕获后改成 timeout。候选的完整获取/持有对照未运行 |
| renewal false / throw / pending | 当前 false 或实际异常返回时告警并取消 HandleLostToken；logger 不记录持有 token；pending 网络操作不由本地 lease timer 强行中断 | [RedLockExtend](https://github.com/madelson/DistributedLock/blob/338025c469c13ba6b4d569344956752707b35a8c/src/DistributedLock.Redis/RedLock/RedLockExtend.cs) 把失败/故障结果视为 Lost；单次 extension 预算耗尽为 Unknown，LeaseMonitor 继续到本地 lease lifetime 超限；时间与故障处理不能仅用相同 cadence 证明等价 |
| 丢锁日志与异常可见性 | rejection 带 key，throw 记录原异常；既有 `Redis_distributed_lock_logs_lock_key_when_renewal_is_rejected` / `Redis_distributed_lock_logs_exception_when_renewal_throws` | 候选公开 HandleLostToken 不携带原 renewal exception。可以补 key 的脱敏告警，但不能仅靠 token 恢复异常类型/原因。保留原异常告警需要更深 seam，未实现；不宣称日志合同通过 |
| 正常 Dispose 与重复 Dispose | 停止续期，正常释放不主动触发 HandleLostToken；重复 Dispose 无再次释放 | [RedisDistributedLockHandle](https://github.com/madelson/DistributedLock/blob/338025c469c13ba6b4d569344956752707b35a8c/src/DistributedLock.Redis/RedisDistributedLockHandle.cs) 交换 inner handle 后 Dispose，重复调用无操作；正常 LeaseMonitor Dispose 不调用丢锁取消。候选 Dispose 后再次读取 HandleLostToken 会抛 ObjectDisposedException，adapter 必须在持有时获取 token；管道当前在 Dispose 前结束 linked-token scope，无需扩大合同到 Dispose 后重新读取 token |
| 单 release exception 类型 | behavior 用 ExceptionDispatchInfo 原样抛单异常；`Command_lock_behavior_preserves_a_single_release_exception_type` | 单 Redis 的 [RedLockRelease](https://github.com/madelson/DistributedLock/blob/338025c469c13ba6b4d569344956752707b35a8c/src/DistributedLock.Redis/RedLock/RedLockRelease.cs#L55) 也会包成 AggregateException。薄 adapter 需仅在单一 inner exception 时解包并保留 stack；直接转发 wrapper 不满足类型合同 |
| handler exception + 多 release failure | behavior 收集 handler 和全部逆序释放异常；`Command_lock_behavior_aggregates_handler_then_release_failures_without_masking_either` 与 `Command_lock_behavior_attempts_every_reverse_release_and_stops_all_renewals_when_releases_fail` | 保留 behavior 聚合；不能让单锁库的 AggregateException 遮蔽原始 release 类型，不能在首次释放失败时提前停止 |
| A 过期、B 已接管、A release / renew | 当前 Lua 原子比较 token；InMemory store 同样比较 token 与过期时点 | [RedisMutexPrimitive](https://github.com/madelson/DistributedLock/blob/338025c469c13ba6b4d569344956752707b35a8c/src/DistributedLock.Redis/Primitives/RedisMutexPrimitive.cs#L17) 原子比较 lock ID 后删除/续期。仅源码匹配；两个客户端真实反例、断连、续租拒绝与 cleanup 读回未执行 |
| namespace、fallback、连接复用 | session 前缀规范化，service Trim/ToLowerInvariant，再 logical key；拒绝前缀 whitespace/control；生产缺 Redis fail-fast | `RedisDistributedLock.Key` 精确使用传入 RedisKey。保留现有前缀、fail-fast、Testing/Development fallback、已有 multiplexer；不引入多节点拓扑或 fencing。隔离与日志安全未做候选运行审计 |

共享注册和 behavior 当前有 5 个 Program 消费者：Maintenance、Wms、Mes、Quality、IndustrialTelemetry。检索命令为 `rg -n 'AddNervIipCommandLocking|NervIipCommandLockBehavior' backend/services --glob 'Program.cs'`。因此后续迁移不能只用 Maintenance fake tests 声称覆盖所有服务；本次均未修改。

## 责任量化与最薄集成形状

计数口径为基线文件中的物理行，含空白与花括号；这不是性能或实际删除量。`CommandLocking.cs` 共 527 行：注册 16–71（56 行）、命令 behavior 73–191（119 行）、RedisCommandDistributedLock 193–375（183 行）、store interface 377–384（8 行）、Redis store 386–462（77 行）、InMemory store 464–527（64 行），其余 20 行为 using / namespace / 段间空白。

| 类别 | 量化 | 净替代裁决 |
| --- | --- | --- |
| 可委托库的底层责任 | 6 项：获取重试、持有 ID、SET NX、续租 Lua、释放 Lua、后台续期/丢锁监测；Redis lock + Redis store 的整体改写上限为 **260 行**（183+77） | 260 包含必须重写的 key namespace 与接口桥接，不能全算净删除；527 更不能算删除收益。若为了原 fake 测试继续保留旧 Redis lock 循环，183 行也不能计为已删除 |
| 必须保留本仓责任 | 3 段及 store interface / **247 行**（56+119+64+8）：注册与环境策略、命令多键/异常/token 编排、现有可控时间 fake store 与其接口；Redis store 中 namespace 逻辑还需提取保留 | 5 个服务使用同一集成；不改业务 key 与互斥范围 |
| 最薄直接 adapter | 至少 4 个 acquire 方法 + 1 个 handle（HandleLostToken / Dispose / DisposeAsync）；4 个显式默认/时长设置；1 处 namespace 构造；1 处单释放异常解包 | 这是结构估算，未实施、未按估算 LOC 扣减。桥接不包含旧重试/续期循环；做到这些仍过不了时间与原 renewal exception seam |
| 新依赖 / config / runtime | 直接候选新增 2 个包身份：Redis 1.1.1、Core 1.0.9；NetCorePal 路线再多 1 个 wrapper 包。现有 Redis / abstractions / DI 复用；不新增 Redis 实例、公共配置键或环境开关 | 需要固定依赖及升级 owner；候选 min validity / operation budget 属新增语义责任，不能只减代码不计成本 |
| 测试与运维责任 | 至少 2 层：原命令管道确定性合同、真实 Redis 候选 ownership/竞争/失效/隔离/cleanup；原日志故障诊断另需 seam | 时间 seam 缺失时不能用真实 sleep 替代；fake 原实现也不能作为候选后台循环证据 |
| 本次实际净变化 | 生产代码删除 **0 行**、新增运行依赖 **0 个**、运行配置变化 **0 项** | no-go，不提交不可验证的 adapter，也不建立上游 fork |

最薄形状应是：现有 behavior → NetCorePal `IDistributedLock` adapter（key 前缀、显式时长、单异常桥接）→ 单 `IDatabase` 的候选 mutex → 现有 handler；候选 handle 的 loss token 返回给 behavior。NetCorePal 自带 wrapper 少写接口桥接，但既不开放 options，也不解包 release 异常，因此不是本仓可直接替换的最薄合格实现。

当固定版本已缺少强制 seam 时，理论底层委托收益不能认定为净收益。增加测试专用实现并保留旧循环会让同一生产合同拥有两套实现，fork 内部 timer 则增加第三方同步维护；均超出本票目标，故本次净替代收益不可成立。

## 复核方式与未运行项

本次实际取得的版本 metadata 可用以下只读步骤复核；临时目录不进入生产构建，也不产生 Redis 数据：

```sh
mkdir -p /tmp/issue4164-packages
curl -fsSL https://api.nuget.org/v3-flatcontainer/distributedlock.redis/1.1.1/distributedlock.redis.1.1.1.nupkg -o /tmp/issue4164-packages/redis.nupkg
curl -fsSL https://api.nuget.org/v3-flatcontainer/distributedlock.core/1.0.9/distributedlock.core.1.0.9.nupkg -o /tmp/issue4164-packages/core.nupkg
curl -fsSL https://api.nuget.org/v3-flatcontainer/netcorepal.extensions.distributedlocks.redis/3.3.0/netcorepal.extensions.distributedlocks.redis.3.3.0.nupkg -o /tmp/issue4164-packages/netcorepal.nupkg
unzip -p /tmp/issue4164-packages/redis.nupkg DistributedLock.Redis.nuspec
unzip -p /tmp/issue4164-packages/core.nupkg DistributedLock.Core.nuspec
unzip -p /tmp/issue4164-packages/netcorepal.nupkg NetCorePal.Extensions.DistributedLocks.Redis.nuspec
```

源码调查先读取官方 tag 2.8.3，再用包内 commit 回核 Redis 1.1.1 的 tag 2.7.1 与 Core 1.0.9 的 tag 2.8；上述被引用的 Redis / Core 时间、mutex、释放文件在各自包 commit 和 2.8.3 间无 diff。正式证据链接仍指向包 commit，不要求依赖这一跨版本一致性推断。

| 证据层 | 本次实际状态 | 可证明范围 |
| --- | --- | --- |
| 源码 / 包 metadata | 已读取固定包 `.nuspec`、官方源码、维护发布页、当前生产 producer 和既有合同测试 | 版本、许可证、声明依赖/TFM、内部时间驱动与合同差异；满足 no-go 的官方证据条件 |
| fake / 确定性合同 | 只读现有测试，未执行 | 原 seam 的结构；不声称本次测试通过或候选可被 fake 驱动 |
| 真实 Redis | 未运行；包括两客户端竞争、过期换 owner、旧 owner 释放/续期、断连/拒绝、namespace 和 cleanup 读回 | 无 Redis 实验证据，不给生产迁移通过结论 |
| 真实业务管道 | 未运行候选 adapter / MediatR 对照，未启动服务 | 无端到端候选兼容证据 |
| build / restore / CI / FullChain | 未运行候选 build/restore、后端回归、Redis/CAP、FullChain 或性能；文档门禁与 PR CI 结果留在 PR | 文档门禁不能代替这些运行边界 |

未来只有在候选提供可注入时间/可证明等价 seam，或用户另行批准变更可测试性合同后，才有重开迁移评估的理由；届时仍须验证实际 release 异常、日志诊断、取消差异和真实 Redis 反例。此处不是迁移 go，不提交迁移范围、回滚 PR 或未获确认的后续票。
