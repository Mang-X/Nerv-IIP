# GanttChart — 工单甘特图

> 导出:`@nerv-iip/scheduling`（**非** shadcn 原版；引擎无关的复合组件）
> 引擎接缝详见 `frontend/packages/scheduling/README.md`。

## 用途

工单 → 工序 WBS 视角的时间线:工序条、依赖链、关键路径、里程碑、进度。给跟单/管理看进度与瓶颈。
**Do NOT** 用它做资源/机台负载视角（用 `ResourceSchedulerBoard`）。

## Props / Emits

```ts
defineProps<{
  model?: ScheduleModel          // 来自 useSchedulingPlan(toModel)
  scale?: TimeScale              // 'auto'(默认) | 'hour' | 'day' | 'week' | 'month'
  readOnly?: boolean             // 默认 false
  loading?: boolean              // true → 骨架占位
  engineKind?: 'auto' | 'dhtmlx'  // 默认 auto(有 DHTMLX 引擎则用之,否则显示占位)
}>()
defineEmits<{
  taskSelect: [taskId: string]
  taskDragEnd: [payload: TaskDragPayload]   // 归一化:{ taskId, operationId, resourceId?, startUtc, endUtc, kind }
  conflictClick: [taskId: string]
}>()
// expose: command(cmd: EngineCommand)  // zoomIn/Out、scaleTo、scrollToToday、fitToScreen、selectTask…
```

## 视觉（设计系统 v2 token，零裸 hex/palette）

- 工序条：`--brand` 实心圆角胶囊；工单分组条：`--muted`。冲突：`--destructive` 描边。选中：`--brand` 描边 + 辉光。
- 网格发丝线 `--border`；now 线 `--brand` 虚线。
- 亮/暗 + 运行时动态色由 `useColorMode` / `--brand` 驱动;DHTMLX 皮肤经 `engine/dhtmlx/skin.ts` 绑 token。

## 交互 / 可达性

- 点击条 → `taskSelect`；冲突条 → `conflictClick`。可编辑时拖动 → `taskDragEnd`（落点归一化，不泄露引擎结构）。
- 缩放/刻度由工具栏经 `command` 下发。`prefers-reduced-motion` 下禁用条形过渡。

## Do / Don't

- **Do** 按页面任务独立组合工具栏与详情面板，保持甘特组件只负责工单/工序时间线。
- **Don't** 直接 import 引擎实现(DHTMLX);**Don't** 在模板出现 reasonCode/operationId 等工程语言。
