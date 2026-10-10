# MES / APS 走查证据与活动记录（2026-10-10）

对应[报告](2026-10-10-mes-aps-ui-ux-walkthrough.md)、[计划](2026-10-10-mes-aps-ui-ux-plan.md)、[操作文档](2026-10-10-mes-aps-ui-ux-operations.md)。这是本地冻结审计证据，未提交PR或上传外部tracker。

## 证据边界

固定SHA `f7573754ed98bdb0583e8465057de625b535ca49`，场景 `issue-1912-real-machine-walkthrough`，session `nerv-195b-a3a1a9`。真实页面、HTTP服务、PostgreSQL及Redis/CAP实际运行；没有使用业务mock。公开API创建WO2只属于fixture，不属于UI转单证明。角色是admin，角色视角不等于最小权限角色验收。

原始页面截图与DOM在 `artifacts/mes-aps-walkthrough-2026-10-10/`；此目录被gitignore，不随报告自动提交。截图只含隔离审计数据。`network.json`只留URL/status/requestId，没有授权头、正文、token/password。工程/工单fixture文件归一成指定字段事实，未留原始HTTP正文或凭据。原始服务日志未复制进文档。

页面最先使用T3协作预览，随后明确返回无可用automation host，按工具说明改用Chrome CUA；没有通过隐藏浏览器fetch替代页面操作。一次Vite依赖优化504在重载后恢复，归为启动预热，不列为产品缺陷。

## 服务日志证据摘要

服务业务日志通过Aspire MCP `list_console_logs` 实际读取，**未取得其落盘绝对路径**。下表是本轮归一摘要，不伪造可下载raw log链接；原工具结果在本线程工具记录。AppHost启动日志 `/Users/mang/.aspire/logs/cli_20261010T081042194_detach-child_4b11a741c42c4711976f8e7a21f441a8.log` 不包含这些业务日志，不能替代它们。

| 本地时间（Asia/Shanghai） | 事实 | 关联 |
| --- | --- | --- |
| 16:14–16:15 | 新需求类型forecast；MRP查询排除demand_sources.forecast | U01 |
| 16:18:17 | PostgreSQL22001；来源字符串141字符，列限128；已计算生产2/原料2.8，最终保存失败 | B01 run01a124e4-2ec2-747c-81cf-2eaacd9d0f13 |
| 16:24:05 | ERP人工/实际时间事件缺WC-ROD费率；machine事件缺机器事实 | B03，报工PRPT-20261010-000001 |
| 16:30:34 | BG POST downtime403，IAM/上下文读取200；同trace CORS OriginNotAllowed | B08 trace a3bd0672e0eb2879287126daa9e97b21 |
| 16:33:24 | UI费率维护成功 | WC-ROD-01 10CNY/h有效10-10 |
| 16:33:39 | ERP replay POST HTTP200但NoHandler；保持Pending | B03 trace7459af879fcae8a8466ae8037a59de5b，deadletter01a124e9-82b4-792e-871d-afba64f2de1d |
| 16:41:06.633–.735 | MES撤销CAP消费者100ms成功，正确plan/revision1的inbox/watermark写入，无工序UPDATE | B04 plan-01a124ecbb7072ea9f8077e320abfa54 |

MRP来源内容是服务/计数摘要，无客户正文：`inventory-http:2;erp-purchase-orders:0;accepted-purchases:0;mes-work-orders:0;master-data-planning-parameters:2;master-data-uom-conversions:0`。

## 关键静态定位（固定SHA）

这些链接解释实际故障的实现原因；静态风险仍保持静态身份，不因有代码路径就记为运行通过。

