# ERP 销售订单到 DemandPlanning 需求桥

本文只描述 ERP SalesOrder 生命周期到 DemandPlanning `DemandSource` 的**当前事实所有权、事件契约语义、版本收敛和预测冲减边界**。演示 seed、真实跨进程验证、脚本预算和诊断操作见 [`../../runbooks/erp-sales-order-demand-planning.md`](../../runbooks/erp-sales-order-demand-planning.md)；M2-L 清理前的混合正文冻结于 [`../../reports/m2-l-sales-order-to-demand-planning-pre-clean-2026-09-07.md`](../../reports/m2-l-sales-order-to-demand-planning-pre-clean-2026-09-07.md)。

## 事实所有权

ERP 拥有销售订单、客户、站点、行数量/UOM/要求交期、状态和单调递增订单业务版本。DemandPlanning 不读取 ERP schema，也不复制金额、信用或履约事实；它只消费 ERP 公共生命周期事件，维护订单级版本水位，并把有效订单行投影为 `demand_type=sales-order` 的需求来源。

`sales-order` demand type 由 ERP 集成拥有；Planning 手工需求不能伪装成销售订单来源。历史手工行或导入数据需要迁移分类时也不能形成假的 ERP 文档引用。

## 生命周期事件

ERP 的销售订单生命周期通过 released / changed / cancelled 事实表达。发货单登记交付后，ERP 另发布 delivery-registered 事实；它与生命周期事件共享订单版本和完整订单行快照。事件必须携带：

- organization/environment；
- 稳定 order id / order no；
- customer、site 与完整有效订单行快照；行快照包含订单数量和已交付数量；
- 单调递增 `orderVersion`；
- correlation/causation；
- 稳定业务幂等键。

DemandPlanning 只消费公开 contract，不引用 ERP Domain/Web/Infrastructure，也不通过 Gateway 查询 ERP 数据库来补齐缺失行。

## 版本收敛

1. 初次 released 建立/更新每个有效订单行的 DemandSource。
2. changed 与 delivery-registered 都是完整快照；只接受高于当前 watermark 的版本。交付数量必须介于零与订单数量之间，有效销售需求取订单数量减已交付数量；交付完的既有行归零并标记 fulfilled，未曾投影的零需求行不创建 DemandSource。数量/交期随新版本更新，快照中缺失或取消的既有行归零并保留取消来源状态。
3. cancelled 将订单下既有需求行归零并推进订单水位；低版本 release/change/delivery-registered 不能复活已取消需求。
4. 相同 consumer + idempotency key 只执行一次；合法但低版本的不同事件可以留下 inbox 审计，但不回滚投影。
5. 合法业务拒绝与 poison message 进入受控 DLQ/诊断路径；数据库或 transport 瞬态失败由消息基础设施重试，handler 不吞掉失败伪造成功。
6. MRP 只消费有效、正数量的需求投影；pegging 继续携带订单级 `source_reference`，并为新生成的销售需求 pegging 保留 `source_line_reference`，因此同单相同 SKU/交期的行仍可区分。旧 pegging 的行身份保持未知，不凭 SKU/交期推定归属。
7. 新版本使已投影需求的数量或交期变化、取消订单或移除订单行时，DemandPlanning 按订单号与行身份定位受影响的建议 pegging。整单取消，或订单全部既有行在当前快照中已归零或变化时，该订单旧 pegging 中行身份未知的份额也确定失效；仍有未变更的正数量行时保留未知份额，不猜归属。Open 建议移除失效份额；合批建议按 demand pegging 数量比例保留其它需求份额，全部失效则关闭建议。新需求由后续 MRP 计算产生建议。
8. 已接受并转为 MES 工单的建议保持原状态和工单引用；DemandPlanning 发布 `SalesOrderDemandChangedForWorkOrderIntegrationEvent`，携带受影响的需求引用、订单版本、取消标志和工单引用，由 MES 消费方决定后续处置，不自动取消工单。

## 预测与订单冲减

DemandPlanning 还拥有计划员维护的 Forecast：SKU、工厂/站点、UOM、预测期间、数量以及向前/向后订单冲减窗口属于 Planning 事实。销售订单只作为实际需求输入参与冲减，不取得 Forecast 所有权。

冲减按同 SKU 与工厂/站点匹配，并在存在权威 UOM 换算时统一到计划单位；订单日期必须落在配置的冲减窗口内。剩余 Forecast 数量进入 MRP。Forecast 的创建/更新同样使用稳定幂等语义，超时重试不得产生第二条预测或静默覆盖不同内容。

页面是否提供创建/编辑/删除/导入、字段布局和交互文案属于 Product/Frontend，不改变上述事实边界。

## 已发布 MPS 的需求消耗

MPS 行以 `BucketDate` 为单日计划窗。MRP 对同组织、环境、SKU、站点、日期的已发布 MPS 与有效销售需求只计一次：MPS 数量进入净需求，销售行在该日期由 MPS 覆盖，不再叠加。比较数量时按 SKU 的权威 UOM 换算为 MPS 单位；销售总量超过 MPS 的差额形成 `mps-sales-excess` 计划异常，不生成额外采购或工单建议。其它日期或站点的销售需求仍独立进入 MRP。

预测期间内有同 SKU、站点的已发布 MPS 时，该期间预测由 MPS 替代，不再以预测剩余量叠加；没有 MPS 的期间继续按上述预测与订单冲减规则计算。草稿和已评审但未发布的 MPS 不参与消耗。

## 一致性不变量

1. ERP 订单详情只由 ERP 拥有；Planning 的订单投影必须能回到稳定 source reference/version。
2. 同一订单版本的重复、乱序或迟到事件不会把 watermark 倒退。
3. cancel 是有版本的业务事实，不通过删除 DemandSource 表达。
4. 不使用跨 schema 外键或同步数据库读取完成收敛。
5. Gateway/UI 聚合不成为订单或 DemandSource owner。
