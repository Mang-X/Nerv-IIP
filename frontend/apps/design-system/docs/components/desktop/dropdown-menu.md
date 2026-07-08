---
title: NvDropdownMenu 动作菜单
---

<script setup>
import {
  NvDropdownMenu,
  NvDropdownMenuTrigger,
  NvDropdownMenuContent,
  NvDropdownMenuLabel,
  NvDropdownMenuItem,
  NvDropdownMenuSeparator,
  NvButton,
} from '@nerv-iip/ui'
import {
  MoreHorizontalIcon,
  EyeIcon,
  PencilIcon,
  CopyIcon,
  Trash2Icon,
} from 'lucide-vue-next'
</script>

# NvDropdownMenu 动作菜单

为表格行、卡片等收纳「更多操作」。`NvDropdownMenu` 提供模糊浮层与缩放入场，由 Label / Item / Separator 组合操作项，破坏性操作以 `variant="destructive"` 标红。

## 基础用法

`NvDropdownMenuTrigger` 配合 `as-child` 包裹一个图标按钮触发，`NvDropdownMenuContent` 通过 `align` 对齐到触发器一侧。

<Demo>
  <NvDropdownMenu>
    <NvDropdownMenuTrigger as-child>
      <NvButton variant="ghost" size="icon" aria-label="更多操作">
        <MoreHorizontalIcon aria-hidden="true" />
      </NvButton>
    </NvDropdownMenuTrigger>
    <NvDropdownMenuContent align="end" class="w-44">
      <NvDropdownMenuLabel>操作</NvDropdownMenuLabel>
      <NvDropdownMenuItem>
        <EyeIcon aria-hidden="true" />
        查看详情
      </NvDropdownMenuItem>
      <NvDropdownMenuItem>
        <PencilIcon aria-hidden="true" />
        编辑
      </NvDropdownMenuItem>
      <NvDropdownMenuItem>
        <CopyIcon aria-hidden="true" />
        复制单号
      </NvDropdownMenuItem>
      <NvDropdownMenuSeparator />
      <NvDropdownMenuItem variant="destructive">
        <Trash2Icon aria-hidden="true" />
        删除
      </NvDropdownMenuItem>
    </NvDropdownMenuContent>
  </NvDropdownMenu>
</Demo>

```vue
<script setup>
import {
  NvDropdownMenu,
  NvDropdownMenuTrigger,
  NvDropdownMenuContent,
  NvDropdownMenuLabel,
  NvDropdownMenuItem,
  NvDropdownMenuSeparator,
  NvButton,
} from '@nerv-iip/ui'
import { MoreHorizontalIcon, EyeIcon, PencilIcon, CopyIcon, Trash2Icon } from 'lucide-vue-next'
</script>

<template>
  <NvDropdownMenu>
    <NvDropdownMenuTrigger as-child>
      <NvButton variant="ghost" size="icon" aria-label="更多操作">
        <MoreHorizontalIcon aria-hidden="true" />
      </NvButton>
    </NvDropdownMenuTrigger>
    <NvDropdownMenuContent align="end" class="w-44">
      <NvDropdownMenuLabel>操作</NvDropdownMenuLabel>
      <NvDropdownMenuItem> <EyeIcon aria-hidden="true" />查看详情 </NvDropdownMenuItem>
      <NvDropdownMenuItem> <PencilIcon aria-hidden="true" />编辑 </NvDropdownMenuItem>
      <NvDropdownMenuItem> <CopyIcon aria-hidden="true" />复制单号 </NvDropdownMenuItem>
      <NvDropdownMenuSeparator />
      <NvDropdownMenuItem variant="destructive">
        <Trash2Icon aria-hidden="true" />删除
      </NvDropdownMenuItem>
    </NvDropdownMenuContent>
  </NvDropdownMenu>
</template>
```

## 组成

| 组件                      | 说明                                                   |
| ------------------------- | ------------------------------------------------------ |
| `NvDropdownMenu`          | 根容器（纯逻辑），支持 `v-model:open`                  |
| `NvDropdownMenuTrigger`   | 触发器，配合 `as-child` 包裹自定义按钮                 |
| `NvDropdownMenuContent`   | 浮层面板（模糊遮罩 + 缩放入场），支持 `align` / `side` |
| `NvDropdownMenuLabel`     | 分区标题（弱化文字），支持 `inset`                     |
| `NvDropdownMenuItem`      | 操作项，支持 `inset` / `variant` / `as-child`          |
| `NvDropdownMenuSeparator` | 分隔线，分组操作项                                     |
| `NvDropdownMenuGroup`     | 语义分组容器（无样式）                                 |

## 属性

| 属性       | 所属                                           | 说明                                        | 类型                                     | 默认        |
| ---------- | ---------------------------------------------- | ------------------------------------------- | ---------------------------------------- | ----------- |
| `open`     | `NvDropdownMenu`                               | 受控开关状态（`v-model:open`）              | `boolean`                                | —           |
| `as-child` | `NvDropdownMenuTrigger` / `NvDropdownMenuItem` | 将渲染合并到子元素                          | `boolean`                                | `false`     |
| `align`    | `NvDropdownMenuContent`                        | 沿触发器的对齐方式                          | `'start' \| 'center' \| 'end'`           | `'start'`   |
| `side`     | `NvDropdownMenuContent`                        | 浮层弹出方向                                | `'top' \| 'right' \| 'bottom' \| 'left'` | `'bottom'`  |
| `inset`    | `NvDropdownMenuItem` / `NvDropdownMenuLabel`   | 左侧留白对齐（让出图标位）                  | `boolean`                                | `false`     |
| `variant`  | `NvDropdownMenuItem`                           | 操作项语义，破坏性操作用 `destructive` 标红 | `'default' \| 'destructive'`             | `'default'` |
| `disabled` | `NvDropdownMenuItem`                           | 禁用该操作项                                | `boolean`                                | `false`     |
