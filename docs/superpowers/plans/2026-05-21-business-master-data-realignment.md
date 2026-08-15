# 业务主数据重整实施计划

> **供代理执行者使用：**必须使用子技能 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐任务实施本计划。步骤使用复选框（`- [ ]`）语法跟踪。

**目标：**重整 BusinessMasterData，使其能够成为离散制造与流程制造的受治理基础，之后再完成公开 API 和下游服务的推出。

**架构：**BusinessMasterData 继续作为 Layer 0，负责通用业务身份、UOM、资源和静态参考事实。版本化工程事实由 ProductEngineering 负责，库存事实由 Inventory 负责，质量工作流事实由 Quality 负责，执行事实由 MES 负责，工业运行时事实由 IndustrialTelemetry 负责。新增下游解析契约和 MasterData 变更事件，使其他服务能够依赖 MasterData，而不直接耦合数据库。

**技术栈：**.NET 10、FastEndpoints、MediatR、EF Core、Npgsql、netcorepal 仓储/工作单元原语、xUnit、ADR 0011 IntegrationEvent 信封。

---

## 输入资料

1. `docs/adr/0012-business-platform-domain-layering.md`
2. `docs/adr/0013-business-master-data-governance.md`
3. `docs/architecture/business-platform-domain-architecture.md`
4. `docs/architecture/business-master-data-field-matrix.md`
5. `docs/architecture/business-master-data-process-manufacturing-supplement.md`
6. `docs/superpowers/specs/2026-05-20-business-platform-domain-design.md`
7. `docs/superpowers/plans/2026-05-20-business-master-data-foundation.md`

## 边界

1. 不得将 EBOM、MBOM、Recipe、Formula、Routing、ECO 或 ECN 移入 MasterData。
2. 不得将库存余额、库存移动、实际批次、序列号、炉批号、有效期或库存状态移入 MasterData。
3. 不得将检验标准、检验记录、COA、质量放行决策或不合格工作流移入 MasterData。
4. 不得将 MES 批记录、实际消耗、实际产出、偏差、清洁执行或谱系移入 MasterData。
5. 不得在 MasterData 中存储 PLC/DCS/SCADA 连接密钥、遥测样本、告警或设备状态快照。
6. 不得重复存储 IAM 用户、角色、权限或成员关系事实。

## 任务 1：扩展 MasterData 领域模型

**文件：**

- 修改：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/SkuAggregate/Sku.cs`
- 修改：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/BusinessPartnerAggregate/BusinessPartner.cs`
- 修改：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/WorkCenterAggregate/WorkCenter.cs`
- 修改：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/WorkCalendarAggregate/WorkCalendar.cs`
- 修改：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/DeviceAssetAggregate/DeviceAsset.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/UnitOfMeasureAggregate/UnitOfMeasure.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/UomConversionAggregate/UomConversion.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/SiteAggregate/Site.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/ProductionLineAggregate/ProductionLine.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/ShiftAggregate/Shift.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/ReferenceDataAggregate/ReferenceDataCode.cs`
- 修改：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/DomainEvents/MasterDataDomainEvents.cs`
- 修改：`backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Domain.Tests/MasterDataAggregateTests.cs`

- [ ] **步骤 1：编写会失败的聚合测试**

新增测试以覆盖：

1. UOM 必须包含编码、量纲类型、精度和舍入模式。
2. UOM 换算拒绝非正数系数和同单位换算。
3. SKU 必须包含基础 UOM 和可追溯策略。
4. 流程物料 SKU 可以保存存储条件、保质期策略、危险品/过敏原标签和质量必检标志。
5. WorkCenter 可以引用工厂、产线、资源类型、产能单位和默认日历。
6. DeviceAsset 可以保存资产类别、制造商、序列号、静态产能范围、产能 UOM、关键程度、可维护标志和外部引用，但不包含控制密钥。
7. Shift 支持跨午夜工作时段。
8. ReferenceDataCode 在代码集和编码组合上唯一。

运行：

```powershell
dotnet test backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Domain.Tests/Nerv.IIP.Business.MasterData.Domain.Tests.csproj --no-restore
```

预期：失败，因为新聚合和属性尚不存在。

- [ ] **步骤 2：实施最小领域变更**

只实施步骤 1 所列字段和不变量。跨聚合引用使用字符串编码，IAM 引用继续使用公开 ID。为已创建/已变更/已禁用的事实新增领域事件：

```csharp
SkuChangedDomainEvent
SkuDisabledDomainEvent
UnitOfMeasureChangedDomainEvent
BusinessPartnerChangedDomainEvent
ResourceChangedDomainEvent
WorkCalendarChangedDomainEvent
DeviceAssetChangedDomainEvent
ReferenceDataCodeChangedDomainEvent
```

