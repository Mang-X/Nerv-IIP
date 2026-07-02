---
title: ResourceSchedulerBoard 资源排产板
pageClass: ds-wide
---

<script setup>
import { ResourceSchedulerBoard } from '@nerv-iip/scheduling'
import { ButtonPro, FieldPro, FieldProGroup, FieldProLabel, InputPro, SelectPro, SelectProContent, SelectProItem, SelectProTrigger, SelectProValue } from '@nerv-iip/ui'
import SchedulingLegend from '../../../../../packages/scheduling/src/components/panels/SchedulingLegend.vue'
import { computed, ref } from 'vue'
import { makeModel, makeCalendarModel, makeBlockDemoModel } from '../../.vitepress/schedulingDemo'

const model = ref(makeModel())

// 图例↔实图 对照:共用同一张排产板实例,旁边逐项标注视觉语言。
const galleryModel = ref(makeModel())
const resLegend = [
  { swatch: 'cat', key: 'weld', label: '工序色卡', desc: '卡片主色按车间/工序着色,一眼分辨机台在做什么工序。' },
  { swatch: 'priority', label: '优先级', desc: '高优先级卡片标「高」角标(WO-2026-003)。' },
  { swatch: 'rush', label: '插单', desc: '紧急加入的工单标记(WO-2026-002),提醒计划员优先保障。' },
  { swatch: 'kit', label: '齐套 足/缺/危', desc: '齐套 chip 按阈值变色:绿=足、黄=缺、红=危(WO-2026-003 装配 60% 危)。' },
  { swatch: 'changeover', label: '换型 chip', desc: '灰底「换型」chip 标换型耗时(WO-2026-002 机加工 45 分钟)。' },
  { swatch: 'bottleneck', label: '瓶颈过载', desc: '泳道负载带随利用率加深,>1 显红「瓶颈」(焊接-01 利用率 125%)。' },
  { swatch: 'conflict', label: '冲突描边', desc: '红色粗描边 = 产能/交期冲突,点选查看原因。' },
  { swatch: 'locked', label: '锁定虚线', desc: '虚线框 = 已锁定,重预览时不被算法挪动。' },
  { swatch: 'block-maintenance', label: '维护块', desc: '斜纹块(灰)= 设备维护,不可拖拽(折弯-02「定期保养」)。' },
  { swatch: 'block-downtime', label: '停机块', desc: '斜纹块(红)= 计划停机,资源不可用。' },
  { swatch: 'block-linechange', label: '换线块', desc: '斜纹块(蓝)= 换线窗口,产线切换准备占用资源。' },
  { swatch: 'block-changeover', label: '换型块', desc: '斜纹块(橙)= 换型窗口,工装/模具换型占用资源(加工中心-03「产品换型」)。' },
  { swatch: 'offwork', label: '非工作·夜班底纹', desc: '浅底纹 = 20:00–08:00 及周末;小时刻度下还分早/中/夜三班。' },
  { swatch: 'now', label: '现在线', desc: '竖线 = 当前时刻。' },
]

const calendarModel = ref(makeCalendarModel())

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

// 资源时间块(底纹)专用:紧凑模型,4 泳道各一类块,一屏看全四类斜纹。
const blockModel = ref(makeBlockDemoModel())
const blockLegend = [
  { swatch: 'block-maintenance', label: '维护块(灰)', desc: '设备维护(折弯-02「定期保养」),不可拖拽。' },
  { swatch: 'block-downtime', label: '停机块(红)', desc: '计划停机(焊接-01「计划停机」),资源不可用。' },
  { swatch: 'block-linechange', label: '换线块(蓝)', desc: '换线窗口(激光切割-01「换线窗口」),产线切换准备占用资源。' },
  { swatch: 'block-changeover', label: '换型块(橙)', desc: '换型窗口(加工中心-03「产品换型」),工装/模具换型占用资源。' },
]

