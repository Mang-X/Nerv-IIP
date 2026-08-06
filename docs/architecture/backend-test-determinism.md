# 后端测试确定性与隔离契约

本文定义 MAN-662 交付的共享测试时间、等待、超时、网络诊断、全局状态隔离和本地重复性验证契约。它不把一次本地通过解释为 CI 趋势、flake rate、lane timing 或 skip budget 证据。

## 时间与等待决策

| 被测行为 | 时钟与等待方式 | 判定边界 |
| --- | --- | --- |
| scheduler、lease、expiry、renewal、retention | 注入 `TimeProvider`；测试使用 `FakeTimeProvider` 推进时间 | 不等待真实时间；生产代码与 timeout/timer 必须使用同一个 provider |
| 真实 transport、进程启动、容器就绪、外部消费者可见性 | 使用 wall clock，并通过 `Eventually` 有界轮询可观察事实 | 不用一次固定 sleep 猜完成时刻；超时必须报告最后一次脱敏观测 |
| 单次可能挂起的异步操作 | 使用 `TestTimeout.RunAsync` 和显式 timeout | helper 自己触发的 timeout 与 caller cancellation 分开；caller cancellation 原样传播 |
| 负向断言（"不再发生第二次"）的 settle 窗口 | 使用 `Consistently.StaysAsync` 有界稳定性断言 | 整个窗口持续观测，第一次观测到违例即失败并报告脱敏观测；不得"睡一次再断言一次"。窗口本身仍是对"该事件没有可观测边沿"的承认——只要被测代码能给出边沿（例如未推进的注入时钟让计时器驱动的事件结构上不可能发生），就应当先消除窗口 |

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

**推进假时钟前必须先确认计时器已注册。** `FakeTimeProvider.Advance` 只会触发**当时已注册**的计时器；若被测代码在 `Advance`
之后才创建 `Task.Delay`/`ITimer`，该计时器以已推进的 now 为基准重新定期，而此后没有任何东西再推进时钟——tick 永久丢失，
等待方永远不返回。`await Task.Yield()` **不是**这种同步屏障：它只让出一次调度，不保证目标代码已跑到注册那一行。
`BackgroundService.StartAsync` 返回时并不保证 `ExecuteAsync` 的方法体已执行，因此「`StartAsync` 之后计时器一定已注册」
是不成立的假设。正确做法是让被测替身在创建 pending 任务**之后**显式发出边沿信号，测试有界地等待该信号再 `Advance`
（`CapTestHostTests.FakeBootstrapper.BootstrapTimersRegistered`）。同一失败模式在 Connector Host 侧由 MAN-799 的
`WaitForTimerCreatedAsync` 屏障处理；两套 helper 分处两个 solution，按仓库边界规则不得互相引用，结构重复是有意的。

超时诊断必须保留 `condition` 或 `operation`、尝试次数、elapsed 和最后一次业务观测；显式 sensitive values 以及 password、secret、token、credential、API key、connection string、headers 和 request body 必须经 `TestDiagnostic.Sanitize` 清除。不得把完整请求头、请求体、连接串或响应体放进 describe、异常或测试输出。

## 网络结果与预算

连接 timeout 和总 request timeout 都必须由调用方显式配置为正数；测试不得用一个模糊的总时长同时冒充 DNS、连接和业务响应预算。生产默认值按真实依赖的正常抖动取秒级（Ops→IAM 为 connect 5s / request 10s），毫秒级预算只由测试通过配置节覆盖；把测试预算写成发布默认值会让依赖的任何一次抖动变成 fail-closed 拒绝。

`NetworkFailureClassifier.FromException` 强制传入 caller 的 `CancellationToken`：token 已取消时原样重抛原异常（`ExceptionDispatchInfo`），只有 helper 自己的超时才归类为 `RequestTimeout`。分类器不允许有"猜"caller 意图的默认参数。

