# 库存已入库后的成本资本化差额调查

调查日期：2026-10-19；范围：[#4467](https://github.com/Mang-X/Nerv-IIP/issues/4467)，母跟踪 [#4454](https://github.com/Mang-X/Nerv-IIP/issues/4454) 的 A13。Scope：spike，难度高；最多 1 工作日，本次当日交回。只交付调查报告和未决证据，不修改生产代码、费率、历史数据或精度规则。

**结论：原单的 0.138795 CNY 差额与入库单位成本算术一致，但尚不能裁定为允许差异或漏资本化。现有实现明确包含最终收货差异及迟到成本调整记账，不能以差额非零直接判错，也不能以 Posted 或 CAP processed 判定对账完成。原会话已停止，缺少原单来源项、事件版本、实际处理顺序和凭证明细；调查在此停止，生产验收保持未完成。**

## 基线与证据等级

- 原真实页面运行：`a9b625b297e7797c058e40b428c24bf611750daf`，会话 `nerv-a016-1016ab`。原报告描述 PostgreSQL 18、Redis 8、CAP 和非管理员岗位，本次没有重新执行该旅程。
- 冻结报告、操作指南、规范化摘要及截图取自固定提交 `a154cf38a774c5addc9090f17d86300c930cdd16`：[报告][history-report]、[指南][history-guide]、[摘要][history-evidence]、[成本截图][history-image]。这些文件在本次 main 已不在原路径，通过固定 Git 树读取，不重建旧入口。
- 本次开工 fetch 后的源码基线：`ee3b1388d331ebd25a62a6ed30db1297a0306033`。`git diff --exit-code a9b625b297e7797c058e40b428c24bf611750daf ee3b1388d331ebd25a62a6ed30db1297a0306033 -- backend/services/Business/Erp/src` 自然 exit 0、输出为空：该 ERP 源码子树没有变化。它不证明旧部署配置、事件或数据库内容与当前环境相同，也不把旧 runtime 升级成当前 main 实跑。
- 本次只做冻结资料复核、源码追踪、Decimal 复算与原会话可用性检查；没有新的真实服务业务处理证据。规范化结果及原件哈希见 [investigation-summary.json](assets/issue-4467/investigation-summary.json)。该 JSON 是一次性调查记录，没有运行时消费者，不是新增机器 registry 或正式测试结果。

## 同一业务对象与金额

原摘要将主工单、末道报工和登记单关联到同一产出批次：

| 对象 | 冻结值 |
| --- | --- |
| WO / SKU | `WO-20261016-000001` / `FG-QJ-P1-L`，完工 2，8/8 工序完成 |
| 末道 OP / 报工 | `WO-20261016-000001-OP-80` / `PRPT-20261016-000008` |
| lot / FGR | `LOT-PRPT-20261016-000008` / `FGR-20261016-000001` |
| 登记与库存摘要 | Posted，postedQuantity 2，remainingQuantity 0，unitCost 3.728364 |
| 实际人工 | 0.105493 小时，6.329603 CNY |
| 累计 / 资本化 / 差额 | 7.595523 / 7.456728 / 0.138795 CNY |

本次打开原成本截图核对工单与上述三项金额，并用 Python Decimal 复算：

```text
7.595523 - 7.456728 = 0.138795
3.728364 × 2 = 7.456728
7.595523 - 6.329603 = 1.265920
```

最后一项只是累计减人工的余数。原摘要没有 materialCost、machineOverheadCost 或逐工序结算，不能把 1.265920 全称为机器费用，更不能把 0.138795 指认为 OP80 机器费用。原报告记载至少三次差额稳定，但未提供三次独立请求/响应及各自时间，本次只能引用其冻结陈述，不能新增“再次三次实读”的证明。

若唯一差异只是把最终单位成本四舍五入到六位小数，qty 2 的金额误差至多 0.000001 CNY，无法单独解释 0.138795；这只是条件性算术排除，不能反推实际是迟到机器结算。本票不展开 `numeric_scale_out_of_range` 标准精度扩容：标准人工不可用与已展示的资本化差额是不同字段，原空值仍保留。

## 当前实现可证明的时序与记账口径

以下是固定源码的静态事实，均不等于原单实际走过相应分支：

1. [WorkOrderCost.TryPublishCapitalization][aggregate] 首次发布检查完工时间、报工数量、物料移动数量；没有逐工序机器结算齐备条件。`CapitalizationPublished` 阻止第二次发布。仅满足这些计数，不保证发布时成本集合已包含所有后到结算。
2. [资本化 converter][converter] 从当时 `TotalAccumulatedCost / CompletedQuantity` 形成 `WorkOrderCostCapitalizedIntegrationEvent`，事件业务版本为 V1。实际事件 ID、payload、发布时成本集合和 MES 接收值需要原事件证明，不能从当前常量倒填。
3. [StockMovementPosted 处理器][posting] 在最终收货时，把库存金额借记 `1406-FINISHED-GOODS`；按累计成本清理 `1405-WIP`；累计减资本化的差额进入 `5101-PRODUCTION-VARIANCE`，生成 WOC 凭证。聚合的 `CapitalizedCost` 记录 qty × unitCost，差额记账不会令这个字段自动等于累计成本。
4. [机器结算处理器][machine] 按 SettlementRevision 激活/替代结算，记录机器费用后检查 `IsFullyCapitalized`；已全部资本化时调用同文件的 `PostLateAdjustmentIfCapitalizedAsync`，进入 [CostVariancePosting][late-posting] 的 WOCADJ 路径。正向迟到成本借记 5101、贷记 1405，并增加 WipClearedCost；不重新给原库存定价。[人工结算处理器][labor] 也有这条接续。
5. 因此，“成本在资本化发布后、库存最终回执前进入”可能由最终收货 WOC 差异承接；“全部收货后进入”可能由 WOCADJ 承接。两种分支都可能保留公开资本化差额。需要核实实际凭证及提交结果，不能仅凭 subscriber 成功日志宣告金额有去向。

[当前 ERP 产品语义](../../product/erp/design.md)规定资本化差额为累计减资本化，机器费用为按冻结费率的预定分配；没有规定“所有非零差额均错”或本单允许的差额上限。本报告不替业务负责人批准容差、自动追补库存价或新的关账规则。

## #4366 可复用什么，不能复用什么

完整读取 CLOSED [#4366](https://github.com/Mang-X/Nerv-IIP/issues/4366) 的正文和评论，以及[最终报告](../audits/issue-4366-continuous-production-2026-10-13.md)。其 `9aa493c1` / `nerv-0d43-e0363f` 会话曾出现差额 0.859137，原调查将其描述为门控/机器结算问题，最终会计校准进一步保存了真实 SQL 顺序及 WOC 三行：成品 15.707256、差异 0.859137、WIP 16.566393。该票明确纠正了无 spec 来源的“零差额”验收谓词。之后 `f2d66acb` / `nerv-0d43-b8f062` 的独立 UI 旅程补齐 Inventory 自身的公开量价读回。

可复用的是核账方法：同 WO/OP/lot/FGR/movement 强 ID、公开库存数量和金额、结算来源/版本、真实服务写入顺序、WOC/WOCADJ 分录、WIP 清理之间闭合。**不能复用其金额、OP80 根因、事件 ID、处理顺序或凭证作为 A13 证据。** 该旧会话没有公开 voucher/GL 余额和独立 DB SELECT 的边界也照原样保留，不补造。

另完整读取 [#3469](https://github.com/Mang-X/Nerv-IIP/issues/3469) 和 [#953](https://github.com/Mang-X/Nerv-IIP/issues/953) 正文与评论：前者已关闭，解决一次合法资本化与报工冲销的 MES 行版本竞争，明确排除 ERP 计价规则；后者当前仍开放，目标为双收货超量并发的 PostgreSQL 证据。A13 原旅程无冲销，当前也没有超量事实，均不据此判同根因或重复实施。

## 原会话可用性与未决证据

本次读取原 session manifest：`state=Stopped`，cleanup completedAtUtc 为 `2026-10-16T14:02:33.6648600+00:00`，remaining=[]、errors=[]。对原 BusinessGateway `localhost:53810` 发起只读成本查询，自然 curl exit 7，`http_code=000`、`Couldn't connect to server`；Docker 按 `nerv-a016-1016ab` 过滤容器及卷均为空。原 artifactPath 仅留下 guardian 日志，未发现可核原单成本的服务日志；原走查报告也明确本地请求日志和运行连接不进入冻结资产。端口失败不表示成本 API 自身有缺陷。

不能恢复本单历史证据的项目：

- 八工序人工、机器有效结算的结算 ID、active revision、来源事件 ID、计价时点、冻结费率及 amount；material movement/cost 的实际集合。`occurredAt/completedAt` 不是消费提交时间。
- 原完工、实际工时结算、资本化回传、Inventory posting 事件的业务版本/ID、CAP 消费时间和持久化提交顺序，以及首次发布时实际包含哪些成本项。
- 原 FGR 对应 Inventory movement ID 及 Inventory 自身公开 unitCost/movementAmount；同单 WOC/WOCADJ 的 sourceNo、分录、提交结果与 WipClearedCost。摘要中的 postedQuantity 和 MES unitCost 不能替代这些读面。
- 原差额是否已全部进入差异科目，是否还存在未处理输入、费用错配或 WIP 残留。缺少上述资料，不能批准为“允许差额”，也不能裁定漏资本化。

本票允许输出未决证据。继续重放 FGR、修改费率或重建一个恰好产生 0.138795 的新会话都无法恢复原顺序，且会扩大调查，故本轮在此交回；没有盲重放或启动新栈。

## 业务裁决、重新定级与唯一直接后续

**业务裁决状态：待核账，未裁定允许或错误。** 已确认当前有差异记账机制；A13 是否被它正确承接仍未决。生产差额修复维持 `?`，不因静态门控线索直接降成 scope:M 修复票，也不新建自动补价/等待机制。

2026-10-19 本次完整读取 #4467 正文、标签、评论及原生关系：Parent=#4454，sub_issues=[]、blocked_by=[]，无获批 plan。另读取全部 579 张开放 Issue 正文，用“资本化 / capitalization / 机器结算 / 成本差额”检索命中本票、#953、[#2704](https://github.com/Mang-X/Nerv-IIP/issues/2704)；#2704 只补 MES advisory lock 内保存的鉴别力，明确不改成本。读取全部 9 张开放 PR，未发现匹配 A13 的交付；这只是本次查询时点的复用核查，建票前需刷新。

唯一紧接主线的候选是**同单成本对账补证**，建议仍为限时 spike、高难度、待批准，未建新票。优先取得原会话的受控备份/原日志；若不可恢复，业务负责人先接受“原单归因不可恢复”的边界，再在当前隔离栈保存一条新 WO 的完整上述链路。新数据只证明新案例，不回填 A13。核账只使用已有 ERP 成本/机器结算、Inventory movements、凭证查询和只读诊断，不预先扩 API、Schema 或测试基建。

候选交回条件是同一数据组能解释累计、资本化、差异科目和 WIP 的金额与顺序；若出现明确失败输入，再定最窄修复 owner 和验收，不先开猜测性生产修复票：

- 若公开金额与实际凭证闭合，由业务负责人确认该差异政策/演示口径；没有代码缺陷证据时不修门控。
- 若具体来源项未记账、重复、错版本或库存价不符合批准政策，只围绕该已证实路径重判 scope，优先复用精确 owner；跨域规则或库存重估需求另走批准，不搭载本报告。

本轮没有证据要求增设“差额必须清零”、等待所有机器结算、自动重估或精度扩容。这些均不是本票获批实现。

## 调查结项与证明边界

调查已交付原对象和金额核对、当前机制与旧票会计校准、原证据缺口、待核账裁决和重新定级；独立审核可据此判断调查报告是否足以结项。票面“同原单来源项/版本/时序完整核实”和“明确允许还是漏资本化”仍未完成，保持未勾选；spike 关闭只代表调查交付，不等于 #4454 连续旅程或生产修复通过。

本次没有新增/修改测试，不执行 backend/frontend、PostgreSQL、Redis/CAP、FullChain 或真实岗位 UI 验收；没有新 runtime、CAP 重放、凭证/GL 实读或生产修复。受影响文档门禁及精确 PR head 的实际命令、输出留在 PR 证据中，与本报告的源码/历史运行基线分开。

工作材料位于本实施 worktree 的 `artifacts/issue-4467/`，交付时归档到仓库外并记录哈希。没有启动 fullstack、Aspire、容器、浏览器或常驻辅助进程；只读检查不取得原 session 所有权，不停止其它资源。保留当前工作树和分支供后续修复。

[history-report]: https://github.com/Mang-X/Nerv-IIP/blob/a154cf38a774c5addc9090f17d86300c930cdd16/docs/reports/audits/2026-10-16-mes-aps-fresh-walkthrough.md
[history-guide]: https://github.com/Mang-X/Nerv-IIP/blob/a154cf38a774c5addc9090f17d86300c930cdd16/docs/reports/audits/2026-10-16-mes-aps-operations.md
[history-evidence]: https://github.com/Mang-X/Nerv-IIP/blob/a154cf38a774c5addc9090f17d86300c930cdd16/docs/reports/audits/assets/2026-10-16-mes-aps/evidence.json
[history-image]: https://github.com/Mang-X/Nerv-IIP/blob/a154cf38a774c5addc9090f17d86300c930cdd16/docs/reports/audits/assets/2026-10-16-mes-aps/screenshots/11-cost-precision.png
[aggregate]: https://github.com/Mang-X/Nerv-IIP/blob/ee3b1388d331ebd25a62a6ed30db1297a0306033/backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/WorkOrderCostAggregate/WorkOrderCost.cs#L378
[converter]: https://github.com/Mang-X/Nerv-IIP/blob/ee3b1388d331ebd25a62a6ed30db1297a0306033/backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventConverters/WorkOrderCostIntegrationEventConverter.cs#L10
[posting]: https://github.com/Mang-X/Nerv-IIP/blob/ee3b1388d331ebd25a62a6ed30db1297a0306033/backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventHandlers/WorkOrderCostIntegrationEventHandlers.cs#L289
[late-posting]: https://github.com/Mang-X/Nerv-IIP/blob/ee3b1388d331ebd25a62a6ed30db1297a0306033/backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventHandlers/WorkOrderCostIntegrationEventHandlers.cs#L384
[machine]: https://github.com/Mang-X/Nerv-IIP/blob/ee3b1388d331ebd25a62a6ed30db1297a0306033/backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventHandlers/OperationMachineOverheadSettlementIntegrationEventHandlers.cs#L180
[labor]: https://github.com/Mang-X/Nerv-IIP/blob/ee3b1388d331ebd25a62a6ed30db1297a0306033/backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventHandlers/OperationLaborSettlementIntegrationEventHandlers.cs#L342
