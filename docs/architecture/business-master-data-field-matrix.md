# 业务主数据字段矩阵

本矩阵冻结了 BusinessMasterData 的首轮治理结果。它不是 UI 表单规格，而是界定哪些事实由 MasterData 所有、哪些下游服务消费这些事实，以及哪些事实必须保留在其他领域。

## 分类规则

| 类别 | 定义 | 示例 | 所有权规则 |
| --- | --- | --- | --- |
| Master data | 可由多个领域复用的持久化业务身份或静态属性 | SKU、UOM、合作伙伴、工厂、工作中心、设备资产 | 除非列出更具体的来源，否则由 BusinessMasterData 所有 |
| Reference data | 受控代码表或可复用定义 | 物料类型、存储条件、资产类别、质量特性定义 | 跨领域时由 BusinessMasterData 所有 |
| Transactional data | 受时间约束的业务事件或流程状态 | 采购订单、库存移动、批次记录、报警、检验记录 | 由所属流程领域所有 |
| External reference | 由其他平台或外部系统所有的 ID | IAM userId、fileId、connectorHostId、外部 ERP 代码 | 由原始来源所有；MasterData 仅存储引用 |

## 核心对象

| 对象 | MasterData 所有的字段 | 必需的下游消费者 | 明确不由 MasterData 所有 |
| --- | --- | --- | --- |
| SKU / Material | organizationId, environmentId, code, name, materialType, category, baseUomCode, inventoryUomCode, purchaseUomCode, salesUomCode, manufacturingUomCode, batchTrackingPolicy, serialTrackingPolicy, shelfLifePolicyCode, storageConditionCode, defaultBarcodeRuleCode, procurementType, mrpType, lotSizingPolicy, minimumLotSize, maximumLotSize, lotSizeMultiple, safetyStockQuantity, reorderPointQuantity, plannedDeliveryTimeDays, inHouseProductionTimeDays, goodsReceiptProcessingTimeDays, abcClass, lifecycleStatus, manufacturingEnabled, purchasingEnabled, salesEnabled, qualityRequired, disabled, lifecycle timestamps | ProductEngineering、Planning、Inventory、Quality、ERP、WMS、MES、BarcodeLabel、Maintenance | EBOM/MBOM/配方版本、站点特定的计划覆盖项、库存余额、批次实例、序列号实例、订单价格、实际成本 |
| UnitOfMeasure | code, name, dimensionType, precision, roundingMode, disabled | SKU、ProductEngineering、Planning、Inventory、Quality、ERP、MES、Telemetry | 检验、遥测样本或报告中的实际测量值 |
| UomConversion | fromUomCode, toUomCode, factor, offset, precision, roundingMode, effectiveFrom, effectiveTo | Planning、Inventory、ERP、MES、Quality | 配方特定的收率或批次大小换算；这些属于 ProductEngineering |
| BusinessPartner | partnerCode, name, partnerRoles, status, taxId, taxRegionCode, defaultCurrencyCode, paymentTermsCode, primaryAddress, primaryContactName, primaryContactEmail, primaryContactPhone, creditLimit, creditCurrencyCode, complianceTags, disabled | ERP Procurement/Sales/Finance、WMS、Quality、Planning | RFQ、报价单、采购订单、销售订单、AR/AP 未结金额、供应商记分卡交易 |
| PartnerQualification | partnerCode, qualificationType, materialScope, certificateFileId, validFrom, validTo, status | ERP Procurement、Quality、Planning | 供应商审核工作流、质量放行决策、采购交易 |
| Site / Plant / Area / Line | code, name, hierarchyParentCode, type, timezone, addressRef, disabled | Planning、Inventory、WMS、MES、Maintenance、Telemetry | IAM organization/environment；这些仍为 IAM 事实 |
| WorkCenter | code, name, resourceType, plantCode, lineCode, defaultCalendarCode, capacityUnit, capacityPerDay, utilizationRate, efficiencyRate, numberOfCapacities, finiteCapacity, bottleneck, costCenterCode, disabled | ProductEngineering、Planning、MES、ERP Costing、Maintenance | 排程结果、实际停机、工序报告 |
| WorkCalendar | code, name, timezone, workingTimeRules, exceptionDates, holidayCalendarCode, effectiveFrom, effectiveTo, disabled | Planning、MES、Maintenance | 实际班次出勤、加班审批、生产报告 |
| Shift | code, name, startTime, endTime, crossesMidnight, paidMinutes, breakMinutes, disabled | MES、Planning、与 HR 相邻的业务排程 | IAM 成员关系、薪资计算 |
| Department | code, name, parentDepartmentCode, disabled | Approval、MES、Planning、reporting | IAM organization、IAM role 或 permission |
| Team | code, name, departmentCode, shiftCode, effectiveFrom, effectiveTo, disabled | MES、Planning、Approval | IAM 成员关系 |
| PersonnelSkill | userId, skillCode, level, qualificationRef, effectiveFrom, effectiveTo, disabled | MES 派工、Maintenance、Approval、Quality | 登录名、电子邮件、角色、权限、HR 薪资 |
| DeviceAsset | code, name, assetClassCode, model, manufacturer, serialNo, siteCode, workshopCode, lineCode, workCenterCode, stationCode, parentDeviceId, component list, purchaseDate, purchaseCost, purchaseCurrencyCode, warrantyExpiresOn, supplierPartnerCode, retiredOn, criticality, maintainable, telemetryEnabled, externalRefs, disabled | Telemetry、Maintenance、MES、Planning、ERP Costing | PLC/DCS/SCADA 密钥、测点样本、报警、维修工单、备件消耗、保修索赔工作流、供应商记分卡交易 |
| ResourceCapability | resourceCode, resourceType, capabilityCode, validMaterialTypes, capacityMin, capacityMax, capacityUomCode, compatibleStorageConditions, effectiveFrom, effectiveTo | ProductEngineering、Planning、MES、Maintenance | 产品特定的工艺路线参数；属于 ProductEngineering |
| ReferenceData | codeSet, code, name, description, status, effectiveFrom, effectiveTo | 所有业务领域 | 仅由单一领域所有的交易特定状态 |