**分类优先级：取消/超时语义先于内层 socket 错误码。** 一个 `OperationCanceledException` 完全可能裹着一个 `SocketException`（放弃的那一刻恰好有一跳在飞）。此时按**取消**定论，不去读内层错误码：caller token 已取消则原样重抛，未取消则归 `RequestTimeout`，哪怕内层写着 `ConnectionRefused`。理由是四分法保留的判据是**谁拥有这次放弃**——放弃的是 helper 自己，内层那个错误码只是放弃时刻的一个实现细节，不是本次失败的结论；让它胜出等于用一个更靠里的偶然事实盖掉调用方唯一能据以决策的信息。两侧行为一致但落点不同：测试侧 `FromException` 把取消判定**前置**于 socket 搜索；生产侧 `IamOpsConnectorCredentialValidator` 的 `OperationCanceledException` catch 块根本不进 `ClassifyTransportFailure`（后者只接 `HttpRequestException`）。该优先级由 `NetworkFailureClassifierTests.FromException_RanksHelperOwnedCancellationAboveANestedTransportError` 与生产侧镜像 `OpsConnectorCredentialValidationTests.CancellationsWrappingATransportError` 逐行钉住。

非 HTTP 客户端（Npgsql、裸 socket）不另立一套词汇：它们的传输失败以 `SocketException` 呈现，按 `SocketErrorCode` 归入同一四分法。两侧的 socket 搜索都从**异常自身**起步而不是从 `InnerException` 起步，因此裸 `SocketException` 与被驱动异常包裹的 `SocketException` 分类一致。

`SocketError.TimedOut` 归入 `RequestTimeout`，这**不表示**建连阶段超时被并进了 request 预算：connect 预算与 request 预算始终分开配置（`OpsIamClientOptions` 的 `ConnectTimeout` 5s / `RequestTimeout` 10s），四分法保留的区分是 **caller 拥有的取消 vs helper 拥有的超时**，不是 connect 阶段 vs 交换阶段。枚举**不为此扩容**：再切一类 `ConnectTimeout` 会把「谁拥有这次放弃」这个真正的判据换成一个调用方无法据以决策的阶段标签。阶段信息属于诊断字段（预算、elapsed、脱敏 operation），不属于分类。

生产侧的等价分类（如 `IamOpsConnectorCredentialValidator.ClassifyTransportFailure`）**刻意不复用** `Nerv.IIP.Testing`：发布程序集不得引用测试程序集。两处的相似结构是有意的边界重复，不是待消除的 duplication；任一侧改变分类语义时必须同步另一侧和本表。该同步不靠自觉：`OpsConnectorCredentialValidationTests.TransportFailures` 逐行同时断言 `NetworkFailureKind` 与生产侧的 `FailureKind` 字符串，任一侧漏同步即在此处变红。

镜像的**范围只到异常路径**（`ClassifyTransportFailure`）。HTTP **响应**路径两侧刻意不同，下表如实登记：测试侧 `FromResponse` 把对端上报的 408/504 归为 `RequestTimeout`，而生产侧只按 `IsSuccessStatusCode` 分叉，所有非成功且非 401 的响应一律记 `FailureKind=business-response`，数字 status code 走**独立的** `StatusCode` 日志属性而不是塞进 `FailureKind`。这不是漏同步：生产此处唯一的决策是 fail-closed 拒绝，它不需要区分对端超时与对端业务拒绝；需要区分的是测试断言。若哪天生产要按对端超时做重试，才把这条分叉补上并同步本表。

