# 统一排程可视化组件设计(甘特图 / 资源排产板)

> 日期:2026-06-10 ·  状态:设计已批准,转实施
> 范围:`frontend/packages/scheduling`(新建)+ business-console `/scheduling` 接入
> 关联:#78(甘特/排产前端参考)、#206(BusinessScheduling / APS lite)、#207(设备运行事实联动)、ADR 0014(APS 与设备 IIoT 排程边界)

## 1. 背景与目标

自研甘特图进展缓慢。决策:**先用 DHTMLX Gantt 专业版试用版做 MVP**,但通过一层**引擎无关的统一接口**封装,使后续切换到自研引擎时**业务层与页面零改动**。

交付两个组件:

- **工单甘特(`GanttChart`)** —— 工单 → 工序 WBS 视角的时间线,给跟单/管理看进度、依赖、关键路径、瓶颈。
- **资源排产板(`ResourceSchedulerBoard`)** —— 工作中心/资源为行的负载视角,给计划员排产、看过载与利用率。

约束(来自 `frontend/apps/business-console/AGENTS.md` 与设计系统 v2):

1. 设计系统 v2:near-black `--primary` + 动态 `--brand` 蓝点缀 + 亮暗双主题,token 单一源在 `@nerv-iip/ui`。
2. 不改 shadcn 原版组件;定制一律复制重建。
3. 三大支柱(产品·业务·UX);UI 不暴露工程语言(operationId/source/code/demo/seed…);配置驱动、少写死;文档与代码同步。
4. 后端能力以 facade 代码事实为准。

## 2. 后端事实(已存在,不在本次改动范围)

BusinessGateway 已暴露 `/api/business-console/v1/scheduling/plans` 全套:`preview` / `list` / `create` / `{planId}`(detail)/ `{planId}/gantt` / `{planId}/release`。`@nerv-iip/api-client` 的 business-console 导出中已有生成类型:

- `NervIipContractsSchedulingSchedulePlanContract`:`planId / problemId / problemFingerprint / algorithmVersion / status('preview'|'generated'|'released') / generatedAtUtc / assignments[] / resourceLoads[] / conflicts[] / unscheduledOperations[] / changeSummary[] / ganttItems[]`。
- `ScheduleAssignmentContract`:`assignmentId / orderId / operationId / operationSequence / resourceId / workCenterId / startUtc / endUtc / isLocked / explanationCode`。
- `ScheduleResourceLoadContract`:`resourceId / windowStartUtc / windowEndUtc / assignedMinutes / availableMinutes / utilization`。
- `ScheduleConflictContract`:`conflictId / reasonCode / severity('info'|'warning'|'error') / orderId / operationId / resourceId / message`。
  - `reasonCode ∈ {dueDate, capacity, calendar, material, quality, equipment, noEligibleResource, outsideHorizon, invalidLockedAssignment, predecessorUnscheduled}`。
- `UnscheduledOperationContract`:`orderId / operationId / reasonCode / message`。
- `ScheduleChangeContract`:`orderId / operationId / changeType('added'|'moved'|'delayed'|'preserved'|'blocked') / message`。
- `GanttScheduleItemContract`:`itemId / orderId / operationId / operationSequence / resourceId / workCenterId / startUtc / endUtc / status / hasConflict / conflictReasonCode`。

后端算法是**确定性有限产能启发式,不做全局优化/自动重排**,但支持 `isLocked`(锁定分配)+ `preview`(围绕锁定项重算)+ `conflicts` + `changeSummary`。**这决定了前端编辑语义(见 §6)。**

> 注:契约里没有显式的"工序依赖链(link)"与"资源产能日历"的独立端点。本次:依赖链由 `operationSequence`(同工单工序顺序)派生展示,真正的 link 编辑作为**后端缺口**整批发 consolidated issue;资源产能由 `resourceLoads` 的 `availableMinutes` 近似展示。

## 3. 架构:三层 + 中间接缝

