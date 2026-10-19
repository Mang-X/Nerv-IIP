# 急单工作中心覆盖在 APS 发布后改变：调查报告

调查日期：2026-10-19；范围：[#4465](https://github.com/Mang-X/Nerv-IIP/issues/4465)，母跟踪 [#4454](https://github.com/Mang-X/Nerv-IIP/issues/4454) 的 A11。Scope：spike，高难度；时间盒最多 1 工作日，当日停止调查。仅交付证据与后续候选，不修改生产代码或母 spec。

**已在当前独立真实栈复现：创建时 OP10 为 `WC-ROD-01`，APS 持久化输入和 assignment 已变为 `WC-TUB-01`；发布后 MES 写入 TUB，撤销后仍保留 TUB。** 覆盖首先在 Scheduling 来源映射／问题组装处丢失，不是撤销时才丢失，也没有证据表明求解器在两个合格中心之间自主改选。最终保留覆盖还是明确拒绝，仍需产品批准；本报告不把建议变成长期规则。

## 基线与可审产物

- 独立 worktree：`/Users/mang/.t3/worktrees/Nerv-IIP/t3-3878fe76`；分支 `t3/investigate-issue-4465-spike`。开工工作区干净；fetch 后 HEAD、`origin/main` 均为 `ee3b1388d331ebd25a62a6ed30db1297a0306033`，随后 `git ls-remote origin refs/heads/main` 同值。
- 本次运行源码 SHA 为上述 `ee3b1388…`，不是后来报告提交的 SHA。会话 `nerv-4465-261019`，官方 `fullstack start` 创建的 ephemeral Aspire 隔离栈、PostgreSQL／Redis、真实浏览器。身份为真实管理员 `admin`，组织 `org-001`、环境 `env-dev`；**未进行最小权限岗位验收**。没有 mock HTTP、替换 provider、手工写业务库或补料绕过。
- [规范化证据](assets/2026-10-19-issue-4465/evidence.json)保存强身份、五次 MES 快照、APS 输入和三个方案状态、关键 UI 回执、20 条服务日志字段投影、两个消费事件及采集局限。SQL 仅为本隔离会话的只读诊断，具体查询随各快照保留；公开读面与数据库读面分列。
- 截图：[真实建单表单](assets/2026-10-19-issue-4465/01-create-form.png)、[创建后同 OP10](assets/2026-10-19-issue-4465/02-created-op10.png)、[方案已发布](assets/2026-10-19-issue-4465/03-released-plan.png)、[撤销后同 OP10](assets/2026-10-19-issue-4465/04-revoked-op10.png)。截图辅助定位，结论同时依赖持久化和公开状态。
- 历史症状仍归原运行 `a9b625b297e7797c058e40b428c24bf611750daf`、冻结证据提交 `a154cf38a774c5addc9090f17d86300c930cdd16`（[原报告](https://github.com/Mang-X/Nerv-IIP/blob/a154cf38a774c5addc9090f17d86300c930cdd16/docs/reports/audits/2026-10-16-mes-aps-fresh-walkthrough.md)）。没有将历史非管理员证据算成本次管理员运行。

## 同一 OP10 的五处读回

工单 `WO-20261019-000001`，任务 **`WO-20261019-000001-OP-10`**，物料 `SF-ROD-01`，数量 1。生产版本 **`01a151e0-bc75-73a1-9f4b-c8e377b6f0ce`**，冻结路线 **`ROUTING-SF-ROD-01:1`**。方案 **`plan-01a151e5e0c0785ca461b1ebacd11920`**，问题 **`workbench-01a151e5d71f7da1acf680d7fa96df45`**，指纹 `662d623bbe62a54e0e1842942dc46e35e391d43d5441575f57c92b780332b2eb`。

| 位置／实际时刻（UTC） | 同 OP10 工作中心与时长 | 版本、状态与证据 |
| --- | --- | --- |
| 创建，约 02:01:24 | MES `WC-ROD-01`，60 分钟 | `Queued`，row_version 0，schedule plan/revision 均 NULL；公开详情及数据库一致；路线身份为上列版本 |
| APS 来源／持久化输入，02:03:02 | `eligibleResourceIds=[WC-TUB-01]`，primary=TUB，7 分钟 | sourceReference=`product-engineering:routing:ROUTING-SF-ROD-01:1:OP-WB-CUT`；同任务 ID/序号10；工程路线公开读回 OP10=TUB、standardMinutes=22 |
| assignment，02:03:02 | `resourceId=workCenterId=WC-TUB-01`；08:00–08:07 | plan=`generated`，`aps-lite-v1`，contractVersion 1；3 条 assignment，未排0；MES 另读仍 ROD/60、row_version 0 |
| 发布，02:04:09，消费记账 02:04:09.896920 | MES `WC-TUB-01`，7 分钟，deviceAssetId=TUB | release revision **1**；同路线身份；`Queued`，row_version 1，schedule plan/revision 已写入，公开详情同值 |
| 撤销，02:04:39，消费记账 02:04:40.004482 后读回 | MES 仍 `WC-TUB-01`，7 分钟 | plan=`revoked`，revision 1；任务 `ScheduleInvalidated`、reason=`explicit`、row_version 2；schedule plan/revision、scheduledAt、设备清空，路线身份不变；公开详情同值 |

撤销后的第一次诊断读回与异步消费竞争，仍读到 revision 1／`Queued`；没有当成终态。证据同时保留 `revoked` 早读和 `revoked-settled` 完成读回，以状态变化和消费记录确定终态，不用固定 sleep 代替终态。

OP20、OP30 的中心分别保持 ROD、GRD，三工序完整。工单仍是 `created`、有 `RM-BAR-01` 缺料 1.4 kg；缺料和设备采集未接入在方案内表现为 warning。此次没有领料、下达、开工、报工或完工，不将“能发布排程”解释为“可生产”。

60→7 分钟是同一来源链的伴随变化，不是本票另认定的时长算法缺陷：MES 保存表单绝对时长，APS 按路线 run/teardown、数量和效率重新算时长，22 的路线 standardMinutes 也不能直接当成 APS 的运行时长。本票只将该差异作为覆盖来源未贯通的佐证，不修时长算法。

## 实际操作与请求关联

1. 执行 `pwsh -NoProfile -File nerv.ps1 fullstack start -SessionId nerv-4465-261019`，通过 Aspire readiness；另外明确等待 `business-mes`、`business-scheduling`、`business-product-engineering`，再读取 `aspire describe --format Json --apphost infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj`。使用动态发现的 endpoint，不复用其它 worktree 的服务。
2. 在真实 `/mes/work-orders` 表单选择 SF-ROD-01 当前有效版本、数量1、交期本地 `2026-10-20T09:59`、中心 WC-ROD-01、序号10、60分钟；点击“创建急单”。公开详情与 MES 数据库保存三工序，OP10=ROD。
3. 同工单详情点击“对该单排产”→“生成只含该单的方案”，窗口本地10月19日10:00起7天。该入口的实际 POST 为 `/api/business-console/v1/scheduling/workbench/plans`，与排产工作台共用来源 producer；不是另一个求解入口。读取持久化 problem 和公开 plan，未发布时再读 MES 确认仍为 ROD。
4. `/scheduling`→“表格”→对同方案点击“发布”，HTTP200，releaseRevision=1；分别读公开 MES／方案和数据库，保存消费记账。随后点击“撤销发布”→真实确认框“确认撤销”，HTTP200；消费完成后刷新同工单详情并重复读回。

关键请求不是单靠 HTTP200 证明终态。下列 `traceId` 在 BusinessGateway 和所属服务的 request-finished 日志一致，生成 trace 还包含 MES 来源及 ProductEngineering 路线查询；完整路径、时刻、status、spanId 的安全字段投影见 evidence.json。

| 行为 | 跨服务 traceId | 结果 |
| --- | --- | --- |
| 急单创建 | `e5eba4292c9f37e039ce50910a8c1812` | Gateway 与 MES 均200 |
| 方案生成 | `191f220c256c7ddc6535e04a2f9d2a08` | Gateway／Scheduling／MES 来源／工程路线读取均200 |
| 发布 | `3c830939496f0f45b0027cef0e3abc23` | Gateway 与 Scheduling 均200；MES 消费事件 `evt-01a151e6e5d57e42b2bcff4ac0c9188d` |
| 撤销 | `099eadbdbbab3853191a6527b3f98ee3` | Gateway 与 Scheduling 均200；MES 消费事件 `evt-01a151e75b8c7369890edd1e2a115db3` |

日志经官方 `nerv.ps1 fullstack logs <resource> -SessionId nerv-4465-261019 -Tail 2500`／`aspire logs` 采集；另外尝试了 `aspire otel logs --format Json`。按 trace-id 查询返回空数组，因此采用 console JSON 的关联字段，没有声称 Dashboard trace 查询成功。Gateway 结构化日志没有 CorrelationId 属性；保留匹配 trace，UI 发布／撤销回执中的 `X-Correlation-Id` 单独保存，不混为同一种标识。

原始创建 POST body 未捕获：输入由提交前真实表单截图证明，创建 HTTP200 由双服务日志证明，落地结果由同工单公开／数据库读回证明。原始 CAP envelope 也未留存；本次 CAP message 表查询为空，不能据此宣称 inbox payload 已逐字段核对。已有 subscriber 完成日志、MES processed-integration-event ID／幂等键和实际 row_version 变化，足以证明本次对应消费与终态，但不替代 transport lane 自动验收。

采集失败均未隐藏：初次 API GET 错带 JSON Content-Type 返回400，去掉无 body GET 的 Content-Type 后200；首次私有 psql 未提供密码，在查询前失败，改用容器自身环境后只读成功。Vite 首次加载出现 `504 (Outdated Optimize Dep)` 后自行重载恢复，后续页面操作实际完成。没有因此修改产品或配置。

## 丢失位置：运行事实与静态机制

以下代码指针均固定于本次运行源码 SHA，不把报告提交当成产品改动。

1. **MES 创建正确保存覆盖。** [CreateRushWorkOrderCommand](https://github.com/Mang-X/Nerv-IIP/blob/ee3b1388d331ebd25a62a6ed30db1297a0306033/backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/WorkOrders/CreateRushWorkOrderCommand.cs#L160) 对所选序号使用 request.WorkCenterId／Duration，其余工序沿路由，并保留路线身份。本次 ROD/60 落库直接验证该分支。
2. **Scheduling 来源不能携带这个覆盖。** [SchedulingWorkbenchSourceProvider](https://github.com/Mang-X/Nerv-IIP/blob/ee3b1388d331ebd25a62a6ed30db1297a0306033/backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Scheduling/SchedulingWorkbenchSourceProvider.cs#L149) 按工单生产版本重新取得路线；私有 MES task DTO 只读 ID、序号、EarliestStartUtc，向 workbench operation source 仅投影 ID/序号。WorkCenterId 和 Duration 没有进入后续 operation source。这是静态定位；本次持久化输入只允许 TUB 是其真实运行结果。
3. **问题组装从工程路线生成约束。** [SchedulingProblemProducer](https://github.com/Mang-X/Nerv-IIP/blob/ee3b1388d331ebd25a62a6ed30db1297a0306033/backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Scheduling/SchedulingProblemProducer.cs#L195) 以 routing operation.WorkCenterCode 过滤 eligible resource，并从工程 operation 算 duration。本样本 ROD 资源只带 `OP-WB-CNC` 能力，TUB 带 `OP-WB-CUT`；直接把 CUT 的中心字符串换成 ROD，并不能证明生产能力相容。**此处能力集合又由路线映射构建，不能倒推真实车间绝不具备该能力。**
4. **发布正常应用 assignment，造成 MES 覆盖被替换。** [发布消费者](https://github.com/Mang-X/Nerv-IIP/blob/ee3b1388d331ebd25a62a6ed30db1297a0306033/backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEventHandlers/SchedulingPlanReleasedIntegrationEventHandler.cs#L222) 调 `ApplyScheduleAssignment`。[领域方法](https://github.com/Mang-X/Nerv-IIP/blob/ee3b1388d331ebd25a62a6ed30db1297a0306033/backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/OperationTaskAggregate/OperationTask.cs#L582) 在无手工派工时写中心、时间和设备。本次没有手工派工，row_version 0→1，中心与时长变为 assignment 值。
5. **撤销是撤回排程来源，不是恢复创建快照。** 同文件 [RevokeScheduleAssignment](https://github.com/Mang-X/Nerv-IIP/blob/ee3b1388d331ebd25a62a6ed30db1297a0306033/backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/OperationTaskAggregate/OperationTask.cs#L605) 清 plan/revision/设备并标失效，不恢复中心／时长／EarliestStartUtc；本次 row_version 1→2 后 TUB/7 仍在。没有证据将其单独判为撤销实现违约；首先需裁决覆盖与撤销的产品语义。

## #4274 原策略核查与本次裁定

完整读取 [#4274](https://github.com/Mang-X/Nerv-IIP/issues/4274) 正文和两条评论，并核对[原 T02](https://github.com/Mang-X/Nerv-IIP/issues/4272#issuecomment-6096122199)、#4272 正式 r1／最终结项评论及 [PR #4294](https://github.com/Mang-X/Nerv-IIP/pull/4294) 正文／审核／回应。本次入口核对 #4465 无评论、sub_issues=[]、blocked_by=[]，Parent=#4454，无独立 plan；按 #4465 与当前分支查全部状态 PR 均无既有交付。按“工作中心／覆盖”检索开放 owner 未发现另一张完整匹配票；不重开 #4274/#4272。

- **原批准意图**：#4274 第二条是“验证有意的工作中心覆盖与正常冻结路由相容”。#4272 r1 只规定沿有效生产版本完整工序链，没有批准“后续静默恢复默认中心”。当前 [产品说明](../../product/mes/design.md)及[操作指南](../../../frontend/apps/docs/docs/getting-started/planning-to-finished-goods.md)仍明确工作中心／时长覆盖选中工序。
- **已实现且原审核接受的范围**：#4294 新增的 [RushWorkOrderRoutingTests](https://github.com/Mang-X/Nerv-IIP/blob/ee3b1388d331ebd25a62a6ed30db1297a0306033/backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/RushWorkOrderRoutingTests.cs#L15) 验证创建时选中20使用 `WC-OVERRIDE`、60分钟，其余工序保留路线。原真实运行做到三条 assignment，但 PR 明写“APS 仍按冻结生产版本路由排程”“未发布”。它不能证明发布／撤销的覆盖相容性；本次没有篡改其历史结项。
- **既有长期边界**：[ADR 0014](../../adr/0014-aps-and-iiot-scheduling-boundary.md)要求 Scheduling 拥有问题／方案、MES 应用发布结果、求解器消费标准化事实；[ADR 0022](../../adr/0022-scheduling-rescheduling-evolution-freeze.md)要求规则源与引擎分离。两者均没有授权以工程默认值静默替换已接受输入，也没有规定“任意覆盖中心必须无条件可排”。
- **本次技术裁定**：不能把现象称为“工序齐全所以业务一致”，也不应先修有限产能算法或只在 MES 发布消费者强留旧中心。后者会让执行中心和已发布方案资源不同。需要在进入求解的来源层确定兼容或拒绝，不能靠消费者掩盖来源差异。
- **保留的产品未决项**：是否支持跨中心能力覆盖、中心覆盖是否独立于时长覆盖、覆盖在重预览／撤销后的存续期。报告没有资格据现有实现替产品作最终选择；这三点未被 #4274 原正文及结项证据裁定。

## 重新定级与唯一可先行的最小切片

调查仍为 `scope:spike`。**若要求完整保留有意覆盖并贯穿发布／撤销，应按 scope:L 重新批准拆分**：覆盖来源／持久化与公开事实、Scheduling 组装和资源能力语义、MES 撤销语义都要对齐。当前同一任务字段会被发布改写，不能不区分原覆盖和排程结果就直接当永久来源；本报告不发明新 override registry、开关或迁移方案。

**推荐先批准一个 scope:M、难度中候选：APS workbench 明确拒绝与版本路线中心不相容的任务，停止静默换值。** 只覆盖当前可达的跨中心差异，归 Scheduling 来源组装；使用已有 MES 任务公开字段及路线读取，复用现有业务拒绝反馈，在创建方案／问题前拒绝，不新增求解器、DTO 生成链、数据库迁移或跨域消费者分支。该候选是受控规则建议，**未批准、未建票、未实施**，不是宣称已经满足原“保留覆盖”意图。

独立验收应固定一个组合 SHA，以真实页面和公开读回证明：

1. SF-ROD-01 的同 OP10：MES 为 ROD、路线为 TUB时，生成方案明确说明中心不相容，保留当前选择／草稿；没有新增 problem／plan／assignment，MES 工单和任务不变，不能继续发布被静默改成 TUB 的方案。
2. 中心与冻结路线一致的正常三工序链仍能生成、发布、撤销；任务强 ID／标准工序／路线版本相容，按既有正常规则比对，不把正常 APS 时长归一化当成故障。通过后才可说该受控拒绝切片没有破坏正常路线。
3. 原拒绝单修正为相容输入后可重新生成并发布，同对象读回闭合；复用 `SchedulingWorkbenchBatchSourceTests`／`SchedulingProblemProducerTests` 的公开入口及现有真实栈，不新增测试框架。

该 M 候选不回滚已被改变的历史任务，不解释任意备选中心是否可用，也不解决同中心的绝对时长覆盖。它们不是给本 PR 加防御代码的理由；产品若选择完整保留，回到上述 L 主线，不同时开互相冲突的两套实施票。#4274 原完整工序链成果保留。

## 停止点、未完成验收与清理

本次交付了同任务五处读回、丢失位置、原策略证据缺口和重新定级／最小候选。独立审核确认调查产物后可收口 spike；**生产修复、最终覆盖策略、正常路线修复后的独立验收、最小权限及 #4454 连续旅程仍未完成**。本次没有跑单元／集成测试、FullChain 正式 lane、移动端、跨午夜或生产环境验收，不把诊断读回算成这些门禁通过。

本地差异探针 `uv run artifacts/issue-4465/detect_difference.py` 实际输出 `created=WC-ROD-01; assignment=WC-TUB-01; revoked=WC-TUB-01`、exit1；它是症状仍存在的负向证据，不是本报告 PR 的门禁失败，也没有生产修复后的 GREEN。Python 全部通过 uv 管理，无包安装。

原始取证材料位于本 worktree 的 `artifacts/issue-4465/`；公开保留仅采用字段白名单生成的 evidence.json 与截图，不提交授权头、口令、token、连接串、raw 服务日志或完整请求／响应正文。确切报告 head、文档门禁命令／输出、CI 和审核归属随 PR 发布，不倒写成本次产品运行基线。

官方停止命令 `pwsh -NoProfile -File nerv.ps1 fullstack stop -SessionId nerv-4465-261019` 实际返回 `state=Stopped remaining=0`；manifest 的 cleanup.remaining/errors 均为空。五个业务／前端端口及一次性本机凭据桥接端口已各做一次连接检查，全部关闭；桥接进程已结束、私密凭据文件已删除、浏览器自有 tab 已关闭。停止了本会话的4个容器、专属网络／卷，未停止其它栈。工具环境：PowerShell 7.6.4、Aspire CLI 13.4.6、uv 0.11.31、uv CPython 3.12.13。工作树和分支保留供独立审核。
