# ADR 0012: 业务平台关键链路与主平台边界

- Status: Accepted
- Date: 2026-05-20

## Context

主平台已经推进到 IAM 持久化认证、Gateway 权限门禁、Console Auth 和脚本治理阶段。业务平台需要并行规划，避免等主平台完全收尾后才开始拆工业业务域。

GitHub issues #72 到 #77 给出了第一版业务域输入，覆盖共享基础域、通用能力域、MES、WMS、ERP 和全链路验收。这一版方向合理，但它偏向“MES/WMS/ERP 三件套”，对工业关键链路中更靠前或更靠近设备现场的系统边界描述不足，例如 PDM/PLM、MPS/MRP、IIoT、CMMS，以及 CRM、SRM、CPQ、OMS、WCS、SCADA、PLC/DCS 等系统在首批链路中的位置。

这些系统不应该全部变成首批独立服务。否则业务平台会在开工前膨胀成完整工业软件套件，无法形成可验证纵切。正确做法是按关键链路筛选：

1. 缺了它，链路无法解释事实来源或数量来源的，要进入首批规划。
2. 可以由 ERP/MES/WMS 子域承载的，不在首批拆独立系统。
3. 属于现场设备或第三方系统的，只定义适配边界，不把外部系统重写一遍。
4. 只有特定场景才成立的，先预留升级边界。

## Decision

1. 业务平台继续作为主平台之外的行业领域扩展。主平台只提供 IAM、File Storage、AppHub、Ops、Notification、Knowledge、AI Integration、PlatformGateway、Connector Host 协议等通用控制面能力。
2. 接受 #72 到 #77 作为业务平台基础输入，但将业务规划升级为“关键链路优先”模型，而不是只围绕 MES/WMS/ERP。
3. 首批必须纳入规划的关键业务域为：
   - BusinessMasterData：SKU、业务伙伴、组织业务属性、资源、工作中心、设备资产主数据。
   - ProductEngineering：PDM/PLM lite，拥有 CAD 文件引用、工程物料、EBOM、MBOM、工艺路线版本、ECO/ECN 和发布状态。
   - DemandPlanning：MPS/MRP lite，拥有需求来源、主生产计划、物料需求、计划采购建议、计划工单建议和 pegging 关系。
   - Inventory：库存台账、库位、批次、序列号、库存移动、盘点和调整的唯一库存事实源。
   - Quality、BarcodeLabel、BusinessApproval：质量、条码标签和业务审批通用能力。
   - ERP：采购、销售、财务 MVP；在首批承载 SRM-lite、CRM-lite 和 OMS-lite 的最小能力。
   - WMS：仓储执行；预留 WCS adapter 边界，但不在没有自动化仓场景时独立建设 WCS。
   - MES：工单、工序、报工、完工入库请求、规则排产和生产日报。
   - IndustrialTelemetry：IIoT/Telemetry lite，拥有 tag 映射、采集点、时序摘要、报警事件和设备状态快照；PLC/DCS/SCADA 是外部来源或 Connector 适配对象。
   - Maintenance：CMMS lite，拥有维修工单、保养计划、点检、故障、停机原因和备件需求。
4. 首批不独立建设但必须预留边界的系统为：
   - CAD：作为工程文件和设计来源，通过 File Storage 与 ProductEngineering 引用，不作为平台内建 CAD。
   - SCADA、PLC/DCS：作为现场控制和数据来源，通过 Connector Host、工业协议 Connector 和 IndustrialTelemetry 接入。
   - SRM：首批压缩进 ERP Procurement 的供应商、询价/报价和采购协同最小能力；供应商门户后置。
   - CRM：首批压缩进 ERP Sales 的客户、商机、报价和销售订单；完整营销、跟进、线索池后置。
   - CPQ：只有配置型产品、按单设计或复杂报价规则成立时才独立；首批只预留报价配置边界。
   - OMS：首批由 ERP Sales + WMS fulfillment 承担订单履约；多渠道订单、拆单合单、履约路由后置。
   - WCS：首批只定义 WMS 到 WCS 的任务/回执 adapter；自动化立库、输送线、AGV/AMR 接入后再独立。
   - EAM：首批用 CMMS lite 覆盖设备维护；资产全生命周期、折旧、备件深度财务后置。
