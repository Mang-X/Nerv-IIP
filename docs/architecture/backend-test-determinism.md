# 后端测试确定性与隔离契约

本文定义 MAN-662 交付的共享测试时间、等待、超时、网络诊断、全局状态隔离和本地重复性验证契约。它不把一次本地通过解释为 CI 趋势、flake rate、lane timing 或 skip budget 证据。

## 时间与等待决策

| 被测行为 | 时钟与等待方式 | 判定边界 |
| --- | --- | --- |
| scheduler、lease、expiry、renewal、retention | 注入 `TimeProvider`；测试使用 `FakeTimeProvider` 推进时间 | 不等待真实时间；生产代码与 timeout/timer 必须使用同一个 provider |
| 真实 transport、进程启动、容器就绪、外部消费者可见性 | 使用 wall clock，并通过 `Eventually` 有界轮询可观察事实 | 不用一次固定 sleep 猜完成时刻；超时必须报告最后一次脱敏观测 |
| 单次可能挂起的异步操作 | 使用 `TestTimeout.RunAsync` 和显式 timeout | helper 自己触发的 timeout 与 caller cancellation 分开；caller cancellation 原样传播 |
| 负向断言（"不再发生第二次"）的 settle 窗口 | 使用 `Consistently.StaysAsync` 有界稳定性断言 | 整个窗口持续观测，第一次观测到违例即失败并报告脱敏观测；不得"睡一次再断言一次"。窗口关闭时仍在飞的那次观测不被丢弃：它在独立的 grace 预算内被等完再照常裁定（丢掉它等于丢掉最可能暴露违例的那一次）；grace 也超时才判**超时**（`ConsistentlyObservationTimeoutException`）——verdict unknown，既不是 pass 也不是违例。窗口本身仍是对"该事件没有可观测边沿"的承认——只要被测代码能给出边沿（例如未推进的注入时钟让计时器驱动的事件结构上不可能发生），就应当先消除窗口 |

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
是不成立的假设。正确做法是有界地等待**「计时器已注册」这条边沿本身**再 `Advance`：现行标准设施是
`TimerRegistrationObservingTimeProvider.WaitForTimerCountAsync(n)`（`CreateTimer` 内先 `base.CreateTimer` 再计数，
信号严格晚于注册）；被测替身自己发边沿信号（`CapTestHostTests.FakeBootstrapper.BootstrapTimersRegistered`）只用在
替身本来就要创建 pending 任务的场合，且该信号必须发在创建 pending 任务**之后**——发在之前就又变回一个不保证注册
已发生的假屏障。业务结果——「命令已派发」「metric 已出现」——**都不是**这条边沿：它们仅在「生产代码今天恰好先建
计时器再干活」时才顺带成立，`ApprovalOverdueSchedulerTests`（S4）与
`InventoryReservationExpirationTests`（#1491）各栽过一次。同一失败模式在 Connector Host 侧由 MAN-799 的
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
首个合格 main push run `30999368607` 的 normalized artifacts 完成 refresh：committed baseline 自此为
`granularity: test` / `durationMetric: trx-elapsed`，`backend-shard-1` 的
`nerv.iip.businessgateway.web.tests.dll` 当时记为 **22 996.0 ms / 1036 例**，耗时对比恢复为 `available` 的
report-only delta。2026-08-07 又因 MAN-669 PR-A 换片导致的 (lane, assembly) 失键刷新到 run
`31185687984`（schema 2、per-lane provenance），该行现为 **20 726.0 ms / 1036 例**；差额落在 hosted
runner 抖动内。上表保留为 MAN-663 当时的取证过程，权威数字以 committed baseline 为准。另注：
implementation-readiness 里「822 000 ms → 22 996 ms（约 −97.2%）」是**跨口径**百分比（分母取自旧
project-wall-clock baseline，分子是 trx-elapsed），只作量级参考；**同口径**佐证正是上表两行——
869.4 s / 1023 例 → 22.0 s / 1036 例，同为 hosted runner 的 per-assembly TRX elapsed。

## Seeded order 与本地六轮验证

`Nerv.IIP.Testing.Xunit` 的 case/collection orderer 读取 `NERV_IIP_TEST_ORDER_SEED`；变量缺失时使用固定 `nerv-iip-default`。排序键为 `SHA-256(seed + display-name)`，hash 相同再按 name 做 ordinal 排序；display name 是 `ITestCase` 与 `ITestCollection` 共有的唯一稳定标识，`[Theory]` 的 display name 含参数文本但每行数据固定，排序仍然稳定。四个目标程序集通过 `SeededTestOrdering.targets` 链接同一份 assembly attribute 文件声明 orderer（不是四份副本），不改变 collection serialization 或业务测试职责。

`pwsh scripts/verify-backend-test-determinism.ps1` 先经受治理的 `New-ExclusiveInvocationClaim`（`FileMode.CreateNew`）原子取得 invocation claim，再创建新的 `artifacts/test-determinism/man-662/<invocation-id>/`，执行六轮、每轮四项目：seed 固定为 `man662-01` 至 `man662-06`，serial/parallel 交替，`MaxParallelThreads` 为 1/4，项目顺序逐轮旋转。profile 只由受支持的 VSTest `<xUnit>` runsettings 设置，不通过 compile-time attribute 切换；第一轮构建后后续轮次使用同一程序集。同一显式 invocation ID 的并发失败者在执行项目前即被拒绝，既有或失败证据都不能被 rerun 覆盖。

每个 `summary.json` 含六个本地复现字段 `run`、`seed`、`profile`、`projectOrder`、`elapsedMs`、`exitCode`，外加 `projectResults`：逐项目的 `exitCode` 与 `total/passed/skipped/failed` 计数（以 `DOTNET_CLI_UI_LANGUAGE=en` 保证解析与 locale 无关）。退出码相等**不等于**结果一致——某轮静默跳过测试时退出码仍为 0，因此验证器跨轮比对同一项目的四个计数，任一不一致即失败。MAN-662 仍不生成 TRX、`trxPaths`、per-test/lane timing、skip budget 或 rerun accounting。

## Quarantine 与外部证据边界

MAN-661 独占 required-lane/opt-in-lane policy、machine-readable quarantine registry 与 enforcement；MAN-662 不创建 quarantine registry 或规则。MAN-661 的定量 trend/flake evidence 是外部证据，不阻塞本变更实现、评审、合并或 code-completion。当前 baseline status 为 `awaiting MAN-661`；只能在 MAN-661 落地后用真实 artifact identifier 替换，不能填推测值。

`backend/test-determinism-baseline.json` 的**到期债务行现为 0**。登记之初的三类均已清零：`Task.Delay` 33 行 → `#1470`（2026-09-05）、`UnreachableAddress` 7 行 → `#1472`（2026-08-25）、`StaticSetter` 47 行 → `#1471`（2026-09-12，见下节）。checker 的 `$allowedPatterns` 仍保留全部 pattern，以便回潮时能被重新识别；零登记行不等于该 pattern 被豁免，而是它当前确实不存在。

