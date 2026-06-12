# 数据字典规则（Master Data Reference / CodeSet 权威规范）

> 状态：v1（2026-06-10 修订）。本文件是**数据字典（ReferenceData / CodeSet）的单一事实源**——CodeSet 目录、标准码值、治理规则、字段校验映射、前后端对齐约定。
> 取代此前散落在 [`master-data-module-product-design.md`](./master-data-module-product-design.md) §5、ADR [`0013-business-master-data-governance.md`](../adr/0013-business-master-data-governance.md) 中的零散描述（§5 现指向本文件）。
> 三处实现必须与本文件一致:① 后端种子 `backend/services/Business/MasterData/.../Application/Seed/MasterDataSeedService.cs`(运行真相);② 前端常量 `frontend/apps/business-console/src/data/masterDataReference.ts`(Phase 1 兜底);③ 本文件(设计真相)。

---

## 1. 概念

- **ReferenceData / 数据字典**:平台的受控值表。一条记录 = `(CodeSet, Code, Name)` + 多租户 `(OrganizationId, EnvironmentId)` + 启用状态。
- **CodeSet（字典分组）**:一类受控值的集合名（如 `material-type`、`storage-condition`）。CodeSet 名由平台保留,工厂不可新增/改名。
- **Code**:该 CodeSet 内的码值（提交给后端、存库、被其它主数据引用）。**英文小写中划线**(kebab-case)为约定。
- **Name**:展示给用户的中文名。
- 业务对象(SKU 等)的受控字段存的是 **Code**;UI 一律显示 **Name**,不暴露 Code。

## 2. CodeSet 目录（权威清单）

> 「类别」三档:**系统枚举**=带系统行为语义、码值与语义不可改、工厂只能启用/停用;**平台预置+可维护**=平台预置常用值、工厂可增/停用;**工厂自定义**=平台不预置或仅给样例、工厂自行维护。

### 2.1 物料相关

| CodeSet | 中文名 | 类别 | 标准码值（code = 中文名） |
|---|---|---|---|
| `material-type` | 物料类型 | 系统枚举 | `raw-material`=原材料 / `semi-finished`=半成品 / `finished-goods`=成品 / `packaging`=包装物 / `consumable`=辅料消耗品 / `spare-part`=备品备件 / `tooling`=工装刀具 |
| `product-category` | 产品分类 | 平台预置+可维护 | `electronic`=电子料 / `mechanical`=机械件 / `plastic`=塑胶件 / `hardware`=五金件 / `chemical`=化学品 / `assembly`=组装件 |
| `batch-tracking-policy` | 批次追踪策略 | 系统枚举 | `none`=不管理 / `optional`=可选记录 / `mandatory`=强制批次 |
| `serial-tracking-policy` | 序列号追踪策略 | 系统枚举 | `none`=不管理 / `on-receipt`=入库赋序 / `on-production`=生产赋序 / `on-shipment`=出货赋序 |
| `shelf-life-policy` | 保质期策略 | 系统枚举 | `none`=无保质期 / `fifo`=先进先出 / `fefo`=先到期先出 / `expiry-controlled`=到期管控 |
| `storage-condition` | 仓储条件 | 平台预置+可维护 | `ambient`=常温 / `refrigerated`=冷藏 / `frozen`=冷冻 / `dry`=干燥防潮 / `esd`=防静电 / `hazardous`=危化品 |
| `barcode-rule` | 条码规则 | 平台预置+可维护 | `code128`=Code128 / `ean13`=EAN-13 / `gs1-128`=GS1-128 / `qr`=二维码 / `customer-spec`=客户指定 |
| `uom-dimension` | 计量量纲 | 系统枚举 | `count`=计数 / `length`=长度 / `area`=面积 / `volume`=体积 / `weight`=重量 / `time`=时间 |

### 2.2 业务伙伴 / 组织 / 人员

| CodeSet | 中文名 | 类别 | 标准码值 |
|---|---|---|---|
| `partner-type` | 业务伙伴角色 | 系统枚举 | `customer`=客户 / `supplier`=供应商 / `carrier`=承运商 |
| `skill` | 技能/工种 | 工厂自定义 | 样例:`welding`=焊接 / `assembly`=装配 / `inspection`=质检 / `cnc-operation`=数控操作 / `forklift`=叉车 |
| `skill-level` | 技能等级 | 系统枚举 | `junior`=初级 / `intermediate`=中级 / `senior`=高级 / `expert`=专家 |
| `quality-reason` | 质量原因/不良代码 | 工厂自定义 | 样例:`scratch`=划伤 / `dimension-ng`=尺寸不良 / `missing-part`=缺件 / `solder-defect`=焊接不良 |
| `compliance-tag` | 合规标签 | 平台预置+可维护 | `rohs`=RoHS / `reach`=REACH / `msd`=湿敏元件 / `ul`=UL认证 |

### 2.3 设备 / 产线（按需启用）

| CodeSet | 中文名 | 类别 | 标准码值 |
|---|---|---|---|
| `device-status` | 设备状态 | 系统枚举 | `running`=运行 / `idle`=待机 / `maintenance`=保养 / `fault`=故障 / `scrapped`=报废 |
| `line-type` | 产线类型 | 系统枚举 | `flow`=流水线 / `cell`=单元线 / `discrete`=离散 |
| `work-center-type` | 工作中心粒度 | 系统枚举 | `work-center`=工作中心 / `section`=工段 / `station-group`=工位组 |

