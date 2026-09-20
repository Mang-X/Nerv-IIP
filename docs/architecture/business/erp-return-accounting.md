# ERP 退货会计边界

本文描述采购退货和销售 RMA 的当前补偿性会计事实边界。已记录收货、已过账凭证、已完成 WMS 移动、已匹配供应商发票和已形成 AR/AP 都保持不可变；退货通过新的退货/借项/贷项事实与平衡凭证补偿，而不是修改历史。

M2-L 清理前包含 facade/产品交付状态的原文冻结于 [`../../reports/m2-l-erp-return-accounting-rules-pre-clean-2026-09-07.md`](../../reports/m2-l-erp-return-accounting-rules-pre-clean-2026-09-07.md)。

## 事实所有权与触发

| 事实 | Owner | ERP 结果 |
| --- | --- | --- |
| 供应商退货 WMS 出库完成 | WMS | ERP 根据原采购收货/PO 行记录采购退货与会计补偿 |
| 客户 RMA WMS 入库完成 | WMS | ERP 记录仓库已收货事实，不因此直接产生贷项 |
| 客户退货检验通过/有条件放行 | Quality | ERP 形成贷项通知、冲减原 AR 并过账补偿凭证 |
| 客户退货检验拒绝 | Quality | ERP 记录拒绝处置，不改变 AR |

ERP 不读取 WMS、Quality、Inventory 数据库；跨域触发使用公开版本化事件、本地 inbox 和稳定业务幂等语义。

## 采购退货

1. WMS 退货出库引用原 ERP 收货和 PO 行；SKU/UOM、数量、scope 或剩余可退量不一致时 ERP 拒绝补偿。
2. 实物库存移除由 WMS/Inventory 链路拥有；ERP 只在仓储完成事实成立后记录财务/单据补偿。
3. 尚未开票部分冲回原 GR/IR 收货暂估：借 GR/IR、贷库存。
4. 已匹配发票部分通过供应商借项通知应用于匹配的未结 AP：借 AP、贷库存；不能超过对应未结金额。
5. 同一采购退货可以同时包含未开票与已开票补偿，但退货数量只记录一次，每份凭证必须平衡并引用稳定源退货身份。

## 销售 RMA

1. RMA 必须引用 ERP 销售订单行及源 AR；客户、SKU/UOM、数量和金额不能超出 ERP 拥有的未退交付/未结事实。
2. ERP 发布授权；WMS 拥有退货入库执行。仓储完成只推进收货事实，不自动代表可贷项。
3. Quality 的放行/拒绝是贷项资格事实；Quality 不直接修改 AR。
4. 合格 RMA 创建贷项通知并应用原 AR，过账借销售退回/折让、贷 AR；这是应收补偿，不是现金收款。
5. 重放 WMS/Quality 事件必须用稳定业务键定位既有 RMA、通知单、AP/AR 核销与凭证，不产生第二次财务影响。

## 边界

- ERP 拥有退货单据、借/贷项通知、AP/AR 核销与会计凭证。
- WMS 拥有仓储任务与入出库完成；Inventory 拥有库存过账；Quality 拥有检验与处置。
- BusinessGateway 是否暴露某一退货 facade、Business Console 是否已有对应页面，不改变上述事实 owner；UI/产品交付状态属于 Frontend/Product/Tracker，不写入 Architecture。
