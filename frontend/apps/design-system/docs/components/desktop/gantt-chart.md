---
title: GanttChart 工单甘特
pageClass: ds-wide
aside: false
---

<script setup>
import { GanttChart } from '@nerv-iip/scheduling'
import { ref } from 'vue'
import { makeModel } from '../../.vitepress/schedulingDemo'

const model = ref(makeModel())

// 拖拽落点更新模型(否则引擎按原模型重绘,条会弹回原位)。
function onDrag(p) {
  model.value = {
    ...model.value,
    tasks: model.value.tasks.map((t) =>
      t.id === p.taskId ? { ...t, startUtc: p.startUtc, endUtc: p.endUtc, resourceId: p.resourceId ?? t.resourceId } : t,
    ),
  }
}
</script>

# GanttChart 工单甘特

工单 → 工序 WBS 视角的时间线:工序条、依赖链、里程碑、进度。给跟单与管理看进度和瓶颈。来自 `@nerv-iip/scheduling`(引擎无关的复合组件,非 shadcn 原版)。

拖动工序条可改期(可编辑时);时间轴由 DHTMLX 引擎渲染,引擎在部署时提供(开发用试用版,交付时向客户手动分发正式版),无本地引擎包时画布显示占位。

## 基础用法

传入 `toModel` 归一化后的 `ScheduleModel`。

<Demo block>
  <div style="height: 440px; width: 100%">
    <GanttChart :model="model" @task-drag-end="onDrag" />
  </div>
</Demo>

```vue
<script setup lang="ts">
import { GanttChart, toModel } from '@nerv-iip/scheduling'
import type { SchedulePlanContract } from '@nerv-iip/api-client'
import { ref } from 'vue'

const model = ref(toModel(plan as SchedulePlanContract)) // plan 来自 APS facade

function onDrag(p) {
  // 改期后回写模型;接后端时改为「锁定 → 重预览」(见 SchedulingWorkbench)
  model.value = { ...model.value, tasks: model.value.tasks.map((t) =>
    t.id === p.taskId ? { ...t, startUtc: p.startUtc, endUtc: p.endUtc } : t) }
}
</script>

<template>
  <GanttChart :model="model" @task-select="onSelect" @task-drag-end="onDrag" />
</template>
```

## 数据模型

组件只消费引擎无关的 `ScheduleModel`,用 `toModel(plan)` 从 APS `SchedulePlanContract` 映射;换引擎只换适配器,组件层与业务层零改动。映射只采用契约真实字段,当前 APS 未提供的卡片字段(负责人、优先级、齐套率等)留空、**不伪造**,待后端补齐后自动填充。

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `model` | 排程数据模型(`toModel` 输出) | `ScheduleModel` | — |
| `scale` | 时间刻度 | `'auto' \| 'hour' \| 'day' \| 'week' \| 'month'` | `'auto'` |
| `readOnly` | 只读(禁用拖拽编辑) | `boolean` | `false` |
| `loading` | 加载态(骨架占位) | `boolean` | `false` |
| `engineKind` | 渲染引擎选择 | `'auto' \| 'dhtmlx'` | `'auto'` |

**Emits**:`taskSelect(taskId)`、`taskDragEnd(payload)`(落点归一化,不泄露引擎结构)、`conflictClick(taskId)`。
**Expose**:`command(cmd)` — 下发 `zoomIn`/`zoomOut`/`scaleTo`/`scrollToToday`/`fitToScreen`/`selectTask` 等命令。

## 相关

- [ResourceSchedulerBoard 资源排产板](./resource-scheduler) — 同一模型的资源泳道视角。
- [SchedulingWorkbench 排产工作台](./scheduling-workbench) — 组合两视图 + 工具栏 + 面板 + 锁定—重预览编辑闭环。