| 结果 | 测试侧分类（`NetworkFailureClassifier`） | 生产侧 `FailureKind`（`IamOpsConnectorCredentialValidator`） | 必须保留 | 禁止混淆或输出 |
| --- | --- | --- | --- | --- |
| DNS 解析失败（`HttpRequestError.NameResolutionError`，或 socket `HostNotFound`/`NoData`/`TryAgain`/`NoRecovery`） | `NetworkFailureKind.Dns` | `dns` | 脱敏的 DNS 失败类别 | 不伪装为 HTTP 503；不输出目标凭据 |
| 连接被拒绝（socket `ConnectionRefused`） | `NetworkFailureKind.ConnectionRefused` | `connection-refused` | 脱敏的 refused 类别 | 不伪装为 request timeout |
| helper 自己的超时（HTTP 侧的 helper-owned cancellation，或 socket `TimedOut`；connect 与交换两个阶段同归此类） | `NetworkFailureKind.RequestTimeout` | `request-timeout` | 预算、elapsed、脱敏 operation | caller cancellation 必须原样传播，不能改写成 timeout |
| 取消异常内层裹着 socket 错误码（caller token **未**取消） | `NetworkFailureKind.RequestTimeout`（取消判定前置于 socket 搜索，内层错误码不参与） | `request-timeout`（`OperationCanceledException` catch 块，不进 `ClassifyTransportFailure`） | 预算、elapsed、脱敏 operation | 不按内层 `ConnectionRefused`/`HostNotFound` 改判；caller token 已取消时仍必须原样重抛 |
| 其余传输失败（未列入上面三行的 socket 错误码，以及不带 socket 异常的 `HttpRequestException`） | 分类器抛 `ArgumentException`（测试侧必须显式扩表） | `transport-error` | 脱敏的类别 | 不静默归入以上任何一类 |
| 对端上报的 408 / 504 | `NetworkFailureKind.RequestTimeout` | `business-response`，`StatusCode=408`/`504`（生产不单独分叉，见上一段） | 数字 status code | 测试侧不并入 `BusinessError`，否则四分法失去意义 |
| 其余非成功 HTTP 响应（401 除外） | `NetworkFailureKind.BusinessError` | `business-response`，数字 status code 记在 `StatusCode` | 数字 status code 与脱敏 reason phrase | 不记录 response body 或 headers；不降级成 transport fault |
| HTTP 401 | 无（不是网络失败） | 不记日志，直接 `iam-rejected` | 拒绝判定本身 | 不记为传输故障，也不写入凭据 |
| 成功响应但 body 不可解析或 principal 不完整 | 无（不是网络失败） | `invalid-response`，`StatusCode` 为该成功码 | 数字 status code 与异常 | 不记录 response body |

## 可变全局状态隔离矩阵

| 状态面 | 隔离方式 | 契约 |
| --- | --- | --- |
| FluentValidation global resolvers、current/default culture、`TZ` 与显式环境变量 | scoped capture/restore；所有 mutator 串行进入 `GlobalTestStateScope` | 精确区分原本不存在、空字符串和有值；`DisposeAsync` 必须恢复并释放 scope |
| 同一服务内的 host startup 与共享 host fixture | xUnit collection serialization | 同一 service 的启动/停止不得跨 collection 并发；该约束不等于整个 solution 串行 |
| BusinessGateway host startup（`Nerv.IIP.BusinessGateway.Web.Tests`） | `BusinessGatewayTestHostGate` 多读单写 permit：构建独占，请求共享；permit 由**服务端**中间件持有 | 见下方 MAN-663 一节。构建期间无请求在飞，请求期间无构建发生；程序集不再关闭并行 |
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
permit。因此「构建时无请求在飞、请求时无构建发生」，请求之间仍完全并行。这是真实互斥，
**不宣称 FastEndpoints 静态状态可恢复**。这是根 `AGENTS.md`「Backend Test Determinism」允许的第三种手段
（互斥门），与 collection serialization、sacrificial process isolation 并列，同样不声称 restore。

