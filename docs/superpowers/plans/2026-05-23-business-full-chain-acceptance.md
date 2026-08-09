# 业务全链路验收实施计划

> **供代理执行者使用：**必须使用子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实施本计划。各步骤使用复选框（`- [ ]`）语法跟踪。

**目标：**在 ERP 完成后，为七条关键业务链增加端到端验收覆盖，以实施 #77。

**架构：**验收测试位于各个服务之外的 `backend/tests/Nerv.IIP.Business.Acceptance.Tests`。测试使用公开 HTTP API 和可通过集成事件观察的结果，不读取服务数据库作为主要断言依据。

**技术栈：**.NET 10、xUnit、ASP.NET Core 测试宿主、HttpClient、现有服务的 WebApplicationFactory 模式、受治理的 PowerShell 脚本。

---

## 规格

使用 `docs/superpowers/specs/2026-05-23-business-full-chain-acceptance-design.md`。

## 前置条件

在以下检查通过之前不得开始本计划：

1. `scripts/verify-business-wave1-foundation.ps1`
2. `scripts/verify-business-wave2-execution.ps1`
3. `scripts/verify-business-equipment-reliability.ps1`
4. `scripts/verify-business-erp-procurement-sales-finance-mvp.ps1`
5. `dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore`

## 文件

- 创建：`backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj`
- 创建：`backend/tests/Nerv.IIP.Business.Acceptance.Tests/BusinessAcceptanceFixture.cs`
- 创建：`backend/tests/Nerv.IIP.Business.Acceptance.Tests/BusinessApiClients.cs`
- 创建：`backend/tests/Nerv.IIP.Business.Acceptance.Tests/BusinessAcceptanceEventRecorder.cs`
- 创建：`backend/tests/Nerv.IIP.Business.Acceptance.Tests/EngineeringToManufacturingAcceptanceTests.cs`
- 创建：`backend/tests/Nerv.IIP.Business.Acceptance.Tests/PlanToProcureProduceAcceptanceTests.cs`
- 创建：`backend/tests/Nerv.IIP.Business.Acceptance.Tests/ProcureToPayAcceptanceTests.cs`
- 创建：`backend/tests/Nerv.IIP.Business.Acceptance.Tests/OrderToCashAcceptanceTests.cs`
- 创建：`backend/tests/Nerv.IIP.Business.Acceptance.Tests/ProductionToCostAcceptanceTests.cs`
- 创建：`backend/tests/Nerv.IIP.Business.Acceptance.Tests/EquipmentToMaintenanceAcceptanceTests.cs`
- 创建：`backend/tests/Nerv.IIP.Business.Acceptance.Tests/WcsAdapterAcceptanceTests.cs`
- 修改：`backend/Nerv.IIP.sln`
- 创建：`scripts/verify-business-full-chain-acceptance.ps1`
- 修改：`docs/architecture/implementation-readiness.md`
- 修改：`README.md`

## 任务 1：创建验收测试框架

- [ ] **步骤 1：创建测试项目**

运行：

```powershell
dotnet new xunit -n Nerv.IIP.Business.Acceptance.Tests -o backend/tests/Nerv.IIP.Business.Acceptance.Tests --framework net10.0
dotnet sln backend/Nerv.IIP.sln add backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj
```

- [ ] **步骤 2：添加引用**

按照现有后端测试模式引用各服务的 Web 项目和共享测试辅助工具。不得为了行为断言而从一个服务引用另一个服务的 Domain/Infrastructure 项目。

- [ ] **步骤 3：实现测试装置**

`BusinessAcceptanceFixture` 应当为 MasterData、ProductEngineering、Inventory、Quality、MES、DemandPlanning、WMS、IndustrialTelemetry、Maintenance 和 ERP 提供已授权的客户端。

- [ ] **步骤 4：实现事件记录器**

使用集成事件转换器的输出、测试消息总线钩子或服务的可观察结果。在可用时，记录器必须捕获事件类型、版本、来源服务、来源文档 ID 和关联 ID。

- [ ] **步骤 5：运行空测试框架**

运行：

```powershell
dotnet test backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj --no-restore
```

预期：空测试框架通过。

## 任务 2：添加工程与计划链路

- [ ] **步骤 1：工程到制造**

测试流程：

1. 创建 SKU、工作中心和资源引用。
2. 创建工程文档、EBOM、MBOM、Routing 和 ProductionVersion。
3. 针对成品需求运行 MRP。
4. 在 MES 中接受计划工单建议。
5. 通过公开 ID 断言 MES 工单引用已发布的 ProductionVersion、MBOM 和 Routing 事实。

