---
layout: page
title: NvMobileDropdownMenu 下拉筛选
---

<script setup>
import { NvMobileDropdownMenu, NvMobileDropdownMenuItem, NvCell } from '@nerv-iip/ui-mobile'
import { ref } from 'vue'

const status = ref('')
const center = ref('')
const sort = ref('newest')

const statusOptions = [
  { label: '全部状态', value: '' },
  { label: '待开工', value: 'pending' },
  { label: '生产中', value: 'running' },
  { label: '已完工', value: 'done' },
  { label: '已暂停', value: 'paused' },
]
const centerOptions = [
  { label: '全部工作中心', value: '' },
  { label: 'CNC 加工', value: 'cnc' },
  { label: '焊接', value: 'weld' },
  { label: '装配', value: 'assembly' },
  { label: '质检', value: 'qc' },
]
const sortOptions = [
  { label: '最新创建', value: 'newest' },
  { label: '交期最近', value: 'due' },
  { label: '优先级高', value: 'priority' },
]

const rows = [
  { code: 'WO-2406-0421', product: '齿轮箱端盖', center: 'CNC 02' },
  { code: 'WO-2406-0426', product: '液压阀体 V3', center: '装配 01' },
  { code: 'WO-2406-0433', product: '主轴箱体', center: '焊接 03' },
]
</script>

<MobileDoc>

<template #phone>

  <section>
    <p class="nv-mdoc-label">筛选栏</p>
    <div class="overflow-hidden rounded-xl border border-border">
      <NvMobileDropdownMenu>
        <NvMobileDropdownMenuItem v-model="status" title="状态" :options="statusOptions" />
        <NvMobileDropdownMenuItem v-model="center" title="工作中心" :options="centerOptions" />
        <NvMobileDropdownMenuItem v-model="sort" title="排序" :options="sortOptions" />
      </NvMobileDropdownMenu>
      <NvCell
        v-for="r in rows"
        :key="r.code"
        :title="r.product"
        :note="r.code"
        :value="r.center"
      />
    </div>
  </section>
</template>

# NvMobileDropdownMenu 下拉筛选

横向筛选栏（Arco NvMobileDropdownMenu 形态）。多个触发标签带下拉箭头，点击其一在栏下方展开选项面板；选中后回填触发标签文案并触发事件。同一时刻只允许一个面板展开，点击遮罩或再次点击触发器关闭。面板向下滑入，遵循 `prefers-reduced-motion`。

## 基础用法

`NvMobileDropdownMenu` 作为容器协调「同时只开一个」，每个 `NvMobileDropdownMenuItem` 用 `v-model` 绑定当前选中值，`title` 为未选时的回退标签。

```vue
<script setup>
import { NvMobileDropdownMenu, NvMobileDropdownMenuItem } from '@nerv-iip/ui-mobile'
import { ref } from 'vue'

const status = ref('')
const statusOptions = [
  { label: '全部状态', value: '' },
  { label: '生产中', value: 'running' },
  { label: '已完工', value: 'done' },
]
</script>

<template>
  <NvMobileDropdownMenu>
    <NvMobileDropdownMenuItem v-model="status" title="状态" :options="statusOptions" />
  </NvMobileDropdownMenu>
</template>
```

## NvMobileDropdownMenuItem 属性

| 属性      | 说明               | 类型               | 默认 |
| --------- | ------------------ | ------------------ | ---- |
| `title`   | 未选中时的回退标签 | `string`           | —    |
| `options` | 选项列表           | `DropdownOption[]` | —    |
| `v-model` | 当前选中值         | `string \| number` | —    |

`DropdownOption`：`{ label: string; value: string \| number }`

## 事件

| 事件     | 说明           | 回调参数                    |
| -------- | -------------- | --------------------------- |
| `change` | 选中某项时触发 | `(value: string \| number)` |

</MobileDoc>
