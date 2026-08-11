# NERV-789 Ops IAM 取消宿主回归 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 Production Ops 宿主补齐 IAM 抛裸 `OperationCanceledException` 时 endpoint 仍失败关闭为 401 的可证伪回归证据。

**Architecture:** 保留真实 Production 宿主、内部服务认证、连接器认证与 endpoint 管线，只扩展既有进程内 IAM handler，使其可返回 HTTP 响应或抛异常。生产 catch 已在 `main` 正确实现，本计划不修改生产语义；通过临时削弱 catch 的本地变异证明新增宿主测试能捕获回归。

**Tech Stack:** .NET 10、ASP.NET Core `WebApplicationFactory`、xUnit、`IHttpMessageHandlerBuilderFilter`。

## Global Constraints

- 仅修改 Ops 测试与中文工程规格/计划；不修改业务 endpoint、API 契约、数据库、Gateway、前端或共享测试基建。
- 调用方令牌已取消时必须原样传播；调用方未取消的裸 `OperationCanceledException` 才按 helper 自有超时失败关闭。
- Production 宿主测试不得恢复不可达端口或真实网络依赖。
- 测试必须断言 IAM 恰好调用一次，防止其它认证短路产生同为 401 的假绿。

---

### Task 1: 补齐 Production 宿主裸取消回归

**Files:**
- Modify: `backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/OperationTaskEndpointTests.cs:367`
- Modify: `backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/OpsTestHostIsolation.cs:93`
- Test: `backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/OperationTaskEndpointTests.cs`

**Interfaces:**
- Consumes: `CreateProductionFactory(StubbedIamCredentialHandlerFilter iam)`、`IamOpsConnectorCredentialValidator.ValidateAsync(...)`。
- Produces: `StubbedIamCredentialHandlerFilter(Exception exception)`；异常脚本执行时递增 `RequestCount` 并返回 faulted `Task<HttpResponseMessage>`。

- [x] **Step 1: 写入尚不能编译的宿主回归测试**

在既有 `Production_does_not_accept_development_fake_connector_credential` 后新增：

```csharp
[Fact]
public async Task Production_fails_closed_when_iam_request_has_helper_owned_cancellation()
{
    var iam = new StubbedIamCredentialHandlerFilter(
        new OperationCanceledException("helper-owned timeout"));
    await using var productionFactory = CreateProductionFactory(iam);
    var client = CreateInternalServiceClient(productionFactory, ProductionInternalServiceToken);
    AddConnectorHeaders(client, DevelopmentFakeConnectorSecret);

    var response = await client.GetAsync(
        "/api/ops/v1/operation-tasks/pending?organizationId=org-001&environmentId=env-dev&connectorHostId=connector-host-001&take=10");

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    Assert.Equal(1, iam.RequestCount);
}
```

- [x] **Step 2: 运行测试并确认 RED 是缺少异常构造能力**

Run:

```bash
dotnet test backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj \
  --configuration Release --no-restore \
  --filter "FullyQualifiedName=Nerv.IIP.Ops.Web.Tests.OperationTaskEndpointTests.Production_fails_closed_when_iam_request_has_helper_owned_cancellation"
```

Expected: 编译失败，指出 `StubbedIamCredentialHandlerFilter` 不能以 `OperationCanceledException` 构造；失败原因只能是尚未实现脚本化异常路径。

- [x] **Step 3: 最小扩展 IAM 测试 handler**

将固定状态码 primary constructor 改成私有脚本字段与两个窄构造函数：

```csharp
internal sealed class StubbedIamCredentialHandlerFilter : IHttpMessageHandlerBuilderFilter
{
    private readonly Func<Task<HttpResponseMessage>> response;
    private int requestCount;

    public StubbedIamCredentialHandlerFilter(HttpStatusCode statusCode)
        : this(() => Task.FromResult(new HttpResponseMessage(statusCode)))
    {
    }

    public StubbedIamCredentialHandlerFilter(Exception exception)
        : this(() => Task.FromException<HttpResponseMessage>(exception))
    {
    }

    private StubbedIamCredentialHandlerFilter(Func<Task<HttpResponseMessage>> response)
    {
        this.response = response;
    }

    public int RequestCount => Volatile.Read(ref requestCount);

    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return builder =>
        {
            next(builder);
            if (builder.Name != nameof(IamOpsConnectorCredentialValidator))
            {
                return;
            }

            builder.PrimaryHandler = new ScriptedHttpMessageHandler((_, _) =>
            {
                Interlocked.Increment(ref requestCount);
                return response();
            });
        };
    }
}
```

