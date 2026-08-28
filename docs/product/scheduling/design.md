# 排产工作台 模块产品/业务设计

> 业务域:排产计划(/scheduling) · 前端落点:`frontend/apps/business-console/src/pages/scheduling` + `@nerv-iip/scheduling`
> 关联:MAN-523 / #964(APS 方案只读甘特)、MAN-580 / #1049(领导演示闭环)、#206(BusinessScheduling / APS lite)、#207(设备运行事实)、ADR 0014(APS 排程边界)
> 设计/实施依据:`docs/superpowers/specs/2026-06-10-unified-scheduling-gantt-design.md`、`docs/superpowers/plans/2026-06-10-unified-scheduling-gantt.md`

## 1. 这页给谁用、解决什么

- **计划员/排产员**:把已生成的排程方案看清楚(工单时间、资源负载),发现冲突与未排产工序,核查明细后发布有效方案。
- **车间主管/跟单**:从工单视角看进度与瓶颈;从资源视角看机台/工作中心的负载与过载。

当前主操作:**批量选择待排工单 → 生成首版 → 甘特/资源/表格统一编辑 → 锁定重预览 → 查看失效影响与方案对比 → 发布新版**。历史方案继续提供只读表格、甘特、明细和发布治理视图。

## 2. 信息架构(IA)

- 顶层域「排产计划」`/scheduling`,单页**排产工作台**。页内以平级 Tab 保留三个任务面(不进菜单树):
  - **领导演示工作台**:MES 待排工单池、统一 `WorkingScheduleDraft`、甘特/资源/表格编辑、锁定重预览、影响与 KPI 对比、发布新版。
  - **表格**:方案状态、失效原因、工序数、冲突/未排摘要和发布动作。
  - **甘特图**:资源泳道默认按工作中心,可切换按车间或产线分组；名称来自主数据名录,缺少归属时保留「未归属」兜底泳道；只绘制后端 assignment 的真实起止时间,支持自动适配、班次级和日级缩放及横向滚动。
- 历史甘特点击工序块打开现有方案明细 Sheet；工作台甘特、资源泳道和表格的修改全部写入同一草稿，前端不计算权威排程结果。
- 与「制造执行 › 规则排程」的关系:规则排程是 #206 之前的过渡触发入口,保留触发能力,展示统一导流到本工作台。

## 3. UX 要点