**permit 必须由服务端持有。** permit 曾由客户端 `DelegatingHandler` 环绕 `base.SendAsync` 持有，这是**不成立**的：
TestServer 的 `ClientHandler` 在响应头 flush 时即返回，服务端仍在写 body；而 `HttpClient` 的
`ResponseContentRead` 缓冲发生在 handler 链**之外**。于是存在「permit 已还、服务端仍在跑」的窗口，正是这道门要
排除的竞态。现由 `RequestPermitStartupFilter` 注册的最外层中间件持有 permit，覆盖整条服务端管线。回归测试
`Host_construction_waits_for_a_response_body_that_is_still_being_written` 在旧的客户端实现下**会失败**（实测：
`Host construction completed while the gateway was still writing a response body`），因此这条不是散文。
副产物是 permit 只可能被服务端管线持有、永远不会被测试线程持有，构建取全部 permit 因此不可能被发起它的线程阻塞。

**租约释放前先 drain。** 租约注销与「仍在飞的请求解析自己的 fake」必须互斥，而不是「大概率不重叠」：
每条请求在中间件里登记到自己的 scope，`ReleaseScopeAsync` 先等到该 scope 在飞数归零（有界预算，超时报告
in-flight 数、elapsed 与 attempts）再从注册表移除。

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
| 无法逐请求表达的配置（builder 设置、非实例注册、名单外类型、**只移除不重注册**） | 由 `BusinessGatewayTestHost.Lease` 自动回退到 dedicated 宿主，正确性不依赖共享是否适用 |

**「只移除不重注册」为什么要专门检测。** 租约把测试的 `configureServices` 块回放到一个探针 collection 上以收割
实例。若探针是空的，`RemoveAll<T>()` 不留任何痕迹，于是「移除某注册但不放回」的块会被误判为可共享，而共享宿主
仍保留真实注册——测试跑在与 dedicated 宿主不同的接线上，且没有任何提示。因此探针以共享宿主自己的注册清单
（开放泛型除外，它们无法用工厂 descriptor 注册且从不被网关测试替换）预置哨兵 descriptor：回放后缺失的哨兵即为
移除意图，只有该类型同时被补上逐请求实例才算可共享，否则回退 dedicated 宿主。
`A_configuration_block_that_only_removes_a_registration_falls_back_to_a_dedicated_host` 断言这一点。

**反向证明。** `BusinessGatewaySharedHostIsolationTests` 是行为断言而非 attribute 断言。三次故意注入泄漏均被
捕获：把 health state 改回单例 → 1 失败；把逐请求 scope 解析改成「最近一个租约」的共享槽 → 3 失败；让 builder
设置不再强制 dedicated 宿主 → 1 失败。第四次：把 permit 改回客户端 `DelegatingHandler` 持有 → 1 失败。
宿主复用断言使用引用同一性（两个 profile 各一个宿主、25 轮租约同宿主），不使用全局宿主计数——该计数只由两个
`Lazy<T>` profile 工厂自增，结构上不可能大于 2，断言它的上界等于自证。

### 测试分层清单（哪些必须启 HTTP 宿主）

| 分类 | 需要 HTTP 宿主 | 类 | 处置 |
| --- | --- | --- | --- |
| (a) 纯合同/元数据 | 否 | `BusinessConsoleSearchableDirectoryPolicyTests`、`BusinessGatewayWmsTrustedCompletionContractTests`、`MaintenanceLifecycleWireRoundTripTests` | 本来就是轻量路径，本次未改动 |
| (b) 直接应用行为 | 否 | `BusinessGatewayAuthorizationClientTests`、`BusinessMesAcceptedReceiptClientTests`、`BusinessMesMaterialIssueClientTests`、`BusinessMesQualityHoldClientTests`、`BusinessConsoleWorkerDirectoryValidationTests`、`PublicIdempotencyRequestValidationTests`、`SchedulingWorkbenchValidationTests`、`WmsTrustedRequestContextTests`、`BusinessGatewayIdempotencySafetyTests`、`BusinessGatewayPrincipalWorkContextResolverTests` | 本来就是轻量路径，本次未改动 |
| (c) 必须启宿主，已迁到共享 profile 租约 | **是** | `BusinessGatewayProxyTests`、`BusinessGatewayAuthorizationTests`、`BusinessGatewayMaintenanceTelemetryTests`、`BusinessGatewayWmsTests`、`BusinessGatewayWorkbenchTests`、`BusinessGatewaySearchTests`、`BusinessConsoleSearchableDirectoryWireTests`、`BusinessGatewayPrincipalWorkContextEndpointTests`、`BusinessGatewayConnectorTagCoverageTests`、`BusinessGatewayOpenApiTests`、`BusinessGatewayNotificationOpenApiTests`、`BusinessGatewayLifecycleConflictOpenApiTests` | 共享宿主 + 逐租约 scope |
| (d) 必须启宿主，且需要独立启动 profile | **是**（dedicated） | `BusinessGatewayProductionSecurityTests`、`BusinessGatewayHttpClientResilienceTests`、`BusinessGatewayRateLimitTests`，以及 `BusinessGatewayAuthorizationTests` 中断言缺失 JWT 配置的用例 | 保留 dedicated 宿主，构建仍走同一 gate |
| (e) 隔离机制自身 | **是** | `BusinessGatewaySharedHostIsolationTests` | 本次新增 |

