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

连接 timeout 和总 request timeout 都必须由调用方显式配置为正数；测试不得用一个模糊的总时长同时冒充 DNS、连接和业务响应预算。生产默认值按真实依赖的正常抖动取秒级（Ops→IAM 为 connect 5s / request 10s），毫秒级预算只由测试通过配置节覆盖；把测试预算写成发布默认值会让依赖的任何一次抖动变成 fail-closed 拒绝。

`NetworkFailureClassifier.FromException` 强制传入 caller 的 `CancellationToken`：token 已取消时原样重抛原异常（`ExceptionDispatchInfo`），只有 helper 自己的超时才归类为 `RequestTimeout`。分类器不允许有"猜"caller 意图的默认参数。

生产侧的等价分类（如 `IamOpsConnectorCredentialValidator.ClassifyTransportFailure`）**刻意不复用** `Nerv.IIP.Testing`：发布程序集不得引用测试程序集。两处的相似结构是有意的边界重复，不是待消除的 duplication；任一侧改变分类语义时必须同步另一侧和本表。

| 结果 | 分类 | 必须保留 | 禁止混淆或输出 |
| --- | --- | --- | --- |
| DNS 解析失败 | `NetworkFailureKind.Dns` | 脱敏的 DNS 失败类别 | 不伪装为 HTTP 503；不输出目标凭据 |
| 连接被拒绝 | `NetworkFailureKind.ConnectionRefused` | 脱敏的 refused 类别 | 不伪装为 request timeout |
| helper 自己的 request timeout | `NetworkFailureKind.RequestTimeout` | 预算、elapsed、脱敏 operation | caller cancellation 必须原样传播，不能改写成 timeout |
| 对端上报的 408 / 504 | `NetworkFailureKind.RequestTimeout` | 数字 status code | 不并入 `BusinessError`，否则四分法失去意义 |
| 其余非成功 HTTP 业务响应 | `NetworkFailureKind.BusinessError` | 数字 status code 与脱敏 reason phrase | 不记录 response body 或 headers；不降级成 transport fault |

## 可变全局状态隔离矩阵

| 状态面 | 隔离方式 | 契约 |
| --- | --- | --- |
| FluentValidation global resolvers、current/default culture、`TZ` 与显式环境变量 | scoped capture/restore；所有 mutator 串行进入 `GlobalTestStateScope` | 精确区分原本不存在、空字符串和有值；`DisposeAsync` 必须恢复并释放 scope |
| 同一服务内的 host startup 与共享 host fixture | xUnit collection serialization | 同一 service 的启动/停止不得跨 collection 并发；该约束不等于整个 solution 串行 |
| BusinessGateway host startup（`Nerv.IIP.BusinessGateway.Web.Tests`） | `BusinessGatewayTestHostGate` 多读单写 permit：构建独占，请求共享 | 见下方 MAN-663 一节。构建期间无请求在飞，请求期间无构建发生；程序集不再关闭并行 |
| BusinessGateway 逐测试下游 fake 与 downstream health | 逐请求 scope header 路由到租约实例，租约释放即注销 | 无「写入再重置」步骤；旧 header 显式报错，不静默回落 |
| FastEndpoints serializer、validation 和 discovery mutation | collection serialization **加** sacrificial process isolation | 变异只发生在一次性测试进程；进程结束即丢弃。FastEndpoints 进程静态状态不可恢复，绝不描述为 restore |
| JSON 全局序列化选项 | 与上一行同源：`Config.Serializer.Options` 即 FastEndpoints 进程静态状态 | 不单独提供 restore 路径；普通程序集只断言"未观测到该变异" |
| 静态缓存（`static readonly` 字典/`Lazy<T>`/`ConcurrentDictionary` memo） | 已盘点：目标程序集内无跨测试可写静态缓存；被测缓存均随 DI scope 或 `DbContext` 生命周期创建 | 新增可写静态缓存必须同时给出 scope 化方案，不得靠测试顺序回避 |
| 服务定位器（静态 `IServiceProvider` / `ServiceLocator` 单例） | 已盘点：本仓库不使用静态服务定位器，依赖一律构造函数注入 | 引入任何进程级 provider 单例前先改本表；`WebApplicationFactory` 的 provider 属于 fixture 生命周期，不是全局状态 |

隔离的两侧都有断言：`Nerv.IIP.FastEndpoints.ProcessIsolation.Tests` 证明变异确实进程级泄漏且不可恢复；`Nerv.IIP.Ops.Web.Tests` 的 `FastEndpointsStaticStateIsolationTests` 从普通 lane 反向证明该变异不可被观测。"一程序集一进程"因此是有断言支撑的结论，而不是散文。

MAN-664 的 IndustrialTelemetry order-sensitive host surface 结构拆分仍明确 defer。

## MAN-663 BusinessGateway 共享宿主 profile 与安全并行