#1470 的 33 行分三批清偿。第一批 8 行（`CapTestHostTests`、`TestTimeoutTests`、`ProcessMemorySamplerTests`、`FileStorageTusProviderTests`、`NotificationCapOutboxAcceptanceTests`、`OpsConnectorCredentialValidationTests`）：「挂起到被取消」的哨兵改为 `PendingOperation.UntilCanceledAsync`（不再创建任何计时器），TUS 上传会话过期改为注入 `TimeProvider`，CAP outbox 可见性改为 `Eventually` 有界轮询，进程内存采样改为等显式的「已采样」信号。第二批 12 行（`ErpSalesOrderDemandConsumerTests` 4 行、`DemandPlanningEndpointContractTests`、`PlanningInputAdapterTests`、`ApprovalOverdueSchedulerTests` 2 行、`ErpCostAccountingPostgresAcceptanceTests`、`InventoryDirectoryPostgresTests`、`MaintenanceCommandLockTests`、`HttpSchedulingEquipmentAvailabilityProviderBatchingTests`）：`ApprovalOverdueScheduler` 的 `PeriodicTimer` 改为跑在注入的 `TimeProvider` 上（未推进的假时钟让第二次 tick 结构上不可能发生，而不只是不太可能）；真实 PostgreSQL/Redis/CAP 的可见性（advisory-lock waiter、容器就绪、MRP run 终态、CAP 立即重试与 fallback 扫描）一律改为 `Eventually` 有界轮询并报告脱敏的最后观测；两处 HTTP 并发上限测试（MasterData SKU 明细、设备可用性批次）用测试自己控制的 gate 取代 50 ms 持有时间——先等在飞数**到达**上限这个真实边沿，再断言它在全部请求在飞期间**保持**不越界，因此断言从「不超过」升级为等值；负向断言的 settle 窗口收敛到新的共享原语 `Consistently.StaysAsync`（`Eventually` 的反向对偶：整窗轮询、第一次观测到违例即失败并报告脱敏观测/尝试次数/elapsed，而不是睡一次再断言一次）。

第三批 13 行清空剩余全部 `Task.Delay`，覆盖 `Nerv.IIP.Business.FullChain.Tests` 与 `Nerv.IIP.Business.Mes.Web.Tests` 两个程序集：

- **真实 transport / 容器就绪**（第 2 类）改为 `Eventually` 有界轮询：Docker PostgreSQL/Redis 就绪、Maintenance 与 MES/Inventory 的 Redis CAP consumer group 注册、ERP↔WMS 重放回执计数、DemandPlanning 重复/乱序事件的消费证据、MES/Inventory produced-lot 双路径终态、以及 `MesCapSubscriptionTests` 的断言重试循环。诊断字段一律走 `EventuallyTimeoutException`（condition + attempts + elapsed + 脱敏最后观测），连接串作为 sensitive value 传入。MES/Inventory 那一处**保留**原先的 messaging 诊断：`ReadMessagingFactsAsync` 只在超时分支读一次，它是超时的诊断而不是被等待条件的一部分。
- **外部调度进程的负向断言**改为 `Consistently.StaysAsync`：`MaintenanceRuntimeHoursPostgresRedisAcceptanceTests` 的「阈值以下不生成工单」与「后续 tick 不重复生成」两个窗口。调度器跑在另一个进程的真实时钟上，测试无法给它注入 `TimeProvider`，所以窗口本身仍是承认；变化在于整窗持续观测、第一次违例即失败并报告脱敏观测。
- **三处 PostgreSQL 并发序列化的 settle 窗口被直接消除**（`MesSchedulePlanProvenancePostgresTests`、`SkuDisabledConsumerTests`、`WorkOrderCapitalizationConcurrencyPostgresTests`）：MES 的 scope coordinator 用 `pg_advisory_xact_lock` 序列化竞争写者，而 `pg_stat_activity` 把这次等待**暴露成可观测事实**。新增共享 helper `MesPostgresAdvisoryLockProbe.WaitForWaitersAsync` 有界等到「本库有一个 backend 停在 advisory lock 上」这个真实边沿，再断言竞争任务未完成——观测到阻塞，而不是假定阻塞发生在某个睡眠之内。每个用例各用一个临时库，因此 `datname = current_database()` 足以与并发测试类区分——这不是注释里的声称：三个调用方都在**测试方法体内**用 `Guid.CreateVersion7()` 命名新建库（`nerv_mes_schedule_*`、`nerv_mes_sku_disabled_*`、`nerv_mes_cost_race_*`），并在 dispose 时 `DROP DATABASE ... WITH (FORCE)`。上一个用例即使泄漏了一个仍在等锁的 backend，它停在探针连接根本看不见的另一个库上，因此不可能被计入。哪天有调用方改成跨用例共享库，这条判据就必须收紧到 `pg_locks` 的 `classid`/`objid` 精确匹配本用例申请的那把锁——该约束写在 `MesPostgresAdvisoryLockProbe` 的 XML 注释里。
- **MES 端点重放**（`MesEndpointContractTests`）改为向宿主注入 `FakeTimeProvider`：端点在调用方未给 `ChangedAtUtc` 时用注入的 `TimeProvider` 打时间戳，因此「两次请求带着不同的服务端时间戳」从「5 ms 墙钟间隔大概率产生两个不同瞬时」变成测试自己控制的事实。锚点取**真实 now**（链路上仍有按真实日期评估的就绪判定），只有两次请求之间的差值是伪造的。
- **`ConcurrentLifecycleSaveGate`** 的 `Task.WhenAny(allArrived, Task.Delay(budget))` 改为 `TestTimeout.RunAsync` + 显式 catch：这是一个真实并发屏障上的预算（输掉幂等竞争的参与者可能根本不到达），不是 sleep-before-assert；改写把「刻意的回退」写成显式分支，而不是藏在一个 loser 被静默丢弃的 `WhenAny` 竞速里。

上面五条只描述第三批各自的位点。以下是 baseline 治理规则与 MAN-662 的运行历史，它们属于 baseline 章节的散文，与 `ConcurrentLifecycleSaveGate` 这道并发屏障无关：

owner 必须在登记它的变更合并之后依然存在——用当前 PR 自己的票做 owner，等于合并当天债务就没有责任人。`reason` 按行而非按文件书写：同一文件里结构不同的位点（例如轮询间隔与负向断言的稳定性窗口）不得共用一句解释。checker 通过只证明 inventory 与元数据吻合，不代表债务已清零。前两次六轮运行保留为 RED 历史：`20260803T192749730Z-18bd13f4bc794fbd9f054c1be2bb1410/summary.json` 的 exit 序列为 `[1,0,1,1,1,0]`，`20260803T200756805Z-929b74e11b494261941716a905a10563/summary.json` 为 `[1,1,1,0,1,1]`。其中 Task 4 首次观测的 SourceLookup EF InMemory 排序异常已经由 Task 8 的显式不同业务时间戳隔离关闭，不再登记为未修复 flake。

