# 齐套、下达与质量放行后的排程接续调查

调查日期：2026-10-17；范围：[#4462](https://github.com/Mang-X/Nerv-IIP/issues/4462)，母跟踪 [#4454](https://github.com/Mang-X/Nerv-IIP/issues/4454) 的 A08。Scope：spike；时间盒：最多 1 工作日，本次当日完成资料与源码调查。交付物为本报告；不修改生产代码，不代替产品裁决，不重新验收母票。

**结论：当前真实页面已证明的最短恢复路径仍是计划员锁定重预览、显式发布新版本、现场刷新原任务。质量合格不等于排程自动接续。** “正常进展使任务失效”是现有事件规则的结果，不能仅凭往返较多就认定规则错误。质量放行是否应缩小失效范围或保留原排程，仍待 MES／质量／排产产品负责人共同裁决。

## 基线与证据等级

| 证据 | 固定版本／来源 | 本报告可证明什么 |
| --- | --- | --- |
| 历史真实页面与公开读回 | 运行 `a9b625b297e7797c058e40b428c24bf611750daf`，2026-10-16，隔离会话 `nerv-a016-1016ab`；冻结资产提交 `a154cf38a774c5addc9090f17d86300c930cdd16` | A08 三类失效的实际症状、OP80 经人工接回后完成；不是本次重跑 |
| 本次静态核对 | `193a39fa4c8bac387bd0dfc77caa629dcbb71dea`；开工时本地 HEAD 与 `git ls-remote origin refs/heads/main` 一致 | 事件筛选、作用域、失效投影、MES 状态与发布门禁的实现事实；不证明当前运行环境已走通 |
| 既有验收与裁定 | [#4282](https://github.com/Mang-X/Nerv-IIP/issues/4282)、[#4288](https://github.com/Mang-X/Nerv-IIP/issues/4288)、[#3191](https://github.com/Mang-X/Nerv-IIP/issues/3191) 正文及评论 | 复用预览、锁定、对比、连续旅程及质量发布例外，不重建这些能力；旧 CI／验收不升级为本次通过 |

已读取的冻结来源：[A08 原报告][history-report]、[连续操作指南第 3–9 步][history-guide]、[规范化证据][history-evidence]。历史主单 `WO-20261016-000001`，产品 `FG-QJ-P1-L`，数量 2，OP10–80 共八道；11 张 `MIR-20261016-000001` 至 `000011` 收齐，齐套 Ready；OP70 检验记录 `01a144e1-6b7c-7d19-94a3-397c03368115`；最终 OP80 报工 `PRPT-20261016-000008`。这些是历史业务对象，不应在新环境照抄提交。

原规范化证据没有逐次排程 planId、失效行 ID、sourceEventId、correlation 或三次事件对应日志，因此**不能复算每一次收料究竟触发几张方案、几道任务，也不能证明事件先后顺序和耗时**。以下精确 reason 与作用域来自本次源码，不冒充旧运行逐事件取证。旧图中的 Ready 只证明当时齐套状态，不能独立证明失效原因。

本次用 `git diff --name-only a9b625b297e7797c058e40b428c24bf611750daf 193a39fa4c8bac387bd0dfc77caa629dcbb71dea -- <目标路径>` 对下述七个 producer 文件核对，输出为空：输入事件 handler、失效记录命令、发布命令、MES 发布／失效 handler、`OperationTask`、`useScheduleInvalidation.ts`、`scheduling.vue`。这支持解释历史路径的代码等价；未核全部依赖与环境等价，故不宣称当前真实栈已验收。

## 三类失效对象、原因与恢复

方案的“失效”是独立 `SchedulePlanInvalidation` 留痕，**不会把方案生命周期本身改成另一状态**；查询据此展示 `isInvalidated`。三类输入均可匹配同组织、环境内 `Generated` 与 `Released` 方案，取代／撤销方案不在基础匹配集合中。受影响 assignment 被放进失效事件；MES 再更新对应任务。[失效命令][record]、[方案查询][query]是本表的共同依据。

| 业务动作 | 当前事件／原因 | 失效对象与作用域（静态） | 当前恢复责任 |
| --- | --- | --- | --- |
| 线边收料导致库存可用量变化 | `StockAvailabilityChangedIntegrationEvent` → `materialReadinessChanged` | 按事件 SKU 查排程问题快照 `MaterialReadiness.Shortages.MaterialId`，依 order／sku／operation／resource 范围找工单或工序，再选其 assignment；不是每次库存变化都无条件使全厂失效。历史报告观察到物料变化失效，但未保留逐 MIR 关联 | 物料员先确认原申请收料和 Ready，再由计划员重预览／发布，现场刷新；Ready 本身不清排程失效 |
| 工单下达 | `WorkOrderReleasedIntegrationEvent` → `workOrderReleased` | 按问题快照中 `OrderId == WorkOrderId` **或** `SkuCode == 事件 SKU` 选订单；可能涉及同 SKU 的其它工单，不只新下达单。仅匹配到快照但没有相应 assignment 时，不能据此推定存在被打失效的 MES 任务 | 计划员确认原工单已下达后重预览／发布；排程发布不替代工单下达 |
| OP70 检验合格／让步放行 | `InspectionResultIntegrationEvent` 的 `InspectionPassed`／`InspectionConditionalReleased` → `qualityReleased`；拒绝为 `qualityBlocked` | 只收 MES 相关 payload 来源（`mes`、`mes-operation` 及历史 `business-mes`）和 `operation`／`first-article`。依次选 `WorkOrderId`、`OperationTaskId`、`SourceDocumentId` 作匹配值；有工单 ID 时匹配该单全部 assignment，不能理解成只影响受检 OP70 | 质量员交出原工单、检验记录与结论；计划员接回核对／发布；操作员刷新 OP80 后才能继续 |

事件映射与筛选见[输入事件 handler][input]；物料快照和同 SKU 匹配见[快照匹配][match]。库存 producer 在账本变更时产生可用量事件，相关字段来自[库存转换器][stock]；不能仅凭“收料”按钮点击认定事件已完成。

MES 失效消费者按事件的 `AffectedOperations.OperationId` 查任务，不因 `qualityReleased` 而跳过。`MarkScheduleInvalidated` 对 `InProgress`、`Paused`、`Completed`、`Cancelled` 不改状态，其余会置 `ScheduleInvalidated` 并保存 reason。因此历史 OP70 完成而 OP80 尚未开工时，同工单失效会留下 OP70 实绩、阻断 OP80。它不是把八道已完成任务全部回退。[MES 消费者][mes-consumer]、[任务状态][task-invalidate]。

新版发布回流通过 `ApplyScheduleAssignment` 把可重排任务从 `ScheduleInvalidated` 变为 `Queued`，清除原失效 reason 并记下方案／发布版本；它仍保留其它实际开工检查。已开工、暂停和终态任务不被新版覆盖。恢复成功应以**同一个 OP80 的任务状态及开工许可读回**为准，不能仅看发布 HTTP 成功或 CAP processed。[任务回流][task-apply]、[回流受理][mes-release]。

## 为什么质量发布例外没有解决现场接续

[#3191 的既有交付](https://github.com/Mang-X/Nerv-IIP/issues/3191#issuecomment-5560661855)已明确：`qualityReleased` 留痕但不单独阻断后端发布。当前[发布命令][release]的两个分支应分开理解：

1. 对尚未发布的候选，失效记录中**只有** `qualityReleased` 时，后端不按这条记录拒绝发布；同方案如还存在物料、设备、质量阻断等其它 reason，仍拒绝。错误冲突与未排工序门禁同样保留。
2. 对已经 `Released` 的原方案，再调用 release 会直接返回原结果，不产生新的发布动作，不能用来让 MES 重接失效任务。

前端历史表和草案的发布按钮仍按 `isInvalidated` 统一禁用，文案引导“重排后再发布”；最新 reason 也不表示该方案只存在这一条 reason。[前端发布判断][ui-release]、[草案判断][ui-draft]。因此后端例外不能被解释成当前 UI 支持直接发布失效方案，更不能推导出自动恢复。

**当前操作结论是需要计划员接回，但“必须如此”的长期产品政策未定。** 质量合格、让步放行、首件门禁解除与“全部排程约束仍成立”不是同一事实。本报告不通过改文案或扩大操作员权限取消这一责任。

## 更新后的岗位交接步骤（沿用现有规则）

以下是本调查基线的操作顺序，供同类旅程复验；当前产品语义仍从[排产 Product](../../product/scheduling/design.md)与[MES Product](../../product/mes/design.md)进入。未修改 Product 或用户教程，因为本次没有批准新的业务行为。

1. **物料员**：在原工单完成全部领料、线边收料，读回实际数量与齐套 Ready；请求尚在过账时查原 MIR，不重复收料。计划员无物料读权限时由物料员确认，不临时扩权。
2. **计划员**：确认工单已下达、同一产品与数量未变，再正式生成／发布排程。将收料、下达前置可避开这两类已知后续输入扰动，但不保证没有其它失效事件。
3. **操作员**：刷新原工单／工序，核对已排程与开工许可；正常派工、开工、报工。已开工与已完工事实不为了刷新而重建。
4. **质量员**：在原 OP70 产生真实来源检验记录，交接工单号、工序号、检验记录、结论与时间。只说明质量结论，不承诺排程已恢复；让步不能当首件合格。
5. **操作员 → 计划员**：OP80 若显示待重排，交出原工单／工序、当前失效原因和当前方案（可读时）；停止重复开工、派工或重复报检。不要另建工单。
6. **计划员**：打开当前失效方案，核对工单、已执行事实、剩余任务和所有可见风险；保留应保持的锁定，执行“锁定重预览”，查看对比后“发布新版”并确认。发现设备／物料／质量仍阻断时先交对应岗位处理，不凭“最新原因是质量放行”忽略其它事实。
7. **操作员**：等待发布回流后刷新同一个 OP80，确认排程恢复且开工原因已解除，再开工／报工；若仍失效，保留新旧方案、任务状态、请求关联与服务日志交回排查。仅刷新不会重算方案；这里没有要求新增重试、自动发布或恢复中心。

历史指南第 9 步已走通过上述人工路径；本次未重跑其耗时或岗位切换次数，不能量化改善幅度。

## 待裁决选项与风险

以下是互斥的政策方向，不是获批实施计划。产品负责人需明确**适用结论、影响范围、已有失效如何处理、接续责任**。质量负责人确认是否只含纯 `InspectionPassed`；MES 负责人确认现场开工职责；排产负责人确认保留原资源／时间的适用前提。尚未取得该裁决。

| 选项 | 对本场景的含义 | 风险／批准前需回答 |
| --- | --- | --- |
| A：维持现行人工接回 | 沿上表操作，继续在正式发布前完成收料与下达；质量后仍由计划员检查并发布新版本 | 岗位往返保留，计划员离线时 OP80 等待；需要产品接受该交接成本，不能把操作指南当体验已修复 |
| B：质量事件缩到受检工序 | 产品若确认只改变受检工序的排程约束，可优先使用明确工序身份，继续保留失效、预览与显式发布 | OP70 对 OP80 的质量前后依赖是否允许保留？缺工序身份时怎么办？不能仅因字段存在便改为工序级；只缩范围也不恢复已失效任务 |
| C：纯质量合格不新增执行失效 | 对仍有效的已发布任务，纯 Passed 后保留原 assignment；其它质量结论和其它失效维持现状，不清旧失效、不重发旧方案 | 需确认这类合格只放松约束、允许现场沿原计划继续；历史 `qualityBlocked`／物料／设备失效不能被清掉，让步与首件规则不可顺带放宽；这不是自动发布 |

本票不提出无人值守发布的实现。[ADR 0032](../../adr/0032-scheduling-local-rescheduling-unfreeze.md)仍规定候选经计划员确认、沿既有 release/supersede 链发布；如产品另要求自动发布，须复评 ADR，超出本 Spike 及下列 ≤M 候选。

## 复用、后续切片与重新定级

本次读取 [#4282](https://github.com/Mang-X/Nerv-IIP/issues/4282) 锁定／对比交付、[#4288](https://github.com/Mang-X/Nerv-IIP/issues/4288) 连续验收及 [#1051](https://github.com/Mang-X/Nerv-IIP/issues/1051)、[#1058](https://github.com/Mang-X/Nerv-IIP/issues/1058) 当前正文／评论。后两者是 L3 tracking，不承接本次质量接续政策，也不应因本票再建重排引擎。本次开放票搜索“排程 质量”“排程 失效”未发现更精确的开放政策 owner；这不是全仓穷尽性证明，正式开实施票前仍需复核。

仅保留紧贴 A08 的两个后续候选，**本次不新建实施票**：

| 候选 | 预计范围／先后 | 最小独立验收与停止条件 |
| --- | --- | --- |
| 采用 A 后，把原单交接指引落实到现有失效提示 | S；纯前端提示与现有导航，不改失效策略、权限或合同 | 操作员从同一个失效任务看懂原因与计划员责任，计划员仍经既有修订发布恢复；如要求通知系统或新工作流则停止扩张 |
| 产品选 B 或 C 后，单独调整 Scheduling 质量事件的失效判定 | M 候选、高难度；两者择一；仅现有结构化身份和事件内已足够判断的输入，不改 DB／API／MES 状态机，不处理旧失效补救 | 固定同工单 OP70 完成／OP80 待开工样本，纯 Passed 按裁决表现；拒绝／让步、已有其它失效及其它工单均保持边界；检查已执行项不被覆盖，再做同来源真实 UI → CAP → MES 开工读回。若需跨域改状态、增加契约／持久化前提或清理旧失效，重新定级 L 并拆票，不能继续称 M |

两个候选均需产品批准具体范围后才能开票／实施；它们不是本报告已交付的能力。未选择政策前，本票仍是 `scope:spike`，不能把范围重新定级理解成已经授权修复。后续实现再按 ADR 0021 评估 Product、用户教程与相关接力说明的同步。

## 调查结项与未验证范围

本报告在时间盒内记录了三类对象／原因、已证明的最短恢复路径、岗位责任、未决选项与 ≤M 候选。是否接受本调查结项，由 #4462 的独立审核核对报告证据后决定；报告 PR 合并本身不证明生产流程已改善，也不代表母 #4454 整体验收。

明确保留：质量策略未裁决；逐次 planId／失效行／sourceEventId／correlation 与关联日志缺失；当前版本同旅程真实 UI 重验未运行；纯质量例外、并发质量／设备／物料事件、乱序与旧失效接续没有本次运行证据。未来若需证明这些场景，由获批窄修复或专门验收收集，不用旧 CI、静态代码或最终库存拼出新链。

本次未启动真实栈、浏览器、容器或业务辅助进程，未写业务数据；没有自有运行资源需要停止。未运行业务单元测试、正式 PostgreSQL／Redis-CAP／FullChain lane、硬件与生产环境验证。文档门禁、PR 精确 head 与 CI 另记在 PR，避免将文档检查冒充业务通过。

[history-report]: https://github.com/Mang-X/Nerv-IIP/blob/a154cf38a774c5addc9090f17d86300c930cdd16/docs/reports/audits/2026-10-16-mes-aps-fresh-walkthrough.md
[history-guide]: https://github.com/Mang-X/Nerv-IIP/blob/a154cf38a774c5addc9090f17d86300c930cdd16/docs/reports/audits/2026-10-16-mes-aps-operations.md
[history-evidence]: https://github.com/Mang-X/Nerv-IIP/blob/a154cf38a774c5addc9090f17d86300c930cdd16/docs/reports/audits/assets/2026-10-16-mes-aps/evidence.json
[input]: https://github.com/Mang-X/Nerv-IIP/blob/193a39fa4c8bac387bd0dfc77caa629dcbb71dea/backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/IntegrationEventHandlers/SchedulingInputChangeIntegrationEventHandlers.cs#L378
[record]: https://github.com/Mang-X/Nerv-IIP/blob/193a39fa4c8bac387bd0dfc77caa629dcbb71dea/backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Commands/RecordSchedulePlanInvalidationsCommand.cs#L96
[match]: https://github.com/Mang-X/Nerv-IIP/blob/193a39fa4c8bac387bd0dfc77caa629dcbb71dea/backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Commands/RecordSchedulePlanInvalidationsCommand.cs#L366
[query]: https://github.com/Mang-X/Nerv-IIP/blob/193a39fa4c8bac387bd0dfc77caa629dcbb71dea/backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Queries/SchedulingQueries.cs#L61
[stock]: https://github.com/Mang-X/Nerv-IIP/blob/193a39fa4c8bac387bd0dfc77caa629dcbb71dea/backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/IntegrationEventConverters/InventoryIntegrationEventConverters.cs#L116
[mes-consumer]: https://github.com/Mang-X/Nerv-IIP/blob/193a39fa4c8bac387bd0dfc77caa629dcbb71dea/backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEventHandlers/SchedulingPlanReleasedIntegrationEventHandler.cs#L393
[task-invalidate]: https://github.com/Mang-X/Nerv-IIP/blob/193a39fa4c8bac387bd0dfc77caa629dcbb71dea/backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/OperationTaskAggregate/OperationTask.cs#L233
[task-apply]: https://github.com/Mang-X/Nerv-IIP/blob/193a39fa4c8bac387bd0dfc77caa629dcbb71dea/backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/OperationTaskAggregate/OperationTask.cs#L522
[mes-release]: https://github.com/Mang-X/Nerv-IIP/blob/193a39fa4c8bac387bd0dfc77caa629dcbb71dea/backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEventHandlers/SchedulingPlanReleasedIntegrationEventHandler.cs#L195
[release]: https://github.com/Mang-X/Nerv-IIP/blob/193a39fa4c8bac387bd0dfc77caa629dcbb71dea/backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Commands/ReleaseSchedulePlanCommand.cs#L66
[ui-release]: https://github.com/Mang-X/Nerv-IIP/blob/193a39fa4c8bac387bd0dfc77caa629dcbb71dea/frontend/apps/business-console/src/pages/scheduling.vue#L877
[ui-draft]: https://github.com/Mang-X/Nerv-IIP/blob/193a39fa4c8bac387bd0dfc77caa629dcbb71dea/frontend/apps/business-console/src/pages/scheduling.vue#L955
