# MasterData 数据字典 Reference

本页是 ReferenceData / CodeSet 的**当前人工查询目录**，记录稳定 CodeSet、标准码值和受控字段映射。它不是独立运行时事实源：当前可用值、启停状态与写入校验最终以 MasterData seed、ReferenceData/独立目录 API、领域校验器和实际前端消费代码为准。

稳定维护规则见 [`../../governance/data/reference-data.md`](../../governance/data/reference-data.md)。M2 拆分前的阶段性落地叙事通过 Git 历史追溯，不复制到当前 Reference。

## Producer 与消费边界

- 后端基础目录与 seed：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Seed/MasterDataSeedService.cs`。
- 当前 ReferenceData 查询/维护能力：MasterData API 与 BusinessGateway facade。
- 前端离线兜底：`frontend/apps/business-console/src/data/masterDataReference.ts`；实时目录可用时优先消费 API。
- ProductCategory、Skill、QualityReason 已有结构化独立目录时，以各自目录实体/API 为结构化事实源；legacy CodeSet 只承担兼容读取/校验边界。
- ProductEngineering `StandardOperation` 是标准工序事实源；legacy `operation` CodeSet 不应继续扩张为新的工艺主数据入口。

## 1. 概念

- **ReferenceData / 数据字典**：一条记录由 `(CodeSet, Code, Name)`、`OrganizationId`、`EnvironmentId` 与启用状态组成。
- **CodeSet**：一类受控值集合，如 `material-type`、`storage-condition`。平台保留的 CodeSet 名称及可维护性由治理规则约束。
- **Code**：业务提交、持久化和跨域引用使用的稳定码值；惯例为英文小写 kebab-case。
- **Name**：用户可读展示名。业务对象保存 Code，UI 展示 Name。
- `product-category`、`skill`、`quality-reason` 仍可能承担 legacy 兼容；新增结构化维护优先使用 ProductCategory、Skill、QualityReason 独立目录。
- SKU `category` 的权威值域已经是 ProductCategory 层级目录；legacy `product-category` CodeSet 只用于存量兼容，不再是新写路径的主要校验源。

## 2. CodeSet 目录

类别口径：

- **系统枚举**：码值带系统行为语义；标准集合受平台治理。
- **平台预置+可维护**：平台提供常用值，租户可按治理规则维护。
- **工厂自定义**：平台不预置或只提供样例，租户维护稳定 code/name。

### 2.1 物料相关

| CodeSet | 中文名 | 类别 | 标准码值 / 样例 |
| --- | --- | --- | --- |
| `material-type` | 物料类型 | 系统枚举 | `raw-material`=原材料 / `semi-finished`=半成品 / `finished-goods`=成品 / `packaging`=包装物 / `consumable`=辅料消耗品 / `spare-part`=备品备件 / `tooling`=工装刀具 |
| `product-category` | 产品分类（legacy 兼容） | 平台预置+可维护 | `electronic`=电子料 / `mechanical`=机械件 / `plastic`=塑胶件 / `hardware`=五金件 / `chemical`=化学品 / `assembly`=组装件 |
| `batch-tracking-policy` | 批次追踪策略 | 系统枚举 | `none`=不管理 / `optional`=可选记录 / `mandatory`=强制批次 |
| `serial-tracking-policy` | 序列号追踪策略 | 系统枚举 | `none`=不管理 / `on-receipt`=入库赋序 / `on-production`=生产赋序 / `on-shipment`=出货赋序 |
| `shelf-life-policy` | 保质期策略 | 系统枚举 | `none`=无保质期 / `fifo`=先进先出 / `fefo`=先到期先出 / `expiry-controlled`=到期管控 |
| `storage-condition` | 仓储条件 | 平台预置+可维护 | `ambient`=常温 / `refrigerated`=冷藏 / `frozen`=冷冻 / `dry`=干燥防潮 / `esd`=防静电 / `hazardous`=危化品 |
| `inventory-location` | 主线产品库位候选码 | 平台预置+可维护 | `loc-raw-01`=原料库 / `loc-semi-01`=半成品库 / `loc-fg-01`=成品库 / `loc-line-01`=线边库 |
| `barcode-rule` | 条码规则 | 平台预置+可维护 | `code128`=Code128 / `ean13`=EAN-13 / `gs1-128`=GS1-128 / `qr`=二维码 / `customer-spec`=客户指定 |
| `uom-dimension` | 计量量纲 | 系统枚举 | `count`=计数 / `length`=长度 / `area`=面积 / `volume`=体积 / `weight`=重量 / `time`=时间 |

`inventory-location` 是跨域配置候选码，不拥有 Inventory `StockLocation` 事实；实际库存库位仍由 Inventory 维护。

### 2.2 业务伙伴 / 组织 / 人员

| CodeSet | 中文名 | 类别 | 标准码值 / 样例 |
| --- | --- | --- | --- |
| `partner-type` | 业务伙伴角色 | 系统枚举 | `customer`=客户 / `supplier`=供应商 / `carrier`=承运商 |
| `skill` | 技能/工种（legacy 兼容） | 工厂自定义 | `welding`、`assembly`、`inspection`、`cnc-operation`、`forklift`、`equipment-maintenance` 等 |
| `skill-level` | 技能等级 | 系统枚举 | `junior`=初级 / `intermediate`=中级 / `senior`=高级 / `expert`=专家 |
| `quality-reason` | 质量原因/不良代码（legacy 兼容） | 工厂自定义 | `scratch`、`dimension-ng`、`missing-part`、`solder-defect` 等 |
| `compliance-tag` | 合规标签 | 平台预置+可维护 | `rohs`=RoHS / `reach`=REACH / `msd`=湿敏元件 / `ul`=UL认证 |

### 2.3 设备 / 产线

| CodeSet | 中文名 | 类别 | 标准码值 |
| --- | --- | --- | --- |
| `device-status` | 设备状态 | 系统枚举 | `running`=运行 / `idle`=待机 / `maintenance`=保养 / `fault`=故障 / `scrapped`=报废 |
| `line-type` | 产线类型 | 系统枚举 | `flow`=流水线 / `cell`=单元线 / `discrete`=离散 |
| `work-center-type` | 工作中心粒度 | 系统枚举 | `work-center`=工作中心 / `section`=工段 / `station-group`=工位组 |

`work-center-type` 只描述工作中心粒度；Workshop 是独立组织/资源实体，不属于该 CodeSet。

### 2.4 产品工程 / 工艺

| CodeSet | 中文名 | 类别 | 标准码值 / 样例 |
| --- | --- | --- | --- |
| `operation` | 标准工序目录（legacy 兼容） | 工厂自定义 | `welding`、`assembly`、`inspection`、`cnc-operation`、`packaging` 等 |

新的标准工序维护应消费 ProductEngineering `StandardOperation`，其中包含默认工作中心、准备/加工工时、控制码和执行标志；发布契约继续按自己的快照语义持有 `operationCode` 等字段。

### 2.5 通用业务选择项

| CodeSet | 中文名 | 类别 | 标准码值 |
| --- | --- | --- | --- |
| `priority` | 优先级 | 工厂自定义 | 平台不预置；租户维护稳定 code/name。未配置时应显式不可用，不由调用方猜造枚举。 |

`priority` 只定义可选项目录，不定义调度、工单、质量或维护的业务优先级算法。

## 3. 受控字段映射

| 字段 | 当前校验来源 | 备注 |
| --- | --- | --- |
| SKU `category` | ProductCategory 独立目录 | legacy `product-category` 仅过渡兼容 |
| SKU `materialType` | `material-type` | 必须引用启用值 |
| SKU `batchTrackingPolicy` | `batch-tracking-policy` | 必须引用启用值 |
| SKU `serialTrackingPolicy` | `serial-tracking-policy` | 必须引用启用值 |
| SKU `shelfLifePolicyCode` | `shelf-life-policy` | 必须引用启用值 |
| SKU `storageConditionCode` | `storage-condition` | 必须引用启用值 |
| SKU `defaultBarcodeRuleCode` | `barcode-rule` | 必须引用启用值 |
| SKU `baseUomCode` 及各 `*UomCode` | `UnitOfMeasure.Code` | UoM 是独立实体，不走 CodeSet |
| PersonnelSkill `skillCode` | legacy `skill` / 当前技能目录兼容边界 | 切换完成前保持 legacy code 可读 |
| PersonnelSkill `level` | `skill-level` | 必须引用启用值 |
| BusinessPartner `partnerType` / `partnerRoles` | `partner-type` | 角色值域 |

UoM 换算是有向规则；正向与反向可分别维护。当前校验至少要求源/目标单位存在且启用、量纲一致、`factor > 0`，并保证同一 `(fromUomCode,toUomCode,effectiveFrom)` 不重复。平台不自动用倒数推导反向规则，以保留独立精度、舍入和 affine offset 语义。

## 4. 查询与对齐

- 物料表单在实时目录可用时优先通过 API 获取受控值，前端常量只作为离线兜底；ProductCategory 直接使用独立目录 API。
- 对已经接入离线兜底的 CodeSet，seed/API 产生的 code/name 与前端兜底必须保持语义一致；具体变更纪律见 Governance。
- `inventory-location` 不进入当前物料表单的通用兜底集合；它服务于部署配置与跨域位置约定。
- 精确当前行为应回到 seed、API、校验器和调用代码核实；本页不以“最后更新时间”或阶段完成清单证明实现状态。

相关产品语义：[`../../product/master-data/design.md`](../../product/master-data/design.md)。长期治理决策：[`../../adr/0013-business-master-data-governance.md`](../../adr/0013-business-master-data-governance.md)。
