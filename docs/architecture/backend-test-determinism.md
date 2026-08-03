# 后端测试确定性与隔离契约

本文定义 MAN-662 交付的共享测试时间、等待、超时、网络诊断、全局状态隔离和本地重复性验证契约。它不把一次本地通过解释为 CI 趋势、flake rate、lane timing 或 skip budget 证据。

## 时间与等待决策

| 被测行为 | 时钟与等待方式 | 判定边界 |
| --- | --- | --- |
| scheduler、lease、expiry、renewal、retention | 注入 `TimeProvider`；测试使用 `FakeTimeProvider` 推进时间 | 不等待真实时间；生产代码与 timeout/timer 必须使用同一个 provider |
| 真实 transport、进程启动、容器就绪、外部消费者可见性 | 使用 wall clock，并通过 `Eventually` 有界轮询可观察事实 | 不用一次固定 sleep 猜完成时刻；超时必须报告最后一次脱敏观测 |
| 单次可能挂起的异步操作 | 使用 `TestTimeout.RunAsync` 和显式 timeout | helper 自己触发的 timeout 与 caller cancellation 分开；caller cancellation 原样传播 |

`Eventually` 的每次观测都必须可读、稳定并脱敏。示例：

```csharp
var posted = await Eventually.WaitAsync(
    condition: "inventory movement is posted",
    observe: token => ReadMovementAsync(movementId, token),
    isSatisfied: state => state.Status == "Posted",
    describe: state => $"status={state.Status}; attempt={state.Attempt}",
    options: new EventuallyOptions(
        Timeout: TimeSpan.FromSeconds(10),
        PollInterval: TimeSpan.FromMilliseconds(100),
        SensitiveValues: [accessToken, connectionString]),
    cancellationToken);
```

```csharp
await TestTimeout.RunAsync(
    operation: $"connector handshake device={deviceCode}",
    action: token => connector.HandshakeAsync(token),
    timeout: TimeSpan.FromSeconds(5),
    cancellationToken,
    sensitiveValues: [credential]);
```

超时诊断必须保留 `condition` 或 `operation`、尝试次数、elapsed 和最后一次业务观测；显式 sensitive values 以及 password、secret、token、credential、API key、connection string、headers 和 request body 必须经 `TestDiagnostic.Sanitize` 清除。不得把完整请求头、请求体、连接串或响应体放进 describe、异常或测试输出。

## 网络结果与预算

连接 timeout 和总 request timeout 都必须由调用方显式配置为正数；测试不得用一个模糊的总时长同时冒充 DNS、连接和业务响应预算。

| 结果 | 分类 | 必须保留 | 禁止混淆或输出 |
| --- | --- | --- | --- |
| DNS 解析失败 | `NetworkFailureKind.Dns` | 脱敏的 DNS 失败类别 | 不伪装为 HTTP 503；不输出目标凭据 |
| 连接被拒绝 | `NetworkFailureKind.ConnectionRefused` | 脱敏的 refused 类别 | 不伪装为 request timeout |
| helper 自己的 request timeout | `NetworkFailureKind.RequestTimeout` | 预算、elapsed、脱敏 operation | caller cancellation 必须原样传播，不能改写成 timeout |
| 非成功 HTTP 业务响应 | `NetworkFailureKind.BusinessError` | 数字 status code 与脱敏 reason phrase | 不记录 response body 或 headers；不降级成 transport fault |

## 可变全局状态隔离矩阵

| 状态面 | 隔离方式 | 契约 |
| --- | --- | --- |
| FluentValidation global resolvers、current/default culture、`TZ` 与显式环境变量 | scoped capture/restore；所有 mutator 串行进入 `GlobalTestStateScope` | 精确区分原本不存在、空字符串和有值；`DisposeAsync` 必须恢复并释放 scope |
| 同一服务内的 host startup 与共享 host fixture | xUnit collection serialization | 同一 service 的启动/停止不得跨 collection 并发；该约束不等于整个 solution 串行 |
| FastEndpoints serializer、validation 和 discovery mutation | collection serialization **加** sacrificial process isolation | 变异只发生在一次性测试进程；进程结束即丢弃。FastEndpoints 进程静态状态不可恢复，绝不描述为 restore |

MAN-663 的命名 BusinessGateway shared-host profiles 与安全并行化仍明确 defer；本契约不提前实现或宣称该并行面。MAN-664 的 IndustrialTelemetry order-sensitive host surface 结构拆分也明确 defer；本次只把现有目标程序集纳入可复现顺序与并发 profile。

## Seeded order 与本地六轮验证

`Nerv.IIP.Testing.Xunit` 的 case/collection orderer 读取 `NERV_IIP_TEST_ORDER_SEED`；变量缺失时使用固定 `nerv-iip-default`。排序键为 `SHA-256(seed + fully-qualified-name)`，hash 相同再按 name 做 ordinal 排序。四个目标程序集只声明 orderer，不改变 collection serialization 或业务测试职责。

`pwsh scripts/verify-backend-test-determinism.ps1` 创建新的 `artifacts/test-determinism/man-662/<invocation-id>/`，执行六轮、每轮四项目：seed 固定为 `man662-01` 至 `man662-06`，serial/parallel 交替，`MaxParallelThreads` 为 1/4，项目顺序逐轮旋转。profile 只由受支持的 VSTest `<xUnit>` runsettings 设置，不通过 compile-time attribute 切换；第一轮构建后后续轮次使用同一程序集。已存在 invocation 路径会被拒绝，失败记录不能被 rerun 覆盖。

每个 `summary.json` 只含六个本地复现字段：`run`、`seed`、`profile`、`projectOrder`、`elapsedMs`、`exitCode`。MAN-662 不生成 TRX、`trxPaths`、per-test/lane timing、skip budget 或 rerun accounting。

## Quarantine 与外部证据边界

MAN-661 独占 required-lane/opt-in-lane policy、machine-readable quarantine registry 与 enforcement；MAN-662 不创建 quarantine registry 或规则。MAN-661 的定量 trend/flake evidence 是外部证据，不阻塞本变更实现、评审、合并或 code-completion。当前 baseline status 为 `awaiting MAN-661`；只能在 MAN-661 落地后用真实 artifact identifier 替换，不能填推测值。

现有 `backend/test-determinism-baseline.json` 有 89 个登记债务行，owner 均为 MAN-662，统一到期日为 2026-09-03；checker 通过只证明 inventory 与元数据吻合，不代表债务已清零。Task 4 首次 Inventory 全项目运行还观测到未改动 SourceLookup EF InMemory 排序一次失败（`ArgumentException: At least one object must implement IComparable`）；单测隔离与未改代码的全项目 rerun 随后通过，因此保留为 flake 边界而不在此修复。既有 bounded solution baseline 在 BusinessGateway 1023/1023 通过、运行 8m41s 后被中断，已完成程序集为零失败且至少 77 个 PostgreSQL 条件测试 skip，但没有最终 solution aggregate；不得把该边界写成 solution green。

## MAN-650 迁移状态

| MAN-650 项 | 状态 |
| --- | --- |
| Maintenance Redis renewal | migrated by MAN-662 |
| Inventory expiry metric | migrated by MAN-662 |
| Ops Production fake credential | migrated by MAN-662 |
| IndustrialTelemetry out-of-range/order-sensitive host surface | isolated here, structural split tracked by MAN-664 |