Task 8 首次终态六轮证据为 `artifacts/test-determinism/man-662/20260803T203911574Z-46fea15ab6ae4a6687fed1add88ad86b/summary.json`：6 轮、24 个项目运行全部 exit 0；对应 solution 证据 `artifacts/script-logs/man662-task8-fix2-full-solution/20260804-044915-206/` 为 66 个测试程序集、5849 passed、87 skipped、0 failed。最终 code review 修复后又以新 invocation `artifacts/test-determinism/man-662/20260804T053200000Z-final-review-fixes/summary.json` 重跑，exit 序列仍为 `[0,0,0,0,0,0]`。复审全解先后用 RED 日志 `artifacts/script-logs/man662-final-review-fixes-full-solution/20260804-054231-650/` 和 `artifacts/script-logs/man662-final-review-fixes-full-solution-green/20260804-055257-685/` 坐实并关闭了旧 sanitizer 断言与单次 scheduler yield 假设；最终 GREEN 为 `artifacts/script-logs/man662-final-review-fixes-full-solution-green2/20260804-060229-765/`：66 个测试程序集，5853 passed、87 skipped、0 failed，stderr 为空。MAN-661 仍只负责 lane timing、TRX、trend、skip/rerun 与 quarantine 的外部定量证据，当前状态继续是 `awaiting MAN-661`，不改变上述 MAN-662 本地 code-completion 结论。

## baseline schema 2：`expiring-debt` 与 `permanent` 两种分类（#1471）

#1471 清偿了 47 行 `StaticSetter`。清偿方式不是「把 setter 写得更礼貌」——checker 命中的是**赋值语句本身**，
包一层 try/finally 或在旁边 `await using` 一个 scope 都不会让它消失。真正的出路只有两条，本次两条都用上了：

1. **把静态写入搬进 `GlobalTestStateScope`。** 受审计的共享测试基建集中承担这些写入，测试体里不再散落裸
   setter。scope 只提供**当前真有调用点**的 `UseCulture`（同时设 current 与 current UI culture）与
   `SetEnvironmentVariable` 两个 mutator——首版还写了 `UseCurrentCulture` / `UseCurrentUiCulture` /
   `UseDefaultThreadCulture` / `UseDefaultThreadUiCulture` / `UsePropertyNameResolver` /
   `UseDisplayNameResolver` 六个，全仓零调用、零覆盖，等于推荐一段没有任何会变红的测试的代码，已删除；
   capture/restore 面**不跟着缩**：默认线程 culture 与 FluentValidation 两个 resolver 仍然被捕获与恢复，
   所以绕过 mutator 直接写这些静态的测试同样会被清理干净。需要新 mutator 时连同调用点与自测一起加。
   `SetEnvironmentVariable` 在**首次写入该变量时**捕获旧值，因此
   「忘了在 `CaptureAsync` 里报名」不再是静默且不可恢复的错误；dispose 后再调用任何 mutator 抛
   `ObjectDisposedException`，而不是做一次无人恢复的写入。三态（不存在 / 空串 / 有值）恢复语义未变。
   35 行按此迁移：`IamPostgresProfileTests`(11)、`BusinessGatewayProxyTests`(4)、
   `AppHubServiceReadinessTests`(4)、`IamRepositoryTests`(4)、`NervIipLocalizationTests`(4)、
   `IamManagementEndpointAuthorizationTests`(3)、`MaintenanceWorkOrderIdempotencyTests`(2)、
   `NervIipObservabilityRegistrationTests`(2)、`PerformanceMetricTests`(1)。各文件里手写的
   `PreserveEnvironment`/`RestoreEnvironment` 私有 helper 一并删除：它们只恢复不串行化，是 scope 的弱化复制品。
   FastEndpoints serializer/validation/discovery 类的静态变异**本批不存在**（实测这 47 行全是 culture 与
   环境变量），因此没有动用 collection serialization / 一次性进程隔离；那两条手段的适用面不变。

2. **隔离机制自身的位点重新分类为常设例外（22 行 / 23 个 finding）。** 分三处：`GlobalTestStateScopeTests.cs`
   的 12 行是自测，变异就是被测行为；`GlobalTestStateScope.cs` 的 9 行（含一行文本出现两次、占 10 个
   occurrence）是这条机制的实现本身，也就是仓库指定的静态写入落点，没有别处可搬；`BoundedObservationWindow.cs`
   的 1 行是有界轮询原语的 poll interval（见下一节「共享测试基建已纳入扫描」）。三者都不会随任何后续重构消失，
   给它们一个到期日是编造 deadline 而不是设 deadline。
   为此 baseline schema 升到 **2**，每行必须显式声明 `classification`：

   | classification | 必填 | 禁止 | 到期硬失败 |
   |---|---|---|---|
   | `expiring-debt` | `ownerIssue`、`exitCondition`、`expiresOn` | `rationale` | 是 |
   | `permanent` | `rationale` | `ownerIssue`、`exitCondition`、`expiresOn` | 否 |

   两组元数据**互斥**：permanent 行带 `expiresOn`、或 debt 行带 `rationale`，都直接判失败，而不是挑一个生效——
   混用会让人以为该行是按另一套规则审过的。`classification` 缺失或取值不在这两者之内同样失败，所以
   schema 1 的旧行不会被当成默认 debt 悄悄放行。

   防止 permanent 退化成万能豁免，靠三道锁：
   - **`路径=pattern` 白名单由 checker 自己持有**（`$PermanentAllowlist` 参数默认值，当前三条：
     `backend/tests/Nerv.IIP.Testing.Tests/GlobalTestStateScopeTests.cs=StaticSetter`、
     `backend/common/Testing/Nerv.IIP.Testing/GlobalTestStateScope.cs=StaticSetter`、
     `backend/common/Testing/Nerv.IIP.Testing/BoundedObservationWindow.cs=Task.Delay`）。**锁到 pattern 一级**
     是关键：只锁路径的话，白名单文件里将来出现的任何 pattern（`Thread.Sleep`、`ShortLease`…）都能拿一条为
     culture setter 写的理由蒙混过关，而「变异即被测行为」只覆盖 `StaticSetter`。baseline 不能把自己写进
     白名单——新增一个常设例外必须改脚本，走脚本治理与评审，而不是往 JSON 里再加一行。参数本身只是 checker
     自测 harness 的接缝，CI 与 `.\nerv.ps1` 调用一律不传参、用默认值。
   - **`rationale` 与 `reason` 都必填**：`reason` 逐行写「这一行为什么在这里写」，`rationale` 按常设理由
     分类写。当前四类：scope **之外**的前置/teardown（scope 造不出自己要恢复的状态）、scope **之内**的变异
     （变异即被测行为）、原语实现本身（没有别处可搬）、有界轮询原语的 poll interval（等待栈的底，无处可迁）。
     全文件一串样板等于没写。
   - **checker 自测覆盖被削弱的形态**（`scripts/tests/check-backend-test-determinism.Tests.ps1`）：白名单内通过、
     用**默认白名单**校验同一行必须失败（白名单一旦放宽成「permanent 即放行」，这条立刻变红）、白名单指向别的
     文件必须失败、**白名单未覆盖的 pattern（同一路径）必须失败、把该 pattern 写进白名单后必须通过**（这一对
     锁住 pattern 一级，白名单退回只锁 path 即变红）、白名单条目格式错误或 pattern 不受支持必须失败、
     permanent 带 debt 元数据失败、permanent 缺 `rationale` 失败、未知/缺失 `classification` 失败、
     debt 行带 `rationale` 失败。

   通过输出也随之细化为 `... admitted=N, expiringDebtRows=X, permanentRows=Y.`，「到期债务是否归零」在门禁输出里
   一眼可读，不必去数 JSON。

