# ERP 销售订单到 DemandPlanning 需求桥

本文只描述 ERP SalesOrder 生命周期到 DemandPlanning `DemandSource` 的**当前事实所有权、事件契约语义、版本收敛和预测冲减边界**。演示 seed、真实跨进程验证、脚本预算和诊断操作见 [`../../runbooks/erp-sales-order-demand-planning.md`](../../runbooks/erp-sales-order-demand-planning.md)；M2-L 清理前的混合正文冻结于 [`../../reports/m2-l-sales-order-to-demand-planning-pre-clean-2026-09-07.md`](../../reports/m2-l-sales-order-to-demand-planning-pre-clean-2026-09-07.md)。

## 事实所有权

ERP 拥有销售订单、客户、站点、行数量/UOM/要求交期、状态和单调递增订单业务版本。DemandPlanning 不读取 ERP schema，也不复制金额、信用或履约事实；它只消费 ERP 公共生命周期事件，维护订单级版本水位，并把有效订单行投影为 `demand_type=sales-order` 的需求来源。

`sales-order` demand type 由 ERP 集成拥有；Planning 手工需求不能伪装成销售订单来源。历史手工行或导入数据需要迁移分类时也不能形成假的 ERP 文档引用。

## 生命周期事件

ERP 的销售订单生命周期通过 released / changed / cancelled 事实表达。事件必须携带：

- organization/environment；
- 稳定 order id / order no；
- customer、site 与完整有效订单行快照；
- 单调递增 `orderVersion`；
- correlation/causation；
- 稳定业务幂等键。

DemandPlanning 只消费公开 contract，不引用 ERP Domain/Web/Infrastructure，也不通过 Gateway 查询 ERP 数据库来补齐缺失行。

## 版本收敛

1. 初次 released 建立/更新每个有效订单行的 DemandSource。
2. changed 是完整快照；只接受高于当前 watermark 的版本。数量/交期随新版本更新，快照中缺失或取消的既有行归零并保留取消来源状态。
3. cancelled 将订单下既有需求行归零并推进订单水位；低版本 release/change 不能复活已取消需求。
4. 相同 consumer + idempotency key 只执行一次；合法但低版本的不同事件可以留下 inbox 审计，但不回滚投影。
5. 合法业务拒绝与 poison message 进入受控 DLQ/诊断路径；数据库或 transport 瞬态失败由消息基础设施重试，handler 不吞掉失败伪造成功。
6. MRP 只消费有效、正数量的需求投影；pegging/计划建议继续携带稳定 `source_reference`，因此可追溯 ERP 订单而无需复制订单详情。

## 预测与订单冲减

DemandPlanning 还拥有计划员维护的 Forecast：SKU、工厂/站点、UOM、预测期间、数量以及向前/向后订单冲减窗口属于 Planning 事实。销售订单只作为实际需求输入参与冲减，不取得 Forecast 所有权。

冲减按同 SKU 与工厂/站点匹配，并在存在权威 UOM 换算时统一到计划单位；订单日期必须落在配置的冲减窗口内。剩余 Forecast 数量进入 MRP。Forecast 的创建/更新同样使用稳定幂等语义，超时重试不得产生第二条预测或静默覆盖不同内容。

页面是否提供创建/编辑/删除/导入、字段布局和交互文案属于 Product/Frontend，不改变上述事实边界。

## 一致性不变量

1. ERP 订单详情只由 ERP 拥有；Planning 的订单投影必须能回到稳定 source reference/version。
2. 同一订单版本的重复、乱序或迟到事件不会把 watermark 倒退。
3. cancel 是有版本的业务事实，不通过删除 DemandSource 表达。
4. 不使用跨 schema 外键或同步数据库读取完成收敛。
5. Gateway/UI 聚合不成为订单或 DemandSource owner。
