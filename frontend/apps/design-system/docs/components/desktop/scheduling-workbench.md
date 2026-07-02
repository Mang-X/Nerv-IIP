---
title: SchedulingWorkbench 排产工作台
pageClass: ds-wide
aside: false
---

<script setup>
import { SchedulingWorkbench } from '@nerv-iip/scheduling'
import { toast } from '@nerv-iip/ui'
import { ref } from 'vue'
import { makeModel } from '../../.vitepress/schedulingDemo'

const model = ref(makeModel())

const H = 3_600_000
const shift = (iso, hours) => new Date(Date.parse(iso) + hours * H).toISOString()

// mock preview:携锁定项调用「后端」重排。演示语义 —— 保留锁定 + 已排里程碑/时间块,
// 其余工序整体后移 2h,并回填 changes[](变更 tab 可见)。基于当前 model 计算,返回新 ScheduleModel。
async function preview(locked) {
  const lockedIds = new Set(locked.map((l) => l.assignmentId))
  const cur = model.value
  const changes = []
  const tasks = cur.tasks.map((t) => {
    // order 分组父节点、里程碑、资源时间块、以及锁定项都保留不动。
    if (t.type !== 'operation' || t.isMilestone || t.blockKind) return t
    if (lockedIds.has(t.id)) {
      changes.push({ orderId: t.orderId, operationId: t.operationId, changeType: 'preserved', message: '锁定,保持不动', taskId: t.id })
      return t
    }
    changes.push({ orderId: t.orderId, operationId: t.operationId, changeType: 'moved', message: '重排后移 2 小时', taskId: t.id })
    return { ...t, startUtc: shift(t.startUtc, 2), endUtc: shift(t.endUtc, 2) }
  })
  await new Promise((r) => setTimeout(r, 350)) // 模拟后端往返
  return { ...cur, tasks, changes, meta: { ...cur.meta, status: 'preview' } }
}

// mock release:提交当前计划。
async function release() {
  await new Promise((r) => setTimeout(r, 250))
  toast.success('计划已发布', { description: '排程已下发到执行' })
}

// @fix:未排产项点「去处理」。真实页面跳转补料/改派;这里给提示。
function onFix(orderId, operationId) {
  toast.info(`去处理未排项:${orderId} / ${operationId}`, { description: '跳转补料 / 改派(示例)' })
}
</script>

# SchedulingWorkbench 排产工作台

把 [工单甘特](./gantt-chart) 与 [资源排产板](./resource-scheduler) 组合成完整工作台:工具栏(刻度 / 缩放 / 今天 / 撤销重做)+ 视图切换 + 右侧详情与冲突/未排/变更面板 + 「锁定 → 重新排程 → 发布」编辑闭环。适合直接搭一个排产页。

## 基础用法

`preview` / `release` 由业务层注入(对接后端);未注入时工具栏隐藏对应动作。工作台内部维护编辑快照栈,拖拽/锁定即时生效、可撤销重做。

本 demo 注入了 **mock** `preview` / `release` / `@fix`,可完整走通编辑闭环:

- **拖** 一条工序改期(横向)或改派资源(跨泳道);
- 在右侧详情面板 **锁定** 要保留的工序(再点可 **解锁**);
- 工具栏 **重新排程** → 携锁定项调用 mock preview(锁定项不动,其余整体后移 2h)→ 右侧 **变更** tab 显示 moved / preserved diff;
- **发布计划** → 触发 mock release(toast 提示);
- 右侧 **未排** tab 里点 **去处理** → 触发 `@fix`(toast 提示);
- 工具栏 **撤销 / 重做** 回退前端快照;可切 **只读** 关闭编辑。

<Demo block>
  <div style="height: 560px; width: 100%">
    <SchedulingWorkbench :model="model" default-view="order" :preview="preview" :release="release" @fix="onFix" />
  </div>
</Demo>

```vue
<script setup lang="ts">
import { SchedulingWorkbench, toModel } from '@nerv-iip/scheduling'

const model = toModel(plan)
async function preview(locked) { return toModel(await previewSchedulingPlan(locked)) }
async function release(planId: string) { await releaseSchedulingPlan(planId) }
</script>

<template>
  <SchedulingWorkbench
    :model="model"
    default-view="order"
    :preview="preview"
    :release="release"
    @fix="goFix"
  />
</template>
```

## 编辑闭环

「锁定—重预览」贴合后端确定性有限产能重排:

1. 拖动工序改时间(横向)或改派资源(跨泳道);
2. 在详情面板显式**锁定**要保留的工序;
3. 工具栏「重新排程」→ 携锁定项调用后端 `preview` 重算 → 变更摘要 / 冲突刷新;
4. 「发布计划」提交 `release`。撤销/重做为前端快照栈。

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `model` | 排程数据模型(`toModel` 输出) | `ScheduleModel` | — |
| `loading` | 加载态 | `boolean` | `false` |
| `readOnly` | 只读 | `boolean` | `false` |
| `defaultView` | 初始视图 | `'order' \| 'resource'` | `'order'` |
| `engineKind` | 渲染引擎选择 | `'auto' \| 'dhtmlx'` | `'auto'` |
| `preview` | 携锁定分配重预览;缺省则隐藏「重新排程」 | `(locked) => Promise<ScheduleModel>` | — |
| `release` | 发布当前计划;缺省则隐藏「发布」 | `(planId) => Promise<void>` | — |

**Emits**:`fix(orderId, operationId)` — 未排产项点「去处理」时抛出,供页面跳转补料/改派。

## 引擎

引擎无关设计:组件层只依赖 `SchedulingEngine` 接口,不依赖具体实现。本包对接 DHTMLX Gantt 专业版;正式自研引擎在后续 PR 落地,届时只增适配器、组件层不改。DHTMLX 评估许可禁止分发,库文件不入库——无本地 vendor 时组件优雅占位。

## 相关

- [GanttChart 工单甘特](./gantt-chart) · [ResourceSchedulerBoard 资源排产板](./resource-scheduler) — 可单独使用的两个视图组件。
