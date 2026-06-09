# 数据库 Schema Catalog

本文档记录当前 Nerv-IIP 已落地和计划落地的数据库 schema。物理结构仍以 EF Core migrations 和 EntityConfigurations 为准；本文档负责解释业务语义、边界、索引意图和可视化上下文。

当前 catalog 覆盖第五阶段已经迁移验证通过、并在第六阶段完成 schema governance hardening 的 AppHub 与 Ops，第七阶段已经落地 IAM Persistent Auth Foundation 的 IAM，以及 BusinessMasterData、BusinessProductEngineering、BusinessInventory、BusinessQuality、BusinessMES、BusinessDemandPlanning、BarcodeLabel、BusinessApproval、WMS、BusinessIndustrialTelemetry、BusinessMaintenance、BusinessScheduling、Notification 和 FileStorage 第一阶段 MVP 的 schema 基线。Knowledge、AI Integration 和 Observability 索引在真正建表前必须补充相同粒度的条目和 convention tests。

## 读法

1. `Owner` 表示维护该表 schema 和迁移的服务。
2. `Kind` 为 `business` 的表属于领域模型；`system` 表由框架或基础设施维护。
3. `Source` 指向 schema 权威文件。迁移和配置冲突时，以最新迁移和实体配置为准，并修正文档。
4. `Known gaps` 不是待办占位，而是当前已知的规范差距，进入下一轮 hardening 时应优先消除。

## BusinessMasterData Schema

Schema: `business_masterdata`

Owner: `backend/services/Business/MasterData`

Source:

1. `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/ApplicationDbContext.cs`
2. `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/DesignTimeApplicationDbContextFactory.cs`
3. `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/EntityConfigurations/*.cs`
4. `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/Migrations/20260521074323_InitialBusinessMasterData.cs`
5. `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/Migrations/20260521085711_RealignBusinessMasterData.cs`
6. `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/Migrations/20260527073140_AddNumberingCounters.cs`
7. `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/Migrations/20260608093210_AddWorkshopAndTeamMembers.cs`
8. `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/Migrations/20260608094841_AddBusinessPartnerRolesAndTaxId.cs`

| Table | Kind | Purpose | Key columns | Index intent | Lifecycle |
| --- | --- | --- | --- | --- | --- |
| `skus` | business | 物料和产品 SKU 主数据，用于计划、库存、质量、执行和流程型制造识别。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code` 是业务唯一键；`base_uom_code`、`inventory_uom_code`、`purchase_uom_code`、`sales_uom_code`、`manufacturing_uom_code` 记录单位口径；`batch_tracking_policy`、`serial_tracking_policy`、`shelf_life_policy_code`、`storage_condition_code` 和 `quality_required` 记录跨域追溯与质量前置策略。 | 唯一索引防止同一组织/环境内 SKU code 重复；`category + disabled` 支持按品类过滤可用 SKU 列表。 | 聚合创建后保留审计时间；停用通过 `disabled` 软关闭，不物理删除；已发生单据通过引用快照保持历史可读。 |
| `business_partners` | business | 供应商、客户、承运商等业务伙伴主数据。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code` 是业务唯一键；`partner_type` 是兼容主角色；`partner_roles` 保存 supplier/customer/carrier 等多角色；`tax_id` 在组织/环境内可选唯一。 | code 唯一索引用于同一伙伴跨多角色复用；`organization_id + environment_id + tax_id` 唯一索引防止税号重复；`partner_type + disabled` 支持按主角色列活跃记录。 | 聚合创建后保留；伙伴退出使用 `disabled` 停用；多角色调整通过 update 生命周期表达。 |
| `departments` | business | 组织部门主数据，用于归属、人员和组织层级。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code` 是业务唯一键；`parent_department_code` 表示上级部门 code。 | 唯一索引保护部门 code；`parent_department_code + disabled` 支持部门树和活跃子部门列表。 | 聚合创建后保留；组织调整通过停用旧部门并创建/维护新部门表达。 |
| `teams` | business | 班组主数据，记录所属部门和默认班次。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code` 是业务唯一键；`department_code` 与 `shift_code` 为跨聚合业务引用。 | 唯一索引保护班组 code；`department_code + disabled` 支持按部门列活跃班组。 | 聚合创建后保留；停用表示班组不再参与排产和执行。 |
| `personnel_skills` | business | 人员技能授予事实，包含有效期和技能等级。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + user_id + skill_code + effective_from` 是业务唯一键；`effective_from/effective_to` 为日期有效期。 | 唯一索引防止同一人员同一技能起始日重复；`user_id + disabled` 支持查人员技能；`skill_code + disabled` 支持按技能查人员。 | 创建后作为技能有效期事实保留；失效或撤销使用 `disabled`，历史有效期不物理删除。 |
| `units_of_measure` | business | 计量单位主数据，用于 SKU、BOM/配方、库存、质检、采购销售、报工和遥测数值口径。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code` 是业务唯一键；`dimension_type`、`precision`、`rounding_mode` 定义单位维度和数值规则。 | 唯一索引保护 UOM code；`dimension_type + disabled` 支持按维度筛选可用单位。 | 聚合创建后保留；停用表示不能被新主数据或新单据引用。 |
| `uom_conversions` | business | 单位换算规则，包含生效日、换算因子、偏移量、精度和舍入规则。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + from_uom_code + to_uom_code + effective_from` 是业务唯一键。 | 唯一索引防止同一生效日起重复换算；`from_uom_code + to_uom_code` 支持快速查转换路径。 | 创建后保留；新生效规则通过新记录表达，历史换算规则不物理删除。 |
| `sites` | business | 工厂或站点主数据，是工业资源层级根。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code` 是业务唯一键；`timezone` 用于本地日历解释。 | 唯一索引保护 site code；`disabled` 支持快速扫描活跃站点。 | 聚合创建后保留；停用表示不再给新资源或计划使用。 |
| `workshops` | business | 车间主数据，归属于 site，用于产线、工作中心和班组责任关系。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code` 是业务唯一键；`site_code`、`manager_user_id` 和 `description` 描述车间归属与责任人。 | 唯一索引保护 workshop code；`site_code + disabled` 支持按站点列活跃车间。 | 聚合创建后保留；停用表示不再给新产线、工作中心或班组关系使用。 |
| `production_lines` | business | 产线主数据，归属于 site/plant，可选归属 workshop。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code` 是业务唯一键；`site_code` 为上级站点业务引用；`workshop_code` 为可选车间业务引用。 | 唯一索引保护 line code；`site_code + disabled`、`workshop_code + disabled` 支持按站点或车间列活跃产线。 | 聚合创建后保留；停用表示不再接收新计划或执行引用。 |
| `shifts` | business | 班次主数据，用于日历、班组、排班、计划和执行。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code` 是业务唯一键；`starts_at`、`ends_at`、`crosses_midnight`、`paid_minutes` 描述本地班次窗口。 | 唯一索引保护 shift code；`disabled` 支持快速扫描活跃班次。 | 聚合创建后保留；停用表示不再给新日历或班组使用。 |
| `reference_data_codes` | business | 跨域引用代码表，CodeSet 以 `docs/architecture/master-data-dictionary-rules.md` 的保留表为准，例如物料类型、储存条件、质量原因和合规标签。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code_set + code` 是业务唯一键。 | 唯一索引保护同一 code set 内 code 不重复；`code_set + disabled` 支持按代码集查询可用代码。 | 聚合创建后保留；系统枚举禁止新增非标准码或改名，平台预置/工厂自定义分组按治理规则新增，语义变化应停用旧 code。 |
| `team_members` | business | 班组成员关系，连接 MasterData team 与 IAM/user id。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + team_code + user_id` 在 `disabled = false` 时是当前成员唯一键；`effective_from/effective_to` 保存历史有效期。 | 部分唯一索引防止同一人员在同一班组存在多个 active 关系，同时允许移除后保留历史并重新加入；`team_code + disabled` 支持列班组成员；`user_id + disabled` 支持按人员反查班组。 | 创建后保留历史；移除成员使用 `disabled` 标记，不物理删除。 |
| `work_centers` | business | 工作中心和资源主数据，用于产能计划、工艺路线、流程设备选择和执行路由。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code` 是业务唯一键；`resource_type`、`plant_code`、`line_code`、`workshop_code`、`default_calendar_code`、`capacity_unit` 和 `finite_capacity` 描述资源层级和计划能力。 | 唯一索引保护工作中心 code；`disabled`、`workshop_code + disabled` 支持快速扫描活跃工作中心和按车间过滤。 | 聚合创建后保留；停用表示不再接收计划或执行任务。 |
| `work_calendars` | business | 工作日历聚合根，定义可用于工作中心或计划的日历代码。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code` 是业务唯一键。 | 唯一索引保护日历 code；`disabled` 支持快速扫描活跃日历。 | 聚合创建后保留；停用表示日历不再分配给新计划。 |
| `work_calendar_working_times` | business | 工作日历拥有的周期性工作时间窗口。 | `id` 为 owned row Guid；`work_calendar_id` 指向 `work_calendars`；`day_of_week + starts_at + ends_at` 表示本地工作窗口。 | `work_calendar_id` 支持按日历加载所有工作时间；随聚合级联维护。 | owned collection，生命周期完全跟随 `work_calendars` 聚合。 |
| `device_assets` | business | 设备资产主数据，记录设备型号、产线、工作中心归属、资产类别、静态容量、关键等级和可维护/可遥测标记。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code` 是业务唯一键；`line_code` 与 `work_center_code` 为跨聚合业务引用；`minimum_capacity`、`maximum_capacity` 与 `capacity_uom_code` 记录流程设备静态能力。 | 唯一索引保护设备 code；`work_center_code + disabled` 支持按工作中心列活跃设备。 | 聚合创建后保留；退役或不可用使用 `disabled` 停用；PLC/DCS/SCADA 密钥、tag、报警和状态快照不进入本表。 |
| `numbering_counters` | business | MasterData service-local 编号计数器，用于普通创建请求自动分配 SKU 等业务号。 | `organization_id + environment_id + document_type + site_code + date_segment` 是计数器唯一范围；`current_value` 和 `version` 维护已分配序列与乐观并发。 | 唯一索引保护同一编号范围只有一个 counter。 | 创建请求按需创建并递增；不由用户直接维护。 |
| `numbering_idempotency_keys` | business | MasterData 创建请求幂等键记录，把客户端 key 绑定到已分配业务号和 payload fingerprint。 | `organization_id + environment_id + document_type + idempotency_key` 是唯一键；`number` 保存首次分配结果。 | 唯一索引阻止同一 key 在同一文档类型内重复创建不同单据。 | 随创建请求写入，保留用于重试去重和冲突诊断。 |
| `cap_published_messages` | system | CAP published message outbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部索引用于投递扫描和过期清理。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `cap_received_messages` | system | CAP received message inbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部索引用于消费幂等、分组扫描和过期清理。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `cap_locks` | system | CAP distributed lock table，由 netcorepal/CAP 基础设施维护。 | 主键 `Key`。 | 主键用于 CAP 内部协调。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `__EFMigrationsHistory` | system | EF Core migration history table，记录 BusinessMasterData 已应用迁移。 | `MigrationId + ProductVersion` 由 EF Core 维护。 | EF Core 用于判定待应用迁移。 | 必须位于 `business_masterdata` schema；业务代码不直接读写。 |

Known gaps:

1. CAP system tables 当前只在 catalog 中标记 system-owned，后续可补 table comment 便于数据库工具展示。

Release notes:

1. `AddBusinessPartnerRolesAndTaxId` 将 `business_partners.code` 从 partner-type 内唯一收紧为组织/环境内唯一；迁移会在创建新唯一索引前检查重复 `(organization_id, environment_id, code)`，发现旧数据重复时中止并要求先合并或重命名。

## BusinessQuality Schema

Schema: `quality`

Owner: `backend/services/Business/Quality`

Source:

1. `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/ApplicationDbContext.cs`
2. `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/EntityConfigurations/NonconformanceReportEntityTypeConfiguration.cs`
3. `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/EntityConfigurations/InspectionPlanEntityTypeConfiguration.cs`
4. `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/EntityConfigurations/InspectionRecordEntityTypeConfiguration.cs`
5. `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/Migrations/20260522092605_InitialQualityNcrSchema.cs`
6. `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/Migrations/20260523034736_AddQualityInspectionFacts.cs`

| Table | Kind | Purpose | Key columns | Index intent | Lifecycle |
| --- | --- | --- | --- | --- | --- |
| `nonconformance_reports` | business | Quality 拥有的不合格品报告，记录来源、不良数量、处置方案、审批链和关闭所需外部执行引用。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + ncr_code` 是业务唯一键；`source_type + source_document_id` 保留来源检验、报工或退货单据引用；`source_inspection_record_id` 可关联打开 NCR 的检验记录；`rework_work_order_id`、`scrap_movement_id`、`return_document_id` 只记录下游执行结果 ID。 | 唯一索引保护 NCR 编号；`organization_id + environment_id + status` 支持待处置列表；来源索引用于从检验/报工/退货追踪 NCR；`source_inspection_record_id` 索引用于从检验记录反查 NCR。 | 创建后从 `open` 进入 `disposition-in-progress`，关闭为 `closed`；关闭后不再修改处置；Quality 不直接改 MES/Inventory/ERP/WMS 数据。 |
| `inspection_plans` | business | Quality inspection plan 版本和适用性事实，定义收货、工序、终检、维修或客退场景的检验要求。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + plan_code` 是业务唯一键；`category`、`sku_code`、`partner_id`、`work_center_id`、`device_asset_id` 和 `document_type` 描述适用范围；`supersedes_plan_id` 指向上一版本。 | 唯一索引保护计划编码；`organization_id + environment_id + category + status` 支持按类别和状态查询；`organization_id + environment_id + status` 支持仅按状态过滤。 | 从 `draft` 创建，激活后进入 `active`；新版本会把旧版本标记为 `superseded`；激活后不再修改执行特性。 |
| `inspection_plan_characteristics` | business | 检验计划特性和抽样规则，记录每个计划版本要检查的项目。 | `id` 为 Guid v7 强类型 ID；`inspection_plan_id` 归属计划；`characteristic_code` 是计划内稳定特性编码；`method`、`severity`、`is_required` 和 `sampling_rule` 描述检查规则。 | `inspection_plan_id + characteristic_code` 唯一，防止同一计划版本内重复特性编码。 | 随所属检验计划创建和级联删除；计划激活后不再新增或变更执行特性。 |
| `inspection_records` | business | 检验执行记录和最终结果事实，保留来源单据、被检 SKU、数量、批次/序列号和处置引用。 | `id` 为 Guid v7 强类型 ID；`inspection_plan_id` 可选指向执行的计划版本；`source_type + source_service + source_document_id` 保留来源业务单据；`result` 为 `passed`、`rejected` 或 `conditional-release`；`nonconformance_report_id` 记录由该检验打开的 NCR。 | `organization_id + environment_id + source_service + source_document_id` 支持按来源追踪；`organization_id + environment_id + source_type + result` 支持场景+结果查询；`organization_id + environment_id + result` 支持仅按结果过滤。 | 创建即形成最终检验结果并发出 passed/rejected 集成事件；非 passed 结果必须保留处置原因；通过 `OpenNcrFromInspection` 最多关联一个 NCR。 |
| `inspection_result_lines` | business | 检验结果行测量值和缺陷事实，记录每个特性的观测值、结果、缺陷原因、缺陷数量和附件。 | `id` 为 Guid v7 强类型 ID；`inspection_record_id` 归属检验记录；`characteristic_code` 对应计划或临时检查项；`result` 为 `passed`、`failed` 或 `conditional-release`；`defect_quantity` 表示该行缺陷数量。 | `inspection_record_id + characteristic_code` 支持按记录读取结果行和重复检查。 | 随所属检验记录创建和级联删除；failed 或 conditional-release 行必须保留缺陷/让步原因，conditional-release 行必须有正数缺陷数量。 |
| `cap_published_messages` | system | CAP published message outbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部索引用于投递扫描和过期清理。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `cap_received_messages` | system | CAP received message inbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部索引用于消费幂等、分组扫描和过期清理。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `cap_locks` | system | CAP distributed lock table，由 netcorepal/CAP 基础设施维护。 | 主键 `Key`。 | 主键用于 CAP 内部协调。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `__EFMigrationsHistory` | system | EF Core migration history table，记录 BusinessQuality 已应用迁移。 | `MigrationId + ProductVersion` 由 EF Core 维护。 | EF Core 用于判定待应用迁移。 | 必须位于 `quality` schema；业务代码不直接读写。 |

## BusinessProductEngineering Schema

Schema: `product_engineering`

Owner: `backend/services/Business/ProductEngineering`

Source:

1. `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/ApplicationDbContext.cs`
2. `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/ProductEngineeringPersistenceServiceCollectionExtensions.cs`
3. `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/EntityConfigurations/*.cs`
4. `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/Migrations/20260522120104_InitialProductEngineeringSchema.cs`
5. `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/Migrations/20260523014957_CompleteProductEngineeringReleaseFacts.cs`
6. `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/Migrations/20260527073213_AddNumberingCounters.cs`

