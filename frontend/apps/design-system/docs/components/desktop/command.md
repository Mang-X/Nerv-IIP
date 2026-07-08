---
title: NvCommand 命令面板
---

<script setup>
import { NvCommand, NvButton, messagePro } from '@nerv-iip/ui'
import {
  ActivityIcon,
  GaugeIcon,
  LayersIcon,
  PlusIcon,
  RocketIcon,
  SearchIcon,
} from 'lucide-vue-next'
import { ref } from 'vue'

const cmdOpen = ref(false)
const cmdGroups = [
  {
    label: '导航',
    items: [
      { id: 'instances', label: '实例总览', hint: 'G I', icon: LayersIcon },
      { id: 'orders', label: '工单工作台', hint: 'G O', icon: GaugeIcon },
      { id: 'iam', label: '用户与权限', icon: ActivityIcon, keywords: 'user role permission' },
    ],
  },
  {
    label: '快捷操作',
    items: [
      { id: 'new-wo', label: '新建工单', hint: '⌘N', icon: PlusIcon, keywords: 'create work order' },
      { id: 'dispatch', label: '派发到产线', icon: RocketIcon, keywords: 'dispatch line' },
      { id: 'search-material', label: '搜索物料编码', icon: SearchIcon, keywords: 'material' },
    ],
  },
]
function onCommandSelect(item) {
  messagePro.success(`执行：${item.label}`)
}
</script>

# NvCommand 命令面板

⌘K 唤起的全局命令面板，可搜索导航与快捷操作，支持完整键盘导航（↑ ↓ 移动、↵ 执行、esc 关闭）。命令按分组传入，匹配 `label` 与 `keywords`。

## 基础用法

<Demo>
  <NvButton variant="ghost" size="sm" @click="cmdOpen = true">
    <template #leading><SearchIcon aria-hidden="true" /></template>
    命令面板 ⌘K
  </NvButton>
  <NvCommand v-model:open="cmdOpen" :groups="cmdGroups" @select="onCommandSelect" />
</Demo>

```vue
<script setup>
import { NvCommand, NvButton, messagePro } from '@nerv-iip/ui'
import { GaugeIcon, LayersIcon, PlusIcon } from 'lucide-vue-next'
import { ref } from 'vue'

const cmdOpen = ref(false)
const cmdGroups = [
  {
    label: '导航',
    items: [
      { id: 'instances', label: '实例总览', hint: 'G I', icon: LayersIcon },
      { id: 'orders', label: '工单工作台', hint: 'G O', icon: GaugeIcon },
    ],
  },
  {
    label: '快捷操作',
    items: [
      {
        id: 'new-wo',
        label: '新建工单',
        hint: '⌘N',
        icon: PlusIcon,
        keywords: 'create work order',
      },
    ],
  },
]
function onCommandSelect(item) {
  messagePro.success(`执行：${item.label}`)
}
</script>

<template>
  <NvButton @click="cmdOpen = true">命令面板 ⌘K</NvButton>
  <NvCommand v-model:open="cmdOpen" :groups="cmdGroups" @select="onCommandSelect" />
</template>
```

## 属性

| 属性          | 说明                       | 类型             | 默认                    |
| ------------- | -------------------------- | ---------------- | ----------------------- |
| `open`        | 受控开关（`v-model:open`） | `boolean`        | `false`                 |
| `groups`      | 命令分组数据               | `CommandGroup[]` | —                       |
| `placeholder` | 搜索框占位符               | `string`         | `搜索命令、工单、产线…` |
| `hotkey`      | 是否启用 ⌘K 全局快捷键     | `boolean`        | `true`                  |

## 数据结构

| 字段       | 所属           | 说明           | 类型            |
| ---------- | -------------- | -------------- | --------------- |
| `label`    | `CommandGroup` | 分组标题       | `string`        |
| `items`    | `CommandGroup` | 分组内命令项   | `CommandItem[]` |
| `id`       | `CommandItem`  | 唯一标识       | `string`        |
| `label`    | `CommandItem`  | 命令名称       | `string`        |
| `hint`     | `CommandItem`  | 右侧快捷键提示 | `string`        |
| `icon`     | `CommandItem`  | 命令图标组件   | `Component`     |
| `keywords` | `CommandItem`  | 额外搜索关键词 | `string`        |

## 事件

| 事件     | 说明           | 参数                  |
| -------- | -------------- | --------------------- |
| `select` | 选中命令时触发 | `(item: CommandItem)` |