(a)(b) 两类约 150 例本来就不启宿主。(c) 类断言的是认证、授权、中间件、序列化与真实路由，**必须**经过 HTTP 宿主，
因此改造方向是让它们共用宿主，而不是把它们降级成反射断言（那会丢覆盖）。

### 未做的优化及其理由（spec §7 评估结论）

spec §7 要求**评估**把 697 个细粒度重复的路由/权限用例合并成数据驱动 contract matrix。**结论：评估后不做。**
该合并的收益前提是「每个用例各付一次宿主构建成本」，而共享宿主后单个用例的边际成本已降到毫秒级（整程序集
1036 例合计 wall 约 10 s），收益基本消失；代价则是确定的——`[Theory]` 行的失败定位粒度显著劣于独立命名用例，
而这些用例覆盖的正是权限与 401/403 语义。若将来该程序集重新变成瓶颈，应重新评估而不是把本结论当成永久决定。

### 耗时验收证据

验收标准是 **GitHub hosted runner 热缓存 ≤ 2 min**，因此以 hosted runner 数据为准（本机 macOS 数据只作参考）。
两侧同为 `ubuntu24@20260720.247.2` / SDK `10.0.302`，取自 MAN-661 evidence artifact 的 per-assembly
`elapsedMilliseconds`（TRX 执行窗口，不含 restore/build）：

| | 用例数 | hosted runner 该程序集 TRX elapsed | 来源 |
| --- | --- | --- | --- |
| 改造前 | 1023 | **869.4 s（14 m 29 s）** | main push run `30890682487`，tested SHA `90715433b` |
| 改造后 | 1036 | **22.0 s（22 034.6 ms）** | PR run `30899938177`，tested SHA `1c374177` |

本机 macOS `--no-build` wall 为 8.7–11.3 s，作为 5 seed × 5 并发档矩阵的稳定性证据保留，但**不**用于 ≤2 min 验收。

上表两个 run 都发生在 MAN-669 分片**之前**，两侧 evidence lane 均为当时的 `backend`。合并 MAN-669 后该程序集的证据落在
`backend-shard-1`（job `Backend Tests - BusinessGateway`）。per-assembly TRX elapsed 只取决于该程序集自己的执行窗口，
不受 lane 拓扑影响，因此上表的 before/after 对比在分片后依然成立；变的只是后续 refresh 要从哪条 lane 取数。