```
┌─ Vue 组件层(稳定契约;business-console 只认这层)─────────────────┐
│  <GanttChart> · <ResourceSchedulerBoard> · <SchedulingWorkbench>           │
│  props / emits / slots 为公开契约;内部通过 provide/inject 选 engine      │
├─ SchedulingEngine 适配器接口(← 换引擎的唯一接缝)──────────────────┤
│  mount(el,opts) · setData(model) · applyCommand(cmd) · on(evt,cb) · destroy()│
│   ├─ DhtmlxEngine  —— 封装 DHTMLX Gantt 9.x 试用版 vanilla 核心(MVP)      │
│   └─ NativeEngine  —— 轻量 SVG 渲染器(自研对接位 + CI/视觉/性能/降级兜底)│
├─ 数据契约层(引擎无关)────────────────────────────────────────────┤
│  ScheduleModel(task/link/resource/assignment/conflict/scale/load)         │
│  aps-mapper:SchedulePlanContract ⇄ ScheduleModel(纯函数,往返可测)      │
└──────────────────────────────────────────────────────────────────┘
```

**可替换性保证**:`SchedulingEngine` 是 headless 接口;`DhtmlxEngine` 与 `NativeEngine` 必须共同通过同一套**引擎契约测试**(engine conformance test)。换自研版本 = 新增一个实现 `SchedulingEngine` 的类并通过该契约测试,组件层与业务层零改动。

### 3.1 SchedulingEngine 接口(草案,实施时细化)

```ts
export interface SchedulingEngineOptions {
  view: 'order' | 'resource'        // 工单甘特 / 资源排产板
  readOnly: boolean
  scale: TimeScale                  // 'hour' | 'day' | 'week' | 'month' | 'auto'
  theme: ThemeBinding               // 设计 token → 引擎样式变量
  locale: 'zh' | 'en'
}

export interface SchedulingEngine {
  mount(container: HTMLElement, options: SchedulingEngineOptions): void
  setData(model: ScheduleModel): void
  applyCommand(command: EngineCommand): void   // zoomIn/Out, scaleTo, scrollToToday,
                                               // selectTask, lockTask, autoSchedule,
                                               // undo, redo, focusConflict, fitToScreen
  on<E extends EngineEventName>(event: E, cb: (payload: EngineEventPayload<E>) => void): Unsubscribe
  getState(): EngineSnapshot          // 当前缩放/选中/视口,供持久化
  destroy(): void
}
```

引擎事件(归一化,不泄露 DHTMLX 细节):`taskSelected` · `taskDragEnd`(改期/改时长/改派)· `linkCreated` · `linkDeleted` · `scaleChanged` · `viewportChanged` · `conflictClicked`。

`taskDragEnd` 携带归一化的 `{ operationId, resourceId, startUtc, endUtc, kind: 'move'|'resize'|'reassign' }`,**不含任何引擎私有结构**——这是接缝处的关键契约。

### 3.2 数据模型(ScheduleModel,引擎无关)

```ts
interface ScheduleModel {
  tasks: ScheduleTask[]          // 工序(可含工单作为分组父节点)
  links: ScheduleLink[]          // 依赖(MVP 由 operationSequence 派生)
  resources: ScheduleResource[]  // 工作中心/资源(资源视图行 + 产能)
  loads: ResourceLoadBucket[]    // 资源负载(直方图/利用率)
  conflicts: ScheduleConflict[]
  unscheduled: UnscheduledItem[]
  changes: ScheduleChange[]      // 重预览 diff
  horizon: { startUtc: string; endUtc: string }
  meta: { planId: string; status: PlanStatus; algorithmVersion: string }
}
```

`aps-mapper` 提供 `toModel(plan): ScheduleModel` 与 `toLockedAssignments(model): ScheduleAssignmentContract[]`(供重预览回传),两向纯函数、可单测往返。

## 4. DHTMLX 接入与许可合规

- 试用包事实:DHTMLX Gantt **v9.1.4 专业版评估许可**(XB Software);提供 ESM 构建 `dhtmlxgantt.es.js` + 类型 `.d.ts`;PRO 特性齐(critical_path / auto_scheduling / 资源面板 / 资源负载图 / undo / grouping / constraints)。**无 Vue 封装**(自封装 vanilla 核心)。
- **许可合规(硬约束)**:评估许可**禁止分发** → 库文件**不进 git**。安装走 trial 私有源:`@dhx:registry=https://npm.dhtmlx.com` + 可选依赖 `@dhx/trial-gantt`;`.npmrc` 记 registry,`gantt_trial` 本地目录作样例参考源。任何 vendored 拷贝进 `.gitignore`。
- 适配器:动态 `import()` 引擎(按需加载,包构建不强依赖);每实例 `Gantt.getGanttInstance()`;映射 config(layout + 资源面板)与事件;**用 CSS 变量把 DHTMLX 皮肤绑到设计 token**(条形 `--brand`、冲突 `--destructive`、网格/文本跟随亮暗),实现 v2 质感。
- **无许可可 CI**:`@nerv-iip/scheduling` 构建 + 单元/契约/组件测试跑在 `NativeEngine` 上;DHTMLX 集成/视觉/性能测试在检测到 trial 包时才执行,否则带清晰提示 skip(对齐本仓既有"许可/基础设施门禁"模式)。

