# Issue 407 BusinessMasterData 缺口实施计划

> **供代理执行者使用：**必须使用子技能 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项实施本计划。步骤使用复选框（`- [ ]`）语法跟踪。

**目标：**补齐 GitHub issue #407 中 BusinessMasterData 的 P0/P1 缺口，覆盖 SKU 计划属性、渠道 UOM、产能属性、生命周期标志、合作伙伴商务字段、UOM 生效截止日期，以及日历/班次排程事实。

**架构：**遵循 ADR 0013 和 `business-master-data-field-matrix.md`，继续由 BusinessMasterData 作为持久 SKU/UOM/合作伙伴/资源/日历静态事实的第 0 层所有者。扩展现有聚合与通用 MasterData 资源 API 界面，不引入跨服务耦合，也不把计划逻辑迁入平台服务。DemandPlanning、MES、ERP、WMS、Scheduling 和 Quality 可通过现有内部 HTTP/OpenAPI 契约获取这些静态字段的快照。

**技术栈：**.NET 10、CleanDDD/netcorepal、FastEndpoints、EF Core migration、PostgreSQL schema `business_masterdata`、xUnit。

---

### Task 1：回归测试

**文件：**
- 修改：`backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Domain.Tests/MasterDataAggregateTests.cs`
- 修改：`backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/MasterDataApiContractTests.cs`
- 修改：`backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/MasterDataSchemaConventionTests.cs`

- [ ] 添加领域测试，证明 SKU 可分别保留库存/采购/销售/制造 UOM、计划默认值、生命周期状态和用途门禁。
- [ ] 添加领域测试，证明 BusinessPartner.Update 可变更主要角色并保留商务/税务/联系默认值。
- [ ] 添加领域测试，证明 WorkCenter 会存储利用率、效率、产能数量、成本中心和瓶颈标志。
- [ ] 添加领域测试，证明 UomConversion 支持 `EffectiveTo`、WorkCalendar 支持时区/生效范围/节假日日历，Shift 支持休息分钟数。
- [ ] 运行 `dotnet test backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Domain.Tests/Nerv.IIP.Business.MasterData.Domain.Tests.csproj --filter MasterDataAggregateTests --no-restore`；预期首次运行因生产字段缺失而失败。
- [ ] 为新字段的创建/更新/详情投影添加 Web/API 测试。
- [ ] 运行 `dotnet test backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/Nerv.IIP.Business.MasterData.Web.Tests.csproj --filter MasterDataApiContractTests --no-restore`；预期首次运行因命令/DTO 缺失而失败。

### Task 2：领域与命令实现

**文件：**
- 修改：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/SkuAggregate/Sku.cs`
- 修改：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/BusinessPartnerAggregate/BusinessPartner.cs`
- 修改：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/WorkCenterAggregate/WorkCenter.cs`
- 修改：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/UomConversionAggregate/UomConversion.cs`
- 修改：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/WorkCalendarAggregate/WorkCalendar.cs`
- 修改：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/ShiftAggregate/Shift.cs`
- 修改：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Commands/MasterData/CreateMasterDataCommands.cs`
- 修改：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Commands/MasterData/MasterDataLifecycleCommands.cs`
- 修改：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Queries/GetMasterDataResourceDetailQuery.cs`
- 修改：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Endpoints/MasterData/MasterDataEndpoints.cs`

- [ ] 添加可空/带默认值的 SKU 计划字段：`ProcurementType`、`MrpType`、`LotSizingPolicy`、`MinimumLotSize`、`MaximumLotSize`、`LotSizeMultiple`、`SafetyStockQuantity`、`ReorderPointQuantity`、`PlannedDeliveryTimeDays`、`InHouseProductionTimeDays`、`GoodsReceiptProcessingTimeDays`、`AbcClass`。
- [ ] 添加 SKU 生命周期和用途门禁：`LifecycleStatus`、`PurchasingEnabled`、`ManufacturingEnabled`、`SalesEnabled`。
- [ ] 添加 SKU 渠道 UOM 输入/更新参数，不再把所有渠道 UOM 折叠成基础 UOM。
- [ ] 当命令处理器具有 `ApplicationDbContext` 时，依据生效中的 UOM 换算校验非基础 SKU 渠道 UOM。
- [ ] 为 UOM 换算添加 `EffectiveTo`，并拒绝早于开始日期的结束日期。
- [ ] 添加 WorkCenter 产能字段：`UtilizationRate`、`EfficiencyRate`、`NumberOfCapacities`、`CostCenterCode`、`Bottleneck`；若对测试有用，再添加计算所得的有效日产能。
- [ ] 添加合作伙伴商务/联系字段：`TaxRegionCode`、`DefaultCurrencyCode`、`PaymentTermsCode`、`PrimaryAddress`、`PrimaryContactName`、`PrimaryContactEmail`、`PrimaryContactPhone`。
- [ ] 修复 `BusinessPartner.Update`，使角色规范化使用新的角色输入，而非旧的 `PartnerType`。
- [ ] 为 WorkCalendar 添加 `Timezone`、`HolidayCalendarCode`、`EffectiveFrom`、`EffectiveTo`。
- [ ] 为 Shift 添加 `BreakMinutes`。
- [ ] 重新运行失败测试并使其通过。

### Task 3：EF 配置与 migration

**文件：**
- 修改：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/EntityConfigurations/*.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/Migrations/<timestamp>_CloseBusinessMasterData407Gaps.cs`
- 修改：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`

- [ ] 为每个新增持久化字段添加列映射，包括最大长度、精度、必填值/默认值和注释。
- [ ] 使用 `Persistence__Provider=PostgreSQL` 生成 EF migration。
- [ ] 运行 schema 约定测试，并补齐所有缺失的注释/默认值。

### Task 4：文档

**文件：**
- 修改：`docs/architecture/business-master-data-field-matrix.md`
- 修改：`docs/architecture/database-schema-catalog.md`
- 修改：`docs/architecture/implementation-readiness.md`

- [ ] 更新字段矩阵中的开放问题，说明 SKU 默认计划属性作为共享默认值位于 BusinessMasterData；计划服务仍可拥有站点级覆盖值。
- [ ] 更新 BusinessMasterData schema 目录中 `skus`、`uom_conversions`、`business_partners`、`work_centers`、`work_calendars` 和 `shifts` 的行备注。
- [ ] 更新就绪状态，注明 issue #407 对 MasterData 静态计划/资源/合作伙伴字段的收口。

### Task 5：验证与 PR

**文件：**
- 除非验证发现失败，否则不修改代码。

- [ ] 运行聚焦的 Domain 测试。
- [ ] 运行聚焦的 Web/API/schema 测试。
- [ ] 运行 `dotnet test backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/Nerv.IIP.Business.MasterData.Web.Tests.csproj`。
- [ ] 运行 `dotnet test backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Domain.Tests/Nerv.IIP.Business.MasterData.Domain.Tests.csproj`。
- [ ] 运行 `dotnet test backend/Nerv.IIP.sln --filter "FullyQualifiedName~Business.MasterData"`。
- [ ] 如果变更后的公共契约工作流要求更新 OpenAPI snapshot 或生成客户端，应运行受治理的 OpenAPI/codegen 脚本，不得手工编辑生成文件。
- [ ] 提交所有范围内的变更。
- [ ] 推送 `codex/issue-407-business-master-data-gap`。
- [ ] 创建 PR，并在标题中包含 `Fix #407`，或在正文中包含 `Closes #407`。