- [ ] **步骤 3：运行领域测试**

运行：

```powershell
dotnet test backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Domain.Tests/Nerv.IIP.Business.MasterData.Domain.Tests.csproj --no-restore
```

预期：通过。

- [ ] **步骤 4：提交领域重整**

运行：

```powershell
git add backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Domain.Tests
git commit -m "feat: realign business master data domain"
```

## 任务 2：更新持久化、迁移和 schema 目录

**文件：**

- 修改：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/ApplicationDbContext.cs`
- 修改：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/EntityConfigurations/*.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/EntityConfigurations/UnitOfMeasureEntityTypeConfiguration.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/EntityConfigurations/UomConversionEntityTypeConfiguration.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/EntityConfigurations/SiteEntityTypeConfiguration.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/EntityConfigurations/ProductionLineEntityTypeConfiguration.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/EntityConfigurations/ShiftEntityTypeConfiguration.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/EntityConfigurations/ReferenceDataCodeEntityTypeConfiguration.cs`
- 修改：`backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/MasterDataSchemaConventionTests.cs`
- 修改：`backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/MasterDataPostgresProfileTests.cs`
- 修改：`docs/architecture/database-schema-catalog.md`

- [ ] **步骤 1：扩展 schema 测试**

扩展 schema 约定测试，断言新表使用 `business_masterdata` schema，并断言表注释、列注释、字符串 ID 约定和迁移历史 schema。

- [ ] **步骤 2：配置表和索引**

使用以下唯一键：

| 表 | 唯一键 |
| --- | --- |
| `units_of_measure` | organizationId + environmentId + code |
| `uom_conversions` | organizationId + environmentId + fromUomCode + toUomCode + effectiveFrom |
| `sites` | organizationId + environmentId + code |
| `production_lines` | organizationId + environmentId + code |
| `shifts` | organizationId + environmentId + code |
| `reference_data_codes` | organizationId + environmentId + codeSet + code |

为每个业务属性添加注释，并在相关位置注明单位含义。

- [ ] **步骤 3：生成迁移**

运行：

```powershell
dotnet tool restore
dotnet tool run dotnet-ef migrations add RealignBusinessMasterData --project backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/Nerv.IIP.Business.MasterData.Infrastructure.csproj --startup-project backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Nerv.IIP.Business.MasterData.Web.csproj --output-dir Migrations
```

预期：迁移只创建 `business_masterdata` schema 对象，不更改 IAM 或其他业务服务的 schema。

- [ ] **步骤 4：运行持久化测试**

运行：

```powershell
dotnet test backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/Nerv.IIP.Business.MasterData.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~MasterDataSchemaConventionTests|FullyQualifiedName~MasterDataPostgresProfileTests"
```

预期：schema 约定测试通过；配置 `NERV_IIP_TEST_POSTGRES` 时 PostgreSQL 配置档测试通过，未配置时明确跳过。

- [ ] **步骤 5：提交持久化重整**

运行：

```powershell
git add backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests docs/architecture/database-schema-catalog.md
git commit -m "feat: persist realigned business master data"
```

## 任务 3：新增解析契约和集成事件

**文件：**

- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/IntegrationEvents/MasterDataIntegrationEvents.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/IntegrationEventConverters/MasterDataIntegrationEventConverters.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Queries/ResolveMasterDataReferencesQuery.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Queries/ValidateMasterDataReferencesQuery.cs`
- 修改：`backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/MasterDataIntegrationEventTests.cs`
- 修改：`backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/MasterDataEndpointTests.cs`
- 修改：`docs/architecture/business-platform-domain-architecture.md`

- [ ] **步骤 1：定义事件载荷**

为以下内容新增稳定记录：

```csharp
SkuChangedIntegrationEvent
SkuDisabledIntegrationEvent
UnitOfMeasureChangedIntegrationEvent
BusinessPartnerChangedIntegrationEvent
ResourceChangedIntegrationEvent
WorkCalendarChangedIntegrationEvent
DeviceAssetChangedIntegrationEvent
ReferenceDataCodeChangedIntegrationEvent
```

事件必须包含 organizationId、environmentId、稳定编码、当前状态和业务发生时间戳。事件不得包含令牌、密钥、完整附件或 PLC 控制数据。

- [ ] **步骤 2：新增序列化测试**

断言事件 JSON 使用 camelCase 属性名，并继续与 ADR 0011 信封兼容。

- [ ] **步骤 3：新增解析查询契约**

新增解析和校验查询，接收 organizationId、environmentId 以及 `{ resourceType, code }` 引用集合。返回：

```text
resourceType, code, exists, active, displayName, snapshotVersion, disabledReason
```

- [ ] **步骤 4：更新架构事件基线**

在 `docs/architecture/business-platform-domain-architecture.md` 中新增 MasterData 变更事件。

- [ ] **步骤 5：运行事件和查询测试**

运行：

```powershell
dotnet test backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/Nerv.IIP.Business.MasterData.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~MasterDataIntegrationEventTests|FullyQualifiedName~MasterDataEndpointTests"
```

预期：通过。

- [ ] **步骤 6：提交契约**

运行：

```powershell
git add backend/services/Business/MasterData docs/architecture/business-platform-domain-architecture.md
git commit -m "feat: add master data resolve contracts"
```

## 任务 4：完善 API 接口面和 IAM 权限

**文件：**

- 修改：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Auth/BusinessPermissionCodes.cs`
- 修改：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Commands/*.cs`
- 修改：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Queries/*.cs`
- 修改：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Endpoints/MasterData/MasterDataEndpoints.cs`
- 修改：`backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/MasterDataEndpointTests.cs`
- 修改：`backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/MasterDataOpenApiTests.cs`
- 修改：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs`
- 修改：`docs/architecture/authorization-matrix.md`

- [ ] **步骤 1：更新端点测试**

覆盖匿名访问返回 `401`、缺少权限返回 `403`、成功创建、重复键，以及 SKU、UOM、合作方、部门、团队、人员技能、工作中心、日历、班次、站点、产线、设备资产和参考数据的解析/校验行为。

- [ ] **步骤 2：实施端点**

使用 FastEndpoints 暴露创建/列表/解析 API。保持操作 ID 稳定，并要求具备 `authorization-matrix.md` 记录的权限代码。

- [ ] **步骤 3：初始化权限**

将新的 MasterData 权限添加到 IAM 初始数据和初始管理员角色中。现有权限字符串的含义仍兼容时，保持其不变。

- [ ] **步骤 4：运行 API 和 IAM 测试**

运行：

```powershell
dotnet test backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/Nerv.IIP.Business.MasterData.Web.Tests.csproj --no-restore
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore --filter FullyQualifiedName~IamFoundationTests
```

预期：通过。

- [ ] **步骤 5：提交 API 重整**

运行：

```powershell
git add backend/services/Business/MasterData backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs docs/architecture/authorization-matrix.md
git commit -m "feat: expose realigned master data api"
```

## 任务 5：更新下游计划和就绪状态

**文件：**

- 修改：`docs/superpowers/plans/2026-05-20-business-product-engineering-mvp.md`
- 修改：`docs/superpowers/plans/2026-05-20-business-common-capability-foundation.md`
- 修改：`docs/superpowers/plans/2026-05-20-business-demand-planning-mvp.md`
- 修改：`docs/superpowers/plans/2026-05-20-business-mes-execution-mvp.md`
- 修改：`docs/superpowers/plans/2026-05-20-business-master-data-foundation.md`
- 修改：`docs/architecture/implementation-readiness.md`
- 修改：`README.md`

- [ ] **步骤 1：更新 ProductEngineering 计划**

将 Recipe/Formula 和 ProcessParameter 作为由 ProductEngineering 负责的流程制造版本化工程事实加入计划。

- [ ] **步骤 2：更新 Inventory/Quality/MES 计划说明**

记录这些服务会使用 MasterData 的 UOM、SKU 可追溯策略、资源层级和特性定义，但由服务自身负责实际事务事实。

- [ ] **步骤 3：更新就绪状态文档**

记录 MasterData 重整是下游服务能够将 BusinessMasterData 视为稳定依赖之前的门禁。

- [ ] **步骤 4：运行文档验证**

运行：

```powershell
rg -n "MasterData realignment|BusinessMasterData Process|Recipe|Formula|UnitOfMeasure|UomConversion" docs README.md
git diff --check
```

预期：命令以 `0` 退出。

- [ ] **步骤 5：提交就绪状态更新**

运行：

```powershell
git add docs README.md
git commit -m "docs: record master data realignment readiness"
```

## 自审清单

1. `business-master-data-field-matrix.md` 中的每个对象都有对应的 MasterData 实施任务，或者已记录非 MasterData 责任方。
2. 在不将 Recipe/Formula 版本移入 MasterData 的前提下表达流程制造需求。
3. API 契约包含创建/列表以及批量解析和校验操作。
4. IntegrationEvent 覆盖下游服务可能缓存的变更。
5. 下游计划不另行创建重复或竞争性的 SKU、UOM、合作方、资源或设备主数据事实。
