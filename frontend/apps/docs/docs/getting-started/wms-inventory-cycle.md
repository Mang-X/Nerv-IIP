# 仓储收发与库存闭环

这条路径帮助仓储、库存和现场作业角色理解当前 WMS 与 Inventory 的基础闭环。它覆盖收货、上架、库存可用量、拣货、出库、盘点和库存移动，不把 FEFO/FIFO 高级拣货或完整 LPN/HU 写成已实现。

## 适用角色

- 仓库收货员：处理到货、收货和上架。
- 仓库发货员：处理拣货、出库和 WCS 任务。
- 库存管理员：查看可用量、库存移动和盘点差异。
- PDA 一线人员：用移动页面执行轻量收货、上架、拣货、盘点和报工相关动作。

## 前置资料

- MasterData 中已有仓库、库位、SKU、UOM 和供应商/客户资料。
- Inventory 已能处理 movement requested、posted 和 failed 事实。
- WMS 与 Inventory 的公共契约和 BusinessGateway facade 已可用。

## 页面入口

| 目标 | Business Console 路由 |
| --- | --- |
| WMS 总览 | `/wms` |
| 收货 | `/wms/inbound` |
| 上架 | `/wms/putaway` |
| 拣货 | `/wms/picking` |
| 出库 | `/wms/outbound` |
| 盘点 | `/wms/counts` |
| WCS 任务 | `/wms/wcs` |
| 库存可用量 | `/inventory/availability` |
| 库存移动 | `/inventory/movements` |
| 库存盘点 | `/inventory/counts` |

## 操作步骤

1. 在 `/wms/inbound` 登记或查看收货单，确认供应商、SKU、数量和批次/序列要求。
2. 在 `/wms/putaway` 将已收货物料上架到库位。
3. 在 `/inventory/availability` 查询 SKU 可用量，区分现有量、预留量和可用量。
4. 在 `/wms/picking` 对出库单创建或查看拣货任务；当前拣货会通过 Inventory 内部服务 API 预留库存。
5. 在 `/wms/outbound` 完成出库，Inventory 按 reservation id 分配预留并过账。
6. 在 `/inventory/movements` 查看库存移动结果；失败时根据失败原因重试或修正单据。
7. 在 `/wms/counts` 或 `/inventory/counts` 做盘点，确认差异并形成调整。

## 业务对象/单据流

Inbound Order -> Receiving -> Putaway -> Stock Balance -> Reservation -> Picking Task -> Outbound Order -> Inventory Movement -> Posted/Failed Event -> Count Adjustment。

## 状态变化

- 收货单从待收货到已收货，再进入上架。
- 出库单从待拣货、已预留、拣货中到出库完成；过账失败时进入 InventoryPostingFailed。
- 库存移动从 requested 到 posted 或 failed；失败单据可在允许状态下重试。

## 成功结果

- 收货后库存可用量可查询，上架结果能解释库位变化。
- 出库前库存预留被记录，出库完成后库存移动过账。
- 盘点差异有可追踪的任务和调整事实。

## 常见失败/空态

- 可用量为空：确认 SKU、仓库、组织和环境范围，不使用未 seeded 的演示编号。
- 出库预留失败：库存不足或 SKU/库位范围不匹配。
- 库存过账失败：查看 `inventory.StockMovementPostingFailed` 对应诊断。
- WCS 页面无任务：当前 WCS 仍是任务状态读面和基础 dispatch/fail/retry/complete 事实，不代表设备已在线。

## 当前限制

- FEFO/FIFO 拣货、ASN expected/received 差异、directed putaway、LPN/HU 仍未作为完整能力交付。
- 条码标签已有后端能力和 PDA/扫码相关基础，但正式 BarcodeLabel 前端页面仍后置。
- MinIO/S3 multipart 和对象存储直传不属于当前 WMS 上手路径。

[内部缺口记录](/internal/gaps/wms-inventory-cycle)
