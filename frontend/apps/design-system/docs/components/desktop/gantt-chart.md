---
title: GanttChart 工单甘特
pageClass: ds-wide
---

<script setup>
import { GanttChart } from '@nerv-iip/scheduling'
import SchedulingLegend from '../../../../../packages/scheduling/src/components/panels/SchedulingLegend.vue'
import { Button } from '@nerv-iip/ui'
import { computed, ref } from 'vue'
import { makeModel, makeCalendarModel } from '../../.vitepress/schedulingDemo'

const model = ref(makeModel())

// 图例↔实图 对照:共用同一张甘特实例(避免多开重实例),旁边列表逐项标注视觉语言。
const galleryModel = ref(makeModel())
const ganttLegend = [
  { swatch: 'cat', key: 'weld', label: '工序分色', desc: '按车间/工序着色(如焊接=红、下料=绿),一眼分辨工序归属。' },
  { swatch: 'planned', label: '计划基线', desc: '半透明浅条 = 计划时段;实心条 = 实际排程,错位即偏差(WO-2026-003 晚 3h)。' },
  { swatch: 'dep', label: '依赖箭头', desc: '前序完成→后序开始(FS)的连线,勾勒工艺路线。' },
  { swatch: 'milestone', label: '里程碑菱形', desc: '菱形节点(冲焊下线)无时长;条尾小菱形+标签为阶段点(冲焊完成)。' },
  { swatch: 'conflict', label: '冲突描边', desc: '红色粗描边 = 产能/交期冲突,点选查看原因。' },
  { swatch: 'locked', label: '锁定虚线', desc: '虚线框 = 已锁定,重预览时不被算法挪动。' },
  { swatch: 'offwork', label: '非工作底纹', desc: '浅底纹 = 每日 20:00–次日 08:00 及周末,引擎自动着色。' },
  { swatch: 'now', label: '现在线', desc: '竖线 = 当前时刻,横切进度与延误。' },
]

const calendarModel = ref(makeCalendarModel())

// 拖拽落点更新模型(否则引擎按原模型重绘,条会弹回原位)。
function onDrag(p) {
  model.value = {
    ...model.value,
    tasks: model.value.tasks.map((t) =>
      t.id === p.taskId ? { ...t, startUtc: p.startUtc, endUtc: p.endUtc, resourceId: p.resourceId ?? t.resourceId } : t,
    ),
  }
}

// ① 刻度切换
const scale = ref('day')
const scales = [
  ['auto', '自动'],
  ['hour', '时'],
  ['day', '日'],
  ['week', '周'],
  ['month', '月'],
]

// 图例分色:取模型里出现的工序色。
const cats = computed(() => {
  const seen = new Map()
  for (const t of model.value.tasks) {
    if (t.colorKey && !seen.has(t.colorKey)) seen.set(t.colorKey, { key: t.colorKey, label: t.text || t.colorKey })
  }
  return [...seen.values()]
})

// ③ 选中 → 详情(TaskDetailPanel 未公开导出,这里用页面内简易 inspector)。
const selectedId = ref(null)
const selected = computed(() => model.value.tasks.find((t) => t.id === selectedId.value) ?? null)
const fmt = (iso) => (iso ? new Date(iso).toLocaleString('zh-CN', { month: 'numeric', day: 'numeric', hour: '2-digit', minute: '2-digit' }) : '—')

// 只读 / 加载 / 空态各自持一份独立模型,互不影响。
const readOnlyModel = ref(makeModel())
const emptyModel = ref({ ...makeModel(), tasks: [], links: [] })
</script>

# GanttChart 工单甘特

工单 → 工序 WBS 视角的时间线:工序条、依赖链、里程碑、进度。给跟单与管理看进度和瓶颈。来自 `@nerv-iip/scheduling`(引擎无关的复合组件,非 shadcn 原版)。

拖动工序条可改期(可编辑时);时间轴由 DHTMLX 引擎渲染,引擎在部署时提供(开发用试用版,交付时向客户手动分发正式版),无本地引擎包时画布显示占位。

## 基础用法

传入 `toModel` 归一化后的 `ScheduleModel`。样例数据已带**计划 vs 实际基线**(WO-2026-003 实际较计划晚 3h,条后半透明「计划」条即偏差)、**里程碑**(WO-2026-001 分组下「冲焊下线」菱形,焊接条尾贴「冲焊完成」阶段点)与**依赖链**。

