# 业务全链路验收实施计划

> 仅作为历史输入。截至 2026-05-23，请使用 `docs/superpowers/specs/2026-05-23-business-full-chain-acceptance-design.md` 和 `docs/superpowers/plans/2026-05-23-business-full-chain-acceptance.md`。这份较早的计划形成于第 1/2/2.5 波次完成之前，其中引用的前置脚本名称后来已经变更。

> **供代理执行者使用：**必须使用子技能 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项实施本计划。各步骤使用复选框（`- [ ]`）语法跟踪。

**目标：**在纵切 1 至纵切 9 实施完成后，为业务平台的七条关键链路添加端到端验收覆盖。

**架构：**验收测试位于各服务之外的 `backend/tests/Nerv.IIP.Business.Acceptance.Tests`。测试只验证公开 HTTP API 和可通过集成事件观察到的结果。除非通过显式辅助方法执行可选诊断断言，否则测试不得直接读取服务数据库。

**技术栈：**.NET 10、xUnit、ASP.NET Core 测试宿主、HttpClient、PostgreSQL 配置档案测试、PowerShell 验证脚本。

---

## 前置条件

0. `scripts/verify-business-main-platform-integration-readiness.ps1`
1. `scripts/verify-business-master-data-foundation.ps1`
2. `scripts/verify-business-product-engineering-mvp.ps1`
3. `scripts/verify-business-common-capability-foundation.ps1`
4. `scripts/verify-business-demand-planning-mvp.ps1`
5. `scripts/verify-business-erp-procurement-sales-finance-mvp.ps1`
6. `scripts/verify-business-wms-execution-mvp.ps1`
7. `scripts/verify-business-mes-execution-mvp.ps1`
8. `scripts/verify-business-industrial-telemetry-mvp.ps1`
9. `scripts/verify-business-maintenance-mvp.ps1`

开始本计划之前，每个前置脚本都必须通过。

## 验收链路

| 链路 | 必须达到的结果 |
| --- | --- |
| 从工程到制造 | 工单引用已发布的 MBOM 和路线。 |
| 从计划到采购/生产 | ERP 和 MES 可接受 MRP 建议。 |
| 从采购到库存再到应付 | 采购收货触发检验、入库、库存移动和 AP 候选。 |
| 从订单到发货再到应收 | 销售订单下达出库，触发库存移动和 AR 候选。 |
| 从生产执行到成本 | 工序报工和成品收货产生成本候选。 |
| 从设备到维修再到产能 | 报警创建维修工单，并发出资产不可用/恢复事实。 |
| 从 WMS 到 WCS 适配器 | WCS 下发、回调失败和重试诊断均可见。 |

## 文件结构图

```text
backend/tests/Nerv.IIP.Business.Acceptance.Tests/
  Nerv.IIP.Business.Acceptance.Tests.csproj
  BusinessAcceptanceFixture.cs
  BusinessApiClients.cs
  EngineeringToManufacturingAcceptanceTests.cs
  PlanToProcureProduceAcceptanceTests.cs
  ProcureToPayAcceptanceTests.cs
  OrderToCashAcceptanceTests.cs
  ProductionToCostAcceptanceTests.cs
  EquipmentToMaintenanceAcceptanceTests.cs
  WcsAdapterAcceptanceTests.cs

scripts/verify-business-full-chain-acceptance.ps1
docs/architecture/implementation-readiness.md
README.md
```

## 任务 1：创建验收测试项目

**文件：**

- 创建：`backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj`
- 创建：`backend/tests/Nerv.IIP.Business.Acceptance.Tests/BusinessAcceptanceFixture.cs`
- 创建：`backend/tests/Nerv.IIP.Business.Acceptance.Tests/BusinessApiClients.cs`
- 修改：`backend/Nerv.IIP.sln`

- [ ] **步骤 1：创建项目**

运行：

```powershell
dotnet new xunit -n Nerv.IIP.Business.Acceptance.Tests -o backend/tests/Nerv.IIP.Business.Acceptance.Tests --framework net10.0
dotnet sln backend/Nerv.IIP.sln add backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj
```

- [ ] **步骤 2：添加服务引用**

添加对每个业务 `.Web` 项目以及 `backend/common/Testing/Nerv.IIP.Testing/Nerv.IIP.Testing.csproj` 的引用。

- [ ] **步骤 3：实现测试夹具**

`BusinessAcceptanceFixture` 为 IAM 和每个已实施的业务服务启动测试宿主，写入初始管理员权限数据，公开已授权的 `HttpClient` 实例，并在测试之间使用服务公开的清理辅助方法或隔离的测试数据库名称重置数据。

- [ ] **步骤 4：运行空项目**

运行：

```powershell
dotnet test backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj --no-restore
```

预期：默认模板测试通过。

- [ ] **步骤 5：提交测试支架**

运行：

```powershell
git add backend/Nerv.IIP.sln backend/tests/Nerv.IIP.Business.Acceptance.Tests
git commit -m "test: add business acceptance harness"
```

## 任务 2：添加工程与计划验收

**文件：**

- 创建：`backend/tests/Nerv.IIP.Business.Acceptance.Tests/EngineeringToManufacturingAcceptanceTests.cs`
- 创建：`backend/tests/Nerv.IIP.Business.Acceptance.Tests/PlanToProcureProduceAcceptanceTests.cs`

- [ ] **步骤 1：编写工程到制造测试**

测试必须：

1. 创建 SKU、工作中心和设备资产。
2. 登记工程文档。
3. 发布 MBOM 和路线。
4. 创建 MRP 需求并运行 MRP。
5. 在 MES 中接受计划工单建议。
6. 断言 MES 工单包含已发布的 MBOM 和路线引用。

