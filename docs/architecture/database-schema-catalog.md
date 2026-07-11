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
9. `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/Migrations/20260612073659_AddCodingTables.cs`
10. `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/Migrations/20260612074516_AddCodeRules.cs`
11. `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/Migrations/20260613064323_AddCodeRuleVersions.cs`
12. `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/Migrations/20260614032310_AddProductCategoryAndSkillCatalogs.cs`
13. `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/Migrations/20260616013831_CloseBusinessMasterData407Gaps.cs`
14. `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/Migrations/20260622021648_AddBusinessPartnerCreditLimit.cs`

| Table | Kind | Purpose | Key columns | Index intent | Lifecycle |
| --- | --- | --- | --- | --- | --- |
| `skus` | business | 物料和产品 SKU 主数据，用于计划、库存、质量、执行和流程型制造识别。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code` 是业务唯一键；`base_uom_code`、`inventory_uom_code`、`purchase_uom_code`、`sales_uom_code`、`manufacturing_uom_code` 记录单位口径；`procurement_type`、`mrp_type`、`lot_sizing_policy`、`minimum_lot_size`、`maximum_lot_size`、`lot_size_multiple`、`safety_stock_quantity`、`reorder_point_quantity`、`planned_delivery_time_days`、`in_house_production_time_days`、`goods_receipt_processing_time_days` 和 `abc_class` 记录跨域共享计划默认值；`lifecycle_status`、`purchasing_enabled`、`manufacturing_enabled`、`sales_enabled`、`batch_tracking_policy`、`serial_tracking_policy`、`shelf_life_policy_code`、`shelf_life_days`、`near_expiry_threshold_days`、`storage_condition_code` 和 `quality_required` 记录跨域追溯、效期、生命周期与用途闸门。 | 唯一索引防止同一组织/环境内 SKU code 重复；`category + disabled` 支持按品类过滤可用 SKU 列表。 | 聚合创建后保留审计时间；停用通过 `disabled` 软关闭，不物理删除；`shelf_life_days` 和 `near_expiry_threshold_days` 是 Inventory 批次效期推导与近效期预警的 MasterData 口径，`lifecycle_status` 和用途闸门控制新业务使用，已发生单据通过引用快照保持历史可读。 |
| `business_partners` | business | 供应商、客户、承运商等业务伙伴主数据。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code` 是业务唯一键；`partner_type` 是兼容主角色；`partner_roles` 保存 supplier/customer/carrier 等多角色；`tax_id` 在组织/环境内 active 记录可选唯一；`tax_region_code`、`default_currency_code`、`payment_terms_code`、`primary_address` 和 primary contact 字段保存跨域单据默认值；`credit_limit` 与 `credit_currency_code` 保存客户信用额度，供 ERP 销售订单强制信用检查读取。 | code 唯一索引用于同一伙伴跨多角色复用；`organization_id + environment_id + tax_id` active-only 部分唯一索引防止启用伙伴税号重复，同时允许停用后复用；`partner_type + disabled` 支持按主角色列活跃记录。 | 聚合创建后保留；伙伴退出使用 `disabled` 停用并释放 taxId active 唯一占用；多角色、商业默认值、联系人和信用额度调整通过 update 生命周期表达；缺失客户信用额度由 ERP 销售订单创建按阻断策略处理。 |
| `departments` | business | 组织部门主数据，用于归属、人员和组织层级。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code` 是业务唯一键；`parent_department_code` 表示上级部门 code。 | 唯一索引保护部门 code；`parent_department_code + disabled` 支持部门树和活跃子部门列表。 | 聚合创建后保留；组织调整通过停用旧部门并创建/维护新部门表达。 |
| `teams` | business | 班组主数据，记录所属部门和默认班次。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code` 是业务唯一键；`department_code` 与 `shift_code` 为跨聚合业务引用。 | 唯一索引保护班组 code；`department_code + disabled` 支持按部门列活跃班组。 | 聚合创建后保留；停用表示班组不再参与排产和执行。 |
| `personnel_skills` | business | 人员技能授予事实，包含有效期和技能等级。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + user_id + skill_code + effective_from` 是业务唯一键；`effective_from/effective_to` 为日期有效期。 | 唯一索引防止同一人员同一技能起始日重复；`user_id + disabled` 支持查人员技能；`skill_code + disabled` 支持按技能查人员。 | 创建后作为技能有效期事实保留；失效或撤销使用 `disabled`，历史有效期不物理删除。 |
| `product_categories` | business | 层级产品/物料分类目录，用于 Business Console 产品分类树和后续 SKU 分类治理；当前 SKU `category` 字段仍兼容 `product-category` CodeSet 校验。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + category_code` 是业务唯一键；`parent_code` 保存同组织/环境内父分类 code；`description` 为展示说明；`disabled` 为停用标记。 | 唯一索引保护分类 code；`organization_id + environment_id + parent_code + disabled` 支持分类树查询；`disabled` 支持活跃目录扫描。 | 聚合创建后保留；归档使用 `disabled` 软停用；层级调整通过 update 改写 `parent_code`，不建立跨表 FK。 |
| `skills` | business | 技能/工种目录，定义技能名称、分组、是否需要证书及证书有效月数；人员技能授予仍由 `personnel_skills` 记录用户与技能的有效期事实。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + skill_code` 是业务唯一键；`group_name`、`requires_certification`、`validity_months` 和 `description` 描述技能目录属性；`disabled` 为停用标记。 | 唯一索引保护技能 code；`organization_id + environment_id + group_name + disabled` 支持分组目录查询；`disabled` 支持活跃目录扫描。 | 聚合创建后保留；归档使用 `disabled` 软停用；已登记人员技能保留历史 code 引用。 |
| `units_of_measure` | business | 计量单位主数据，用于 SKU、BOM/配方、库存、质检、采购销售、报工和遥测数值口径。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code` 是业务唯一键；`dimension_type`、`precision`、`rounding_mode` 定义单位维度和数值规则。 | 唯一索引保护 UOM code；`dimension_type + disabled` 支持按维度筛选可用单位。 | 聚合创建后保留；停用表示不能被新主数据或新单据引用。 |
| `uom_conversions` | business | 单位换算规则，包含生效起止日、换算因子、偏移量、精度、舍入规则和停用状态。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + from_uom_code + to_uom_code + effective_from` 是业务唯一键；`effective_to` 表示可选失效日期。 | 唯一索引防止同一生效日起重复换算；`from_uom_code + to_uom_code` 支持快速查转换路径；`disabled` 支持快速扫描活跃换算。 | 创建后保留；新生效规则通过新记录表达，过期或错误换算通过 `effective_to` 或软停用退出 active 集合。 |
| `sites` | business | 工厂或站点主数据，是工业资源层级根。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code` 是业务唯一键；`timezone` 用于本地日历解释。 | 唯一索引保护 site code；`disabled` 支持快速扫描活跃站点。 | 聚合创建后保留；停用表示不再给新资源或计划使用。 |
| `workshops` | business | 车间主数据，归属于 site，用于产线、工作中心和班组责任关系。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code` 是业务唯一键；`site_code`、`manager_user_id` 和 `description` 描述车间归属与责任人。 | 唯一索引保护 workshop code；`site_code + disabled` 支持按站点列活跃车间。 | 聚合创建后保留；停用表示不再给新产线、工作中心或班组关系使用。 |
| `production_lines` | business | 产线主数据，归属于 site/plant，可选归属 workshop。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code` 是业务唯一键；`site_code` 为上级站点业务引用；`workshop_code` 为可选车间业务引用。 | 唯一索引保护 line code；`site_code + disabled`、`workshop_code + disabled` 支持按站点或车间列活跃产线。 | 聚合创建后保留；停用表示不再接收新计划或执行引用。 |
| `shifts` | business | 班次主数据，用于日历、班组、排班、计划和执行。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code` 是业务唯一键；`starts_at`、`ends_at`、`crosses_midnight`、`paid_minutes` 和 `break_minutes` 描述本地班次窗口和休息扣减。 | 唯一索引保护 shift code；`disabled` 支持快速扫描活跃班次。 | 聚合创建后保留；停用表示不再给新日历或班组使用。 |
| `reference_data_codes` | business | 跨域引用代码表，CodeSet 以 `docs/architecture/master-data-dictionary-rules.md` 的保留表为准，例如物料类型、储存条件、质量原因和合规标签。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code_set + code` 是业务唯一键。 | 唯一索引保护同一 code set 内 code 不重复；`code_set + disabled` 支持按代码集查询可用代码。 | 聚合创建后保留；系统枚举禁止新增非标准码或改名，平台预置/工厂自定义分组按治理规则新增，语义变化应停用旧 code。 |
| `team_members` | business | 班组成员关系，连接 MasterData team 与 IAM/user id。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + team_code + user_id` 在 `disabled = false` 时是当前成员唯一键；`effective_from/effective_to` 保存历史有效期。 | 部分唯一索引防止同一人员在同一班组存在多个 active 关系，同时允许移除后保留历史并重新加入；`team_code + disabled` 支持列班组成员；`user_id + disabled` 支持按人员反查班组。 | 创建后保留历史；移除成员使用 `disabled` 标记，不物理删除。 |
| `work_centers` | business | 工作中心和资源主数据，用于产能计划、工艺路线、流程设备选择和执行路由。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code` 是业务唯一键；`resource_type`、`plant_code`、`line_code`、`workshop_code`、`default_calendar_code`、`capacity_unit`、`capacity_minutes_per_day`、`utilization_rate`、`efficiency_rate`、`number_of_capacities`、`finite_capacity`、`bottleneck` 和 `cost_center_code` 描述资源层级、额定产能和成本归属。 | 唯一索引保护工作中心 code；`disabled`、`workshop_code + disabled` 支持快速扫描活跃工作中心和按车间过滤。 | 聚合创建后保留；停用表示不再接收计划或执行任务。 |
| `work_calendars` | business | 工作日历聚合根，定义可用于工作中心或计划的日历代码。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code` 是业务唯一键；`timezone`、`effective_from`、`effective_to` 和 `holiday_calendar_code` 描述日历解释口径与有效期。 | 唯一索引保护日历 code；`disabled` 支持快速扫描活跃日历。 | 聚合创建后保留；停用表示日历不再分配给新计划；有效期用于计划快照选择。 |
| `work_calendar_working_times` | business | 工作日历拥有的周期性工作日标记。 | `id` 为 owned row Guid；`work_calendar_id` 指向 `work_calendars`；`day_of_week` 表示本地周期性工作日，具体工作时间由 Shift 表达。 | `work_calendar_id` 支持按日历加载所有工作日；`work_calendar_id + day_of_week` 唯一约束防止重复工作日；随聚合级联维护。 | owned collection，生命周期完全跟随 `work_calendars` 聚合。 |
| `work_calendar_holidays` | business | 工作日历拥有的节假日日期。 | `id` 为 owned row Guid；`work_calendar_id` 指向 `work_calendars`；`date + name` 表示本地节假日。 | `work_calendar_id` 支持按日历加载所有节假日；随聚合级联维护。 | owned collection，生命周期完全跟随 `work_calendars` 聚合。 |
| `work_calendar_exceptions` | business | 工作日历拥有的例外日，可覆盖某天是否工作及可选工作时间窗口。 | `id` 为 owned row Guid；`work_calendar_id` 指向 `work_calendars`；`date` 表示本地例外日期，`is_working_day` 和可选 `starts_at/ends_at` 表示覆盖规则。 | `work_calendar_id` 支持按日历加载所有例外日；随聚合级联维护。 | owned collection，生命周期完全跟随 `work_calendars` 聚合。 |
| `device_assets` | business | 设备资产主数据，记录设备型号、产线、工作中心归属、资产类别、静态容量、关键等级和可维护/可遥测标记。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + code` 是业务唯一键；`line_code` 与 `work_center_code` 为跨聚合业务引用；`minimum_capacity`、`maximum_capacity` 与 `capacity_uom_code` 记录流程设备静态能力。 | 唯一索引保护设备 code；`work_center_code + disabled` 支持按工作中心列活跃设备。 | 聚合创建后保留；退役或不可用使用 `disabled` 停用；PLC/DCS/SCADA 密钥、tag、报警和状态快照不进入本表。 |
| `code_rules` | business | MasterData 拥有的当前生效编码规则配置，保存可被 Coding engine 消费的 rule key、适用资源、scope 和 segments JSON。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + rule_key` 是业务唯一键；`segments` 为 `jsonb`，保存 constant/date/field/sequence/checksum 等片段定义。 | 唯一索引保护同一组织/环境内同一 rule key 只有一条当前定义；种子服务从 `StandardCodeRules` 幂等 upsert。 | 规则作为当前配置事实保留；立即生效的版本化配置会推进本表，历史与计划版本保存在 `code_rule_versions`。 |
| `code_rule_versions` | business | 编码规则配置版本审计表，记录运营配置的版本号、生效边界、状态、创建人和变更原因。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + rule_key + version` 是版本唯一键；`effective_from_utc` 定义新规则对后续编码的生效边界；`segments` 为 `jsonb`。 | 唯一索引保护同一规则版本不重复；`organization_id + environment_id + rule_key + effective_from_utc` 索引用于按规则查计划/历史版本。 | seed 为标准规则写入 version 1 审计；运营配置新增下一版本，立即生效版本同步推进 `code_rules`，计划版本先保留为 scheduled，并由 MasterData 后台任务到期晋升；同一规则只保留当前定义版本为 active，旧生效版本转 superseded。 |
| `code_counters` | business | MasterData service-local 编码计数器，用于 SKU、UOM、伙伴、组织、资源和设备等普通创建请求自动分配业务 code。 | `organization_id + environment_id + rule_key + site_code + reset_key` 是计数器唯一范围；`current_value` 和 `version` 维护已分配序列与乐观并发。 | 唯一索引保护同一编码规则范围只有一个 counter。 | 创建请求按需创建并递增；不由用户直接维护。 |
| `code_idempotency_keys` | business | MasterData 创建请求幂等键记录，把客户端 key 绑定到已分配业务 code 和 payload fingerprint。 | `organization_id + environment_id + rule_key + idempotency_key` 是唯一键；`code` 保存首次分配结果。 | 唯一索引阻止同一 key 在同一规则内重复创建不同资源。 | 随创建请求写入，保留用于重试去重和冲突诊断。 |
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
5. `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/EntityConfigurations/InspectionTaskEntityTypeConfiguration.cs`
6. `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/EntityConfigurations/QualityReasonEntityTypeConfiguration.cs`
7. `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/Coding/CodeEntityTypeConfigurations.cs`
8. `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/Migrations/20260522092605_InitialQualityNcrSchema.cs`
9. `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/Migrations/20260523034736_AddQualityInspectionFacts.cs`
10. `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/Migrations/20260614032327_AddQualityReasonCatalog.cs`
11. `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/Migrations/20260615020533_AddQualityCodingTables.cs`
12. `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/Migrations/20260616013940_AddQualityBusinessGap415.cs`
13. `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/Migrations/20260619051226_AddQualityDefectConsumerReliability.cs`
14. `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/Migrations/20260626170759_AddQualityNcrInventoryDispositionRouting.cs`
15. `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/Migrations/20260629074947_AddQualityLongtailReviewFixes.cs`
16. `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/Migrations/20260705071335_AddQualityInspectionTasks.cs`
17. `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/Migrations/20260707020004_AddQualityCapaAutomationAndVerification.cs`
18. `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/EntityConfigurations/SpcControlChartEntityTypeConfiguration.cs`
19. `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/Migrations/20260707120127_AddQualitySpcControlCharts.cs`