- [ ] **步骤 2：计划到采购/生产**

测试流程：

1. 创建需求来源。
2. 初始化低于需求的可用量。
3. 运行 MRP。
4. 断言生成一条计划采购建议和一条计划工单建议。
5. 在 ERP 中接受采购建议，并在 MES 中接受工单建议。
6. 断言 DemandPlanning 将两条建议都标记为已接受，并记录下游文档引用。

- [ ] **步骤 3：运行聚焦测试**

运行：

```powershell
dotnet test backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj --no-restore --filter "FullyQualifiedName~EngineeringToManufacturingAcceptanceTests|FullyQualifiedName~PlanToProcureProduceAcceptanceTests"
```

预期：聚焦测试通过。

## 任务 3：添加采购、销售与生产链路

- [ ] **步骤 1：采购到付款**

流程：ERP 采购申请 -> RFQ -> 供应商报价 -> 采购订单 -> 采购收货 -> Quality 检验通过 -> WMS 入库完成 -> Inventory 库存移动 -> ERP 应付候选项。

断言库存增加，且应付候选项金额等于收货数量乘以单价。

- [ ] **步骤 2：订单到收款**

流程：ERP 商机 -> 报价 -> 销售订单 -> 发货单 -> WMS 出库完成 -> Inventory 库存移动 -> ERP 应收候选项。

断言库存减少，且应收候选项金额等于发货数量乘以销售价格。

- [ ] **步骤 3：生产到成本**

流程：MES 工单 -> 工序任务 -> 生产报工 -> Quality 工序检验 -> 成品收货请求 -> WMS 入库完成 -> Inventory 库存移动 -> ERP 成本候选项。

断言成本候选项引用报工 ID、工单 ID 和库存移动/完成来源 ID。

- [ ] **步骤 4：运行聚焦测试**

运行：

```powershell
dotnet test backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj --no-restore --filter "FullyQualifiedName~ProcureToPayAcceptanceTests|FullyQualifiedName~OrderToCashAcceptanceTests|FullyQualifiedName~ProductionToCostAcceptanceTests"
```

预期：聚焦测试通过。

## 任务 4：添加设备与 WCS 链路

- [ ] **步骤 1：设备到维护再到产能**

流程：MasterData 设备资产 -> IndustrialTelemetry 标签 -> 告警触发 -> Maintenance 工单创建 -> 资产不可用 -> 工单完成 -> 资产恢复 -> MES 排程约束更新。

断言可以通过公开契约或服务的可观察结果看到 `industrialTelemetry.AlarmRaised`、`maintenance.AssetUnavailable` 和 `maintenance.AssetRestored`。

- [ ] **步骤 2：WMS 到 WCS 适配器**

流程：WMS 仓库任务 -> WCS 调度 -> 失败回调 -> 诊断信息可见 -> 重试调度 -> 成功回调 -> 仓库任务完成。

断言 WMS 在仓库作业成功完成前不会创建 Inventory 库存移动请求。

- [ ] **步骤 3：运行聚焦测试**

运行：

```powershell
dotnet test backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj --no-restore --filter "FullyQualifiedName~EquipmentToMaintenanceAcceptanceTests|FullyQualifiedName~WcsAdapterAcceptanceTests"
```

预期：聚焦测试通过。

## 任务 5：添加完整验证脚本并更新就绪状态

- [ ] **步骤 1：创建脚本**

创建 `scripts/verify-business-full-chain-acceptance.ps1`。该脚本必须点源 `scripts/lib/ScriptAutomation.ps1`，并在运行验收测试项目之前运行全部前置脚本。

- [ ] **步骤 2：运行最终验证**

运行：

```powershell
scripts/verify-business-full-chain-acceptance.ps1
scripts/check-script-governance.ps1
git diff --check
```

预期：所有检查都通过。

- [ ] **步骤 3：更新就绪状态和 README**

记录全链路验收已通过、验证脚本已存在，以及只有目标配置的运行通过后才能关闭 #77。

## 自查清单

1. 七条链路中的每一条都至少有一个测试。
2. 主要断言使用公开 API 和可通过集成事件观察的事实。
3. 测试失败时输出链路名称、来源文档 ID、下游文档 ID 和事件类型。
4. 验证脚本遵守脚本治理辅助函数规则。