<Demo block>
  <div style="height: 440px; width: 100%">
    <GanttChart :model="model" :scale="scale" @task-drag-end="onDrag" @task-select="selectedId = $event" />
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

## 用例演示

### 刻度切换

通过 `:scale` 控制时间刻度(`auto` / 时 / 日 / 周 / 月),或用 `Expose` 的 `command('scaleTo', …)` 下发。

<Demo block>
  <div style="display:flex; gap:.5rem; flex-wrap:wrap; margin-bottom:.75rem">
    <Button
      v-for="[val, label] in scales"
      :key="val"
      :variant="scale === val ? 'default' : 'outline'"
      size="sm"
      @click="scale = val"
    >{{ label }}</Button>
  </div>
  <div style="height: 420px; width: 100%">
    <GanttChart :model="model" :scale="scale" @task-drag-end="onDrag" @task-select="selectedId = $event" />
  </div>
</Demo>

### 图例 ↔ 实际效果 对照

同一张甘特(即上例数据),对着右侧清单在图里逐一指认视觉语言——不是只看图例条,而是「图例 ↔ 图上真身」并排讲清。`SchedulingLegend`(`view="order"`)嵌在图底作为速查条。

<Demo block>
  <div style="height: 380px; width: 100%">
    <GanttChart :model="galleryModel" scale="hour" :read-only="true" />
  </div>
  <div style="border:1px solid var(--border); border-top:0; border-radius:0 0 8px 8px; overflow:hidden; margin-top:-1px">
    <SchedulingLegend view="order" :categories="cats" />
  </div>
  <div style="display:grid; grid-template-columns:repeat(auto-fill, minmax(280px, 1fr)); gap:.5rem; margin-top:1rem">
    <div v-for="item in ganttLegend" :key="item.label" style="display:flex; gap:.625rem; align-items:flex-start; border:1px solid var(--border); border-radius:8px; padding:.625rem .75rem">
      <span style="flex:none; display:inline-flex; align-items:center; justify-content:center; width:28px; height:18px; margin-top:.1rem">
        <span v-if="item.swatch === 'cat'" style="width:24px; height:10px; border-radius:3px" :style="{ background: `var(--nerv-cat-${item.key})` }"></span>
        <span v-else-if="item.swatch === 'planned'" style="width:24px; height:10px; border-radius:3px; border:1px dashed color-mix(in srgb, var(--muted-foreground) 50%, transparent); background:color-mix(in srgb, var(--muted-foreground) 15%, transparent)"></span>
        <svg v-else-if="item.swatch === 'dep'" width="26" height="12" viewBox="0 0 26 12" style="color:var(--muted-foreground)"><path d="M1 6h18" fill="none" stroke="currentColor" stroke-width="1.4"/><path d="M16 3l4 3-4 3" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linejoin="round"/></svg>
        <span v-else-if="item.swatch === 'milestone'" style="width:11px; height:11px; transform:rotate(45deg); border-radius:2px; background:var(--brand)"></span>
        <span v-else-if="item.swatch === 'conflict'" style="width:24px; height:10px; border-radius:3px; border:2px solid var(--destructive); background:color-mix(in srgb, var(--destructive) 22%, transparent)"></span>
        <span v-else-if="item.swatch === 'locked'" style="width:24px; height:10px; border-radius:3px; border:1px dashed color-mix(in srgb, var(--brand) 70%, transparent)"></span>
        <span v-else-if="item.swatch === 'offwork'" style="width:24px; height:12px; border-radius:3px; background:var(--muted)"></span>
        <span v-else-if="item.swatch === 'now'" style="width:2px; height:16px; border-radius:99px; background:var(--brand)"></span>
      </span>
      <div style="font-size:.8125rem; line-height:1.35">
        <div style="font-weight:600; margin-bottom:.15rem">{{ item.label }}</div>
        <div style="color:var(--muted-foreground)">{{ item.desc }}</div>
      </div>
    </div>
  </div>
</Demo>

### 选中 → 详情

监听 `@task-select` 拿到 `taskId`,在模型里查回工序绘制详情。（`TaskDetailPanel` 内部组件尚未公开导出,这里用页面内简易 inspector;正式页面用 `SchedulingWorkbench` 自带的详情面板。）在上方甘特里点任意工序条即可选中。