| Table | Kind | Purpose | Key columns | Index intent | Lifecycle |
| --- | --- | --- | --- | --- | --- |
| `nonconformance_reports` | business | Quality 拥有的不合格品报告，记录来源、不良数量、处置方案、审批链和关闭所需外部执行引用。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + ncr_code` 是业务唯一键；`source_type + source_document_id` 保留来源检验、报工或退货单据引用；`source_inspection_record_id` 可关联打开 NCR 的检验记录；`disposition_approval_chain_id` 记录外部 BusinessApproval chain id；`uom_code`、`site_code`、`location_code`、`owner_type`、`owner_id` 是从来源检验复制的库存定位快照，用于 NCR 处置时向 Inventory 发起 blocked 库存释放或报废核销；`rework_work_order_id`、`scrap_movement_id`、`return_document_id` 只记录下游执行结果 ID。 | 唯一索引保护 NCR 编号；`organization_id + environment_id + status` 支持待处置列表；来源索引用于从检验/报工/退货追踪 NCR；`source_inspection_record_id` 索引用于从检验记录反查 NCR；`ux_ncr_mes_defect_source` 只约束当前 MES defect consumer 自动创建的 `in-process` + `MES-SKU-UNRESOLVED` source，在同一组织/环境内只能登记一个 NCR，避免误约束 Quality inspection 产生的 `in-process` NCR。 | 创建后从 `open` 进入 `disposition-in-progress`，关闭为 `closed`；关闭后不再修改处置；`rework`、`scrap`、`return-to-supplier`、`conditional-release` 必须先记录 MRB 评审并引用已批准且绑定同一 NCR 的 BusinessApproval chain（source service 为 Quality，document type 为 NCR disposition，document id 为 `ncr_code`）；`sort-and-screen` 保持低风险处置，不要求 MRB 或中央审批；Quality 通过公共集成事件请求 Inventory 处理 NCR 库存处置，不直接写 Inventory schema。 |
| `ncr_mrb_reviews` | business | NCR 材料评审委员会记录，保留处置提交前的评审人、结论、意见和评审时间。 | `id` 为 Guid v7 强类型 ID；`nonconformance_report_id` 归属 NCR；`reviewer_id` 是 IAM/user 或委员会成员公共引用；`decision` 保存评审结论。 | `nonconformance_report_id + reviewer_id` 支持按 NCR 读取评审记录并定位评审人。 | 随 NCR disposition 写入并级联保存；历史处置事件会携带评审快照。 |
| `corrective_actions` | business | CAPA/8D 纠正预防措施生命周期，记录根因、围堵、责任人、有效性验证、验证检验和关闭审批事实。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + capa_code` 是业务唯一键；`source_ncr_id` 可关联触发 CAPA 的 NCR；`root_cause`、`containment_action`、`owner_user_id`、`due_at_utc` 和 `status` 描述 CAPA 主体；`effectiveness_inspection_record_id` 引用通过的 Quality 验证检验；`close_approval_chain_id` 记录可选 BusinessApproval 关闭审批链。 | 唯一索引保护 CAPA 编号；`organization_id + environment_id + status` 支持 CAPA 队列；`source_ncr_id` 支持从 NCR 追踪 CAPA；`ix_corrective_actions_effectiveness_inspection` 和 `ix_corrective_actions_close_approval_chain` 支持从验证检验或审批链反查 CAPA。 | 从 `open` 开始，记录纠正/预防行动后才能通过已合格的验证检验完成有效性验证；关闭可按配置要求 BusinessApproval 审批，关闭后不再修改；高严重度 rework/scrap NCR 处置可按配置自动创建关联 CAPA。 |
| `corrective_action_items` | business | CAPA 围堵、纠正和预防行动条目。 | `id` 为 Guid v7 强类型 ID；`corrective_action_id` 归属 CAPA；`action_type` 为 containment/corrective/preventive；`owner_user_id` 和 `due_at_utc` 记录责任和期限；`completed_by_user_id` 与 `completed_at_utc` 记录行动项完成审计。 | `corrective_action_id + action_type` 支持按 CAPA 和行动类型读取。 | 随 CAPA 创建和级联保存；CAPA 关闭后不再新增行动项；有效性验证前必须完成所有行动项。 |
| `quality_reasons` | business | 质量原因/不良代码目录，按分组维护原因码、严重度和默认 NCR 处置建议。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + reason_code` 是业务唯一键；`group_name`、`severity` 和 `default_disposition` 描述原因分类和处置默认值；`enabled` 控制是否可选。 | 唯一索引保护原因 code；`organization_id + environment_id + group_name + enabled` 支持按分组列可用原因；`enabled` 支持活跃目录扫描。 | 聚合创建后保留；归档使用 `enabled=false` 软停用；历史检验/NCR 继续保留原因快照或 code 引用。 |
| `code_counters` | business | Quality service-local 编码计数器，用于质量原因等普通创建请求自动分配业务 code。 | `organization_id + environment_id + rule_key + site_code + reset_key` 是计数器唯一范围；`current_value` 和 `version` 维护已分配序列与乐观并发。 | 唯一索引保护同一编码规则范围只有一个 counter。 | 创建请求按需创建并递增；不由用户直接维护。 |
| `code_idempotency_keys` | business | Quality 创建请求幂等键记录，把客户端 key 绑定到已分配业务 code 和 payload fingerprint。 | `organization_id + environment_id + rule_key + idempotency_key` 是唯一键；`code` 保存首次分配结果。 | 唯一索引阻止同一 key 在同一规则内重复创建不同资源。 | 随创建请求写入，保留用于重试去重和冲突诊断。 |
| `inspection_plans` | business | Quality inspection plan 版本和适用性事实，定义收货、工序、终检、维修或客退场景的检验要求。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + plan_code` 是业务唯一键；`category`、`sku_code`、`partner_id`、`work_center_id`、`device_asset_id` 和 `document_type` 描述适用范围；`supersedes_plan_id` 指向上一版本。 | 唯一索引保护计划编码；`organization_id + environment_id + category + status` 支持按类别和状态查询；`organization_id + environment_id + status` 支持仅按状态过滤。 | 从 `draft` 创建，激活后进入 `active`；新版本会把旧版本标记为 `superseded`；激活后不再修改执行特性。 |
| `inspection_plan_characteristics` | business | 检验计划特性、规格公差和 AQL 抽样规则，记录每个计划版本要检查的项目。 | `id` 为 Guid v7 强类型 ID；`inspection_plan_id` 归属计划；`characteristic_code` 是计划内稳定特性编码；`characteristic_type` 为 variable/attribute；`nominal_value`、`lower_spec_limit`、`upper_spec_limit`、`unit_code` 描述计量规格；`sampling_inspection_level`、`sampling_aql`、`sampling_sample_size`、`sampling_acceptance_number`、`sampling_rejection_number` 描述 AQL 抽样。 | `inspection_plan_id + characteristic_code` 唯一，防止同一计划版本内重复特性编码。 | 随所属检验计划创建和级联删除；计划激活后不再新增或变更执行特性；旧特性默认按 attribute 兼容。 |
| `inspection_tasks` | business | Quality 待检任务事实，由 ERP/WMS 收货、MES 工序完成和完工入库请求事件按激活的检验计划自动生成。 | `id` 为 Guid v7 强类型 ID；`inspection_plan_id` 记录命中的计划；`inspection_record_id` 在任务完成后引用生成的检验记录；`source_type + source_service + source_document_id + source_document_line_id + sku_code` 保留来源单据/行/工序上下文；`quantity`、`uom_code`、`batch_no`、`serial_no` 保留待检实物上下文；`status` 为 `pending`、`in-progress` 或 `completed`；`due_at_utc` 和 `overdue_reminder_sent_at_utc` 支撑超期提醒。 | `organization_id + environment_id + status + due_at_utc` 支持待检工作台和超期扫描；`organization_id + environment_id + source_type + source_service + source_document_id + source_document_line_id + sku_code` 唯一约束防止同一来源重复生成任务；`organization_id + environment_id + trigger_idempotency_key` 唯一约束支撑事件重投幂等。 | 事件生成后为 `pending`；检验员从任务创建检验记录时进入 `in-progress` 并在记录创建后变为 `completed`；超期提醒只标记首次发送时间，不关闭任务。 |
| `inspection_records` | business | 检验执行记录和最终结果事实，保留来源单据、被检 SKU、数量、批次/序列号、库存放行维度和处置引用。 | `id` 为 Guid v7 强类型 ID；`inspection_plan_id` 可选指向执行的计划版本；`source_type + source_service + source_document_id` 保留来源业务单据；`uom_code`、`site_code`、`location_code`、`source_quality_status`、`owner_type`、`owner_id` 是 Inventory 事件消费所需的可选库存状态转换维度；`result` 为 `passed`、`rejected` 或 `conditional-release`。 | `organization_id + environment_id + source_service + source_document_id` 支持按来源追踪；`organization_id + environment_id + source_type + source_service + source_document_id + sku_code` 唯一约束防止同一来源单据同一 SKU 并发重复建检验记录，同时允许一张来源单据包含多个 SKU；`organization_id + environment_id + source_type + result` 支持场景+结果查询；`organization_id + environment_id + result` 支持仅按结果过滤。 | 创建即形成最终检验结果并发出 passed/conditional-release/rejected 集成事件；计划检验由规格/AQL 自动判定；非 passed 结果必须保留处置原因；通过 `OpenNcrFromInspection` 最多关联一个 NCR。 |
| `measuring_devices` | business | Quality 量具/检具主数据及校准到期生命周期。 | `device_code` 是量具业务编码；`device_type`、`accuracy`、`calibration_interval_days` 描述量具能力；`status` 为 `in-use`、`calibration`、`disabled` 或 `retired`；全部时间为 UTC。 | `organization_id + environment_id + device_code` 唯一；`organization_id + environment_id + calibration_due_at_utc + status` 支持到期扫描和看板。 | 校准有效期内可用；临期为 warning；到期自动转 calibration；disabled/retired 不可恢复为在用。 |
| `calibration_records` | business | 已接受的量具校准审计记录。 | `measuring_device_id` 为聚合内外键；`calibration_no` 为校准凭据编号；`calibrated_at_utc`、外部校准服务方 `calibration_provider` 和可选 `certificate_file_id` 保留结果与证书引用；应用审计身份不由该行保存。 | `measuring_device_id + calibration_no` 唯一，防止重复导入同一校准记录。 | 新记录将量具恢复为 in-use 并以配置周期重算下次到期日；随量具删除。 |
| `inspection_result_lines` | business | 检验结果行测量值和缺陷事实，记录每个特性的数值实测、文本观测、结果、缺陷原因、缺陷数量和附件。 | `id` 为 Guid v7 强类型 ID；`inspection_record_id` 归属检验记录；`characteristic_code` 对应计划或临时检查项；`measured_value` 保存计量型数值；`observed_value` 保存文本观测或数值字符串；`result` 为 `passed`、`failed` 或 `conditional-release`；`defect_quantity` 表示该行缺陷数量。 | `inspection_record_id + characteristic_code` 支持按记录读取结果行和重复检查。 | 随所属检验记录创建和级联删除；计划记录的行结果由规格/AQL 计算；failed 或 conditional-release 行必须保留缺陷/让步原因。 |
| `spc_control_charts` | business | SPC Xbar-R 控制图控制限锁定事实，按 SKU、特性和工作中心保存已确认的中心线、平均极差和 Xbar/R 控制限。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + sku_code + characteristic_code + work_center_id + subgroup_size` 是控制图唯一范围；`center_line`、`average_range`、`xbar_upper_control_limit`、`xbar_lower_control_limit`、`range_upper_control_limit`、`range_lower_control_limit` 保存锁定控制限；`locked`、`limits_calculated_at_utc`、`locked_at_utc` 保留锁定状态和审计时间。 | 唯一索引防止同一组织/环境/SKU/特性/工作中心/子组大小产生多份锁定控制限；SPC 查询默认从检验实测值投影并在存在锁定记录时使用锁定控制限。 | 查询可自动计算控制限；操作员确认后写入锁定记录；后续判异和质量预警以 Quality SPC 事件表达，不复用设备报警表。 |
| `cap_published_messages` | system | CAP published message outbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部索引用于投递扫描和过期清理。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `cap_received_messages` | system | CAP received message inbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部索引用于消费幂等、分组扫描和过期清理。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `cap_locks` | system | CAP distributed lock table，由 netcorepal/CAP 基础设施维护。 | 主键 `Key`。 | 主键用于 CAP 内部协调。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `integration_event_dead_letters` | system | Quality 消费侧在业务处理前拒绝的集成事件，用于版本不兼容、envelope 缺失等场景的排查和 replay 标记。 | `id` 为 Guid；`consumer_name`、`event_id`、`event_type`、`event_version`、`source_service`、`idempotency_key`、`status` 和 `event_json` 保留拒绝事实。 | `consumer_name + status + dead_lettered_at_utc` 支撑 pending 队列查看；`consumer_name + event_id` 支撑单事件排查。 | PostgreSQL profile 下由共享 persistent DLQ store 写入；operator replay 后标记 `Replayed`，不删除原始拒绝事实。 |
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
7. `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/Migrations/20260612073638_AddCodingTables.cs`
8. `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/Migrations/20260612140150_AddEngineeringDocumentItemCodeAndReadEndpoints.cs`
9. `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/Migrations/20260613110725_AddStandardOperations.cs`
10. `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/Migrations/20260615081834_ProductEngineeringIssue408ReleaseSemantics.cs`
11. `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/Migrations/20260623071722_AddEngineeringChangeSupersedeSuccessor.cs`
12. `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/Migrations/20260707023000_AddSopDocumentDispatchFields.cs`

| Table | Kind | Purpose | Key columns | Index intent | Lifecycle |
| --- | --- | --- | --- | --- | --- |
| `engineering_documents` | business | 工程文档引用事实，登记 CAD、图纸、工艺文件以及 SOP/电子作业指导书在 File Storage 中的文件 ID、文件名、内容类型、文档类型、生命周期状态、可选关联工程物料和 SOP 下发范围。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + document_number + revision` 是文档版本业务唯一键；`file_id` 是 File Storage 文件业务引用；`item_code` 是可选工程物料业务引用；SOP 使用 `document_type = sop`，并通过 `operation_code`、可选 `work_center_code`、`routing_code`、`routing_revision` 和 `effective_date` 绑定 StandardOperation/Routing 工序范围。 | 文档号版本唯一索引防重复登记；`organization_id + environment_id + file_id + revision` 防止同一文件修订被重复绑定；`organization_id + environment_id + item_code + document_type` 支持按工程物料和文档类型筛选；`organization_id + environment_id + document_type + operation_code + work_center_code + status + effective_date` 支持 MES/PDA 按当前工序选择已发布且已生效 SOP。 | 注册后作为外部文件引用保留；SOP 通过 ECO 把旧版本归档，新生效版本成为当前展示版本；文件本体、对象存储 key 和下载授权仍由 File Storage 管理。 |
| `engineering_items` | business | 工程物料版本事实，记录工程料号、修订、名称和发布状态。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + item_code + revision` 是工程物料版本业务唯一键；`status` 表示 draft/published/archived。 | 业务唯一索引保护同一工程物料修订；`organization_id + environment_id + status` 支持按状态筛选工程物料版本。 | 可创建草稿或直接发布；发布后不可直接改名，后续变化通过新修订表达。 |
| `engineering_boms` | business | EBOM 版本聚合根，描述父工程物料和组件工程物料组成。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + bom_code + revision` 是 EBOM 版本唯一键；`parent_item_code` 为工程物料业务引用；`effective_date` 为发布时间。 | 业务唯一索引防重复发布；`parent_item_code + status` 支持按父项查看可用 EBOM。 | 草稿添加组件，发布后组件不可直接修改；下游 MBOM 引用已发布 EBOM 版本。 |
| `engineering_bom_lines` | business | EBOM 组件行，记录子工程物料、数量、单位、替代组、虚拟件、位号、损耗/得率和倒冲快照。 | owned row `id`；`engineering_bom_id` 指向 EBOM 聚合；`child_item_code`、`quantity`、`unit_of_measure_code` 描述组件；`is_phantom`、`alternate_group`、`alternate_priority`、`reference_designators`、`scrap_rate`、`yield_rate`、`backflush` 记录发布时组件语义。 | `engineering_bom_id + child_item_code` 唯一，防止同一 EBOM 内组件重复。 | owned collection，生命周期跟随 `engineering_boms`；发布后不可直接修改，后续变化通过新修订或 ECO 生效传播。 |
| `manufacturing_boms` | business | MBOM/配方版本聚合根，将生产 SKU 的制造物料清单绑定到已发布 EBOM 版本。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + bom_code + revision` 是 MBOM 版本唯一键；`sku_code` 为生产 SKU；`engineering_bom_version_id` 保存 EBOM 业务版本引用。 | 业务唯一索引防重复发布；`sku_code + status` 支持按 SKU 查可用 MBOM。 | 草稿添加物料和配方参数，发布要求引用已发布 EBOM；发布后不可直接修改。 |
| `manufacturing_bom_material_lines` | business | MBOM 物料行，记录生产 SKU 所需原辅料、数量、单位、损耗率、替代/替换料、虚拟件、位号、得率和倒冲快照。 | owned row `id`；`manufacturing_bom_id` 指向 MBOM 聚合；`sku_code`、`quantity`、`unit_of_measure_code`、`scrap_rate` 描述用料；`is_phantom`、`alternate_group`、`alternate_priority`、`substitute_sku_codes`、`reference_designators`、`yield_rate`、`backflush` 记录发布时制造语义。 | `manufacturing_bom_id + sku_code` 唯一，防止同一 MBOM 内物料重复。 | owned collection，生命周期跟随 `manufacturing_boms`；替代料编码为发布快照，不跨 schema 建外键。 |
| `manufacturing_bom_recipe_lines` | business | MBOM 配方/工艺参数行，用于流程型制造的目标参数。 | owned row `id`；`manufacturing_bom_id` 指向 MBOM 聚合；`parameter_code`、`target_value`、`unit_of_measure_code` 描述参数。 | `manufacturing_bom_id + parameter_code` 唯一，防止同一 MBOM 内参数重复。 | owned collection，生命周期跟随 `manufacturing_boms`。 |
| `routings` | business | 工艺路线版本聚合根，描述 SKU 的工作中心工序序列。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + routing_code + revision` 是路线版本唯一键；`sku_code` 为生产 SKU；`effective_date` 为发布时间。 | 业务唯一索引防重复发布；`sku_code + status` 支持按 SKU 查可用路线。 | 草稿添加工序，发布后不可直接修改；ProductionVersion 只能绑定已发布路线。 |
| `routing_operations` | business | 工艺路线工序行，记录工序顺序、工作中心、标准工序码值、工序显示名、准备/运行/收尾工时、控制码和执行标志快照。 | owned row `id`；`routing_id` 指向路线聚合；`sequence`、`work_center_code`、`operation_code`、`operation_name`、`standard_minutes` 描述工序；`setup_minutes`、`run_minutes`、`teardown_minutes`、`control_key`、`requires_reporting`、`requires_quality_inspection`、`is_outsourced` 来自发布时启用的 StandardOperation。 | `routing_id + sequence` 唯一，防止同一路线内工序顺序重复。 | owned collection，生命周期跟随 `routings`；Routing release 必须校验 StandardOperation 存在且启用，历史路线不因标准工序默认值调整而变更。 |
| `standard_operations` | business | 标准工序主数据，用于工艺路线编制时按工序预填默认工作中心、准备/加工工时、控制码和执行标志。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + operation_code` 是业务唯一键；`default_work_center_code` 是 MasterData 工作中心业务引用；`standard_setup_minutes` 与 `standard_run_minutes` 拆分默认工时；`control_key`、`requires_reporting`、`requires_quality_inspection`、`is_outsourced` 记录默认控制行为；`enabled` 表示是否可被新路线选择。 | 业务唯一索引保护同一组织/环境内工序 code；`organization_id + environment_id + enabled` 支持按可用状态列标准工序。 | 创建后作为主数据保留；更新调整后续路线编制默认值；归档使用 `enabled=false` 退出新路线选择，不改历史 routing operation 快照。 |
| `engineering_changes` | business | ECO/ECN 工程变更聚合根，记录变更号、原因、审批引用、影响范围、生效日期和发布状态。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + change_number` 是变更唯一键；`approval_reference_id` 是审批链业务引用；`effective_date` 保存即时或计划生效日期。 | 变更号唯一索引防重复；`organization_id + environment_id + status` 支持按状态查询变更，scheduled ECO 后台晋升按状态和生效日期扫描。 | 草稿记录影响范围，经审批引用确认后发布；future `effective_date` 先进入 scheduled 状态，支持取消和改期，到期后台任务归档受影响版本并发布 release 事件；发布或取消后作为工程变更事实保留。 |
| `engineering_change_affected_versions` | business | 工程变更影响版本行，记录受 ECO/ECN 影响的 EBOM、MBOM、Routing、ProductionVersion 或 EngineeringDocument/SOP 业务版本 ID，并可记录取代该版本的 successor version id。 | owned row `id`；`engineering_change_id` 指向变更聚合；`version_kind + version_id` 标识受影响对象；`superseded_by_version_id` 可选记录 old→new 取代关系；同一 ECO 内一个 affected version 只能声明一个 successor。 | `engineering_change_id + version_kind + version_id` 唯一，避免同一变更重复标注同一版本。 | owned collection，生命周期跟随 `engineering_changes`；successor id 为同服务内业务版本引用，不建立额外 FK；SOP 使用 `engineering-document` kind 归档旧文档版本。 |
| `production_versions` | business | ProductEngineering 拥有的生产版本绑定事实，将已发布 MBOM 版本和工艺路线版本绑定到 SKU、生效日期、批量区间和 MES 工单创建选择规则。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + sku_code` 定义租户环境内 SKU 范围；`mbom_version_id` 和 `routing_version_id` 为工程版本业务引用；`valid_from/valid_to`、`lot_size_min/lot_size_max`、`priority` 和 `is_default` 驱动解析选择。 | `organization_id + environment_id + sku_code + status` 支持 MES 解析活跃生产版本；`sku_code + is_default + valid_from + valid_to` 支持默认版本重叠校验；`mbom_version_id + routing_version_id` 支持按工程版本追踪生产版本。 | 创建后为 `active`；归档为 `archived` 后不再被 MES 解析；当前版本只允许绑定已发布 MBOM/route，暂不暴露锁定状态。 |
| `code_counters` | business | ProductEngineering service-local 编码计数器，用于工程文档、工程物料、BOM、routing 和 ECO 普通创建请求自动分配业务 code。 | `organization_id + environment_id + rule_key + site_code + reset_key` 是计数器唯一范围；`current_value` 和 `version` 维护已分配序列与乐观并发。 | 唯一索引保护同一编码规则范围只有一个 counter。 | 创建请求按需创建并递增；不由用户直接维护。 |
| `code_idempotency_keys` | business | ProductEngineering 创建请求幂等键记录，把客户端 key 绑定到已分配业务 code 和 payload fingerprint。 | `organization_id + environment_id + rule_key + idempotency_key` 是唯一键；`code` 保存首次分配结果。 | 唯一索引阻止同一 key 在同一规则内重复创建不同资源。 | 随创建请求写入，保留用于重试去重和冲突诊断。 |
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
6. `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Infrastructure/Migrations/20260615073828_AddInventoryStatusReservationValuation.cs`

| Table | Kind | Purpose | Key columns | Index intent | Lifecycle |
| --- | --- | --- | --- | --- | --- |
| `stock_locations` | business | Inventory 拥有的仓库、库区、库位或逻辑库存地点事实。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + location_code` 是业务唯一键；`site_code` 是 MasterData 站点业务引用。 | 唯一索引保护库存地点编码；`organization_id + environment_id + site_code + status` 支持按站点列可用库位。 | 创建后保留；状态字段表达启停，不物理删除。 |
| `stock_ledgers` | business | Inventory 拥有的当前库存余额事实，按 SKU、UOM、站点、地点、批次、序列号、生产日期、效期、受控库存状态和 owner 维度聚合。 | `id` 为 Guid v7 强类型 ID；`production_date` 和 `expiry_date` 记录批次生产/效期；`on_hand_quantity`、`reserved_quantity`、`moving_average_unit_cost`、`inventory_value`、`is_frozen_for_count`、`ledger_version` 和 `row_version` 维护余额、计价、盘点冻结和并发控制；`quality_status` 只允许 `unrestricted`、`quality`、`restricted`、`blocked`。 | 唯一索引保护同一库存维度只有一条余额并纳入生产日期/效期；SKU/site/expiry 索引用于 FEFO 与近效期查询；状态 check constraint 防自由文本库存状态。 | 由库存移动、预留/释放、状态转移和盘点调整驱动更新；FEFO 预留按 `expiry_date` 优先，过期批次默认禁止常规预留/出库；不由 WMS、MES 或 ERP 平行维护余额。 |
| `stock_movements` | business | 追加式库存移动事实，记录来源服务、来源单据、幂等键、有符号数量、批次效期和移动金额。 | `id` 为 Guid v7 强类型 ID；`source_service + source_document_id + idempotency_key` 保护外部调用幂等；`production_date`、`expiry_date` 保留移动入账时的批次效期；`quantity` 为正负移动量；`unit_cost` 和 `movement_amount` 记录移动平均计价输入/结果。 | 幂等唯一索引防重复入账；SKU/site/location 索引用于追踪库存历史；SKU/site/expiry 索引用于效期追溯；状态 check constraint 防自由文本库存状态。 | 创建后不可变；余额、预留、计价变化通过 ledger 更新体现。 |
| `stock_reservations` | business | Inventory 拥有的库存预留/分配事实，按来源服务、来源单据和库存维度保留预留数量。 | `id` 为 Guid v7 强类型 ID；`source_service + source_document_id + idempotency_key` 保护预留幂等；`production_date`、`expiry_date` 保存被预留批次效期；`reserved_quantity`、`released_quantity`、`allocated_quantity`、`open_quantity` 和 `status` 记录生命周期；`expires_at_utc` 是自动释放 deadline，`row_version` 协调续期与扫描并发；`quality_status` 只允许受控库存状态。 | 来源单据唯一索引防重复预留；SKU/site/location/status 索引用于 ATP 和补偿查询；SKU/site/expiry/status 索引用于 FEFO 和临期预留追踪；`organization_id + environment_id + expires_at_utc + status` 支持超时扫描；状态 check constraint 防自由文本库存状态。 | 创建时按来源用途写入受控默认 TTL，调用方只能在预留仍有效且关联作业仍在进行时续期；到期会原子恢复 ledger 可用量并发布公共失效事件。FEFO 预留可产生多个 reservation allocation，但 WMS 当前仅消费单 allocation 拣货；不删除历史预留事实。 |
| `stock_count_tasks` | business | 库存盘点任务事实，记录盘点范围、期望 ledger version、盘点数量、差异、审批状态、冻结状态、取消状态和复盘状态。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + count_task_code` 是业务唯一键；`organization_id + environment_id + idempotency_key` 是调用方幂等恢复键；`expected_ledger_version` 是确认差异前的并发基线。 | 状态与库存维度索引支持待盘点任务查询；幂等键唯一索引用于 WMS RPC 超时后的同 key 恢复；状态 check constraint 防自由文本库存状态。 | 创建后冻结目标 ledger 的普通移动；阈值外差异进入 `pending-approval` 并保持冻结，审批通过才过账；拒绝或退回进入 `recount-required` 并释放冻结。 |
| `stock_count_adjustments` | business | 盘点差异确认事实，记录盘点任务、幂等键、差异数量/金额、审批链、生命周期以及已生成的库存移动 ID。 | `id` 为 Guid v7 强类型 ID；`idempotency_key` 保护确认幂等；`approval_chain_id` 唯一关联 BusinessApproval 链；`movement_id` 仅在 `posted` 后指向库存移动业务 ID；`status` 只允许 `pending-approval`、`posted`、`voided`。 | 盘点任务和库存维度索引用于差异追踪；审批链唯一索引阻止同一链重复处理。 | 阈值外差异先保留为待审批事实，不直接覆盖历史移动；批准后转 posted，拒绝或退回后 voided 并由任务进入复盘。 |
| `integration_event_dead_letters` | system | Inventory 消费侧在业务处理前拒绝的集成事件，用于版本不兼容、envelope 缺失等场景的排查和 replay 标记。 | `id` 为 Guid；`consumer_name`、`event_id`、`event_type`、`event_version`、`source_service`、`idempotency_key`、`status` 和 `event_json` 保留拒绝事实。 | `consumer_name + status + dead_lettered_at_utc` 支撑 pending 队列查看；`consumer_name + event_id` 支撑单事件排查。 | PostgreSQL profile 下由共享 persistent DLQ store 写入；operator replay 后标记 `Replayed`，不删除原始拒绝事实。 |
| `cap_published_messages` | system | CAP published message outbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部索引用于投递扫描和过期清理。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `cap_received_messages` | system | CAP received message inbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部索引用于消费幂等、分组扫描和过期清理。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `cap_locks` | system | CAP distributed lock table，由 netcorepal/CAP 基础设施维护。 | 主键 `Key`。 | 主键用于 CAP 内部协调。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `__EFMigrationsHistory` | system | EF Core migration history table，记录 BusinessInventory 已应用迁移。 | `MigrationId + ProductVersion` 由 EF Core 维护。 | EF Core 用于判定待应用迁移。 | 必须位于 `inventory` schema；业务代码不直接读写。 |

Known gaps:

1. Inventory 已覆盖库存地点、余额、受控库存状态、预留/释放/分配、移动平均计价、移动、盘点冻结和盘点调整；批次谱系、WMS 作业单和 ERP GL 月结仍归后续业务切片补齐。
2. `restricted` 是质量让步接收/条件放行后的受控库存状态。`AddRestrictedQualityStatus` 的 Down 迁移会恢复三值 check constraint；如果库中已经存在 `restricted` 行，回滚前必须先迁移或清理这些库存事实，否则数据库会拒绝重新添加旧约束。

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
11. `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/Migrations/20260609061105_AddMesConsumerInboxIdempotency.cs`
12. `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/Migrations/20260612073555_AddCodingTables.cs`
13. `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/Migrations/20260615092140_AddMesBusinessLoopFacts.cs`
14. `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/Migrations/20260624151023_UseIdempotencyKeyForProcessedIntegrationEvents.cs`
15. `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/Migrations/20260624150553_AddMesQualityHoldContexts.cs`
16. `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/Migrations/20260705071030_AddOperationTaskQualityInspectionTrigger.cs`
17. `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/Migrations/20260706064951_AddMesEngineeringChangeWorkOrderImpacts.cs`
18. `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/Migrations/20260707024208_AddOperationTaskSopOperationCode.cs`

| Table | Kind | Purpose | Key columns | Index intent | Lifecycle |
| --- | --- | --- | --- | --- | --- |
| `work_orders` | business | MES 持久化工单事实，记录 SKU、生产版本引用、计划数量、单位、优先级、交期、执行状态、累计良品/报废数量、完工关闭时间，以及从 DemandPlanning 或等价来源计划转单时复制的 durable source plan reference。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + work_order_id` 是业务唯一键；`production_version_id` 是 ProductEngineering 业务引用；`uom_code` 为来源计划单位快照；`completed_quantity`、`scrap_quantity` 和 `over_receipt_tolerance_percent` 支撑执行进度；`source_system + source_document_type + source_document_id + source_demand_reference` 是跨服务来源计划/需求回溯引用，不建立跨 schema FK。 | 唯一约束保护同一组织/环境内工单号；SKU/交期索引用于排产扫描；`source_system + source_document_id` 索引用于从来源计划回查已转 MES 工单。 | 工单创建后可 release/start/hold/complete/close/cancel/scrap；complete 表示计划数量已由良品或报废执行覆盖，成品入库仍由 `output_lot_genealogies` 的产出批次强引用单独门控；历史保留用于报工、入库请求、成本追踪和需求/MRP/MES 计划来源追溯。 |
| `operation_tasks` | business | MES 工序任务事实，保存工序顺序、工作中心、可选工作中心、持续时间、执行窗口、暂停累计、实际人工/机器工时、派工班次/人员/设备事实、ProductEngineering 标准工序编码以及 Quality 报检触发快照。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + operation_task_id` 是业务唯一键；`operation_code` 是可选 StandardOperation 编码，用于 MES/PDA 查询当前生效 SOP/电子作业指导书；`sku_code`、`uom_code`、`planned_quantity` 和 `requires_quality_inspection` 复制工单/Routing 报检上下文；`assigned_user_id`、`device_asset_id`、`shift_id` 和 `assigned_at_utc` 记录当前派工事实；`paused_at_utc`、`paused_duration_ticks`、`labor_time_ticks`、`machine_time_ticks` 记录暂停扣时后的执行工时事实。 | 工单/工序顺序索引用于按工单加载工序并执行前序门禁；外键索引用于报工与工单追踪；`operation_code` 不建立跨 schema FK，仅作为 ProductEngineering SOP 查询条件；完工事件把 `requires_quality_inspection` 和 SKU 快照传递给 Quality，用于生成过程检任务。 | 随工单创建或执行调整保留；状态表达排产、派工和执行进度；完工后工时字段作为 OEE/绩效核算输入，SOP 由 ProductEngineering 当前版本查询决定，报检字段作为事件驱动待检任务生成依据。 |
| `production_reports` | business | MES 报工事实，记录工单/工序的良品数、报废数、返工数、完工标记、报工时间、可选报废原因/不良记录号、产出批次/序列号谱系，以及冲销单到原报工的审计链。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + report_no` 是报工业务号唯一键；`work_order_id` 与 `operation_task_id` 为 MES 业务引用；`material_movement_count` 固化本报工发出的生产耗料 Inventory movement 数；`source` 标识 `manual` 或 `telemetry`；OEE 字段是创建时固化的投影快照，冲销复用原报工快照，不建立跨服务 FK。 | 报工号唯一索引用于重试和追踪；工单/工序/时间索引用于执行时间线查询；`organization_id + environment_id + reversed_report_no` 唯一索引用于防重复冲销。 | 报工创建后作为执行历史保留；物料消耗通过公共 Inventory movement request 表达，工单累计 report/material count 随 `WorkOrderCompleted` 提供 ERP 成本闭包证明。 |
| `telemetry_production_report_candidates` | business | MES 接收 IndustrialTelemetry 生产计数 delta 后的人工处理队列；它保存配置为 draft 的自动报工，或在活动报警、未映射工作中心、无唯一在制工单时暂停的计数。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + source_idempotency_key` 是跨服务计数事实唯一边界；设备、tag、bucket、good quantity 和可选工作中心/工单/工序记录可审计的解析上下文；`status` 仅为 `draft` 或 `pending-confirmation`，`suspension_reason` 解释自动过账被抑制的原因。 | 唯一索引阻止同一 IIoT event idempotency key 重复生成候选；范围、状态和创建时间索引支撑待人工确认队列扫描。 | 候选保留为自动报工审计和人工确认输入；它不修改工单进度，只有 `posted` 模式且设备—工作中心—在制工序匹配、无活动报警的计数才会生成正式 `production_reports`。#693 只落地内部持久化队列，不新增候选查询/确认 HTTP 合约。 |
| `production_report_material_consumptions` | business | MES 报工引用的实际物料批次消耗事实，用于工单、报工、物料批次追溯和 Inventory 过账失败诊断；冲销报工写入负数量表示线边库存回补。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + report_no` 指向报工号；记录 `material_id`、`material_lot_id`、`uom_code`、消耗数量、线边领料申请号、`inventory_posting_failure_code`、`inventory_posting_failure_message` 和 `inventory_posting_failed_at_utc`。 | 物料批次索引用于按批次追溯；工单索引用于工单谱系；报工/物料/批次唯一索引用于报工明细加载和重复写入兜底。 | 随报工写入，作为执行追溯历史保留；不拥有 Inventory 批次余额；Inventory 拒绝生产消耗过账时由 MES 消费失败事件并回写到对应消耗事实，报工读面暴露最近失败诊断；历史缺失 UOM 的记录以 `UNSPECIFIED` 显式标记，不能用于发送库存事件。 |
| `output_lot_genealogies` | business | MES 产出批次谱系断点，连接工单、产出工序、报工号、产出批次/序列号和对应报工耗料事实。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + produced_lot_no` 是产出批次唯一键；`organization_id + environment_id + report_no` 唯一指向创建该批次的报工；`work_order_id`、`operation_task_id`、`quantity` 和 `created_at_utc` 保存产出上下文。 | 批次唯一索引用于完工入库强引用和批次追溯；工单索引用于工单谱系；报工唯一索引用于从产出批次回查耗料边。 | 由输出工序报工自动生成；完工入库请求必须引用已存在的产出批次；耗料边仍由 `production_report_material_consumptions.report_no` 连接，不拥有 Inventory 批次余额。 |
| `defect_records` | business | MES 制程不良事实，记录工单/可选工序、不良编码、数量、状态、记录时间、Quality NCR 链接和处置回写引用，支撑 MES 质量上下文列表。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + defect_no` 是不良记录业务号唯一键；`work_order_id` 指向 MES 工单业务号，`operation_task_id` 是可选工序业务引用；`ncr_id`、`ncr_code`、`disposition_type` 和 `disposition_reference_id` 只保存 Quality/downstream 公开引用。 | 不良号唯一索引用于重试和追踪；工单/时间与工序/时间索引用于质量上下文分页和工序追溯。 | 创建后通过公共 Quality defect-raised 事件请求 NCR；Quality 仍拥有正式 NCR/处置，MES 消费处置事件后只更新本地执行上下文。 |
| `quality_hold_contexts` | business | MES 本地质量保留投影，消费 Quality 检验结果事件后记录工单/可选工序的当前质量 hold 状态和 release 审计，供 release/start/dispatch 门禁使用。 | `id` 为 Guid v7 强类型 ID；`work_order_id` 与可选 `operation_task_id` 绑定 MES 执行对象；`source_service + source_document_id` 记录 Quality 检验来源文档；`inspection_record_id`、`inspection_plan_id`、`result`、`event_type`、`disposition_reason`、`recorded_at_utc` 和 `active` 保存最新质量结论；`held_inspection_record_id`、`hold_reason`、`held_at_utc`、`held_by` 保留原始 hold 来源；`release_inspection_record_id`、`release_reason`、`release_source`、`released_at_utc`、`released_by` 记录自动或人工释放来源。 | `organization_id + environment_id + source_service + source_document_id` 唯一，防止同一 Quality 来源上下文重复投影；工单/active 索引用于 release/start/dispatch 门禁快速查询。 | 由 Quality `InspectionResultIntegrationEvent` 更新；`rejected` 激活 hold，`passed`/`conditional-release` 按来源文档范围清除对应 hold；人工强制释放必须记录理由和操作者且重复释放幂等。Quality 仍拥有正式检验记录，MES 只保存门禁所需执行上下文。 |
| `material_requirements` | business | MES 工单/工序级物料需求与供应快照，记录来自 released MBOM、Inventory 和 WMS readiness 的执行侧视图。 | `id` 为 Guid v7 强类型 ID；`work_order_id` 和可选 `operation_task_id` 绑定 MES 执行对象；`material_id`、`material_lot_id`、需求量、可用量、备料量和 source snapshot 保留来源。 | 工单/物料索引用于齐套检查；工序索引用于开工前阻断检查。 | 由上游 readiness/导入适配器或测试 fixture 捕获，后续重新计算可写新快照；MES 不把它作为库存余额真相源。 |
| `material_issue_requests` | business | MES 领料/备料申请与线边接收事实，跟踪 requested/received 数量、单位、实际接收批次和 Inventory 过账失败诊断。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + request_no` 是领料申请业务号唯一键；记录工单、工序、物料、`uom_code`、批次、请求/接收数量、状态、`inventory_posting_failure_code`、`inventory_posting_failure_message`、`inventory_posting_failed_at_utc` 和 `inventory_posting_rollback_key`。 | 请求号唯一索引用于重试和下游引用；工单/物料索引用于齐套汇总；工序索引用于工序开工检查。 | 创建后作为 MES 执行事实保留，并通过公共 Inventory movement request 事件表达线边领料出库意图；Inventory 过账拒绝会由 MES 消费失败事件、按归一化 receipt-step rollback key 至多回滚一次对应乐观接收数量并保留失败诊断；历史缺失 UOM 的记录以 `UNSPECIFIED` 显式标记并被事件转换拒绝；WMS 拣货执行仍由 WMS 边界拥有。 |
| `finished_goods_receipt_requests` | business | MES 完工入库请求事实，向 Inventory 边界表达成品收货意图，并保留可选产出批次/序列号、生产日期、效期、入库单位成本、Inventory 累计过账数量、过账引用和失败诊断。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + request_no` 是入库请求业务号唯一键；记录 `work_order_id`、`sku_id`、`quantity`、`uom_code`、`unit_cost`、`status`、请求时间、`produced_lot_no`、`serial_no`、`production_date`、`expiry_date`、`posted_quantity`、`posted_inventory_movement_id`、`posted_at_utc`、`inventory_posting_failure_code`、`inventory_posting_failure_message` 和 `inventory_posting_failed_at_utc`；`posted_quantity` 支撑 `PartiallyPosted` 与剩余数量重试；`unit_cost` 对 legacy 记录可为空，新建请求需提供正数以传递给 Inventory 移动平均计价。 | 请求号唯一索引用于下游引用；工单/SKU/时间索引用于入库请求追踪；创建请求的幂等 fingerprint 包含 `unit_cost`、`production_date` 和 `expiry_date`，同一业务重试 key 携带不同成本或效期时视为 payload 冲突。 | 创建后通过公共 Inventory movement request 事件表达入库意图并携带批次效期；Inventory 拥有正式库存移动和计价结果，MES 保存请求成本输入、累计已过账数量、已知过账引用和 Inventory 拒绝诊断；失败或部分过账请求通过同一请求号重试剩余数量，不创建新的入库申请。 |
| `schedule_results` | business | MES 规则排产结果事实，保存排产版本、触发原因、排产时间和 JSON assignment 结果。 | `schedule_version` 唯一；`assignments_json` 和 `affected_work_order_ids_json` 是 append-only JSON。 | 版本唯一索引支持读取当前/历史版本；触发源/时间索引用于诊断。 | 每次排产生成一条版本化结果，历史版本保留。 |
| `work_center_unavailabilities` | business | MES 工作中心不可用窗口事实，来自维修或手工约束，用于排产避让。 | `organization_id + environment_id + downtime_event_no` 是停机事件业务号唯一键；`work_center_id`、`from_utc`、`to_utc` 定义不可用窗口；`device_asset_id` 记录可选设备来源。 | 停机号唯一索引用于恢复和追踪；工作中心窗口索引用于排产查询；asset open 索引用于设备恢复处理。 | 打开窗口以 `to_utc = null` 表示仍不可用，关闭后保留历史。 |
| `device_asset_work_center_mappings` | business | MES 本地设备资产到工作中心映射，用于把 Maintenance 设备事件转换为 MES 排产约束。 | `organization_id + environment_id + device_asset_id` 唯一；`work_center_id` 是 MasterData 工作中心公开 ID。 | 唯一索引防止同一设备映射到多个 MES 工作中心。 | 映射作为本地配置事实保留。 |
| `shift_handovers` | business | MES 班次交接事实，记录班次、班组、交接状态、创建时间、接收时间和创建时未结事项数量。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + handover_no` 是交接业务号唯一键；`shift_id` 和 `team_id` 是 MasterData 公开 ID 快照；`open_issue_count` 当前为创建时 organization/environment 维度的粗粒度未结事项快照，源事实尚未全部具备 shift/team 归属。 | 交接号唯一索引用于接收和追踪；班次/创建时间索引用于班次交接列表分页。 | 创建后从 `Open` 进入 `Accepted`；重复接收保持首次接收时间，作为跨班组执行交接历史保留。 |
| `engineering_change_work_order_impacts` | business | MES 消费 ProductEngineering ECO release 后保存受影响工单、已归档生产版本 guard 标记和人工决策留痕。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + work_order_id + change_number` 是幂等业务唯一键；`archived_production_version_id` 和可选 `superseded_by_production_version_id` 记录 ProductEngineering 生产版本引用；`work_order_status_at_detection` 记录 ECO 影响检测时的 MES 工单状态，用于人工继续后恢复 hold 前状态；`status` 表达 archived marker、pending decision、auto rebound、manual confirmation block 或 decided；`decision`、`decided_by`、`decision_reason` 和 `decided_at_utc` 保留工艺/计划员决策依据。 | 唯一索引防止同一 ECO 对同一 MES 工单重复处理；`organization_id + environment_id + archived_production_version_id + status` 支撑新建工单拒绝已归档生产版本及 ECO 影响排查。 | ECO release 事件写入归档版本标记并扫描未关闭工单；Created/Released 工单按配置自动重绑新版或 hold 待确认，已开工工单进入待决策并发布通知事件；人工继续会解除工程变更 hold 并恢复检测时状态，人工中止会取消工单；不改写 ProductEngineering 事实。 |
| `code_counters` | business | MES service-local 编码计数器，用于工单、报工、物料请求、不良、停机、交接和完工入库请求等普通创建请求自动分配业务 code。 | `organization_id + environment_id + rule_key + site_code + reset_key` 是计数器唯一范围；`current_value` 和 `version` 维护已分配序列与乐观并发。 | 唯一索引保护同一编码规则范围只有一个 counter。 | 创建请求按需创建并递增；不由用户直接维护。 |
| `code_idempotency_keys` | business | MES 创建请求幂等键记录，把客户端 key 绑定到已分配业务 code 和 payload fingerprint。 | `organization_id + environment_id + rule_key + idempotency_key` 是唯一键；`code` 保存首次分配结果。 | 唯一索引阻止同一 key 在同一规则内重复创建不同资源。 | 随创建请求写入，保留用于重试去重和冲突诊断。 |
| `cap_published_messages` | system | CAP published message outbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部索引用于投递扫描和过期清理。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `cap_received_messages` | system | CAP received message inbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部索引用于消费幂等、分组扫描和过期清理。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `cap_locks` | system | CAP distributed lock table，由 netcorepal/CAP 基础设施维护。 | 主键 `Key`。 | 主键用于 CAP 内部协调。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `processed_integration_events` | system | MES 业务 inbox，记录设备可用性事件消费者已经执行业务副作用的事件。 | `consumer_name + idempotency_key` 是唯一消费边界；`event_id` 仅用于追溯原始发布事件；`source_service + event_type + processed_at_utc` 支撑消费诊断。 | 唯一索引用于 RabbitMQ/CAP 重投、re-release 和并发消费兜底。 | 随消费者成功处理写入；不建立跨 schema 外键，不删除原始事件事实。 |
| `integration_event_dead_letters` | system | MES 消费侧在业务处理前拒绝的集成事件，用于版本不兼容、envelope 缺失等场景的排查和 replay 标记。 | `id` 为 Guid；`consumer_name`、`event_id`、`event_type`、`event_version`、`source_service`、`idempotency_key`、`status` 和 `event_json` 保留拒绝事实。 | `consumer_name + status + dead_lettered_at_utc` 支撑 pending 队列查看；`consumer_name + event_id` 支撑单事件排查。 | PostgreSQL profile 下由共享 persistent DLQ store 写入；operator replay 后标记 `Replayed`，不删除原始拒绝事实。 |
| `__EFMigrationsHistory` | system | EF Core migration history table，记录 BusinessMES 已应用迁移。 | `MigrationId + ProductVersion` 由 EF Core 维护。 | EF Core 用于判定待应用迁移。 | 必须位于 `mes` schema；业务代码不直接读写。 |