> 注:`work-center-type` 用于区分工作中心粒度,**与"车间(Workshop)组织层"是两码事**(车间是独立实体,不在字典里;见产品设计文档 §2.1)。

## 3. SKU 受控字段 → CodeSet 校验映射

SKU 创建/更新时,以下字段的取值**必须存在于对应 CodeSet 且为启用状态**(后端校验,#346）：

| SKU 字段 | 校验 CodeSet | 备注 |
|---|---|---|
| `category` | `product-category` | |
| `materialType` | `material-type` | |
| `batchTrackingPolicy` | `batch-tracking-policy` | |
| `serialTrackingPolicy` | `serial-tracking-policy` | |
| `shelfLifePolicyCode` | `shelf-life-policy` | |
| `storageConditionCode` | `storage-condition` | |
| `defaultBarcodeRuleCode` | `barcode-rule` | |
| `baseUomCode` 及各 *UomCode | （不走字典）引用 `UnitOfMeasure.Code` | 计量单位是独立实体,非 CodeSet |

人员技能 `skillCode` 字段校验 `skill`,`level` 字段校验 `skill-level`;业务伙伴 `partnerType`/`partnerRoles` 校验 `partner-type`。

UoM 换算是有向换算规则,允许工厂同时维护正向和反向换算(例如 `kg->g` 与 `g->kg`),也允许同量纲换算网络闭合;后端只强制创建时源/目标单位存在且启用、二者属于同一 `uom-dimension`、`factor > 0` 且同一 `(fromUomCode,toUomCode,effectiveFrom)` 不重复。反向规则不会由平台自动倒数推导,以便保留独立精度、舍入和 affine offset 语义。

## 4. 治理规则

1. **CodeSet 名平台保留**:工厂不可新增/改名 CodeSet,只能在既有 CodeSet 下维护码值。
2. **系统枚举不可改**:类别为「系统枚举」的 CodeSet,其码值与语义平台固化,工厂**只能启用/停用**,不可改 code、不可新增(否则破坏单据/排产/追溯逻辑)。
3. **平台预置+可维护 / 工厂自定义**:工厂可新增码值、可停用,**不可物理删除**(已被历史单据引用)。
4. **Code 唯一性**:在 `(OrganizationId, EnvironmentId, CodeSet)` 范围内 `Code` 唯一。
5. **停用而非删除**:字典码一律软删除(停用);停用前应校验无启用主数据在用,或给出在用提示。新单据只能引用启用码,历史单据保留对停用码的引用。
6. **Code 不可改**:码值创建后不可改(被引用),Name 可改。
7. **校验时机**:SKU 等受控字段在创建/更新时做字典存在性 + 启用校验(#346)。

## 5. 前后端对齐约定

- **本文件 = 设计真相**;**后端种子 `MasterDataSeedService` = 运行真相**;二者**必须一致**(任一方改动需同步本文件并对齐另一方)。
- **前端常量 `masterDataReference.ts` = 离线兜底**:物料表单优先实时 `?codeSet=` 拉取,后端字典暂不可用时才用本常量;其码值必须与本文件一致。
- **Phase 2 联动**:物料表单已实时 `?codeSet=` 拉取(`数据字典`页维护 → 表单即时可选),前端常量降级为离线兜底。
- 三处的 code 值集合必须等同；前端离线兜底的中文 label 应与本文件和后端种子 name 保持一致，避免实时字典不可用时出现不同展示名。

## 6. 落地状态（2026-06-10）

- ✅ 前端:`数据字典`页(CodeSet 主从可维护),物料表单优先通过 `?codeSet=` 实时拉取产品分类、物料类型、追踪策略、存储条件、条码规则和合规标签;`masterDataReference.ts` 保留为离线兜底。
- ✅ 后端种子 `MasterDataSeedService` 已通过 #352/#369 对齐本文件:补齐 §2 权威码值,修正 `product-category`/`material-type` 旧错配,对 `batch-tracking-policy:lot`、`serial-tracking-policy:serial`、`shelf-life-policy:180d/365d`、`uom-dimension:mass/quantity` 等历史误种码值执行软停用而非物理删除；seed 会修复既有启用标准码的中文 name 与 UOM 种子的名称/量纲。
- ✅ SKU 创建/更新会按 §3 校验受控字段必须引用启用 ReferenceData;人员技能登记会校验技能目录与技能等级必须引用启用 ReferenceData;系统枚举 CodeSet 禁止运行时新增非标准码或改写标准码名称,平台预置/工厂自定义 CodeSet 仍可按治理规则新增码值。

## 附:相关文件
- 后端种子:`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Seed/MasterDataSeedService.cs`
- 前端常量:`frontend/apps/business-console/src/data/masterDataReference.ts`
- 字典页:`frontend/apps/business-console/src/pages/master-data/reference-data.vue`
- 模块设计:[`master-data-module-product-design.md`](./master-data-module-product-design.md) §5
- 治理 ADR:[`0013-business-master-data-governance.md`](../adr/0013-business-master-data-governance.md)
