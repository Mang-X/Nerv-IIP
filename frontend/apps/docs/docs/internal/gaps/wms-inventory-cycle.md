# 内部缺口记录：仓储收发与库存闭环

该页面是内部缺口记录，不作为官网 Docs 对外营销文案。

## 证据页面

- `/getting-started/wms-inventory-cycle`
- `frontend/apps/business-console/src/pages/wms`
- `frontend/apps/business-console/src/pages/inventory`
- `docs/architecture/implementation-readiness.md` WMS、Inventory、BarcodeLabel 段落

## 建议 issue 标题

- `[WMS/Inventory] 仓储上手路径补齐 FEFO/FIFO、LPN/HU 和条码前端闭环说明`

## 缺口记录

### 能力缺失

- FEFO/FIFO、directed putaway、LPN/HU、ASN expected/received 差异和完整仓储分析仍不能写成已交付。
- Inventory 已有 `StockLocation` 持久事实，但缺少面向 WMS 输入选择器的授权可读目录；全量批次候选目录同样未交付。
- WMS 盘点聚合当前没有 lot 语义，只能按库位筛选；不能复用收货或发货批次候选伪造盘点批次。

### 操作不连贯

- 收货、上架、库存可用量、拣货和出库虽然都有页面，但跨页上下文仍需要用户对照 SKU、站点和库位。WMS 近期作业候选能减少常用值手填，但不能覆盖从未进入近期作业窗口的新库位或历史批次。

### 手填 ID

- 出库、拣货、库存移动和盘点排查仍可能要求用户从列表复制单号或 SKU；在权威库位/全量批次目录交付前，近期作业候选之外的值仍缺少完整选择来源。

### 术语不清

- WMS 的作业状态、Inventory 的 movement 状态和 WCS 的任务状态需要在页面上进一步区分，避免用户把过账失败误判为作业未完成。

### 反馈不足

- 库存不足、预留失败和库存过账失败后的修正建议仍不够直达；需要从失败行跳转到库存可用量、移动明细或重试动作。
