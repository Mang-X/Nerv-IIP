# BusinessMasterData 当前字段与事实所有权

本文只描述 BusinessMasterData **当前拥有的稳定业务事实、下游消费关系与跨域边界**。审批、变更审计等规则见 [`../../governance/data/master-data-ownership.md`](../../governance/data/master-data-ownership.md)；M2-L 拆分前的混合正文冻结于 [`../../reports/m2-l-business-master-data-field-matrix-pre-split-2026-09-07.md`](../../reports/m2-l-business-master-data-field-matrix-pre-split-2026-09-07.md)。字段、接口和持久化的精确实现最终以当前代码、公开契约、迁移和测试为准。

## 事实分类

| 类别 | 当前边界 | 示例 |
| --- | --- | --- |
| Master data | 可被多个业务域复用的持久化业务身份或稳定属性；无更具体 owner 时归 BusinessMasterData | SKU、UOM、合作伙伴、工厂、工作中心、设备资产 |
| Reference data | 跨域复用的受控代码表或可复用定义归 BusinessMasterData | 物料类型、存储条件、资产类别、质量特性定义 |
| Transactional data | 有时间约束的流程状态、事件或执行事实归产生它的流程域 | 采购订单、库存移动、批次记录、报警、检验记录 |
| External reference | 原始 ID 与安全事实仍由来源系统拥有；MasterData 只保存引用 | IAM `userId`、`fileId`、Connector Host ID、外部 ERP code |

## 核心对象所有权

| 对象 | BusinessMasterData 当前拥有 | 明确不拥有 |
| --- | --- | --- |
| SKU / Material | 业务身份、分类、各业务 UOM、跟踪/保质期/存储策略、采购/MRP/批量与默认提前期、安全库存/再订货点默认值、生命周期与业务启用状态 | EBOM/MBOM/配方版本、站点/场景计划覆盖、库存余额、批次/序列实例、订单价格与实际成本 |
| UnitOfMeasure / UomConversion | UOM 身份、量纲、精度、舍入及通用换算和生效期 | 检验/遥测实际测量值、配方特定收率和批次换算 |
| BusinessPartner / PartnerQualification | 合作伙伴身份、角色、税务/区域、默认币种/账期、主地址与联系人、信用额度、合规标签；资质类型、材料范围、证书引用、生效期与状态 | RFQ/报价/采购销售订单、AR/AP、供应商记分卡交易、审核工作流与质量放行 |
| Site / Plant / Area / Line | 设施层级代码、名称、父级、类型、时区、地址引用与停用状态 | IAM organization/environment |
| WorkCenter | 资源身份、工厂/产线归属、默认日历、容量/利用率/效率、有限产能、瓶颈与成本中心等稳定属性 | 排程结果、实际停机、工序报告 |
| WorkCalendar / Shift | 可复用工作日历、规则、例外、生效期；班次起止、跨日、工休时长 | 实际出勤、加班审批、生产报告 |
| Department / Team / PersonnelSkill | 业务组织/班组/技能的稳定业务关系和生效状态 | IAM membership、role、permission 与 HR 薪资 |
| DeviceAsset | 设备业务身份、分类/型号/序列、资源层级归属、父子组件、采购/保修/供应商、关键性、可维护/遥测启用等稳定资产属性 | PLC/DCS/SCADA 密钥、测点样本、报警、维修工单、备件消耗和保修索赔流程 |
| ResourceCapability | 资源及能力代码、适用物料、容量范围/UOM、兼容存储条件与生效期 | 产品特定工艺路线参数 |
| ReferenceData | 跨域 CodeSet 的代码、名称、说明、状态与生效期 | 单一领域私有的交易状态 |

## 流程制造敏感事实

| 事实 | MasterData 角色 | 其它 owner |
| --- | --- | --- |
| 浓度、效价、密度、纯度、水分 | 稳定物料属性与允许单位 | 配方/收率/工艺参数由 ProductEngineering；实际检验/批记录/遥测由 Quality / MES / Telemetry |
| 保质期与到期规则 | 默认策略及其存储条件依赖 | 配方引用由 ProductEngineering；实际到期与放行由 Inventory / Quality |
| 危险品、过敏原、监管标签 | 稳定物料/合作伙伴合规标签 | 配方兼容由 ProductEngineering；存储隔离与放行由 WMS / Quality |
| 批次/序列跟踪策略 | SKU 的默认跟踪要求 | 实际批次、序列、炉次、日期码实例由 Inventory |
| 设备容量与兼容性 | 静态容量范围、UOM、物料兼容性与清洁类别 | 路线使用由 ProductEngineering；实际设备使用和清洁执行由 MES |
| 质量特性定义 | 跨域复用的特性代码、名称、量纲与 UOM | 检验标准、抽样、结果和放行由 Quality |

## 下游交互边界

下游创建新业务单据前通过 MasterData 公共契约解析业务代码/ID；需要历史可读性时只保存轻量不可变引用快照；缓存有效主数据时通过 MasterData IntegrationEvent 同步。主记录禁用、归档、替换或合并不能破坏既有业务单据历史。

业务域不得因为消费 MasterData 而共享其数据库表或取得写权限。大规模依赖需要批量解析与有效性检查能力；具体 endpoint 和字段形状由当前公开契约定义，本文不维护第二份 API 清单。
