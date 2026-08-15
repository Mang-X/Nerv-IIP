# BarcodeLabel 业务扫码、GS1 与可追溯性实施计划

> **供代理执行者使用：**必须使用子技能 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项实施本计划。各步骤使用复选框（`- [ ]`）语法跟踪。

**目标：**通过为 BarcodeLabel 增加 GS1 解析、基于序列号的 EPCIS 可追溯能力和真实库存扫码业务动作路由，关闭 #418。

**架构：**BarcodeLabel 继续拥有标签与扫码事实，发布共享的条码扫码事件封装，并将显式支持的库存工作流转换为 Inventory 既有的库存移动请求集成事件。Inventory 仍是库存事实的所有者；不引入仅存在于 UI 的流程或跨 schema 耦合。

**技术栈：**.NET 10、CleanDDD/NetCorePal、FastEndpoints、EF Core PostgreSQL、CAP 集成事件、xUnit、`Nerv.IIP.Contracts.Inventory`。

---

## 规格

使用 `docs/superpowers/specs/2026-06-15-barcode-label-business-scan-gs1-traceability-design.md`。

## 任务

### Task 1：共享条码契约

- [ ] 创建 `backend/common/Contracts/Nerv.IIP.Contracts.BarcodeLabel`，其中包含 `BarcodeScanAcceptedIntegrationEvent`、事件类型常量和载荷记录。
- [ ] 将该项目加入 `backend/Nerv.IIP.sln`。
- [ ] 在 BarcodeLabel Web 测试和 Web 项目中引用该契约。
- [ ] 增加测试，验证事件类型、版本和信封字段。

### Task 2：GS1 领域模型

- [ ] 为 GS1 mod-10、GS1 AI 解析和带序列号的标签生成增加预期失败的领域测试。
- [ ] 在 BarcodeLabel Domain 下实现 `Gs1BarcodeValue`、`Gs1ApplicationIdentifierParser` 和 GS1 辅助组件。
- [ ] 扩展 `BarcodeRule`，支持 `gs1-128`、`gs1-datamatrix` 和 `gs1-mod10`。
- [ ] 扩展 `LabelPrintItem` 和打印批次创建流程，持久化 `gtin`、`lotNo`、`serialNumber` 和 `epcUri`。
- [ ] 运行 BarcodeLabel Domain 测试，并确保既有的确定性自定义编码测试通过。

### Task 3：EPCIS 持久化

- [ ] 为 commissioning（启用）与 object-event（对象事件）EPCIS 事实增加预期失败的测试。
- [ ] 增加 `EpcisEvent` 聚合/实体和 `DbSet`。
- [ ] 配置 `epcis_events` 及新增的标签/扫码列，并添加注释和索引。
- [ ] 增加 EF migration，并更新 `docs/architecture/database-schema-catalog.md`。
- [ ] 运行 schema 约定测试。

### Task 4：扫码命令路由

- [ ] 增加预期失败的 Web 命令测试，验证已接受的 `inventory.receipt` GS1 扫码会发布 `InventoryMovementRequestedIntegrationEvent`。
- [ ] 扩展 `RecordScanCommand` 和 endpoint 请求，增加可选库存上下文：`SkuCode`、`UomCode`、`SiteCode`、`LocationCode`、`QualityStatus`、`OwnerType`、`OwnerId`、`Quantity`。
- [ ] 将 GS1 数据解析到扫码记录字段中。
- [ ] 针对已接受的扫码发布共享 `BarcodeScanAcceptedIntegrationEvent`。
- [ ] 仅针对受支持的库存工作流发布 `InventoryMovementRequestedIntegrationEvent`。
- [ ] 对已接受扫码中的不受支持工作流，以及缺少库存上下文的请求抛出 `KnownException`。
- [ ] 保留被拒绝扫码的日志，但不发布下游业务动作事件。

### Task 5：API、文档与验证

- [ ] 更新 BarcodeLabel endpoint 契约测试以覆盖新增请求字段，但不改变路由形状。
- [ ] 更新 `docs/architecture/business-platform-domain-architecture.md`、`docs/architecture/api-contract-and-codegen.md` 和 `docs/architecture/implementation-readiness.md`。
- [ ] 运行：
  - `dotnet test backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Domain.Tests/Nerv.IIP.Business.BarcodeLabel.Domain.Tests.csproj --no-restore`
  - `dotnet test backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests.csproj --no-restore`
  - `dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/Nerv.IIP.Business.Inventory.Web.Tests.csproj --no-restore --filter FullyQualifiedName~InventoryMovementRequestedConsumerTests`
  - `pwsh scripts/verify-business-barcode-label-mvp.ps1`

## 自审

规格覆盖情况：#418 的全部需求均映射到任务。占位符扫描：无。类型一致性：事件与命令名称符合既有服务约定及 Inventory 的库存移动请求契约。