Known gaps:

1. MES 当前已有物料需求快照、领料/线边接收、报工消耗批次、产出批次/序列号、质量 hold 投影和工单进度追溯事实；仍需后续把 WMS 作业状态和 ERP/采购到货通过正式 adapter/event 持续刷新执行快照。
2. MES durable source plan reference 只保存来源计划/需求的稳定业务标识和快照字段；DemandPlanning 仍拥有需求来源、MRP run、pegging 和计划建议事实，MES 不读取 `demand_planning` schema，也不对来源表建外键。

## BusinessDemandPlanning Schema

Schema: `demand_planning`

Owner: `backend/services/Business/DemandPlanning`

Source:

1. `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/ApplicationDbContext.cs`
2. `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/EntityConfigurations/*.cs`
3. `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/Migrations/20260523103405_InitialDemandPlanningSchema.cs`
4. `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/Migrations/20260527073227_AddNumberingCounters.cs`
5. `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/Migrations/20260612073626_AddCodingTables.cs`
6. `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/Migrations/20260702063924_AddMpsLifecycleAndMrpInputSources.cs`
7. `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/Migrations/20260702093625_AddMrpNetRequirementExplanation.cs`
8. `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/Migrations/20260706070015_AddForecastInputsAndMrpExceptions.cs`

