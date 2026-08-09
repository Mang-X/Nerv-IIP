# ADR 0013：业务主数据治理

- 状态：已接受
- 日期：2026-05-21

## 背景

BusinessMasterData 是业务平台的首个切片，也是 ProductEngineering、DemandPlanning、Inventory、Quality、ERP、WMS、MES、IndustrialTelemetry 和 Maintenance 的基础。初始切片正确地将 SKU、业务伙伴、业务组织属性、工作中心、日历和设备资产放入 Layer 0（第零层），但审核发现，这些内容只能构成最小骨架。

同一 MasterData 服务必须同时支持离散制造和流程制造。流程制造还会引入 UOM 换算、物料效价或浓度、保质期、存储约束、Recipe/Formula（配方）版本、工艺参数、设备产能与兼容性、质量规格和受监管放行规则等需求。如果将这些决策推迟到 ERP、MES、WMS 或 Quality 实施阶段，下游领域就会各自维护一套主数据，平台也会失去稳定的事实来源。

## 决策

1. BusinessMasterData 拥有通用业务身份和静态参考事实；多个业务领域在创建事务前必须共享这些信息。
2. MasterData 必须区分以下四类数据：
   - 主数据：持久的业务身份和静态属性，例如 SKU、业务伙伴、工厂、工作中心、设备资产和计量单位。
   - 参考数据：受控代码表，例如物料类型、业务伙伴角色、资产类别、存储条件、危险类别、质量特性和工艺参数定义。
   - 事务数据：订单、移动、生产报工、检验记录、报警、工单和财务过账。这些数据不属于 MasterData。
   - 外部引用：IAM 用户、组织和环境 ID，File Storage 文件 ID，Connector Host/AppHub ID，以及外部系统标识符。MasterData 可以引用这些信息，但不拥有其事实。
3. 在 MasterData 重整计划明确以下事项前，当前 MasterData 实施计划不得进入 API 冻结或下游依赖推广阶段：
   - UOM 及换算的所有权。
   - SKU/物料工业属性和追溯策略。
   - 业务伙伴身份、角色和敏感商业字段。
   - 站点/工厂/区域/产线/工作中心/设备资源层级。
   - 设备静态产能、兼容性和外部引用。
   - 流程制造补充要求，以及 ProductEngineering 的 Recipe/Formula（配方）边界。
   - 面向下游的解析 API，以及 MasterData 变更 IntegrationEvent（集成事件）。
4. 下游服务不得直接读取 MasterData 数据库，也不得创建重复的主数据事实。它们应通过公开 API、参考快照和 IntegrationEvent（集成事件）消费 MasterData。
5. 可能影响下游决策的 MasterData 变更必须能够按照 ADR 0011 作为 IntegrationEvent（集成事件）发布。下游服务缓存或快照这些事实前，至少必须为 SKU、UOM、业务伙伴、资源、工作日历和设备资产变更定义稳定的事件名称及版本化载荷。
6. MasterData 必须定义物理删除之外的生命周期状态。停用、归档、替换、合并和生效日期变更必须保留历史引用，不得静默使现有业务单据失效。
7. 必须显式分类业务伙伴敏感字段和涉及人员信息的字段。IAM 仍拥有用户、角色、权限和成员关系事实；ERP 仍拥有采购、销售和财务事务；Quality 仍拥有检验标准和放行决策，除非字段矩阵明确将某字段标记为 MasterData 参考定义。

## 理由

1. 大多数下游领域都会共享 SKU、UOM、业务伙伴、资源和设备数据。若将它们作为各服务的本地字段，会导致计划、库存、质量和财务行为不一致。
2. 仅用离散制造的 SKU 加 MBOM/工艺路线解释，无法安全地建模流程制造。必须在 MES 和 ProductEngineering 难以变更之前，确定物料属性、Recipe/Formula（配方）边界和工艺参数定义。
3. 业务服务有意按数据库 schema 隔离。公开解析 API 和事件用于取代跨 schema 外键。
4. 小型且受治理的 MasterData 服务优于巨型主数据服务。字段矩阵负责裁定哪些内容属于 MasterData，哪些内容仍归 ProductEngineering、Quality、Inventory、MES、ERP、Telemetry 或 Maintenance。

## 后果

1. MasterData 切片在完成 API、权限 seed 和就绪性文档前，新增一个重整步骤。
2. ProductEngineering MVP 必须显式支持 Recipe/Formula（配方）和工艺参数，将其作为 MBOM/工艺路线的流程制造扩展，同时不得将版本化工程事实放入 MasterData。
3. Inventory 保留实际库位、批次/序列号实例、库存余额和移动。MasterData 可以拥有供 Inventory 消费的 SKU 追溯策略和 UOM 规则。
4. Quality 保留检验标准、计划、记录、不合格事项和放行决策。MasterData 可以拥有可复用的特性定义和参考代码。
5. MES 保留批次生产执行、实际消耗、实际产出、批记录、偏差、清洁执行和谱系。MasterData 拥有静态资源和物料事实。
6. 在下游服务实现可以依赖 MasterData 之前，还需要补充文档和测试。

## 实施说明

1. 字段级事实来源是 `docs/architecture/business-master-data-field-matrix.md`。
2. 流程制造补充内容由 `docs/architecture/business-master-data-process-manufacturing-supplement.md` 治理。
3. 可执行的调整计划是 `docs/superpowers/plans/2026-05-21-business-master-data-realignment.md`。
4. 原 MasterData 基础计划仍是有效的历史输入，但只有在重整计划更新领域模型、事件和 API 契约后，才可以执行任务 4 和任务 5。
