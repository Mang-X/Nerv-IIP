# 业务平台第 3 波 ERP 注册、验证与就绪状态实施计划

> **供代理执行者使用：**必须使用子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项任务实施本计划。步骤使用复选框（`- [ ]`）语法跟踪进度。

**目标：**在 #137、#138 和 #139 服务切片就绪后，协调 ERP 的共享集成工作。

**架构：**ERP 服务切片负责领域行为。本计划负责共享解决方案、AppHost、IAM、schema、就绪状态和脚本集成，并为全链路验收 #77 做准备。

**技术栈：**.NET 10、Aspire AppHost、IAM 初始数据、受治理的 PowerShell 脚本、Markdown 架构文档。

---

## 规格

使用：

1. `docs/superpowers/specs/2026-05-23-business-wave-3-agent-session-design.md`
2. `docs/superpowers/specs/2026-05-23-erp-procurement-sales-finance-mvp-design.md`

## 文件

- 修改：`backend/Nerv.IIP.sln`
- 修改：`infra/aspire/Nerv.IIP.AppHost/Program.cs`
- 修改：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs`
- 修改：`docs/architecture/authorization-matrix.md`
- 修改：`docs/architecture/database-schema-catalog.md`
- 修改：`docs/architecture/implementation-readiness.md`
- 修改：`README.md`
- 创建：`scripts/verify-business-erp-procurement-mvp.ps1`
- 创建：`scripts/verify-business-erp-sales-mvp.ps1`
- 创建：`scripts/verify-business-erp-finance-mvp.ps1`
- 创建：`scripts/verify-business-erp-procurement-sales-finance-mvp.ps1`

## 任务 1：确认 ERP 切片就绪状态

- [ ] **步骤 1：检查切片交接信息**

阅读以下切片的最终摘要：

1. #137 ERP 采购
2. #138 ERP 销售
3. #139 ERP 财务

将每个 `Shared Changes Needed` 章节复制到集成摘要中。

- [ ] **步骤 2：验证本地项目是否存在**

运行：

```powershell
rg --files backend/services/Business/Erp
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/Nerv.IIP.Business.Erp.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/Nerv.IIP.Business.Erp.Web.Tests.csproj --no-restore
```

预期：ERP 项目存在，且聚焦测试通过。

## 任务 2：添加解决方案和 AppHost 注册

- [ ] **步骤 1：添加解决方案条目**

将 ERP Domain、Infrastructure、Web、Domain.Tests 和 Web.Tests 项目添加到 `backend/Nerv.IIP.sln`。

- [ ] **步骤 2：构建后端解决方案**

运行：

```powershell
dotnet build backend/Nerv.IIP.sln --no-restore
```

预期：解决方案构建成功。如果 ERP 失败，应将阻塞问题退回所属切片，不得将其隐藏在集成工作中。

- [ ] **步骤 3：在 AppHost 中注册 ERP**

添加 PostgreSQL 数据库：

```csharp
var businessErpDatabase = postgres.AddDatabase("business-erp-db", "nerv_iip_erp");
```

将服务注册为 `business-erp`；除非端口矩阵已经变更，否则使用本地端口 `5118`：

```csharp
var businessErp = builder.AddProject<Projects.Nerv_IIP_Business_Erp_Web>("business-erp")
    .WithHttpEndpoint(port: 5118, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otelCollector.GetEndpoint("otlp-http"))
    .WithEnvironment("OpenTelemetry__Protocol", "HttpProtobuf")
    .WithReference(businessErpDatabase, "PostgreSQL")
    .WaitFor(businessErpDatabase)
    .WaitFor(otelCollector);
```

按照现有的 `rabbitmq is not null` 模式添加 RabbitMQ 引用；如有需要，添加 Gateway 引用，并将 `businessErp` 纳入 Gateway 引用列表。

- [ ] **步骤 4：构建 AppHost**

运行：

```powershell
dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore
```

预期：AppHost 构建成功。

## 任务 3：添加 IAM、schema 和就绪状态文档

- [ ] **步骤 1：写入初始权限数据**

添加：

1. `business.erp.procurement.read`
2. `business.erp.procurement.manage`
3. `business.erp.sales.read`
4. `business.erp.sales.manage`
5. `business.erp.finance.read`
6. `business.erp.finance.manage`

- [ ] **步骤 2：更新授权矩阵**

记录每项权限及其所属区域。

- [ ] **步骤 3：更新 schema 目录**

添加采购、销售和财务中的 `erp` 表。确认注释以及 JSON/文本列指导与 schema 约定测试一致。

- [ ] **步骤 4：更新就绪状态文档和 README**

更新当前事实：

1. 只有三个切片的验证脚本全部通过，才能将 ERP 标记为已实现。
2. ERP 使用端口 5118。
3. 只有 ERP 最终验证通过后，才能将全链路验收标记为已解除阻塞。

## 任务 4：创建验证脚本

- [ ] **步骤 1：创建聚焦验证脚本**

每个脚本都必须点引用 `scripts/lib/ScriptAutomation.ps1`，并使用辅助函数，不得直接调用原生命令。

聚焦验证脚本：

1. `scripts/verify-business-erp-procurement-mvp.ps1`
2. `scripts/verify-business-erp-sales-mvp.ps1`
3. `scripts/verify-business-erp-finance-mvp.ps1`

每个脚本都应使用适合所属切片的筛选条件运行 ERP Domain/Web 测试；如果触及映射，还应运行 schema 约定测试。

- [ ] **步骤 2：创建 ERP 最终聚合脚本**

`scripts/verify-business-erp-procurement-sales-finance-mvp.ps1` 应运行：

1. ERP 采购验证。
2. ERP 销售验证。
3. ERP 财务验证。
4. `dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore`.

- [ ] **步骤 3：运行治理门禁**

运行：

```powershell
scripts/check-script-governance.ps1
```

预期：脚本治理门禁通过。

## 任务 5：运行 ERP 最终集成验证

- [ ] **步骤 1：运行聚焦检查和聚合检查**

运行：

```powershell
scripts/verify-business-erp-procurement-mvp.ps1
scripts/verify-business-erp-sales-mvp.ps1
scripts/verify-business-erp-finance-mvp.ps1
scripts/verify-business-erp-procurement-sales-finance-mvp.ps1
git diff --check
```

预期：所有检查均通过。

- [ ] **步骤 2：记录第 3 波集成状态**

在 PR/会话摘要中包含：

```markdown
## Wave 3 ERP Integration State

- ERP Procurement: registered | skipped | blocked - reason and verification command
- ERP Sales: registered | skipped | blocked - reason and verification command
- ERP Finance: registered | skipped | blocked - reason and verification command
- AppHost: registered | skipped | blocked - reason and verification command
- Full-chain acceptance: unblocked | blocked - reason
```

## 自审清单

1. ERP 是第 3 波唯一新增的服务。
2. 所有使用本地端口 5118 的位置都已记录该端口。
3. IAM 初始数据、授权矩阵、schema 目录和就绪状态相互一致。
4. ERP 最终验证通过前，不得将全链路验收标记为已解除阻塞。
