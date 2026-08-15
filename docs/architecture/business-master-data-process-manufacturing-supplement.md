# 业务主数据流程制造补充说明

本补充说明扩展了 BusinessMasterData 基线，使业务平台能够支持化工、食品、制药、冶金及类似的批次或连续生产等流程制造场景。它不替代离散制造模型，而是增加边界规则，防止仅适用于离散制造的假设泄漏到 ProductEngineering、MES、Quality、Inventory 和 Planning。

## 范围

流程制造支持必须覆盖以下场景族：

1. 化工：浓度、密度、纯度、危险物料处理、兼容设备和工艺参数。
2. 食品：过敏原、保质期、存储条件、批次可追溯性、包装和放行规则。
3. 制药：GMP 配方版本、效价、批记录、设备洁净度、质量放行和受监管的变更控制。
4. 冶金：牌号、炉次、联产品/副产品、收率和批次谱系。

## MasterData 增补项

| 能力 | MasterData 职责 | 说明 |
| --- | --- | --- |
| 物料属性 | 存储物料形态、牌号、存储条件、危险类别、过敏原标签、监管标签、保质期策略、默认质量必需标志等稳定属性 | 实际检验值保留在 Quality 或 MES |
| 单位体系 | 负责维护 UOM、单位组、换算、精度和舍入 | 配方特定的换算仍归 ProductEngineering |
| 工厂/资源层级 | 负责维护 Site/Plant/Area/Line/WorkCenter/DeviceAsset 层级 | IAM organization/environment 仍归 IAM |
| 设备能力 | 负责维护静态容量、容量 UOM、物料兼容性、洁净类别、温度/压力设计范围和公用工程需求引用 | 实际运行值保留在 Telemetry 和 MES |
| 参考定义 | 负责维护物料形态、存储条件、危险类别、质量特性定义和工艺参数定义等跨领域代码集 | 领域特定的工作流状态保留在各领域 |
| 合作伙伴合规 | 负责维护合作伙伴身份、合作伙伴角色和稳定的合规标签或证书引用 | 供应商审核工作流和放行决策保留在 Quality/SRM |

## ProductEngineering 边界

ProductEngineering 不得将流程制造视为简单的 MBOM 变体。它拥有以下版本化工程事实：

| 对象 | ProductEngineering 职责 |
| --- | --- |
| 配方（Recipe / Formula） | 版本化配方身份、产品/物料产出、批量基准、生效日期、放行状态 |
| 配方行（Formula line） | 投入物料、数量或比例、UOM、收率贡献、损耗系数、替代物料、返工/复用规则 |
| 联产品/副产品（Co-product / by-product） | 预期产出物料、收率、成本相关性和可追溯性要求 |
| 工艺步骤/阶段（Process step / phase） | 有序阶段、所需资源能力、工作中心、预期时长和准备/清洁依赖 |
| 工艺参数目标（Process parameter target） | 与已放行配方/工艺路线版本绑定的温度、压力、流量、pH、速度或其他目标值及公差 |
| 变更控制（Change control） | 用于配方、公式、工艺路线和参数版本的 ECO/ECN 或等效放行流程 |

MasterData 负责维护可复用定义和静态资源事实。ProductEngineering 负责维护版本化的产品特定配方、公式和工艺路线内容。

## 领域边界

| 事实 | 所有者 |
| --- | --- |
| SKU 物料身份及默认属性 | BusinessMasterData |
| UOM 与换算 | BusinessMasterData |
| 产品的配方/公式版本及工艺参数 | ProductEngineering |
| 实际生产批次、批号、炉次、序列号或日期码实例 | Inventory |
| 库存余额、FEFO 执行及库存状态 | Inventory |
| 检验标准、抽样规则、结果、COA 及放行决策 | Quality |
| 批次生产工单、实际投入/产出、偏差、清洁执行及谱系 | MES |
| 运行时温度、压力、流量、报警及状态快照 | IndustrialTelemetry |
| 维护工单、点检、停机及资产恢复 | Maintenance |

## 验收场景

在宣告支持流程制造前，必须能够表达以下场景：

1. 化工混配：使用已配置的 UOM 规则换算 kg 和 L；应用密度/浓度引用；选择兼容的容器容量；在 MasterData 外记录实际工艺值。
2. 食品生产：在物料上标记过敏原和存储条件；通过 Inventory 执行保质期和 FEFO；将配方版本保留在 ProductEngineering。
3. 制药批次：使用已放行的公式版本和与 GMP 相关的质量特性；将批记录和放行决策保留在 MasterData 外。
4. 冶金炉次：在 SKU 上跟踪牌号和炉次/lot 策略；将实际炉次谱系保留在 MES/Inventory。

## 实施影响

1. 下游业务服务依赖前，MasterData Task 4 必须公开 UOM、SKU 工业属性、资源层级和设备能力 API。
2. ProductEngineering MVP 必须更新为将 Recipe/Formula 和 ProcessParameter 作为一等版本化事实，而非仅有 EBOM/MBOM/Routing。
3. Inventory MVP 必须保留实际 lot、序列号、炉次、到期和库存状态事实，同时从 MasterData 消费 SKU 可追溯性和 UOM 策略。
4. Quality MVP 必须区分可复用的特性定义与检验标准及放行决策。
5. MES MVP 必须将批次执行和谱系与静态主数据事实分别建模。

## 非目标

1. MasterData 不存储批次生产记录、实际工艺值、检验结果或遥测样本。
2. MasterData 不实现 GMP 电子批记录工作流。
3. MasterData 不控制 PLC/DCS/SCADA，也不存储控制凭据。
4. MasterData 不计算成本、MRP 或库存余额。