- [ ] **步骤 2：编写计划到采购/生产测试**

测试必须：

1. 创建成品销售需求。
2. 写入低于需求量的初始可用库存数据。
3. 运行 MRP。
4. 断言存在一项计划采购建议和一项计划工单建议。
5. 在 ERP 中接受采购建议，并在 MES 中接受工单建议。
6. 断言两项已接受建议在 DemandPlanning 中均已关闭，并返回正式单据 ID。

- [ ] **步骤 3：运行并提交**

运行：

```powershell
dotnet test backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj --no-restore --filter "FullyQualifiedName~EngineeringToManufacturingAcceptanceTests|FullyQualifiedName~PlanToProcureProduceAcceptanceTests"
git add backend/tests/Nerv.IIP.Business.Acceptance.Tests
git commit -m "test: cover engineering and planning business chains"
```

预期：测试通过。

## 任务 3：添加采购、销售与生产验收

**文件：**

- 创建：`backend/tests/Nerv.IIP.Business.Acceptance.Tests/ProcureToPayAcceptanceTests.cs`
- 创建：`backend/tests/Nerv.IIP.Business.Acceptance.Tests/OrderToCashAcceptanceTests.cs`
- 创建：`backend/tests/Nerv.IIP.Business.Acceptance.Tests/ProductionToCostAcceptanceTests.cs`

- [ ] **步骤 1：编写采购到付款测试**

流程：采购申请 -> RFQ -> 供应商报价 -> 采购订单 -> 采购收货 -> 质量检验通过 -> WMS 入库完成 -> Inventory 库存移动 -> AP 候选。断言库存增加，并且 AP 候选金额等于收货数量乘以单价。

- [ ] **步骤 2：编写订单到收款测试**

流程：商机 -> 报价 -> 销售订单 -> 发货单 -> WMS 出库完成 -> Inventory 库存移动 -> AR 候选。断言库存减少，并且 AR 候选金额等于发货数量乘以销售价格。

- [ ] **步骤 3：编写生产到成本测试**

流程：MES 工单 -> 工序任务 -> 工序报工 -> 工序质量检验 -> 成品收货请求 -> WMS 入库完成 -> Inventory 移动 -> ERP 成本候选。断言成本候选引用报工 ID、工单 ID 和库存移动 ID。

- [ ] **步骤 4：运行并提交**

运行：

```powershell
dotnet test backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj --no-restore --filter "FullyQualifiedName~ProcureToPayAcceptanceTests|FullyQualifiedName~OrderToCashAcceptanceTests|FullyQualifiedName~ProductionToCostAcceptanceTests"
git add backend/tests/Nerv.IIP.Business.Acceptance.Tests
git commit -m "test: cover procure sales and production business chains"
```

预期：测试通过。

## 任务 4：添加设备与 WCS 验收

**文件：**

- 创建：`backend/tests/Nerv.IIP.Business.Acceptance.Tests/EquipmentToMaintenanceAcceptanceTests.cs`
- 创建：`backend/tests/Nerv.IIP.Business.Acceptance.Tests/WcsAdapterAcceptanceTests.cs`

- [ ] **步骤 1：编写设备到维修测试**

流程：创建设备资产 -> 创建遥测标签 -> 触发报警 -> Maintenance 创建维修工单 -> 将资产标记为不可用 -> 完成工单 -> 可观察到资产恢复事件。断言发出的 `maintenance.AssetUnavailable` 和 `maintenance.AssetRestored` 事件包含设备资产 ID、原因、开始时间和恢复时间，使 MES 与计划域能够消费这些事件。

- [ ] **步骤 2：编写 WCS 适配器测试**

流程：WMS 创建仓储任务 -> 下发 WCS 任务 -> 外部失败回调 -> 诊断可见 -> 重试下发 -> 成功回调 -> 仓储任务完成。断言在仓储作业成功完成之前，WMS 绝不会过账库存移动。

- [ ] **步骤 3：运行并提交**

运行：

```powershell
dotnet test backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj --no-restore --filter "FullyQualifiedName~EquipmentToMaintenanceAcceptanceTests|FullyQualifiedName~WcsAdapterAcceptanceTests"
git add backend/tests/Nerv.IIP.Business.Acceptance.Tests
git commit -m "test: cover equipment maintenance and wcs chains"
```

预期：测试通过。

## 任务 5：添加完整验证脚本并更新就绪状态说明

**文件：**

- 创建：`scripts/verify-business-full-chain-acceptance.ps1`
- 修改：`docs/architecture/implementation-readiness.md`
- 修改：`README.md`

- [ ] **步骤 1：创建验证脚本**

该脚本按顺序运行所有业务纵切前置验证脚本，然后运行：

```powershell
dotnet test backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj --no-restore
```

- [ ] **步骤 2：运行最终验证**

运行：

```powershell
scripts/verify-business-full-chain-acceptance.ps1
git diff --check
```

预期：两条命令的退出码均为 `0`。

- [ ] **步骤 3：提交验收就绪状态说明**

运行：

```powershell
git add backend/tests/Nerv.IIP.Business.Acceptance.Tests scripts/verify-business-full-chain-acceptance.ps1 docs/architecture/implementation-readiness.md README.md
git commit -m "test: add business full chain acceptance"
```

## 自审清单

1. 测试使用公开 API 和已授权客户端。
2. 任何验收测试都不得通过访问服务数据库来完成主要断言。
3. 规格中的每条关键链路都至少有一个测试。
4. 失败时输出单据 ID、建议 ID、移动 ID 和事件名称，以便诊断。
