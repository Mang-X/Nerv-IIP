# 非空排程连续旅程核验（2026-10-12）

本报告对应 [#4367](https://github.com/Mang-X/Nerv-IIP/issues/4367)、[#4288 spec r1](https://github.com/Mang-X/Nerv-IIP/issues/4288) 与[已批准切片方案](https://github.com/Mang-X/Nerv-IIP/issues/4288#issuecomment-6123446945)。Scope Gate 复核为 M：复用既有公开入口，单 PR，只保存时点验收证据，不修改生产代码、授权模型、排程政策、时间模型或测试基建。

## 版本与前置

- 真栈 SHA：`14f873264cfa452bcf10747ec21a33ffabc4c4a2`，也是本树创建及启动时最新 `origin/main`。前置 [#4365](https://github.com/Mang-X/Nerv-IIP/issues/4365#issuecomment-6124852159) 已关闭，[PR #4369](https://github.com/Mang-X/Nerv-IIP/pull/4369) 已合并于此 SHA；准备操作依据[准备报告](minimum-role-journey-prerequisites-2026-10-12.md)，全部所需主体在新栈重建，不沿用已停止的旧 runtime。
- 独立会话 `nerv-bcf2-e9ad2f`；`org-001/env-dev`、界面时区 `Asia/Shanghai`；session 专属 PostgreSQL 18、Redis 8、MinIO 与 VictoriaLogs 容器/卷，非生产。IAM、PlatformGateway、BusinessGateway、MES、Scheduling、MasterData、Business Console 的实际 describe 为 `Running/Healthy`；不以 manifest 的 Running 单独代证。
- macOS `27.0.1 (26A434)`、.NET `10.0.302`、PowerShell `7.6.4`、Aspire `13.4.6`、Node `v24.18.0`、本机 pnpm `12.0.0`、Docker Server `29.8.2`。仓库 pnpm 声明以当前 package 为准，本次未改依赖或 lockfile。
- 实际启动：`NERV_IIP_LEADER_DEMO=false NERV_IIP_LEADER_DEMO_WORLD=false NERV_IIP_LEADER_DEMO_HISTORY=false pwsh -NoProfile -Command '& ./nerv.ps1 fullstack start -Scenario issue-1912-real-machine-walkthrough; Read-Host "Keep launcher alive"'`，launcher 保持存活。公开入口：PlatformGateway `http://localhost:54822`、BusinessGateway `http://localhost:54833`、Business Console `http://localhost:54842`；后续运行必须从新会话取 URL。
- 与旅程直接相关的已交付成果包含于运行树：#4279 的调查合并 `bac6b1bfcc9afc95b079db4acb507d73dfba55c0`、#4282 修复 `675d8fd7cca4feaa8090e8a6a0df3b69f99fa444`、#4283 报告 `94ca021da4bd7dff5d0303c180763092c4409d92`、#4286 修复 `d2c78ecd61c7f37b5277b812e3a06a385c85df25` 均通过 `git merge-base --is-ancestor <SHA> HEAD`。调查关闭与旧 UI 证据不作为本轮非空停机影响的证明。

## 身份与场景输入

准备身份只经公开 IAM / 主数据入口创建角色、账号、员工和班组；不执行业务验收，不写数据库或预造库存、生产、质量终态。口令、access/refresh token、连接串不入报告或附件。

- 计划员 `journey4367-planner`：principal `user-01a13040cd417534b58f3ab307ef7057`，role `role-01a13040cc9e775aaac40c3b939d7de0`。Organization scope；只有排程读/管理/发布、工单读/管理、物料/资源/生产版本/路线读及通知读权限；没有 IAM/admin/internal 权限。工单管理用于真实 UI 创建急单，目录读用于选择物料和工作中心。
- 现场 `journey4367-operator`：principal `user-01a13040d19e7bee9959de8fe9c8bfd6`（精确 principal 以附件本人 `/auth/me` 为准），执行角色 `role-01a13040d14a74c0ab86ee5f55516a81` 只有路线八工作中心 grant；另一个只读角色只提供 `business.maintenance.downtime-reasons.read` 的组织级停机原因目录读取。`/me/work-context` 对 `business.mes.operations.read` 公开返回所选 `WC-CT-01` 的单一 exact work-center 授权路径；目录角色没有扩大 MES 执行权限。
- 两账号均公开创建 membership、设置 scope、本人登录及改密，再本人 GET `/api/console/v1/auth/me` 精确比较 principal、roleIds、permissionCodes、org/env；完整机器字段在[脱敏附件](assets/2026-10-12-issue-4367/evidence.json)。员工 `EMP-J4367-PLANNER/OPERATOR` 为 active；现场加入三车间的 `TEAM-J4367-01/02/03`，班组使用既有 `EARLY`，目录关系不代替授权。
- 产品 `FG-QJ-P1-L`，生产版本 `01a1303f-276c-7217-8ed8-1c8aea07eaa2`，MBOM `MBOM-FG-QJ-P1-L:2`，路线 `ROUTING-FG-QJ-P1-L:1`。计划员在 `/mes/work-orders` 的“创建急单”真实选择目录/生产版本/工作中心并提交两次，每次数量 1、交期 `2026-10-13T13:18:00Z`。工单 `WO-20261012-000001/000002`，各自产生 `-OP-10` 至 `-OP-80` 八道工序。第一张急单输入工时 300 分钟，MES 创建读面保存该值；APS 方案使用工程路线标准时间，实际八道合计 59 分钟，不能拿创建输入代替排程结果。
- 初始现场为 Queued、工单 created、真实缺料，`allowedActions=[]`。本切片没有为排程验收入库/补料、下达或开工，不把排程发布等同现场可执行。

## 实际连续路径与权威读回

所有业务写动作来自真实 Business Console DOM，T3 preview 无 response mock、route interception 或 API fixture 桥接。一次性 Python HTTP 调用只做公开身份/主数据准备及权威读回。浏览器取证使用 `Response.clone()`，不消费应用原响应；保存附件是闭合业务字段投影，原始请求头、token 和任意正文不入库。

| 阶段 | 实际 UI 行为与公开结果 | 证明边界 |
| --- | --- | --- |
| 基线 | 工单行“对该单排产”生成 `plan-01a13045b4a2728fb017aa47a97e8925`，8 assignments；表格“发布”返回 `releaseRevision=1` | 带缺料/设备未知预警的排程，不代表下达/开工 |
| 新基线 | 排程总览只选工单 1，指定 `2026-10-13 15:50 → 2026-10-14 18:00` 本地窗口；真实异步 job 生成 `plan-01a1304768ed7942a3d0e82061dd76c3`，8 assignments | 公共日历实际使任务从 `08:00 UTC` 开始；这个窗口没有证明跨班 |
| 插单 | 工单 2 急单及优先级 2000 保存；异步候选 `insertion-01a1304818cd76548dbbc7c31259f9b2`；“选定候选并保存修订”实际保存、表格发布为 revision 2 | 新增工单 2 的八道真实工序，共 16；原工单八道资源/起止逐项不变，移动数 0，新增数 8；物料到齐未知，UI 明示“不可承诺” |
| 停机 | 现场 `/mes/downtime` 选择工单 1 工序 60、`WC-CT-01`、既有 `DT-MECH`，开始 `2026-10-12T13:29:00Z`，提交得到 `DOWNTIME-20261012-000001` | 停机公开终态 `workOrderId/operationTaskId=null`，保存的是 `WC-CT-01` 资源级事实，未保存工序关联。Scheduling GET `/plans/{insertionId}/downtime-impact` 将该事实映射为两工单的 `-OP-60`，`affectedOperations=2`、备选数 0；不是空影响代证 |
| 冻结 | 表格编辑工单 1 工序 10“持久锁定”，再从当前两工单生成 `plan-01a1305154f5723baba99f8b70076ad3`，生成并选定局部候选 `plan-01a13051edbe7221bec0bc19b2ffed8b` | 候选 `freezeContext.assignments` 非空，原因 `manualLock`；`WC-TUB-01 / 2026-10-13T08:00:00Z → 08:07:00Z` 逐项保持，KPI 锁定 1/1；默认稳定窗口 0 分钟、无已开工/完工事实，不宣称这些冻结分支已跑 |
| 发布与撤销 | 冻结候选表格发布为 revision 3；现场“确认恢复”读回停机 Recovered，恢复 `2026-10-12T13:36:12.8Z`、历时 7.21 分钟；计划员“撤销发布/确认撤销” | 公开 plan 为 revoked；两张工单工序 60 均 `ScheduleInvalidated`、`scheduledAtUtc=null`、原因 explicit、`allowedActions=[]`。缺料/工单未下达仍阻止开工，恢复 HTTP 成功不冒充生产恢复 |
| 跨班排程 | 继续选工单 2，通过 UI 指定本地 `2026-10-13 23:50 → 2026-10-14 18:00`，实际异步 job 求解 `plan-01a13054c20a7390b8e1ad4b48586fbe` 并确认发布，revision 4 | 8 道真实工序、未排 0。工序 20 为 `15:57 → 16:08 UTC`，跨公开 EARLY 结束/MIDDLE 开始 `16:00 UTC`；整个工单 `15:50 → 16:49 UTC`。现场工序 20 的 plannedStart 精确为 `15:57 UTC`，scheduledAt 非空；八道均 Queued。计划有 8 道延期、准时率 0，原样报告，不为成功改变交期或策略 |

**跨班排程计算与发布已实际运行；现场跨班生产未运行。** 公共日历同时含 DAY/EARLY/MIDDLE/NIGHT 且存在重叠，本次工序 `segments=null`，不宣称跨班分段或真实员工换班。`shiftId=null`、实际开工/完工为空。跨午夜的现场实绩独立列为**未跑**：运行发生在 2026-10-12 晚间，任务计划跨 2026-10-13/14 本地午夜；没有等待未来计划时刻或执行缺料工单，不以未来甘特图、模拟时钟或 API 回填实际时间代证。获批第 3 切片要求跨班排程连续验收：本轮真实 UI 异步求解/发布、跨公开班次边界及八道 MES 排程投影覆盖该要求；现场可执行状态按现有规则读回为缺料/未下达、`allowedActions=[]`，无需人为使其可开工。现场跨班生产/员工换班属于第 2 切片的生产旅程，本报告只保留未跑事实，不将其追加为 #4367 必需项。

停机无 ETR、无合格备选时，候选移动数 0，已有位置保持；本轮只证明非空影响与人工锁保持，未证明等待恢复时间后的右移或替代设备分配。追加人工锁后直接修订插单候选收到 400（要求完整工单集合且不能追加锁），随后经既有“生成首版 → 局部候选 → 发布”完成冻结核验，没有放宽合同。准备时缺目录/停机原因读取权限的 403 已按现有权限补齐；浏览器重复登录、超时与过期依赖加载不记为业务通过。

## 事件、服务记录与证据

- MES inbox 公开版本对应的只读 SELECT 留下 7 个真实 `SchedulePlanReleased/SchedulePlanRevoked` 事件：4 次 release、2 次 supersede revoke、1 次 explicit revoke。最后 release event `evt-01a1305600817c7298c8b262cc4f84c0` 对应跨班 plan；explicit revoke `evt-01a130542ff1730a8ae6db161451a1b5` 对应冻结 plan revision 3。7 条 CAP received 全部 `Succeeded`、Retries 0。它们只解释异步传播，不代替上节公开业务读回。
- 数据库白名单读回：工单 1 八道 schedule_plan_id/revision 为空、ScheduleInvalidated；工单 2 八道 schedule_plan_id 为跨班 plan、schedule_release_revision=4、Queued。数据库仅 SELECT，业务结论优先来自公开 API。
- 相关服务日志经 `pwsh ./nerv.ps1 fullstack logs business-scheduling -SessionId nerv-bcf2-e9ad2f -Tail 240` 与 `... logs business-mes ... -Tail 240` 实际保存；早期部分请求日志不在有界 tail 中，不能声称全程无缺失。原日志仅本地限制访问，不提交正文。transport/inbox 与最终公开读面共同保留相关 ID。
- 真实截图：[停机登记](assets/2026-10-12-issue-4367/screenshots/downtime-registered.png)、[撤销终态](assets/2026-10-12-issue-4367/screenshots/revoked.png)、[跨班方案甘特](assets/2026-10-12-issue-4367/screenshots/cross-shift-gantt.png)。截图保留当前 UI 的缺料/设备未知/延期及 Gantt evaluation 提示，不调整画面掩盖事实。
- 一次性验证实际命令 `python3 /tmp/nerv4367-verify.py > artifacts/issue-4367/verification.log 2>&1`，自然 exit 0：`RESULT 14 passed; scheduling runtime/public readback only; no elapsed shift production proof`。14 项包含两身份精确比较、permission-aware scope、版本终态、非空插单与基线保持、非空停机、精确冻结、撤销、真实恢复间隔、显式跨班边界、八道 MES 投影、readiness 拦截和旧工单保持撤销。计数不扩大证明范围；脚本不是新增永久测试框架。

## 交付与未跑边界

本 PR 只新增报告与精选附件，运行 SHA 保持上文原归属；报告 head 的文档检查、build、diff-check 与 PR CI 分别绑定确切 SHA 留在 PR。运行代码/配置/依赖相同不等于重新启动了新 head。

未跑：跨班现场生产与换班、实际跨午夜、物料消耗/冲销、NCR、生产完工、设备采集或物理停机、打印/PDA/真机、backend/frontend/provider/FullChain 测试、独立 #4288 父验收。本票手动停机采用逻辑 `WC-CT-01` resource，设备 physical identity 为空，不将它宣称为物理设备实绩。上述范围外未跑项不追加为本切片验收门槛；本票跨班排程证据已记录，子票交付仍待 exact-head 复审、CI 与正常保护合并，不宣称 #4288 完成。

自有资源实际命令 `pwsh ./nerv.ps1 fullstack stop -SessionId nerv-bcf2-e9ad2f` exit 0，回执 `state=Stopped remaining=0`；2026-10-12 21:46:43 +08:00 后 session 标签容器与包含 session ID 的卷回读为空。launcher 已退出，临时秘密删除；脱敏证据归档及 SHA256 清单位置由 PR 给出。当前 worktree/分支保留供独立审核，未删除父编排树或其它会话。