为兼容入驻和导入，BusinessPartner 的 `creditLimit` 与 `creditCurrencyCode` 可为 null；但通用更新 API 将省略值或 null 解释为“保留现有值”。当前该通用更新接口不支持清除已有信用额度；若该业务操作成为必需，应新增专用的信用额度命令。

## 流程制造敏感字段

| 事实 | MasterData 角色 | ProductEngineering 角色 | Quality / Inventory / MES 角色 |
| --- | --- | --- | --- |
| Concentration, potency, density, purity, moisture | 作为稳定主数据属性时，定义可复用的物料属性和允许单位 | 在配方版本、收率和工艺参数计算中使用这些值 | 在检验、批记录和遥测中记录实际测量值 |
| Shelf life and expiry rule | 存储默认保质期策略代码及其对存储条件的依赖 | 当配方/公式版本需要时引用该策略 | Inventory 按批次计算实际到期日；Quality 控制放行 |
| Hazard, allergen, regulatory tag | 存储稳定的物料和合作伙伴合规标签 | 使用标签校验配方兼容性 | WMS 执行存储隔离；Quality 控制放行 |
| Batch/serial tracking policy | 存储 SKU 是否要求跟踪批次、序列号、炉次、日期代码或到期日 | 在配方和工艺路线中引用该策略 | Inventory 所有实际批次/序列号/炉次/日期代码实例 |
| Equipment capacity and compatibility | 存储静态容量范围、UOM、物料兼容性和清洁类别 | 在工艺路线/配方中使用兼容的资源类别 | MES 记录实际设备使用和清洁执行 |
| Quality characteristic definition | 存储可复用的特性代码、名称、量纲和 UOM | 在产品/配方版本中引用必需特性 | Quality 所有检验标准、抽样规则、结果和放行 |

## 下游引用契约

下游服务必须采用以下模式之一：

1. 创建新的业务单据前，通过 MasterData 公共 API 按代码或 ID 解析。
2. 需要历史可读性时，在下游单据上存储轻量、不可变的引用快照。
3. 缓存有效主数据时，订阅 MasterData IntegrationEvents。
4. 主记录被禁用、归档、替换或合并后，保持现有单据历史有效。

下游服务在大规模依赖 MasterData 前，其公共 API 必须包含批量解析和有效性检查 endpoint。

## 治理矩阵

| 范围 | 管理责任角色 | 变更前所需审批 | 最低审计要求 |
| --- | --- | --- | --- |
| SKU and UOM | 业务管理员 + 计划/物料所有者 | UOM、可追溯性、保质期或启用角色变更必须审批 | 变更前/后值、原因、生效日期、操作者 |
| Partner identity | 销售/采购所有者 | 角色、税务/合规或资质变更必须审批 | 变更前/后值、原因、操作者 |
| Resource hierarchy | 生产/维护所有者 | 工厂/产线/工作中心/设备重新归属必须审批 | 原/新父级、生效日期、操作者 |
| Device asset | 维护所有者 | 资产类别、关键性、可维护性或启用遥测变更必须审批 | 变更前/后值、操作者 |
| Reference data | 代码集的领域管理责任人 | 删除代码或改变语义必须审批 | 代码集、代码、原/新含义、操作者 |

## 开放问题

| 问题 | 重要性 | 所有者 |
| --- | --- | --- |
| Should warehouse and storage area identity move from Inventory to MasterData? | 仓库可以是静态设施，但库存库位和余额属于 Inventory 事实。 | Inventory 所有者 + 架构所有者 |
| Which planning attributes belong on SKU versus DemandPlanning? | 已针对共享默认值解决：SKU 所有默认提前期、批量、MRP 策略、安全库存和再订货点值；DemandPlanning 可所有站点特定或场景特定的覆盖项和快照。 | 计划所有者 + 物料所有者 |
| Should quality characteristic definitions live in MasterData or Quality? | 可复用代码定义跨领域，但检验标准属于 Quality 事实。 | Quality 所有者 + 架构所有者 |
| How much additional partner commercial data is allowed in MasterData? | 税务、银行和结算字段涉及隐私及授权；客户信用额度现由 MasterData 所有，用于 ERP 销售信用检查。 | ERP 所有者 + 安全所有者 |