## 共享测试基建已纳入扫描（#1471 前是盲区）

`scripts/check-backend-test-determinism.ps1` 的 `Get-SolutionTestSourceFiles` 先 `dotnet sln list`，再用
`Test-IsTestProject` 过滤：项目名以 `Tests` 结尾，或 csproj 里写了 `<IsTestProject>true</IsTestProject>`。
`backend/common/Testing/` 下的三个项目（`Nerv.IIP.Testing`、`Nerv.IIP.Testing.Xunit`、`Nerv.IIP.Testing.PostgreSql`）
两个条件都不满足，因此**整个目录曾不在扫描清单里**。2026-08-06 实测：solution 共 162 个项目，按测试项目口径选中
66 个，`common/Testing` 下的 3 个项目全部落选；#1471 清偿前 checker 的输出为 `files=577, findings=89, admitted=47`。

#1471 把 35 处 culture / 环境变量写入迁进 `GlobalTestStateScope`，也就是把静态写入集中到了这条边界的**内侧**。
那一步把「不扫描 `common/Testing`」从顺带的事实升级成被依赖的设计前提，而该目录里的裸静态变异当时没有任何门禁
会发现——口头约定而已。因此 checker 现在**显式**把 `backend/common/Testing/**` 的项目并入扫描
（`Test-IsSharedTestingProject`），并在该目录一个项目都没选中时直接失败，避免改名/搬迁把覆盖面悄悄缩回去。
纳入后实测输出为 `files=597, findings=23, admitted=22, expiringDebtRows=0, permanentRows=22`：新增的 11 个
finding 全部登记为 `permanent`——`GlobalTestStateScope.cs` 的 10 个 `StaticSetter`（`Environment.SetEnvironmentVariable(name, value);`
一行文本出现两次，占一行 `occurrenceCount: 2`）与 `BoundedObservationWindow.cs` 的 1 个轮询 `Task.Delay`。
`admitted` 计的是去重后的 `path|pattern|hash` 键数，所以它比 `findings` 少 1 是这条重复行造成的，不是漏登记。
`Nerv.IIP.Testing.Xunit` 与 `Nerv.IIP.Testing.PostgreSql` 实测零 finding。

**曾落在盲区、现已登记或仍需人读的等待原语**：`Eventually.WaitAsync` / `Eventually.AssertAsync` 的轮询 `Task.Delay`、
`Consistently.StaysAsync` 的轮询 `Task.Delay`（两者现在共用 `BoundedObservationWindow`）、`ConcurrencyFanOutGate` 经由 `TestTimeout` 的预算等待，
以及 `PendingOperation`、`CapTestHost` 里的等待路径。

**为什么可以接受**：这些 `Task.Delay` 是**有界轮询的间隔**，不是 sleep-before-assert。它们的语义由
`Nerv.IIP.Testing.Tests` 里的直接单测钉住（超时必须报告 condition/attempts/elapsed/脱敏最后观测；caller
cancellation 原样传播；窗口关闭时在飞的观测在 grace 内被裁定、grace 也超时才抛 `ConsistentlyObservationTimeoutException` 而不是判 pass），
且每个原语都接受注入的 `TimeProvider`，测试用 `FakeTimeProvider` 驱动而不是等真实时间。换句话说：这一层的
正确性由行为断言保证，而不是由文本扫描保证。这与 main 上既有的 `Eventually.cs` 同源，不是本次新造的规避手段。

**回潮如何识别**：#1471 起，`Nerv.IIP.Testing` 里新增的 `Task.Delay` / `Thread.Sleep` / 静态 setter 会作为
**未登记 finding 直接变红**——白名单只覆盖 `BoundedObservationWindow.cs=Task.Delay` 与
`GlobalTestStateScope.cs=StaticSetter` 两条，别的文件、别的 pattern 都不在内。文本扫描仍然只能问「有没有」，
不能问「是不是有界轮询」：`Task.WhenAny(x, Task.Delay(...))` 无预算竞速、或读 `DateTimeOffset.UtcNow` 而不是
注入 `TimeProvider` 的计时，扫描器分不出好坏，只能在 code review 里拦。判据不变：新等待必须(1)接受
`TimeProvider`，(2)接受 caller 的 `CancellationToken` 并原样传播取消，(3)超时抛带诊断的专用异常，(4)在
`Nerv.IIP.Testing.Tests` 里有一条会在该性质被削弱时变红的测试。扫描范围到此为止：required/opt-in lane 与
enforcement 归 MAN-661，普通测试变更不得自行再改门禁口径。

## 第一轮走查修复（#1470 / PR #1482）

- **FileStorage 的过期语义收敛到单一注入时钟。** 上传会话/下载授权的 `ExpiresAtUtc`、文件的 `CompletedAtUtc`
  与 `PhysicalDeleteAfterUtc` 由 `PostgreSqlFileStorageService` 写、由 `PostgreSqlFileStorageGarbageCollector`
  读；写侧改注入时钟而读侧留在 `DateTimeOffset.UtcNow` 会让同一批列由两个时钟驱动。回退到「只改 `IsExpired`」
  并不能解决这一点：TUS 的 `IsExpired` 与 GC 读的是同一列，只要有任何一侧被注入，两个时钟就已经并存。因此收口
  方向是让**所有**读写这批列的路径解析同一个 `TimeProvider` 注册。`FileStoragePostgreSqlServiceTests` 的
  `GarbageCollector_ReadsUploadSessionExpiryThroughTheClockThatWroteIt` 与
  `GarbageCollector_KeepsSessionsWrittenByAClockAnchoredBehindTheWallClock` 在 GC 退回墙钟时**都会失败**
  （实测 2 FAIL）。扫描器写的 `ScannedAtUtc` 是审计戳、没有任何过期比较读它，刻意留在墙钟上。
- **`Consistently.StaysAsync` 区分「超时」与「违例」。** 窗口在首次观测返回前就到期时抛
  `ConsistentlyObservationTimeoutException`（`TimeoutException` 家族），而不是 `ConsistentlyViolatedException`。
  否则一次冷启动的 Docker PostgreSQL 查询会把「基础设施慢」误诊成「负向断言被违反」，而且诊断只能编造一个
  从未存在的「最后一次观测」。异常报告 condition（脱敏）、attempts 与 elapsed。
- **共享 fan-out gate。** MasterData SKU 明细与设备可用性两处的并发上限断言曾是同形状的两份手抄，且已经在
  「safety budget 是否接收 caller 的 `CancellationToken`」上分叉（前者不传，请求被取消时要挂满 30 s）。
  两处收编为 `Nerv.IIP.Testing.ConcurrencyFanOutGate`，跨两个测试程序集共用同一份语义。