// 编辑工序 demo:独立 model + 页面内简易编辑面板。@task-select 选中工序后可改开始时间 / 资源泳道 / 锁定,
// 回写 model 触发排产板实时更新。本 demo 里 resourceId === workCenterId === 泳道 id。
const H = 3_600_000
const editModel = ref(makeModel())
const editId = ref(null)
// 只允许选中真实工序(排除工单分组父节点、里程碑、资源时间块)。
const editTask = computed(() => {
  const t = editModel.value.tasks.find((x) => x.id === editId.value)
  return t && t.type === 'operation' && !t.isMilestone && !t.blockKind ? t : null
})
const RES_OPTIONS = [
  ['激光切割-01', '激光切割-01 · 钣金线'],
  ['折弯-02', '折弯-02 · 钣金线'],
  ['焊接-01', '焊接-01 · 焊装线'],
  ['加工中心-03', '加工中心-03 · 机加线'],
]
function onEditSelect(id) {
  const t = editModel.value.tasks.find((x) => x.id === id)
  editId.value = t && t.type === 'operation' && !t.isMilestone && !t.blockKind ? id : null
}
// <input type="datetime-local"> 用本地无时区串;与 UTC ISO 互转(仅演示,按浏览器本地时区)。
const toLocalInput = (iso) => {
  const d = new Date(iso)
  const p = (n) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}`
}
const editStart = computed({
  get: () => (editTask.value ? toLocalInput(editTask.value.startUtc) : ''),
  set: (v) => {
    if (!editTask.value || !v) return
    const next = new Date(v).toISOString()
    patch(editTask.value.id, (t) => {
      const dur = Date.parse(t.endUtc) - Date.parse(t.startUtc) // 保持原时长,顺移 endUtc
      return { ...t, startUtc: next, endUtc: new Date(Date.parse(next) + dur).toISOString() }
    })
  },
})
const editResource = computed({
  get: () => editTask.value?.resourceId ?? '',
  set: (rid) => {
    if (!editTask.value || !rid) return
    patch(editTask.value.id, (t) => ({
      ...t,
      resourceId: rid,
      workCenterId: rid,
      dimensions: { ...t.dimensions, workCenter: { id: rid, label: rid } },
    }))
  },
})
function toggleLock() {
  if (!editTask.value) return
  patch(editTask.value.id, (t) => ({ ...t, locked: !t.locked }))
}
// 统一回写:替换整棵 tasks(不可变更新)触发引擎重绘。
function patch(id, fn) {
  editModel.value = { ...editModel.value, tasks: editModel.value.tasks.map((t) => (t.id === id ? fn(t) : t)) }
}
const fmtDur = (t) => Math.round((Date.parse(t.endUtc) - Date.parse(t.startUtc)) / H * 10) / 10
</script>

# ResourceSchedulerBoard 资源排产板

一资源一泳道,左轴维度可切换(工作中心 / 设备 / 班组 / 产线)。给计划员看机台负载、换型与过载。来自 `@nerv-iip/scheduling`,与 `GanttChart` 同模型、同引擎。

拖动工单卡片可改时间(横向)或改派到另一资源泳道(纵向);时间轴由 DHTMLX 引擎渲染,无本地引擎包时画布显示占位。

## 基础用法

模型带 `groupDimensions` 时,左上角出现维度切换。样例数据在**折弯-02**泳道有一段「定期保养」维护块、在**加工中心-03**有一段「产品换型」块(斜纹、不可拖拽);焊接-01 利用率 1.25 呈**过载瓶颈**。

<Demo block>
  <div style="height: 500px; width: 100%">
    <ResourceSchedulerBoard :model="model" @task-drag-end="onDrag" @task-select="selectedId = $event" />
  </div>
</Demo>

```vue
<script setup lang="ts">
import { ResourceSchedulerBoard, toModel } from '@nerv-iip/scheduling'
import { ref } from 'vue'

const model = ref(toModel(plan)) // 含 groupDimensions 时左轴维度可切换