| Table | Kind | Purpose | Key columns | Index intent | Lifecycle |
| --- | --- | --- | --- | --- | --- |
| `demand_sources` | business | DemandPlanning 拥有的销售订单、手工需求和安全库存等需求来源事实；预测输入已拆到 `forecast_inputs` 并在进入 MRP 前做订单消耗。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + source_reference` 是业务唯一键；保留 SKU、数量、需求日期和来源单据引用。 | 业务唯一索引防重复录入；SKU/日期/类型索引用于 MPS/MRP 输入扫描。 | 创建或调整后作为计划输入保留；不会创建正式销售、采购或生产单据。 |
| `forecast_inputs` | business | DemandPlanning 拥有的预测输入事实，按 SKU、UOM、site 和期间记录 forecast 数量及前后消耗窗口。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + forecast_reference` 是业务唯一键；保留 SKU、期间、数量、backward/forward consumption days。 | forecast reference 唯一索引用于幂等维护；SKU/site/period end 索引用于 MRP horizon 扫描。 | 创建或调整后进入 MRP 输入准备；同 SKU/UOM/site 的 sales-order 在配置窗口内先消耗 forecast，剩余 forecast 才作为 MRP forecast demand。 |
| `master_production_schedules` | business | 日粒度 MPS bucket，固化 MRP 展开前的主生产计划口径。 | `id` 为 Guid v7 强类型 ID；记录 SKU、bucket date、计划数量、UOM、状态，以及评审/发布人和时间快照。 | SKU/bucket date 索引用于按物料和日期展开净需求。 | 草稿可维护，评审后可发布；只有 released MPS bucket 进入 MRP 输入。历史保留用于追踪 MRP 输入。 |
| `mrp_runs` | business | MRP 计算运行头和输入快照元数据。 | `id` 为 Guid v7 强类型 ID；`run_id` 为外部可见运行编号；保存计划窗口、状态、输入快照摘要、输入来源汇总和输入覆盖周期。 | run id 唯一索引支持幂等读取；状态/创建时间索引用于运行列表。 | 每次 MRP 运行生成独立事实；不直接创建 ERP/MES 正式单据，运行结果可展示 MPS、销售需求、预测、安全库存等输入来源。 |
| `planning_suggestions` | business | MRP 生成的计划采购建议、计划工单建议和计划收货例外消息（`reschedule-in`/`reschedule-out`/`cancel`），并持久化计划员可读的净需求解释。 | `id` 为 Guid v7 强类型 ID；记录 suggestion id、建议类型、SKU、数量、需求日期、提前期偏置后的下达日期或例外原计划日期和状态；`gross_demand_quantity`、`on_hand_quantity`、`reserved_quantity`、`available_to_net_quantity`、`scheduled_receipt_quantity`、`safety_stock_quantity`、`net_requirement_quantity`、`planned_quantity`、`scrap_rate`、`yield_rate`、`primary_source_type`、`formula` 和 `uom_conversion_summary` 保存本次 MRP 输入快照与公式。 | run id/status 索引用于按 MRP run 查看建议；SKU/date 索引用于计划员筛选。 | 计划采购/工单建议可被接受、拒绝或关闭；例外消息作为计划员 workbench/read model 展示，不触发 ERP/MES 下游建单事件；净需求解释随建议保留，避免前端或下游服务重算 MRP。 |
| `mrp_pegging_links` | business | 从计划建议回溯到需求、BOM 展开、在途供应和库存快照的 pegging 链路。 | `id` 为 Guid v7 强类型 ID；记录 suggestion、demand、parent/child 关系、数量、`source_type` 和 `gross_demand_quantity`，用于区分销售、预测、安全库存、MPS、组件展开和在途来源。 | suggestion id 索引用于读取建议追溯；run id 索引用于诊断整次展开。 | 随 MRP run 与建议生成后保留，用于解释计划结果和来源跳转；不建立跨服务 FK。 |
| `code_counters` | business | DemandPlanning service-local 编码计数器，用于需求来源等普通创建请求自动分配业务 code。 | `organization_id + environment_id + rule_key + site_code + reset_key` 是计数器唯一范围；`current_value` 和 `version` 维护已分配序列与乐观并发。 | 唯一索引保护同一编码规则范围只有一个 counter。 | 创建请求按需创建并递增；不由用户直接维护。 |
| `code_idempotency_keys` | business | DemandPlanning 创建请求幂等键记录，把客户端 key 绑定到已分配业务 code 和 payload fingerprint。 | `organization_id + environment_id + rule_key + idempotency_key` 是唯一键；`code` 保存首次分配结果。 | 唯一索引阻止同一 key 在同一规则内重复创建不同资源。 | 随创建请求写入，保留用于重试去重和冲突诊断。 |
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
4. `backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Infrastructure/Migrations/20260615073431_AddGs1ScanTraceability.cs`
5. `backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Infrastructure/Migrations/20260616015829_AddGs1CompanyPrefixLength.cs`
6. `backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Infrastructure/Migrations/20260629081137_Issue556Gs1EpcisAggregationAndSerialUniqueness.cs`
7. `backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Infrastructure/Migrations/20260707114840_AddScanNaturalKeyAndDownstreamStatus.cs`
8. `backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Infrastructure/Migrations/20260710035759_AddPrintLifecycleAndPrinterTransport.cs`

| Table | Kind | Purpose | Key columns | Index intent | Lifecycle |
| --- | --- | --- | --- | --- | --- |
| `barcode_rules` | business | 条码规则定义，描述编码范围、前缀、序列和业务对象绑定；GS1 规则额外保存显式 `gs1_company_prefix_length`，用于正确拆分 SGTIN EPC URI。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + rule_code` 是业务唯一键；GS1 规则要求 13 位 GTIN root 和 6-12 位 company prefix length。 | rule code 唯一索引防重复；对象类型/状态索引用于选择可用规则。 | 规则版本作为配置事实保留；停用后不再用于新标签生成。 |
| `label_templates` | business | 标签模板引用，绑定 FileStorage file id、模板名称和变量 schema。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + template_code` 是业务唯一键；`template_file_id` 是 FileStorage 公开引用。 | template code 唯一索引防重复；状态索引用于筛选可用模板。 | 模板登记后保留；文件本体和下载授权仍由 FileStorage 管理。 |
| `label_print_batches` | business | 标签打印批次，记录模板、规则、业务来源和真实打印状态。 | `id` 为 Guid v7 强类型 ID；记录 `pending`、`sent-to-printer`、`printed` 或 `failed` 状态，以及 printer/job/failure facts；`organization_id + environment_id + idempotency_key` 是业务唯一键。 | 幂等键唯一索引防重复；来源单据索引用于追踪打印来源。 | 批次创建只生成标签；只有适配器反馈 `printed` 才发出完成事实，失败不会驱动库存操作。 |
| `label_print_items` | business | 打印批次内单张标签项，保存条码值、GS1 GTIN、批次、序列号和 EPC URI。 | `id` 为 Guid v7 强类型 ID；`label_print_batch_id` 归属批次；`label_value` 是标签值；记录 `created`、`printed`、`reprinted`、`voided` 或 `consumed` 状态和作废原因。 | label value 索引用于扫码反查；batch+sequence 唯一索引用于批次内顺序；GTIN/lot/serial 索引用于序列化追溯。 | 作废标签在扫码前被拒绝；已消费标签不再允许作废或重印。 |
| `scan_records` | business | 扫码记录事实，记录扫码对象、结果、设备/人员、幂等键、GS1 解析字段、下游业务动作选择和 downstream processing status。 | `id` 为 Guid v7 强类型 ID；记录 scanned value、source workflow、source document、result、idempotency key、GTIN、lot、serial、SSCC、quantity、SKU/UOM/site/location、downstream event id 和 `downstream_processing_status`（`observed`、`requested` 或 `not-required`）。 | 幂等键索引用于 PDA/Connector 重试去重；accepted `organization_id + environment_id + scanned_value + source_workflow + source_document_id` 唯一索引用于同单据同条码连扫兜底；EPC URI、lot-present GTIN/lot/serial 和 no-lot GTIN/serial 唯一索引用于防止同一序列化条码跨幂等键重复扫码；rejected scanned value 可重复记录；SSCC 索引用于物流单元追溯；source workflow/document 索引用于业务追溯。 | append-only 扫码事实；应用层对同一 accepted 扫码自然键返回既有 ScanRecord，避免重复发布库存移动；`inventory.receipt`、`inventory.issue`、`inventory.adjustment` 会发布 Inventory movement-requested 事件；`wms.receiving`、`production.report`、`quality.inspection` 和 `inventory.count` 会记录显式 downstream scan action 并发布通用 BarcodeScanAccepted 事实，具体业务状态仍归各下游服务。 |
| `epcis_events` | business | BarcodeLabel 生成的 EPCIS 追溯事件，覆盖 serialized label commissioning、accepted scan object event，以及 SSCC parent 与 SGTIN child 的 aggregation/disaggregation 语义。 | `id` 为 Guid v7 强类型 ID；记录 event type、action、CBV business step URI、disposition、label value、GTIN、lot、serial、EPC URI、parent SSCC、可选 parent EPC URI、source workflow/document、可选 print item 或 scan record id。 | event type + EPC URI、event type + lot-present GTIN/lot/serial 和 event type + no-lot GTIN/serial 唯一索引用于防止同一序列化事件重复写入；parent SSCC 索引用于按物流单元回放聚合关系；source workflow/document 索引用于按业务单据回放；print item/scan record 索引用于从标签或扫码事实跳转。 | append-only 追溯事实；不建立跨 schema FK，不替代 MES/WMS/Inventory 自有业务状态。 |
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
5. `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Infrastructure/Migrations/20260615075007_Issue417ApprovalWorkflowGaps.cs`
6. `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Infrastructure/Migrations/20260621134207_Issue449ApprovalActionsDecisionAudit.cs`
7. `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Infrastructure/Migrations/20260626170441_AddApprovalChainConcurrencyToken.cs`