spec §8 要求「使用 MAN-661 的每用例基线对比」。MAN-663 落地当时该 baseline 为
`unavailableReason: incompatible-granularity-or-duration-metric`（committed baseline 是 project-wall-clock，
运行摘要是 test-granularity trx-elapsed，不可比），因此上表改用同一 evidence artifact 的 **per-assembly TRX
elapsed** 做 before/after 对比。局限如实记录：这是程序集粒度而非每用例粒度，两个 run 的 runner 硬件不完全同机，
且不含 restore/build 时间；结论「量级下降」稳健，但不应被当作每用例 baseline 已经建立。2026-08-05 已用合并后
首个合格 main push run `30999368607` 的 normalized artifacts 完成 refresh：committed baseline 现为
`granularity: test` / `durationMetric: trx-elapsed`，`backend-shard-1` 的
`nerv.iip.businessgateway.web.tests.dll` 记为 **22 996.0 ms / 1036 例**，耗时对比恢复为 `available` 的
report-only delta。上表保留为 MAN-663 当时的取证过程，权威数字以 committed baseline 为准。另注：
implementation-readiness 里「822 000 ms → 22 996 ms（约 −97.2%）」是**跨口径**百分比（分母取自旧
project-wall-clock baseline，分子是 trx-elapsed），只作量级参考；**同口径**佐证正是上表两行——
869.4 s / 1023 例 → 22.0 s / 1036 例，同为 hosted runner 的 per-assembly TRX elapsed。

## Seeded order 与本地六轮验证

`Nerv.IIP.Testing.Xunit` 的 case/collection orderer 读取 `NERV_IIP_TEST_ORDER_SEED`；变量缺失时使用固定 `nerv-iip-default`。排序键为 `SHA-256(seed + display-name)`，hash 相同再按 name 做 ordinal 排序；display name 是 `ITestCase` 与 `ITestCollection` 共有的唯一稳定标识，`[Theory]` 的 display name 含参数文本但每行数据固定，排序仍然稳定。四个目标程序集通过 `SeededTestOrdering.targets` 链接同一份 assembly attribute 文件声明 orderer（不是四份副本），不改变 collection serialization 或业务测试职责。

`pwsh scripts/verify-backend-test-determinism.ps1` 先经受治理的 `New-ExclusiveInvocationClaim`（`FileMode.CreateNew`）原子取得 invocation claim，再创建新的 `artifacts/test-determinism/man-662/<invocation-id>/`，执行六轮、每轮四项目：seed 固定为 `man662-01` 至 `man662-06`，serial/parallel 交替，`MaxParallelThreads` 为 1/4，项目顺序逐轮旋转。profile 只由受支持的 VSTest `<xUnit>` runsettings 设置，不通过 compile-time attribute 切换；第一轮构建后后续轮次使用同一程序集。同一显式 invocation ID 的并发失败者在执行项目前即被拒绝，既有或失败证据都不能被 rerun 覆盖。

每个 `summary.json` 含六个本地复现字段 `run`、`seed`、`profile`、`projectOrder`、`elapsedMs`、`exitCode`，外加 `projectResults`：逐项目的 `exitCode` 与 `total/passed/skipped/failed` 计数（以 `DOTNET_CLI_UI_LANGUAGE=en` 保证解析与 locale 无关）。退出码相等**不等于**结果一致——某轮静默跳过测试时退出码仍为 0，因此验证器跨轮比对同一项目的四个计数，任一不一致即失败。MAN-662 仍不生成 TRX、`trxPaths`、per-test/lane timing、skip budget 或 rerun accounting。

## Quarantine 与外部证据边界

MAN-661 独占 required-lane/opt-in-lane policy、machine-readable quarantine registry 与 enforcement；MAN-662 不创建 quarantine registry 或规则。MAN-661 的定量 trend/flake evidence 是外部证据，不阻塞本变更实现、评审、合并或 code-completion。当前 baseline status 为 `awaiting MAN-661`；只能在 MAN-661 落地后用真实 artifact identifier 替换，不能填推测值。

现有 `backend/test-determinism-baseline.json` 有 47 个登记债务行，**全部**是 `StaticSetter` → `#1471`（2026-09-12）。登记之初还有另外两类，现均已**清零**：`Task.Delay` 33 行 → `#1470`（2026-09-05），`UnreachableAddress` 7 行 → `#1472`（2026-08-25）。checker 的 `$allowedPatterns` 仍保留这两个 pattern，以便回潮时能被重新识别；零登记行不等于该 pattern 被豁免，而是它当前确实不存在。