function onDrag(p) {
  // 改时间 / 改派;接后端时改为「锁定 → 重预览」的编辑闭环
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

### 图例 ↔ 实际效果 对照

同一张排产板(即上例数据),对着右侧清单在板上逐一指认卡片与泳道的视觉语言——「图例 ↔ 图上真身」并排讲清。`SchedulingLegend`(`view="resource"`)嵌在图底作为速查条。

<Demo block>
  <div style="height: 460px; width: 100%">
    <ResourceSchedulerBoard :model="galleryModel" :read-only="true" />
  </div>
  <div style="border:1px solid var(--border); border-top:0; border-radius:0 0 8px 8px; overflow:hidden; margin-top:-1px">
    <SchedulingLegend view="resource" :categories="cats" />
  </div>
  <div style="display:grid; grid-template-columns:repeat(auto-fill, minmax(280px, 1fr)); gap:.5rem; margin-top:1rem">
    <div v-for="item in resLegend" :key="item.label" style="display:flex; gap:.625rem; align-items:flex-start; border:1px solid var(--border); border-radius:8px; padding:.625rem .75rem">
      <span style="flex:none; display:inline-flex; align-items:center; justify-content:center; width:28px; height:18px; margin-top:.1rem">
        <span v-if="item.swatch === 'cat'" style="width:24px; height:10px; border-radius:3px" :style="{ background: `var(--nerv-cat-${item.key})` }"></span>
        <span v-else-if="item.swatch === 'priority'" style="border-radius:4px; padding:1px 4px; font-size:.55rem; font-weight:700; color:var(--destructive); background:color-mix(in srgb, var(--destructive) 15%, transparent)">高</span>
        <svg v-else-if="item.swatch === 'rush'" viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2.1" stroke-linecap="round" stroke-linejoin="round" style="color:var(--sched-rush)"><path d="M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z"/></svg>
        <span v-else-if="item.swatch === 'kit'" style="display:inline-flex; gap:2px">
          <span style="width:7px; height:7px; border-radius:99px; background:var(--sched-kit-ok)"></span>
          <span style="width:7px; height:7px; border-radius:99px; background:var(--sched-kit-warn)"></span>
          <span style="width:7px; height:7px; border-radius:99px; background:var(--sched-kit-bad)"></span>
        </span>
        <span v-else-if="item.swatch === 'changeover'" style="border-radius:3px; padding:1px 5px; font-size:.55rem; font-weight:600; background:color-mix(in srgb, var(--foreground) 10%, transparent)">换型</span>
        <span v-else-if="item.swatch === 'bottleneck'" style="border-radius:3px; padding:1px 5px; font-size:.55rem; font-weight:700; color:var(--destructive); background:color-mix(in srgb, var(--destructive) 15%, transparent)">瓶颈</span>
        <span v-else-if="item.swatch === 'conflict'" style="width:24px; height:10px; border-radius:3px; border:2px solid var(--destructive); background:color-mix(in srgb, var(--destructive) 15%, transparent)"></span>
        <span v-else-if="item.swatch === 'locked'" style="width:24px; height:10px; border-radius:3px; border:1px dashed color-mix(in srgb, var(--muted-foreground) 70%, transparent)"></span>
        <span v-else-if="item.swatch === 'block-maintenance'" class="ds-hatch" style="--h: var(--sched-block-maintenance)"></span>
        <span v-else-if="item.swatch === 'block-downtime'" class="ds-hatch" style="--h: var(--sched-block-downtime)"></span>
        <span v-else-if="item.swatch === 'block-linechange'" class="ds-hatch" style="--h: var(--sched-block-linechange)"></span>
        <span v-else-if="item.swatch === 'block-changeover'" class="ds-hatch" style="--h: var(--sched-block-changeover)"></span>
        <span v-else-if="item.swatch === 'offwork'" style="width:24px; height:12px; border-radius:3px; background:color-mix(in srgb, var(--foreground) 5%, transparent)"></span>
        <span v-else-if="item.swatch === 'now'" style="width:2px; height:16px; border-radius:99px; background:var(--brand)"></span>
      </span>
      <div style="font-size:.8125rem; line-height:1.35">
        <div style="font-weight:600; margin-bottom:.15rem">{{ item.label }}</div>
        <div style="color:var(--muted-foreground)">{{ item.desc }}</div>
      </div>
    </div>
  </div>
</Demo>

<style scoped>
.ds-hatch {
  width: 24px;
  height: 12px;
  border-radius: 3px;
  background-color: color-mix(in srgb, var(--h) 12%, transparent);
  background-image: repeating-linear-gradient(-45deg, transparent 0, transparent 2px, color-mix(in srgb, var(--h) 50%, transparent) 2px, color-mix(in srgb, var(--h) 50%, transparent) 3px);
  border: 1px solid color-mix(in srgb, var(--h) 45%, transparent);
}
</style>

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

### 资源时间块(底纹)

四类资源时间块以**斜纹底纹**落在对应泳道、不可拖拽:**维护**(灰)/ **停机**(红)/ **换线**(蓝)/ **换型**(橙)。下例用一份紧凑模型——4 条工序分落 4 个资源泳道,每条泳道在同一个 6 小时窗内各放一类块,`scale="hour"` 初始视口一屏即可看全四类底纹,对照下方图例逐一指认。

<Demo block>
  <div style="height: 400px; width: 100%">
    <ResourceSchedulerBoard :model="blockModel" scale="hour" :read-only="true" />
  </div>
  <div style="display:grid; grid-template-columns:repeat(auto-fill, minmax(280px, 1fr)); gap:.5rem; margin-top:1rem">
    <div v-for="item in blockLegend" :key="item.label" style="display:flex; gap:.625rem; align-items:flex-start; border:1px solid var(--border); border-radius:8px; padding:.625rem .75rem">
      <span style="flex:none; display:inline-flex; align-items:center; justify-content:center; width:28px; height:18px; margin-top:.1rem">
        <span v-if="item.swatch === 'block-maintenance'" class="ds-hatch" style="--h: var(--sched-block-maintenance)"></span>
        <span v-else-if="item.swatch === 'block-downtime'" class="ds-hatch" style="--h: var(--sched-block-downtime)"></span>
        <span v-else-if="item.swatch === 'block-linechange'" class="ds-hatch" style="--h: var(--sched-block-linechange)"></span>
        <span v-else-if="item.swatch === 'block-changeover'" class="ds-hatch" style="--h: var(--sched-block-changeover)"></span>
      </span>
      <div style="font-size:.8125rem; line-height:1.35">
        <div style="font-weight:600; margin-bottom:.15rem">{{ item.label }}</div>
        <div style="color:var(--muted-foreground)">{{ item.desc }}</div>
      </div>
    </div>
  </div>
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

### 工作日历 / 班次

引擎按日历自动渲染:**非工作时段与周末**染浅底纹(每日 20:00–次日 08:00、周六/周日),**小时刻度**下顶部插入三班制刻度(**夜班 00–08 / 早班 08–16 / 中班 16–24**,夜班与非工作淡化),**「现在」竖线**标当前时刻。下例 horizon 跨周五 → 周一(含夜间):浅色带即不可排产/夜班时段,周六整列为周末底纹,赶工卡片压在底纹上一目了然。

<Demo block>
  <div style="height: 320px; width: 100%">
    <ResourceSchedulerBoard :model="calendarModel" scale="hour" :read-only="true" />
  </div>
</Demo>

## 泳道与维度

左轴按所选维度铺泳道(工作中心 / 设备 / 班组 / 产线),泳道头显示资源名与产能指标(利用率 / OEE)。工单卡片显示工序色、产品、数量、齐套 chip 与进度;资源负载带随利用率加深,>1 过载显著提示。资源时间块(维护/停机/换线/换型)以斜纹块落在对应泳道,不可拖拽。

## 业务操作 · 编辑工序

排产板本身负责「拖拽改期 / 跨泳道改派」;若要在板外**编辑工序信息**,监听 `@task-select` 拿到工序、在页面里放一个简易编辑面板,回写 `model` 即让板子实时更新。下例点选任一工序卡片后可:改**开始时间**(按原时长顺移结束)、改**资源泳道**(换道)、**锁定/解锁**。资源时间块(斜纹)与工单分组节点不可编辑,点选无效。

编辑面板放在画布容器**之外**,不占画布高度;回写用不可变更新 `model.value = { ...model.value, tasks: model.value.tasks.map(...) }` 触发引擎重绘。

<Demo block>
  <div style="height: 460px; width: 100%">
    <ResourceSchedulerBoard :model="editModel" @task-select="onEditSelect" @task-drag-end="patch($event.taskId, (t) => ({ ...t, startUtc: $event.startUtc, endUtc: $event.endUtc, resourceId: $event.resourceId ?? t.resourceId, workCenterId: $event.resourceId ?? t.workCenterId, dimensions: $event.resourceId ? { ...t.dimensions, workCenter: { id: $event.resourceId, label: $event.resourceId } } : t.dimensions }))" />
  </div>
  <div style="border:1px solid var(--border); border-radius:8px; padding:1rem; margin-top:1rem">
    <div v-if="editTask">
      <div style="display:flex; align-items:center; justify-content:space-between; gap:.75rem; margin-bottom:.875rem">
        <div style="font-weight:600; font-size:.875rem">编辑工序 · {{ editTask.text }} · {{ editTask.orderId }}</div>
        <ButtonPro :variant="editTask.locked ? 'brand' : 'outline'" size="sm" @click="toggleLock">{{ editTask.locked ? '已锁定 · 点击解锁' : '锁定' }}</ButtonPro>
      </div>
      <FieldProGroup>
        <FieldPro>
          <FieldProLabel for="edit-start">开始时间</FieldProLabel>
          <InputPro id="edit-start" type="datetime-local" v-model="editStart" />
        </FieldPro>
        <FieldPro>
          <FieldProLabel for="edit-res">资源 / 泳道</FieldProLabel>
          <SelectPro v-model="editResource">
            <SelectProTrigger id="edit-res"><SelectProValue placeholder="选择资源泳道" /></SelectProTrigger>
            <SelectProContent>
              <SelectProItem v-for="[val, label] in RES_OPTIONS" :key="val" :value="val">{{ label }}</SelectProItem>
            </SelectProContent>
          </SelectPro>
        </FieldPro>
      </FieldProGroup>
      <div style="color:var(--muted-foreground); font-size:.75rem; margin-top:.75rem">
        时长 {{ fmtDur(editTask) }}h · 改开始时间按原时长顺移结束 · 改资源即跨泳道换道 · 锁定后重排不被算法挪动。
      </div>
    </div>
    <div v-else style="color:var(--muted-foreground); font-size:.8125rem">在上方排产板点选一条工序卡片,这里出现可编辑字段。</div>
  </div>
</Demo>

```vue
<script setup lang="ts">
import { ResourceSchedulerBoard } from '@nerv-iip/scheduling'
import { computed, ref } from 'vue'

const model = ref(toModel(plan))
const editId = ref<string | null>(null)
const editTask = computed(() => model.value.tasks.find((t) =>
  t.id === editId.value && t.type === 'operation' && !t.isMilestone && !t.blockKind) ?? null)

function patch(id: string, fn: (t) => typeof t) {
  model.value = { ...model.value, tasks: model.value.tasks.map((t) => (t.id === id ? fn(t) : t)) }
}
function onSelect(id: string) {
  const t = model.value.tasks.find((x) => x.id === id)
  editId.value = t?.type === 'operation' && !t.blockKind ? id : null
}
// 改开始时间:保持原时长,顺移 endUtc
function setStart(iso: string) {
  patch(editTask.value.id, (t) => {
    const dur = Date.parse(t.endUtc) - Date.parse(t.startUtc)
    return { ...t, startUtc: iso, endUtc: new Date(Date.parse(iso) + dur).toISOString() }
  })
}
// 改资源:同步 resourceId / workCenterId / dimensions.workCenter,让卡片换道
function setResource(rid: string) {
  patch(editTask.value.id, (t) => ({
    ...t, resourceId: rid, workCenterId: rid,
    dimensions: { ...t.dimensions, workCenter: { id: rid, label: rid } },
  }))
}
</script>

<template>
  <ResourceSchedulerBoard :model="model" @task-select="onSelect" />
  <!-- 板外编辑面板:setStart / setResource / 切 locked 回写 model -->
</template>
```

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
- [业务操作 · 编辑工序](#业务操作-编辑工序) — 独立排产板 + 板外编辑面板,改开始时间 / 资源泳道 / 锁定并实时回写。