| Table | Kind | Purpose | Key columns | Index intent | Lifecycle |
| --- | --- | --- | --- | --- | --- |
| `approval_templates` | business | 业务审批模板，按业务单据类型定义审批链。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + template_code` 是业务唯一键。 | template code 唯一索引防重复；业务单据类型/状态索引用于选择 active 模板。 | 模板激活后供新审批链复制步骤；历史模板保留。 |
| `approval_template_steps` | business | 模板中的有序审批步骤定义，支持同 step no 的会签/或签和简单条件路由。 | `id` 为 Guid v7 强类型 ID；`approval_template_id` 归属模板；`step_no` 为模板内顺序；`completion_policy` 为 `all`/`any`；`condition_expression` 当前支持空、`documentType=<value>`、`sourceService=<value>`。 | template + step no + approver 唯一，防止同一模板步骤重复配置。 | 随模板维护；运行链启动后会复制为 runtime steps。 |
| `approval_chains` | business | 运行中的业务审批链实例，绑定来源服务和来源单据。 | `id` 为 Guid v7 强类型 ID；记录 chain id、source service、source document id、status、`round_no` 当前提交轮次和 `row_version` 乐观并发令牌。 | 来源单据索引用于业务反查；状态索引用于待审批列表；`row_version` 作为 EF concurrency token 保护审批决策和运行时步骤变更。 | 从 pending 进入 approved/rejected/returned/withdrawn 等状态；returned/withdrawn 可由重提回到 pending 并推进 `round_no`；不替代 Ops 运维审批。 |
| `approval_steps` | business | 运行审批链中的步骤快照，保存会签/或签策略、条件结果和超时通知状态。 | `id` 为 Guid v7 强类型 ID；`approval_chain_id` 归属链；`step_no` 为链内顺序；`overdue_notified_at_utc` 标记已发超时事件。 | chain + step no + approver 唯一，支持按链加载步骤和并行组推进。 | 随链创建并被审批动作、加签和转签推进；加签会将当前步骤组显式切为 all-required；重提会按新提交时间重算 due time；内部 `checkOverdueApprovalSteps` endpoint 与 `Approval:OverdueCheck` 后台扫描器使用服务端时钟标记超时并发布事件；平台 AppHost 为本地 `org-001`/`env-dev` 启用自动扫描，Notification 消费后生成催办任务并可按 `Approval:OverdueEscalation:RecipientRefs` 追加升级收件人，不直接跨服务写通知表。 |
| `approval_decisions` | business | append-only 审批决定与治理动作事实，记录审批人、代理审批来源、动作、意见、轮次和时间。 | `id` 为 Guid v7 强类型 ID；`approval_chain_id`、`round_no` 和 `step_no` 绑定决策位置；`decision` 可记录 approve/reject/return/withdraw/resubmit/add_signer/transfer；`on_behalf_of_actor_type/ref` 记录被代理审批人。 | chain + step + actor + on-behalf 普通索引用于审计查询；approve/reject/return 另有 chain + round + step + actor + on-behalf partial unique index，保留同一轮次内 DB 级重复决策防护，同时允许新提交轮次再次决策。 | 决策/动作只追加不物理删除，作为审批审计事实；StepResolved 与 ActionRecorded 事件幂等键包含轮次、步骤和 actor/action 信息。 |
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
4. `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Infrastructure/Migrations/20260615072943_ScopeWcsTaskExternalTaskIdByTenant.cs`
5. `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Infrastructure/Migrations/20260616110921_AddWmsInventoryReservationLink.cs`
6. `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Infrastructure/Migrations/20260630023839_AddWmsWcsCancellationConsumerInbox.cs`
7. `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Infrastructure/Migrations/20260706064412_AddWmsReceivingQualityGate.cs`
8. `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Infrastructure/Migrations/20260711064952_AddBackorderReplenishment.cs`

| Table | Kind | Purpose | Key columns | Index intent | Lifecycle |
| --- | --- | --- | --- | --- | --- |
| `inbound_orders` | business | WMS 入库执行单头，记录来源单据、仓库、状态、收货完成与取消审计事实。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + inbound_order_no` 是业务唯一键；`cancellation_reason` 和 `cancelled_at_utc` 记录取消原因与 UTC 时间。 | 入库单号唯一索引防重复；`source_document_type + source_document_id + status` 索引支持 ERP 按来源关闭开放入库预期。 | 入库单创建后推进收货、上架、完成或取消；库存移动由 Inventory 边界承接。 |
| `inbound_order_lines` | business | 入库行，记录 SKU、数量、批次/序列号、生产日期、效期、收货结果和 WMS 质量门禁状态。 | `id` 为 Guid v7 强类型 ID；`inbound_order_id` 归属入库单；`production_date`、`expiry_date` 是 WMS 收货采集并传递给 Inventory 的批次效期；`quality_gate_status` 记录 `pending`、`passed`、`conditional-release`、`rejected` 或 `not-required`；`inspection_record_id` 和 `quality_disposition_reason` 复制 Quality 放行/拒收事实。 | inbound order 索引用于加载明细。 | 生命周期随入库单推进并保留历史；需检入库完成时先生成 `quality` 库存状态的 Inventory movement request 并等待 Quality 结果；免检入库直接生成 `unrestricted` movement request。 |
| `outbound_orders` | business | WMS 出库执行单头、复核包装事实和取消审计。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + outbound_order_id` 是业务唯一键；`cancellation_reason` 和 `cancelled_at_utc` 记录取消原因与取消时间。 | 出库单号唯一索引防重复；来源单据索引用于销售/调拨追踪。 | 出库单创建后推进拣货、复核、包装、完成或取消；取消会释放 Inventory reservation 并保留审计事实。 |
| `outbound_order_lines` | business | 出库行，记录 SKU、请求数量、实发数量、短拣欠交和可选 Inventory public reservation id。 | `id` 为 Guid v7 强类型 ID；`outbound_order_id` 归属出库单；`issued_quantity`/`backorder_quantity` 记录 pack review 时按 WarehouseTask 执行量收敛后的实发和欠交；`fulfillment_recorded` 区分真实 0 实发短拣与历史未记录执行量的兼容回退；`inventory_reservation_id` 记录拣货任务创建时由 Inventory reservation API 返回的预留事实 ID。 | outbound order 索引用于加载明细。 | 生命周期随出库单推进并保留历史；WMS 只保存 public reservation id，不维护库存余额；短拣差额通过 Inventory reservation release 释放。 |
| `backorder_orders` | business | 短拣产生的耐久欠交单据，保留原出库单行、SKU、目标拣货位、欠交数量和关闭审计。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + backorder_order_no` 和原出库单号 + 行号分别唯一。 | 单据号唯一索引支持查询与幂等；原出库单行唯一索引阻止完成重试重复生成欠交事实。 | 出库复核按实际拣货量发现欠交时创建；客服/计划可通过 WMS API 查询并带原因关闭，不回写其他服务 schema。 |
| `warehouse_tasks` | business | 上架、拣货和补货建议任务事实，记录任务类型、库位、数量和状态。 | `id` 为 Guid v7 强类型 ID；补货建议以 BackorderOrder 为来源单据，并把短拣拣货位作为目标位置。 | 组织、环境、任务号唯一索引防重复；状态索引用于任务队列。 | 短拣欠交与 `Replenishment` 建议在同一 WMS 事务创建；任务完成或取消后保留执行历史。首 tranche 不选择实际补货来源库位。 |
| `count_executions` | business | WMS 盘点执行和差异输出事实。 | `id` 为 Guid v7 强类型 ID；记录 count execution id、库位/SKU/差异数量；`inventory_count_task_id` 保存 Inventory count task public id，用于冻结台账和确认调整。 | execution id 唯一索引防重复；状态/仓库索引用于盘点列表。 | 创建时通过 Inventory count-task 边界冻结对应台账，完成时通过 Inventory adjustment 边界确认差异；外部 `count-adjustment` 事件不作为 WMS 盘点闭环。 |
| `wcs_tasks` | business | WCS adapter 任务映射、状态和外部任务诊断。 | `id` 为 Guid v7 强类型 ID；随 warehouse task 记录 `organization_id`、`environment_id`，并记录 warehouse task id、external task id、状态和失败原因。 | `organization_id + environment_id + external_task_id` 唯一索引保护租户内回调幂等并支持设备回调定位；状态索引用于自动化队列。 | 由 dispatch/complete/fail 推进，保留自动化执行诊断；WCS 事件必须携带真实租户上下文。 |
| `inventory_movement_requests` | business | WMS 向 Inventory 请求库存移动的本地 pending/posted/failed 元数据。 | `id` 为 Guid v7 强类型 ID；记录业务来源、幂等键、movement type、posting 状态、Inventory movement id、可选 `inventory_reservation_id`、`production_date`、`expiry_date` 和失败代码/消息。 | 幂等键索引用于防重复 request；状态索引用于补偿扫描；posted/failed 事件按组织、环境、movement type、来源单据、来源行和幂等键匹配本地 request。 | 由 WMS completion 在本地事务内创建 pending request，并通过公共 `Nerv.IIP.Contracts.Inventory` movement-requested / stock-movement-posted / stock-movement-posting-failed 集成事件异步闭环；入库 request 携带收货效期，出库 request 可携带 Inventory reservation id 供 Inventory 过账时分配预留；Inventory 业务拒绝会把 request 标记为 Failed，释放对应预留并冻结对应入库/出库单，retry 只重提 failed 行。 |
| `supplier_return_requests` | business | WMS 从 rejected 收货检验结果生成的退供应商请求事实。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + supplier_return_no` 是业务唯一键；`inbound_order_no`、`inbound_order_line_no`、`inspection_record_id`、SKU、批次/序列号、owner 和数量保留退供定位快照。 | 退供号唯一索引用于幂等和追踪；`organization_id + environment_id + inbound_order_no + inbound_order_line_no + inspection_record_id` 防止同一检验结果重复生成退供事实。 | 由 WMS Quality inspection result consumer 在 `quality.InspectionRejected` 时创建，后续 ERP 采购退货联动只引用该公开事实，不回写 Quality 或 Inventory schema。 |
| `integration_event_dead_letters` | system | WMS 消费侧在业务处理前拒绝的集成事件，用于版本不兼容、envelope 缺失等场景的排查和 replay 标记。 | `id` 为 Guid；`consumer_name`、`event_id`、`event_type`、`event_version`、`source_service`、`idempotency_key`、`status` 和 `event_json` 保留拒绝事实。 | `consumer_name + status + dead_lettered_at_utc` 支撑 pending 队列查看；`consumer_name + event_id` 支撑单事件排查。 | PostgreSQL profile 下由共享 persistent DLQ store 写入；operator replay 后标记 `Replayed`，不删除原始拒绝事实。 |
| `processed_integration_events` | system | WMS 业务 inbox，记录 WCS 撤单适配器消费者等已经执行业务副作用的事件。 | `consumer_name + idempotency_key` 是唯一消费边界；`event_id` 仅用于追溯原始发布事件；`source_service + event_type + processed_at_utc` 支撑消费诊断。 | 唯一索引用于 CAP/RabbitMQ/Redis 重投、re-release 和并发消费兜底。 | 随消费者成功处理写入；不建立跨 schema 外键，不删除原始事件事实；缺失 WCS adapter endpoint 等确定性配置错误进入 dead-letter 而不是写入 inbox。 |
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
5. `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/Migrations/20260612073612_AddCodingTables.cs`
6. `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/Migrations/20260615075555_AddErpIssue411BusinessGapClosure.cs`
7. `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/Migrations/20260617032054_AddErpPurchaseOrderApprovalChain.cs`
8. `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/Migrations/20260625062958_AddErpConsumerReliabilityBaseline.cs`
9. `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/Migrations/20260629063800_AddErpLongTailCurrencyTolerance.cs`
10. `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/Migrations/20260707020237_AddErpPeriodPaymentReceiptClose.cs`
11. `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/Migrations/20260707121325_AddPurchaseRequisitionConversionToPurchaseOrder.cs`

| Table | Kind | Purpose | Key columns | Index intent | Lifecycle |
| --- | --- | --- | --- | --- | --- |
| `purchase_requisitions` | business | ERP 采购申请，承接 DemandPlanning 建议或手工采购需求。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + suggestion_id` 幂等唯一；`converted_purchase_order_no`/`converted_at_utc` 记录转 PO 结果。 | suggestion/requisition no 唯一索引用于防重复下游单据。 | 创建后进入采购流；ERP 不拥有计划建议状态；转 PO 后进入 `Converted`，不可再次转入其他采购订单。 |
| `request_for_quotations` / `request_for_quotation_lines` / `request_for_quotation_suppliers` | business | RFQ 头、行和邀请供应商引用。 | RFQ id 归属头；行和供应商通过 owning FK 加载。 | RFQ no 唯一索引用于采购过程追踪。 | 创建后接收供应商报价；供应商主数据仍由 MasterData 拥有。 |
| `supplier_quotations` / `supplier_quotation_lines` | business | 供应商报价事实，记录数量、单价和承诺日期。 | `quotation_no` 是业务编号；行归属报价。 | quotation no 唯一索引用于报价幂等。 | 接收后保留为采购比价事实。 |
| `purchase_orders` / `purchase_order_lines` / `purchase_order_line_sources` / `purchase_order_changes` / `purchase_order_change_lines` | business | 采购订单头、行、来源申请追溯和不可变更改审计，记录订单币种、金额、收货容差、已收数量与审批中的目标值。 | `purchase_order_no` 是业务编号；`version` 是单调递增修订号；`approval_chain_id` 记录创建审批链；change 行记录待审批或已应用的目标 quantity/price/date、原因和审批链。 | PO no 唯一用于收货反查；source 行在同一 PO line 下按 requisition no + line no 唯一；change 的 approval chain 在同一 PO 下唯一。 | 创建后先进入 `PendingApproval`；已 release 的改数量/价格/交期先写入 change audit，批准后才影响开放在途；final delivery 按 under tolerance 关闭余量，取消拒绝已收货或已开票 PO；DemandPlanning 通过 ERP list 的 open quantity 读取实时候选在途。 |
| `purchase_receipts` / `purchase_receipt_lines` | business | ERP 采购收货事实，记录质量状态摘要、币种汇率和库存过账维度快照。 | `purchase_receipt_no` 是业务编号；头记录 PO 币种和暂估汇率；行引用 PO line no，并复制 SKU、UOM、location 和可选 lot。 | receipt no 唯一索引用于质量/WMS/GR-IR/Inventory 下游引用。 | 记录后不可变；收货通过公开 Inventory movement request 事件请求库存入账，并由 ERP Finance 按收货币种/汇率生成借库存、贷 GR/IR 的本位币平衡暂估凭证；质量检验和 AP 仍由公开事件/API 接线，收货本身不创建 AP。 |
| `purchase_returns` / `purchase_return_lines` | business | ERP 采购退货补偿事实，记录完成的 WMS 退供应商出库、源收货行和 GR/IR/借项数量拆分。 | `purchase_return_no` 是 ERP 单据号；`purchase_receipt_no` 和 `wms_outbound_order_no` 分别指向不可变收货和实际 WMS 出库；GR/IR 段使用 PO 单价，借项段保留匹配发票单价。 | return no 与 WMS outbound no 在组织/环境内唯一。 | 不删除原收货或凭证；未开票数量借 GR/IR、贷库存，已匹配发票数量以借项冲减 AP、贷库存。 |
| `supplier_invoices` / `supplier_invoice_lines` | business | 供应商发票三单匹配事实，匹配 PO、采购收货和发票行。 | `invoice_no` 是业务编号；头记录 PO/receipt/supplier/date/due date/currency/exchange rate/local total/match status，行记录 PO line、receipt line、SKU、UOM、数量和单价。 | invoice no 唯一索引用于防重复；行归属索引用于加载匹配明细。 | 匹配通过后按发票币种/汇率冲 GR/IR 并创建 AP，生成借 GR/IR、贷 AP 的本位币平衡应付子分类账凭证；发票币种必须与 PO 币种一致；超出数量、金额或价格百分比容差、累计已开票数量超过收货数量时标记 `PaymentHeld`，不创建 AP/凭证；人工释放 held 发票后执行同样的 GR/IR clearing + AP 生成，作废 held 发票后状态为 `Voided` 且数量不再占用后续累计开票匹配。 |
| `accounting_periods` | business | ERP 会计期间开关控制，记录期间关闭、重开和迟到集成事件例外原因。 | `period_code` 是业务期间码；记录 start/end date、status、opened/closed/reopened audit fields。 | `organization_id + environment_id + period_code` 唯一；日期范围索引用于凭证 posting date 守卫。 | 未配置期间时保持兼容；配置且关闭的期间拒绝新凭证，迟到集成事件或人工补录需先以审计原因重开期间后再过账。 |
| `gl_accounts` | business | ERP 最小总账科目层级。 | `code`、`name`、`account_type` 和可选 `parent_code` 形成租户内科目表；类型限定为资产、负债、权益、收入或费用。 | `organization_id + environment_id + code` 唯一。 | Phase 1 只冻结最小科目身份与层级；标准成本、期间制造费用分摊和完整科目维护 UI 后置。 |
| `work_center_cost_rates` | business | ERP 拥有的工作中心实际人工小时费率。 | `work_center_id` 引用 MES 公共工作中心标识快照，`hourly_rate` 为本位币正数。 | `organization_id + environment_id + work_center_id` 唯一。 | MES 报工事件按真实数量/理论节拍折算小时，ERP 再应用本表费率；不跨 schema 查询 MES。 |
| `work_order_costs` / `work_order_cost_details` | business | ERP 工单实际成本头与可审计材料/人工明细。 | 头保存 MES `work_order_id`、成品 SKU、完工数量/时间、预期/已收报工与耗料移动计数、已资本化数量/金额和已清 WIP；明细保存来源事件单据、成本类型、工作中心或材料维度、带符号数量、费率、金额和报工号。 | 工单在组织/环境内唯一；同一工单下来源单据唯一，消费者 inbox 同时防事件重投。 | MES 报工归集人工，Inventory 已过账生产耗料归集或冲销移动平均材料成本；只有 MES 完工闭包计数满足后才发布单位成本，部分成品入库按每次移动增量清 WIP，末次差额挂生产差异。 |
| `pending_material_costs` | system | Inventory 耗料实际成本早于对应 MES 报工到达时的 ERP 乱序缓冲。 | 保存 Inventory movement id、MES report no、材料 SKU、带符号数量、移动平均单价与过账时间。 | `organization_id + environment_id + movement_id` 唯一；report no 索引用于报工到达后绑定。 | 报工消费者绑定到真实工单成本明细后删除；不跨 schema 查询，也不把下游标识伪造为工单号。 |
| `opportunities` | business | 销售商机事实。 | `opportunity_no` 是业务编号；`customer_code` 引用 MasterData 客户。 | opportunity no 唯一索引用于 CRM-lite 追踪。 | 创建后作为报价前置事实保留。 |
| `quotations` / `quotation_lines` | business | 销售报价和报价行。 | `quotation_no` 是业务编号；行记录 SKU、数量、单价和交期。 | quotation no 唯一索引用于销售订单创建。 | 报价需显式 approve 才能创建销售订单。 |
| `sales_orders` / `sales_order_lines` / `sales_order_changes` | business | 销售订单、行和不可变变更审计，记录已释放发货数量与未履约行的撤销状态。 | `sales_order_no` 是业务编号；`version` 是单调递增修订号；行记录 ordered/delivered quantity 与 cancelled；change 记录改单/撤单原因和时间。 | SO no 唯一索引用于发货请求反查。 | ERP 只拥有订单事实；仅未履约行可改或撤，整单撤销会通过 WMS 既有出库取消 API 关闭未完成出库并释放库存预留；已取消行不再占用 active SO credit exposure。 |
| `sales_return_authorizations` / `sales_return_authorization_lines` | business | ERP RMA 授权、真实 WMS 入库投影与 Quality 贷项资格事实。 | `rma_no`、源销售行、源 AR、WMS inbound、Quality disposition 和 credit note 编号构成审计链。 | rma no 唯一；非空 WMS inbound no 唯一。 | 仅已发货且未超出 AR open amount 的数量可授权；WMS 收货后由 Quality `passed`/`conditional-release` 决定可贷项，`rejected` 仅记录拒绝。 |
| `delivery_orders` / `delivery_order_lines` | business | ERP 发货请求，供 WMS outbound 执行。 | `delivery_order_no` 是业务编号；头记录 `status`、可选 `cancelled_at_utc`/`cancellation_reason`；行引用 SO line no，并复制 SKU、UOM、location 和可选 lot。 | delivery order no 唯一索引用于 WMS/AR 下游引用；既有记录迁移为 `released`。 | release 后通过公开 WMS outbound-requested 事件请求出库执行；WMS outbound cancellation 事件会回写 ERP delivery 为 `cancelled` 并保留取消原因，不表达 WMS task state。 |
| `account_payables` | business | 应付候选事实。 | `payable_no` 是业务编号；记录来源单据、供应商、币种、金额、已付金额、已应用借项、本位币金额、汇率、发票日、到期日和付款条件。 | AP no 唯一索引用于幂等生成。 | 支付和借项都不允许超过 open amount；付款命令支持一次核销多张 AP、on-account 预付和付款汇率差异，生成本位币平衡凭证及已实现汇兑损益行，并持久化 `payment_executions` 审计单据；供应商发票匹配创建的 AP 通过 GR/IR clearing 入账，采购退货借项以 AP/库存补偿凭证入账。 |
| `payment_executions` / `payment_execution_allocations` | business | AP 付款执行单及核销分配，记录 approved 到 executed 的最小付款执行审计链。 | `payment_execution_no` 是业务编号；头记录 supplier、amount、currency、`payment_exchange_rate`、payment date、cash account、status、approver/executor；分配行记录 payable no 和 amount。 | payment execution no 唯一索引用于付款幂等和凭证追溯；分配行归属索引用于加载核销明细。 | 可先批准付款执行单并进入月结未过账读面；执行时核销 AP open amount，同步账龄读面，并按付款汇率生成付款凭证。 |
| `account_receivables` | business | 应收候选事实。 | `receivable_no` 是业务编号；记录来源单据、客户、币种、金额、已收金额、已应用贷项、本位币金额、汇率、发票日、到期日和付款条件。 | AR no 唯一索引用于幂等生成。 | 收款和贷项都不允许超过 open amount；列表和汇总按币种输出本位币 open amount；RMA Quality 合格后贷项以销售退回/AR 补偿凭证入账，不伪造现金收款。 |
| `debit_notes` / `credit_notes` | business | ERP 供应商借项和客户贷项补偿单据。 | 借项关联 purchase return 与 AP；贷项关联 RMA 与 AR，并记录币种、汇率和本位币金额。 | 借项号、贷项号在组织/环境内唯一；RMA 与贷项一对一。 | 单据和 AP/AR application 与平衡凭证同一 ERP 事务提交，重放不重复冲销。 |
| `cash_receipts` / `cash_receipt_allocations` | business | 现金收款登记单及 AR 匹配分配，记录 registered 到 matched 的审计链。 | `cash_receipt_no` 是业务编号；头记录 customer、amount、currency、receipt date、cash account、status；分配行记录 receivable no 和 amount。 | cash receipt no 唯一索引用于收款幂等和凭证追溯；分配行归属索引用于加载核销明细。 | 可先登记收款并进入月结未过账读面；匹配时核销 AR open amount，同步账龄读面，并生成收款凭证。 |
| `cost_candidates` | business | 成本候选事实，引用 MES、Inventory 或 WMS 公开事实。 | `candidate_no` 是业务编号；`source_type + source_document_no` 描述来源；记录币种、汇率和本位币金额。 | candidate no 唯一索引用于成本候选幂等。 | 作为候选保留，不代表最终成本结转；创建时生成本位币平衡候选成本凭证。 |
| `journal_vouchers` / `journal_voucher_lines` | business | 平衡凭证事实和借贷行。 | `voucher_no` 是业务编号；行记录 account code、document currency debit/credit、exchange rate 和 local currency debit/credit。 | voucher no 唯一索引用于凭证审计。 | posted 后不可变，凭证按本位币借贷平衡；AP/AR/Cost 创建和 AP/AR 清算命令会自动写入最小子分类账凭证；所有已配置会计期间内的凭证会拒绝写入 closed period。 |
| `code_counters` | business | ERP service-local 编码计数器，用于采购、销售和财务普通创建请求自动分配业务 code。 | `organization_id + environment_id + rule_key + site_code + reset_key` 是计数器唯一范围；`current_value` 和 `version` 维护已分配序列与乐观并发。 | 唯一索引保护同一编码规则范围只有一个 counter。 | 创建请求按需创建并递增；不由用户直接维护。 |
| `code_idempotency_keys` | business | ERP 创建请求幂等键记录，把客户端 key 绑定到已分配业务 code 和 payload fingerprint。 | `organization_id + environment_id + rule_key + idempotency_key` 是唯一键；`code` 保存首次分配结果。 | 唯一索引阻止同一 key 在同一规则内重复创建不同资源。 | 随创建请求写入，保留用于重试去重和冲突诊断。 |
| `processed_integration_events` | system | ERP 业务 inbox，记录 BusinessApproval 完成事件等消费者已经执行业务副作用的事件。 | `consumer_name + idempotency_key` 是唯一消费边界；`event_id` 仅用于追溯原始发布事件；`source_service + event_type + processed_at_utc` 支撑消费诊断。 | 唯一索引用于 CAP/RabbitMQ/Redis 重投、re-release 和并发消费兜底。 | 随消费者成功处理写入；不建立跨 schema 外键，不删除原始事件事实。 |
| `integration_event_dead_letters` | system | ERP 消费侧在业务处理前拒绝的集成事件，用于版本不兼容、envelope 缺失、来源非法等场景的排查和 replay 标记。 | `id` 为 Guid；`consumer_name`、`event_id`、`event_type`、`event_version`、`source_service`、`idempotency_key`、`status` 和 `event_json` 保留拒绝事实。 | `consumer_name + status + dead_lettered_at_utc` 支撑 pending 队列查看；`consumer_name + event_id` 支撑单事件排查。 | PostgreSQL profile 下由共享 persistent DLQ store 写入；operator replay 后标记 `Replayed`，不删除原始拒绝事实。 |
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
6. `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/Migrations/20260703093236_AddActiveAlarmRuleUniqueness.cs`
7. `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/Migrations/20260707021103_AddDeviceControlTagMetadata.cs`
8. `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/Migrations/20260707120823_AddIndustrialTelemetryHistorian.cs`
9. `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/Migrations/20260708033658_AddHistorianDownsamplingWindowKeys.cs`
10. `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/Migrations/20260709024233_AddDeviceControlCommandLedger.cs`
11. `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/Migrations/20260709054127_AddDeviceControlOutcomeAndDeadLetters.cs`
12. `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/Migrations/20260710035710_AddOeeProductionFactProjection.cs`
13. `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/Migrations/20260710075143_RenameOeeProductionFactSourceReportIndex.cs`
14. `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/Migrations/20260710163723_AddDeviceControlChannelBinding.cs`
15. `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/Migrations/20260711071138_AddDeviceControlReceiptColumns.cs`