#1470 的 33 行分三批清偿。第一批 8 行（`CapTestHostTests`、`TestTimeoutTests`、`ProcessMemorySamplerTests`、`FileStorageTusProviderTests`、`NotificationCapOutboxAcceptanceTests`、`OpsConnectorCredentialValidationTests`）：「挂起到被取消」的哨兵改为 `PendingOperation.UntilCanceledAsync`（不再创建任何计时器），TUS 上传会话过期改为注入 `TimeProvider`，CAP outbox 可见性改为 `Eventually` 有界轮询，进程内存采样改为等显式的「已采样」信号。第二批 12 行（`ErpSalesOrderDemandConsumerTests` 4 行、`DemandPlanningEndpointContractTests`、`PlanningInputAdapterTests`、`ApprovalOverdueSchedulerTests` 2 行、`ErpCostAccountingPostgresAcceptanceTests`、`InventoryDirectoryPostgresTests`、`MaintenanceCommandLockTests`、`HttpSchedulingEquipmentAvailabilityProviderBatchingTests`）：`ApprovalOverdueScheduler` 的 `PeriodicTimer` 改为跑在注入的 `TimeProvider` 上（未推进的假时钟让第二次 tick 结构上不可能发生，而不只是不太可能）；真实 PostgreSQL/Redis/CAP 的可见性（advisory-lock waiter、容器就绪、MRP run 终态、CAP 立即重试与 fallback 扫描）一律改为 `Eventually` 有界轮询并报告脱敏的最后观测；两处 HTTP 并发上限测试（MasterData SKU 明细、设备可用性批次）用测试自己控制的 gate 取代 50 ms 持有时间——先等在飞数**到达**上限这个真实边沿，再断言它在全部请求在飞期间**保持**不越界，因此断言从「不超过」升级为等值；负向断言的 settle 窗口收敛到新的共享原语 `Consistently.StaysAsync`（`Eventually` 的反向对偶：整窗轮询、第一次观测到违例即失败并报告脱敏观测/尝试次数/elapsed，而不是睡一次再断言一次）。

第三批 13 行清空剩余全部 `Task.Delay`，覆盖 `Nerv.IIP.Business.FullChain.Tests` 与 `Nerv.IIP.Business.Mes.Web.Tests` 两个程序集：

