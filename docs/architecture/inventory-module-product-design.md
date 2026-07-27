# 库存与仓储模块 · 产品业务设计

> 业务域：库存（Inventory）+ 仓储作业（WMS）。本文件是该域产品 / IA / UX / 分期 / 验收的依据，
> 与 `docs/architecture/master-data-module-product-design.md` 同级。
> 改本域页面前先读本文件；发现后端缺口在 §4 回填。

## 1. 这个域给谁用、主操作是什么

| 角色 | 主场景 | 落点页面 |
|---|---|---|
| 仓管 | 看本厂现在有什么货、在哪个库位、能不能动 | 库存可用量、批次与预留 |
| 仓管 | 收货入库 → 上架；领料/发货 → 拣货 → 出库 | 入库单、上架任务、拣货任务、出库单 |
| 仓管 / 盘点员 | 盘点账实差异并调整 | 盘点执行、盘点任务 |
| 计划员 / 质量 | 查某物料某批次的可用量与冻结原因（从上游页面带上下文进来） | 库存可用量（深链） |

**主操作不是"查询"，是"看货和动货"。** 这条决定了首屏形态（见 §2）。

## 2. 首屏原则：进页面就该看到货，而不是先填条件

2026-07 走查结论（owner 原话）：「一进来不是应该直接看到所有库存表格吗，还要让用户填写」。

仓管的心智是**先看全貌、再下钻**，不是先背出物料编码再查询。因此本域所有列表页遵循：

1. **首屏必须有数据**。没有"请先选择物料"这类拦路空态。
2. **筛选是收窄，不是前置条件**。选物料 = 下钻，而不是"开始查询"。
3. **下钻要有回头路**。进入单物料明细后必须能一键返回全厂视图。
4. **凡值域来自目录的字段一律只选不填**（物料、工厂、单位、库位、批次、序列号、状态）。
   自由文本只留给真正的自由文本（备注、理由、新建单号）。
5. **覆盖范围要如实交代**。当前实现受后端读面限制（§3），界面必须写清"看到的是多少"，
   并给继续加载的出路——不许让人误以为看到的就是全部。

### 单位为什么不是独立筛选项
台账维度上单位由物料决定（原材料按品类是 kg / l，计件件号才是 pcs）。让用户手输单位，
只会写出"查不到货"的组合。所以单位一律**跟随所选物料的基本单位自动带出**，界面上只读展示。

### 为什么不做跨物料的数量合计
不同物料单位不同，把 kg 和 pcs 加在一起是错误的业务口径。全厂视图的汇总**只出现在可加的
计数上**（有货物料数、分布库位数、台账行数），绝不加总数量。

## 3. 当前实现与后端约束（重要：这是临时形态）

库存域**没有**"不带物料的全量库存读面"：

| 读面 | 必填 | 分页 | 能不能当全厂库存表 |
|---|---|---|---|
| `GET /inventory/availability` | skuCode + uomCode + siteCode | ❌ 无（>1000 行报错） | ❌ 必须先定物料 |
| `GET /inventory/expiry-alerts` | siteCode | ✅ | ❌ SQL 硬过滤"有效期且临期/过期"，无效期的常规库存永远查不出来 |

因此首屏采用**按物料目录并发扫描 + 前端聚合**（`useInventorySiteStockOverview`）：
逐个物料真实查台账再汇总。**每一行都来自真实查询，不是造数**；代价是覆盖面受扫描批次限制，
所以界面上标注 `已扫描 N/M 个物料` 并提供「继续扫描其余物料」。

同理，**库位 / 批次 / 序列号没有任何列表读面**，选择器的选项由
`useWarehouseCodeCatalog` 从**系统里已真实存在的编码**派生（上架/拣货任务的起讫库位、
盘点执行的库位、出库单行的库位/批次/序列号、当前页已加载的台账行），
并在选择器底部注明「数据来自现有库存与仓储作业记录（暂无库位主数据）」——不冒充主数据。

> **后端补齐 §4 的读面后，`useInventorySiteStockOverview` 与 `useWarehouseCodeCatalog`
> 应当整体删除**，页面直接换成真实读面。这两个 composable 是补偿层，不是长期架构。

## 4. 后端缺口（按优先级）

### 4.1 【P0】全量库存行读面（分页）

首屏扫描的唯一替代品。建议新增 `ListStockLedgerQuery` 并在网关代理：

