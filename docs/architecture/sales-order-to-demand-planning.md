# ERP 销售订单到 DemandPlanning 需求桥

## 边界与事实源

ERP 拥有销售订单、客户、站点、行数量/单位/要求交期、状态和单调递增的订单业务版本。DemandPlanning 不读取 ERP schema，也不复制订单金额或履约事实；它只消费 ERP 公共生命周期事件，维护订单级版本水位，并把每个有效订单行投影为 `demand_type=sales-order` 的 DemandSource。

ERP 发布三个 v1 具体事件：`SalesOrderReleasedIntegrationEvent`、`SalesOrderChangedIntegrationEvent`、`SalesOrderCancelledIntegrationEvent`。每个事件携带完整订单行快照、organization/environment、stable order id/no、customer、site、order version、correlation/causation，以及只含 ASCII 字母数字、冒号、下划线和连字符的稳定幂等键。

## 收敛规则

1. 初次释放（含信用冻结解除）建立或更新每个非取消订单行的 DemandSource。
2. changed 是完整快照；数量/交期按更高 `orderVersion` 更新，快照中缺失或标记取消的既有行归零并保留 `source_status=cancelled`。
3. cancelled 将订单下所有既有需求行归零并保留取消状态；订单水位墓碑阻止迟到的低版本 release/change 复活需求。
4. 相同 consumer + idempotency key 只处理一次；合法但低版本的不同事件仍写 inbox 审计，却不回滚投影。
5. envelope 或业务字段错误进入 DemandPlanning 持久 DLQ；数据库/网络瞬态错误仍由 CAP 重试，handler 不把业务拒绝变成 poison message。
6. MRP 只读取 `source_status=active AND quantity>0` 的需求。pegging 和生产建议沿用 `source_reference`，因此可回溯到 ERP 订单号。

## 可复用演示前置路径：SO-DEMO-001

1. 在同一 organization/environment 准备 active 客户 `CUST-001`，配置足够信用额度，或先走信用冻结后审批释放路径。
2. 准备 active SKU `SKU-FG-A`、UOM `EA`、站点 `SITE-001`，以及能生成生产建议的 released ProductionVersion/MBOM/Routing 和必要库存快照。
3. 创建并批准报价，至少包含一行 `SKU-FG-A`、正数量和要求交期。
4. 从报价创建销售订单 `SO-DEMO-001`，显式提交 `siteCode=SITE-001`；订单释放后等待 DemandPlanning consumer。
5. 在 `/planning` 验证来源 `SO-DEMO-001`、订单行、客户、版本与 active 状态；点击来源可进入 `/erp/sales/orders?keyword=SO-DEMO-001`。
6. 运行覆盖要求交期的 MRP，验证 pegging/计划工单建议的 demand source reference 为 `SO-DEMO-001`。
7. 重放相同事件，再依次投递更高版本 change、低版本 change 和 cancel；验证重复/乱序不回滚，新数量可见，取消后 quantity=0/status=cancelled 且后续 MRP 不再使用该需求。

`sales-order` demand type 由 ERP 集成独占；Planning 手工录入不再提供该类型，迁移会把没有上游文档 ID 的旧手工 `sales-order` 行归类为 `manual`，避免和真实订单行重复计数。

真实跨进程验收运行 `scripts/verify-erp-sales-order-demand-planning.ps1`。脚本使用一次性 PostgreSQL 数据库保存 ERP outbox 与 DemandPlanning inbox/watermark/DemandSource，使用 Redis CAP transport，分别启动 MasterData、ERP、DemandPlanning 进程。独立 probe 以不同 transport key 注入同一业务版本重放和低版本迟到消息，强制越过 inbox 首层去重，并等待两个 event id 都进入持久 inbox 后再验证 watermark 不回滚；完全相同 idempotency key 的重复投递由真实 Redis 消费套件单独覆盖。证据输出到 `artifacts/acceptance/man517/sales-order-demand-planning-evidence.json`，finally 删除测试库并停止托管进程，但不会停止脚本启动前已运行的基础设施。不要用 InMemory provider 代替该验收。