## 5. UI / IA / UX(遵循设计系统 v2)

### IA
- business-console 新增 `/scheduling` 域「排产工作台」。页内**视图切换**(工单甘特 / 资源排产板)属布局,**不进菜单树**(守 AGENTS「Tabs 属布局」规则)。
- 复用区块:`PageHeader + Toolbar + SectionCards`;KPI:计划版本 / 工序数 / 冲突数 / 未排产数 / 平均资源利用率。

### UX
- **锁定—重预览闭环**(§6):拖动 → 标记锁定 → 重预览 → 变更摘要侧栏 + 冲突高亮 → release。
- 冲突面板:reason-code 业务化芯片(交期/产能/日历/物料/质量/设备/无可用资源…),点击选中并滚动到问题条。
- 未排产面板:每项给"原因(业务语言)+ 去修复"出路。
- 空状态指向下一步("暂无排程计划,请先生成计划");点条出检视 Sheet(工单/工序/资源/起止/解释)。
- 术语说人话,绝不出现 operationId/source/code/demo/seed。

### UI 质感(高级 · 呼吸 · 创新)
- 暗色优先画布(低于卡片一档,inset 浮层 + `--shadow-*` 立体);工序条为**浮起圆角胶囊**、柔阴影、按状态微渐变;品牌色驱动选中/关键路径辉光;发丝级网格;宽松行高(呼吸感)。
- 微交互:hover 抬升、拖拽幽灵 + 吸附辅助线、now 线脉冲、冲突脉冲环(遵守 `prefers-reduced-motion`)。
- 资源直方图:半透明产能带 + 过载热度色阶。
- 创新点:**时间密度自适应刻度**(随缩放自动日/周/月)、大计划**缩略概览 ribbon**、冲突/关键路径**焦点模式**(暗化无关行)。
- 全部走 token → 亮暗 + 运行时动态色,无裸 palette / 裸 hex。

## 6. 编辑语义:锁定—重预览闭环(已确认)

"完整可编辑工作台"落成**贴合后端的锁定—重预览**模型,而非前端自由持久化:

1. 用户拖动工序(改期/改时长/改派)→ 引擎发 `taskDragEnd`。
2. 前端在本地模型把该分配标记 `isLocked` 到新位置(乐观更新)。
3. 调 `preview`(携带全部锁定分配)→ 后端围绕锁定项重算 → 返回新 `SchedulePlanContract`(含 `changeSummary` / `conflicts` / `unscheduledOperations`)。
4. 前端 diff 高亮(added/moved/delayed/preserved/blocked),侧栏列变更与新冲突。
5. **撤销/重做** = 前端计划状态栈(快照),不触后端。
6. 满意 → `release` 提交;失败/不满意 → 撤销回上一快照。

`autoSchedule`(DHTMLX PRO 能力)在 MVP 仅作**客户端预排辅助**(可视化建议),最终仍以后端 `preview`/`release` 为权威,不绕过后端持久化。

## 7. 打包落点

新建 `frontend/packages/scheduling` → `@nerv-iip/scheduling`(镜像 `@nerv-iip/ui`:`private` / `type:module` / 源码导出 `./src/index.ts` / `vue-tsc` typecheck / `vp test run src`):

```
src/
  model/        types.ts · aps-mapper.ts · (*.test.ts)
  engine/       engine.ts(接口) · conformance.ts(契约测试套件)
                dhtmlx/ DhtmlxEngine.ts · skin.css.ts
                native/ NativeEngine.ts(SVG)
  components/   GanttChart.vue · ResourceSchedulerBoard.vue · SchedulingWorkbench.vue
                panels/ (ConflictPanel · UnscheduledPanel · ChangeSummaryPanel · InspectorSheet · ToolbarScheduling)
  composables/  useSchedulingPlan.ts · useSchedulingEdits.ts · useEngine.ts
  index.ts      barrel(只导出公开契约)
```