5. 单仓过渡开发阶段允许把业务平台服务放在 `backend/services/Business/{Context}` 下，项目命名采用 `Nerv.IIP.Business.{Context}.Web|Domain|Infrastructure`。这只是开发期代码组织便利，不表示业务平台成为主平台核心能力。
6. 主平台服务不得引用 `backend/services/Business` 下的 Web、Domain、Infrastructure 项目。业务服务只能通过 Platform SDK、公开 Contracts、OpenAPI、IntegrationEvent 和 IAM 授权上下文消费主平台能力。
7. 业务前端使用独立 `frontend/apps/business-console` 或等价业务应用入口，不把 MES/WMS/ERP/PDM/CMMS 页面塞进主平台 console。需要页面聚合时，创建业务扩展自己的 BusinessGateway/BFF，不让 PlatformGateway 承载业务规则。
8. IAM 继续拥有身份、平台组织、环境、角色、权限、会话和外部客户端事实。业务组织域只保存业务部门、班组、技能、资质等行业属性，并通过 `organizationId`、`environmentId`、`userId` 引用 IAM 公开身份上下文。
9. AppHub 继续拥有受管应用、实例、节点、能力和存活事实。设备资产、工业 tag、报警、维修工单和 OEE 不进入 AppHub；它们分别归 BusinessMasterData、IndustrialTelemetry、Maintenance 和 MES。
10. Ops 继续拥有平台动作任务、执行尝试、审计记录和运维审批挂点。BusinessApproval 只处理业务单据审批，不接管平台运维动作生命周期。
11. Inventory 是库存数量、可用量、冻结量、批次、序列号、库存移动和盘点调整的唯一事实源。WMS、MES、ERP、DemandPlanning 不直接改库存表，也不维护平行库存余额。
12. ProductEngineering 是 EBOM、MBOM、工艺路线版本和工程变更的事实源。MES 工单、MRP 展开、ERP 成本估算只能引用已发布版本，不直接维护工程版本事实。
13. DemandPlanning 是计划建议和 MRP pegging 的事实源。它生成计划采购建议和计划工单建议，但不直接创建采购订单、正式工单或库存移动。
14. IndustrialTelemetry 只拥有工业数据采集后的平台侧事实，不控制 PLC/DCS，不替代 SCADA，也不绕过 Connector Host 直连现场。
15. Maintenance 是维修、保养、点检和停机原因事实源。MES 可以消费停机和可用性结果，DemandPlanning 可以消费产能影响，但不直接改维修事实。
16. 跨业务服务状态传播默认使用 ADR 0011 定义的 IntegrationEvent envelope、版本策略、幂等、DLQ 和 replay 规则。强一致入口可以使用同步 API，但不得通过共享数据库表或跨 schema 外键协作。
17. 首批实施顺序固定为：MasterData → ProductEngineering → Inventory/Quality/Approval/Barcode 基础能力 → DemandPlanning → ERP Procurement/Sales/Finance MVP → WMS → MES → IndustrialTelemetry/Maintenance → full-chain acceptance。
18. 每个业务服务沿用 `docs/architecture/backend-cleanddd-netcorepal-guidelines.md`、数据库 schema 规范、OpenAPI 规范和权限矩阵。新增权限码先登记到授权矩阵，再进入 IAM seed、端点鉴权和测试。

## Rationale

1. 只做 MES/WMS/ERP 会缺少产品定义、工程变更、计划来源、设备数据和维修闭环，后续链路会靠手工假数据维持。
2. 把 PDM/PLM lite 纳入首批规划，可以解释 CAD、EBOM、MBOM、工艺路线和工程变更如何进入制造链路。
3. 把 MPS/MRP lite 纳入首批规划，可以解释销售订单、预测、库存、BOM 和在制如何变成采购建议与工单建议。
4. 把 IIoT/Telemetry 和 CMMS lite 纳入首批规划，可以让设备状态、报警、OEE、停机和维护对 MES 排产产生真实影响。
5. CRM、SRM、CPQ、OMS、WCS 都有价值，但它们要么可以先由 ERP/WMS 子域承载，要么只有特定业务形态才需要独立服务。
6. 将 PLC/DCS/SCADA/CAD 视为外部系统或适配来源，能避免业务平台误把现场控制系统和工程设计软件重做一遍。
7. 保持主平台与业务平台分离，可以延续 Nerv-IIP “通用控制面 + 行业扩展”的定位。

## Consequences

1. 业务平台规划会比第一版更完整，早期文档和契约工作量会上升。
2. 首批实现仍必须切片推进，不能因为规划覆盖 PDM/PLM、MRP、IIoT、CMMS 就一次性开多个大系统。
3. ProductEngineering、DemandPlanning、IndustrialTelemetry 和 Maintenance 的 schema、OpenAPI、Contracts、seed、权限和测试需要按主平台同等标准治理。
4. 业务服务之间需要严格的事件契约、幂等处理和版本引用，否则工程变更、MRP 重算、库存移动和设备事件会很难诊断。
5. #77 全链路验收需要升级为“工程到制造、计划到采购/生产、设备到维护、订单到交付”的关键链路验收，而不仅是采购、生产、销售、盘点五条业务流。

## Alternatives Considered

1. 继续只规划 MES/WMS/ERP。该方案较简单，但缺少产品工程、计划和设备现场链路，后续一定返工。
2. 将 SRM、CRM、CPQ、OMS、WCS、SCADA、PLC/DCS、CAD 全部拆成独立首批服务。该方案覆盖面最大，但会导致范围爆炸，不利于形成可验证 MVP。
3. 先按行业软件名称建系统，再思考链路。该方案容易堆名词，不能保证事实源清晰。
4. 按关键链路筛选并区分事实源、子域、外部系统和升级边界。该方案兼顾完整性和可实施性，因此采用。

## Implementation Notes

1. 首批业务架构说明见 `docs/architecture/business-platform-domain-architecture.md`。
2. 完整业务平台规格见 `docs/superpowers/specs/2026-05-20-business-platform-domain-design.md`。
3. 业务服务首次建表前必须补充 schema catalog 条目、实体配置注释、migration 和 schema convention tests。
4. 业务服务首次暴露 Gateway 或服务 API 前必须补 OpenAPI 测试，并生成或更新业务前端 api-client。
5. 业务服务首次发布 IntegrationEvent 前必须补 Contracts DTO、事件常量、序列化测试和消费者幂等测试。
6. 业务权限码首次落地前必须同步更新 IAM seed、授权矩阵、Endpoint 鉴权声明和权限测试。
