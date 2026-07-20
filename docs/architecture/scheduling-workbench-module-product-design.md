# 排产工作台 模块产品/业务设计

> 业务域:排产计划(/scheduling) · 前端落点:`frontend/apps/business-console/src/pages/scheduling` + `@nerv-iip/scheduling`
> 关联:MAN-523 / #964(APS 方案只读甘特)、#78(后续交互式甘特/RFC)、#206(BusinessScheduling / APS lite)、#207(设备运行事实)、ADR 0014(APS 排程边界)
> 设计/实施依据:`docs/superpowers/specs/2026-06-10-unified-scheduling-gantt-design.md`、`docs/superpowers/plans/2026-06-10-unified-scheduling-gantt.md`

## 1. 这页给谁用、解决什么

- **计划员/排产员**:把已生成的排程方案看清楚(工单时间、资源负载),发现冲突与未排产工序,核查明细后发布有效方案。
- **车间主管/跟单**:从工单视角看进度与瓶颈;从资源视角看机台/工作中心的负载与过载。

当前主操作:**看计划 → 看冲突/未排产 → 核查明细 → 发布有效方案**。拖拽改期、改派和自动重排不属于只读首版。

## 2. 信息架构(IA)

- 顶层域「排产计划」`/scheduling`,单页**排产工作台**。页内以平级 Tab 保留两种核查方式(不进菜单树):
  - **表格**:方案状态、失效原因、工序数、冲突/未排摘要和发布动作。
  - **甘特图**:工作中心/资源为行,只绘制后端 assignment 的真实起止时间；支持自动适配、班次级和日级缩放及横向滚动。
- 点击甘特工序块打开现有方案明细 Sheet；甘特内不编辑业务事实。
- 与「制造执行 › 规则排程」的关系:规则排程是 #206 之前的过渡触发入口,保留触发能力,展示统一导流到本工作台。

## 3. UX 要点

- **只读可信**:只画 facade 返回的 assignment,不猜工序时长、资源或依赖；时间非法和资源缺失的 assignment 不绘制并给出计数诊断。
- **状态不只靠颜色**:冲突使用图标 + “冲突”文字 + 实线边框,锁定使用锁图标 + “锁定”文字 + 虚线边框；未排工序在时间轴外列出业务说明。
- **失效门禁**:全局黄色提示展示失效原因,甘特发布按钮禁用；用户须重排并生成新方案。
- **空态与错误**:无 assignment、加载失败和权限不足分别给明确反馈。
- 空状态:无计划时指引「前往规则排程」或在需求与计划中生成方案。
- 不暴露工程语言(reasonCode/operationId/source/demo/seed 等)。

## 4. 角色与权限

- 可见域:计划员、排产员、车间主管;只读旁观:跟单、管理。
- 当前 `navigation.ts` 的 `requiredPermissions` 为宽松默认;BusinessGateway 的 `scheduling.*` 读/发布权限为权威。新增「调整/发布」操作时,前端可见性与网关授权**一起改**,补 `scheduling.plan.read` / `scheduling.plan.release` 等权限码映射(随权限拆分 PR 同步)。

## 5. 数据来源(facade 代码事实)

BusinessGateway `/api/business-console/v1/scheduling/plans`:`list` / `{planId}`(detail)/ `{planId}/gantt` / `{planId}/release` / `preview` / `create`(POST 问题定义)。
读取经 `@nerv-iip/api-client` 生成 SDK + curated barrel(`SchedulePlanContract` 等)→ `@nerv-iip/scheduling` 的 `toModel` 归一化。

## 6. 技术落点(引擎可替换)

详见 `frontend/packages/scheduling/README.md`。两层:Vue 组件层(稳定契约)→ `SchedulingEngine` 适配器 / 包内只读时间轴(无商业引擎时)→ `ScheduleModel` + `aps-mapper`。
DHTMLX Gantt 9.x 专业版仍是可选专业引擎(评估许可,禁分发,不入 git);无本地引擎包且 `readOnly=true` 时,`SchedulingCanvas` 使用同一 package 的只读 DOM 时间轴，不引入第二套第三方甘特库。编辑态无引擎时仍明确提示引擎未加载。

## 7. 分期

- **只读首版(已落地,MAN-523 / #964)**:表格 + 资源甘特、真实 assignment 映射、自动/班次/日缩放、冲突/锁定/未排/失效状态、明细下钻、发布门禁；有 DHTMLX 时复用专业引擎,无 vendor 时复用包内只读适配层。
- **后续交互版(#78)**:拖拽改派/改期、撤销/重做和围绕锁定项重预览；必须另行完成后端语义与审批设计。
- **后续**:真正的后端重预览(见 §8 缺口)、依赖编辑、产能日历可视化、与 #207 设备 availability 联动着色。

## 8. 后端缺口(整批 consolidated issue,落地后回填 issue 号)

1. **从计划 + 锁定分配重预览**:现有 `preview` 需要完整排程问题定义(`SchedulingProblemContract`),前端仅持有计划。需提供「按 planId + 锁定分配重算」端点,或让计划可回溯其问题。当前前端「重新排程」在缺该端点时仅在客户端保持锁定项,不做后端重算。
2. **工序依赖编辑**:契约无独立 link 端点;MVP 依赖链由 `operationSequence` 派生展示,不可编辑。
3. **资源产能日历**:MVP 以 `resourceLoads.availableMinutes` 近似;缺资源工作日历端点用于精确产能带。

> Issue:_待发后回填_(`scheduling.前端MVP后端缺口`)。

## 9. 验收

- 工作台在亮/暗 + 动态色下展示表格与只读资源甘特;冲突/锁定/未排/失效不只靠颜色区分,点击工序可检视明细,失效方案不可发布。
- `@nerv-iip/scheduling` 只读适配层与引擎契约测试通过;business-console typecheck/test/build 全绿。
- E2E 渲染与视图切换、视觉基线、性能门禁(~2000 工序)就绪。
- UI 无工程语言、无假数据/假分页;文档与代码同步。