依赖:`@nerv-iip/ui` · `@nerv-iip/api-client` · `vue` · 可选 `@dhx/trial-gantt`。
business-console:新增 `src/pages/scheduling/*`、`composables/useScheduling.ts`、`navigation.ts` 项 + 权限对齐;占位 `mes/schedules.vue` 导流到新工作台(保留规则排程触发入口,但展示走新组件)。

## 8. 测试体系

- **单元/契约**:`aps-mapper` 往返;**引擎契约测试**(两适配器共同通过);组件 props/emits/slots(`NativeEngine` + jsdom,免 DHTMLX)。`vitest` / `vp test`。
- **E2E(Playwright)**:载入计划 → 渲染 → 缩放/刻度切换 → 拖动改派 → 重预览 → 变更摘要出现 → 选中冲突滚动定位 → release。默认 `NativeEngine`(确定性、免许可、CI 稳),DHTMLX 变体 opt-in。
- **视觉回归**:两组件 × 亮/暗 × 动态色 × {空/加载/正常/冲突/未排产} 截图基线(`NativeEngine` 保确定性)。
- **性能门禁**:大数据集(~2k 工序 / 200 资源)首屏渲染 + 滚动/缩放帧成本阈值,JSONL 指标输出,门禁化(对齐后端 `verify-business-performance-baseline` 模式);DHTMLX 开 `smart_rendering`,`NativeEngine` 行虚拟化。

## 9. 文档(同步交付)

1. 本 spec(已提交)。
2. 模块产品/业务设计文档:`docs/architecture/scheduling-workbench-module-product-design.md`(产品/IA/UX/分期/验收/后端缺口)。
3. DESIGN 契约:`frontend/DESIGN/components/gantt-chart.md` · `resource-scheduler-board.md` · `patterns/blocks/scheduling-workbench.md`。
4. **引擎适配器契约文档**(自研对接说明):`frontend/packages/scheduling/README.md` + DESIGN governance 一节。
5. 更新 `frontend/DESIGN/index.md`(组件/路线图索引)、`docs/architecture/frontend-navigation-map.md`(新增 /scheduling)、`docs/architecture/implementation-readiness.md`(记一笔)。

## 10. 验收标准(Done)

- `pnpm -C frontend typecheck && test && build` 全绿;新增组件/页有契约或单测覆盖。
- 引擎契约测试通过(DHTMLX 在 trial 存在时 + Native 始终)。
- E2E 主链路绿(NativeEngine);视觉基线已建;性能门禁有阈值与 JSONL。
- 两组件在亮/暗 + 动态色下质感达标(高级/呼吸/创新),无裸 palette / 裸 hex / 工程语言 / 假数据。
- 文档(§9)全部同步;后端缺口(link 编辑、产能日历)已发 consolidated issue 并在模块文档回填。
- 切换引擎路径在 README 有可操作说明,并由引擎契约测试背书。

## 11. 实施分期

- **P0 接缝先行**:包骨架 + ScheduleModel + aps-mapper + SchedulingEngine 接口 + 引擎契约测试 + NativeEngine。(可替换性与测试地基)
- **P1 DHTMLX MVP**:DhtmlxEngine + token 皮肤 + GanttChart/ResourceSchedulerBoard + SchedulingWorkbench(只读 → 锁定-重预览)。
- **P2 业务接入**:business-console `/scheduling` 页 + 导航 + 权限 + composables;schedules.vue 导流。
- **P3 测试与文档**:E2E / 视觉 / 性能 + 全部文档 + 门禁全绿。
- **P4 成品确认**:浏览器可视化向用户确认。

## 12. 风险与缓解

| 风险 | 缓解 |
|---|---|
| 试用版 30 天到期 / 禁分发 | 引擎无关接缝 + NativeEngine 兜底;库不入 git;到期切自研或采购仅换适配器 |
| 后端缺 link 编辑 / 产能日历端点 | MVP 由 operationSequence/resourceLoads 近似;整批发 consolidated issue,模块文档回填 |
| DHTMLX 自带皮肤与 v2 设计系统冲突 | CSS 变量绑设计 token,不接受其默认皮肤;视觉回归守底线 |
| 大数据集性能 | smart_rendering / 虚拟化 + 性能门禁阈值 |
| CI 无 DHTMLX 许可 | 默认引擎用 Native;DHTMLX 测试 opt-in 且 skip-with-reason |