- **权威可编辑**:首版与修订版都由 BusinessScheduling 持久化生成；前端只编辑 assignment 草稿并提交显式锁定，不猜工序时长、冲突、未排原因或 KPI。
- **单一草稿**:拖拽、资源改派、时间表格、锁定/解锁和撤销/重做共享 `WorkingScheduleDraft`，不存在视图间复制状态。
- **状态不只靠颜色**:冲突使用图标 + “冲突”文字 + 实线边框,锁定使用锁图标 + “锁定”文字 + 虚线边框；未排工序在时间轴外列出业务说明。
- **时间轴背景讲日历事实**(MAN-693 / #1261):方案带出工作日历(班次窗口)与不可用窗口后,
  甘特按**权威日历**画工作/非工作底纹、在交班时刻画班次边界,并给设备维护 / 计划停机 / 换线 / 换型
  这四类窗口上可辨识的斜纹底纹(恒在卡片之下)。后端没带日历时退回「周末 + 夜间」的通用作息假设,
  不假装有日历。
- **图例即图面事实**:图例按「工序分色 / 甘特语义 / 卡片 / 状态 / 阻塞 / 日历」分组,每一项都由当前
  模型推导(`deriveLegendSemantics`)——方案里没有换型窗口就不列换型,后端没带日历就不谈班次边界。
  绝不展示图上不存在的语义。
- **失效门禁**:历史失效方案不能直接发布；修订响应按最新来源事件返回受影响资源、工单和工序，并与新候选方案一起展示后端 KPI 对比。
- **空态与错误**:无 assignment、加载失败和权限不足分别给明确反馈。
- **禁用态必须自解释**(MAN-691 / #1259):生成首版、锁定重预览、发布新版与历史表发布按钮在禁用时都用 `title` 说明**为什么**灰(缺权限 / 没选工单 / 没有草案 / 方案终态或失效),可用时说明这一步做什么。
- **失败透传服务端消息**(MAN-691 / #1259):写操作失败一律先取服务端说法(信封 `message`、RFC7807 `detail`/`title`、字段校验 `errors`)拼成「<动作>失败：<服务端消息>」;取不到才用兜底文案。generated client 在 `throwOnError` 下抛的是**响应体对象**而非 `Error`,只判 `error instanceof Error` 会把 HTTP 失败全吞成猜测文案。统一走 `apps/business-console/src/utils/notify.ts` 的 `serverErrorMessage`。
- **只读边界自解释**(MAN-691 / #1259):历史方案表标注「只读查阅」,说明这里只能查看明细 / 发布 / 撤销发布,并给「去草案工作区修改」引导入口;方案明细 Sheet 同样标注只读。
- **物料是软约束,不是排产门槛**(#1291):产品裁决——**齐套是开工门槛,不是排产门槛**。
  APS 缺料工序照排,只在方案里带出「物料风险」(缺哪个物料、缺口多少),甘特卡片、
  草案表格与方案明细都显式提示「需在开工前完成备料」;开工仍由 MES 侧的线边齐套硬门拦截。
  后端口径可配置:`Scheduling:MaterialConstraintMode` = `Soft`(默认)/ `Hard`(缺料即不可排);
  **非法值在启动期直接失败**,不静默回落到更宽松的一侧(否则一个拼错的配置会看起来"生效了")。
  **锁定工序同样吃这套语义**:它已占住计划位置,缺料照样登记风险,不因为"没参与重排"而漏掉。
  修复前 APS 直接采用 MES 线边齐套口径,备料前整批工单排不出去、后继全 `predecessorUnscheduled`,排产链断裂。
- **设备「不知道」不等于「不可用」**(#1320):设备标识两侧必须同键——排程资源 id /
  `eligibleResourceIds` 用**设备业务编码**(`DEV-CNC-01`,MasterData `code` 与 IIoT/维护世界的
  `deviceAssetId` 共同持有),不能用 MasterData 的聚合主键 GUID;错配会让可用性查询一台都命中不了。
  在此之上,**真实停机/维护/活动报警(`Unavailable`)仍是硬阻**,而**无快照 / 快照过期 /
  采集源不可达(`Unknown`)改为软约束**:工序照排,只带出「设备数据风险」并在卡片、tooltip、
  草案表格、方案明细提示「开工前请人工确认设备可用」。口径可配置:
  `Scheduling:EquipmentUnknownMode` = `Soft`(默认)/ `Hard`(状态未知即全窗不可用),
  非法值同样在启动期直接失败。修复前两个问题叠加:所有设备查无快照 → 被发全窗 `Unknown` 兜底窗口 →
  适配器把 ≠`Available` 一律当硬不可用 → 任何新方案 0 已排、发布守卫拒 error 方案,锁定→发布链结构性不可达。
- **风险是预警,不是发布门**:物料风险与设备数据风险都只登记**预警级**冲突。发布守卫只拒
  error 级冲突与未排工序——软化后的风险因此不阻断发布,而真实停机造成的未排工序照旧拒绝。
- **图例与图面一致**(#1274 铁律):「缺料待备」「设备状态未知」既然出现在甘特卡片与 tooltip 上,
  `deriveLegendSemantics` 就必须同步推导出 `status.materialRisk` / `status.equipmentRisk`,图例随之出现;
  方案全齐套、设备状态全都清楚时图例里不出现对应项。
- **随刻度出现/消失的语义要按刻度推导**(走查台账 #41):班次边界竖线由引擎逐格判定
  (单元格起点 === 班次窗口起点)。因此 `deriveLegendSemantics(model, now, scale)` 接受当前刻度,
  `calendar.shift` 按 `shiftBoundaryRendersAt` 推导,而不是只看「后端有没有带日历」;
  `'auto'` 的解析与引擎同源(`model/scale.ts` 的 `resolveTimeScale`,引擎直接调用它),
  图例与图面不许各算各的。图例消费方(`SchedulingLegend`)必须把 `scale` 传下去。
  两档判定的强度**不同,不要混为一谈**:
  - **日 / 周 / 月刻度 = 精确**:格子 ≥1 天,08:00 / 16:00 这类班次起点不可能等于格子起点 → 恒 0 条线
    (零点起班那条也只与日边界重合,讲不出班次信息)。与时间轴相位无关。台账 #41 抓到的正是这一档。
  - **班次级(hour)= 近似**:按「偶数整点即格线」判定,前提是 DHTMLX 的 2 小时格从偶数整点起步。
    引擎从不设 `config.start_date`,时间轴范围由任务时间推导后按刻度对齐,**对齐到哪一档单位
    本仓库没有证据**;时间轴若起于奇数整点,该分支会与图面相反。**治本方向**:让引擎回传实际
    生成的格线,或显式设置 `config.start_date` 钉死相位,届时该分支可升级为精确判定。
- **未排原因是中文人话**(台账 #38):不可排原因与冲突说明由 BusinessScheduling 直接产出中文业务语言,
  读面不再需要翻译枚举名;上游码值(质量放行/物料缺口)套一层中文说明再上屏。
- 空状态:无计划时指引「前往规则排程」或在需求与计划中生成方案。
- 不暴露工程语言(reasonCode/operationId/source/demo/seed 等)。

## 4. 角色与权限

- 可见域:计划员、排产员、车间主管;只读旁观:跟单、管理。
- 路由读取门槛:`business.scheduling.plans.read`；批量生成、编辑和修订:`business.scheduling.plans.manage`；发布新版:`business.scheduling.plans.release`。前端只负责提前禁用，BusinessGateway 权限检查是最终授权边界。候选工单读取继续使用 MES 工单 facade 的现有读取权限。

## 5. 数据来源(facade 代码事实)

BusinessGateway 新增两个公开两跳契约:`POST /api/business-console/v1/scheduling/workbench/plans` 从最多 500 个 MES 工单生成首版；`POST /api/business-console/v1/scheduling/plans/{planId}/revisions` 从持久化 base problem + included orders + explicit locks 生成修订版、失效影响与方案对比。既有 `list` / detail / gantt / release / revoke 继续承担历史和版本治理。
读取经 `@nerv-iip/api-client` 生成 SDK + curated barrel(`SchedulePlanContract` 等)→ `@nerv-iip/scheduling` 的 `toModel` 归一化。

## 6. 技术落点(引擎可替换)

详见 `frontend/packages/scheduling/README.md`。两层:Vue 组件层(稳定契约)→ `SchedulingEngine` 适配器 / 包内只读时间轴(无商业引擎时)→ `ScheduleModel` + `aps-mapper`。
DHTMLX Gantt 9.x 专业版仍是可选专业引擎(评估许可,禁分发,不入 git);无本地引擎包且 `readOnly=true` 时,`SchedulingCanvas` 使用同一 package 的只读 DOM 时间轴，不引入第二套第三方甘特库。编辑态无引擎时仍明确提示引擎未加载。

## 7. 分期

- **只读首版(已落地,MAN-523 / #964)**:历史表格 + 资源甘特、冲突/锁定/未排/失效状态、明细下钻和发布门禁。
- **领导演示交互版(已落地,MAN-580 / #1049)**:批量待排、统一草稿、拖拽/表格编辑、锁定重预览、失效影响、后端方案对比和新版发布；复用 `@nerv-iip/scheduling` 公开 Gantt/Resource 组件和既有 APS/override/invalidation/release 能力。
- **明确后置**:MAN-582 实际偏差预测、MAN-583 拆分/转移批/并行机建模、MAN-588 无人值守候选方案引擎；本次不修改旧 PR #178 的 scheduling visualization package。

## 8. 后端缺口(整批 consolidated issue,落地后回填 issue 号)

1. **工序依赖编辑**:契约无独立 link 端点;当前依赖链由 `operationSequence` 派生展示,不可编辑。
2. ~~**资源产能日历可视化**~~:已由 MAN-693 / #1261 关掉——`SchedulePlanContract` 增补
   `calendars` / `blockWindows` 两组只读事实(投影自排程问题的班次窗口与不可用窗口,**不新增端点、
   不新增持久化**),甘特据此画日历底纹、班次边界与阻塞斜纹。
   遗留:块的默认泳道粒度是**工作中心**(资源板默认按工作中心铺泳道,也可切到车间/产线),单台设备维护会落在其工作中心对应的分组行;
   设备级泳道要等资源维度切换落地。设备运行事实(#207)推出的实时不可用窗口只进入排程输入、
   不落问题快照,因此**重新读取历史方案时不会带出**——需要时再单独立项持久化。

> Issue:_待发后回填_(`scheduling.前端MVP后端缺口`)。

## 9. 验收

- 一次可选择 100 条以上、最多 500 条待排工单；首版与修订版都由权威服务持久化，锁定 assignment 在修订中保持资源与时间。
- 甘特拖拽、资源泳道、表格编辑和撤销/重做共享同一草稿；失效影响与准时率、延期、利用率、移动/锁定/未排统计均来自后端响应。
- `@nerv-iip/scheduling` 保持零差异；business-console typecheck/test/build 全绿。
- E2E 渲染与视图切换、视觉基线、性能门禁(~2000 工序)就绪。
- UI 无工程语言、无假数据/假分页;文档与代码同步。
