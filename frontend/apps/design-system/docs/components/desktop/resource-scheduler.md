---
title: ResourceSchedulerBoard 资源排产板
pageClass: ds-wide
aside: false
---

<script setup>
import { ResourceSchedulerBoard } from '@nerv-iip/scheduling'
import { ref } from 'vue'
import { makeModel } from '../../.vitepress/schedulingDemo'

const model = ref(makeModel())

// 拖拽落点更新模型:改时间(横向)与改派资源/泳道(纵向)。
// 本 demo 里 resourceId === workCenterId === 泳道 id,故同步更新维度归属让卡片换道。
function onDrag(p) {
  model.value = {
    ...model.value,
    tasks: model.value.tasks.map((t) => {
      if (t.id !== p.taskId) return t
      const rid = p.resourceId ?? t.resourceId
      return {
        ...t,
        startUtc: p.startUtc,
        endUtc: p.endUtc,
        resourceId: rid,
        workCenterId: rid ?? t.workCenterId,
        dimensions: rid ? { ...t.dimensions, workCenter: { id: rid, label: rid } } : t.dimensions,
      }
    }),
  }
}
</script>

# ResourceSchedulerBoard 资源排产板

一资源一泳道,左轴维度可切换(工作中心 / 设备 / 班组 / 产线)。给计划员看机台负载、换型与过载。来自 `@nerv-iip/scheduling`,与 `GanttChart` 同模型、同引擎。

拖动工单卡片可改时间(横向)或改派到另一资源泳道(纵向);时间轴由 DHTMLX 引擎渲染,无本地引擎包时画布显示占位。

## 基础用法

模型带 `groupDimensions` 时,左上角出现维度切换。

<Demo block>
  <div style="height: 460px; width: 100%">
    <ResourceSchedulerBoard :model="model" @task-drag-end="onDrag" />
  </div>
</Demo>

```vue
<script setup lang="ts">
import { ResourceSchedulerBoard, toModel } from '@nerv-iip/scheduling'
import { ref } from 'vue'

const model = ref(toModel(plan)) // 含 groupDimensions 时左轴维度可切换

function onDrag(p) {
  // 改时间 / 改派;接后端时改为「锁定 → 重预览」(见 SchedulingWorkbench)
  model.value = { ...model.value, tasks: model.value.tasks.map((t) =>
    t.id === p.taskId ? { ...t, startUtc: p.startUtc, endUtc: p.endUtc, resourceId: p.resourceId ?? t.resourceId } : t) }
}
</script>

<template>
  <ResourceSchedulerBoard :model="model" @task-drag-end="onDrag" />
</template>
```

## 泳道与维度

左轴按所选维度铺泳道(工作中心 / 设备 / 班组 / 产线),泳道头显示资源名与产能指标(利用率 / OEE)。工单卡片显示工序色、产品、数量、齐套 chip 与进度;资源负载带随利用率加深,>1 过载显著提示。

## 属性

与 `GanttChart` 一致(差异仅在视角:排产板按资源泳道)。

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `model` | 排程数据模型(`toModel` 输出) | `ScheduleModel` | — |
| `scale` | 时间刻度 | `'auto' \| 'hour' \| 'day' \| 'week' \| 'month'` | `'auto'` |
| `readOnly` | 只读(禁用拖拽) | `boolean` | `false` |
| `loading` | 加载态 | `boolean` | `false` |
| `engineKind` | 渲染引擎选择 | `'auto' \| 'dhtmlx'` | `'auto'` |

**Emits**:`taskSelect(taskId)`、`taskDragEnd(payload)`(`kind: 'move' \| 'reassign'`)、`conflictClick(taskId)`。
**Expose**:`command(cmd)`。

## 相关

- [GanttChart 工单甘特](./gantt-chart) — 同一模型的工单/工序时间线视角。
- [SchedulingWorkbench 排产工作台](./scheduling-workbench) — 组合两视图 + 工具栏 + 面板 + 锁定—重预览编辑闭环。
