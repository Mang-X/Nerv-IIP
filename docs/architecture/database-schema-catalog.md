# 数据库 Schema Catalog

本文档记录当前 Nerv-IIP 已落地和计划落地的数据库 schema。物理结构仍以 EF Core migrations 和 EntityConfigurations 为准；本文档负责解释业务语义、边界、索引意图和可视化上下文。

当前 catalog 覆盖第五阶段已经迁移验证通过、并在第六阶段完成 schema governance hardening 的 AppHub 与 Ops，第七阶段已经落地 IAM Persistent Auth Foundation 的 IAM，以及 BusinessProductEngineering 和 FileStorage 第一阶段 MVP 的 schema 基线。Notification、Knowledge、AI Integration 和 Observability 索引在真正建表前必须补充相同粒度的条目和 convention tests。

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

| Table | Kind | Purpose | Key columns | Index intent | Lifecycle |
| --- | --- | --- | --- | --- | --- |
| `skus` | business | 物料和产品 SKU 主数据，用于计划、库存、质量、执行和流程型制造识别。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code` 是业务唯一键；`base_uom_code`、`inventory_uom_code`、`purchase_uom_code`、`sales_uom_code`、`manufacturing_uom_code` 记录单位口径；`batch_tracking_policy`、`serial_tracking_policy`、`shelf_life_policy_code`、`storage_condition_code` 和 `quality_required` 记录跨域追溯与质量前置策略。 | 唯一索引防止同一组织/环境内 SKU code 重复；`category + disabled` 支持按品类过滤可用 SKU 列表。 | 聚合创建后保留审计时间；停用通过 `disabled` 软关闭，不物理删除；已发生单据通过引用快照保持历史可读。 |
| `business_partners` | business | 供应商、客户、承运商等业务伙伴主数据。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + partner_type + code` 是业务唯一键。 | 唯一索引隔离不同 partner type 的 code；`partner_type + disabled` 支持按伙伴类型列活跃记录。 | 聚合创建后保留；伙伴退出使用 `disabled` 停用。 |
| `departments` | business | 组织部门主数据，用于归属、人员和组织层级。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code` 是业务唯一键；`parent_department_code` 表示上级部门 code。 | 唯一索引保护部门 code；`parent_department_code + disabled` 支持部门树和活跃子部门列表。 | 聚合创建后保留；组织调整通过停用旧部门并创建/维护新部门表达。 |
| `teams` | business | 班组主数据，记录所属部门和默认班次。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code` 是业务唯一键；`department_code` 与 `shift_code` 为跨聚合业务引用。 | 唯一索引保护班组 code；`department_code + disabled` 支持按部门列活跃班组。 | 聚合创建后保留；停用表示班组不再参与排产和执行。 |
| `personnel_skills` | business | 人员技能授予事实，包含有效期和技能等级。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + user_id + skill_code + effective_from` 是业务唯一键；`effective_from/effective_to` 为日期有效期。 | 唯一索引防止同一人员同一技能起始日重复；`user_id + disabled` 支持查人员技能；`skill_code + disabled` 支持按技能查人员。 | 创建后作为技能有效期事实保留；失效或撤销使用 `disabled`，历史有效期不物理删除。 |
| `units_of_measure` | business | 计量单位主数据，用于 SKU、BOM/配方、库存、质检、采购销售、报工和遥测数值口径。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code` 是业务唯一键；`dimension_type`、`precision`、`rounding_mode` 定义单位维度和数值规则。 | 唯一索引保护 UOM code；`dimension_type + disabled` 支持按维度筛选可用单位。 | 聚合创建后保留；停用表示不能被新主数据或新单据引用。 |
| `uom_conversions` | business | 单位换算规则，包含生效日、换算因子、偏移量、精度和舍入规则。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + from_uom_code + to_uom_code + effective_from` 是业务唯一键。 | 唯一索引防止同一生效日起重复换算；`from_uom_code + to_uom_code` 支持快速查转换路径。 | 创建后保留；新生效规则通过新记录表达，历史换算规则不物理删除。 |
| `sites` | business | 工厂或站点主数据，是工业资源层级根。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code` 是业务唯一键；`timezone` 用于本地日历解释。 | 唯一索引保护 site code；`disabled` 支持快速扫描活跃站点。 | 聚合创建后保留；停用表示不再给新资源或计划使用。 |
| `production_lines` | business | 产线主数据，归属于 site/plant。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code` 是业务唯一键；`site_code` 为上级站点业务引用。 | 唯一索引保护 line code；`site_code + disabled` 支持按站点列活跃产线。 | 聚合创建后保留；停用表示不再接收新计划或执行引用。 |
| `shifts` | business | 班次主数据，用于日历、班组、排班、计划和执行。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code` 是业务唯一键；`starts_at`、`ends_at`、`crosses_midnight`、`paid_minutes` 描述本地班次窗口。 | 唯一索引保护 shift code；`disabled` 支持快速扫描活跃班次。 | 聚合创建后保留；停用表示不再给新日历或班组使用。 |
| `reference_data_codes` | business | 跨域引用代码表，例如物料形态、储存条件、资产类别、危险类别、质量特性定义或工艺参数定义。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code_set + code` 是业务唯一键。 | 唯一索引保护同一 code set 内 code 不重复；`code_set + disabled` 支持按代码集查询可用代码。 | 聚合创建后保留；语义变化应新建或停用旧 code，避免静默改变历史解释。 |
| `work_centers` | business | 工作中心和资源主数据，用于产能计划、工艺路线、流程设备选择和执行路由。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code` 是业务唯一键；`resource_type`、`plant_code`、`line_code`、`default_calendar_code`、`capacity_unit` 和 `finite_capacity` 描述资源层级和计划能力。 | 唯一索引保护工作中心 code；`disabled` 支持快速扫描活跃工作中心。 | 聚合创建后保留；停用表示不再接收计划或执行任务。 |
| `work_calendars` | business | 工作日历聚合根，定义可用于工作中心或计划的日历代码。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code` 是业务唯一键。 | 唯一索引保护日历 code；`disabled` 支持快速扫描活跃日历。 | 聚合创建后保留；停用表示日历不再分配给新计划。 |
| `work_calendar_working_times` | business | 工作日历拥有的周期性工作时间窗口。 | `id` 为 owned row Guid；`work_calendar_id` 指向 `work_calendars`；`day_of_week + starts_at + ends_at` 表示本地工作窗口。 | `work_calendar_id` 支持按日历加载所有工作时间；随聚合级联维护。 | owned collection，生命周期完全跟随 `work_calendars` 聚合。 |
| `device_assets` | business | 设备资产主数据，记录设备型号、产线、工作中心归属、资产类别、静态容量、关键等级和可维护/可遥测标记。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code` 是业务唯一键；`line_code` 与 `work_center_code` 为跨聚合业务引用；`minimum_capacity`、`maximum_capacity` 与 `capacity_uom_code` 记录流程设备静态能力。 | 唯一索引保护设备 code；`work_center_code + disabled` 支持按工作中心列活跃设备。 | 聚合创建后保留；退役或不可用使用 `disabled` 停用；PLC/DCS/SCADA 密钥、tag、报警和状态快照不进入本表。 |
| `cap_published_messages` | system | CAP published message outbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部索引用于投递扫描和过期清理。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `cap_received_messages` | system | CAP received message inbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部索引用于消费幂等、分组扫描和过期清理。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `cap_locks` | system | CAP distributed lock table，由 netcorepal/CAP 基础设施维护。 | 主键 `Key`。 | 主键用于 CAP 内部协调。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `__EFMigrationsHistory` | system | EF Core migration history table，记录 BusinessMasterData 已应用迁移。 | `MigrationId + ProductVersion` 由 EF Core 维护。 | EF Core 用于判定待应用迁移。 | 必须位于 `business_masterdata` schema；业务代码不直接读写。 |