- **`Eventually.AssertAsync`。** `ErpSalesOrderDemandConsumerTests`、`MesCapSubscriptionTests`、
  `NotificationCapOutboxAcceptanceTests` 三份「重试断言块直到成立」的 helper（观测类型还分两种形状）收编为
  一个原语。只重试断言形状的失败（`XunitException` 与 EF 的 `InvalidOperationException`），其余异常立即上抛，
  不被当成「还没就绪」重试满整个预算。为此 `Nerv.IIP.Testing` 引用了 xUnit 断言包（落点与白名单口径在第二轮
  收窄，见下节 S2/S3）。
- **显式预算/取消。** `ErpCostAccountingPostgresAcceptanceTests` 与 `MesPostgresAdvisoryLockProbe` 的探针
  连接改回显式 10 s 预算并接收 caller 的 `CancellationToken`（此前一处退回 Npgsql 默认 15 s，一处传
  `CancellationToken.None`）。

## 第二轮走查修复（#1470 / PR #1482）

- **窗口内的观测有了结构性预算（S1/S8）。** `Eventually.WaitAsync` 与 `Consistently.StaysAsync` 现在都经由共享驱动器
  `BoundedObservationWindow`（第二轮时叫 `Eventually.ObserveWithinWindowAsync`）发起观测：token 仍然交给 `observe`，但**即使 `observe` 完全忽略它**，
  窗口关闭时该次观测也会被放弃。此前一次卡住的 Npgsql 连接有两个不同的后果——`Eventually` 一侧是测试**挂死
  而不是变红**（最贵的失败形态），`Consistently` 一侧是**窗口静默退化成单次观测**，负向断言根本没有被复查。
  放弃掉的观测会显式消费它后来的异常，不会以 `UnobservedTaskException` 落到别的测试头上。
  `ObservationBudgetTests` 钉住这一层：删掉那个 `WaitAsync` 后它们从「失败」变成「挂起」，因此每条都自带有界
  外层预算。（其中「晚故障被谁消费」那一条在第三轮被证明并未真正钉住，见下节 A2。）
- **同时把 token 真正接到 IO 上。** `MaintenanceRuntimeHoursPostgresRedisAcceptanceTests.ReadFactsAsync`、
  `ErpWmsDeliveryCompletionPostgresRedisAcceptanceTests.CountSiblingConsumerReceiptsAsync`、
  `MesInventoryProducedLotPostgresRedisAcceptanceTests` 的 `ReadMesFactsAsync`/`ReadInventoryFactsAsync`
  改为**必填**的 `CancellationToken` 参数并传到 `OpenAsync`/`ExecuteReaderAsync`/`ReadAsync`；三处
  `Eventually.AssertAsync` 的 helper（Notification/MES/DemandPlanning）签名从 `Func<Task>` 改为
  `Func<CancellationToken, Task>`，token 一路进到 EF 查询与裸 ADO 诊断查询。必填参数本身就是「不许再丢」的
  编译期护栏。**这句穷举在第二轮写错过一次**（漏了 `ErpSalesOrderDemandConsumerTests` 等位点），第三轮重新
  逐个调用点核过，完整清单见下节「丢弃观测 token 的位点（完整清单）」——声明穷举就必须真穷举，不真穷举的
  穷举比不写更糟。
- **`Eventually.AssertAsync` 的白名单与它的契约对齐（S2）。** 原实现写的是
  `exception is XunitException or InvalidOperationException`，而 `ObjectDisposedException` 与 Npgsql 的
  `NpgsqlOperationInProgressException` 都**继承自** `InvalidOperationException`，于是注释里宣称「立即上抛」
  的那两类失败恰好会被重试满整个 30 s 预算再报成 timeout。现在改为**精确类型**判定
  （`exception.GetType() == typeof(InvalidOperationException)`，EF 的 `SingleAsync` 正好抛这个精确类型），
  派生类型一律立即上抛。`EventuallyAssertTests` 双向钉住：plain 与 `XunitException` 重试、
  `ObjectDisposedException`/派生类型/无关异常在第一次尝试就原样抛出。
- **xUnit 依赖收到最窄落点（S3）。** `Nerv.IIP.Testing` 此前引 `xunit` 元包（core + assert + analyzers），
  实际只用到 `Xunit.Sdk.XunitException` 一个类型，该类型位于 `xunit.assert`，故改为只引 `xunit.assert`。
  **同目录的 `Nerv.IIP.Testing.Xunit` 评估后不是更窄落点**：它带的是 `xunit.extensibility.core`，而
  `xunit.core.dll` 里根本没有 `XunitException`（仍要额外加 `xunit.assert`）；把 `AssertAsync` 挪过去还会
  把它与它转调的 `WaitAsync` 拆开，而它的三个真实消费方（Erp/Mes/Notification 验收测试）引用的是
  `Nerv.IIP.Testing` 而不是 `Nerv.IIP.Testing.Xunit`。边界口径同时更正：22 个消费方中 21 个是 xUnit 测试
  项目，第 22 个是 `common/Testing` 下的共享库 `Nerv.IIP.Testing.PostgreSql`（checker 的
  `Test-IsTestProject` 也判它不是测试项目）；真正成立的不变量是**没有任何可发布程序集**引用它们。
- **假时钟推进的屏障换成显式边沿（S4）。** `ApprovalOverdueSchedulerTests` 的 `clock.Advance(1h)` 此前以
  「已派发一条命令」为屏障，这只在 `ApprovalOverdueScheduler.ExecuteAsync` 今天恰好先建 `PeriodicTimer`
  再调 `TryCheckAllScopesAsync` 时成立——两行调换顺序，测试就静默挂死。现在改为等
  `TimerRegistrationObservingTimeProvider.WaitForTimerCountAsync(1)`，即计时器**注册**这件事本身。为此
  `TimerRegistrationObservingTimeProvider` 与 `BoundedSignal` 从 `Nerv.IIP.Testing.Tests` 提升为
  `Nerv.IIP.Testing` 的公开设施（原 `FakeClockBarriers.cs` 删除；`BoundedSignal` 第三轮再迁到自己的文件），业务测试程序集可以直接用同一道屏障。
- **`ConcurrencyFanOutGate.StaysWithinAsync` 的 describe 改为描述被观测值（S5）。** 原来是
  `describe: _ => Describe(InFlight)`：丢掉传入的 `MaxInFlight` 改为在诊断时刻**重读** `InFlight`，报出来的
  可能不是触发违例的那个值。
- **`ConsistentlyObservationTimeoutException` 去掉恒为 0 的 `attempts`（S6）。** 该异常只在
  `!observedAtLeastOnce` 分支抛出，所以「已完成观测数」结构上恒为 0、「已发起观测数」结构上恒为 1，两者都是
  伪装成诊断的常量。现在只保留 condition 与 elapsed，消息里那句「(0 completed observations)」一并删除。
  **该结论在第三轮被 A1 推翻**：加上 grace 裁定之后完成观测数不再恒为 0，`CompletedObservations` 重新加回，
  见下节。