| 条目 | 核对点 | Producer |
| --- | --- | --- |
| B01 | MRP来源列限128 | [MrpRunEntityTypeConfiguration.cs:18](https://github.com/Mang-X/Nerv-IIP/blob/f7573754ed98bdb0583e8465057de625b535ca49/backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/EntityConfigurations/MrpRunEntityTypeConfiguration.cs#L18) |
| B01/U01 | 来源字符串/forecast排除与独立预测输入 | [PlanningInputAdapters.cs:262](https://github.com/Mang-X/Nerv-IIP/blob/f7573754ed98bdb0583e8465057de625b535ca49/backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Planning/PlanningInputAdapters.cs#L262)；[PlanningInputAdapters.cs:435](https://github.com/Mang-X/Nerv-IIP/blob/f7573754ed98bdb0583e8465057de625b535ca49/backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Planning/PlanningInputAdapters.cs#L435) |
| B02 | 急单单任务 vs 正常转单完整路由 | [CreateRushWorkOrderCommand.cs:155](https://github.com/Mang-X/Nerv-IIP/blob/f7573754ed98bdb0583e8465057de625b535ca49/backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/WorkOrders/CreateRushWorkOrderCommand.cs#L155)；[MesWorkbenchCommands.cs:725](https://github.com/Mang-X/Nerv-IIP/blob/f7573754ed98bdb0583e8465057de625b535ca49/backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs#L725) |
| B03 | 人工/实绩事件缺费率时return及成本等待 | [WorkOrderCostIntegrationEventHandlers.cs:173](https://github.com/Mang-X/Nerv-IIP/blob/f7573754ed98bdb0583e8465057de625b535ca49/backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventHandlers/WorkOrderCostIntegrationEventHandlers.cs#L173) |
| B03 | ERP有replay端点，未注册可处理该事件handler | [ErpDeadLetterEndpoints.cs:25](https://github.com/Mang-X/Nerv-IIP/blob/f7573754ed98bdb0583e8465057de625b535ca49/backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Endpoints/DeadLetters/ErpDeadLetterEndpoints.cs#L25) |
| B04 | Revoke加载plan未IncludeAssignments | [RevokeSchedulePlanCommand.cs:44](https://github.com/Mang-X/Nerv-IIP/blob/f7573754ed98bdb0583e8465057de625b535ca49/backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Commands/RevokeSchedulePlanCommand.cs#L44) |
| B04 | 从Assignments投影AffectedOperations | [SchedulingIntegrationEventConverters.cs:318](https://github.com/Mang-X/Nerv-IIP/blob/f7573754ed98bdb0583e8465057de625b535ca49/backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/IntegrationEventConverters/SchedulingIntegrationEventConverters.cs#L318) |
| B04 | 空operationIds跳过MES排程清理 | [SchedulingPlanReleasedIntegrationEventHandler.cs:330](https://github.com/Mang-X/Nerv-IIP/blob/f7573754ed98bdb0583e8465057de625b535ca49/backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEventHandlers/SchedulingPlanReleasedIntegrationEventHandler.cs#L330) |
| B05 | 工序作为sourceDocument且ncrId写死null | [MesWorkbenchQueries.cs:2149](https://github.com/Mang-X/Nerv-IIP/blob/f7573754ed98bdb0583e8465057de625b535ca49/backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Workbench/MesWorkbenchQueries.cs#L2149) |
| B06 | 缺陷事件无SKU，NCR写占位 | [MesIntegrationEventConverters.cs:703](https://github.com/Mang-X/Nerv-IIP/blob/f7573754ed98bdb0583e8465057de625b535ca49/backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEventConverters/MesIntegrationEventConverters.cs#L703)；[DefectRaisedIntegrationEventHandlerForOpenNcr.cs:22](https://github.com/Mang-X/Nerv-IIP/blob/f7573754ed98bdb0583e8465057de625b535ca49/backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/IntegrationEventHandlers/DefectRaisedIntegrationEventHandlerForOpenNcr.cs#L22) |
| B08 | BG停机EnsureWorkCenterAccess | [BusinessConsoleMesEndpoints.cs:2588](https://github.com/Mang-X/Nerv-IIP/blob/f7573754ed98bdb0583e8465057de625b535ca49/backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Mes/BusinessConsoleMesEndpoints.cs#L2588) |
| U02 | 响应data读取异常 | [OrderUrgencyMesDueDateProvider.cs:48](https://github.com/Mang-X/Nerv-IIP/blob/f7573754ed98bdb0583e8465057de625b535ca49/backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Urgency/OrderUrgencyMesDueDateProvider.cs#L48) |
| U04 | 仅报废强制消耗/入库预期物料数 | [MesProductionCommands.cs:298](https://github.com/Mang-X/Nerv-IIP/blob/f7573754ed98bdb0583e8465057de625b535ca49/backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Production/MesProductionCommands.cs#L298)；[MesProductionCommands.cs:423](https://github.com/Mang-X/Nerv-IIP/blob/f7573754ed98bdb0583e8465057de625b535ca49/backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Production/MesProductionCommands.cs#L423) |
| U05 | 收料Requested/PartiallyReceived门禁，非WMS完成门禁 | [MesWorkbenchCommands.cs:1118](https://github.com/Mang-X/Nerv-IIP/blob/f7573754ed98bdb0583e8465057de625b535ca49/backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs#L1118) |
| U13 | 设备缺失降级与站点时区snapshot | [ProductionReportOeeDimensionSnapshotProvider.cs:34](https://github.com/Mang-X/Nerv-IIP/blob/f7573754ed98bdb0583e8465057de625b535ca49/backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Production/ProductionReportOeeDimensionSnapshotProvider.cs#L34) |
| S01 | Paused恢复readiness静态风险 | [MesOperationTaskActionReadinessEvaluator.cs:164](https://github.com/Mang-X/Nerv-IIP/blob/f7573754ed98bdb0583e8465057de625b535ca49/backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Readiness/MesOperationTaskActionReadinessEvaluator.cs#L164) |

前端质量错误链接：`frontend/apps/business-console/src/pages/mes/quality.vue:136-137,485-491` 判断/^WO/，把工序ID拼成工单URL；`:501-513` NCR链接依赖ncrId，顶部入口又受ncrCount>0控制。

ERP replay handler全仓检索实际仅Notification/Scheduling/MES实现；Common `IntegrationEventReliability.cs:829-859` 无CanReplay匹配时返回NoHandler并保留状态。此处不是HTTP失败；UI重放结果才是业务结果。

## 隔离数据活动与终态

| 操作 | 审计对象 | 终态/边界 |
| --- | --- | --- |
| 需求UI录入 | MPS-20261010-ROD-01，SF-ROD-01，SITE-001，2pcs，due10-20 | 一条forecast需求；非销售订单真实旅程 |
| MPS UI创建/评审/发布 | SF-ROD-01，2pcs，10-20 | 已发布 |
| MRP UI运行两次 | 10-10至11-09 | 首轮完成0建议；第二轮失败 |
| 急单UI创建 | WO-20261010-000001，1pcs | 仅OP10；释放后无排程也可开工，最终Completed |
| 原料UI入账 | AUDIT-MES-APS-20261010，RM-BAR-01，10kg，无批次 | 原料库存已入账；不是采购收货验收 |
| 领料/收料UI | MIR-20261010-000001，1.4kg | 已收；WMS对应出库/拣选仍待处理 |
| 报工UI | PRPT-20261010-000001，good1，scrap0 | LOT-PRPT-20261010-000001；人工/机器0.0088h，无工人/班次/批次消耗 |
| 完工入库UI | FGR-20261010-000001，1pcs | 等待ERP成本0/1；未确认Inventory成品终态 |
| 公共API fixture | WO-20261010-000002，AUDIT-ROD-20261010-01，1pcs | 路由10/20/30；证明下游，不证明MRP建议采纳 |
| APS UI生成4方案 | 单单首版eb95…；批量ec359…；重预览ecbb…；候选f89b… | ecbb发布后撤销；f89b仍草稿/未发布 |
| WO2操作UI | OP10开工→暂停；OP20尝试开工拒绝 | OP10Paused；OP20/30Queued且撤销后仍Scheduled |
| 缺陷UI | DEF-20261010-000001，WO2 OP10，尺寸超差1 | CAP创建NCR-org001-envdev-01a124f6a459749496b5643e4b5b5a4b；未处置/关闭 |
| 费率UI恢复尝试 | WC-ROD-01，10CNY/h，valid10-10 | 修订成功；ERP重放NoHandler，未修复代码 |
| 仅预览/取消 | 检验、交接、冲销、停机失败 | 不产生错SKU检验/交接/负向报工；停机未写入 |

没有实施产品修复或直接SQL修正，没有删已有业务数据、创建票据、发布消息、创建/合并PR。测试数据只属于本次独立runtime。停栈清理不会清理任何其它工作树/服务。

## 截图与页面快照索引

00系列是启动后的亮色布局；01–48为后续实际流程（其中12有同阶段两张）。文件名是采集顺序，不承诺截图时刻与每个toast相同。07实际上是刷新后的Ready；08/21只有失败后输入上下文；12显示提交后重开表单；15为发布后历史列表；28同时证明NoHandler和表格重叠；43是撤销后MES权威读回。直接错误画面不足的项目在报告中明确，不填造截图。

| 文件 | 本地截图 |
| --- | --- |
| 00-aps-1024.png | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/00-aps-1024.png) |
| 00-aps-1280.png | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/00-aps-1280.png) |
| 00-mes-overview-1280.png | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/00-mes-overview-1280.png) |
| 00-workorders-1280.png | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/00-workorders-1280.png) |
| 01-demand-mrp-zero.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/01-demand-mrp-zero.jpg) |
| 02-mrp-failed.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/02-mrp-failed.jpg) |
| 03-work-order-detail.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/03-work-order-detail.jpg) |
| 04-audit-stock.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/04-audit-stock.jpg) |
| 05-wms-pending.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/05-wms-pending.jpg) |
| 06-picking-zero.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/06-picking-zero.jpg) |
| 07-received-kit-stale.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/07-received-kit-stale.jpg) |
| 08-rush-scheduling-failed.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/08-rush-scheduling-failed.jpg) |
| 09-operation-unscheduled.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/09-operation-unscheduled.jpg) |
| 10-rush-completed.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/10-rush-completed.jpg) |
| 11-receipt-dead-end.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/11-receipt-dead-end.jpg) |
| 12-receipt-awaiting-cost.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/12-receipt-awaiting-cost.jpg) |
| 12-receipt-posted.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/12-receipt-posted.jpg) |
| 13-aps-three-operations.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/13-aps-three-operations.jpg) |
| 14-aps-table-edit.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/14-aps-table-edit.jpg) |
| 15-aps-published.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/15-aps-published.jpg) |
| 16-scope-unavailable.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/16-scope-unavailable.jpg) |
| 17-mes-scheduled.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/17-mes-scheduled.jpg) |
| 18-predecessor-block.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/18-predecessor-block.jpg) |
| 19-dispatch-no-worker.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/19-dispatch-no-worker.jpg) |
| 20-quality-wrong-sku.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/20-quality-wrong-sku.jpg) |
| 21-downtime-failed.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/21-downtime-failed.jpg) |
| 22-andon.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/22-andon.jpg) |
| 23-handover-no-team.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/23-handover-no-team.jpg) |
| 24-traceability.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/24-traceability.jpg) |
| 25-foundation-blocked.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/25-foundation-blocked.jpg) |
| 26-rate-recovery.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/26-rate-recovery.jpg) |
| 27-cost-deadletters.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/27-cost-deadletters.jpg) |
| 28-replay-unsupported.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/28-replay-unsupported.jpg) |
| 29-dispatch.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/29-dispatch.jpg) |
| 30-line-side-unconsumed.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/30-line-side-unconsumed.jpg) |
| 31-wip.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/31-wip.jpg) |
| 32-report-record.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/32-report-record.jpg) |
| 33-daily-report.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/33-daily-report.jpg) |
| 34-quality-defect.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/34-quality-defect.jpg) |
| 35-quality-broken-link.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/35-quality-broken-link.jpg) |
| 36-capacity.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/36-capacity.jpg) |
| 37-overview.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/37-overview.jpg) |
| 38-aps-gantt.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/38-aps-gantt.jpg) |
| 39-resource-board.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/39-resource-board.jpg) |
| 40-aps-table.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/40-aps-table.jpg) |
| 41-candidate-draft.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/41-candidate-draft.jpg) |
| 42-aps-revoked.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/42-aps-revoked.jpg) |
| 43-revoke-mes-readback.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/43-revoke-mes-readback.jpg) |
| 44-report-reversal-preview.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/44-report-reversal-preview.jpg) |
| 45-ncr-unresolved.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/45-ncr-unresolved.jpg) |
| 46-ncr-disposition.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/46-ncr-disposition.jpg) |
| 47-receipt-final.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/47-receipt-final.jpg) |
| 48-workorder-urgency.jpg | [打开](../../../artifacts/mes-aps-walkthrough-2026-10-10/screenshots/48-workorder-urgency.jpg) |

页面DOM快照位于同一artifact目录 `pages/`，包含计划/驾驶舱/工序/日报/质量/NCR/候选/撤销读回/收料/追溯/死信等。快照本身不是截图，不等价于视觉检查。

## 验证、清理和未运行

- 实际：Business Console vue-tsc/Vite构建通过；真实页面和服务闭环尝试、定向源码与GitHub去重、Markdown本地链接/源文件存在性核对。
- 未运行：自动化测试、正式provider/FullChain Authority lanes、PR CI、main CI、真实角色/PDA/设备/打印机、全生产闭环终态。无测试policy-skip声明；这次是主动未执行测试，而不是policy绿色skip。
- 已执行 `pwsh -NoProfile -File ./nerv.ps1 fullstack stop -SessionId nerv-195b-a3a1a9`；16:54:34结果 `state=Stopped remaining=0`。本次session的容器/网络/隔离卷已清理，测试数据随隔离卷移除；截图和本报告保留，其它session/容器未清理。持有runtime的临时PTY已结束，临时凭据文件已删除。