- **真实 transport / 容器就绪**（第 2 类）改为 `Eventually` 有界轮询：Docker PostgreSQL/Redis 就绪、Maintenance 与 MES/Inventory 的 Redis CAP consumer group 注册、ERP↔WMS 重放回执计数、DemandPlanning 重复/乱序事件的消费证据、MES/Inventory produced-lot 双路径终态、以及 `MesCapSubscriptionTests` 的断言重试循环。诊断字段一律走 `EventuallyTimeoutException`（condition + attempts + elapsed + 脱敏最后观测），连接串作为 sensitive value 传入。MES/Inventory 那一处**保留**原先的 messaging 诊断：`ReadMessagingFactsAsync` 只在超时分支读一次，它是超时的诊断而不是被等待条件的一部分。
- **外部调度进程的负向断言**改为 `Consistently.StaysAsync`：`MaintenanceRuntimeHoursPostgresRedisAcceptanceTests` 的「阈值以下不生成工单」与「后续 tick 不重复生成」两个窗口。调度器跑在另一个进程的真实时钟上，测试无法给它注入 `TimeProvider`，所以窗口本身仍是承认；变化在于整窗持续观测、第一次违例即失败并报告脱敏观测。
- **三处 PostgreSQL 并发序列化的 settle 窗口被直接消除**（`MesSchedulePlanProvenancePostgresTests`、`SkuDisabledConsumerTests`、`WorkOrderCapitalizationConcurrencyPostgresTests`）：MES 的 scope coordinator 用 `pg_advisory_xact_lock` 序列化竞争写者，而 `pg_stat_activity` 把这次等待**暴露成可观测事实**。新增共享 helper `MesPostgresAdvisoryLockProbe.WaitForWaitersAsync` 有界等到「本库有一个 backend 停在 advisory lock 上」这个真实边沿，再断言竞争任务未完成——观测到阻塞，而不是假定阻塞发生在某个睡眠之内。每个用例各用一个临时库，因此 `datname = current_database()` 足以与并发测试类区分。
- **MES 端点重放**（`MesEndpointContractTests`）改为向宿主注入 `FakeTimeProvider`：端点在调用方未给 `ChangedAtUtc` 时用注入的 `TimeProvider` 打时间戳，因此「两次请求带着不同的服务端时间戳」从「5 ms 墙钟间隔大概率产生两个不同瞬时」变成测试自己控制的事实。锚点取**真实 now**（链路上仍有按真实日期评估的就绪判定），只有两次请求之间的差值是伪造的。
- **`ConcurrentLifecycleSaveGate`** 的 `Task.WhenAny(allArrived, Task.Delay(budget))` 改为 `TestTimeout.RunAsync` + 显式 catch：这是一个真实并发屏障上的预算（输掉幂等竞争的参与者可能根本不到达），不是 sleep-before-assert；改写把「刻意的回退」写成显式分支，而不是藏在一个 loser 被静默丢弃的 `WhenAny` 竞速里。owner 必须在登记它的变更合并之后依然存在——用当前 PR 自己的票做 owner，等于合并当天债务就没有责任人。`reason` 按行而非按文件书写：同一文件里结构不同的位点（例如轮询间隔与负向断言的稳定性窗口）不得共用一句解释。checker 通过只证明 inventory 与元数据吻合，不代表债务已清零。前两次六轮运行保留为 RED 历史：`20260803T192749730Z-18bd13f4bc794fbd9f054c1be2bb1410/summary.json` 的 exit 序列为 `[1,0,1,1,1,0]`，`20260803T200756805Z-929b74e11b494261941716a905a10563/summary.json` 为 `[1,1,1,0,1,1]`。其中 Task 4 首次观测的 SourceLookup EF InMemory 排序异常已经由 Task 8 的显式不同业务时间戳隔离关闭，不再登记为未修复 flake。

Task 8 首次终态六轮证据为 `artifacts/test-determinism/man-662/20260803T203911574Z-46fea15ab6ae4a6687fed1add88ad86b/summary.json`：6 轮、24 个项目运行全部 exit 0；对应 solution 证据 `artifacts/script-logs/man662-task8-fix2-full-solution/20260804-044915-206/` 为 66 个测试程序集、5849 passed、87 skipped、0 failed。最终 code review 修复后又以新 invocation `artifacts/test-determinism/man-662/20260804T053200000Z-final-review-fixes/summary.json` 重跑，exit 序列仍为 `[0,0,0,0,0,0]`。复审全解先后用 RED 日志 `artifacts/script-logs/man662-final-review-fixes-full-solution/20260804-054231-650/` 和 `artifacts/script-logs/man662-final-review-fixes-full-solution-green/20260804-055257-685/` 坐实并关闭了旧 sanitizer 断言与单次 scheduler yield 假设；最终 GREEN 为 `artifacts/script-logs/man662-final-review-fixes-full-solution-green2/20260804-060229-765/`：66 个测试程序集，5853 passed、87 skipped、0 failed，stderr 为空。MAN-661 仍只负责 lane timing、TRX、trend、skip/rerun 与 quarantine 的外部定量证据，当前状态继续是 `awaiting MAN-661`，不改变上述 MAN-662 本地 code-completion 结论。

## MAN-650 迁移状态

| MAN-650 项 | 状态 |
| --- | --- |
| Maintenance Redis renewal | migrated by MAN-662 |
| Inventory expiry metric | migrated by MAN-662 |
| Ops Production fake credential | migrated by MAN-662 |
| IndustrialTelemetry out-of-range/order-sensitive host surface | isolated here, structural split tracked by MAN-664 |