- **`ProcessMemorySampler` 的 J9 契约补齐另一半（S7）。** 此前只覆盖「取过样之后 stop，信号仍是
  `RanToCompletion`」；新增 `FirstIntervalSampleTaken_is_cancelled_when_the_sampler_stops_before_its_first_tick`
  覆盖「从未 tick 就结束时必须取消」——采样间隔取 1 小时，让「从未 tick」是结构事实而不是竞态。

## 第三轮走查修复（#1470 / PR #1482）

- **`Consistently.StaysAsync` 窗口关闭时不再放弃在飞观测（A1）。** 第二轮把「窗口静默退化」的退化点后移了一次
  观测而已：窗口关闭时若已有 ≥1 次完成观测，原实现直接 `return lastObservation`，被放弃的那次观测继续跑完但
  结论被丢弃。**最可能暴露违例的恰恰是那一次观测**（它跨过了整个窗口的尾部），丢掉它 = 负向断言的灵敏度被
  静默削弱，真阳性可能被吞——与 spec 第 3 条「第一次观测到额外事件即失败」直接冲突。现在：
  - 在飞观测拿到的**不是**窗口 token，而是只与 caller 绑定的 token，窗口关闭不再打断它；
  - 窗口关闭后它在一个**独立的** grace 预算内被等完，然后照常裁定：违例就抛
    `ConsistentlyViolatedException`，成立就作为最后一次观测返回；
  - grace 也超时则抛 `ConsistentlyObservationTimeoutException`（`TimeoutException` 家族）——**verdict
    unknown，既不是 pass 也不是违例**；
  - grace 默认取 `options.Timeout`（与调用方已经认可的窗口同数量级），可由 `observationGrace` 覆盖。

  `ConsistentlyObservationTimeoutException` 因此改带 `CompletedObservations` 与 `Grace`。第二轮 S6 曾以
  「attempts 恒为 0，是伪装成诊断的常量」为由删掉计数——该理由随本改动失效：现在它是 0（窗口内一次都没读到）
  或 N（前 N 次干净、尾部那次读不到），两种情况的读法完全不同，消息也分别措辞。

  **护栏**：`ConsistentlyTests.StaysAsync_AdjudicatesAnObservationThatStartedInsideTheWindowAndFinishedAfterItClosed`
  构造「窗口 T-ε 才翻转」的观测并断言仍抛 `ConsistentlyViolatedException`；把裁定退回旧行为（`grace: null`）
  实测变红，报 `The stability window ended without adjudicating the observation that was in flight when it
  closed. Verdict: returned commands=1 (a pass)`（同批 6 条变红）。
- **`Eventually` 一侧刻意不跟进。** 正向断言的预算存在的意义就是拒绝「满足了，但晚了」，所以窗口关闭时它继续
  放弃在飞观测。两侧语义不同这件事写在 `BoundedObservationWindow` 的 XML 里，抽共享驱动器时不许抹平。
- **两个原语的逐行克隆收编为 `BoundedObservationWindow`（A6）。** 8 条参数校验、时钟与 linked CTS、观测/裁定/
  轮询循环、两条 cancellation 过滤器只剩一份；裁定语义（adjudicate / onWindowClosed / grace 策略）作为
  callback 留在各自一侧。分叉风险此前已可见（两侧 attempts 递增与脱敏时机已经不一致）。
- **被放弃观测的资源前提写进契约（A4）。** `BoundedObservationWindow` 的 XML 现在把它写成不变量：**`observe`
  必须自持它触碰的一切资源**（连接、DI scope、`DbContext`），因为它可能在启动它的窗口结束之后仍在运行。
  `ReadFactsAsync` 自建 `NpgsqlConnection`，三处 `Eventually.AssertAsync` 都在 lambda 内 `CreateScope()`；
  当时漏掉的唯一违反位点是 `MesPostgresAdvisoryLockProbe`（闭包复用调用者作用域的连接），已在第五轮修正——
  见下文。违反的症状是 EF 的「A second operation was started on this context instance」从某个不相干的
  后续行抛出，因此与下一条互为兜底。
- **`Eventually.AssertAsync` 白名单补上 EF 拼写（A5）。** EF Core 的并发使用错误是**精确**
  `InvalidOperationException`，第二轮的精确类型判据只挡住了 Npgsql 的 `NpgsqlOperationInProgressException`
  拼写，EF 拼写会被重试满 30 s 再报 timeout。现在按 EF 自己的 `CoreStrings.ConcurrentMethodInvocation` 措辞
  排除，措辞钉在真实 EF 程序集上（改词即变红）。
  **实测记录（EF Core 10.0.8，2026-08-06）**：持有 context 自己的 `IConcurrencyDetector` 临界区后，再次进入该
  临界区，以及 `ToListAsync`/`SaveChangesAsync`/`FindAsync`/`AnyAsync`，**全部未抛出**——EF Core 10 无法被
  确定性地驱动抛出该异常，剩下的唯一办法是真线程竞态，那会是 flaky 测试。因此测试如实声明：它钉的是**措辞与
  行为**，**不钉运行时类型**；类型判断若有误只会让该分支失效（派生类早已被精确类型判据立即上抛），不会造成危害。
- **`BoundedSignal` 落点与预算（A7）。** 从 `TimerRegistrationObservingTimeProvider.cs` 迁到
  `BoundedSignal.cs`；5 s 预算从不可注入的 `private static readonly` 改为带默认值的参数
  （`BoundedSignal.DefaultBudget` / `TimerRegistrationObservingTimeProvider(registrationBudget:)`）。
- **A2：一条自称钉住却没钉住的用例改真。** `ObservationBudgetTests` 里那条用例的 XML 写着「fault 必须被原语的
  `ContinueWith` 消费」，但用例自己 `await` 了被放弃的 task——**这个 await 本身就消费了异常**。实测把
  `ConsumeLateFault` 整段删空后 86 条测试仍全绿。现在拆成两条：
  - verdict 一条保留原形状，XML 如实写明**它不钉 `ContinueWith`**；
  - `TheLateFaultOfAnAbandonedObservationIsConsumedByThePrimitive` 真钉住：全程不持有被放弃 task 的强引用
    （只留 `WeakReference`，创建它的 helper 标 `NoInlining`），挂 `TaskScheduler.UnobservedTaskException`
    并强制 GC/终结器，按唯一 marker 过滤掉并行测试的串扰。实测把 `ConsumeLateFault` 删空后**只有这一条**变红：
    `The abandoned observation's fault reached TaskScheduler.UnobservedTaskException, which means the
    bounded-window driver no longer consumes it`。

### 丢弃观测 token 的位点（完整清单）

第二轮那句「剩余只有 StackExchange.Redis 那几处 + `ConcurrencyFanOutGate`/`ApprovalOverdueSchedulerTests`」
**不成立**。2026-08-06 对全部 `Eventually.WaitAsync` / `Eventually.AssertAsync` / `Consistently.StaysAsync`
调用点逐个核过，丢弃 token 的 observe 共 13 处（不含 `Nerv.IIP.Testing.Tests` 内部刻意如此的用例）：