```
GET /api/inventory/v1/ledgers
GET /api/business-console/v1/inventory/ledgers      （网关 facade）

必填：organizationId, environmentId
可选：siteCode, skuCode, uomCode, locationCode, lotNo, serialNo,
      qualityStatus, ownerType, ownerId,
      includeZeroOnHand (默认 false), asOfDate
分页：page (默认 1), pageSize (默认 50, 1..200)
排序：sortBy = skuCode | locationCode | expiryDate | availableQuantity，sortDir = asc | desc

响应 StockLedgerResponse:
  items: StockLedgerLineResponse[]
  totalCount, page, pageSize
  skuCount, locationCount            // 可加的计数，供概览卡使用；不要返回跨物料数量合计

StockLedgerLineResponse:
  skuCode, skuDisplayName, uomCode, siteCode, locationCode,
  lotNo?, serialNo?, qualityStatus, ownerType, ownerId?,
  productionDate?, expiryDate?, shelfLifeDays?, expiryDateSource?,
  isExpired, isBlocked, blockReasonCode?, blockReason?,
  movementAllowed, movementBlockReasonCode?, movementBlockReason?,
  countAllowed, countBlockReasonCode?, countBlockReason?,
  onHandQuantity, reservedQuantity, availableQuantity, inventoryValue
```

要点：
- 字段沿用现有 `StockAvailabilityLineResponse`，**额外补 `skuCode` / `skuDisplayName` / `uomCode` / `siteCode`**
  （现有行级 DTO 不带这些，因为查询已按物料定死）。
- 网关现有 `BusinessConsoleInventoryAvailabilityResponse` **丢掉了下游的 `inventoryValue`**，
  新 facade 请勿重复这个遗漏。

### 4.2 【P0】库位主数据读面

库位是本域出现频次最高的手输字段（库存 3 页 + WMS 6 页共 16 处）。
现状：只有 `POST /api/inventory/v1/locations`（创建），**网关连这个都没代理**，也没有任何 GET。

```
GET /api/inventory/v1/locations
GET /api/business-console/v1/inventory/locations     （网关 facade）

必填：organizationId, environmentId
可选：siteCode, warehouseCode, zoneCode, locationType, isActive, keyword
分页：page, pageSize

响应行：locationCode, displayName, siteCode, warehouseCode?, zoneCode?,
        locationType, isActive, capacityUom?, capacityQuantity?
```

同时请把 `POST /locations` 一并代理到网关，否则前端无法引导用户"去新建库位"。

### 4.3 【P1】批次 / 序列号目录读面

```
GET /api/business-console/v1/inventory/lots
必填：organizationId, environmentId
可选：siteCode, skuCode, locationCode, qualityStatus, activeOnly, keyword
分页：page, pageSize
响应行：lotNo, skuCode, uomCode, siteCode, productionDate?, expiryDate?,
        qualityStatus, onHandQuantity, availableQuantity

GET /api/business-console/v1/inventory/serials     （同上，返回 serialNo + 所在库位 + 状态）
```

### 4.4 【P1】预留（Reservation）读面与网关代理

「批次与预留」页名字里有预留，但**预留目前完全没有读面**：Inventory 服务里
`StockReservation` 有聚合、有 5 个 POST 写面（创建 / FEFO / 释放 / 续期），
**一个 Query 都没有**，网关也未代理任何 `reservations/*`。

```
GET /api/business-console/v1/inventory/reservations
必填：organizationId, environmentId
可选：siteCode, skuCode, lotNo, locationCode, status, sourceService, sourceDocumentId, expiringBefore
分页：page, pageSize
响应行：reservationId, skuCode, uomCode, siteCode, locationCode, lotNo?, serialNo?,
        reservedQuantity, status, sourceService, sourceDocumentId, sourceDocumentLineNo?,
        createdAtUtc, expiresAtUtc?
```

另请把 `POST reservations/{id}/release` 与 `renew` 代理到网关，否则页面只能看不能动。

### 4.5 【P2】网关未代理的既有 WMS 读面
`replenishment-tasks`、`backorder-orders`、`wcs-dispatch-circuits` 在服务侧已存在读面，
网关未暴露，前端因此做不出补货与缺料看板。

### 4.6 【P2】其它域串进来的缺口
- **币种字典**：采购/销售/财务/维修工单多处硬编码 `CNY`，缺 currency 读面。
- **采购收货单列表**：只有写入口 `recordBusinessConsoleErpPurchaseReceipt`，无 list，
  导致应付来源只能挂采购订单号。
- **报警码定义主数据**：设备报警规则的 `alarmCode` 是规则产出的码，需要定义目录而非事件流水。

## 5. 验收清单（本域改动自检）

- [ ] 进入库存可用量 / 批次与预留，**不选任何条件就能看到表格**。
- [ ] 表格上方/下方写清已扫描覆盖范围，且「继续扫描」可用。
- [ ] 点物料能下钻，下钻后能一键返回全厂视图。
- [ ] 库位 / 批次 / 序列号 / 状态 **没有任何自由文本输入框**（新批次号等确需录入的除外，
      且必须给既有值建议）。
- [ ] 单位不可手输，跟随物料自动带出。
- [ ] 概览不出现跨物料的数量合计。
- [ ] 选择器空态写明数据来源与下一步（去哪里维护），不留悬念。
