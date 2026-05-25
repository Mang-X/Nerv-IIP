# Gantt And Scheduling Visualization RFC

本文档收口 GitHub issue #78：基于 shadcn-vue + Leafer UI 构建甘特图与排产图的技术方案评估。

## 结论

1. shadcn-vue 适合作为甘特/排产页面的表格、工具栏、筛选、弹窗、上下文菜单和反馈组件来源；在本仓库中必须通过 `@nerv-iip/ui` 稳定导出使用，不允许业务页面 deep-import shadcn 组件。
2. Leafer UI 适合作为 Canvas 渲染候选：当前文档确认其具备 DOM view 初始化、`Rect`/`Text`/`Pen` 等绘制元素、`Group` 层级管理、pointer/drag/zoom/menu 事件以及 PNG/JPEG/SVG/JSON export 能力。
3. #78 只完成技术方案归档，不进入当前业务后端、领域服务或 Console MVP 实施范围。仓库当前主线仍以 #77 full-chain acceptance 和已拆分业务服务 issue 为准。
4. 甘特图/排产图进入实施前，必须另建独立 feature issue/spec，先冻结后端 APS 或 MES schedule 查询契约，再做前端组件包和页面。

## 边界

### 进入后续候选范围

- 任务、工单、工序、资源、班次、依赖、进度、约束冲突的只读可视化。
- 基于后端 APS/MES 返回结果的拖拽预览、调整建议提交和冲突提示。
- 网格与时间轴联动、展开折叠、搜索筛选、撤销重做和导出。

### 当前不实施

- 不在本轮 Console MVP 中新增甘特页面、时间轴编辑器或排产操作 UI。
- 不在前端引入 `leafer-ui`、`@tanstack/vue-virtual`、Excel/PDF export 等新运行时依赖。
- 不在 MES、DemandPlanning 或 ERP 中补 APS 优化引擎。
- 不改变 #77 full-chain acceptance 的验收范围。

## 推荐架构

```text
frontend/packages/scheduling-visualization
  +-- model/              # task/resource/dependency/calendar DTO adapters
  +-- time-scale/         # date <-> pixel, zoom levels, visible range
  +-- commands/           # move/resize/assign undo-redo commands
  +-- canvas/             # Leafer adapter, layers, renderers, interactions
  +-- components/         # Vue wrappers, grid shell, toolbar, dialogs
  +-- tests/              # model/time-scale/command/canvas adapter tests

frontend/apps/console
  +-- pages/business/...  # only imports stable package APIs
```

shadcn-vue remains the UI shell; Leafer UI remains behind a local adapter so API churn is contained. The package should not own MES, DemandPlanning, Inventory, WMS or ERP facts. It consumes generated API clients and emits intent commands only.

## Phased Delivery

| Phase | Scope | Exit Criteria |
| --- | --- | --- |
| M0 Contract spike | Freeze schedule DTOs, row hierarchy, dependency and conflict payloads. | Contract tests prove Console can render fixture data without domain leakage. |
| M1 Read-only Gantt MVP | Grid, time scale, task bars, milestones, today line and basic dependencies. | 100/1,000 row fixtures render with no horizontal page overflow and no auth bypass. |
| M2 Interaction preview | Drag/resize preview, conflict markers, undo/redo and submit intent. | Operations stay client-side until explicit save; server remains source of truth. |
| M3 Scheduling view | Resource rows, load histogram, capacity threshold and locked tasks. | Overload/conflict semantics are driven by APS/MES payloads, not frontend rules. |
| M4 Hardening | Virtualization, keyboard access, export and Playwright visual checks. | Performance benchmark and browser screenshots are attached to the implementing PR. |

## Risks

| Risk | Level | Mitigation |
| --- | --- | --- |
| Canvas API churn | Medium | Hide Leafer behind adapter interfaces and pin package versions per PR. |
| Dependency route complexity | High | Start with simple orthogonal lines; postpone obstacle avoidance until user cases demand it. |
| Large schedules | Medium | Design visible row/time range virtualization before M2, not after performance regressions. |
| Vue state and canvas drift | Medium | Keep authoritative state in Pinia/composables; canvas renderers receive immutable snapshots. |
| Domain rules in frontend | High | Frontend displays conflicts returned by APS/MES; it does not make finite-capacity scheduling decisions. |

## Implementation Gate

A future implementation issue must include all of the following before code starts:

1. Backend owner and public API contract for schedule data and update intents.
2. `@nerv-iip/ui` component gaps already available or explicitly added through the design system path.
3. A package-level test plan covering time-scale math, command undo/redo and renderer snapshot behavior.
4. Browser verification plan for desktop and mobile viewports.
5. Performance fixture sizes and acceptable frame/update budgets.

Until those gates are met, #78 is considered completed as an RFC archive only.