| 位点 | 类别 |
| --- | --- |
| `MaintenanceLifecycleDockerAcceptanceTests`（Redis PING/Connect） | StackExchange.Redis 无 `CancellationToken` 重载 |
| `MesInventoryProducedLotPostgresRedisAcceptanceTests`（`StreamGroupInfoAsync`） | 同上 |
| `MaintenanceRuntimeHoursPostgresRedisAcceptanceTests`（`StreamGroupInfoAsync`） | 同上 |
| `ConcurrencyFanOutGate` × 2（`InFlight` / `MaxInFlight`） | 内存计数器，不可能阻塞 |
| `ErpSalesOrderDemandConsumerTests` × 2（`InjectedFailureCount` / `AttemptCount`） | 内存计数器（第二轮**漏登记**，本轮补注释） |
| `ApprovalOverdueSchedulerTests` × 2 | 内存计数器 |
| `InventoryReservationExpirationTests` × 2（Prometheus 文本导出到 `MemoryStream`、内存字段读取） | 内存；main 上既有，本 PR 未改动 |
| `MaintenancePlanDueSchedulerTests` × 2（`sender.LastCommand` / `sender.Attempts`） | 内存；main 上既有，本 PR 未改动 |

Redis 三处的预算由 multiplexer 自己的 connect/sync timeout 加上 `BoundedObservationWindow` 的结构性放弃提供；
内存那十处不可能阻塞，丢 token 不构成丢预算。两类都在位点处逐条注明（本 PR 未改动的两个文件除外）。

## 第四轮走查修复（#1470 / PR #1482）

- **checker 数字更正（B1）。** 上一节曾写 `files=576`；2026-08-06 实跑 `pwsh
  scripts/check-backend-test-determinism.ps1` 的输出是
  `Backend test determinism check passed: files=577, findings=89, admitted=47.`，已按实测更正。同批复核的其余
  数字与实测吻合：`dotnet sln backend/Nerv.IIP.sln list` 为 162 个项目，按 `Test-IsTestProject` 口径选中 66 个，
  `backend/common/Testing/` 下 3 个项目全部落选；`backend/test-determinism-baseline.json` 为 47 行、全部
  `StaticSetter`；上表丢 token 位点合计 13 处。（这 47 行已由 #1471 清偿，当前值见上面的 schema 2 一节；
  此处保留的是第四轮走查当时的实测快照。）
- **`Consistently.StaysAsync` 的假时钟使用约束写进契约（B2）。** grace 计时器是在**窗口关闭那一刻**才用注入的
  `TimeProvider` 创建的，所以 `FakeTimeProvider` 下只 `Advance` 一次只能关掉窗口，随即注册的 grace 计时器仍未
  到期；若在飞观测永不返回，调用会**永久挂起而不是变红**——正是 MAN-799 与 MAN-663 各踩过一次的「计时器晚注册」
  形态。XML 现在明确要求推进第二次，并指向正面钉住它的
  `ObservationBudgetTests.StaysAsync_ReportsATimeoutWhenTheObservationIgnoresItsTokenPastTheWindowAndTheGrace`
  （删掉它的第二次 `Advance` 即复现挂起）。
- **`describe` 的取值时机写成不变量，并且不再能顶替它要解释的失败（B3）。** `describe` 只在失败路径上求值一次
  （超时的最后观测、或违例的那次观测），不是每轮观测后立即求值。两件事因此成立：
  - **观测必须是值快照。** 若观测是活句柄（`DbContext`、观测自己已释放的 scope 里的实体、别的线程仍在改的集合、
    未关闭的连接），诊断报告的会是**诊断时刻**的状态而不是失败时刻的状态，或者干脆在已释放资源上抛异常。
    `Eventually.WaitAsync` 的 XML 现在把它写成不变量，`Consistently.StaysAsync` 引用同一条。
  - **穷举复核结果：当前全部 27 个 `describe` 都是值快照，零活对象。** 逐个位点见下表。
  - **`describe` 自己抛异常不再让诊断消失。** 此前它会**顶替** `EventuallyTimeoutException` /
    `ConsistentlyViolatedException`，测试只会看到「某个 lambda 抛了 NRE」，而 condition / attempts / elapsed
    全部丢失。新增 `BoundedObservationWindow.SafeDescribe`：抛异常时降级为脱敏的
    `<describe threw {类型}: {消息}>` 占位串，原始失败照常抛出。占位串同样过 `TestDiagnostic.Sanitize`。
    护栏：`EventuallyTests.WaitAsync_StillReportsTheTimeoutWhenDescribeItselfThrows` 与
    `ConsistentlyTests.StaysAsync_StillReportsTheViolationWhenDescribeItselfThrows`（`Nerv.IIP.Testing.Tests`
    因此从 86 条增至 88 条）。

### `describe` 位点穷举（27 处，全部为值快照）

| 位点 | 观测类型 | 判定 |
| --- | --- | --- |
| `Eventually.AssertAsync`（内部） | `Exception?` | 只读 `GetType().Name` 与 `Message`，均为已固化的字符串 |
| `ConcurrencyFanOutGate.WaitForInFlightAsync` | `int` | 值；闭包另读 `MaxInFlight`/`TotalEntries` 作**补充**上下文，已在位点注明 |
| `ConcurrencyFanOutGate.StaysWithinAsync` | `int` | 同上，判定值领先、活计数器仅作补充 |
| `MaintenanceLifecycleDockerAcceptanceTests` × 2（PG/Redis 就绪） | `(bool, Exception?)` | 值 + 已固化的异常文本 |
| `InventoryDirectoryPostgresTests`（PG 就绪） | `(bool, Exception?)` | 同上 |
| `ErpWmsDeliveryCompletionPostgresRedisAcceptanceTests` | `int` | 值；闭包只含 `int` 局部量 |
| `SalesOrderDemandPlanningPostgresRedisAcceptanceTests` | `int` | 值 |
| `MaintenanceRuntimeHoursPostgresRedisAcceptanceTests` × 3（`DescribeFacts`） | `MaintenanceFacts` | 只含标量的只读快照，scope 内读完即脱离 |
| `MaintenanceRuntimeHoursPostgresRedisAcceptanceTests`（Redis 消费组） | `string` | 值 |
| `MesInventoryProducedLotPostgresRedisAcceptanceTests`（双库事实） | 标量元组 | 值 |
| `MesInventoryProducedLotPostgresRedisAcceptanceTests`（消费组） | `List<string>` | **每次观测新建**的列表，无共享可变状态 |
| `MesPostgresAdvisoryLockProbe` | `int` | 值 |
| `InventoryReservationExpirationTests` × 2 | `string` / `decimal` | 值（Prometheus 导出文本、已读出的数量） |
| `DemandPlanningEndpointContractTests` | `MrpRun` | `AsNoTracking` 且 scope 已释放；只读已物化的标量 `Status`/`FailureReason`，无导航属性 |
| `ErpSalesOrderDemandConsumerTests` × 3 | `int` / 标量元组 | 值；闭包另读 probe 计数器作补充上下文 |
| `MaintenancePlanDueSchedulerTests` × 2 | 命令对象 / `int` | 命令的 `BusinessDate` 为不可变标量 |
| `ErpCostAccountingPostgresAcceptanceTests` | `int` | 值 |
| `IndustrialTelemetryHistorianTests` | `(bool, bool)` | 值；`DbContext` 在 observe 内 `await using` 自持并释放 |
| `ApprovalOverdueSchedulerTests` × 2 | `int` / `bool` | 值 |

