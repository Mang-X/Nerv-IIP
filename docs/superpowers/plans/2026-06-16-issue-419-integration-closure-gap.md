# Issue 419 集成闭环缺口实施计划

> **供代理执行者使用：**必须使用子技能 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项实施本计划。步骤使用复选框（`- [ ]`）语法跟踪。

**目标：**将 #419 rebase 到最新 `origin/main`，更新跨服务事件接线全景以反映已合并的相邻 issue，并使可复用的 ADR 0011 公共事件 envelope 门禁保持最新。

**架构：**元 issue 继续以文档优先、轻量代码为原则。公共事件契约治理保留在 `backend/common/Contracts` 和 `backend/tests/Nerv.IIP.Contracts.IntegrationEvents.Tests`；具体业务链修复仍归所属服务 issue 负责，不得集中到 Gateway 或新的共享服务中。

**技术栈：**.NET 10、xUnit、公共 Contracts 项目、受治理的 PowerShell 验证命令。

---

## 文件

- 修改：`docs/superpowers/specs/2026-06-16-issue-419-integration-closure-gap.md`
- 修改：`backend/tests/Nerv.IIP.Contracts.IntegrationEvents.Tests/IntegrationEventEnvelopeContractTests.cs`
- 修改：`backend/tests/Nerv.IIP.Contracts.IntegrationEvents.Tests/Nerv.IIP.Contracts.IntegrationEvents.Tests.csproj`
- 修改：`backend/common/Contracts/Nerv.IIP.Contracts.MasterData/MasterDataIntegrationEvents.cs`
- 修改：`backend/common/Contracts/Nerv.IIP.Contracts.MasterData/Nerv.IIP.Contracts.MasterData.csproj`
- 修改：`backend/common/Contracts/Nerv.IIP.Contracts.ProductEngineering/ProductEngineeringContracts.cs`
- 修改：`backend/common/Contracts/Nerv.IIP.Contracts.ProductEngineering/Nerv.IIP.Contracts.ProductEngineering.csproj`
- 修改：`backend/common/Contracts/Nerv.IIP.Contracts.Quality/QualityIntegrationEvents.cs`
- 修改：`backend/tests/Nerv.IIP.Messaging.CAP.Tests/Nerv.IIP.Messaging.CAP.Tests.csproj`
- 修改：`docs/architecture/implementation-readiness.md`

### Task 1：Rebase 并重新核实事实

- [x] **步骤 1：获取并 rebase**

运行：

```powershell
git fetch origin --prune
git rebase origin/main
```

预期：分支基于最新 main。以当前 main 的事实解决冲突。

- [x] **步骤 2：读取 issue 和就绪文档**

运行：

```powershell
gh issue view 419 --json number,title,body,labels,state,url,updatedAt
Get-Content docs/architecture/implementation-readiness.md -Raw
```

预期：#419 仍是元 issue；就绪文档反映最近合并的业务缺口收口情况。

- [x] **步骤 3：捕获当前事件接线事实**

对公共契约、转换器和消费者运行定向搜索：

```powershell
rg -n "IIntegrationEventConverter|ICapSubscribe|IIntegrationEventHandler|IntegrationEventConsumerGuard" backend -g "*.cs"
rg -n "InventoryMovementRequestedIntegrationEvent|SchedulePlanReleasedIntegrationEvent|WmsOutboundOrderRequestedIntegrationEvent|InspectionResultIntegrationEvent|NcrDispositionDecidedIntegrationEvent" backend -g "*.cs"
```

预期：在更新 #419 规格前，识别来自 MES、Scheduling、ERP、WMS、Quality 和 Approval 的新接通路径。

### Task 2：保持公共 envelope 门禁最新

- [x] **步骤 1：保留基于发现机制的测试**

保留 `backend/tests/Nerv.IIP.Contracts.IntegrationEvents.Tests/IntegrationEventEnvelopeContractTests.cs`：它扫描引用的公共契约程序集，查找名称以 `IntegrationEvent` 结尾且已导出的非泛型类。

- [x] **步骤 2：添加新公开的事件程序集**

添加 BarcodeLabel 和 Scheduling 引用及 using，使相邻已合并 issue 引入的当前公共契约纳入同一 ADR 0011 门禁。

- [x] **步骤 3：验证通过**

运行：

```powershell
dotnet test backend/tests/Nerv.IIP.Contracts.IntegrationEvents.Tests/Nerv.IIP.Contracts.IntegrationEvents.Tests.csproj --no-restore --verbosity minimal
```

实施后预期：通过。

### Task 3：更新文档

- [x] **步骤 1：根据最新 main 重写规格**

更新 `docs/superpowers/specs/2026-06-16-issue-419-integration-closure-gap.md`，使其区分：

1. 最新 main 中新接通的路径。
2. 仍已发布但未被消费的事件，或仍为服务本地的事件。
3. 五条链路的状态：其中部分环节现已闭环，但仍有残留缺口。
4. Saga/process-manager 缺失情况。
5. Envelope/DLQ 治理状态。

- [x] **步骤 2：更新就绪文档**

更新 `docs/architecture/implementation-readiness.md`，说明 `Nerv.IIP.Contracts.IntegrationEvents.Tests` 门禁会发现公共集成事件契约，而不只是检查固定子集；同时使 #419 反映当前公共程序集覆盖范围。

- [x] **步骤 3：最终验证**

运行：

```powershell
dotnet test backend/tests/Nerv.IIP.Contracts.IntegrationEvents.Tests/Nerv.IIP.Contracts.IntegrationEvents.Tests.csproj --no-restore
dotnet test backend/tests/Nerv.IIP.Messaging.CAP.Tests/Nerv.IIP.Messaging.CAP.Tests.csproj --no-restore
git diff --check
```

预期：所有命令均通过。除非本计划新增或修改 PowerShell 脚本，否则无需运行 `scripts/check-script-governance.ps1`。