| Table | Kind | Purpose | Key columns | Index intent | Lifecycle |
| --- | --- | --- | --- | --- | --- |
| `telemetry_tags` | business | IndustrialTelemetry 拥有的设备采集 tag 映射、采样策略和可写控制值域元数据。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + device_asset_id + tag_key` 是业务唯一键；`value_type`、`unit_code` 和 `sampling_policy` 描述采集口径；`is_writable`、`control_min_value`、`control_max_value`、`control_allowed_values_json` 描述设备控制命令下发前的允许值域。 | tag 唯一索引防重复映射；设备/tag 维度支持采集配置查询和控制命令值域校验。 | 创建后保留为采集与控制元数据；PLC/DCS/SCADA 凭据不进入本 schema。 |
| `alarm_rules` | business | IndustrialTelemetry 拥有的报警规则阈值配置，记录设备、tag、比较符、阈值、单位、严重级别、独立优先级、死区、开/关延时、最小持续时间和启停状态。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + device_asset_id + rule_code` 是业务唯一键；`deadband_value`、`on_delay_seconds`、`off_delay_seconds`、`min_duration_seconds` 和 `priority` 支撑 ISA-18.2 风格去抖与优先级。 | 规则唯一索引防重复；设备+tag 索引用于规则维护和受控评估查询。 | 规则配置可更新启停、阈值、去抖参数和优先级；不保存 PLC/DCS/SCADA 控制命令或凭据。 |
| `device_state_snapshots` | business | 设备状态快照事实，记录设备状态、发生时间、来源系统/连接器和来源序列。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + source_system + source_connector + device_asset_id + source_sequence` 是幂等唯一键，且 source 元数据为空时按 not-distinct 参与唯一性；`occurred_at_unix_time_milliseconds` 和 `recorded_at_unix_time_milliseconds` 是由对应 UTC timestamp 派生的内部排序键。 | 来源 scope+序列唯一索引防重复写入；设备+时间索引用于时间线、current-state 和 runtime availability 查询；设备+发生/记录 Unix ms+source sequence 索引用于 provider-neutral current-state 排序和 DeviceStateChanged 发布门控。 | 只追加受控状态事实，不表达控制命令。 |
| `oee_production_facts` | business | IndustrialTelemetry 从 MES `mes.ProductionReportRecorded` 消费的报工投影，用于可解释 OEE 性能率与质量率，不跨 schema 查询 MES。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + source_report_no` 是投影幂等唯一键；保存 work center/device 快照、良品/报废/返工、单位、理论速率与报工时间，冲销报工以负数量作为独立事实。 | 报工号唯一索引抑制 CAP 重投；组织/环境+设备+报工时间索引支撑 OEE 窗口聚合。 | 由 MES 事件追加投影；不反向修改 MES 报工，缺少已分派设备或工作中心的事件不创建设备级事实，读面会报告生产事实缺失的降级状态。 |
| `alarm_events` | business | 工业报警生命周期事实，记录 raise、ack、shelve、escalation、clear、严重级别、独立优先级、触发 tag、观测值、阈值、单位和外部报警 ID。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + device_asset_id + alarm_code + external_alarm_id` 是非 cleared 状态下的外部报警幂等键；规则报警额外用 `organization_id + environment_id + device_asset_id + tag_key + external_alarm_id` 约束同一规则/tag 只能存在一个 active 事件；`status` 表示 `raised` / `acknowledged` / `shelved` / `cleared`；`acknowledged_at_utc`、`acknowledged_by`、`shelved_at_utc`、`shelved_until_utc`、`shelved_by`、`shelve_reason`、`escalated_at_utc`、`escalation_reason`、`escalation_recipient_refs` 记录处置生命周期；`tag_key`、`observed_value`、`threshold_value`、`unit_code` 为 RCA 上下文字段。 | 外部报警 active 唯一索引防重复 raise；规则 active 唯一索引兜底并发/规则定义变更时的同条件重复报警；设备+时间索引用于报警时间线和 RCA 查询。 | 报警创建后可确认、限时搁置、到期/手工解除搁置、一次性升级和清除；清除只补充 cleared facts，不删除历史；升级通过 `industrialTelemetry.AlarmEscalated` 交给 Notification 投递。 |
| `telemetry_summaries` | business | 粗粒度采集汇总 bucket，保存 tag 数值摘要和可选来源系统/连接器元数据，兼容报警规则评估和旧数据面。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + source_system + source_connector + device_asset_id + tag_key + source_sequence` 是幂等唯一键，且 source 元数据为空时按 not-distinct 参与唯一性；`sample_count`、`min_value`、`max_value`、`average_value` 保存摘要；`bucket_end_unix_time_milliseconds` 是由 `bucket_end_utc` 派生的内部排序/迟到桶判断键。 | 来源 scope+序列唯一索引防重复；设备+tag+bucket 起点索引用于兼容查询；设备+tag+bucket end Unix ms 索引用于 provider-neutral 去抖连续性和 late-bucket 抑制查询。 | 可由 raw historian 重算；保留用于兼容当前报警和旧接口语义。 |
| `telemetry_raw_samples` | business | historian raw 层采样明细 bucket，保存采集端最细粒度写入的 min/max/avg/first/last 和来源元数据。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + source_system + source_connector + device_asset_id + tag_key + source_sequence` 是幂等唯一键，且 source 元数据为空时按 not-distinct 参与唯一性；`bucket_start_utc`、`bucket_end_utc`、`hourly_window_start_utc` 和 `bucket_end_unix_time_milliseconds` 定位采样窗口与 raw→hourly 降采样窗口；`sample_count`、`min_value`、`max_value`、`average_value`、`first_value`、`last_value` 保存 raw bucket 明细。 | 来源 scope+序列唯一索引防重复写入；设备+tag+bucket 起点索引用于历史明细查询；org/env+hourly window+设备+tag 索引用于 indexed anti-join 查找未生成 hourly rollup 的窗口；设备+tag+bucket end Unix ms 索引用于 retention、迟到桶和范围扫描。 | raw 层按保留策略清理；hourly/daily rollup 生成后可缩短 raw 保留。 |
| `telemetry_rollups` | business | historian hourly/daily 降采样结果，保存 min/max/加权 avg/first/last。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + device_asset_id + tag_key + grain + window_start_utc` 是 rollup 唯一键；`grain` 为 `Hourly` 或 `Daily`；`window_start_utc`、`window_end_utc`、`daily_window_start_utc` 和 `window_end_unix_time_milliseconds` 定位汇总窗口与 hourly→daily 降采样窗口；`source_sequence` 是 deterministic historian 序列。 | 设备+tag+grain+window 起点唯一索引防重复 downsampling；org/env+grain+daily window+设备+tag 索引用于 indexed anti-join 查找未生成 daily rollup 的日期；设备+tag+grain+window end Unix ms 索引用于历史查询和 retention 清理。 | hourly 和 daily 层分别按保留策略清理；不跨 schema 引用设备主数据。 |
| `device_control_channel_bindings` | business | 设备控制通道绑定，显式登记每台设备的控制命令下发目标（连接器主机 + 连接器实例），使命令下发在服务端按设备解析路由目标，操作员不手输基础设施标识（MAN-438/#792）。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + device_asset_id` 是业务唯一键（一设备一绑定）；`connector_host_id`/`instance_key` 是命令下发的路由目标；`is_active` 表示绑定是否可用于下发；`disabled_reason` 保存停用原因用于审计。 | org/env+设备唯一索引防重复绑定并支撑创建命令时按设备解析。 | 由运维维护 upsert；停用为软停用（`is_active=false` + 记录原因）；命令下发时按 `(org, env, device)` 解析，缺绑定或已停用则拒绝下发；PLC/DCS/SCADA 凭据不进入本 schema。 |
| `device_control_commands` | business | 设备控制命令台账，投影 Ops operation task 的下发事实，记录设备、命令类型、tag/值或参数集、请求人、原因、幂等/关联键、Ops operation task id、下发时状态/审批快照和终态结果，支撑命令 result/history 读面（#842/#838）。 | `id` 为 Guid v7 强类型 ID；`operation_task_id` 是 Ops 命令的唯一自然键，也是读面按 commandId 定位的外部键；`command_type` 为 `write-tag`/`start-stop`/`parameter-set`；`tag_key`/`value` 用于单 tag 命令，`parameters_json` 保存参数集命令输入；`status`/`approval_status` 初值是下发时 Ops 快照，单命令读面实时从 Ops 刷新状态；`approval_status` 在终态由消费者投影为 `approved`/`rejected`；`finished_at_utc`/`failure_code` 由 Ops OperationTaskCompleted/Failed/ApprovalRejected 消费者在终态写入（rejected 也投影，MAN-438/#792）；`device_receipt_code`/`device_receipt_message` 由 completed/failed 消费者回拉 Ops task attempt 输出的设备回执（如 Good/BadOutOfRange）持久化，供历史行展示真实设备回执而非 Ops 通用码；`connector_host_id`/`instance_key` 由 `device_control_channel_bindings` 解析，非请求输入；`requested_at_unix_time_milliseconds` 是由 `requested_at_utc` 派生的 provider-neutral 历史范围过滤/排序键。 | `operation_task_id` 唯一索引防重复台账并支撑幂等下发；org/env+时间索引与 org/env+设备+时间索引支撑历史按设备/时间过滤分页。 | 下发时追加台账事实；`status` 由 Ops OperationTaskCompleted/Failed 消费者幂等推进到 `completed`/`failed` 终态（首个终态定格），使历史按状态过滤反映真实结果；命令执行进度仍以 Ops operation task 为权威；PLC/DCS/SCADA 凭据或连接 payload 不进入本 schema。 |
| `integration_event_dead_letters` | system | 消费前被拒绝（信封/版本校验失败）的集成事件死信记录，用于设备控制 Ops 结果消费者的 replay/triage。 | 由 `Nerv.IIP.Messaging.CAP.EntityFrameworkCore` 的 `ConfigureIntegrationEventDeadLetters()` 统一配置；`consumer_name`、`event_type`、`event_version`、`event_json`、`failure_code`、`status` 记录死信上下文与状态。 | `consumer_name+status+dead_lettered_at_utc` 与 `consumer_name+event_id` 索引用于死信查询与 replay。 | 仅在消费前拒绝时追加；业务代码不直接读写，经统一 DLQ store/管理入口 replay。 |
| `CAPLock` | system | CAP distributed lock table，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部协调。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `CAPPublishedMessage` | system | CAP published message outbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部投递扫描。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `CAPReceivedMessage` | system | CAP received message inbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部消费幂等。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `__EFMigrationsHistory` | system | EF Core migration history table，记录 BusinessIndustrialTelemetry 已应用迁移。 | `MigrationId + ProductVersion` 由 EF Core 维护。 | EF Core 用于判定待应用迁移。 | 必须位于 `industrial_telemetry` schema；业务代码不直接读写。 |

`device_state_snapshots`、`telemetry_summaries` 和 `telemetry_raw_samples` 的 `source_system`、`source_connector` 是 #207 运行事实幂等 scope 的可选来源元数据列；唯一索引把 `organization_id`、`environment_id`、来源元数据、设备和 `source_sequence` 组合为幂等边界，telemetry summary/raw sample 额外包含 `tag_key`。

## BusinessMaintenance Schema

Schema: `maintenance`

Owner: `backend/services/Business/Maintenance`

Source:

1. `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/ApplicationDbContext.cs`
2. `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/EntityConfigurations/MaintenanceEntityTypeConfigurations.cs`
3. `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/Migrations/20260523112317_InitialMaintenanceSchema.cs`
4. `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/Migrations/20260525050928_AddMaintenanceIntegrationEventDeadLetters.cs`
5. `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/Migrations/20260601032417_AddMaintenancePlanRuntimeWindow.cs`
6. `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/Migrations/20260601083444_AddMaintenanceRuntimeWindowQueryIndex.cs`
7. `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/Migrations/20260609061021_AddMaintenanceConsumerInboxIdempotency.cs`
8. `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/Coding/CodeEntityTypeConfigurations.cs`
9. `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/Migrations/20260615020603_AddMaintenanceCodingTables.cs`
10. `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/Migrations/20260621114259_AddMaintenanceInspectionWorkOrderSource.cs`
11. `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/Migrations/20260624150948_UseIdempotencyKeyForProcessedIntegrationEvents.cs`
12. `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/Migrations/20260705132948_AddMaintenanceMeasurementsLaborCosts.cs`

