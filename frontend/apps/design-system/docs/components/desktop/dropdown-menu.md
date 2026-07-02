---
title: DropdownMenu 动作菜单
---

<script setup>
import {
  DropdownMenuPro,
  DropdownMenuProTrigger,
  DropdownMenuProContent,
  DropdownMenuProLabel,
  DropdownMenuProItem,
  DropdownMenuProSeparator,
  ButtonPro,
} from '@nerv-iip/ui'
import {
  MoreHorizontalIcon,
  EyeIcon,
  PencilIcon,
  CopyIcon,
  Trash2Icon,
} from 'lucide-vue-next'
</script>

# DropdownMenu 动作菜单

为表格行、卡片等收纳「更多操作」。`DropdownMenuPro` 提供模糊浮层与缩放入场，由 Label / Item / Separator 组合操作项，破坏性操作以 `variant="destructive"` 标红。

## 基础用法

`DropdownMenuProTrigger` 配合 `as-child` 包裹一个图标按钮触发，`DropdownMenuProContent` 通过 `align` 对齐到触发器一侧。

<Demo>
  <DropdownMenuPro>
    <DropdownMenuProTrigger as-child>
      <ButtonPro variant="ghost" size="icon" aria-label="更多操作">
        <MoreHorizontalIcon aria-hidden="true" />
      </ButtonPro>
    </DropdownMenuProTrigger>
    <DropdownMenuProContent align="end" class="w-44">
      <DropdownMenuProLabel>操作</DropdownMenuProLabel>
      <DropdownMenuProItem>
        <EyeIcon aria-hidden="true" />
        查看详情
      </DropdownMenuProItem>
      <DropdownMenuProItem>
        <PencilIcon aria-hidden="true" />
        编辑
      </DropdownMenuProItem>
      <DropdownMenuProItem>
        <CopyIcon aria-hidden="true" />
        复制单号
      </DropdownMenuProItem>
      <DropdownMenuProSeparator />
      <DropdownMenuProItem variant="destructive">
        <Trash2Icon aria-hidden="true" />
        删除
      </DropdownMenuProItem>
    </DropdownMenuProContent>
  </DropdownMenuPro>
</Demo>

```vue
<script setup>
import {
  DropdownMenuPro, DropdownMenuProTrigger, DropdownMenuProContent,
  DropdownMenuProLabel, DropdownMenuProItem, DropdownMenuProSeparator, ButtonPro,
} from '@nerv-iip/ui'
import { MoreHorizontalIcon, EyeIcon, PencilIcon, CopyIcon, Trash2Icon } from 'lucide-vue-next'
</script>

<template>
  <DropdownMenuPro>
    <DropdownMenuProTrigger as-child>
      <ButtonPro variant="ghost" size="icon" aria-label="更多操作">
        <MoreHorizontalIcon aria-hidden="true" />
      </ButtonPro>
    </DropdownMenuProTrigger>
    <DropdownMenuProContent align="end" class="w-44">
      <DropdownMenuProLabel>操作</DropdownMenuProLabel>
      <DropdownMenuProItem>
        <EyeIcon aria-hidden="true" />查看详情
      </DropdownMenuProItem>
      <DropdownMenuProItem>
        <PencilIcon aria-hidden="true" />编辑
      </DropdownMenuProItem>
      <DropdownMenuProItem>
        <CopyIcon aria-hidden="true" />复制单号
      </DropdownMenuProItem>
      <DropdownMenuProSeparator />
      <DropdownMenuProItem variant="destructive">
        <Trash2Icon aria-hidden="true" />删除
      </DropdownMenuProItem>
    </DropdownMenuProContent>
  </DropdownMenuPro>
</template>
```

## 组成

| 组件 | 说明 |
|---|---|
| `DropdownMenuPro` | 根容器（纯逻辑），支持 `v-model:open` |
| `DropdownMenuProTrigger` | 触发器，配合 `as-child` 包裹自定义按钮 |
| `DropdownMenuProContent` | 浮层面板（模糊遮罩 + 缩放入场），支持 `align` / `side` |
| `DropdownMenuProLabel` | 分区标题（弱化文字），支持 `inset` |
| `DropdownMenuProItem` | 操作项，支持 `inset` / `variant` / `as-child` |
| `DropdownMenuProSeparator` | 分隔线，分组操作项 |
| `DropdownMenuProGroup` | 语义分组容器（无样式） |

## 属性

| 属性 | 所属 | 说明 | 类型 | 默认 |
|---|---|---|---|---|
| `open` | `DropdownMenuPro` | 受控开关状态（`v-model:open`） | `boolean` | — |
| `as-child` | `DropdownMenuProTrigger` / `DropdownMenuProItem` | 将渲染合并到子元素 | `boolean` | `false` |
| `align` | `DropdownMenuProContent` | 沿触发器的对齐方式 | `'start' \| 'center' \| 'end'` | `'start'` |
| `side` | `DropdownMenuProContent` | 浮层弹出方向 | `'top' \| 'right' \| 'bottom' \| 'left'` | `'bottom'` |
| `inset` | `DropdownMenuProItem` / `DropdownMenuProLabel` | 左侧留白对齐（让出图标位） | `boolean` | `false` |
| `variant` | `DropdownMenuProItem` | 操作项语义，破坏性操作用 `destructive` 标红 | `'default' \| 'destructive'` | `'default'` |
| `disabled` | `DropdownMenuProItem` | 禁用该操作项 | `boolean` | `false` |
