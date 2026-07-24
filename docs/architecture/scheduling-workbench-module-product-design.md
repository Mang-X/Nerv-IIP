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
  - **甘特图**:工作中心/资源为行,只绘制后端 assignment 的真实起止时间；支持自动适配、班次级和日级缩放及横向滚动。
- 历史甘特点击工序块打开现有方案明细 Sheet；工作台甘特、资源泳道和表格的修改全部写入同一草稿，前端不计算权威排程结果。
- 与「制造执行 › 规则排程」的关系:规则排程是 #206 之前的过渡触发入口,保留触发能力,展示统一导流到本工作台。

## 3. UX 要点

- **权威可编辑**:首版与修订版都由 BusinessScheduling 持久化生成；前端只编辑 assignment 草稿并提交显式锁定，不猜工序时长、冲突、未排原因或 KPI。
- **单一草稿**:拖拽、资源改派、时间表格、锁定/解锁和撤销/重做共享 `WorkingScheduleDraft`，不存在视图间复制状态。
- **状态不只靠颜色**:冲突使用图标 + “冲突”文字 + 实线边框,锁定使用锁图标 + “锁定”文字 + 虚线边框；未排工序在时间轴外列出业务说明。
- **失效门禁**:历史失效方案不能直接发布；修订响应按最新来源事件返回受影响资源、工单和工序，并与新候选方案一起展示后端 KPI 对比。
- **空态与错误**:无 assignment、加载失败和权限不足分别给明确反馈。
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
2. **资源产能日历可视化**:调度器已消费权威日历，但编辑 UI 尚未绘制独立产能带。

> Issue:_待发后回填_(`scheduling.前端MVP后端缺口`)。

## 9. 验收

- 一次可选择 100 条以上、最多 500 条待排工单；首版与修订版都由权威服务持久化，锁定 assignment 在修订中保持资源与时间。
- 甘特拖拽、资源泳道、表格编辑和撤销/重做共享同一草稿；失效影响与准时率、延期、利用率、移动/锁定/未排统计均来自后端响应。
- `@nerv-iip/scheduling` 保持零差异；business-console typecheck/test/build 全绿。
- E2E 渲染与视图切换、视觉基线、性能门禁(~2000 工序)就绪。
- UI 无工程语言、无假数据/假分页;文档与代码同步。
