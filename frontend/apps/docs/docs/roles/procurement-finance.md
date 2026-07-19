# 采购与财务：第一周路径

采购与财务把采购、销售单据走通并归集财务事实。第一周先走通下面 8 条路径，每条路径都以一个
业务结果收尾。状态口径见[按角色入门](/roles/)。

## 第一周路径

| #   | 路径（页面操作串）                                                                                                                | 业务结果                 | 状态                                                                                                  |
| --- | --------------------------------------------------------------------------------------------------------------------------------- | ------------------------ | ----------------------------------------------------------------------------------------------------- |
| 1   | 在 `/erp/procurement/rfqs` 发询价，在 `/erp/procurement/supplier-quotations` 录报价，再到 `/erp/procurement/purchase-orders` 下单 | 询比价到采购订单成单     | ✅ 可用                                                                                               |
| 2   | 在 `/erp/procurement/receipts` 跟踪采购收货                                                                                       | 采购到货与入库事实对得上 | ✅ 可用                                                                                               |
| 3   | 把 MRP 采购建议转成采购申请与采购订单                                                                                             | 计划缺口自动进入采购执行 | ⛔ 缺口：转化流程未打通（[#711](https://github.com/Mang-X/Nerv-IIP/issues/711)）                      |
| 4   | 变更或取消已下达的采购/销售订单                                                                                                   | 单据变更受控且下游同步   | ⛔ 缺口：变更与取消流程未交付（[#712](https://github.com/Mang-X/Nerv-IIP/issues/712)）                |
| 5   | 处理采购退货与销售退货（RMA）                                                                                                     | 退货与贷项/借项凭证闭环  | ⛔ 缺口：退货体系未交付（[#713](https://github.com/Mang-X/Nerv-IIP/issues/713)）                      |
| 6   | 在 `/erp/sales/quotations` 报价，在 `/erp/sales/orders` 成单（含信用检查），再到 `/erp/sales/deliveries` 发货                     | 销售报价到发货成链       | ✅ 可用                                                                                               |
| 7   | 在 `/erp/finance/ar-ap` 核对应收应付，在 `/erp/finance/vouchers` 查凭证                                                           | 业务单据可下钻到财务事实 | ✅ 可用                                                                                               |
| 8   | 在 `/erp/finance/cost-candidates` 归集成本候选                                                                                    | 生产消耗进入成本核算视野 | 🟡 部分可用：科目表与 WIP 成本核算框架未交付（[#714](https://github.com/Mang-X/Nerv-IIP/issues/714)） |

## 发货完成与应收口径

- ERP 交付单下达到 WMS 后保持 `released`；WMS 回写实际出库行数量后，ERP 按行累计已发数量。仍有未发数量时状态为 `partially-shipped`，所有行均发完后才是 `completed`。
- `shippedAtUtc` 表示首次正数发货时间，`completedAtUtc` 表示所有交付行完成时间；交付单列表同时返回每行的计划数量和已发数量。
- 应收只在交付单首次进入 `completed` 时创建。同一 WMS 完成事件重试不会重复累计发货数量，也不会重复生成应收。

## 从哪里学

- 概念解释中的 ERP 采购/销售/财务流程图尚未提供，随后续功能批次补齐；先按上表页面串对照操作。
- 与计划联动（MRP 建议来源）见教程：[需求计划到完工入库](/getting-started/planning-to-finished-goods)
