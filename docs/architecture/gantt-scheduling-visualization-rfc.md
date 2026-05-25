# Gantt And Scheduling Visualization RFC

本文档收口 GitHub issue #78：基于 shadcn-vue + Leafer UI 构建甘特图与排产图的技术方案评估，并记录 2026-05-25 的前端组件包 foundation 实施范围。

## 结论

1. shadcn-vue 适合作为甘特/排产页面的表格、工具栏、筛选、弹窗、上下文菜单和反馈组件来源；在本仓库中必须通过 `@nerv-iip/ui` 稳定导出使用，不允许业务页面 deep-import shadcn 组件。
2. Leafer UI 作为 Canvas 渲染引擎已进入 `frontend/packages/scheduling-visualization`，但只通过本地 adapter 暴露给组件，不允许 Console 页面直接导入。
3. 2026-05-25 foundation slice 已交付 mock-only 甘特图、排程图、toolbar、legend、detail sheet、workspace、time-scale、command stack、scene renderer、search/filter、visible row virtualization、drag preview intent events、package-local preview 和 package tests；不接真实后端。
4. 当前仍不进入业务后端、领域服务或 Console MVP 路由范围。仓库主线继续以 #77 full-chain acceptance 和已拆分业务服务 issue 为准。
5. 真实 Console 页面实施前，仍必须另建独立 feature issue/spec，先冻结后端 APS 或 MES schedule 查询契约，再接入 generated API client 和路由权限。

## 边界

### 进入后续候选范围

- 任务、工单、工序、资源、班次、依赖、工作日历/维护/停机窗口、进度、约束冲突的只读可视化。
- 基于后端 APS/MES 返回结果的拖拽预览、调整建议提交和冲突提示。
- 网格与时间轴联动、展开折叠、搜索筛选、撤销重做和导出。

### 当前不实施

- 不在本轮 Console MVP 中新增甘特页面、时间轴编辑器或排产操作 UI。
- 不在 Console app 中直接引入 `leafer-ui`、`@tanstack/vue-virtual`、Excel/PDF export 等新运行时依赖。
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

shadcn-vue remains the UI shell; Leafer UI remains behind a local adapter so API churn is contained. The package should not own MES, DemandPlanning, Inventory, WMS or ERP facts. The current slice consumes mock fixtures only; a future Console integration may consume generated API clients and emit intent commands after contracts are frozen.

## 2026-05-25 Foundation Delivered

`@nerv-iip/scheduling-visualization` now provides:

1. Mock Gantt and schedule fixtures for package-local tests and demos.
2. Task/resource models, row flattening/grouping, time-scale math and visible range helper.
3. Undo/redo preview command state and typed selection state.
4. Leafer scene builders plus `renderSceneToLeafer`.
5. Vue components: `GanttChart`, `ScheduleChart`, `SchedulingToolbar`, `SchedulingLegend`, `SchedulingDetailSheet` and `SchedulingWorkspace`.
6. Public host integration events: `selectionChange`, `previewCommand`, `commitPreview` and `resetPreview`.
7. Schedule operation dependency links, selected-chain/all/hidden link modes, calendar highlight windows, conflict reason/submit-policy metadata, explicit parallel-capacity lane layout, and package-local Vite preview via `pnpm -C frontend --filter @nerv-iip/scheduling-visualization dev`.

The package separates visual collisions from scheduling rules. Exclusive resource overlaps remain visually overlapped and are displayed as conflicts; bars only use lanes when a fixture explicitly marks operations as a shared parallel capacity group. Blocking overlap, downtime, dependency, and capacity decisions must be supplied as conflict facts by a fixture or future APS/MES API payload.

The package deliberately does not add a Console route, Gateway facade, OpenAPI contract, generated API client change, persistence schema, or backend scheduling engine.

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