`Nerv.IIP.BusinessGateway.Web.Tests` 原先为**每个**测试新建一个 `WebApplicationFactory<Program>`，并以
`[assembly: CollectionBehavior(DisableTestParallelization = true)]` 关闭整程序集并行。该 attribute 不是隔离
手段，只是把「每测试一宿主」这一成本掩盖成串行执行；同时存在的 `BusinessGatewayTestIsolationTests` 断言该
attribute 必须存在，因此保护的是 workaround 本身，而不是它替代的隔离性质。两者已一并移除。

**为什么原来必须串行。** 唯一的进程级危险来自宿主构建：`Program.cs` 的
`app.UseFastEndpoints(c => c.Serializer.Options.Converters.Add(...))` 写 FastEndpoints 进程静态状态。
`BusinessGatewayTestHostGate` 精确处理这一点——多读单写信号量，宿主构建独占全部 permit，网关请求各占一个
permit（客户端 `DelegatingHandler` 持有）。因此「构建时无请求在飞、请求时无构建发生」，请求之间仍完全并行。
这是真实互斥，**不宣称 FastEndpoints 静态状态可恢复**。

**profile 表。**

| profile | 设置 | 用途 | 宿主数 |
| --- | --- | --- | --- |
| `Default` | JWT（JWKS/issuer/audience）+ 抬高的 rate-limit permit | 未固定下游 base URL 的代理/授权/维护面测试、`/swagger`、`/health` | 1（整程序集） |
| `ServiceBaseUrls` | `Default` + 全部下游 base URL 固定为 `*.local` | 授权、WMS、搜索、工作台等原本调用 `BusinessGatewayTestServiceBaseUrls.Configure` 的测试 | 1（整程序集） |
| dedicated（自动回退） | 逐测试 `IWebHostBuilder` 设置 | 生产安全（`Production` 环境、缺失 CORS/base URL）、`IHttpMessageHandlerBuilderFilter` 弹性、`TimeProvider` 替换、rate limit 预算 | 按测试，仍走同一 gate |

**可变状态与 reset 机制。** 共享宿主不做「写入再重置」——没有可被遗忘的 reset 步骤：

| 状态面 | 机制 |
| --- | --- |
| 下游 fake（16 个 `IBusiness*Client`、`IBusinessGatewayAuthorizationClient`、`IInternalServiceTokenProvider`） | 每租约一个 scope id；客户端逐请求带 `X-Nerv-IIP-Test-Scope`，容器按该 header 解析实例。租约释放即注销，之后带旧 header 的请求**显式报错**而非静默回落 |
| 未被覆盖的下游 | 回落到真实注册，语义与「每测试一宿主」完全一致 |
| `BusinessGatewayDownstreamHealthState` | 唯一真正跨测试可变的单例，按租约一份；无 scope 的匿名请求（`/swagger`、`/health` 契约面）用独立实例 |
| IAM 授权缓存 | 位于 `HttpBusinessGatewayAuthorizationClient` 内，而所有测试都替换该客户端，故共享宿主上不存在该缓存面 |
| rate limiter（按 principal 分区，全部测试同一 principal） | 共享 profile 抬高 permit；限流本身由 `BusinessGatewayRateLimitTests` 在 dedicated 宿主上以自有预算覆盖（本次**新增**覆盖，此前为零） |
| NSwag 文档生成 | 并发生成会产生半填充 schema 字典；文档按 profile 生成一次后缓存复用，断言仍针对真实生成结果 |
| 无法逐请求表达的配置（builder 设置、非实例注册、名单外类型） | 由 `BusinessGatewayTestHost.Lease` 自动回退到 dedicated 宿主，正确性不依赖共享是否适用 |

**反向证明。** `BusinessGatewaySharedHostIsolationTests` 是行为断言而非 attribute 断言。三次故意注入泄漏均被
捕获：把 health state 改回单例 → 1 失败；把逐请求 scope 解析改成「最近一个租约」的共享槽 → 3 失败；让 builder
设置不再强制 dedicated 宿主 → 1 失败。

**测试分层。** 1034 个用例中约 150 个属于纯合同/元数据或直接应用行为（`*ClientTests`、`*ValidationTests`、
`WmsTrustedRequestContextTests`、`BusinessGatewayIdempotencySafetyTests` 等），本来就不启宿主，本次未改动其
路径。其余用例断言的是认证、授权、中间件、序列化与真实路由，即**必须**经过 HTTP 宿主，因此改造方向是让它们
共用宿主，而不是把它们降级成反射断言。

## Seeded order 与本地六轮验证

`Nerv.IIP.Testing.Xunit` 的 case/collection orderer 读取 `NERV_IIP_TEST_ORDER_SEED`；变量缺失时使用固定 `nerv-iip-default`。排序键为 `SHA-256(seed + display-name)`，hash 相同再按 name 做 ordinal 排序；display name 是 `ITestCase` 与 `ITestCollection` 共有的唯一稳定标识，`[Theory]` 的 display name 含参数文本但每行数据固定，排序仍然稳定。四个目标程序集通过 `SeededTestOrdering.targets` 链接同一份 assembly attribute 文件声明 orderer（不是四份副本），不改变 collection serialization 或业务测试职责。