三处「闭包另读活计数器」（`ConcurrencyFanOutGate` × 2、`ErpSalesOrderDemandConsumerTests` × 3）是**有意**的：
决定裁决的观测值排在最前，活读数只跟在后面做补充，位点处已有注释说明。它们不构成活对象观测。

## 第五轮走查修复（#1470 / PR #1482）

- **`MesPostgresAdvisoryLockProbe` 的 observe 改为自持连接（C1）。** 这是仓库里最后一处违反
  `BoundedObservationWindow` 资源不变量的位点：`observe` 闭包复用 `WaitForWaitersAsync` 作用域里的
  `NpgsqlConnection`/`NpgsqlCommand`。`Eventually` 是正向断言、**不给 grace**，窗口关闭时在飞观测被直接放弃，
  随后方法返回触发 `await using` 释放——被弃观测与释放竞态成立（后果是一个被 `ConsumeLateFault` 吞掉的
  `ObjectDisposedException`，即「症状被吞、不变量被破」的最坏组合）。现在每次观测在 lambda 内
  `await using` 自建连接与命令：被弃观测自带资源，随窗口 token 取消后把连接还给 Npgsql 池，方法返回时不再有
  共享对象被释放。显式 10 s 连接预算与 caller token 传递（第二轮 S1/H3）原样保留，`connectionString` 继续作为
  sensitive value 传给 `TestTimeout` 与 `EventuallyOptions`。
- **开销实测（2026-08-06，`postgres:18` 容器，各 3 轮）。** 最坏情况是 15 s 预算 / 50 ms 轮询 = 约 300 次
  open/close，但每测试库连接串未关闭池化（只有 admin 串设 `Pooling=false`），因此除首次外都是池的租/还。三个
  调用点（`MesSchedulePlanProvenancePostgresTests`、`SkuDisabledConsumerTests`、
  `WorkOrderCapitalizationConcurrencyPostgresTests`，共 10 条用例）实测墙钟：改前 5.19 / 5.79 / 4.41 s，
  改后 4.40 / 4.16 / 4.25 s——无退化（差异落在噪声内，实际轮询次数远小于最坏值，因为等待边沿在数十毫秒内出现）。

## MAN-808 假时钟锚点与 Inventory 过期 worker 第二趟屏障（#1491 / PR #1501）

- **`TimerRegistrationObservingTimeProvider` 新增锚定起点的构造函数
  （`(DateTimeOffset startDateTime, TimeSpan? registrationBudget = null)`）。** 注入 `TimeProvider` 只覆盖「读注入
  时钟」那一面；下游可能还有读**进程 wall clock** 的领域守卫，`StockReservation.NormalizeFutureUtc`（「到期时间必须
  在未来」）就是一条。这类被测对象配上停在 2000 年的默认 `FakeTimeProvider` 会直接抛，假时钟必须锚在真实 now 再从
  那里 `Advance`。锚点做成**构造参数**而不是事后 `SetUtcNow`：向前设时钟会立即触发所有**触发点已被这次跳跃跨过**
  的已注册计时器（锚定这一跳通常宽达几十年，实际就是全部），「先构造、后锚定」只在还没人注册时才安全——那正是一条
  隐式顺序假设，而这个类存在的意义就是让测试不再依赖顺序。原无参构造函数（A7 的 `registrationBudget:`）语义不变，
  既有调用点零改动；`TimerRegistrationObservingTimeProviderTests` 钉住：起点即给定值、注册边沿照常发布、
  **两个**构造函数都拒绝非正预算且 `ParamName` 是调用者可见的 `registrationBudget`。
- **`WaitForTimerCountAsync(n)` 数的是这口时钟上的累计注册数，不是「某个组件的计时器」。** 调用点必须自证「本宿主
  里只有一个计时器注册方」，否则第二个组件的注册会静默满足屏障。**这条前提由计数断言钉住**
  （#1502）：两个消费点（`InventoryReservationExpirationTests`、`ApprovalOverdueSchedulerTests`）都在
  **`StopAsync` 之后**加了 `Assert.Equal(1, clock.TimersCreated)`——前提被削弱（多出第二个注册方，或 worker 循环
  改成逐轮注册）时计数改变，每次跑都确定性红且直指前提，而不是退化成间歇红。注释不算保障、可执行断言才算，与
  「互斥门必须有一个在门被削弱时会失败的回归测试」是同一条治理先例。
  **断言位置是这条保障的一部分**（#1502 走查）：多出第二个注册方那条主危害与位置无关（断言时计数已是 2），
  但逐轮注册那条分支里，下一轮的注册发生在「第二趟结果已可观测」**之后**，停机前断言会与它赛跑。因此两处断言都
  放在 `StopAsync` 之后——`StopAsync` 会 await `ExecuteTask`，循环退出前的最后一次注册必然已计入，赛跑变成结构
  保证。实测（把 Inventory worker 改成逐轮新建 `PeriodicTimer`）：窗口不放大时停机前位置也 3/3 红（本机赢了这场
  赛跑），但按 MAN-808 同一手法在 `RunOnceAsync` 与下一次构造之间插 1.5 s 放大重注册窗口后，停机前位置变绿（假
  绿），停机后位置仍 3/3 报 `Assert.Equal() Failure: Expected 1, Actual 2`。两次临时改动均已撤销。
- **Inventory 过期 worker 第二趟（#1491）。** `ExpiredStockReservationHostedService` 先无条件跑一趟 `RunOnceAsync`，
  **返回之后**才 `new PeriodicTimer(interval, timeProvider)`，而 `expired_total` 在第一趟内部就已写入。原测试以
  「metric 出现」为屏障随即 `Advance`，一旦落进这个窗口，tick 永久丢失、第二趟永不发生（CI 上表现为 153 次观测全部
  `openQuantity=1`，即 worker 根本没再跑，放宽预算无效）。现改为等 `WaitForTimerCountAsync(1)`，**生产代码零改动**。
  实测护栏：在第一趟与计时器构造之间临时插 1.5 s 延时放大注册窗口，去掉屏障复现同一条 `EventuallyTimeoutException`
  （`openQuantity=1`），加回屏障即绿。

## MAN-650 迁移状态

| MAN-650 项 | 状态 |
| --- | --- |
| Maintenance Redis renewal | migrated by MAN-662 |
| Inventory expiry metric | migrated by MAN-662 |
| Ops Production fake credential | migrated by MAN-662 |
| IndustrialTelemetry out-of-range/order-sensitive host surface | isolated here, structural split tracked by MAN-664 |