Known gaps:

1. CAP system tables 当前只在 catalog 中标记 system-owned，后续可补 table comment 便于数据库工具展示。

## BusinessQuality Schema

Schema: `quality`

Owner: `backend/services/Business/Quality`

Source:

1. `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/ApplicationDbContext.cs`
2. `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/EntityConfigurations/NonconformanceReportEntityTypeConfiguration.cs`
3. `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/Migrations/20260522092605_InitialQualityNcrSchema.cs`

| Table | Kind | Purpose | Key columns | Index intent | Lifecycle |
| --- | --- | --- | --- | --- | --- |
| `nonconformance_reports` | business | Quality 拥有的不合格品报告，记录来源、不良数量、处置方案、审批链和关闭所需外部执行引用。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + ncr_code` 是业务唯一键；`source_type + source_document_id` 保留来源检验、报工或退货单据引用；`rework_work_order_id`、`scrap_movement_id`、`return_document_id` 只记录下游执行结果 ID。 | 唯一索引保护 NCR 编号；`organization_id + environment_id + status` 支持待处置列表；来源索引用于从检验/报工/退货追踪 NCR。 | 创建后从 `open` 进入 `disposition-in-progress`，关闭为 `closed`；关闭后不再修改处置；Quality 不直接改 MES/Inventory/ERP/WMS 数据。 |
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
| `cap_published_messages` | system | CAP published message outbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部索引用于投递扫描和过期清理。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `cap_received_messages` | system | CAP received message inbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部索引用于消费幂等、分组扫描和过期清理。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `cap_locks` | system | CAP distributed lock table，由 netcorepal/CAP 基础设施维护。 | 主键 `Key`。 | 主键用于 CAP 内部协调。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `__EFMigrationsHistory` | system | EF Core migration history table，记录 BusinessProductEngineering 已应用迁移。 | `MigrationId + ProductVersion` 由 EF Core 维护。 | EF Core 用于判定待应用迁移。 | 必须位于 `product_engineering` schema；业务代码不直接读写。 |

Known gaps:

1. CAP system tables 当前只在 catalog 中标记 system-owned，后续可补 table comment 便于数据库工具展示。

## AppHub Schema

Schema: `apphub`

Owner: `backend/services/AppHub`

Source:

1. `backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/ApplicationDbContext.cs`
2. `backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/EntityConfigurations/*.cs`
3. `backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/Migrations/20260517055301_InitialCreate.cs`
4. `backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/Migrations/20260517074353_SchemaGovernanceMetadata.cs`

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

| Table | Kind | Purpose | Key relationships and indexes |
| --- | --- | --- | --- |
| `operation_tasks` | business | 运维操作任务聚合根，记录目标实例、操作码、请求人、幂等范围、参数和当前状态。 | `Id` 为业务生成 string 强类型 ID；`IdempotencyScope` 唯一；`OrganizationId + EnvironmentId + Status + RequestedAtUtc` 支持任务列表和状态扫描。 |
| `operation_attempts` | business | 操作任务执行尝试，记录 connector host 领取、开始、完成和失败原因。 | `OperationTaskId` 指向 `operation_tasks`；索引用于按任务查执行历史。 |
| `audit_records` | business | 操作任务审计记录，记录动作、操作者、发生时间和 correlation id。 | `OperationTaskId + OccurredAtUtc` 支持按任务时间线展示审计。 |
| `cap_published_messages` | system | CAP published message outbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义；业务代码不直接读写。 |
| `cap_received_messages` | system | CAP received message inbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义；用于消费幂等和重试。 |
| `cap_locks` | system | CAP distributed lock table，由 netcorepal/CAP 基础设施维护。 | 主键 `Key`；用于 CAP 内部协调。 |
| `__EFMigrationsHistory` | system | EF Core migration history table，记录 Ops 已应用迁移。 | 必须位于 `ops` schema；业务代码不直接读写。 |

Status value sources:

1. `operation_tasks.Status` 当前由 `OperationTask` 聚合行为维护：`queued`、`dispatched`、`completed`、`failed`。
2. `operation_attempts.Status` 当前由 `OperationAttempt` 维护：`started`、`completed`、`failed`。
3. Connector Protocol 的 `OperationResult.ExecutionStatus=succeeded` 映射为 Ops task/attempt 的 `completed`；其它失败结果映射为 `failed`。

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
| `users` | business | 后台用户认证事实，记录 login name、email、password hash、启用状态、security stamp、permission version、登录时间和失败计数。 | `LoginName` 唯一；`Email` 唯一；`Id` 为调用方提供的有界 string 强类型 ID。 |
| `roles` | business | IAM 角色事实，用于把权限码分组后授予 membership。 | `RoleName` 唯一；拥有 `role_permissions`。 |
| `role_permissions` | business | 角色拥有的权限码集合。 | `RoleId` 指向 `roles`；`RoleId + PermissionCode` 唯一。 |
| `memberships` | business | 用户在 organization/environment scope 内的成员身份。 | `UserId + OrganizationId + EnvironmentId` 唯一；拥有 `membership_roles`。 |
| `membership_roles` | business | membership 绑定的角色集合。 | `MembershipId` 指向 `memberships`；`MembershipId + RoleId` 唯一。 |
| `user_sessions` | business | 用户 refresh session，保存 refresh token hash、issue/expiry/revoke 时间、permission version、client info 和 IP。 | `RefreshTokenHash` 支持 refresh lookup；`UserId + RevokedAtUtc` 支持按用户扫描活动/撤销会话。 |
| `connector_host_credentials` | business | Connector Host 机器身份凭据，记录 connector host id、organization/environment、secret hash 和有效期。 | `ConnectorHostId` 唯一；拥有 `connector_host_credential_capabilities`。 |
| `connector_host_credential_capabilities` | business | Connector Host credential 被授予的能力码集合。 | `ConnectorHostCredentialId` 指向 `connector_host_credentials`；`ConnectorHostCredentialId + CapabilityCode` 唯一。 |
| `seed_manifests` | business | IAM seed 执行清单，用于记录初始 admin、platform admin role、seed permissions、membership 和 local Connector Host credential seed 的版本化幂等执行。 | `SeedName + SeedVersion` 唯一；记录 owner service 与 applied time。 |
| `__EFMigrationsHistory` | system | EF Core migration history table，记录 IAM 已应用迁移。 | 必须位于 `iam` schema；业务代码不直接读写。 |

Known gaps:

1. Gateway-wide permission enforcement 已覆盖现有 Console API；Gateway 转发 bearer token 与 permission/context 到 IAM internal authorization check endpoint，不直接读取 IAM schema。
2. 用户/角色写管理端点在本阶段尚未产品化；PostgreSQL profile 下相关 write endpoints 会先执行 IAM permission 检查，授权通过后返回 501。
3. 客户发布 seed input 与 migration bundle 仍属于后续 release work。

## FileStorage Schema

Schema: `filestorage`

Owner: `backend/services/FileStorage`

Source:

1. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/ApplicationDbContext.cs`
2. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/FileStoragePersistenceServiceCollectionExtensions.cs`
3. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/EntityConfigurations/*.cs`
4. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/Migrations/20260521061426_InitialFileStorageSchema.cs`

| Table | Kind | Purpose | Key relationships and indexes |
| --- | --- | --- | --- |
| `stored_files` | business | FileStorage 已完成文件的公开元数据与内部对象定位事实。 | `file_id` 为业务生成 string ID；`object_key` 唯一且仅限内部持久化；`organization_id + environment_id + owner_service + owner_type + owner_id` 支持按业务 owner 查询。 |
| `upload_sessions` | business | 上传会话元数据，记录预留 fileId、调用方上下文、provider、过期时间和完成状态。 | `upload_session_id` 为业务生成 string ID；`file_id` 唯一；`object_key` 唯一；`organization_id + environment_id + expires_at_utc` 支持过期会话扫描。 |
| `download_grants` | business | 短期下载授权元数据，当前用于平台控制下载路径；tus provider 下可映射到本地 tus 字节内容。 | `download_grant_id` 为业务生成 string ID；`file_id` 指向 `stored_files`；`organization_id + environment_id + file_id + expires_at_utc` 支持授权校验和清理。 |
| `__EFMigrationsHistory` | system | EF Core migration history table，记录 FileStorage 已应用迁移。 | 必须位于 `filestorage` schema；业务代码不直接读写。 |

Known gaps:

1. 默认运行路径仍可使用 in-memory store 和 `server-proxy` metadata stub；设置 `Persistence:Provider=PostgreSQL` 后可启用 PostgreSQL-backed FileStorage service，客户 release bundle 仍待后续。
2. 设置 `FileStorage:UploadProvider=tus` 后已具备本地 tus `HEAD`/`PATCH` offset 传输和 download grant content 读取能力；size/checksum 强校验、过期清理任务和更完整 tus 兼容性仍属于 hardening。
3. tus 端点当前按平台内部服务边界实现为 `AllowAnonymous`，生产入口需要由 Gateway/auth 层保护；MinIO/S3 multipart 不进入 MVP，放到后续对象存储部署联调。`object_key` 不得被提升为公开 API、SDK DTO、Gateway facade 或 Console generated client 字段。

## 后续服务建表前清单

新服务进入建表阶段前，必须先补充本节对应条目，不能等迁移生成后再回忆设计意图。

| Service | Expected schema | Catalog status | Implemented | Validated | Release-supported | Required before first migration |
| --- | --- | --- | --- | --- | --- | --- |
| IAM | `iam` | Implemented | Yes | Yes | No | 已有 PostgreSQL `iam` schema、初始 migration、schema convention tests、idempotent seed、登录/refresh/logout/`/me` 和 Connector Host credential validation；客户 release bundle 仍待后续。 |
| FileStorage | `filestorage` | Implemented baseline | Yes | Yes | No | 已有 `stored_files`、`upload_sessions`、`download_grants` 初始 migration、schema convention tests、PostgreSQL-backed API service、server-proxy metadata API 和本地 tus MVP 传输能力；客户 release bundle 仍待后续；MinIO/S3 multipart 为 post-MVP 部署联调项。 |
| Notification | `notification` | Planned only | No | No | No | 通知模板、投递任务、收件人、渠道、重试和用户可见状态。 |
| Knowledge | `knowledge` | Planned only | No | No | No | 知识源、文档、分片、索引状态、向量/全文索引边界和重建策略；关系库保存索引元数据，外部向量库保存可重建索引。 |
| AI Integration | `ai` or `ai_integration` | Planned only | No | No | No | 模型/provider 配置、工具授权、调用审计、配额周期、prompt/version 归档、审批挂点和敏感信息边界。 |
| Observability indexes | `observability` | Baseline only | No | No | No | 见 `docs/architecture/observability-baseline.md`；建表前补 LogChunk、LogEntryIndex、归档任务、retention 和 Gateway 查询边界。 |

## 下一轮 hardening 建议

1. 生成或维护简版 ER 图，以 AppHub/Ops/IAM 当前 catalog 和数据库注释为输入。
2. 在新增 Notification、Knowledge、AI Integration 或 Observability 索引迁移前，先补该服务的 catalog 草案，再写实体配置、schema convention tests 和 migration；FileStorage 后续新增表时继续按本 catalog 和 schema convention tests 更新。
3. 后续如 CAP system tables 需要进入客户数据字典展示，补充 system table comment 或保持 catalog 的 system-owned 标记为权威说明。