<Demo block>
  <div v-if="selected" style="border:1px solid var(--border); border-radius:8px; padding:.875rem 1rem; font-size:.8125rem">
    <div style="font-weight:600; margin-bottom:.5rem">{{ selected.text }} · {{ selected.orderId }}</div>
    <div style="display:grid; grid-template-columns:auto 1fr; gap:.25rem .75rem; color:var(--muted-foreground)">
      <span>产品</span><span>{{ selected.product ?? '—' }} × {{ selected.quantity ?? '—' }}</span>
      <span>实际</span><span>{{ fmt(selected.startUtc) }} → {{ fmt(selected.endUtc) }}</span>
      <span v-if="selected.plannedStartUtc">计划</span><span v-if="selected.plannedStartUtc">{{ fmt(selected.plannedStartUtc) }} → {{ fmt(selected.plannedEndUtc) }}</span>
      <span>齐套 / 载荷</span><span>{{ selected.kitting != null ? Math.round(selected.kitting * 100) + '%' : '—' }} / {{ selected.load != null ? Math.round(selected.load * 100) + '%' : '—' }}</span>
      <span v-if="selected.changeoverMin">换型</span><span v-if="selected.changeoverMin">{{ selected.changeoverMin }} 分钟</span>
      <span v-if="selected.milestoneLabel">阶段点</span><span v-if="selected.milestoneLabel">{{ selected.milestoneLabel }}</span>
    </div>
  </div>
  <div v-else style="color:var(--muted-foreground); font-size:.8125rem; padding:.5rem 0">在上方甘特里点选一条工序查看详情。</div>
</Demo>

### 只读态

`:read-only="true"` 禁用拖拽编辑,仅供查看(跟单/管理层视角)。

<Demo block>
  <div style="height: 400px; width: 100%">
    <GanttChart :model="readOnlyModel" :read-only="true" />
  </div>
</Demo>

### 加载态

`:loading="true"` 显示骨架占位,用于数据请求中。

<Demo block>
  <div style="height: 220px; width: 100%">
    <GanttChart :model="emptyModel" :loading="true" />
  </div>
</Demo>

### 空态

`tasks` 为空时组件优雅呈现空画布,不报错。

<Demo block>
  <div style="height: 220px; width: 100%">
    <GanttChart :model="emptyModel" />
  </div>
</Demo>

### 工作日历 / 班次

引擎按日历自动着底纹:**每日 20:00–次日 08:00** 与**周末**染成非工作/夜班浅底纹,**「现在」竖线**标当前时刻。下例 horizon 跨周五 → 周一(含夜间),浅色带即不可排产时段——赶工工序(周五夜班、周六加班)会明显压在底纹上。

<Demo block>
  <div style="height: 300px; width: 100%">
    <GanttChart :model="calendarModel" scale="hour" :read-only="true" />
  </div>
</Demo>

## 数据模型

组件只消费引擎无关的 `ScheduleModel`,用 `toModel(plan)` 从 APS `SchedulePlanContract` 映射;换引擎只换适配器,组件层与业务层零改动。`toModel` 只采用契约真实字段(时间/资源/依赖/冲突/未排),卡片增强字段当前 APS 未提供,demo 里后置补样例值以演示能力、**生产不伪造**。

### 字段能力表

| 字段 | 能力 | 后端(APS 契约)现状 |
|---|---|---|
| `startUtc` / `endUtc` | 实际排程条 | 已提供 |
| `plannedStartUtc` / `plannedEndUtc` | 计划 vs 实际双层基线 | 未提供 → demo 补,生产留空 |
| `isMilestone` / `milestoneLabel` | 独立里程碑菱形 / 条尾阶段点 | 未提供 → demo 补,生产留空 |
| `blockKind` | 资源时间块(维护/停机/换线/换型 斜纹块) | 未提供 → demo 补,生产留空 |
| `kitting` | 齐套率 chip(足/缺/危 分级) | 未提供 → demo 补,生产留空 |
| `changeoverMin` | 换型耗时 chip | 未提供 → demo 补,生产留空 |
| `load` | 资源占用率(>1 过载瓶颈) | 未提供 → demo 补,生产留空 |
| `isRush` | 插单高亮 ⚡ | 未提供 → demo 补,生产留空 |
| `owner` / `priority` / `status` | 网格列:负责人/优先级/状态 | 未提供 → demo 补,生产留空 |
| `dueUtc` / `product` / `quantity` | 交期 / 产品 / 数量 | 未提供 → demo 补,生产留空 |
| `hasConflict` / `conflictReason` | 冲突框 + 原因 | 由 `conflicts` 派生 |

> 关键路径当前模型无对应字段、引擎不渲染,故图例与卡片均不展示;待后端补 APS 关键路径标记后再启用。

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
