# ResourceSchedulerBoard — 资源排产板

> 导出:`@nerv-iip/scheduling`（引擎无关的复合组件，与 `GanttChart` 同契约、同引擎）

## 用途

工作中心/资源为行的负载视角:工序块按资源时间轴排布 + 资源负载/利用率（产能带 + 过载热度）。给计划员排产、看过载。
**Do NOT** 用它看工单 WBS 进度（用 `GanttChart`）。

## Props / Emits

与 `GanttChart` **完全一致**（`model / scale / readOnly / loading / engineKind`；`taskSelect / taskDragEnd / conflictClick`；expose `command`）。
差异仅在内部 `view='resource'`:行=资源,工序落到其 `resourceId` 行;order 分组节点不出条;额外渲染 `loads` 直方图。

## 视觉（token）

- 资源行斑马纹 `--muted`;工序块 `--brand`;过载负载带 `color-mix(--warning→--destructive)` 按利用率加深。
- 其余同 `GanttChart`（now 线、选中辉光、冲突描边、亮暗 + 动态色）。

## 交互

- 拖动工序到**另一资源行/时间** → `taskDragEnd { kind: 'reassign' | 'move' }`,经工作台进入锁定—重预览。
- 利用率 >1 的资源带显著加深,提示过载。

## Do / Don't

- **Do** 与 `GanttChart` 共享同一 `ScheduleModel` 与 `SchedulingEngine`,在工作台内用 Tabs 切换。
- **Don't** 为资源视图复制一套数据契约;两视图只是同一模型的不同编排。