| Table | Kind | Purpose | Key columns | Index intent | Lifecycle |
| --- | --- | --- | --- | --- | --- |
| `maintenance_work_orders` | business | 维修工单、报警引用、PM 来源、点检失败来源、诊断描述、结构化 failure mode/cause、指派技师、工时、费用、有效修复开始时间、报警恢复标记、设备不可用和完工事实。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + source_alarm_id` 防止同一报警重复开单；`source_plan_code` 记录 PM 自动开单来源；`source_type + source_reference_id` 记录 alarm、plan、inspection 等来源事实引用；`diagnostic_description` 保存自动开单诊断文本；`failure_mode_code`、`failure_cause_code` 支撑 ISO 14224 风格故障分类；`assigned_technician_user_id` 记录外部用户引用；`estimated_labor_minutes`、`actual_labor_minutes`、`spare_part_cost_amount`、`external_service_cost_amount` 和 `cost_currency_code` 支撑维修人力与费用统计；`repair_started_at_utc` 支撑有效修复段 MTTR；`alarm_cleared` / `alarm_cleared_at_utc` 记录报警已解除但待维修确认状态；`device_asset_id` 引用 MasterData 设备。 | source alarm 唯一索引用于报警幂等；`ux_maintenance_work_orders_source_reference` 防止同一来源事实重复开单；状态/设备字段支撑维修看板查询；设备、技师、时间窗用于可靠性和工时费用统计。 | 手工、报警、PM 到期、用量到期或点检失败生成；完成后保留停机、修复段、实际工时、费用汇总和备件引用事实。 |
| `maintenance_work_order_spare_part_lines` | business | 维修工单备件需求行，只记录需求和用量事实，不维护库存余额。 | `id` 为 Guid v7 强类型 ID；`maintenance_work_order_id` 归属工单；`sku_code`、`quantity`、`uom_code` 描述备件。 | 工单外键索引用于加载备件行。 | 工单完工时由 Maintenance 发布 `inventory.InventoryMovementRequested` 出库请求，Inventory 负责扣减库存。 |
| `maintenance_plans` | business | 预防性维护计划、保养周期、下一次到期日、可选运行小时触发阈值和可选 runtime availability 维护窗口事实。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + plan_code` 是业务唯一键；`interval`、`starts_on`、`last_generated_on`、`next_due_on`、`runtime_hour_interval`、`last_generated_runtime_hours`、`next_due_runtime_hours`、`owner`、`window_start_utc` 和 `window_end_utc` 描述计划、PM 生成状态、用量触发状态与维护窗口。 | 计划编码唯一索引用于防重复；设备与窗口字段支撑 availability 查询；`next_due_on` 支撑 PM 到期扫描；运行小时字段支撑用量触发 PM 追赶。 | 创建后作为计划事实保留；PM 调度支持 ISO day interval（如 `P7D`）按原 `next_due_on` 追赶多周期，也支持运行小时阈值触发；窗口边界必须成对提供并按 UTC 保存；版本化/暂停策略由后续切片补齐。 |
| `code_counters` | business | Maintenance service-local 编码计数器，用于保养计划等普通创建请求自动分配业务 code。 | `organization_id + environment_id + rule_key + site_code + reset_key` 是计数器唯一范围；`current_value` 和 `version` 维护已分配序列与乐观并发。 | 唯一索引保护同一编码规则范围只有一个 counter。 | 创建请求按需创建并递增；不由用户直接维护。 |
| `code_idempotency_keys` | business | Maintenance 创建请求幂等键记录，把客户端 key 绑定到已分配业务 code 和 payload fingerprint。 | `organization_id + environment_id + rule_key + idempotency_key` 是唯一键；`code` 保存首次分配结果。 | 唯一索引阻止同一 key 在同一规则内重复创建不同资源。 | 随创建请求写入，保留用于重试去重和冲突诊断。 |
| `maintenance_inspections` | business | 点检记录，可关联维护计划或维修工单。 | `id` 为 Guid v7 强类型 ID；`maintenance_plan_id`、`maintenance_work_order_id` 是业务引用；`inspector`、`result` 和 `inspected_at_utc` 保存执行事实。 | 计划/工单引用支持追溯点检记录；`organization_id + environment_id + inspected_at_utc` 支撑点检测量趋势查询。 | 点检写入后不可覆盖历史，只通过新记录表达新检查。 |
| `maintenance_inspection_measurements` | business | Maintenance 点检测量值行，记录特性、数值、单位、可接受范围和判定结果。 | `id` 为 Guid v7 强类型 ID；`maintenance_inspection_id` 归属点检记录；`characteristic_code`、`measured_value`、`uom_code`、`lower_spec_limit`、`upper_spec_limit` 和 `is_within_spec` 保存 Maintenance 自有测量事实。 | 点检外键索引用于加载测量行；特性索引用于按设备和特性查询趋势。 | 随点检记录追加写入并随点检级联删除；不引用 Quality 内部规格表，趋势和判定由 Maintenance 自有事实表达。 |
| `downtime_reasons` | business | 维护域拥有的停机原因代码表，包含 RCA 原因分类和 TPM/OEE 损失分类。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + reason_code` 是业务唯一键；`reason_category` 表示原因归类；`loss_category` 表示六大损失或等价 OEE 损失分类。 | 原因码唯一索引防重复；完工命令必须校验同组织/环境原因码存在。 | 作为归因基础数据保留；通过 Maintenance create/list endpoint 维护；删除/失效策略后续补齐。 |
| `processed_integration_events` | system | Maintenance 业务 inbox，记录报警自动开单消费者已经执行业务副作用的事件。 | `consumer_name + idempotency_key` 是唯一消费边界；`event_id` 仅用于追溯原始发布事件；`source_service + event_type + processed_at_utc` 支撑消费诊断。 | 唯一索引用于 RabbitMQ/CAP 重投、re-release 和并发消费兜底。 | 随消费者成功处理写入；不建立跨 schema 外键，不删除原始事件事实。 |
| `integration_event_dead_letters` | system | Maintenance 消费侧在业务处理前拒绝的集成事件，用于版本不兼容、envelope 缺失等场景的排查和 replay 标记。 | `id` 为 Guid v7；`consumer_name`、`event_id`、`event_type`、`event_version`、`status` 和 `event_json` 保留拒绝事实。 | `consumer_name + status + dead_lettered_at_utc` 支撑 pending 队列查看；`consumer_name + event_id` 支撑单事件排查。 | 由 Maintenance 消费 guard 写入；operator replay 后标记 `Replayed`，不删除原始拒绝事实。 |
| `CAPLock` | system | CAP distributed lock table，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部协调。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `CAPPublishedMessage` | system | CAP published message outbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部投递扫描。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `CAPReceivedMessage` | system | CAP received message inbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义。 | CAP 内部消费幂等。 | 系统表随服务数据库迁移创建；业务代码不直接读写。 |
| `__EFMigrationsHistory` | system | EF Core migration history table，记录 BusinessMaintenance 已应用迁移。 | `MigrationId + ProductVersion` 由 EF Core 维护。 | EF Core 用于判定待应用迁移。 | 必须位于 `maintenance` schema；业务代码不直接读写。 |

`maintenance_plans.window_start_utc` 与 `maintenance_plans.window_end_utc` 是 #207 设备 runtime availability 的可选维护窗口列；窗口边界必须成对提供，且按 UTC 存储。`maintenance_plans.next_due_on` 与 `last_generated_on` 支撑 #416 PM 到期自动开单；迁移会把已有计划的 `next_due_on` 回填为 `starts_on`。#554 起，PM 生成按原 `next_due_on` 多周期追赶，`runtime_hour_interval`、`last_generated_runtime_hours` 和 `next_due_runtime_hours` 支撑运行小时用量触发。

## BusinessScheduling Schema

Schema: `scheduling`

Owner: `backend/services/Business/Scheduling`

Source:

1. `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/ApplicationDbContext.cs`
2. `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/SchedulingPersistenceServiceCollectionExtensions.cs`
3. `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/EntityConfigurations/*.cs`
4. `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/Migrations/20260531111947_InitialSchedulingSchema.cs`
5. `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/Migrations/20260626170713_AddSchedulingInputChangeInvalidations.cs`
6. `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/Migrations/20260707042000_AddScheduleAssignmentStandardOperationCode.cs`

| Table | Kind | Purpose | Key columns | Index intent | Lifecycle |
| --- | --- | --- | --- | --- | --- |
| `schedule_problems` | business | APS lite 排程问题快照，保留输入 problem id、组织/环境、计划窗口和 deterministic fingerprint。 | `id` 为 Guid v7 强类型 ID；`organization_id + environment_id + problem_id` 是排程问题幂等范围；`problem_fingerprint` 记录输入快照指纹；`horizon_start_utc` / `horizon_end_utc` 记录排程窗口。 | `organization_id + environment_id + problem_id` 唯一索引用于同一业务上下文内重放、幂等诊断和从 plan 追溯输入。 | 生成排程方案时捕获并保留；不由下游服务直接修改。 |
| `schedule_plans` | business | APS lite 排程方案头，记录生成/发布状态、算法版本、合同版本、问题指纹和方案 KPI。 | `id` 为 Guid v7 强类型 ID；`plan_id` 是公开方案 ID；`problem_id` 和 `problem_fingerprint` 指向输入快照；`status` 表示 generated/released 等生命周期；`scheduled_operation_count`、`unscheduled_operation_count`、`assigned_minutes`、`makespan_minutes`、`total_tardiness_minutes`、`late_operation_count`、`on_time_rate`、`average_resource_utilization` 保存生成时的排程评审指标，其中 `assigned_minutes` 为资源占用分钟，包含加工时间和同一资源前序分配后产生的 setup/changeover 时间。 | `plan_id` 唯一索引用于 detail、Gantt 和 release 查询。 | 生成后可发布；发布后作为 MES、Gantt 和 APS KPI 看板消费的稳定事实保留。 |
| `schedule_plan_assignments` | business | 排程方案内的工序到资源分配结果。 | `schedule_plan_id` 归属方案；`assignment_id` 是方案内公开分配 ID；`work_order_id`、`operation_id`、`standard_operation_code`、`resource_id`、`work_center_id` 和 `start_utc` / `end_utc` 描述分配事实；`standard_operation_code` 保留 ProductEngineering 标准工序码，供 MES 下发当前 SOP/电子作业指导书。 | `schedule_plan_id + assignment_id` 唯一；`schedule_plan_id` 外键索引用于按方案加载。 | 生命周期随方案保留；不直接表达 MES 工序执行状态。 |
| `schedule_plan_resource_loads` | business | 排程方案的资源负载窗口和利用率。 | `schedule_plan_id` 归属方案；`resource_id`、`window_start_utc`、`window_end_utc`、`assigned_minutes`、`available_minutes` 和 `utilization` 描述负载；`assigned_minutes` 同样使用资源占用口径，包含加工时间和 setup/changeover 时间。 | `schedule_plan_id` 外键索引用于读取方案负载。 | 由排程算法生成并随方案保留，可供产能/Gantt 查询。 |
| `schedule_plan_conflicts` | business | 排程方案生成过程中识别的交期、产能、日历、物料、质量或设备冲突。 | `schedule_plan_id` 归属方案；`conflict_id` 是公开冲突 ID；`reason_code`、`severity`、`work_order_id`、`operation_id`、`resource_id` 和 `message` 描述冲突。 | `schedule_plan_id` 外键索引用于按方案加载冲突。 | 生成时追加到方案；用于解释和前端展示，不替代下游执行异常。 |
| `schedule_plan_unscheduled_operations` | business | 在当前 horizon 内无法安排的工序及原因。 | `schedule_plan_id` 归属方案；`work_order_id`、`operation_id`、`reason_code` 和 `message` 描述不可排结果。 | `schedule_plan_id` 外键索引用于按方案加载不可排清单。 | 生成时写入并随方案保留；人工调整或新事实输入通过新方案表达。 |
| `schedule_plan_invalidations` | business | Scheduling 本地事件驱动重排信号投影，记录 Maintenance/Inventory/Quality/MES 事实变化对 generated 方案的失效影响。 | `id` 为 Guid v7 强类型 ID；`organization_id`、`environment_id`、`plan_id` 定位被影响方案；`source_event_type`、`source_event_id`、`source_service` 记录上游事实；`reason_code` 记录 equipment/material/quality/work-order 失效原因；`affected_resource_id`、`affected_work_order_id`、`affected_operation_id`、`affected_sku_code` 保存可用的影响范围。 | `organization_id + environment_id + plan_id + source_event_type + source_event_id` 唯一索引用于消费幂等；`organization_id + environment_id + plan_id + recorded_at_utc` 用于按方案读取待重排原因。 | 由 Scheduling 事件消费者写入；只标记 generated 方案需要重排或失效，不修改 released 方案事实。 |
| `processed_integration_events` | system | Scheduling 消费上游集成事件的业务 inbox，按 consumer 和 idempotency key 保证事件处理幂等。 | `id` 为 Guid v7 强类型 ID；`ConsumerName + IdempotencyKey` 是幂等键；`EventId`、`EventType`、`EventVersion`、`SourceService`、`ProcessedAtUtc` 保留消费审计。 | `ConsumerName + IdempotencyKey` 唯一；`SourceService + EventType + ProcessedAtUtc` 支撑消费诊断。 | 由 Scheduling CAP consumers 写入；业务代码不把它作为领域事实。 |
| `integration_event_dead_letters` | system | Scheduling 消费上游集成事件时被 envelope/version/type 校验拒绝的消息留存，用于后续人工诊断和 replay。 | `id` 为消息 ID；`consumer_name`、`event_id`、`event_type`、`event_version`、`source_service`、`idempotency_key`、`failure_code`、`failure_message`、`status` 和时间戳描述拒绝原因。 | `consumer_name + status + dead_lettered_at_utc` 支撑待处理 DLQ 查询；`consumer_name + event_id` 支撑单事件定位。 | 由通用 `Nerv.IIP.Messaging.CAP.EntityFrameworkCore` 持久化；业务 handler 不直接修改事件 payload。 |
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
6. `backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/Migrations/20260609060902_AddAppHubConsumerInboxIdempotency.cs`
7. `backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/Migrations/20260624150915_UseIdempotencyKeyForProcessedIntegrationEvents.cs`
8. `backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/Migrations/20260706064319_AddConnectorHostHeartbeatLifecycle.cs`

| Table | Kind | Purpose | Key relationships and indexes |
| --- | --- | --- | --- |
| `applications` | business | 应用目录聚合根，记录 organization/environment 范围内的应用键和显示名称。 | `Id` 为 Guid v7 强类型 ID；`OrganizationId + EnvironmentId + ApplicationKey` 唯一；级联拥有 `application_versions`。 |
| `application_versions` | business | 应用版本子实体，记录一个应用已注册的版本号。 | `ApplicationId` 指向 `applications`；`ApplicationId + Version` 唯一。 |
| `managed_nodes` | business | Connector host 或受管节点目录，记录节点键、名称和部署形态。 | `Id` 为 Guid v7 强类型 ID；`OrganizationId + EnvironmentId + NodeKey` 唯一。 |
| `application_instances` | business | 应用实例聚合根，记录实例键、版本、节点、状态、健康、Connector Host 归属和 connector 上报扩展信息。 | `InstanceKey` 唯一；`OrganizationId + EnvironmentId + ApplicationKey` 支持按应用查实例；`OrganizationId + EnvironmentId + ConnectorHostId` 支持按 Connector Host 扫描心跳生命周期；拥有 heartbeat、状态历史和状态变化。 |
| `instance_heartbeat` | business | 应用实例最近一次心跳事实。 | `ApplicationInstanceId` 唯一，和 `application_instances` 一对一。 |
| `instance_state_history` | business | 应用实例状态观测历史，用于状态追踪、诊断和后续告警分析。 | `ApplicationInstanceId + ObservedAtUtc` 支持按实例时间线查询。 |
| `instance_status_changes` | business | 应用实例状态转换历史，记录 previous/current status 和变更时间。 | `ApplicationInstanceId + ChangedAtUtc` 支持按实例状态变更时间线查询。 |
| `registration_idempotency` | business | 注册请求幂等记录，避免 connector 重试导致重复实例注册。 | `IdempotencyKey` 唯一；记录 `RegistrationId` 和 `InstanceKey`。 |
| `cap_published_messages` | system | CAP published message outbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义；业务代码不直接读写。 |
| `cap_received_messages` | system | CAP received message inbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义；用于消费幂等和重试。 |
| `cap_locks` | system | CAP distributed lock table，由 netcorepal/CAP 基础设施维护。 | 主键 `Key`；用于 CAP 内部协调。 |
| `processed_integration_events` | system | AppHub 业务 inbox，记录 Ops operation task 完成/失败刷新消费者已经处理的事件。 | `consumer_name + idempotency_key` 唯一；`event_id` 仅用于追溯原始发布事件；`source_service + event_type + processed_at_utc` 支持消费诊断。 |
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
6. `backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/Migrations/20260626130722_AddOpsAuditHashChain.cs`
7. `backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/Migrations/20260707020801_PersistOperationAttemptOutput.cs`

| Table | Kind | Purpose | Key relationships and indexes |
| --- | --- | --- | --- |
| `operation_tasks` | business | 运维操作任务聚合根，记录目标实例、操作码、请求人、幂等范围、参数、审批状态和当前执行状态。 | `Id` 为业务生成 string 强类型 ID；`IdempotencyScope` 唯一；`OrganizationId + EnvironmentId + Status + RequestedAtUtc` 支持任务列表、审批队列和状态扫描。 |
| `operation_attempts` | business | 操作任务执行尝试，记录 connector host 领取、开始、完成、失败原因和连接器输出回执。 | `OperationTaskId` 指向 `operation_tasks`；`OutputJson` 保存 Connector Host 返回的设备回执、写入值等输出元数据；索引用于按任务查执行历史。 |
| `audit_records` | business | 操作任务审计记录，记录组织/环境 scope、动作、操作者、发生时间、correlation id、`SequenceNo`、`PreviousIntegrityHash` 和 `IntegrityHash`。 | `OperationTaskId + OccurredAtUtc` 支持按任务时间线展示审计；`OrganizationId + EnvironmentId + SequenceNo` 唯一约束保证同 scope 审计链序号不重复；`IntegrityHash` 对不可变审计字段、序号和前序哈希做 SHA-256 摘要，`PreviousIntegrityHash` 将记录链接为可校验链。 |
| `cap_published_messages` | system | CAP published message outbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义；业务代码不直接读写。 |
| `cap_received_messages` | system | CAP received message inbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义；用于消费幂等和重试。 |
| `cap_locks` | system | CAP distributed lock table，由 netcorepal/CAP 基础设施维护。 | 主键 `Key`；用于 CAP 内部协调。 |
| `__EFMigrationsHistory` | system | EF Core migration history table，记录 Ops 已应用迁移。 | 必须位于 `ops` schema；业务代码不直接读写。 |

Status value sources:

1. `operation_tasks.Status` 当前由 `OperationTask` 聚合行为维护：`approval-pending`、`queued`、`dispatched`、`completed`、`failed`、`rejected`。
2. `operation_templates.RequiresApproval=true` 的高风险动作会先进入 Ops 自有 approval gate；审批通过后才可被 Connector Host claim，审批拒绝后进入 `rejected` 终态。
3. Ops 审批决策 actor 由服务端认证/转发上下文派生，不信任请求 body 的 `Actor`；请求人不得审批或拒绝自己发起的高风险动作。
4. `operation_attempts.Status` 当前由 `OperationAttempt` 维护：`started`、`completed`、`failed`。
5. Connector Protocol 的 `OperationResult.ExecutionStatus=succeeded` 映射为 Ops task/attempt 的 `completed`；其它失败结果映射为 `failed`。

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
5. `backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/Migrations/20260628081213_AddIamSecurityAuditRecords.cs`
6. `backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/Migrations/20260705132145_AddIamUserLifecyclePasswordPolicy.cs`

| Table | Kind | Purpose | Key relationships and indexes |
| --- | --- | --- | --- |
| `organizations` | business | IAM 组织范围事实，用于租户与访问 scope 的基础边界。 | `Id` 为调用方提供的有界 string 强类型 ID；包含组织名称、状态、软删除和 row version。 |
| `environments` | business | IAM 环境范围事实，用于把 membership、credential 和后续资源访问限制在组织内环境。 | `OrganizationId + Id` 唯一；`OrganizationId` 是跨表业务引用，不通过跨聚合外键扩大服务耦合。 |
| `users` | business | 后台用户认证事实，记录 login name、email、password hash、启用状态、security stamp、permission version、账号有效期、密码变更/过期/强制改密状态、登录时间、失败计数、最近失败时间和锁定截止时间。 | `LoginName` 唯一；`Email` 唯一；`AccountExpiresAtUtc` 支持扫描即将过期或已过期账号；`Id` 为调用方提供的有界 string 强类型 ID。 |
| `user_password_history` | business | 用户历史密码摘要，支撑密码策略中的历史密码禁复用。 | `UserId` 指向 `users`；`UserId + CreatedAtUtc` 支持按用户读取和裁剪最近历史记录。 |
| `roles` | business | IAM 角色事实，用于把权限码分组后授予 membership。 | `RoleName` 唯一；拥有 `role_permissions`。 |
| `role_permissions` | business | 角色拥有的权限码集合。 | `RoleId` 指向 `roles`；`RoleId + PermissionCode` 唯一。 |
| `role_data_scopes` | business | 角色拥有的行级数据范围集合，用于声明 site/workshop/production-line 可见范围。 | `RoleId` 指向 `roles`；`RoleId + ScopeType + ScopeCode` 唯一；`ScopeCode` 引用 MasterData 组织层级代码，不建立跨服务外键。 |
| `memberships` | business | 用户在 organization/environment scope 内的成员身份。 | `UserId + OrganizationId + EnvironmentId` 唯一；拥有 `membership_roles`。 |
| `membership_roles` | business | membership 绑定的角色集合。 | `MembershipId` 指向 `memberships`；`MembershipId + RoleId` 唯一。 |
| `membership_data_scopes` | business | 用户 membership 拥有的行级数据范围集合，用于声明某用户在特定 organization/environment 下的 site/workshop/production-line 可见范围。 | `MembershipId` 指向 `memberships`；`MembershipId + ScopeType + ScopeCode` 唯一；和角色范围在授权检查时取并集。 |
| `user_sessions` | business | 用户 refresh session，保存 refresh token hash、token family/previous session lineage、issue/expiry/revoke 时间、permission version、client info、IP、认证方式、外部 provider/subject 和 MFA 验证时间。 | `RefreshTokenHash` 支持 refresh lookup；`TokenFamilyId` 支持 refresh token reuse/replay 后整族级联撤销；`PreviousSessionId` 支持轮换 lineage 追溯；`UserId + RevokedAtUtc` 支持按用户扫描活动/撤销会话；`ExternalProvider + ExternalSubject` 支持 SSO session binding 查询。 |
| `connector_host_credentials` | business | Connector Host 机器身份凭据，记录 connector host id、organization/environment、secret hash 和有效期。 | `ConnectorHostId` 唯一；拥有 `connector_host_credential_capabilities`。 |
| `connector_host_credential_capabilities` | business | Connector Host credential 被授予的能力码集合。 | `ConnectorHostCredentialId` 指向 `connector_host_credentials`；`ConnectorHostCredentialId + CapabilityCode` 唯一。 |
| `external_clients` | business | 外部系统或平台应用的 client_credentials 身份，保存 client id、display name、organization/environment、secret hash、启用状态、permission version 和有效期。 | `ClientId` 唯一；secret 只保存 hash。 |
| `authorization_grants` | business | 非用户主体的授权授予事实，首批覆盖 `external-client` 的 permission grant，并支持 resource type/resource id 范围。 | `PrincipalType + PrincipalId + OrganizationId + EnvironmentId + PermissionCode + ResourceType + ResourceId` 唯一；`*` 表示 wildcard；支持有效期和撤销时间。 |
| `security_audit_records` | business | IAM 安全审计记录，覆盖认证决策、会话吊销和角色权限管理变更。 | `Id` 为 `audit-` 前缀 string 强类型 ID；`OrganizationId + EnvironmentId + OccurredAtUtc` 支持租户范围审计查询；`Action + OccurredAtUtc` 支持按安全事件类型查询；`TargetType + TargetId + OccurredAtUtc` 支持按用户、会话或角色追溯；`DetailsJson` 保存登录失败计数、吊销原因和权限前后值等结构化事实。 |
| `seed_manifests` | business | IAM seed 执行清单，用于记录初始 admin、platform admin role、seed permissions、membership 和 local Connector Host credential seed 的版本化幂等执行。 | `SeedName + SeedVersion` 唯一；记录 owner service 与 applied time。 |
| `__EFMigrationsHistory` | system | EF Core migration history table，记录 IAM 已应用迁移。 | 必须位于 `iam` schema；业务代码不直接读写。 |

Known gaps:

1. Gateway-wide permission enforcement 已覆盖现有 Console API；Gateway 转发 bearer token 与 permission/context 到 IAM internal authorization check endpoint，不直接读取 IAM schema。
2. ExternalClient 当前首批覆盖 seed 驱动的 `client_credentials` 发 token 与 AuthorizationGrant 权限检查闭环；P2 已补资源范围 ABAC grant enforcement。IAM 另保留 Development-only 外部 OIDC callback/MFA hook 作为企业身份联调入口，非 Development 环境不接受明文 OIDC body 身份、callback secret 或静态 MFA development code 换发用户会话；当前不包含完整 OAuth/OIDC 授权码服务器、生产 IdP 验签、生产 MFA、动态客户端注册 UI 或 consent 流程。
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
6. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/Migrations/20260705065020_AddFileStorageSecurityHardening.cs`

| Table | Kind | Purpose | Key relationships and indexes |
| --- | --- | --- | --- |
| `stored_files` | business | FileStorage 已完成文件的公开元数据、内部对象定位、扫描结果和生命周期清理事实。 | `file_id` 为业务生成 string ID；`object_key` 唯一且仅限内部持久化；`scan_status`、`scanned_at_utc` 和 `scan_detail` 记录后台扫描结果；`deleted_at_utc`、`physical_delete_after_utc` 和 `deletion_reason` 记录软删到物理清理窗口；`organization_id + environment_id + owner_service + owner_type + owner_id` 支持按业务 owner 查询；`organization_id + environment_id + completed_at_utc` 支持 Console 文件列表按租户分页读取；`scan_status + status` 支持扫描 worker 扫描候选与下载门控；`status + physical_delete_after_utc` 支持过期物理清理。 |
| `upload_sessions` | business | 上传会话元数据，记录预留 fileId、调用方上下文、provider、过期时间和完成状态。 | `upload_session_id` 为业务生成 string ID；`file_id` 唯一；`object_key` 唯一；`organization_id + environment_id + expires_at_utc` 支持过期会话扫描。 |
| `download_grants` | business | 短期下载授权元数据，当前用于平台控制下载路径；tus provider 下可映射到本地 tus 字节内容。 | `download_grant_id` 为业务生成 string ID；`file_id` 指向 `stored_files`；`organization_id + environment_id + file_id + expires_at_utc` 支持授权校验和清理。 |
| `__EFMigrationsHistory` | system | EF Core migration history table，记录 FileStorage 已应用迁移。 | 必须位于 `filestorage` schema；业务代码不直接读写。 |

Known gaps:

1. 默认运行路径仍可使用 in-memory store 和 `server-proxy` metadata stub；设置 `Persistence:Provider=PostgreSQL` 后可启用 PostgreSQL-backed FileStorage service，客户 release bundle 仍待后续。
2. 设置 `FileStorage:UploadProvider=tus` 后已具备本地 tus `HEAD`/`PATCH` offset 传输、download grant content 读取、size/checksum 强校验、过期未完成上传清理、用途类型策略、配额拒绝、后台扫描状态流转和正式文件软删/物理清理能力。
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
6. `backend/services/Notification/src/Nerv.IIP.Notification.Infrastructure/Migrations/20260624151042_UseIdempotencyKeyForProcessedIntegrationEvents.cs`
7. `backend/services/Notification/src/Nerv.IIP.Notification.Infrastructure/Migrations/20260629034446_AddNotificationDeliveryAttemptRetryState.cs`
8. `backend/services/Notification/src/Nerv.IIP.Notification.Infrastructure/Migrations/20260703094204_AddNotificationExternalDeliveryChannels.cs`

| Table | Kind | Purpose | Key relationships and indexes |
| --- | --- | --- | --- |
| `notification_intents` | business | 平台服务提交的通知意图聚合根，用于站内消息和任务通知。 | `Id` 为 Guid v7；`OrganizationId + EnvironmentId + SourceService + SourceEventType + DedupeKey` 唯一；拥有 message 和 task 子事实。 |
| `notification_messages` | business | 面向收件人的站内通知消息。 | `NotificationIntentId` 指向 `notification_intents`；`RecipientRef + Status + CreatedAtUtc` 支持收件箱扫描。 |
| `notification_tasks` | business | 可操作通知任务，用于待办、失败处理或后续审批联动。 | `NotificationIntentId` 指向意图；`MessageId` 指向对应消息；`RecipientRef + Status + CreatedAtUtc` 支持任务列表。 |
| `delivery_attempts` | business | 通知投递尝试记录，为站内与外部 channel provider、失败重试、限流和投递诊断提供事实。 | `NotificationMessageId` 指向消息；`Channel + Status + AttemptedAtUtc` 支持渠道维度排查；`Status + NextRetryAtUtc` 支持待重试/死信扫描；`AttemptNo` 为一基投递尝试序号；`RecipientAddress`、`ProviderName` 和 `ProviderMessageId` 记录外部投递目标和回执，不保存 provider secret。 |
| `notification_recipient_channel_bindings` | business | 收件人到外部通道账号或 webhook URL 的映射，用于把 Notification recipientRef 解析为 provider 可投递地址。 | `OrganizationId + EnvironmentId + RecipientRef + Channel` 唯一；`OrganizationId + EnvironmentId + Channel + Enabled` 支持按通道扫描 active 绑定；`RecipientAddress` 保存企业微信/钉钉 userId、邮件地址或 webhook URL，不保存密钥。 |
| `notification_preferences` | business | 用户级通知类型 × 通道开关，用于非 critical 通知的外部投递偏好。 | `OrganizationId + EnvironmentId + RecipientRef + NotificationType + Channel` 唯一；`NotificationType + Channel + Enabled` 支持偏好命中；critical severity 可以绕过 disabled preference 强制投递。 |
| `notification_subscriptions` | business | 用户级通知类型 × 通道订阅，用于决定哪些非 critical 通知进入外部通道。 | `OrganizationId + EnvironmentId + RecipientRef + NotificationType + Channel` 唯一；`NotificationType + Channel + Enabled` 支持订阅命中；`NotificationType='*'` 表示通配订阅。 |
| `processed_integration_events` | system | Notification 业务 inbox，记录已处理的集成事件，避免重复业务副作用。 | `ConsumerName + IdempotencyKey` 唯一；`EventId` 仅用于追溯原始发布事件；`SourceService + EventType + ProcessedAtUtc` 支持消费诊断。 |
| `integration_event_dead_letters` | system | Notification 消费侧拒绝或 handler 重试耗尽的集成事件，用于版本不兼容、envelope 缺失、业务 handler poison message 等场景的排查和 replay 标记。 | `id` 为 Guid；`consumer_name`、`event_id`、`event_type`、`event_version`、`source_service`、`idempotency_key`、`status` 和 `event_json` 保留拒绝事实；`status` 支持 `Pending`、`Replayed`、`Failed` 和 `Ignored`；`consumer_name + status + dead_lettered_at_utc` 支撑 pending 队列查看；`consumer_name + event_id` 支撑单事件排查。 |
| `cap_published_messages` | system | CAP published message outbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义；业务代码不直接读写。 |
| `cap_received_messages` | system | CAP received message inbox，由 netcorepal/CAP 基础设施维护。 | 主键由 CAP 类型定义；用于 broker 级消费幂等。 |
| `cap_locks` | system | CAP distributed lock table，由 netcorepal/CAP 基础设施维护。 | 主键 `Key`；用于 CAP 内部协调。 |
| `__EFMigrationsHistory` | system | EF Core migration history table，记录 Notification 已应用迁移。 | 必须位于 `notification` schema；业务代码不直接读写。 |

Known gaps:

1. Notification 当前已有站内消息、任务、业务 inbox、CAP storage、persistent DLQ、外部渠道 provider 首批适配、收件人通道映射、偏好/订阅、重试和限流基线；模板映射仍按 Notification baseline 后续深化。
2. `integration_event_dead_letters` 已支持 Notification 管理入口按 consumer、event type 和状态查询、详情查看、单条/批量 replay、失败标记和人工忽略；DLQ 告警/指标聚合仍按 Observability 后续切片推进。

## 后续服务建表前清单

新服务进入建表阶段前，必须先补充本节对应条目，不能等迁移生成后再回忆设计意图。

| Service | Expected schema | Catalog status | Implemented | Validated | Release-supported | Required before first migration |
| --- | --- | --- | --- | --- | --- | --- |
| IAM | `iam` | Implemented | Yes | Yes | No | 已有 PostgreSQL `iam` schema、初始 migration、schema convention tests、idempotent seed、登录/refresh/logout/`/me` 和 Connector Host credential validation；客户 release bundle 仍待后续。 |
| FileStorage | `filestorage` | Implemented baseline | Yes | Yes | No | 已有 `stored_files`、`upload_sessions`、`download_grants` 初始 migration 与安全 hardening migration、schema convention tests、PostgreSQL-backed API service、server-proxy metadata API、本地 tus MVP 传输、用途类型策略、配额、后台扫描和文件生命周期清理能力；客户 release bundle 仍待后续；MinIO/S3 multipart 为 post-MVP 部署联调项。 |
| BusinessMasterData | `business_masterdata` | Implemented | Yes | Yes | No | 已有 Layer 0 realignment schema、numbering counter/idempotency tables、migration、schema convention tests 和 verify script；客户 release bundle 仍待后续。 |
| BusinessProductEngineering | `product_engineering` | Implemented | Yes | Yes | No | 已有 EngineeringDocument、EngineeringItem、EBOM、MBOM、Routing、StandardOperation、ECO/ECN、ProductionVersion、numbering counter/idempotency tables、migration、schema convention tests 和 verify script；客户 release bundle 仍待后续。 |
| BusinessInventory | `inventory` | Implemented | Yes | Yes | No | 已有库存地点、库存台账、库存移动、盘点任务和盘点调整 schema、migration、schema convention tests 和 verify script；客户 release bundle 仍待后续。 |
| BusinessQuality | `quality` | Implemented | Yes | Yes | No | 已有 NCR、InspectionPlan、InspectionRecord、InspectionTask、SPC control chart locks、persistent DLQ schema、MES defect source 唯一约束、migration、schema convention tests 和 verify script；客户 release bundle 仍待后续。 |
| BusinessMES | `mes` | Implemented | Yes | Yes | No | 已有工单、工序任务（含 Quality 报检触发快照）、物料需求快照、领料/线边接收、报工、报工物料批次消耗、不良记录、完工入库请求、排产结果、工作中心不可用窗口、设备映射、班次交接、numbering counter/idempotency tables 和 persistent DLQ schema、migration、schema convention tests 和 verify script；客户 release bundle 仍待后续。 |
| BusinessDemandPlanning | `demand_planning` | Implemented | Yes | Yes | No | 已有需求来源、带评审/发布生命周期的 MPS、记录输入来源/覆盖周期的 MRP run、带来源类型和 gross demand 的 pegging、带 required/release date 和净需求解释的计划建议，以及 numbering counter/idempotency tables schema、migration、schema convention tests 和 verify script；客户 release bundle 仍待后续。 |
| BarcodeLabel | `barcode` | Implemented | Yes | Yes | No | 已有条码规则、标签模板、打印批次、打印项、扫码记录、GS1 序列化字段和 EPCIS 最小追溯事件 schema、migration、schema convention tests 和 verify script；客户 release bundle 仍待后续。 |
| BusinessApproval | `business_approval` | Implemented | Yes | Yes | No | 已有审批模板、审批链、审批步骤、审批决定、审批委托、会签/或签策略、简单条件路由、超时通知标记和代理审批审计字段 schema、migration、schema convention tests 和 verify script；客户 release bundle 仍待后续。 |
| WMS | `wms` | Implemented | Yes | Yes | No | 已有入库、出库、短拣欠交单、上架/拣货/补货建议任务、盘点执行、WCS 任务和库存移动请求元数据 schema、migration、schema convention tests 和 verify script；客户 release bundle 仍待后续。 |
| ERP | `erp` | Implemented | Yes | Yes | No | 已有 Procurement、Sales、Finance MVP 和 numbering counter/idempotency tables schema、migration、schema convention tests 和 verify scripts；客户 release bundle、完整总账月结和银行/税务对账仍待后续。 |
| BusinessIndustrialTelemetry | `industrial_telemetry` | Implemented | Yes | Yes | No | 已有 tag、设备状态快照、带 ack/shelve/escalation/clear 生命周期的报警事件、采集汇总、historian raw/hourly/daily schema、migration、schema convention tests 和 verify script；客户 release bundle 仍待后续。 |
| BusinessMaintenance | `maintenance` | Implemented | Yes | Yes | No | 已有维修工单、保养计划、点检、停机原因和备件行 schema、migration、schema convention tests 和 verify script；客户 release bundle 仍待后续。 |
| BusinessScheduling | `scheduling` | Implemented | Yes | Yes | No | 已有排程问题快照、排程方案、分配、资源负载、冲突、不可排工序、事件驱动方案失效投影、consumer inbox/DLQ 和 CAP system tables schema、migration、schema convention tests、BusinessGateway facade 和 verify script；客户 release bundle、高级优化器仍待后续。 |
| Notification | `notification` | Implemented baseline | Yes | Yes | No | 已有通知意图、站内消息、任务、投递尝试、收件人通道映射、偏好/订阅、业务 inbox、CAP storage 和 persistent DLQ schema、migration、schema convention tests；DLQ 管理入口支持查询、详情、单条/批量 replay 和 ignore；外部渠道 provider 首批覆盖企业微信、钉钉、SMTP 邮件和 webhook，模板映射仍待后续。 |
| Knowledge | `knowledge` | Planned only | No | No | No | 知识源、文档、分片、索引状态、向量/全文索引边界和重建策略；关系库保存索引元数据，外部向量库保存可重建索引。 |
| AI Integration | `ai` or `ai_integration` | Planned only | No | No | No | 模型/provider 配置、工具授权、调用审计、配额周期、prompt/version 归档、审批挂点和敏感信息边界。 |
| Observability indexes | `observability` | Baseline only | No | No | No | 见 `docs/architecture/observability-baseline.md`；建表前补 LogChunk、LogEntryIndex、归档任务、retention 和 Gateway 查询边界。 |

## 下一轮 hardening 建议

1. 生成或维护简版 ER 图，以 AppHub/Ops/IAM 当前 catalog 和数据库注释为输入。
2. 在新增 Knowledge、AI Integration 或 Observability 索引迁移前，先补该服务的 catalog 草案，再写实体配置、schema convention tests 和 migration；Notification/FileStorage 后续新增表时继续按本 catalog 和 schema convention tests 更新。
3. 后续如 CAP system tables 需要进入客户数据字典展示，补充 system table comment 或保持 catalog 的 system-owned 标记为权威说明。