`pwsh scripts/verify-backend-test-determinism.ps1` 先经受治理的 `New-ExclusiveInvocationClaim`（`FileMode.CreateNew`）原子取得 invocation claim，再创建新的 `artifacts/test-determinism/man-662/<invocation-id>/`，执行六轮、每轮四项目：seed 固定为 `man662-01` 至 `man662-06`，serial/parallel 交替，`MaxParallelThreads` 为 1/4，项目顺序逐轮旋转。profile 只由受支持的 VSTest `<xUnit>` runsettings 设置，不通过 compile-time attribute 切换；第一轮构建后后续轮次使用同一程序集。同一显式 invocation ID 的并发失败者在执行项目前即被拒绝，既有或失败证据都不能被 rerun 覆盖。

每个 `summary.json` 含六个本地复现字段 `run`、`seed`、`profile`、`projectOrder`、`elapsedMs`、`exitCode`，外加 `projectResults`：逐项目的 `exitCode` 与 `total/passed/skipped/failed` 计数（以 `DOTNET_CLI_UI_LANGUAGE=en` 保证解析与 locale 无关）。退出码相等**不等于**结果一致——某轮静默跳过测试时退出码仍为 0，因此验证器跨轮比对同一项目的四个计数，任一不一致即失败。MAN-662 仍不生成 TRX、`trxPaths`、per-test/lane timing、skip budget 或 rerun accounting。

## Quarantine 与外部证据边界

MAN-661 独占 required-lane/opt-in-lane policy、machine-readable quarantine registry 与 enforcement；MAN-662 不创建 quarantine registry 或规则。MAN-661 的定量 trend/flake evidence 是外部证据，不阻塞本变更实现、评审、合并或 code-completion。当前 baseline status 为 `awaiting MAN-661`；只能在 MAN-661 落地后用真实 artifact identifier 替换，不能填推测值。

现有 `backend/test-determinism-baseline.json` 有 87 个登记债务行，按 pattern 分派给三个**独立于 MAN-662 的**跟进 issue，各自到期日不同：`Task.Delay` 33 行 → `#1470`（2026-09-05）、`StaticSetter` 47 行 → `#1471`（2026-09-12）、`UnreachableAddress` 7 行 → `#1472`（2026-08-25）。owner 必须在登记它的变更合并之后依然存在——用当前 PR 自己的票做 owner，等于合并当天债务就没有责任人。`reason` 按行而非按文件书写：同一文件里结构不同的位点（例如轮询间隔与负向断言的稳定性窗口）不得共用一句解释。checker 通过只证明 inventory 与元数据吻合，不代表债务已清零。前两次六轮运行保留为 RED 历史：`20260803T192749730Z-18bd13f4bc794fbd9f054c1be2bb1410/summary.json` 的 exit 序列为 `[1,0,1,1,1,0]`，`20260803T200756805Z-929b74e11b494261941716a905a10563/summary.json` 为 `[1,1,1,0,1,1]`。其中 Task 4 首次观测的 SourceLookup EF InMemory 排序异常已经由 Task 8 的显式不同业务时间戳隔离关闭，不再登记为未修复 flake。

Task 8 首次终态六轮证据为 `artifacts/test-determinism/man-662/20260803T203911574Z-46fea15ab6ae4a6687fed1add88ad86b/summary.json`：6 轮、24 个项目运行全部 exit 0；对应 solution 证据 `artifacts/script-logs/man662-task8-fix2-full-solution/20260804-044915-206/` 为 66 个测试程序集、5849 passed、87 skipped、0 failed。最终 code review 修复后又以新 invocation `artifacts/test-determinism/man-662/20260804T053200000Z-final-review-fixes/summary.json` 重跑，exit 序列仍为 `[0,0,0,0,0,0]`。复审全解先后用 RED 日志 `artifacts/script-logs/man662-final-review-fixes-full-solution/20260804-054231-650/` 和 `artifacts/script-logs/man662-final-review-fixes-full-solution-green/20260804-055257-685/` 坐实并关闭了旧 sanitizer 断言与单次 scheduler yield 假设；最终 GREEN 为 `artifacts/script-logs/man662-final-review-fixes-full-solution-green2/20260804-060229-765/`：66 个测试程序集，5853 passed、87 skipped、0 failed，stderr 为空。MAN-661 仍只负责 lane timing、TRX、trend、skip/rerun 与 quarantine 的外部定量证据，当前状态继续是 `awaiting MAN-661`，不改变上述 MAN-662 本地 code-completion 结论。

## MAN-650 迁移状态

| MAN-650 项 | 状态 |
| --- | --- |
| Maintenance Redis renewal | migrated by MAN-662 |
| Inventory expiry metric | migrated by MAN-662 |
| Ops Production fake credential | migrated by MAN-662 |
| IndustrialTelemetry out-of-range/order-sensitive host surface | isolated here, structural split tracked by MAN-664 |
