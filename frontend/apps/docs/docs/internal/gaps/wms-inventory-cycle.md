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

- 当前 WMS/Inventory 文档只描述基础收发、库存移动、盘点和过账失败闭环。
- FEFO/FIFO、directed putaway、LPN/HU 和 ASN 差异仍不能写成已交付。
- BarcodeLabel 后端能力较丰富，但正式前端页面仍后置，需要独立文档与入口。
