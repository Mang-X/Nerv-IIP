# Scheduling Workbench — 排产工作台（区块模式）

> 组件:`SchedulingWorkbench`（`@nerv-iip/scheduling`）。页面：`apps/business-console/src/pages/scheduling/index.vue`。

## 组成

```
PageHeader(标题 + 计划选择)            ← 页面层
SectionCards(KPI:状态/工序/冲突/未排/利用率)  ← 页面层
└ SchedulingWorkbench                         ← 区块
   ├ SchedulingToolbar(刻度/缩放/今天/撤销重做/重排/发布/只读)
   ├ Tabs(工单甘特 | 资源排产板) → GanttChart / ResourceSchedulerBoard
   ├ 右栏 Tabs(冲突 | 未排产 | 变更摘要)
   └ InspectorSheet(点条出详情)
```

## 用法

```vue
<SchedulingWorkbench
  :model="model"            // useScheduling().model
  :loading="loading"
  :release="release"        // 注入后端发布;preview 缺省为客户端保持锁定(见后端缺口)
  engine-kind="auto"
  default-view="order"
/>
```

## 编辑闭环

拖动 → 锁定 → 工具栏「重新排程」(`useSchedulingEdits.repreview`) → 变更摘要/冲突刷新 → 「发布计划」。撤销/重做为前端快照栈。
`preview`/`release` 由业务层注入(`useScheduling`),工作台与后端问题定义解耦。

## Do / Don't

- **Do** 页面只负责 PageHeader/KPI/计划选择/空状态,工作台负责图与编辑闭环。
- **Don't** 把视图切换做成菜单项(属页内布局);**Don't** 在工作台内直接 import 引擎实现。