| Table | Kind | Purpose | Key columns | Index intent | Lifecycle |
| --- | --- | --- | --- | --- | --- |
| `engineering_documents` | business | 工程文档引用事实，登记 CAD、图纸和工艺文件在 File Storage 中的文件 ID、文件名、内容类型和文档类型。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + document_number + revision` 是文档版本业务唯一键；`file_id` 是 File Storage 文件业务引用。 | 文档号版本唯一索引防重复登记；`organization_id + environment_id + file_id + revision` 防止同一文件修订被重复绑定。 | 注册后作为外部文件引用保留；文件本体、对象存储 key 和下载授权仍由 File Storage 管理。 |
| `engineering_items` | business | 工程物料版本事实，记录工程料号、修订、名称和发布状态。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + item_code + revision` 是工程物料版本业务唯一键；`status` 表示 draft/published/archived。 | 业务唯一索引保护同一工程物料修订；`organization_id + environment_id + status` 支持按状态筛选工程物料版本。 | 可创建草稿或直接发布；发布后不可直接改名，后续变化通过新修订表达。 |
| `engineering_boms` | business | EBOM 版本聚合根，描述父工程物料和组件工程物料组成。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + bom_code + revision` 是 EBOM 版本唯一键；`parent_item_code` 为工程物料业务引用；`effective_date` 为发布时间。 | 业务唯一索引防重复发布；`parent_item_code + status` 支持按父项查看可用 EBOM。 | 草稿添加组件，发布后组件不可直接修改；下游 MBOM 引用已发布 EBOM 版本。 |
| `engineering_bom_lines` | business | EBOM 组件行，记录子工程物料、数量和单位。 | owned row `id`；`engineering_bom_id` 指向 EBOM 聚合；`child_item_code`、`quantity`、`unit_of_measure_code` 描述组件。 | `engineering_bom_id + child_item_code` 唯一，防止同一 EBOM 内组件重复。 | owned collection，生命周期跟随 `engineering_boms`。 |
| `manufacturing_boms` | business | MBOM/配方版本聚合根，将生产 SKU 的制造物料清单绑定到已发布 EBOM 版本。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + bom_code + revision` 是 MBOM 版本唯一键；`sku_code` 为生产 SKU；`engineering_bom_version_id` 保存 EBOM 业务版本引用。 | 业务唯一索引防重复发布；`sku_code + status` 支持按 SKU 查可用 MBOM。 | 草稿添加物料和配方参数，发布要求引用已发布 EBOM；发布后不可直接修改。 |
| `manufacturing_bom_material_lines` | business | MBOM 物料行，记录生产 SKU 所需原辅料、数量、单位和损耗率。 | owned row `id`；`manufacturing_bom_id` 指向 MBOM 聚合；`sku_code`、`quantity`、`unit_of_measure_code`、`scrap_rate` 描述用料。 | `manufacturing_bom_id + sku_code` 唯一，防止同一 MBOM 内物料重复。 | owned collection，生命周期跟随 `manufacturing_boms`。 |
| `manufacturing_bom_recipe_lines` | business | MBOM 配方/工艺参数行，用于流程型制造的目标参数。 | owned row `id`；`manufacturing_bom_id` 指向 MBOM 聚合；`parameter_code`、`target_value`、`unit_of_measure_code` 描述参数。 | `manufacturing_bom_id + parameter_code` 唯一，防止同一 MBOM 内参数重复。 | owned collection，生命周期跟随 `manufacturing_boms`。 |
| `routings` | business | 工艺路线版本聚合根，描述 SKU 的工作中心工序序列。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + routing_code + revision` 是路线版本唯一键；`sku_code` 为生产 SKU；`effective_date` 为发布时间。 | 业务唯一索引防重复发布；`sku_code + status` 支持按 SKU 查可用路线。 | 草稿添加工序，发布后不可直接修改；ProductionVersion 只能绑定已发布路线。 |
| `routing_operations` | business | 工艺路线工序行，记录工序顺序、工作中心、工序名称和标准工时。 | owned row `id`；`routing_id` 指向路线聚合；`sequence`、`work_center_code`、`operation_name`、`standard_minutes` 描述工序。 | `routing_id + sequence` 唯一，防止同一路线内工序顺序重复。 | owned collection，生命周期跟随 `routings`。 |
| `engineering_changes` | business | ECO/ECN 工程变更聚合根，记录变更号、原因、审批引用、影响范围和发布时间。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + change_number` 是变更唯一键；`approval_reference_id` 是审批链业务引用。 | 变更号唯一索引防重复；`organization_id + environment_id + status` 支持按状态查询变更。 | 草稿记录影响范围，经审批引用确认后发布；发布后作为工程变更事实保留。 |
| `engineering_change_affected_versions` | business | 工程变更影响版本行，记录受 ECO/ECN 影响的 EBOM、MBOM、Routing 或 ProductionVersion 业务版本 ID。 | owned row `id`；`engineering_change_id` 指向变更聚合；`version_kind + version_id` 标识受影响对象。 | `engineering_change_id + version_kind + version_id` 唯一，避免同一变更重复标注同一版本。 | owned collection，生命周期跟随 `engineering_changes`。 |
| `production_versions` | business | ProductEngineering 拥有的生产版本绑定事实，将已发布 MBOM 版本和工艺路线版本绑定到 SKU、生效日期、批量区间和 MES 工单创建选择规则。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + sku_code` 定义租户环境内 SKU 范围；`mbom_version_id` 和 `routing_version_id` 为工程版本业务引用；`valid_from/valid_to`、`lot_size_min/lot_size_max`、`priority` 和 `is_default` 驱动解析选择。 | `organization_id + environment_id + sku_code + status` 支持 MES 解析活跃生产版本；`sku_code + is_default + valid_from + valid_to` 支持默认版本重叠校验；`mbom_version_id + routing_version_id` 支持按工程版本追踪生产版本。 | 创建后为 `active`；归档为 `archived` 后不再被 MES 解析；当前版本只允许绑定已发布 MBOM/route，暂不暴露锁定状态。 |
| `numbering_counters` | business | ProductEngineering service-local 编号计数器，用于工程文档、工程物料、BOM、routing 和 ECO 普通创建请求自动分配业务号。 | `organization_id + environment_id + document_type + site_code + date_segment` 是计数器唯一范围；`current_value` 和 `version` 维护已分配序列与乐观并发。 | 唯一索引保护同一编号范围只有一个 counter。 | 创建请求按需创建并递增；不由用户直接维护。 |
| `numbering_idempotency_keys` | business | ProductEngineering 创建请求幂等键记录，把客户端 key 绑定到已分配业务号和 payload fingerprint。 | `organization_id + environment_id + document_type + idempotency_key` 是唯一键；`number` 保存首次分配结果。 | 唯一索引阻止同一 key 在同一文档类型内重复创建不同单据。 | 随创建请求写入，保留用于重试去重和冲突诊断。 |
| `cap_published_messages` | system | CAP published message outbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部索引用于投递扫描和过期清理。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `cap_received_messages` | system | CAP received message inbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部索引用于消费幂等、分组扫描和过期清理。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `cap_locks` | system | CAP distributed lock table，由 netcorepal/CAP 基础设施维护。 | 主键 `Key`。 | 主键用于 CAP 内部协调。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `__EFMigrationsHistory` | system | EF Core migration history table，记录 BusinessProductEngineering 已应用迁移。 | `MigrationId + ProductVersion` 由 EF Core 维护。 | EF Core 用于判定待应用迁移。 | 必须位于 `product_engineering` schema；业务代码不直接读写。 |

Known gaps:

1. CAP system tables 当前只在 catalog 中标记 system-owned，后续可补 table comment 便于数据库工具展示。

## BusinessInventory Schema

Schema: `inventory`

Owner: `backend/services/Business/Inventory`

Source:

1. `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Infrastructure/ApplicationDbContext.cs`
2. `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Infrastructure/InventoryPersistenceServiceCollectionExtensions.cs`
3. `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Infrastructure/EntityConfigurations/*.cs`
4. `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Infrastructure/Migrations/20260523020521_InitialInventorySchema.cs`
5. `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Infrastructure/Migrations/20260523064153_AddInventoryCodeCheckConstraints.cs`

| Table | Kind | Purpose | Key columns | Index intent | Lifecycle |
| --- | --- | --- | --- | --- | --- |
| `stock_locations` | business | Inventory 拥有的仓库、库区、库位或逻辑库存地点事实。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + location_code` 是业务唯一键；`site_code` 是 MasterData 站点业务引用。 | 唯一索引保护库存地点编码；`organization_id + environment_id + site_code + status` 支持按站点列可用库位。 | 创建后保留；状态字段表达启停，不物理删除。 |
| `stock_ledgers` | business | Inventory 拥有的当前库存余额事实，按 SKU、UOM、站点、地点、批次、序列号、质量状态和 owner 维度聚合。 | `id` 为 Guid v7 强类型 ID；`on_hand_quantity`、`reserved_quantity`、`ledger_version` 和 `row_version` 维护余额与并发控制。 | 唯一索引保护同一库存维度只有一条余额；维度索引用于可用量查询。 | 由库存移动和盘点调整驱动更新；不由 WMS、MES 或 ERP 平行维护余额。 |
| `stock_movements` | business | 追加式库存移动事实，记录来源服务、来源单据、幂等键和有符号数量。 | `id` 为 Guid v7 强类型 ID；`source_service + source_document_id + idempotency_key` 保护外部调用幂等；`quantity` 为正负移动量。 | 幂等唯一索引防重复入账；SKU/site/location 索引用于追踪库存历史。 | 创建后不可变；余额变化通过 ledger 更新体现。 |
| `stock_count_tasks` | business | 库存盘点任务事实，记录盘点范围、期望 ledger version、盘点数量、差异和状态。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + count_task_code` 是业务唯一键。 | 状态与库存维度索引支持待盘点任务查询。 | 创建后进入盘点生命周期；确认后产生调整事实。 |
| `stock_count_adjustments` | business | 盘点差异确认事实，记录盘点任务、幂等键、差异数量和生成的库存移动 ID。 | `id` 为 Guid v7 强类型 ID；`idempotency_key` 保护确认幂等；`movement_id` 指向库存移动业务 ID。 | 盘点任务和库存维度索引用于差异追踪。 | 确认后作为审计事实保留，不直接覆盖历史移动。 |
| `cap_published_messages` | system | CAP published message outbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部索引用于投递扫描和过期清理。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `cap_received_messages` | system | CAP received message inbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部索引用于消费幂等、分组扫描和过期清理。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `cap_locks` | system | CAP distributed lock table，由 netcorepal/CAP 基础设施维护。 | 主键 `Key`。 | 主键用于 CAP 内部协调。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `__EFMigrationsHistory` | system | EF Core migration history table，记录 BusinessInventory 已应用迁移。 | `MigrationId + ProductVersion` 由 EF Core 维护。 | EF Core 用于判定待应用迁移。 | 必须位于 `inventory` schema；业务代码不直接读写。 |

Known gaps:

1. Inventory MVP 已覆盖库存地点、余额、移动和盘点；预留/冻结、批次谱系、WMS 作业单和 ERP 成本闭环仍归后续业务切片补齐。

## BusinessMES Schema

Schema: `mes`

Owner: `backend/services/Business/Mes`

Source:

1. `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/ApplicationDbContext.cs`
2. `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/MesPersistenceServiceCollectionExtensions.cs`
3. `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/EntityConfigurations/*.cs`
4. `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/Migrations/20260523025528_InitialMesExecutionSchema.cs`
5. `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/Migrations/20260526022531_AddMesIntegrationEventDeadLetters.cs`
6. `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/Migrations/20260527073156_AddNumberingCounters.cs`
7. `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/Migrations/20260530095053_AddMesMaterialSupplyFacts.cs`
8. `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/Migrations/20260530115744_AddMesDispatchAssignmentFacts.cs`
9. `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/Migrations/20260603090745_AddMesDemandPlanningSourcePlanReference.cs`
10. `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/Migrations/20260605064424_AddMesQualityAndShiftHandoverFacts.cs`

| Table | Kind | Purpose | Key columns | Index intent | Lifecycle |
| --- | --- | --- | --- | --- | --- |
| `work_orders` | business | MES 持久化工单事实，记录 SKU、生产版本引用、计划数量、单位、优先级、交期、执行状态，以及从 DemandPlanning 或等价来源计划转单时复制的 durable source plan reference。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + work_order_id` 是业务唯一键；`production_version_id` 是 ProductEngineering 业务引用；`uom_code` 为来源计划单位快照；`source_system + source_document_type + source_document_id + source_demand_reference` 是跨服务来源计划/需求回溯引用，不建立跨 schema FK。 | 唯一约束保护同一组织/环境内工单号；SKU/交期索引用于排产扫描；`source_system + source_document_id` 索引用于从来源计划回查已转 MES 工单。 | 工单创建后进入 MES 执行生命周期，历史保留用于报工、入库请求、成本追踪和需求/MRP/MES 计划来源追溯。 |
| `operation_tasks` | business | MES 工序任务事实，保存工序顺序、工作中心、可选工作中心、持续时间、执行窗口和派工班次/人员/设备事实。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + operation_task_id` 是业务唯一键；`assigned_user_id`、`device_asset_id`、`shift_id` 和 `assigned_at_utc` 记录当前派工事实。 | 工单/工序顺序索引用于按工单加载工序；外键索引用于报工与工单追踪。 | 随工单创建或执行调整保留；状态表达排产、派工和执行进度。 |
| `production_reports` | business | MES 报工事实，记录工单/工序的良品数、报废数、完工标记和报工时间。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + report_no` 是报工业务号唯一键；`work_order_id` 与 `operation_task_id` 为 MES 业务引用。 | 报工号唯一索引用于重试和追踪；工单/工序/时间索引用于执行时间线查询。 | 报工创建后作为执行历史保留，不直接修改 Inventory、Quality 或 ERP 事实。 |
| `production_report_material_consumptions` | business | MES 报工引用的实际物料批次消耗事实，用于工单、报工和物料批次追溯。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + report_no` 指向报工号；记录 `material_id`、`material_lot_id`、消耗数量和线边领料申请号。 | 物料批次索引用于按批次追溯；工单索引用于工单谱系；报工/物料/批次唯一索引用于报工明细加载和重复写入兜底。 | 随报工写入，作为执行追溯历史保留；不拥有 Inventory 批次余额。 |
| `defect_records` | business | MES 制程不良事实，记录工单/可选工序、不良编码、数量、状态和记录时间，支撑 MES 质量上下文列表。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + defect_no` 是不良记录业务号唯一键；`work_order_id` 指向 MES 工单业务号，`operation_task_id` 是可选工序业务引用。 | 不良号唯一索引用于重试和追踪；工单/时间与工序/时间索引用于质量上下文分页和工序追溯。 | 创建后作为 MES 执行侧不良上下文保留；正式 NCR/处置仍由 Quality 服务拥有。 |
| `material_requirements` | business | MES 工单/工序级物料需求与供应快照，记录来自 released MBOM、Inventory 和 WMS readiness 的执行侧视图。 | `id` 为 Guid v7 强类型 ID；`work_order_id` 和可选 `operation_task_id` 绑定 MES 执行对象；`material_id`、`material_lot_id`、需求量、可用量、备料量和 source snapshot 保留来源。 | 工单/物料索引用于齐套检查；工序索引用于开工前阻断检查。 | 由上游 readiness/导入适配器或测试 fixture 捕获，后续重新计算可写新快照；MES 不把它作为库存余额真相源。 |
| `material_issue_requests` | business | MES 领料/备料申请与线边接收事实，跟踪 requested/received 数量和实际接收批次。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + request_no` 是领料申请业务号唯一键；记录工单、工序、物料、批次、请求/接收数量和状态。 | 请求号唯一索引用于重试和下游引用；工单/物料索引用于齐套汇总；工序索引用于工序开工检查。 | 创建后作为 MES 执行事实保留；WMS/Inventory 作业仍由各自服务拥有。 |
| `finished_goods_receipt_requests` | business | MES 完工入库请求事实，向 WMS/Inventory 边界表达成品收货意图。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + request_no` 是入库请求业务号唯一键；记录 `work_order_id`、`sku_id`、`quantity`、`uom_code` 和请求时间。 | 请求号唯一索引用于下游引用；工单/SKU/时间索引用于入库请求追踪。 | 创建后由下游库存/仓储服务消费并回写自身事实；MES 只保存请求事实。 |
| `schedule_results` | business | MES 规则排产结果事实，保存排产版本、触发原因、排产时间和 JSON assignment 结果。 | `schedule_version` 唯一；`assignments_json` 和 `affected_work_order_ids_json` 是 append-only JSON。 | 版本唯一索引支持读取当前/历史版本；触发源/时间索引用于诊断。 | 每次排产生成一条版本化结果，历史版本保留。 |
| `work_center_unavailabilities` | business | MES 工作中心不可用窗口事实，来自维修或手工约束，用于排产避让。 | `organization_id + environment_id + downtime_event_no` 是停机事件业务号唯一键；`work_center_id`、`from_utc`、`to_utc` 定义不可用窗口；`device_asset_id` 记录可选设备来源。 | 停机号唯一索引用于恢复和追踪；工作中心窗口索引用于排产查询；asset open 索引用于设备恢复处理。 | 打开窗口以 `to_utc = null` 表示仍不可用，关闭后保留历史。 |
| `device_asset_work_center_mappings` | business | MES 本地设备资产到工作中心映射，用于把 Maintenance 设备事件转换为 MES 排产约束。 | `organization_id + environment_id + device_asset_id` 唯一；`work_center_id` 是 MasterData 工作中心公开 ID。 | 唯一索引防止同一设备映射到多个 MES 工作中心。 | 映射作为本地配置事实保留。 |
| `shift_handovers` | business | MES 班次交接事实，记录班次、班组、交接状态、创建时间、接收时间和创建时未结事项数量。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + handover_no` 是交接业务号唯一键；`shift_id` 和 `team_id` 是 MasterData 公开 ID 快照；`open_issue_count` 当前为创建时 organization/environment 维度的粗粒度未结事项快照，源事实尚未全部具备 shift/team 归属。 | 交接号唯一索引用于接收和追踪；班次/创建时间索引用于班次交接列表分页。 | 创建后从 `Open` 进入 `Accepted`；重复接收保持首次接收时间，作为跨班组执行交接历史保留。 |
| `numbering_counters` | business | MES service-local 编号计数器，用于工单、报工、物料请求、不良、停机、交接和完工入库请求等普通创建请求自动分配业务号。 | `organization_id + environment_id + document_type + site_code + date_segment` 是计数器唯一范围；`current_value` 和 `version` 维护已分配序列与乐观并发。 | 唯一索引保护同一编号范围只有一个 counter。 | 创建请求按需创建并递增；不由用户直接维护。 |
| `numbering_idempotency_keys` | business | MES 创建请求幂等键记录，把客户端 key 绑定到已分配业务号和 payload fingerprint。 | `organization_id + environment_id + document_type + idempotency_key` 是唯一键；`number` 保存首次分配结果。 | 唯一索引阻止同一 key 在同一文档类型内重复创建不同单据。 | 随创建请求写入，保留用于重试去重和冲突诊断。 |
| `cap_published_messages` | system | CAP published message outbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部索引用于投递扫描和过期清理。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `cap_received_messages` | system | CAP received message inbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部索引用于消费幂等、分组扫描和过期清理。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `cap_locks` | system | CAP distributed lock table，由 netcorepal/CAP 基础设施维护。 | 主键 `Key`。 | 主键用于 CAP 内部协调。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `integration_event_dead_letters` | system | MES 消费侧在业务处理前拒绝的集成事件，用于版本不兼容、envelope 缺失等场景的排查和 replay 标记。 | `id` 为 Guid；`consumer_name`、`event_id`、`event_type`、`event_version`、`source_service`、`idempotency_key`、`status` 和 `event_json` 保留拒绝事实。 | `consumer_name + status + dead_lettered_at_utc` 支撑 pending 队列查看；`consumer_name + event_id` 支撑单事件排查。 | PostgreSQL profile 下由共享 persistent DLQ store 写入；operator replay 后标记 `Replayed`，不删除原始拒绝事实。 |
| `__EFMigrationsHistory` | system | EF Core migration history table，记录 BusinessMES 已应用迁移。 | `MigrationId + ProductVersion` 由 EF Core 维护。 | EF Core 用于判定待应用迁移。 | 必须位于 `mes` schema；业务代码不直接读写。 |

Known gaps:

1. MES 当前已有物料需求快照、领料/线边接收和报工消耗批次追溯事实；仍需后续把 released MBOM、Inventory/WMS 作业状态、Quality hold 和 ERP/采购到货通过正式 adapter/event 持续刷新这些执行快照。
2. MES durable source plan reference 只保存来源计划/需求的稳定业务标识和快照字段；DemandPlanning 仍拥有需求来源、MRP run、pegging 和计划建议事实，MES 不读取 `demand_planning` schema，也不对来源表建外键。

## BusinessDemandPlanning Schema

Schema: `demand_planning`

Owner: `backend/services/Business/DemandPlanning`

Source:

1. `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/ApplicationDbContext.cs`
2. `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/EntityConfigurations/*.cs`
3. `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/Migrations/20260523103405_InitialDemandPlanningSchema.cs`
4. `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/Migrations/20260527073227_AddNumberingCounters.cs`

| Table | Kind | Purpose | Key columns | Index intent | Lifecycle |
| --- | --- | --- | --- | --- | --- |
| `demand_sources` | business | DemandPlanning 拥有的销售订单、预测、安全库存等需求来源事实。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + demand_code` 是业务唯一键；保留 SKU、数量、需求日期和来源单据引用。 | 业务唯一索引防重复录入；SKU/日期/状态索引用于 MPS/MRP 输入扫描。 | 创建或调整后作为计划输入保留；不会创建正式销售、采购或生产单据。 |
| `master_production_schedules` | business | 日粒度 MPS bucket，固化 MRP 展开前的主生产计划口径。 | `id` 为 Guid v7 强类型 ID；记录 SKU、bucket date、计划数量和 UOM。 | SKU/bucket date 索引用于按物料和日期展开净需求。 | 由计划运行或手工调整生成，历史保留用于追踪 MRP 输入。 |
| `mrp_runs` | business | MRP 计算运行头和输入快照元数据。 | `id` 为 Guid v7 强类型 ID；`run_id` 为外部可见运行编号；保存计划窗口、状态和输入快照摘要。 | run id 唯一索引支持幂等读取；状态/创建时间索引用于运行列表。 | 每次 MRP 运行生成独立事实；不直接创建 ERP/MES 正式单据。 |
| `planning_suggestions` | business | MRP 生成的计划采购建议和计划工单建议。 | `id` 为 Guid v7 强类型 ID；记录 suggestion id、建议类型、SKU、数量、需求日期和状态。 | run id/status 索引用于按 MRP run 查看建议；SKU/date 索引用于计划员筛选。 | 可被接受、拒绝或关闭；接受只表达建议状态，不越权写 ERP/MES。 |
| `mrp_pegging_links` | business | 从计划建议回溯到需求、BOM 展开和库存快照的 pegging 链路。 | `id` 为 Guid v7 强类型 ID；记录 suggestion、demand、parent/child 关系和数量。 | suggestion id 索引用于读取建议追溯；run id 索引用于诊断整次展开。 | 随 MRP run 与建议生成后保留，用于解释计划结果。 |
| `numbering_counters` | business | DemandPlanning service-local 编号计数器，用于需求来源等普通创建请求自动分配业务号。 | `organization_id + environment_id + document_type + site_code + date_segment` 是计数器唯一范围；`current_value` 和 `version` 维护已分配序列与乐观并发。 | 唯一索引保护同一编号范围只有一个 counter。 | 创建请求按需创建并递增；不由用户直接维护。 |
| `numbering_idempotency_keys` | business | DemandPlanning 创建请求幂等键记录，把客户端 key 绑定到已分配业务号和 payload fingerprint。 | `organization_id + environment_id + document_type + idempotency_key` 是唯一键；`number` 保存首次分配结果。 | 唯一索引阻止同一 key 在同一文档类型内重复创建不同单据。 | 随创建请求写入，保留用于重试去重和冲突诊断。 |
| `cap_published_messages` | system | CAP published message outbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部索引用于投递扫描和过期清理。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `cap_received_messages` | system | CAP received message inbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部索引用于消费幂等、分组扫描和过期清理。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `cap_locks` | system | CAP distributed lock table，由 netcorepal/CAP 基础设施维护。 | 主键 `Key`。 | 主键用于 CAP 内部协调。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `__EFMigrationsHistory` | system | EF Core migration history table，记录 BusinessDemandPlanning 已应用迁移。 | `MigrationId + ProductVersion` 由 EF Core 维护。 | EF Core 用于判定待应用迁移。 | 必须位于 `demand_planning` schema；业务代码不直接读写。 |

## BarcodeLabel Schema

Schema: `barcode`

Owner: `backend/services/Business/BarcodeLabel`

Source:

1. `backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Infrastructure/ApplicationDbContext.cs`
2. `backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Infrastructure/EntityConfigurations/*.cs`
3. `backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Infrastructure/Migrations/20260523103022_InitialBarcodeLabelSchema.cs`

| Table | Kind | Purpose | Key columns | Index intent | Lifecycle |
| --- | --- | --- | --- | --- | --- |
| `barcode_rules` | business | 条码规则定义，描述编码范围、前缀、序列和业务对象绑定。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + rule_code` 是业务唯一键。 | rule code 唯一索引防重复；对象类型/状态索引用于选择可用规则。 | 规则版本作为配置事实保留；停用后不再用于新标签生成。 |
| `label_templates` | business | 标签模板引用，绑定 FileStorage file id、模板名称和变量 schema。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + template_code` 是业务唯一键；`template_file_id` 是 FileStorage 公开引用。 | template code 唯一索引防重复；状态索引用于筛选可用模板。 | 模板登记后保留；文件本体和下载授权仍由 FileStorage 管理。 |
| `label_print_batches` | business | 标签打印批次，记录模板、规则、业务来源和打印状态。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + batch_code` 是业务唯一键。 | batch code 唯一索引防重复；来源单据索引用于追踪打印来源。 | 批次创建后生成打印项，完成后作为追溯事实保留。 |
| `label_print_items` | business | 打印批次内单张标签项，保存条码值和打印结果。 | `id` 为 Guid v7 强类型 ID；`label_print_batch_id` 归属批次；`barcode_value` 是标签值。 | barcode value 唯一索引防止重复标签；batch 索引用于加载批次明细。 | 生命周期跟随打印批次；不会拥有库存数量或业务单据状态。 |
| `scan_records` | business | 扫码记录事实，记录扫码对象、结果、设备/人员和幂等键。 | `id` 为 Guid v7 强类型 ID；记录 barcode value、scan context、result 和 idempotency key。 | 幂等键索引用于 PDA/Connector 重试去重；barcode value 索引用于追溯。 | append-only 扫码事实；业务含义由调用方服务解释。 |
| `CAPLock` | system | CAP distributed lock table，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部协调。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `CAPPublishedMessage` | system | CAP published message outbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部投递扫描。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `CAPReceivedMessage` | system | CAP received message inbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部消费幂等。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `__EFMigrationsHistory` | system | EF Core migration history table，记录 BarcodeLabel 已应用迁移。 | `MigrationId + ProductVersion` 由 EF Core 维护。 | EF Core 用于判定待应用迁移。 | 必须位于 `barcode` schema；业务代码不直接读写。 |

## BusinessApproval Schema

Schema: `business_approval`

Owner: `backend/services/Business/Approval`

Source:

1. `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Infrastructure/ApplicationDbContext.cs`
2. `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Infrastructure/EntityConfigurations/*.cs`
3. `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Infrastructure/Migrations/20260523103025_InitialBusinessApprovalSchema.cs`
4. `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Infrastructure/Migrations/20260608080210_AddApprovalDelegationsAndApprovalPaging.cs`

| Table | Kind | Purpose | Key columns | Index intent | Lifecycle |
| --- | --- | --- | --- | --- | --- |
| `approval_templates` | business | 业务审批模板，按业务单据类型定义审批链。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + template_code` 是业务唯一键。 | template code 唯一索引防重复；业务单据类型/状态索引用于选择 active 模板。 | 模板激活后供新审批链复制步骤；历史模板保留。 |
| `approval_template_steps` | business | 模板中的有序审批步骤定义。 | `id` 为 Guid v7 强类型 ID；`approval_template_id` 归属模板；`step_no` 为模板内顺序。 | template + step no 唯一，防止步骤顺序重复。 | 随模板维护；运行链启动后会复制为 runtime steps。 |
| `approval_chains` | business | 运行中的业务审批链实例，绑定来源服务和来源单据。 | `id` 为 Guid v7 强类型 ID；记录 chain id、source service、source document id、status。 | 来源单据索引用于业务反查；状态索引用于待审批列表。 | 从 pending 进入 approved/rejected/returned 等状态；不替代 Ops 运维审批。 |
| `approval_steps` | business | 运行审批链中的步骤快照。 | `id` 为 Guid v7 强类型 ID；`approval_chain_id` 归属链；`step_no` 为链内顺序。 | chain + step no 唯一，支持按链加载步骤。 | 随链创建并被审批动作推进。 |
| `approval_decisions` | business | append-only 审批决定事实，记录审批人、动作、意见和时间。 | `id` 为 Guid v7 强类型 ID；`approval_chain_id` 和 `step_no` 绑定决策位置。 | chain/step/time 索引用于审计时间线。 | 决策只追加不物理删除，作为审批审计事实。 |
| `approval_delegations` | business | 审批授权委托事实，记录委托人与被委托人、单据类型范围和有效期。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id` 隔离租户；actor ref 字段保存公开身份引用。 | status + delegate actor 用于委托设置列表；delegator + document type 用于反查委托来源。 | 创建后为 active，可撤销为 revoked；不物理删除，作为授权委托审计事实。 |
| `cap_published_messages` | system | CAP published message outbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部投递扫描。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `cap_received_messages` | system | CAP received message inbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部消费幂等。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `cap_locks` | system | CAP distributed lock table，由 netcorepal/CAP 基础设施维护。 | 主键 `Key`。 | CAP 内部协调。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `__EFMigrationsHistory` | system | EF Core migration history table，记录 BusinessApproval 已应用迁移。 | `MigrationId + ProductVersion` 由 EF Core 维护。 | EF Core 用于判定待应用迁移。 | 必须位于 `business_approval` schema；业务代码不直接读写。 |

## WMS Schema

Schema: `wms`

Owner: `backend/services/Business/Wms`

Source:

1. `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Infrastructure/ApplicationDbContext.cs`
2. `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Infrastructure/EntityConfigurations/WmsEntityTypeConfigurations.cs`
3. `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Infrastructure/Migrations/20260523103259_InitialWmsSchema.cs`

| Table | Kind | Purpose | Key columns | Index intent | Lifecycle |
| --- | --- | --- | --- | --- | --- |
| `inbound_orders` | business | WMS 入库执行单头，记录来源单据、仓库、状态和收货完成事实。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + inbound_order_id` 是业务唯一键。 | 入库单号唯一索引防重复；来源单据索引用于 ERP/MES/外部通知追踪。 | 入库单创建后推进收货、上架和完成；库存移动由 Inventory 边界承接。 |
| `inbound_order_lines` | business | 入库行，记录 SKU、数量、批次/序列号和收货结果。 | `id` 为 Guid v7 强类型 ID；`inbound_order_id` 归属入库单。 | inbound order 索引用于加载明细。 | 生命周期随入库单推进并保留历史。 |
| `outbound_orders` | business | WMS 出库执行单头和复核包装事实。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + outbound_order_id` 是业务唯一键。 | 出库单号唯一索引防重复；来源单据索引用于销售/调拨追踪。 | 出库单创建后推进拣货、复核、包装和完成。 |
| `outbound_order_lines` | business | 出库行，记录 SKU、数量和拣货结果。 | `id` 为 Guid v7 强类型 ID；`outbound_order_id` 归属出库单。 | outbound order 索引用于加载明细。 | 生命周期随出库单推进并保留历史。 |
| `warehouse_tasks` | business | 上架和拣货任务事实，记录任务类型、库位、数量和状态。 | `id` 为 Guid v7 强类型 ID；记录 warehouse task id、任务类型和关联单据。 | 任务 id 唯一索引防重复；状态索引用于任务队列。 | 任务被完成或取消后保留执行历史。 |
| `count_executions` | business | WMS 盘点执行和差异输出事实。 | `id` 为 Guid v7 强类型 ID；记录 count execution id、库位/SKU/差异数量。 | execution id 唯一索引防重复；状态/仓库索引用于盘点列表。 | 完成后产生差异事实，后续由 Inventory 盘点调整边界承接。 |
| `wcs_tasks` | business | WCS adapter 任务映射、状态和外部任务诊断。 | `id` 为 Guid v7 强类型 ID；随 warehouse task 记录 `organization_id`、`environment_id`，并记录 warehouse task id、external task id、状态和失败原因。 | external task id 索引用于外部设备回调；`organization_id + environment_id + external_task_id` 支持租户内诊断查询；状态索引用于自动化队列。 | 由 dispatch/complete/fail 推进，保留自动化执行诊断；WCS 事件必须携带真实租户上下文。 |
| `inventory_movement_requests` | business | WMS 向 Inventory 请求库存移动的本地元数据。 | `id` 为 Guid v7 强类型 ID；记录业务来源、幂等键、movement type 和 posting 状态。 | 幂等键索引用于防重复 posting；状态索引用于补偿扫描。 | 默认通过 HTTP client posting 到 Inventory，测试环境可用 noop/fake client 替换。 |
| `CAPLock` | system | CAP distributed lock table，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部协调。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `CAPPublishedMessage` | system | CAP published message outbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部投递扫描。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `CAPReceivedMessage` | system | CAP received message inbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部消费幂等。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `__EFMigrationsHistory` | system | EF Core migration history table，记录 WMS 已应用迁移。 | `MigrationId + ProductVersion` 由 EF Core 维护。 | EF Core 用于判定待应用迁移。 | 必须位于 `wms` schema；业务代码不直接读写。 |

## ERP Schema

Schema: `erp`

Owner: `backend/services/Business/Erp`

Source:

1. `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/ApplicationDbContext.cs`
2. `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/EntityConfigurations/*.cs`
3. `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/Migrations/*_InitialErpProcurementSalesFinanceSchema.cs`
4. `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/Migrations/20260527073242_AddNumberingCounters.cs`

| Table | Kind | Purpose | Key columns | Index intent | Lifecycle |
| --- | --- | --- | --- | --- | --- |
| `purchase_requisitions` | business | ERP 采购申请，承接 DemandPlanning 建议或手工采购需求。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + suggestion_id` 幂等唯一。 | suggestion/requisition no 唯一索引用于防重复下游单据。 | 创建后进入采购流；ERP 不拥有计划建议状态。 |
| `request_for_quotations` / `request_for_quotation_lines` / `request_for_quotation_suppliers` | business | RFQ 头、行和邀请供应商引用。 | RFQ id 归属头；行和供应商通过 owning FK 加载。 | RFQ no 唯一索引用于采购过程追踪。 | 创建后接收供应商报价；供应商主数据仍由 MasterData 拥有。 |
| `supplier_quotations` / `supplier_quotation_lines` | business | 供应商报价事实，记录数量、单价和承诺日期。 | `quotation_no` 是业务编号；行归属报价。 | quotation no 唯一索引用于报价幂等。 | 接收后保留为采购比价事实。 |
| `purchase_orders` / `purchase_order_lines` | business | 采购订单头和行，记录订单金额与已收数量。 | `purchase_order_no` 是业务编号；行记录 ordered/received quantity。 | PO no 唯一索引用于收货反查。 | release 后可被收货推进；不写 Inventory 余额。 |
| `purchase_receipts` / `purchase_receipt_lines` | business | ERP 采购收货事实，记录质量状态摘要。 | `purchase_receipt_no` 是业务编号；行引用 PO line no。 | receipt no 唯一索引用于质量/WMS/AP 下游引用。 | 记录后不可变；质量检验、入库执行和 AP 候选由公开事件/API 接线。 |
| `opportunities` | business | 销售商机事实。 | `opportunity_no` 是业务编号；`customer_code` 引用 MasterData 客户。 | opportunity no 唯一索引用于 CRM-lite 追踪。 | 创建后作为报价前置事实保留。 |
| `quotations` / `quotation_lines` | business | 销售报价和报价行。 | `quotation_no` 是业务编号；行记录 SKU、数量、单价和交期。 | quotation no 唯一索引用于销售订单创建。 | 报价需显式 approve 才能创建销售订单。 |
| `sales_orders` / `sales_order_lines` | business | 销售订单和行，记录已释放发货数量。 | `sales_order_no` 是业务编号；行记录 ordered/delivered quantity。 | SO no 唯一索引用于发货请求反查。 | ERP 只拥有订单事实，WMS 拥有出库执行。 |
| `delivery_orders` / `delivery_order_lines` | business | ERP 发货请求，供 WMS outbound 执行。 | `delivery_order_no` 是业务编号；行引用 SO line no。 | delivery order no 唯一索引用于 WMS/AR 下游引用。 | release 后保留请求事实，不表达 WMS task state。 |
| `account_payables` | business | 应付候选事实。 | `payable_no` 是业务编号；记录来源单据、供应商、金额和已付金额。 | AP no 唯一索引用于幂等生成。 | 支付推进不允许超过 open amount；完整总账月结后置。 |
| `account_receivables` | business | 应收候选事实。 | `receivable_no` 是业务编号；记录来源单据、客户、金额和已收金额。 | AR no 唯一索引用于幂等生成。 | 收款推进不允许超过 open amount；完整收款核销后置。 |
| `cost_candidates` | business | 成本候选事实，引用 MES、Inventory 或 WMS 公开事实。 | `candidate_no` 是业务编号；`source_type + source_document_no` 描述来源。 | candidate no 唯一索引用于成本候选幂等。 | 作为候选保留，不代表最终成本结转。 |
| `journal_vouchers` / `journal_voucher_lines` | business | 平衡凭证事实和借贷行。 | `voucher_no` 是业务编号；行记录 account code、debit/credit。 | voucher no 唯一索引用于凭证审计。 | posted 后不可变，借贷必须平衡。 |
| `numbering_counters` | business | ERP service-local 编号计数器，用于采购、销售和财务普通创建请求自动分配业务号。 | `organization_id + environment_id + document_type + site_code + date_segment` 是计数器唯一范围；`current_value` 和 `version` 维护已分配序列与乐观并发。 | 唯一索引保护同一编号范围只有一个 counter。 | 创建请求按需创建并递增；不由用户直接维护。 |
| `numbering_idempotency_keys` | business | ERP 创建请求幂等键记录，把客户端 key 绑定到已分配业务号和 payload fingerprint。 | `organization_id + environment_id + document_type + idempotency_key` 是唯一键；`number` 保存首次分配结果。 | 唯一索引阻止同一 key 在同一文档类型内重复创建不同单据。 | 随创建请求写入，保留用于重试去重和冲突诊断。 |
| `cap_published_messages` | system | CAP published message outbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部投递扫描。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `cap_received_messages` | system | CAP received message inbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部消费幂等。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `cap_locks` | system | CAP distributed lock table，由 netcorepal/CAP 基础设施维护。 | 主键 `Key`。 | CAP 内部协调。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `__EFMigrationsHistory` | system | EF Core migration history table，记录 ERP 已应用迁移。 | `MigrationId + ProductVersion` 由 EF Core 维护。 | EF Core 用于判定待应用迁移。 | 必须位于 `erp` schema；业务代码不直接读写。 |

## BusinessIndustrialTelemetry Schema

Schema: `industrial_telemetry`

Owner: `backend/services/Business/IndustrialTelemetry`

Source:

1. `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/ApplicationDbContext.cs`
2. `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/EntityConfigurations/*.cs`
3. `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/Migrations/20260523112234_InitialIndustrialTelemetrySchema.cs`
4. `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/Migrations/20260601024046_AddRuntimeSourceMetadata.cs`
5. `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/Migrations/20260605092510_AddIndustrialTelemetryAlarmRules.cs`

| Table | Kind | Purpose | Key columns | Index intent | Lifecycle |
| --- | --- | --- | --- | --- | --- |
| `telemetry_tags` | business | IndustrialTelemetry 拥有的设备采集 tag 映射和采样策略元数据。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + device_asset_id + tag_key` 是业务唯一键；`value_type`、`unit_code` 和 `sampling_policy` 描述采集口径。 | tag 唯一索引防重复映射；设备/tag 维度支持采集配置查询。 | 创建后保留为采集元数据；PLC/DCS/SCADA 凭据不进入本 schema。 |
| `alarm_rules` | business | IndustrialTelemetry 拥有的报警规则阈值配置，记录设备、tag、比较符、阈值、单位、严重级别和启停状态。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + device_asset_id + rule_code` 是业务唯一键。 | 规则唯一索引防重复；设备+tag 索引用于规则维护和后续受控评估查询。 | 规则配置可更新启停和阈值；不保存 PLC/DCS/SCADA 控制命令或凭据。 |
| `device_state_snapshots` | business | 设备状态快照事实，记录设备状态、发生时间、来源系统/连接器和来源序列。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + source_system + source_connector + device_asset_id + source_sequence` 是幂等唯一键，且 source 元数据为空时按 not-distinct 参与唯一性。 | 来源 scope+序列唯一索引防重复写入；设备+时间索引用于时间线、current-state 和 runtime availability 查询。 | 只追加受控状态事实，不表达控制命令。 |
| `alarm_events` | business | 工业报警生命周期事实，记录 raise/clear、严重级别和外部报警 ID。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + external_alarm_id` 是幂等唯一键；`status` 表示 raised/cleared。 | 外部报警唯一索引防重复；设备+时间索引用于报警时间线。 | 报警创建后可清除；清除只补充 cleared facts，不删除历史。 |
| `telemetry_summaries` | business | 粗粒度采集汇总 bucket，保存 tag 数值摘要和可选来源系统/连接器元数据。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + source_system + source_connector + device_asset_id + tag_key + source_sequence` 是幂等唯一键，且 source 元数据为空时按 not-distinct 参与唯一性；`sample_count`、`min_value`、`max_value`、`average_value` 保存摘要。 | 来源 scope+序列唯一索引防重复；设备+tag+bucket 起点索引用于趋势查询。 | 作为可重算摘要事实保留；原始高速时序不进入平台业务库。 |
| `CAPLock` | system | CAP distributed lock table，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部协调。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `CAPPublishedMessage` | system | CAP published message outbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部投递扫描。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `CAPReceivedMessage` | system | CAP received message inbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部消费幂等。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `__EFMigrationsHistory` | system | EF Core migration history table，记录 BusinessIndustrialTelemetry 已应用迁移。 | `MigrationId + ProductVersion` 由 EF Core 维护。 | EF Core 用于判定待应用迁移。 | 必须位于 `industrial_telemetry` schema；业务代码不直接读写。 |

`device_state_snapshots` 和 `telemetry_summaries` 的 `source_system`、`source_connector` 是 #207 运行事实幂等 scope 的可选来源元数据列；唯一索引把 `organization_id`、`environment_id`、来源元数据、设备和 `source_sequence` 组合为幂等边界，telemetry summary 额外包含 `tag_key`。

## BusinessMaintenance Schema

Schema: `maintenance`

Owner: `backend/services/Business/Maintenance`

Source:

1. `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/ApplicationDbContext.cs`
2. `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/EntityConfigurations/MaintenanceEntityTypeConfigurations.cs`
3. `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/Migrations/20260523112317_InitialMaintenanceSchema.cs`
4. `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/Migrations/20260525050928_AddMaintenanceIntegrationEventDeadLetters.cs`
5. `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/Migrations/20260601032417_AddMaintenancePlanRuntimeWindow.cs`

| Table | Kind | Purpose | Key columns | Index intent | Lifecycle |
| --- | --- | --- | --- | --- | --- |
| `maintenance_work_orders` | business | 维修工单、报警引用、设备不可用和完工事实。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + source_alarm_id` 防止同一报警重复开单；`device_asset_id` 引用 MasterData 设备。 | source alarm 唯一索引用于报警幂等；状态/设备字段支撑维修看板查询。 | 手工或报警创建；完成后保留停机和备件引用事实。 |
| `maintenance_work_order_spare_part_lines` | business | 维修工单备件需求行，只记录需求和用量事实，不维护库存余额。 | `id` 为 Guid v7 强类型 ID；`maintenance_work_order_id` 归属工单；`sku_code`、`quantity`、`uom_code` 描述备件。 | 工单外键索引用于加载备件行。 | 生命周期随维修工单推进并保留历史。 |
| `maintenance_plans` | business | 预防性维护计划、保养周期和可选 runtime availability 维护窗口事实。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + plan_code` 是业务唯一键；`interval`、`starts_on`、`owner`、`window_start_utc` 和 `window_end_utc` 描述计划与维护窗口。 | 计划编码唯一索引用于防重复；设备与窗口字段支撑 availability 查询。 | 创建后作为计划事实保留；窗口边界必须成对提供并按 UTC 保存；后续版本化/暂停策略由后续切片补齐。 |
| `maintenance_inspections` | business | 点检记录，可关联维护计划或维修工单。 | `id` 为 Guid v7 强类型 ID；`maintenance_plan_id`、`maintenance_work_order_id` 是业务引用；`inspector`、`result` 和 `inspected_at_utc` 保存执行事实。 | 计划/工单引用支持追溯点检记录。 | 点检写入后不可覆盖历史，只通过新记录表达新检查。 |
| `downtime_reasons` | business | 维护域拥有的停机原因代码表。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + reason_code` 是业务唯一键。 | 原因码唯一索引防重复。 | 作为归因基础数据保留；删除/失效策略后续补齐。 |
| `integration_event_dead_letters` | system | Maintenance 消费侧在业务处理前拒绝的集成事件，用于版本不兼容、envelope 缺失等场景的排查和 replay 标记。 | `id` 为 Guid v7；`consumer_name`、`event_id`、`event_type`、`event_version`、`status` 和 `event_json` 保留拒绝事实。 | `consumer_name + status + dead_lettered_at_utc` 支撑 pending 队列查看；`consumer_name + event_id` 支撑单事件排查。 | 由 Maintenance 消费 guard 写入；operator replay 后标记 `Replayed`，不删除原始拒绝事实。 |
| `CAPLock` | system | CAP distributed lock table，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部协调。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `CAPPublishedMessage` | system | CAP published message outbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部投递扫描。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `CAPReceivedMessage` | system | CAP received message inbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部消费幂等。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `__EFMigrationsHistory` | system | EF Core migration history table，记录 BusinessMaintenance 已应用迁移。 | `MigrationId + ProductVersion` 由 EF Core 维护。 | EF Core 用于判定待应用迁移。 | 必须位于 `maintenance` schema；业务代码不直接读写。 |

`maintenance_plans.window_start_utc` 与 `maintenance_plans.window_end_utc` 是 #207 设备 runtime availability 的可选维护窗口列；窗口边界必须成对提供，且按 UTC 存储。

## BusinessScheduling Schema

Schema: `scheduling`

Owner: `backend/services/Business/Scheduling`

Source:

1. `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/ApplicationDbContext.cs`
2. `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/SchedulingPersistenceServiceCollectionExtensions.cs`
3. `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/EntityConfigurations/*.cs`
4. `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/Migrations/20260531111947_InitialSchedulingSchema.cs`

| Table | Kind | Purpose | Key columns | Index intent | Lifecycle |
| --- | --- | --- | --- | --- | --- |
| `schedule_problems` | business | APS lite 排程问题快照，保留输入 problem id、组织/环境、计划窗口和 deterministic fingerprint。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + problem_id` 是排程问题幂等范围；`problem_fingerprint` 记录输入快照指纹；`horizon_start_utc` / `horizon_end_utc` 记录排程窗口。 | `organization_id + environment_id + problem_id` 唯一索引用于同一业务上下文内重放、幂等诊断和从 plan 追溯输入。 | 生成排程方案时捕获并保留；不由下游服务直接修改。 |
| `schedule_plans` | business | APS lite 排程方案头，记录生成/发布状态、算法版本、合同版本和问题指纹。 | `id` 为 Guid v7 强类型 ID；`plan_id` 是公开方案 ID；`problem_id` 和 `problem_fingerprint` 指向输入快照；`status` 表示 generated/released 等生命周期。 | `plan_id` 唯一索引用于 detail、Gantt 和 release 查询。 | 生成后可发布；发布后作为 MES 和 Gantt 消费的稳定事实保留。 |
| `schedule_plan_assignments` | business | 排程方案内的工序到资源分配结果。 | `schedule_plan_id` 归属方案；`assignment_id` 是方案内公开分配 ID；`work_order_id`、`operation_id`、`resource_id`、`work_center_id` 和 `start_utc` / `end_utc` 描述分配事实。 | `schedule_plan_id + assignment_id` 唯一；`schedule_plan_id` 外键索引用于按方案加载。 | 生命周期随方案保留；不直接表达 MES 工序执行状态。 |
| `schedule_plan_resource_loads` | business | 排程方案的资源负载窗口和利用率。 | `schedule_plan_id` 归属方案；`resource_id`、`window_start_utc`、`window_end_utc`、`assigned_minutes`、`available_minutes` 和 `utilization` 描述负载。 | `schedule_plan_id` 外键索引用于读取方案负载。 | 由排程算法生成并随方案保留，可供产能/Gantt 查询。 |
| `schedule_plan_conflicts` | business | 排程方案生成过程中识别的交期、产能、日历、物料、质量或设备冲突。 | `schedule_plan_id` 归属方案；`conflict_id` 是公开冲突 ID；`reason_code`、`severity`、`work_order_id`、`operation_id`、`resource_id` 和 `message` 描述冲突。 | `schedule_plan_id` 外键索引用于按方案加载冲突。 | 生成时追加到方案；用于解释和前端展示，不替代下游执行异常。 |
| `schedule_plan_unscheduled_operations` | business | 在当前 horizon 内无法安排的工序及原因。 | `schedule_plan_id` 归属方案；`work_order_id`、`operation_id`、`reason_code` 和 `message` 描述不可排结果。 | `schedule_plan_id` 外键索引用于按方案加载不可排清单。 | 生成时写入并随方案保留；人工调整或新事实输入通过新方案表达。 |
| `cap_published_messages` | system | CAP published message outbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部投递扫描。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `cap_received_messages` | system | CAP received message inbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部消费幂等。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `cap_locks` | system | CAP distributed lock table，由 netcorepal/CAP 基础设施维护。 | 主键 `Key`。 | CAP 内部协调。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `__EFMigrationsHistory` | system | EF Core migration history table，记录 BusinessScheduling 已应用迁移。 | `MigrationId + ProductVersion` 由 EF Core 维护。 | EF Core 用于判定待应用迁移。 | 必须位于 `scheduling` schema；业务代码不直接读写。 |

## AppHub Schema

Schema: `apphub`

Owner: `backend/services/AppHub`

Source:

1. `backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/ApplicationDbContext.cs`
2. `backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/EntityConfigurations/*.cs`
3. `backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/Migrations/20260517055301_InitialCreate.cs`
4. `backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/Migrations/20260517074353_SchemaGovernanceMetadata.cs`
5. `backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/Migrations/20260526022515_AddAppHubIntegrationEventDeadLetters.cs`

| Table | Kind | Purpose | Key relationships and indexes |
| --- | --- | --- | --- |
| `applications` | business | 应用目录聚合根，记录 organization/environment 范围内的应用键和显示名称。 | `Id` 为 Guid v7 强类型 ID；`OrganizationId + EnvironmentId + ApplicationKey` 唯一；级联拥有 `application_versions`。 |
| `application_versions` | business | 应用版本子实体，记录一个应用已注册的版本号。 | `ApplicationId` 指向 `applications`；`ApplicationId + Version` 唯一。 |
| `managed_nodes` | business | Connector host 或受管节点目录，记录节点键、名称和部署形态。 | `Id` 为 Guid v7 强类型 ID；`OrganizationId + EnvironmentId + NodeKey` 唯一。 |
| `application_instances` | business | 应用实例聚合根，记录实例键、版本、节点、状态、健康和 connector 上报扩展信息。 | `InstanceKey` 唯一；`OrganizationId + EnvironmentId + ApplicationKey` 支持按应用查实例；拥有 heartbeat、状态历史和状态变化。 |
| `instance_heartbeat` | business | 应用实例最近一次心跳事实。 | `ApplicationInstanceId` 唯一，和 `application_instances` 一对一。 |
| `instance_state_history` | business | 应用实例状态观测历史，用于状态追踪、诊断和后续告警分析。 | `ApplicationInstanceId + ObservedAtUtc` 支持按实例时间线查询。 |
| `instance_status_changes` | business | 应用实例状态转换历史，记录 previous/current status 和变更时间。 | `ApplicationInstanceId + ChangedAtUtc` 支持按实例状态变更时间线查询。 |
| `registration_idempotency` | business | 注册请求幂等记录，避免 connector 重试导致重复实例注册。 | `IdempotencyKey` 唯一；记录 `RegistrationId` 和 `InstanceKey`。 |
| `cap_published_messages` | system | CAP published message outbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义；业务代码不直接读写。 |
| `cap_received_messages` | system | CAP received message inbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义；用于消费幂等和重试。 |
| `cap_locks` | system | CAP distributed lock table，由 netcorepal/CAP 基础设施维护。 | 主键 `Key`；用于 CAP 内部协调。 |
| `integration_event_dead_letters` | system | AppHub 消费侧在业务处理前拒绝的集成事件，用于版本不兼容、envelope 缺失等场景的排查和 replay 标记。 | `id` 为 Guid；`consumer_name`、`event_id`、`event_type`、`event_version`、`source_service`、`idempotency_key`、`status` 和 `event_json` 保留拒绝事实；`consumer_name + status + dead_lettered_at_utc` 支撑 pending 队列查看；`consumer_name + event_id` 支撑单事件排查。 |
| `__EFMigrationsHistory` | system | EF Core migration history table，记录 AppHub 已应用迁移。 | 必须位于 `apphub` schema；业务代码不直接读写。 |

Status value sources:

1. `ReportedStatus` 当前来自 Connector Protocol 的 `InstanceStateSnapshot.ReportedStatus`，初始值为 `unknown`；后续如收敛为枚举，必须先更新 Connector Protocol 和 catalog。
2. `HealthStatus` 当前来自 Connector Protocol 的 `InstanceStateSnapshot.HealthStatus`，初始值为 `unknown`；它不是数据库枚举。
3. `Reachable` 是 heartbeat reachability boolean，不替代 `HealthStatus`。

Known gaps:

1. CAP system tables 当前只在 catalog 中标记 system-owned，后续可补 table comment 便于数据库工具展示。

## Ops Schema

Schema: `ops`

Owner: `backend/services/Ops`

Source:

1. `backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/ApplicationDbContext.cs`
2. `backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/EntityConfigurations/*.cs`
3. `backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/Migrations/20260517055218_InitialCreate.cs`
4. `backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/Migrations/20260517074341_SchemaGovernanceMetadata.cs`
5. `backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/Migrations/20260526091719_AddOperationApprovalFields.cs`

| Table | Kind | Purpose | Key relationships and indexes |
| --- | --- | --- | --- |
| `operation_tasks` | business | 运维操作任务聚合根，记录目标实例、操作码、请求人、幂等范围、参数、审批状态和当前执行状态。 | `Id` 为业务生成 string 强类型 ID；`IdempotencyScope` 唯一；`OrganizationId + EnvironmentId + Status + RequestedAtUtc` 支持任务列表、审批队列和状态扫描。 |
| `operation_attempts` | business | 操作任务执行尝试，记录 connector host 领取、开始、完成和失败原因。 | `OperationTaskId` 指向 `operation_tasks`；索引用于按任务查执行历史。 |
| `audit_records` | business | 操作任务审计记录，记录动作、操作者、发生时间、correlation id 和 `IntegrityHash`。 | `OperationTaskId + OccurredAtUtc` 支持按任务时间线展示审计；`IntegrityHash` 是不可变审计字段的 tamper-evident SHA-256 摘要。 |
| `cap_published_messages` | system | CAP published message outbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义；业务代码不直接读写。 |
| `cap_received_messages` | system | CAP received message inbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义；用于消费幂等和重试。 |
| `cap_locks` | system | CAP distributed lock table，由 netcorepal/CAP 基础设施维护。 | 主键 `Key`；用于 CAP 内部协调。 |
| `__EFMigrationsHistory` | system | EF Core migration history table，记录 Ops 已应用迁移。 | 必须位于 `ops` schema；业务代码不直接读写。 |

Status value sources:

1. `operation_tasks.Status` 当前由 `OperationTask` 聚合行为维护：`approval-pending`、`queued`、`dispatched`、`completed`、`failed`、`rejected`。
2. `operation_templates.RequiresApproval=true` 的高风险动作会先进入 Ops 自有 approval gate；审批通过后才可被 Connector Host claim，审批拒绝后进入 `rejected` 终态。
3. `operation_attempts.Status` 当前由 `OperationAttempt` 维护：`started`、`completed`、`failed`。
4. Connector Protocol 的 `OperationResult.ExecutionStatus=succeeded` 映射为 Ops task/attempt 的 `completed`；其它失败结果映射为 `failed`。

Known gaps:

1. CAP system tables 当前只在 catalog 中标记 system-owned，后续可补 table comment 便于数据库工具展示。

## IAM Schema

Schema: `iam`

Owner: `backend/services/Iam`

Source:

1. `backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/ApplicationDbContext.cs`
2. `backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/IamPersistenceServiceCollectionExtensions.cs`
3. `backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/EntityConfigurations/*.cs`
4. `backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/Migrations/20260517102102_InitialIamPersistentAuth.cs`

| Table | Kind | Purpose | Key relationships and indexes |
| --- | --- | --- | --- |
| `organizations` | business | IAM 组织范围事实，用于租户与访问 scope 的基础边界。 | `Id` 为调用方提供的有界 string 强类型 ID；包含组织名称、状态、软删除和 row version。 |
| `environments` | business | IAM 环境范围事实，用于把 membership、credential 和后续资源访问限制在组织内环境。 | `OrganizationId + Id` 唯一；`OrganizationId` 是跨表业务引用，不通过跨聚合外键扩大服务耦合。 |
| `users` | business | 后台用户认证事实，记录 login name、email、password hash、启用状态、security stamp、permission version、登录时间、失败计数、最近失败时间和锁定截止时间。 | `LoginName` 唯一；`Email` 唯一；`Id` 为调用方提供的有界 string 强类型 ID。 |
| `roles` | business | IAM 角色事实，用于把权限码分组后授予 membership。 | `RoleName` 唯一；拥有 `role_permissions`。 |
| `role_permissions` | business | 角色拥有的权限码集合。 | `RoleId` 指向 `roles`；`RoleId + PermissionCode` 唯一。 |
| `memberships` | business | 用户在 organization/environment scope 内的成员身份。 | `UserId + OrganizationId + EnvironmentId` 唯一；拥有 `membership_roles`。 |
| `membership_roles` | business | membership 绑定的角色集合。 | `MembershipId` 指向 `memberships`；`MembershipId + RoleId` 唯一。 |
| `user_sessions` | business | 用户 refresh session，保存 refresh token hash、issue/expiry/revoke 时间、permission version、client info、IP、认证方式、外部 provider/subject 和 MFA 验证时间。 | `RefreshTokenHash` 支持 refresh lookup；`UserId + RevokedAtUtc` 支持按用户扫描活动/撤销会话；`ExternalProvider + ExternalSubject` 支持 SSO session binding 查询。 |
| `connector_host_credentials` | business | Connector Host 机器身份凭据，记录 connector host id、organization/environment、secret hash 和有效期。 | `ConnectorHostId` 唯一；拥有 `connector_host_credential_capabilities`。 |
| `connector_host_credential_capabilities` | business | Connector Host credential 被授予的能力码集合。 | `ConnectorHostCredentialId` 指向 `connector_host_credentials`；`ConnectorHostCredentialId + CapabilityCode` 唯一。 |
| `external_clients` | business | 外部系统或平台应用的 client_credentials 身份，保存 client id、display name、organization/environment、secret hash、启用状态、permission version 和有效期。 | `ClientId` 唯一；secret 只保存 hash。 |
| `authorization_grants` | business | 非用户主体的授权授予事实，首批覆盖 `external-client` 的 permission grant，并支持 resource type/resource id 范围。 | `PrincipalType + PrincipalId + OrganizationId + EnvironmentId + PermissionCode + ResourceType + ResourceId` 唯一；`*` 表示 wildcard；支持有效期和撤销时间。 |
| `seed_manifests` | business | IAM seed 执行清单，用于记录初始 admin、platform admin role、seed permissions、membership 和 local Connector Host credential seed 的版本化幂等执行。 | `SeedName + SeedVersion` 唯一；记录 owner service 与 applied time。 |
| `__EFMigrationsHistory` | system | EF Core migration history table，记录 IAM 已应用迁移。 | 必须位于 `iam` schema；业务代码不直接读写。 |

Known gaps:

1. Gateway-wide permission enforcement 已覆盖现有 Console API；Gateway 转发 bearer token 与 permission/context 到 IAM internal authorization check endpoint，不直接读取 IAM schema。
2. ExternalClient 当前首批覆盖 seed 驱动的 `client_credentials` 发 token 与 AuthorizationGrant 权限检查闭环；P2 已补资源范围 ABAC grant enforcement。IAM 另提供外部 OIDC callback/MFA hook 作为企业身份入口，但不包含完整 OAuth/OIDC 授权码服务器、动态客户端注册 UI 或 consent 流程。
3. 客户发布 seed input 与 migration bundle 仍属于后续 release work。

## FileStorage Schema

Schema: `filestorage`

Owner: `backend/services/FileStorage`

Source:

1. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/ApplicationDbContext.cs`
2. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/FileStoragePersistenceServiceCollectionExtensions.cs`
3. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/EntityConfigurations/*.cs`
4. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/Migrations/20260521061426_InitialFileStorageSchema.cs`
5. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/Migrations/20260608105829_AddStoredFilesTenantListIndex.cs`

| Table | Kind | Purpose | Key relationships and indexes |
| --- | --- | --- | --- |
| `stored_files` | business | FileStorage 已完成文件的公开元数据与内部对象定位事实。 | `file_id` 为业务生成 string ID；`object_key` 唯一且仅限内部持久化；`organization_id + environment_id + owner_service + owner_type + owner_id` 支持按业务 owner 查询；`organization_id + environment_id + completed_at_utc` 支持 Console 文件列表按租户分页读取。 |
| `upload_sessions` | business | 上传会话元数据，记录预留 fileId、调用方上下文、provider、过期时间和完成状态。 | `upload_session_id` 为业务生成 string ID；`file_id` 唯一；`object_key` 唯一；`organization_id + environment_id + expires_at_utc` 支持过期会话扫描。 |
| `download_grants` | business | 短期下载授权元数据，当前用于平台控制下载路径；tus provider 下可映射到本地 tus 字节内容。 | `download_grant_id` 为业务生成 string ID；`file_id` 指向 `stored_files`；`organization_id + environment_id + file_id + expires_at_utc` 支持授权校验和清理。 |
| `__EFMigrationsHistory` | system | EF Core migration history table，记录 FileStorage 已应用迁移。 | 必须位于 `filestorage` schema；业务代码不直接读写。 |

Known gaps:

1. 默认运行路径仍可使用 in-memory store 和 `server-proxy` metadata stub；设置 `Persistence:Provider=PostgreSQL` 后可启用 PostgreSQL-backed FileStorage service，客户 release bundle 仍待后续。
2. 设置 `FileStorage:UploadProvider=tus` 后已具备本地 tus `HEAD`/`PATCH` offset 传输和 download grant content 读取能力；size/checksum 强校验、过期清理任务和更完整 tus 兼容性仍属于 hardening。
3. tus 端点当前按平台内部服务边界实现为 `AllowAnonymous`，生产入口需要由 Gateway/auth 层保护；MinIO/S3 multipart 不进入 MVP，放到后续对象存储部署联调。`object_key` 不得被提升为公开 API、SDK DTO、Gateway facade 或 Console generated client 字段。

## Notification Schema

Schema: `notification`

Owner: `backend/services/Notification`

Source:

1. `backend/services/Notification/src/Nerv.IIP.Notification.Infrastructure/ApplicationDbContext.cs`
2. `backend/services/Notification/src/Nerv.IIP.Notification.Infrastructure/EntityConfigurations/*.cs`
3. `backend/services/Notification/src/Nerv.IIP.Notification.Infrastructure/Migrations/20260521080709_InitialNotificationSchema.cs`
4. `backend/services/Notification/src/Nerv.IIP.Notification.Infrastructure/Migrations/20260521091128_AddNotificationCapStorage.cs`
5. `backend/services/Notification/src/Nerv.IIP.Notification.Infrastructure/Migrations/20260526022335_AddNotificationIntegrationEventDeadLetters.cs`

| Table | Kind | Purpose | Key relationships and indexes |
| --- | --- | --- | --- |
| `notification_intents` | business | 平台服务提交的通知意图聚合根，用于站内消息和任务通知。 | `Id` 为 Guid v7；`OrganizationId + EnvironmentId + SourceService + SourceEventType + DedupeKey` 唯一；拥有 message 和 task 子事实。 |
| `notification_messages` | business | 面向收件人的站内通知消息。 | `NotificationIntentId` 指向 `notification_intents`；`RecipientRef + Status + CreatedAtUtc` 支持收件箱扫描。 |
| `notification_tasks` | business | 可操作通知任务，用于待办、失败处理或后续审批联动。 | `NotificationIntentId` 指向意图；`MessageId` 指向对应消息；`RecipientRef + Status + CreatedAtUtc` 支持任务列表。 |
| `delivery_attempts` | business | 通知投递尝试记录，为后续外部 channel provider、失败重试和投递诊断预留。 | `NotificationMessageId` 指向消息；`Channel + Status + AttemptedAtUtc` 支持渠道维度排查。 |
| `processed_integration_events` | system | Notification 业务 inbox，记录已处理的集成事件，避免重复业务副作用。 | `ConsumerName + EventId` 唯一；`SourceService + EventType + ProcessedAtUtc` 支持消费诊断。 |
| `integration_event_dead_letters` | system | Notification 消费侧在业务处理前拒绝的集成事件，用于版本不兼容、envelope 缺失等场景的排查和 replay 标记。 | `id` 为 Guid；`consumer_name`、`event_id`、`event_type`、`event_version`、`source_service`、`idempotency_key`、`status` 和 `event_json` 保留拒绝事实；`consumer_name + status + dead_lettered_at_utc` 支撑 pending 队列查看；`consumer_name + event_id` 支撑单事件排查。 |
| `cap_published_messages` | system | CAP published message outbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义；业务代码不直接读写。 |
| `cap_received_messages` | system | CAP received message inbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义；用于 broker 级消费幂等。 |
| `cap_locks` | system | CAP distributed lock table，由 netcorepal/CAP 基础设施维护。 | 主键 `Key`；用于 CAP 内部协调。 |
| `__EFMigrationsHistory` | system | EF Core migration history table，记录 Notification 已应用迁移。 | 必须位于 `notification` schema；业务代码不直接读写。 |

Known gaps:

1. Notification 当前已有站内消息、任务、业务 inbox、CAP storage 和 persistent DLQ 基线；偏好/订阅、外部渠道 provider、限流和模板映射仍按 Notification baseline 后续深化。
2. `integration_event_dead_letters` 只负责拒绝事实和 replay 标记；自动 replay executor、失败状态机和管理入口仍属于 P2 后续切片。

## 后续服务建表前清单

新服务进入建表阶段前，必须先补充本节对应条目，不能等迁移生成后再回忆设计意图。

| Service | Expected schema | Catalog status | Implemented | Validated | Release-supported | Required before first migration |
| --- | --- | --- | --- | --- | --- | --- |
| IAM | `iam` | Implemented | Yes | Yes | No | 已有 PostgreSQL `iam` schema、初始 migration、schema convention tests、idempotent seed、登录/refresh/logout/`/me` 和 Connector Host credential validation；客户 release bundle 仍待后续。 |
| FileStorage | `filestorage` | Implemented baseline | Yes | Yes | No | 已有 `stored_files`、`upload_sessions`、`download_grants` 初始 migration、schema convention tests、PostgreSQL-backed API service、server-proxy metadata API 和本地 tus MVP 传输能力；客户 release bundle 仍待后续；MinIO/S3 multipart 为 post-MVP 部署联调项。 |
| BusinessMasterData | `business_masterdata` | Implemented | Yes | Yes | No | 已有 Layer 0 realignment schema、numbering counter/idempotency tables、migration、schema convention tests 和 verify script；客户 release bundle 仍待后续。 |
| BusinessProductEngineering | `product_engineering` | Implemented | Yes | Yes | No | 已有 EngineeringDocument、EngineeringItem、EBOM、MBOM、Routing、ECO/ECN、ProductionVersion、numbering counter/idempotency tables、migration、schema convention tests 和 verify script；客户 release bundle 仍待后续。 |
| BusinessInventory | `inventory` | Implemented | Yes | Yes | No | 已有库存地点、库存台账、库存移动、盘点任务和盘点调整 schema、migration、schema convention tests 和 verify script；客户 release bundle 仍待后续。 |
| BusinessQuality | `quality` | Implemented | Yes | Yes | No | 已有 NCR、InspectionPlan、InspectionRecord schema、migration、schema convention tests 和 verify script；客户 release bundle 仍待后续。 |
| BusinessMES | `mes` | Implemented | Yes | Yes | No | 已有工单、工序任务、物料需求快照、领料/线边接收、报工、报工物料批次消耗、不良记录、完工入库请求、排产结果、工作中心不可用窗口、设备映射、班次交接、numbering counter/idempotency tables 和 persistent DLQ schema、migration、schema convention tests 和 verify script；客户 release bundle 仍待后续。 |
| BusinessDemandPlanning | `demand_planning` | Implemented | Yes | Yes | No | 已有需求来源、MPS、MRP run、pegging、计划建议和 numbering counter/idempotency tables schema、migration、schema convention tests 和 verify script；客户 release bundle 仍待后续。 |
| BarcodeLabel | `barcode` | Implemented | Yes | Yes | No | 已有条码规则、标签模板、打印批次、打印项和扫码记录 schema、migration、schema convention tests 和 verify script；客户 release bundle 仍待后续。 |
| BusinessApproval | `business_approval` | Implemented | Yes | Yes | No | 已有审批模板、审批链、审批步骤、审批决定和审批委托 schema、migration、schema convention tests 和 verify script；客户 release bundle 仍待后续。 |
| WMS | `wms` | Implemented | Yes | Yes | No | 已有入库、出库、仓库任务、盘点执行、WCS 任务和库存移动请求元数据 schema、migration、schema convention tests 和 verify script；客户 release bundle 仍待后续。 |
| ERP | `erp` | Implemented | Yes | Yes | No | 已有 Procurement、Sales、Finance MVP 和 numbering counter/idempotency tables schema、migration、schema convention tests 和 verify scripts；客户 release bundle、完整总账月结和银行/税务对账仍待后续。 |
| BusinessIndustrialTelemetry | `industrial_telemetry` | Implemented | Yes | Yes | No | 已有 tag、设备状态快照、报警事件和采集汇总 schema、migration、schema convention tests 和 verify script；客户 release bundle 仍待后续。 |
| BusinessMaintenance | `maintenance` | Implemented | Yes | Yes | No | 已有维修工单、保养计划、点检、停机原因和备件行 schema、migration、schema convention tests 和 verify script；客户 release bundle 仍待后续。 |
| BusinessScheduling | `scheduling` | Implemented | Yes | Yes | No | 已有排程问题快照、排程方案、分配、资源负载、冲突、不可排工序和 CAP system tables schema、migration、schema convention tests、BusinessGateway facade 和 verify script；客户 release bundle、高级优化器仍待后续。 |
| Notification | `notification` | Implemented baseline | Yes | Yes | No | 已有通知意图、站内消息、任务、投递尝试、业务 inbox、CAP storage 和 persistent DLQ schema、migration、schema convention tests；偏好/订阅、外部渠道 provider、限流和模板映射仍待后续。 |
| Knowledge | `knowledge` | Planned only | No | No | No | 知识源、文档、分片、索引状态、向量/全文索引边界和重建策略；关系库保存索引元数据，外部向量库保存可重建索引。 |
| AI Integration | `ai` or `ai_integration` | Planned only | No | No | No | 模型/provider 配置、工具授权、调用审计、配额周期、prompt/version 归档、审批挂点和敏感信息边界。 |
| Observability indexes | `observability` | Baseline only | No | No | No | 见 `docs/architecture/observability-baseline.md`；建表前补 LogChunk、LogEntryIndex、归档任务、retention 和 Gateway 查询边界。 |

## 下一轮 hardening 建议

1. 生成或维护简版 ER 图，以 AppHub/Ops/IAM 当前 catalog 和数据库注释为输入。
2. 在新增 Knowledge、AI Integration 或 Observability 索引迁移前，先补该服务的 catalog 草案，再写实体配置、schema convention tests 和 migration；Notification/FileStorage 后续新增表时继续按本 catalog 和 schema convention tests 更新。
3. 后续如 CAP system tables 需要进入客户数据字典展示，补充 system table comment 或保持 catalog 的 system-owned 标记为权威说明。
