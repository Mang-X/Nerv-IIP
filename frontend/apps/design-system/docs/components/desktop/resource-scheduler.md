---
title: ResourceSchedulerBoard 资源排产板
pageClass: ds-wide
aside: false
---

<script setup>
import { ResourceSchedulerBoard } from '@nerv-iip/scheduling'
import SchedulingLegend from '../../../../../packages/scheduling/src/components/panels/SchedulingLegend.vue'
import { computed, ref } from 'vue'
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

// 图例分色:取模型里出现的工序色。
const cats = computed(() => {
  const seen = new Map()
  for (const t of model.value.tasks) {
    if (t.colorKey && !t.blockKind && !seen.has(t.colorKey)) seen.set(t.colorKey, { key: t.colorKey, label: t.text || t.colorKey })
  }
  return [...seen.values()]
})

// 选中 → 详情(含资源时间块的专有信息)。
const selectedId = ref(null)
const selected = computed(() => model.value.tasks.find((t) => t.id === selectedId.value) ?? null)
const BLOCK = { maintenance: '设备维护', downtime: '计划停机', lineChange: '换线窗口', changeover: '换型窗口' }
const fmt = (iso) => (iso ? new Date(iso).toLocaleString('zh-CN', { month: 'numeric', day: 'numeric', hour: '2-digit', minute: '2-digit' }) : '—')

const readOnlyModel = ref(makeModel())
const emptyModel = ref({ ...makeModel(), tasks: [], links: [], resources: [] })
</script>

# ResourceSchedulerBoard 资源排产板

一资源一泳道,左轴维度可切换(工作中心 / 设备 / 班组 / 产线)。给计划员看机台负载、换型与过载。来自 `@nerv-iip/scheduling`,与 `GanttChart` 同模型、同引擎。

拖动工单卡片可改时间(横向)或改派到另一资源泳道(纵向);时间轴由 DHTMLX 引擎渲染,无本地引擎包时画布显示占位。

## 基础用法

模型带 `groupDimensions` 时,左上角出现维度切换。样例数据在**折弯-02**泳道有一段「定期保养」维护块、在**加工中心-03**有一段「产品换型」块(斜纹、不可拖拽);焊接-01 利用率 1.25 呈**过载瓶颈**。

<Demo block>
  <div style="height: 460px; width: 100%">
    <ResourceSchedulerBoard :model="model" @task-drag-end="onDrag" @task-select="selectedId = $event" />
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

## 用例演示

### 维度切换

组件左上角自带维度切换(工作中心 / 设备 / 班组 / 产线),由模型 `groupDimensions` 驱动;切换后泳道按所选维度重铺、卡片落到对应资源行。上方基础用法 demo 即可直接点选切换。

### 图例:排产板视觉语言

`SchedulingLegend`(`view="resource"`)讲清优先级 / 插单 / 齐套分级 / 换型 / 瓶颈 / 冲突 / 锁定,以及四类资源时间块斜纹配色。

<Demo block>
  <div style="border:1px solid var(--border); border-radius:8px; overflow:hidden">
    <SchedulingLegend view="resource" :categories="cats" />
  </div>
</Demo>

### 选中资源时间块 → 详情

点选卡片或斜纹时间块拿 `taskId`,查回详情。资源时间块(`blockKind`)有专属类型与说明。在上方排产板点选「定期保养」或「产品换型」斜纹块试试。

<Demo block>
  <div v-if="selected" style="border:1px solid var(--border); border-radius:8px; padding:.875rem 1rem; font-size:.8125rem">
    <div v-if="selected.blockKind" style="font-weight:600; margin-bottom:.5rem">资源时间块 · {{ BLOCK[selected.blockKind] }}</div>
    <div v-else style="font-weight:600; margin-bottom:.5rem">{{ selected.text }} · {{ selected.orderId }}</div>
    <div style="display:grid; grid-template-columns:auto 1fr; gap:.25rem .75rem; color:var(--muted-foreground)">
      <span>资源</span><span>{{ selected.resourceId ?? '—' }}</span>
      <span>时间</span><span>{{ fmt(selected.startUtc) }} → {{ fmt(selected.endUtc) }}</span>
      <template v-if="!selected.blockKind">
        <span>齐套 / 载荷</span><span>{{ selected.kitting != null ? Math.round(selected.kitting * 100) + '%' : '—' }} / {{ selected.load != null ? Math.round(selected.load * 100) + '%' : '—' }}</span>
        <span v-if="selected.changeoverMin">换型</span><span v-if="selected.changeoverMin">{{ selected.changeoverMin }} 分钟</span>
      </template>
      <span v-else>类型</span><span v-if="selected.blockKind">非工单占用,不可拖拽</span>
    </div>
  </div>
  <div v-else style="color:var(--muted-foreground); font-size:.8125rem; padding:.5rem 0">在上方排产板点选卡片或斜纹时间块查看详情。</div>
</Demo>

### 齐套 / 过载分级

样例数据已制造分级差异:WO-2026-001 下料 **齐套 100%(绿/足)**、WO-2026-003 装配 **齐套 60%(红/危)且载荷 125% 过载瓶颈**、WO-2026-002 机加工 **换型 45 分钟**。卡片齐套 chip 按阈值变色,泳道负载带随利用率加深,>1 显著提示。对照上方图例即可在板上一一找到。

### 只读 / 加载 / 空态

<Demo block>
  <div style="height: 400px; width: 100%">
    <ResourceSchedulerBoard :model="readOnlyModel" :read-only="true" />
  </div>
</Demo>

<Demo block>
  <div style="height: 200px; width: 100%">
    <ResourceSchedulerBoard :model="emptyModel" :loading="true" />
  </div>
</Demo>

<Demo block>
  <div style="height: 200px; width: 100%">
    <ResourceSchedulerBoard :model="emptyModel" />
  </div>
</Demo>

## 泳道与维度

左轴按所选维度铺泳道(工作中心 / 设备 / 班组 / 产线),泳道头显示资源名与产能指标(利用率 / OEE)。工单卡片显示工序色、产品、数量、齐套 chip 与进度;资源负载带随利用率加深,>1 过载显著提示。资源时间块(维护/停机/换线/换型)以斜纹块落在对应泳道,不可拖拽。

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