- [x] **Step 4: 运行 targeted 测试确认 GREEN**

Run: 重复 Step 2 的命令。

Expected: `1 passed / 0 failed / 0 skipped`。

- [x] **Step 5: 临时削弱生产 catch，证明测试有判别力**

仅在本地把 `OpsConnectorCredentialValidation.cs` 的第二个：

```csharp
catch (OperationCanceledException ex)
```

临时改为：

```csharp
catch (TaskCanceledException ex)
```

重新运行 Step 2 命令。Expected: 新宿主测试失败，裸 `OperationCanceledException` 从完整 endpoint 管线逃逸；这证明测试能捕获 NERV-789 所述回归。随后使用 `apply_patch` 精确恢复 `catch (OperationCanceledException ex)`，不得把变异提交。

- [x] **Step 6: 恢复后重跑相关取消合同**

Run:

```bash
dotnet test backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj \
  --configuration Release --no-restore \
  --filter "FullyQualifiedName~OperationTaskEndpointTests.Production_|FullyQualifiedName~OpsConnectorCredentialValidationTests.Caller_cancellation_propagates|FullyQualifiedName~OpsConnectorCredentialValidationTests.Helper_owned_cancellation"
```

Expected: 新旧 Production 宿主用例、caller cancellation 与 helper cancellation 用例全部通过，0 failed、0 skipped。

- [x] **Step 7: 提交测试改动**

```bash
git add backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/OperationTaskEndpointTests.cs \
  backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/OpsTestHostIsolation.cs \
  docs/superpowers/plans/2026-08-11-nerv-789-ops-iam-cancellation-host-regression.md
git commit -m "test(ops): cover helper-owned IAM cancellation at host boundary"
```

### Task 2: 多轮验证与交付

**Files:**
- Verify: `backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/**`
- Verify: `backend/services/Ops/src/Nerv.IIP.Ops.Web/Application/Auth/OpsConnectorCredentialValidation.cs`
- Verify: `scripts/check-backend-test-determinism.ps1`

**Interfaces:**
- Consumes: Task 1 的新宿主回归与既有 Ops 后端测试门禁。
- Produces: 本地执行证据、GitHub PR、Linear `In Review` 状态和验收评论。

- [ ] **Step 1: 四路并发运行新用例 20 轮**

每轮使用 `--no-build --no-restore`，最多四个并发进程；逐日志核对 `1 passed / 0 failed / 0 skipped`，不得只看聚合脚本退出码。

- [ ] **Step 2: 运行 Ops 全程序集**

```bash
dotnet test backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj \
  --configuration Release --no-restore
```

Expected: 全部常规用例通过；环境门控用例如有 skipped，按既有基线单独报告。

- [ ] **Step 3: 运行受影响治理门禁**

```bash
pwsh scripts/check-backend-test-determinism.ps1
git diff --check origin/main...HEAD
git status --short
```

Expected: checker 通过、diff 无空白错误、工作树干净。

- [ ] **Step 4: 推送并创建 PR**

PR 标题使用中文并关联 `Fixes #1435`。描述必须写明：代码快照已由 #1468/#1526 修复，本 PR 补宿主层裸取消回证；产品文档无影响；未变更业务 endpoint，因此 facade declaration 不适用；列出 RED 变异、20 轮负载、Ops 全程序集和 checker 的实际结果。

- [ ] **Step 5: 更新 Linear 等待审核**

将 NERV-789 状态更新为 `In Review`，评论包含 PR 链接、测试计数、变异结果与剩余远端 CI 状态；不得在 CI pending 时宣称远端绿色。
